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
        bool hasChildren = false, bool hasHostIps = false, bool fullyAllocated = false,
        string? azureResourceId = null) =>
        new()
        {
            Id = id,
            Name = name,
            NetworkAddress = network,
            Cidr = cidr,
            HasChildSubnets = hasChildren,
            HasHostIpAssignments = hasHostIps,
            IsFullyAllocated = fullyAllocated,
            AzureResourceId = azureResourceId
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

        // Each plan item is for a distinct prefix.
        Assert.Contains(plan.Items, i => i.PrefixNetworkAddress == "10.0.0.0" && i.PrefixCidr == 16);
        Assert.Contains(plan.Items, i => i.PrefixNetworkAddress == "10.1.0.0" && i.PrefixCidr == 16);

        // Both auto-created targets keep the unmodified VNet name (intentional — Bastet
        // allows duplicate Subnet.Name; only NetworkAddress+Cidr is unique). If a future
        // change introduces auto-disambiguation of these names, this assertion will catch it.
        Assert.All(plan.Items, i => Assert.Equal("vnet-multi", i.AutoCreateTargetName));
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
        // Plan attempts to import 10.2.5.0/24 from VNet 10.2.0.0/16. Bastet has
        // 10.0.0.0/8 + 10.2.5.0/24 (no /16 between them), so the planner will
        // simultaneously trip two distinct hard fails — both correct, both expected:
        //   * the Azure subnet 10.2.5.0/24 already exists in Bastet (the case under test)
        //   * the auto-created /16 target would contain the existing /24
        // We assert the duplicate-existence error is present; the would-contain
        // error is exercised independently by VNetPrefixWouldContainExistingSubnet_HardFails.
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-x", "10.2.0.0/16", Sub("default", "10.2.5.0/24")));

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

    // -------------------------------------------------------------------------
    // Azure resource ID propagation
    // -------------------------------------------------------------------------

    [Fact]
    public void AzureResourceIds_AreForwardedFromSelectionToPlan()
    {
        const string webId = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-prod/subnets/web";
        const string appId = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-prod/subnets/app";

        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-prod", "10.0.0.0/16",
                new BulkImportSelectedSubnetDto { Name = "web", AddressPrefix = "10.0.1.0/24", AzureResourceId = webId },
                new BulkImportSelectedSubnetDto { Name = "app", AddressPrefix = "10.0.2.0/24", AzureResourceId = appId }));

        List<ExistingSubnetSnapshot> existing = [Existing(1, "Existing", "10.0.0.0", 16)];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        BulkImportPlanItem item = Assert.Single(plan.Items);
        Assert.Equal($"/subscriptions/test/providers/Microsoft.Network/virtualNetworks/vnet-prod", item.VNetResourceId);
        Assert.Equal(2, item.ChildSubnets.Count);
        Assert.Contains(item.ChildSubnets, c => c.Name == "web" && c.AzureResourceId == webId);
        Assert.Contains(item.ChildSubnets, c => c.Name == "app" && c.AzureResourceId == appId);
    }

    // -------------------------------------------------------------------------
    // Availability annotation
    // -------------------------------------------------------------------------
    //
    // Drives the selection UI: anything left selectable must produce a committable
    // plan, and anything blocked must be something BuildPlan would reject. The two
    // agreeing is the whole point of computing this on the server.

    private static string AzSubnetId(string vnetName, string subnetName) =>
        $"/subscriptions/test/providers/Microsoft.Network/virtualNetworks/{vnetName}/subnets/{subnetName}";

    private static BulkAzureSubnetViewModel AzSub(string vnetName, string name, string prefix) =>
        new() { ResourceId = AzSubnetId(vnetName, name), Name = name, AddressPrefix = prefix };

    private static BulkAzureVNetViewModel AzVNet(
        string name, string[] prefixes, params BulkAzureSubnetViewModel[] subnets) =>
        new()
        {
            ResourceId = $"/subscriptions/test/providers/Microsoft.Network/virtualNetworks/{name}",
            Name = name,
            Ipv4AddressPrefixes = [.. prefixes],
            Subnets = [.. subnets]
        };

    [Fact]
    public void Availability_NewPrefixAndSubnet_AreAvailable()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.0.0/16"], AzSub("vnet-a", "web", "10.0.1.0/24"));

        _planner.AnnotateAvailability([vnet], []);

        BulkAzurePrefixViewModel prefix = Assert.Single(vnet.Prefixes);
        Assert.Equal(BulkImportAvailability.Available, prefix.Status);
        Assert.True(prefix.IsSelectable);
        Assert.True(Assert.Single(vnet.Subnets).IsSelectable);
    }

    [Fact]
    public void Availability_PrefixWithCleanExactMatch_WillUpdateExisting()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.0.0/16"]);
        List<ExistingSubnetSnapshot> existing = [Existing(1, "Existing", "10.0.0.0", 16)];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzurePrefixViewModel prefix = Assert.Single(vnet.Prefixes);
        Assert.Equal(BulkImportAvailability.WillUpdateExisting, prefix.Status);
        Assert.True(prefix.IsSelectable);
        Assert.Contains("Existing", prefix.Reason);
    }

    [Fact]
    public void Availability_PrefixTargetHasChildren_IsNotSelectable()
    {
        // Re-importing a VNet you already imported: its target now has children.
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.0.0/16"]);
        List<ExistingSubnetSnapshot> existing = [Existing(1, "Existing", "10.0.0.0", 16, hasChildren: true)];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzurePrefixViewModel prefix = Assert.Single(vnet.Prefixes);
        Assert.Equal(BulkImportAvailability.Blocked, prefix.Status);
        Assert.False(prefix.IsSelectable);
        Assert.Contains("child subnets", prefix.Reason);
    }

    [Fact]
    public void Availability_PrefixTargetHasHostIps_IsNotSelectable()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.0.0/16"]);
        List<ExistingSubnetSnapshot> existing = [Existing(1, "Existing", "10.0.0.0", 16, hasHostIps: true)];

        _planner.AnnotateAvailability([vnet], existing);

        Assert.False(Assert.Single(vnet.Prefixes).IsSelectable);
        Assert.Contains("host IP assignments", vnet.Prefixes[0].Reason);
    }

    [Fact]
    public void Availability_PrefixTargetFullyAllocated_IsNotSelectable()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.0.0/16"]);
        List<ExistingSubnetSnapshot> existing = [Existing(1, "Existing", "10.0.0.0", 16, fullyAllocated: true)];

        _planner.AnnotateAvailability([vnet], existing);

        Assert.False(Assert.Single(vnet.Prefixes).IsSelectable);
        Assert.Contains("fully allocated", vnet.Prefixes[0].Reason);
    }

    [Fact]
    public void Availability_PrefixWouldContainExisting_IsNotSelectable()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.0.0/16"]);
        List<ExistingSubnetSnapshot> existing = [Existing(1, "Child", "10.0.5.0", 24)];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzurePrefixViewModel prefix = Assert.Single(vnet.Prefixes);
        Assert.False(prefix.IsSelectable);
        Assert.Contains("Would contain existing", prefix.Reason);
    }

    [Fact]
    public void Availability_PrefixContainerHasHostIps_IsNotSelectable()
    {
        // The prefix would be auto-created as a child of the /16, which BuildPlanItem rejects
        // because a subnet with host IPs cannot have children. The badge must agree.
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.1.0/24"]);
        List<ExistingSubnetSnapshot> existing = [Existing(1, "Container", "10.0.0.0", 16, hasHostIps: true)];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzurePrefixViewModel prefix = Assert.Single(vnet.Prefixes);
        Assert.Equal(BulkImportAvailability.Blocked, prefix.Status);
        Assert.False(prefix.IsSelectable);
        Assert.Contains("host IP assignments", prefix.Reason);
    }

    [Fact]
    public void Availability_PrefixContainerFullyAllocated_IsNotSelectable()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.1.0/24"]);
        List<ExistingSubnetSnapshot> existing = [Existing(1, "Container", "10.0.0.0", 16, fullyAllocated: true)];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzurePrefixViewModel prefix = Assert.Single(vnet.Prefixes);
        Assert.Equal(BulkImportAvailability.Blocked, prefix.Status);
        Assert.False(prefix.IsSelectable);
        Assert.Contains("fully allocated", prefix.Reason);
    }

    [Fact]
    public void Availability_PrefixWithEligibleContainer_IsAvailable()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.1.0/24"]);
        List<ExistingSubnetSnapshot> existing = [Existing(1, "Container", "10.0.0.0", 16)];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzurePrefixViewModel prefix = Assert.Single(vnet.Prefixes);
        Assert.Equal(BulkImportAvailability.Available, prefix.Status);
        Assert.True(prefix.IsSelectable);
    }

    [Fact]
    public void Availability_OnlyTheDeepestContainerDecides_JustLikeBuildPlanItem()
    {
        // The parent is always the deepest container, so only its eligibility matters:
        // an ineligible /16 blocks even under a clean /8, and a clean /16 imports
        // even under an ineligible /8.
        List<ExistingSubnetSnapshot> deepIneligible =
        [
            Existing(1, "Clean root", "10.0.0.0", 8),
            Existing(2, "Busy container", "10.0.0.0", 16, hasHostIps: true)
        ];
        List<ExistingSubnetSnapshot> deepEligible =
        [
            Existing(1, "Busy root", "10.0.0.0", 8, hasHostIps: true),
            Existing(2, "Clean container", "10.0.0.0", 16)
        ];

        BulkAzureVNetViewModel blocked = AzVNet("vnet-a", ["10.0.1.0/24"]);
        _planner.AnnotateAvailability([blocked], deepIneligible);
        Assert.False(Assert.Single(blocked.Prefixes).IsSelectable);
        Assert.Contains("Busy container", blocked.Prefixes[0].Reason);

        BulkAzureVNetViewModel available = AzVNet("vnet-a", ["10.0.1.0/24"]);
        _planner.AnnotateAvailability([available], deepEligible);
        Assert.True(Assert.Single(available.Prefixes).IsSelectable);
    }

    [Fact]
    public void Availability_SubnetAlreadyImported_IsNotSelectable()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.0.0/16"], AzSub("vnet-a", "web", "10.0.1.0/24"));
        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "Target", "10.0.0.0", 16),
            Existing(2, "web", "10.0.1.0", 24, azureResourceId: AzSubnetId("vnet-a", "web"))
        ];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzureSubnetViewModel subnet = Assert.Single(vnet.Subnets);
        Assert.Equal(BulkImportAvailability.AlreadyImported, subnet.Status);
        Assert.False(subnet.IsSelectable);
        Assert.Contains("Already imported", subnet.Reason);
    }

    [Fact]
    public void Availability_SubnetAddressTakenByHandMadeSubnet_IsBlockedNotAlreadyImported()
    {
        // Bastet requires {NetworkAddress, Cidr} to be unique, so a hand-made subnet blocks the
        // import just as hard - but it isn't "already imported", and the wording should say so.
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.0.0/16"], AzSub("vnet-a", "web", "10.0.1.0/24"));
        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "Target", "10.0.0.0", 16),
            Existing(2, "Hand made", "10.0.1.0", 24)
        ];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzureSubnetViewModel subnet = Assert.Single(vnet.Subnets);
        Assert.Equal(BulkImportAvailability.Blocked, subnet.Status);
        Assert.False(subnet.IsSelectable);
        Assert.Contains("already uses", subnet.Reason);
    }

    [Fact]
    public void Availability_SubnetImportedFromADifferentAzureResource_IsBlocked()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.0.0/16"], AzSub("vnet-a", "web", "10.0.1.0/24"));
        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "Target", "10.0.0.0", 16),
            Existing(2, "web", "10.0.1.0", 24, azureResourceId: AzSubnetId("vnet-other", "web"))
        ];

        _planner.AnnotateAvailability([vnet], existing);

        Assert.Equal(BulkImportAvailability.Blocked, Assert.Single(vnet.Subnets).Status);
    }

    [Fact]
    public void Availability_EncompassingSubnet_IsSelectableEvenWhenTargetExists()
    {
        // VNet 10.11.0.0/24 whose only subnet is also 10.11.0.0/24. The subnet is never created -
        // it marks the target fully allocated - so it must not be reported as a duplicate of the
        // very target it would mark. Without this it would always look blocked once imported.
        BulkAzureVNetViewModel vnet = AzVNet("vnet-e", ["10.11.0.0/24"], AzSub("vnet-e", "default", "10.11.0.0/24"));
        List<ExistingSubnetSnapshot> existing = [Existing(1, "Target", "10.11.0.0", 24)];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzureSubnetViewModel subnet = Assert.Single(vnet.Subnets);
        Assert.Equal(BulkImportAvailability.Available, subnet.Status);
        Assert.True(subnet.IsSelectable);
        Assert.Contains("fully allocated", subnet.Reason);
    }

    [Fact]
    public void Availability_InvalidPrefix_IsNotSelectable()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.0.1/16"]); // not CIDR-aligned

        _planner.AnnotateAvailability([vnet], []);

        Assert.False(Assert.Single(vnet.Prefixes).IsSelectable);
    }

    [Fact]
    public void Availability_StatusName_IsSerializedAsAName_NotAnOrdinal()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.0.0/16"], AzSub("vnet-a", "web", "10.0.1.0/24"));

        _planner.AnnotateAvailability([vnet], []);

        Assert.Equal("Available", vnet.Prefixes[0].StatusName);
        Assert.Equal("Available", vnet.Subnets[0].StatusName);
    }

    [Fact]
    public void Availability_SelectableItems_ProduceACommittablePlan()
    {
        // The property the whole feature rests on: if the UI lets you check it, importing it works.
        // Mixes every state - a fresh VNet, a clean exact match, an already-imported subnet, a
        // blocked target with children, an encompassing subnet, and a prefix whose containing
        // subnet has host IPs.
        BulkAzureVNetViewModel fresh = AzVNet("vnet-fresh", ["10.40.0.0/16"], AzSub("vnet-fresh", "new", "10.40.1.0/24"));
        BulkAzureVNetViewModel partial = AzVNet("vnet-partial", ["10.41.0.0/16"],
            AzSub("vnet-partial", "old", "10.41.1.0/24"),
            AzSub("vnet-partial", "new", "10.41.2.0/24"));
        BulkAzureVNetViewModel blocked = AzVNet("vnet-blocked", ["10.42.0.0/16"], AzSub("vnet-blocked", "x", "10.42.1.0/24"));
        BulkAzureVNetViewModel encompass = AzVNet("vnet-enc", ["10.43.0.0/24"], AzSub("vnet-enc", "all", "10.43.0.0/24"));
        BulkAzureVNetViewModel nested = AzVNet("vnet-nested", ["10.44.1.0/24"], AzSub("vnet-nested", "y", "10.44.1.0/25"));

        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "Partial target", "10.41.0.0", 16),
            Existing(2, "old", "10.41.1.0", 24, azureResourceId: AzSubnetId("vnet-partial", "old")),
            Existing(3, "Blocked target", "10.42.0.0", 16, hasChildren: true),
            Existing(4, "Busy container", "10.44.0.0", 16, hasHostIps: true)
        ];

        List<BulkAzureVNetViewModel> vnets = [fresh, partial, blocked, encompass, nested];
        _planner.AnnotateAvailability(vnets, existing);

        // Build a selection from ONLY what the UI would leave enabled
        List<BulkImportSelectedVNetPrefixDto> selected = [];
        foreach (BulkAzureVNetViewModel vnet in vnets)
        {
            foreach (BulkAzurePrefixViewModel prefix in vnet.Prefixes.Where(p => p.IsSelectable))
            {
                selected.Add(new BulkImportSelectedVNetPrefixDto
                {
                    VNetName = vnet.Name,
                    VNetResourceId = vnet.ResourceId,
                    AddressPrefix = prefix.AddressPrefix,
                    Subnets =
                    [
                        .. vnet.Subnets
                            .Where(s => s.IsSelectable)
                            .Select(s => new BulkImportSelectedSubnetDto
                            {
                                Name = s.Name,
                                AddressPrefix = s.AddressPrefix,
                                AzureResourceId = s.ResourceId
                            })
                    ]
                });
            }
        }

        // The blocked and nested VNets must have been filtered out, and the rest must import cleanly
        Assert.DoesNotContain(selected, p => p.VNetName == "vnet-blocked");
        Assert.DoesNotContain(selected, p => p.VNetName == "vnet-nested");
        Assert.Equal(3, selected.Count);

        BulkImportPlanViewModel plan = _planner.BuildPlan(Sel(false, [.. selected]), existing);

        Assert.True(plan.CanCommit,
            "Everything the annotation left selectable should import. Global errors: "
            + string.Join(" | ", plan.GlobalErrors.Concat(plan.Items.SelectMany(i => i.Errors))));
    }
}
