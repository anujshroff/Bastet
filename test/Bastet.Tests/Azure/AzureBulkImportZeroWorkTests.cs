using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

public class AzureBulkImportZeroWorkTests
{
    private readonly AzureBulkImportPlanner _planner =
        new(new IpUtilityService(), new InputSanitizationService());

    private static string VNetId(string n) =>
        $"/subscriptions/test/providers/Microsoft.Network/virtualNetworks/{n}";

    private static string SubnetId(string v, string s) => $"{VNetId(v)}/subnets/{s}";

    private static BulkAzureSubnetViewModel Sub(string vnet, string name, string prefix) =>
        new() { ResourceId = SubnetId(vnet, name), Name = name, AddressPrefix = prefix };

    private static BulkAzureVNetViewModel VNet(string name, string[] prefixes, params BulkAzureSubnetViewModel[] subs) =>
        new()
        {
            ResourceId = VNetId(name),
            Name = name,
            Ipv4AddressPrefixes = [.. prefixes],
            Subnets = [.. subs]
        };

    private static ExistingSubnetSnapshot Row(
        int id, string name, string network, int cidr,
        string? azureResourceId = null, bool hasChildren = false, bool fullyAllocated = false) =>
        new()
        {
            Id = id,
            Name = name,
            NetworkAddress = network,
            Cidr = cidr,
            AzureResourceId = azureResourceId,
            HasChildSubnets = hasChildren,
            IsFullyAllocated = fullyAllocated
        };

    private BulkAzurePrefixViewModel Annotate(BulkAzureVNetViewModel vnet, params ExistingSubnetSnapshot[] existing)
    {
        _planner.AnnotateAvailability([vnet], existing);
        return vnet.Prefixes[0];
    }

    // Nothing left to add -> AlreadyImported, and therefore hidden by the slider.

    [Fact]
    public void AVNetWhoseEverySubnetIsAlreadyRecorded_IsAlreadyImported()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.50.0.0/16"],
            Sub("vnet-a", "web", "10.50.1.0/24"),
            Sub("vnet-a", "db", "10.50.2.0/24"));

        BulkAzurePrefixViewModel prefix = Annotate(vnet,
            Row(1, "vnet-a", "10.50.0.0", 16, VNetId("vnet-a"), hasChildren: true),
            Row(2, "web", "10.50.1.0", 24, SubnetId("vnet-a", "web")),
            Row(3, "db", "10.50.2.0", 24, SubnetId("vnet-a", "db")));

        Assert.Equal(BulkImportAvailability.AlreadyImported, prefix.Status);
        Assert.False(prefix.IsSelectable);
        Assert.All(vnet.Subnets, s => Assert.False(s.IsSelectable));
    }

    [Fact]
    public void ACollapsedFullyAllocatedTargetFromThisVNet_IsAlreadyImportedNotBlocked()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-c", ["10.51.0.0/16"],
            Sub("vnet-c", "whole", "10.51.0.0/16"));

        BulkAzurePrefixViewModel prefix = Annotate(vnet,
            Row(1, "vnet-c", "10.51.0.0", 16, VNetId("vnet-c"), fullyAllocated: true));

        Assert.Equal(BulkImportAvailability.AlreadyImported, prefix.Status);
        Assert.False(prefix.IsSelectable);
        Assert.Contains("nothing left to add", prefix.Reason);

        BulkAzureSubnetViewModel whole = Assert.Single(vnet.Subnets);
        Assert.Equal(BulkImportAvailability.AlreadyImported, whole.Status);
        Assert.False(whole.IsSelectable);
    }

    [Fact]
    public void AFullyAllocatedTargetBelongingToAnotherVNet_IsStillBlocked()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-c", ["10.52.0.0/16"]);

        BulkAzurePrefixViewModel prefix = Annotate(vnet,
            Row(1, "someone-else", "10.52.0.0", 16, fullyAllocated: true));

        Assert.Equal(BulkImportAvailability.Blocked, prefix.Status);
        Assert.False(prefix.IsSelectable);
    }

    // Something can still be added -> stays selectable.

    [Fact]
    public void AVNetWithOneUnrecordedSubnet_StaysSelectableForTheTopUp()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.53.0.0/16"],
            Sub("vnet-a", "web", "10.53.1.0/24"),
            Sub("vnet-a", "new", "10.53.9.0/24"));

        BulkAzurePrefixViewModel prefix = Annotate(vnet,
            Row(1, "vnet-a", "10.53.0.0", 16, VNetId("vnet-a"), hasChildren: true),
            Row(2, "web", "10.53.1.0", 24, SubnetId("vnet-a", "web")));

        Assert.Equal(BulkImportAvailability.WillUpdateExisting, prefix.Status);
        Assert.True(prefix.IsSelectable);
    }

    [Fact]
    public void AnUnlinkedTargetIsWork_BecauseImportingLinksIt()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.54.0.0/16"]);

        BulkAzurePrefixViewModel prefix = Annotate(vnet, Row(1, "hand-made", "10.54.0.0", 16));

        Assert.Equal(BulkImportAvailability.WillUpdateExisting, prefix.Status);
        Assert.True(prefix.IsSelectable);
    }

    // The rename flag: WouldRenameTarget is a fact the client combines with the live toggle.

    [Fact]
    public void ATargetWhoseNameMatchesTheVNet_IsNotFlaggedForRename()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.55.0.0/16"]);

        BulkAzurePrefixViewModel prefix = Annotate(vnet,
            Row(1, "vnet-a", "10.55.0.0", 16, VNetId("vnet-a")));

        Assert.False(prefix.WouldRenameTarget);
        Assert.Equal(BulkImportAvailability.AlreadyImported, prefix.Status);
    }

    [Fact]
    public void ATargetWhoseNameDiffers_IsFlaggedSoTheRenameToggleCanRevealIt()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.56.0.0/16"]);

        BulkAzurePrefixViewModel prefix = Annotate(vnet,
            Row(1, "some-other-name", "10.56.0.0", 16, VNetId("vnet-a")));

        Assert.True(prefix.WouldRenameTarget);
        Assert.Equal(BulkImportAvailability.AlreadyImported, prefix.Status);
        Assert.False(prefix.IsSelectable);
    }

    // WouldRenameTarget must agree with what BuildPlanItem will actually do, or the wizard
    // shows a row promising a rename that never happens.

    [Fact]
    public void ATargetWithChildren_IsStillFlaggedForRename()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.59.0.0/16"],
            Sub("vnet-a", "web", "10.59.1.0/24"));

        BulkAzurePrefixViewModel prefix = Annotate(vnet,
            Row(1, "some-other-name", "10.59.0.0", 16, VNetId("vnet-a"), hasChildren: true),
            Row(2, "web", "10.59.1.0", 24, SubnetId("vnet-a", "web")));

        Assert.Equal(BulkImportAvailability.AlreadyImported, prefix.Status);
        Assert.True(prefix.WouldRenameTarget);
    }

    [Fact]
    public void AChildlessTargetWithADifferentName_IsFlaggedForRename()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.60.0.0/16"]);

        BulkAzurePrefixViewModel prefix = Annotate(vnet,
            Row(1, "some-other-name", "10.60.0.0", 16, VNetId("vnet-a")));

        Assert.Equal(BulkImportAvailability.AlreadyImported, prefix.Status);
        Assert.True(prefix.WouldRenameTarget);
    }

    [Fact]
    public void TheRenameFlagAgreesWithThePlan_ForAChildlessTarget()
    {
        BulkImportSelectionDto selection = new()
        {
            RenameMatchedBastetSubnets = true,
            VNetPrefixes =
            [
                new BulkImportSelectedVNetPrefixDto
                {
                    VNetName = "vnet-a",
                    VNetResourceId = VNetId("vnet-a"),
                    AddressPrefix = "10.61.0.0/16",
                    Subnets = []
                }
            ]
        };

        List<ExistingSubnetSnapshot> existing =
            [Row(1, "some-other-name", "10.61.0.0", 16, VNetId("vnet-a"))];

        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.61.0.0/16"]);
        _planner.AnnotateAvailability([vnet], existing);

        BulkImportPlanViewModel plan = _planner.BuildPlan(selection, existing);
        BulkImportPlanItem item = Assert.Single(plan.Items);

        Assert.True(vnet.Prefixes[0].WouldRenameTarget);
        Assert.True(item.WillRename);
        Assert.Equal("vnet-a", item.NewName);
    }

    [Fact]
    public void ARenameAppliesToATargetWithChildrenToo()
    {
        BulkImportSelectionDto selection = new()
        {
            RenameMatchedBastetSubnets = true,
            VNetPrefixes =
            [
                new BulkImportSelectedVNetPrefixDto
                {
                    VNetName = "vnet-a",
                    VNetResourceId = VNetId("vnet-a"),
                    AddressPrefix = "10.62.0.0/16",
                    Subnets = []
                }
            ]
        };

        List<ExistingSubnetSnapshot> existing =
        [
            Row(1, "some-other-name", "10.62.0.0", 16, VNetId("vnet-a"), hasChildren: true),
            Row(2, "web", "10.62.1.0", 24, SubnetId("vnet-a", "web"))
        ];

        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.62.0.0/16"],
            Sub("vnet-a", "web", "10.62.1.0/24"));
        _planner.AnnotateAvailability([vnet], existing);

        BulkImportPlanItem item = Assert.Single(_planner.BuildPlan(selection, existing).Items);

        Assert.True(vnet.Prefixes[0].WouldRenameTarget);
        Assert.True(item.WillRename);
        Assert.Equal("vnet-a", item.NewName);
    }

    [Fact]
    public void AMultiPrefixVNetQualifiesTheProposedName_SoAnUnqualifiedTargetWouldRename()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-m", ["10.57.0.0/16", "10.58.0.0/16"]);

        _planner.AnnotateAvailability([vnet],
            [Row(1, "vnet-m", "10.57.0.0", 16, VNetId("vnet-m"))]);

        Assert.True(vnet.Prefixes[0].WouldRenameTarget);
    }

    [Fact]
    public void EverySubnetRecorded_SaysRecorded_WithoutTheCannotImportClause()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.20.0.0/16"], Sub("vnet-a", "web", "10.20.1.0/24"));
        BulkAzurePrefixViewModel prefix = Annotate(vnet,
            Row(1, "vnet-a", "10.20.0.0", 16, VNetId("vnet-a"), hasChildren: true),
            Row(2, "web", "10.20.1.0", 24, SubnetId("vnet-a", "web")));

        Assert.Equal(BulkImportAvailability.AlreadyImported, prefix.Status);
        Assert.Contains("already recorded, so there is nothing to add", prefix.Reason);
        Assert.DoesNotContain("cannot be imported", prefix.Reason);
    }

    [Fact]
    public void AContainedSubnetThatCannotBeImported_IsNamedInTheReason()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.20.0.0/16"],
            Sub("vnet-a", "web", "10.20.1.0/24"),
            Sub("vnet-a", "db", "10.20.9.0/26"));

        BulkAzurePrefixViewModel prefix = Annotate(vnet,
            Row(1, "vnet-a", "10.20.0.0", 16, VNetId("vnet-a"), hasChildren: true),
            Row(2, "web", "10.20.1.0", 24, SubnetId("vnet-a", "web")),
            Row(3, "hand-carved", "10.20.9.0", 27));

        Assert.Equal(BulkImportAvailability.Blocked, vnet.Subnets[1].Status);
        Assert.Equal(BulkImportAvailability.AlreadyImported, prefix.Status);
        Assert.Contains("either already recorded or cannot be imported", prefix.Reason);
    }

    [Fact]
    public void AnUnlinkedExactMatch_IsSelectable_SoTheFilterCannotHideIt()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.80.0.0/16"]);
        BulkAzurePrefixViewModel prefix = Annotate(vnet, Row(1, "legacy-core", "10.80.0.0", 16));

        Assert.Equal(BulkImportAvailability.WillUpdateExisting, prefix.Status);
        Assert.True(prefix.IsSelectable);
    }

    [Fact]
    public void AFullyRecordedPrefix_IsNotSelectable_SoTheFilterHidesIt()
    {
        BulkAzureVNetViewModel vnet = VNet("vnet-a", ["10.85.0.0/16"], Sub("vnet-a", "s1", "10.85.1.0/24"));
        BulkAzurePrefixViewModel prefix = Annotate(vnet,
            Row(1, "vnet-a", "10.85.0.0", 16, VNetId("vnet-a"), hasChildren: true),
            Row(2, "s1", "10.85.1.0", 24, SubnetId("vnet-a", "s1")));

        Assert.Equal(BulkImportAvailability.AlreadyImported, prefix.Status);
        Assert.False(prefix.IsSelectable);
    }
}
