using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

/// <summary>
/// Unit tests for the bulk Azure import planner. The planner has no DB dependency,
/// so these tests construct it directly with the real <see cref="IpUtilityService"/>
/// and <see cref="InputSanitizationService"/>.
/// </summary>
public class AzureBulkImportPlannerTests
{
    private readonly AzureBulkImportPlanner _planner;

    public AzureBulkImportPlannerTests()
    {
        IIpUtilityService ip = new IpUtilityService();
        IInputSanitizationService san = new InputSanitizationService();
        _planner = new AzureBulkImportPlanner(ip, san);
    }

    private static BulkImportSelectedSubnetDto Sub(string name, string prefix) =>
        new() { Name = name, AddressPrefix = prefix };

    private static BulkImportSelectedVNetPrefixDto Pref(
        string vnetName, string prefix, params BulkImportSelectedSubnetDto[] subs) =>
        new()
        {
            VNetName = vnetName,
            VNetResourceId = $"/subscriptions/test/providers/Microsoft.Network/virtualNetworks/{vnetName}",
            AddressPrefix = prefix,
            Subnets = [.. subs]
        };

    private static BulkImportSelectionDto Sel(
        bool rename = false,
        params BulkImportSelectedVNetPrefixDto[] prefixes) =>
        new()
        {
            SubscriptionId = "sub-1",
            SubscriptionName = "Test Sub",
            RenameMatchedBastetSubnets = rename,
            VNetPrefixes = [.. prefixes]
        };

    private static ExistingSubnetSnapshot Existing(
        int id, string name, string network, int cidr,
        bool hasChildren = false, bool hasHostIps = false, bool fullyAllocated = false) =>
        new()
        {
            Id = id,
            Name = name,
            NetworkAddress = network,
            Cidr = cidr,
            HasChildSubnets = hasChildren,
            HasHostIpAssignments = hasHostIps,
            IsFullyAllocated = fullyAllocated
        };

    // -------------------------------------------------------------------------
    // Exact-match target
    // -------------------------------------------------------------------------

    [Fact]
    public void ExactMatch_EmptyTarget_PlansChildCreations()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-prod", "10.0.0.0/16",
                Sub("web", "10.0.1.0/24"),
                Sub("app", "10.0.2.0/24")));

        List<ExistingSubnetSnapshot> existing = [Existing(1, "Existing", "10.0.0.0", 16)];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        Assert.Single(plan.Items);
        BulkImportPlanItem item = plan.Items[0];
        Assert.Equal(BulkImportTargetType.ExactMatch, item.TargetType);
        Assert.Equal(1, item.ExistingTargetSubnetId);
        Assert.False(item.WillRename);
        Assert.False(item.WillMarkFullyAllocated);
        Assert.Equal(2, item.ChildSubnets.Count);
        Assert.Contains(item.ChildSubnets, c => c.Name == "web" && c.NetworkAddress == "10.0.1.0" && c.Cidr == 24);
        Assert.Contains(item.ChildSubnets, c => c.Name == "app" && c.NetworkAddress == "10.0.2.0" && c.Cidr == 24);
    }

    [Fact]
    public void ExactMatch_TargetHasChildren_HardFails()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-prod", "10.0.0.0/16", Sub("web", "10.0.1.0/24")));

        List<ExistingSubnetSnapshot> existing = [Existing(1, "Existing", "10.0.0.0", 16, hasChildren: true)];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.False(plan.CanCommit);
        Assert.Single(plan.Items);
        Assert.Contains(plan.Items[0].Errors, e => e.Contains("already has child subnets"));
    }

    [Fact]
    public void ExactMatch_TargetHasHostIps_HardFails()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-prod", "10.0.0.0/16", Sub("web", "10.0.1.0/24")));

        List<ExistingSubnetSnapshot> existing = [Existing(1, "Existing", "10.0.0.0", 16, hasHostIps: true)];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.Items[0].Errors, e => e.Contains("host IP assignments"));
    }

    [Fact]
    public void ExactMatch_TargetIsFullyAllocated_HardFails()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-prod", "10.0.0.0/16", Sub("web", "10.0.1.0/24")));

        List<ExistingSubnetSnapshot> existing = [Existing(1, "Existing", "10.0.0.0", 16, fullyAllocated: true)];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.Items[0].Errors, e => e.Contains("fully allocated"));
    }

    [Fact]
    public void ExactMatch_RenameRequested_AndNameDiffers_PlansRename()
    {
        BulkImportSelectionDto sel = Sel(true,
            Pref("vnet-prod", "10.0.0.0/16", Sub("web", "10.0.1.0/24")));

        List<ExistingSubnetSnapshot> existing = [Existing(1, "OldName", "10.0.0.0", 16)];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        Assert.True(plan.Items[0].WillRename);
        Assert.Equal("vnet-prod", plan.Items[0].NewName);
    }

    [Fact]
    public void ExactMatch_RenameRequested_AndNamesEqual_DoesNotPlanRename()
    {
        BulkImportSelectionDto sel = Sel(true,
            Pref("vnet-prod", "10.0.0.0/16", Sub("web", "10.0.1.0/24")));

        List<ExistingSubnetSnapshot> existing = [Existing(1, "vnet-prod", "10.0.0.0", 16)];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        Assert.False(plan.Items[0].WillRename);
    }

    // -------------------------------------------------------------------------
    // Auto-create child target
    // -------------------------------------------------------------------------

    [Fact]
    public void AutoCreateChild_WhenContainerExists()
    {
        // Bastet has 10.0.0.0/8 and 10.1.0.0/16. We import VNet 10.2.0.0/16. Container is /8.
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-x", "10.2.0.0/16", Sub("default", "10.2.5.0/24")));

        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "RootSlash8", "10.0.0.0", 8),
            Existing(2, "OtherVnet", "10.1.0.0", 16)
        ];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        BulkImportPlanItem item = plan.Items[0];
        Assert.Equal(BulkImportTargetType.AutoCreateChild, item.TargetType);
        Assert.Equal(1, item.AutoCreateParentSubnetId);
        Assert.Equal("vnet-x", item.AutoCreateTargetName);
        Assert.Single(item.ChildSubnets);
    }

    [Fact]
    public void AutoCreateChild_PicksDeepestContainer()
    {
        // Existing: 10.0.0.0/8 contains 10.0.0.0/16 contains 10.0.0.0/20.
        // We import 10.0.1.0/24. Deepest container is 10.0.0.0/20.
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-deep", "10.0.1.0/24", Sub("a", "10.0.1.0/25")));

        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "S8", "10.0.0.0", 8),
            Existing(2, "S16", "10.0.0.0", 16),
            Existing(3, "S20", "10.0.0.0", 20)
        ];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        Assert.Equal(BulkImportTargetType.AutoCreateChild, plan.Items[0].TargetType);
        Assert.Equal(3, plan.Items[0].AutoCreateParentSubnetId);
    }

    [Fact]
    public void AutoCreateChild_ContainerHasHostIps_HardFails()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-x", "10.2.0.0/16", Sub("default", "10.2.5.0/24")));

        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "RootSlash8", "10.0.0.0", 8, hasHostIps: true)
        ];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.Items[0].Errors, e => e.Contains("host IP assignments"));
    }

    // -------------------------------------------------------------------------
    // Auto-create top-level target
    // -------------------------------------------------------------------------

    [Fact]
    public void AutoCreateTopLevel_WhenNoContainerAndNoExactMatch()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-iso", "192.168.0.0/16", Sub("default", "192.168.1.0/24")));

        List<ExistingSubnetSnapshot> existing = [Existing(1, "RFC1918-10", "10.0.0.0", 8)];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        BulkImportPlanItem item = plan.Items[0];
        Assert.Equal(BulkImportTargetType.AutoCreateTopLevel, item.TargetType);
        Assert.Null(item.AutoCreateParentSubnetId);
        Assert.Equal("vnet-iso", item.AutoCreateTargetName);
        Assert.Single(item.ChildSubnets);
    }

    // -------------------------------------------------------------------------
    // Multiple VNet prefixes
    // -------------------------------------------------------------------------

    [Fact]
    public void MultipleVNetPrefixes_EachIsIndependentTarget()
    {
        // One VNet with two non-overlapping IPv4 prefixes.
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-multi", "10.0.0.0/16", Sub("a", "10.0.1.0/24")),
            Pref("vnet-multi", "10.1.0.0/16", Sub("b", "10.1.1.0/24")));

        List<ExistingSubnetSnapshot> existing = [];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        Assert.Equal(2, plan.Items.Count);
        Assert.All(plan.Items, i => Assert.Equal(BulkImportTargetType.AutoCreateTopLevel, i.TargetType));
    }

    // -------------------------------------------------------------------------
    // Conflict detection — VNet vs VNet
    // -------------------------------------------------------------------------

    [Fact]
    public void VNetPrefixOverlap_HardFails()
    {
        // 10.0.0.0/16 and 10.0.0.0/24 overlap.
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-a", "10.0.0.0/16"),
            Pref("vnet-b", "10.0.0.0/24"));

        List<ExistingSubnetSnapshot> existing = [];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("overlaps"));
    }

    [Fact]
    public void IdenticalVNetPrefixesAcrossVNets_HardFails()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-a", "10.0.0.0/16"),
            Pref("vnet-b", "10.0.0.0/16"));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("overlaps"));
    }

    // -------------------------------------------------------------------------
    // Conflict detection — Azure subnets across VNets
    // -------------------------------------------------------------------------

    [Fact]
    public void AzureSubnetsAcrossVNets_DontOverlap_OK()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-a", "10.0.0.0/16", Sub("default", "10.0.1.0/24")),
            Pref("vnet-b", "10.1.0.0/16", Sub("default", "10.1.1.0/24")));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.True(plan.CanCommit);
    }

    // -------------------------------------------------------------------------
    // Conflict detection — Azure subnet already in Bastet
    // -------------------------------------------------------------------------

    [Fact]
    public void AzureSubnetAlreadyInBastet_HardFails()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-x", "10.2.0.0/16", Sub("default", "10.2.5.0/24")));

        // Existing Bastet tree contains 10.2.5.0/24 already (somewhere unrelated)
        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "RootSlash8", "10.0.0.0", 8),
            Existing(2, "Conflict", "10.2.5.0", 24)
        ];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("already exists in Bastet"));
    }

    // -------------------------------------------------------------------------
    // Conflict detection — VNet prefix would contain existing Bastet subnet
    // -------------------------------------------------------------------------

    [Fact]
    public void VNetPrefixWouldContainExistingSubnet_HardFails()
    {
        // Bastet has 10.0.5.0/24. We import 10.0.0.0/16 with no exact match. Would create invalid hierarchy.
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-broad", "10.0.0.0/16", Sub("a", "10.0.1.0/24")));

        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "Existing24", "10.0.5.0", 24)
        ];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("would contain existing"));
    }

    [Fact]
    public void VNetPrefixContainedByExisting_DoesNotTriggerWouldContainError()
    {
        // Importing 10.0.5.0/24, existing 10.0.0.0/16 contains it. This is the normal AutoCreateChild case.
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-narrow", "10.0.5.0/24", Sub("a", "10.0.5.0/25")));

        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "Existing16", "10.0.0.0", 16)
        ];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        Assert.Equal(BulkImportTargetType.AutoCreateChild, plan.Items[0].TargetType);
    }

    // -------------------------------------------------------------------------
    // Fully encompassing Azure subnet
    // -------------------------------------------------------------------------

    [Fact]
    public void AzureSubnetEqualsVNetPrefix_MarksTargetFullyAllocated_AndNoChildren()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-full", "10.0.0.0/16",
                Sub("everything", "10.0.0.0/16")));

        List<ExistingSubnetSnapshot> existing = [];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        BulkImportPlanItem item = plan.Items[0];
        Assert.True(item.WillMarkFullyAllocated);
        Assert.Equal("everything", item.FullyAllocatingAzureSubnetName);
        Assert.Empty(item.ChildSubnets);
    }

    [Fact]
    public void AzureSubnetEqualsVNetPrefix_OnExactMatchTarget_MarksFullyAllocated()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-full", "10.0.0.0/16",
                Sub("everything", "10.0.0.0/16")));

        List<ExistingSubnetSnapshot> existing = [Existing(1, "Existing", "10.0.0.0", 16)];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        BulkImportPlanItem item = plan.Items[0];
        Assert.Equal(BulkImportTargetType.ExactMatch, item.TargetType);
        Assert.True(item.WillMarkFullyAllocated);
        Assert.Empty(item.ChildSubnets);
    }

    // -------------------------------------------------------------------------
    // Naming
    // -------------------------------------------------------------------------

    [Fact]
    public void IdenticalChildNamesInDifferentTargets_AreNotDisambiguated()
    {
        // Disambiguation is only needed when names collide *within the same target*.
        // Two VNets in non-overlapping space land in different targets, so each can
        // keep the name "default" without conflict.
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-a", "10.0.0.0/16", Sub("default", "10.0.1.0/24")),
            Pref("vnet-b", "10.1.0.0/16", Sub("default", "10.1.1.0/24"))
        );

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.True(plan.CanCommit);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal("default", plan.Items[0].ChildSubnets[0].Name);
        Assert.Equal("default", plan.Items[1].ChildSubnets[0].Name);
    }


    [Fact]
    public void NameCollisionWithinSameTarget_GetsDisambiguated()
    {
        // Test specifically within one target. We can't really get duplicate names in real Azure,
        // but the planner should still defensively disambiguate.
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-x", "10.0.0.0/16",
                Sub("dup", "10.0.1.0/24"),
                Sub("dup", "10.0.2.0/24"))
        );

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.True(plan.CanCommit);
        BulkImportPlanItem item = plan.Items[0];
        Assert.Equal(2, item.ChildSubnets.Count);
        // Names are unique within the target
        Assert.NotEqual(item.ChildSubnets[0].Name, item.ChildSubnets[1].Name);
        Assert.Equal("dup", item.ChildSubnets[0].Name);
        Assert.Contains("vnet-x", item.ChildSubnets[1].Name);
    }

    [Fact]
    public void LongAzureName_IsTruncatedTo50Chars()
    {
        string longName = new('a', 200);
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-x", "10.0.0.0/16", Sub(longName, "10.0.1.0/24")));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.True(plan.CanCommit);
        Assert.True(plan.Items[0].ChildSubnets[0].Name.Length <= 50);
    }

    // -------------------------------------------------------------------------
    // Validation of inputs
    // -------------------------------------------------------------------------

    [Fact]
    public void InvalidVNetPrefix_HardFails()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("bad", "not-a-cidr"));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("invalid"));
    }

    [Fact]
    public void MisalignedVNetPrefix_HardFails()
    {
        // 10.0.0.5/16 — host bits are set
        BulkImportSelectionDto sel = Sel(false,
            Pref("misaligned", "10.0.0.5/16"));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("aligned"));
    }

    [Fact]
    public void AzureSubnetNotInVNet_HardFails()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-x", "10.0.0.0/16",
                Sub("foreign", "172.16.0.0/24")));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("not contained in VNet prefix"));
    }

    [Fact]
    public void NoSelections_HardFails()
    {
        BulkImportPlanViewModel plan = _planner.BuildPlan(Sel(false), []);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("No VNet"));
    }
}
