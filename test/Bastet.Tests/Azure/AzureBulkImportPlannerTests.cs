using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

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

    [Theory]
    [InlineData("<b></b>")]
    [InlineData("   ")]
    [InlineData("")]
    public void VNetNameThatSanitizesToEmpty_FallsBackToThePrefix(string vnetName)
    {
        BulkImportPlanViewModel plan = _planner.BuildPlan(
            Sel(false, Pref(vnetName, "192.168.0.0/16", Sub("web", "192.168.1.0/24"))), []);

        BulkImportPlanItem item = Assert.Single(plan.Items);
        Assert.False(string.IsNullOrWhiteSpace(item.AutoCreateTargetName));
        Assert.Equal("192.168.0.0_16", item.AutoCreateTargetName);
    }

    [Fact]
    public void OrdinaryVNetName_IsUsedAsTheTargetName()
    {
        BulkImportPlanViewModel plan = _planner.BuildPlan(
            Sel(false, Pref("prod-vnet", "192.168.0.0/16", Sub("web", "192.168.1.0/24"))), []);

        Assert.Equal("prod-vnet", Assert.Single(plan.Items).AutoCreateTargetName);
    }

    [Fact]
    public void NullVNetPrefixes_ReportsAnError_DoesNotThrow()
    {
        BulkImportSelectionDto selection = new()
        {
            SubscriptionId = "sub-1",
            SubscriptionName = "Test Sub",
            VNetPrefixes = null!
        };

        BulkImportPlanViewModel plan = _planner.BuildPlan(selection, []);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("No VNet address prefixes were selected."));
    }

    [Fact]
    public void NullEntryInVNetPrefixes_ReportsAnError_DoesNotThrow()
    {
        BulkImportSelectionDto selection = Sel(false, Pref("vnet-a", "10.0.0.0/16"));
        selection.VNetPrefixes.Add(null!);

        BulkImportPlanViewModel plan = _planner.BuildPlan(selection, []);

        Assert.Contains(plan.GlobalErrors, e => e.Contains("was empty"));
    }

    [Fact]
    public void NullSubnetsCollection_ReportsNothingAndDoesNotThrow()
    {
        BulkImportSelectedVNetPrefixDto prefix = Pref("vnet-a", "10.0.0.0/16");
        prefix.Subnets = null!;
        BulkImportSelectionDto selection = Sel(false, prefix);

        BulkImportPlanViewModel plan = _planner.BuildPlan(selection, []);

        Assert.Empty(plan.GlobalErrors);
        Assert.Single(plan.Items);
    }

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

    [Fact]
    public void AutoCreateChild_WhenContainerExists()
    {

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

    [Fact]
    public void MultipleVNetPrefixes_EachIsIndependentTarget()
    {

        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-multi", "10.0.0.0/16", Sub("a", "10.0.1.0/24")),
            Pref("vnet-multi", "10.1.0.0/16", Sub("b", "10.1.1.0/24")));

        List<ExistingSubnetSnapshot> existing = [];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        Assert.Equal(2, plan.Items.Count);
        Assert.All(plan.Items, i => Assert.Equal(BulkImportTargetType.AutoCreateTopLevel, i.TargetType));

        Assert.Contains(plan.Items, i => i.PrefixNetworkAddress == "10.0.0.0" && i.PrefixCidr == 16);
        Assert.Contains(plan.Items, i => i.PrefixNetworkAddress == "10.1.0.0" && i.PrefixCidr == 16);

        Assert.Contains(plan.Items, i => i.AutoCreateTargetName == "vnet-multi (10.0.0.0-16)");
        Assert.Contains(plan.Items, i => i.AutoCreateTargetName == "vnet-multi (10.1.0.0-16)");
    }

    [Fact]
    public void VNetPrefixOverlap_HardFails()
    {

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

    [Fact]
    public void AzureSubnetsAcrossVNets_DontOverlap_OK()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-a", "10.0.0.0/16", Sub("default", "10.0.1.0/24")),
            Pref("vnet-b", "10.1.0.0/16", Sub("default", "10.1.1.0/24")));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.True(plan.CanCommit);
    }

    [Fact]
    public void AzureSubnetAlreadyInBastet_HardFails()
    {

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

    [Fact]
    public void VNetPrefixWouldContainExistingSubnet_HardFails()
    {

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
    public void AzureSubnetEqualsVNetPrefix_SanitizesTheNameThatReachesTheDescription()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-full", "10.0.0.0/16",
                Sub("<script>alert(1)</script>everything", "10.0.0.0/16")));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        string? name = plan.Items[0].FullyAllocatingAzureSubnetName;
        Assert.NotNull(name);
        Assert.DoesNotContain("<", name);
        Assert.DoesNotContain(">", name);
    }

    [Fact]
    public void AzureSubnetEqualsVNetPrefix_AlongsideSiblings_IsRejectedRatherThanPartlyApplied()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-full", "10.40.0.0/16",
                Sub("everything", "10.40.0.0/16"),
                Sub("web", "10.40.1.0/24"),
                Sub("app", "10.40.2.0/24")));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.False(plan.CanCommit);
        BulkImportPlanItem item = plan.Items[0];
        Assert.NotEmpty(item.Errors);
        Assert.False(item.WillMarkFullyAllocated);
        Assert.Empty(item.ChildSubnets);
    }

    [Fact]
    public void AzureSubnetEqualsVNetPrefix_OnItsOwn_IsStillAccepted()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-full", "10.41.0.0/16",
                Sub("everything", "10.41.0.0/16")));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.True(plan.CanCommit);
        Assert.True(plan.Items[0].WillMarkFullyAllocated);
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

    [Fact]
    public void IdenticalChildNamesInDifferentTargets_AreNotDisambiguated()
    {

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

        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-x", "10.0.0.0/16",
                Sub("dup", "10.0.1.0/24"),
                Sub("dup", "10.0.2.0/24"))
        );

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.True(plan.CanCommit);
        BulkImportPlanItem item = plan.Items[0];
        Assert.Equal(2, item.ChildSubnets.Count);

        Assert.NotEqual(item.ChildSubnets[0].Name, item.ChildSubnets[1].Name);
        Assert.Equal("dup", item.ChildSubnets[0].Name);
        Assert.Contains("vnet-x", item.ChildSubnets[1].Name);
    }

    [Fact]
    public void NamesCollidingAfterTruncation_AreDisambiguatedWithoutStalling()
    {

        string shared = new('a', 100);
        string nameA = shared + "-tierA";
        string nameB = shared + "-tierB";
        Assert.Equal(nameA[..100], nameB[..100]);

        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-prod", "10.0.0.0/16",
                Sub(nameA, "10.0.1.0/24"),
                Sub(nameB, "10.0.2.0/24")));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, [Existing(1, "Existing", "10.0.0.0", 16)]);

        Assert.True(plan.CanCommit);
        BulkImportPlanItem item = plan.Items[0];
        Assert.Equal(2, item.ChildSubnets.Count);
        Assert.NotEqual(item.ChildSubnets[0].Name, item.ChildSubnets[1].Name);
        Assert.All(item.ChildSubnets, c => Assert.InRange(c.Name.Length, 1, 100));

        Assert.Contains("vnet-prod", item.ChildSubnets[1].Name);
    }

    [Fact]
    public void ManyNamesCollidingAfterTruncation_AreAllDistinct()
    {

        string prefix = new('x', 100);
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-y", "10.0.0.0/16",
                Sub(prefix + "aa", "10.0.1.0/24"),
                Sub(prefix + "bb", "10.0.2.0/24"),
                Sub(prefix + "cc", "10.0.3.0/24"),
                Sub(prefix + "dd", "10.0.4.0/24")));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, [Existing(1, "Existing", "10.0.0.0", 16)]);

        Assert.True(plan.CanCommit);
        List<BulkImportPlannedChildSubnet> children = plan.Items[0].ChildSubnets;
        Assert.Equal(4, children.Count);
        Assert.Equal(4, children.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(children, c => Assert.InRange(c.Name.Length, 1, 100));
    }

    [Fact]
    public void MaximumLengthAzureName_IsKeptIntact()
    {

        string azureMaxName = new('a', 80);
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-w", "10.0.0.0/16", Sub(azureMaxName, "10.0.1.0/24")));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.True(plan.CanCommit);
        Assert.Equal(azureMaxName, plan.Items[0].ChildSubnets[0].Name);
    }

    [Fact]
    public void LongAzureName_IsTruncatedTo100Chars()
    {
        string longName = new('a', 200);
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-x", "10.0.0.0/16", Sub(longName, "10.0.1.0/24")));

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, []);

        Assert.True(plan.CanCommit);
        Assert.True(plan.Items[0].ChildSubnets[0].Name.Length <= 100);
    }

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
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.0.0.1/16"]);

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

        Assert.DoesNotContain(selected, p => p.VNetName == "vnet-blocked");
        Assert.DoesNotContain(selected, p => p.VNetName == "vnet-nested");
        Assert.Equal(3, selected.Count);

        BulkImportPlanViewModel plan = _planner.BuildPlan(Sel(false, [.. selected]), existing);

        Assert.True(plan.CanCommit,
            "Everything the annotation left selectable should import. Global errors: "
            + string.Join(" | ", plan.GlobalErrors.Concat(plan.Items.SelectMany(i => i.Errors))));
    }

    private static string AzVNetId(string vnetName) =>
        $"/subscriptions/test/providers/Microsoft.Network/virtualNetworks/{vnetName}";

    [Fact]
    public void ExactMatch_TargetLinkedToADifferentVNet_HardFails()
    {
        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-vb", "10.98.0.0/16", Sub("web", "10.98.1.0/24")));

        List<ExistingSubnetSnapshot> existing =
            [Existing(1, "va-target", "10.98.0.0", 16, azureResourceId: AzVNetId("vnet-va"))];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.False(plan.CanCommit);
        string error = Assert.Single(Assert.Single(plan.Items).Errors);
        Assert.Contains(AzVNetId("vnet-va"), error);
        Assert.Contains(AzVNetId("vnet-vb"), error);
    }

    [Fact]
    public void ExactMatch_TargetLinkedToTheSameVNet_StillCommits()
    {

        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-va", "10.98.0.0/16", Sub("web", "10.98.1.0/24")));

        List<ExistingSubnetSnapshot> existing =
            [Existing(1, "va-target", "10.98.0.0", 16, azureResourceId: AzVNetId("vnet-va"))];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        Assert.Empty(Assert.Single(plan.Items).Errors);
    }

    [Fact]
    public void ExactMatch_TargetNotLinkedToAzure_StillCommits()
    {

        BulkImportSelectionDto sel = Sel(false,
            Pref("vnet-vb", "10.98.0.0/16", Sub("web", "10.98.1.0/24")));

        List<ExistingSubnetSnapshot> existing = [Existing(1, "hand-made", "10.98.0.0", 16)];

        BulkImportPlanViewModel plan = _planner.BuildPlan(sel, existing);

        Assert.True(plan.CanCommit);
        Assert.Empty(Assert.Single(plan.Items).Errors);
    }

    [Fact]
    public void Availability_PrefixTargetLinkedToADifferentVNet_IsNotSelectable()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-vb", ["10.98.0.0/16"]);
        List<ExistingSubnetSnapshot> existing =
            [Existing(1, "va-target", "10.98.0.0", 16, azureResourceId: AzVNetId("vnet-va"))];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzurePrefixViewModel prefix = Assert.Single(vnet.Prefixes);
        Assert.Equal(BulkImportAvailability.Blocked, prefix.Status);
        Assert.False(prefix.IsSelectable);

        Assert.Contains(AzVNetId("vnet-va"), prefix.Reason);
        Assert.Contains(AzVNetId("vnet-vb"), prefix.Reason);
    }

    [Fact]
    public void Availability_PrefixTargetLinkedToTheSameVNet_WillUpdateExisting()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-va", ["10.98.0.0/16"]);
        List<ExistingSubnetSnapshot> existing =
            [Existing(1, "va-target", "10.98.0.0", 16, azureResourceId: AzVNetId("vnet-va"))];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzurePrefixViewModel prefix = Assert.Single(vnet.Prefixes);
        Assert.Equal(BulkImportAvailability.WillUpdateExisting, prefix.Status);
        Assert.True(prefix.IsSelectable);
    }

    [Fact]
    public void APlannedChildThatWouldContainAnExistingSubnet_IsRefusedByThePlan()
    {
        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "target", "10.10.0.0", 16),
            Existing(2, "handmade-half", "10.10.2.0", 25)
        ];

        BulkImportPlanViewModel plan = _planner.BuildPlan(
            Sel(false, Pref("vnet-a", "10.10.0.0/16", Sub("multi", "10.10.2.0/24"))), existing);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("handmade-half"));
    }

    [Fact]
    public void APlannedChildWithAMoreSpecificExistingParent_IsRefusedByThePlan()
    {
        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "target", "10.20.0.0", 16),
            Existing(2, "handmade-mid", "10.20.4.0", 22)
        ];

        BulkImportPlanViewModel plan = _planner.BuildPlan(
            Sel(false, Pref("vnet-b", "10.20.0.0/16", Sub("gap", "10.20.4.0/24"))), existing);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("handmade-mid"));
    }

    [Fact]
    public void AnAncestorOutsideTheVNetPrefix_DoesNotRefuseAnImportThatCommitsToday()
    {
        List<ExistingSubnetSnapshot> existing = [Existing(1, "root8", "10.30.0.0", 8)];

        BulkImportPlanViewModel plan = _planner.BuildPlan(
            Sel(false, Pref("vnet-c", "10.30.0.0/16", Sub("web", "10.30.1.0/24"))), existing);

        Assert.Empty(plan.GlobalErrors);
        Assert.True(plan.CanCommit);
    }

    [Fact]
    public void Availability_ASubnetThatWouldContainAnExistingSubnet_IsBlockedWithAReason()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.10.0.0/16"], AzSub("vnet-a", "multi", "10.10.2.0/24"));
        List<ExistingSubnetSnapshot> existing = [Existing(2, "handmade-half", "10.10.2.0", 25)];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzureSubnetViewModel annotated = Assert.Single(vnet.Subnets);
        Assert.Equal(BulkImportAvailability.Blocked, annotated.Status);
        Assert.False(annotated.IsSelectable);
        Assert.Contains("handmade-half", annotated.Reason!);
    }

    [Fact]
    public void Availability_AnEncompassingSubnetOverAPopulatedTarget_IsBlocked()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-full", ["10.77.0.0/24"], AzSub("vnet-full", "full", "10.77.0.0/24"));
        List<ExistingSubnetSnapshot> existing =
            [Existing(1, "vnet-full-target", "10.77.0.0", 24, hasChildren: true)];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzureSubnetViewModel annotated = Assert.Single(vnet.Subnets);
        Assert.Equal(BulkImportAvailability.Blocked, annotated.Status);
        Assert.False(annotated.IsSelectable);
        Assert.Contains("already has child subnets", annotated.Reason!);
    }

    [Fact]
    public void Availability_AnEncompassingSubnetOverAnEmptyTarget_StaysAvailable()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-full", ["10.77.0.0/24"], AzSub("vnet-full", "full", "10.77.0.0/24"));
        List<ExistingSubnetSnapshot> existing =
            [Existing(1, "vnet-full-target", "10.77.0.0", 24)];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzureSubnetViewModel annotated = Assert.Single(vnet.Subnets);
        Assert.Equal(BulkImportAvailability.Available, annotated.Status);
        Assert.True(annotated.IsSelectable);
    }

    [Fact]
    public void Availability_AnEncompassingSubnetOnOnePrefix_DoesNotBlockAnotherPrefixesSubnet()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-multi",
            ["192.168.100.0/24", "10.20.0.0/16"],
            AzSub("vnet-multi", "full", "192.168.100.0/24"),
            AzSub("vnet-multi", "ordinary", "10.20.5.0/24"));

        List<ExistingSubnetSnapshot> existing =
            [Existing(1, "hundred-target", "192.168.100.0", 24, hasChildren: true)];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzureSubnetViewModel ordinary = vnet.Subnets.Single(s => s.Name == "ordinary");
        Assert.Equal(BulkImportAvailability.Available, ordinary.Status);
        Assert.True(ordinary.IsSelectable);
    }

    [Fact]
    public void Availability_SubnetWithAMoreSpecificExistingParent_IsBlocked()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.63.0.0/16"], AzSub("vnet-a", "recarved", "10.63.1.0/25"));
        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "PROD-CORE-63", "10.63.0.0", 16, azureResourceId: AzVNetId("vnet-a")),
            Existing(2, "rig-a", "10.63.1.0", 24)
        ];

        _planner.AnnotateAvailability([vnet], existing);

        BulkAzureSubnetViewModel subnet = Assert.Single(vnet.Subnets);
        Assert.Equal(BulkImportAvailability.Blocked, subnet.Status);
        Assert.False(subnet.IsSelectable);
        Assert.Contains("more specific existing Bastet parent", subnet.Reason);
        Assert.Contains("rig-a", subnet.Reason);
    }

    [Fact]
    public void Availability_AnExistingRowAboveTheVNetPrefix_DoesNotBlock()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["172.16.5.0/24"], AzSub("vnet-a", "web", "172.16.5.0/25"));
        List<ExistingSubnetSnapshot> existing = [Existing(1, "RFC1918 root", "172.16.0.0", 12)];

        _planner.AnnotateAvailability([vnet], existing);

        Assert.Equal(BulkImportAvailability.Available, Assert.Single(vnet.Subnets).Status);
    }

    [Fact]
    public void Availability_TheTargetItself_IsNotAMoreSpecificParent()
    {
        BulkAzureVNetViewModel vnet = AzVNet("vnet-a", ["10.65.0.0/16"], AzSub("vnet-a", "web", "10.65.1.0/24"));
        List<ExistingSubnetSnapshot> existing =
        [
            Existing(1, "Target", "10.65.0.0", 16, azureResourceId: AzVNetId("vnet-a"))
        ];

        _planner.AnnotateAvailability([vnet], existing);

        Assert.Equal(BulkImportAvailability.Available, Assert.Single(vnet.Subnets).Status);
    }
}
