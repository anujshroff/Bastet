using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

/// <summary>
/// Top-up import. A populated target used to be refused outright, which left an Azure subnet that
/// gained a prefix after import impossible to import by any route - while BASTET went on advertising
/// the Azure-assigned range as free space with a Create Subnet button over it.
///
/// The allowance is deliberately narrow: the target must already be linked to THIS VNet. That
/// separates a top-up from adopting a subtree somebody built by hand, which is what re-stamps
/// AzureResourceId on rows nobody imported and pulls them into a later reconcile cascade. The
/// refusal tests below are the fix as much as the permission tests are.
/// </summary>
public class AzureBulkImportTopUpTests
{
    private const string VNetA = "/subscriptions/test/providers/Microsoft.Network/virtualNetworks/vnet-a";
    private const string VNetB = "/subscriptions/test/providers/Microsoft.Network/virtualNetworks/vnet-b";

    private readonly AzureBulkImportPlanner _planner =
        new(new IpUtilityService(), new InputSanitizationService());

    private static BulkImportSelectedSubnetDto Sub(string name, string prefix, string? resourceId = null) =>
        new() { Name = name, AddressPrefix = prefix, AzureResourceId = resourceId ?? $"{VNetA}/subnets/{name}" };

    private static BulkImportSelectionDto Selection(bool rename = false, params BulkImportSelectedSubnetDto[] subs) =>
        new()
        {
            SubscriptionId = "sub-1",
            SubscriptionName = "Test Sub",
            RenameMatchedBastetSubnets = rename,
            VNetPrefixes =
            [
                new BulkImportSelectedVNetPrefixDto
                {
                    VNetName = "vnet-a",
                    VNetResourceId = VNetA,
                    AddressPrefix = "10.90.0.0/16",
                    Subnets = [.. subs]
                }
            ]
        };

    private static ExistingSubnetSnapshot Target(
        string? linkedTo, bool hasChildren = true, bool hasHostIps = false, bool fullyAllocated = false) =>
        new()
        {
            Id = 1,
            Name = "vnet-a",
            NetworkAddress = "10.90.0.0",
            Cidr = 16,
            AzureResourceId = linkedTo,
            HasChildSubnets = hasChildren,
            HasHostIpAssignments = hasHostIps,
            IsFullyAllocated = fullyAllocated
        };

    private BulkImportPlanItem Plan(BulkImportSelectionDto selection, params ExistingSubnetSnapshot[] existing) =>
        _planner.BuildPlan(selection, existing).Items[0];

    // -------------------------------------------------------------------------
    // The allowance
    // -------------------------------------------------------------------------

    /// <summary>The defect: the one genuinely-new subnet could not be imported by any route.</summary>
    [Fact]
    public void ATargetAlreadyLinkedToThisVNet_AcceptsTheMissingSubnet()
    {
        BulkImportPlanItem item = Plan(
            Selection(false, Sub("sn-new", "10.90.77.0/24")),
            Target(linkedTo: VNetA));

        Assert.Empty(item.Errors);
        Assert.Equal(BulkImportTargetType.ExactMatch, item.TargetType);
        Assert.Single(item.ChildSubnets);
    }

    // -------------------------------------------------------------------------
    // The refusals - each one is a way the allowance could have gone wrong
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adoption. A populated target with no Azure link was built by hand; claiming it would stamp
    /// this VNet's id on rows nobody imported, after which a reconcile cascade can archive them.
    /// </summary>
    [Fact]
    public void APopulatedTargetWithNoAzureLink_IsStillRefused()
    {
        BulkImportPlanItem item = Plan(
            Selection(false, Sub("sn-new", "10.90.77.0/24")),
            Target(linkedTo: null));

        Assert.Contains(item.Errors, e => e.Contains("already has child subnets"));
    }

    [Fact]
    public void APopulatedTargetLinkedToADifferentVNet_IsStillRefused()
    {
        BulkImportPlanItem item = Plan(
            Selection(false, Sub("sn-new", "10.90.77.0/24")),
            Target(linkedTo: VNetB));

        Assert.Contains(item.Errors, e => e.Contains("already linked to Azure VNet"));
    }

    [Fact]
    public void ATargetWithHostIpAssignments_IsStillRefused()
    {
        BulkImportPlanItem item = Plan(
            Selection(false, Sub("sn-new", "10.90.77.0/24")),
            Target(linkedTo: VNetA, hasHostIps: true));

        Assert.Contains(item.Errors, e => e.Contains("host IP assignments"));
    }

    [Fact]
    public void ATargetMarkedFullyAllocated_IsStillRefused()
    {
        BulkImportPlanItem item = Plan(
            Selection(false, Sub("sn-new", "10.90.77.0/24")),
            Target(linkedTo: VNetA, fullyAllocated: true));

        Assert.Contains(item.Errors, e => e.Contains("fully allocated"));
    }

    /// <summary>
    /// The gap the verifier named. The commit marks the target fully allocated INSTEAD of creating
    /// children, so a populated target would be left claiming nothing more fits while already
    /// holding rows. The old blanket refusal was preventing this incidentally.
    /// </summary>
    [Fact]
    public void ATopUpCannotMarkAPopulatedTargetFullyAllocated()
    {
        BulkImportPlanItem item = Plan(
            // An Azure subnet covering the whole VNet prefix
            Selection(false, Sub("sn-whole", "10.90.0.0/16")),
            Target(linkedTo: VNetA));

        Assert.Contains(item.Errors, e => e.Contains("already has child subnets"));
        Assert.False(item.WillMarkFullyAllocated);
    }

    /// <summary>
    /// Renaming a target that already holds imported rows changes a label the operator has been
    /// living with, for a run whose purpose is to add the one subnet that was missing.
    /// </summary>
    [Fact]
    public void ATopUpNeverRenamesThePopulatedTarget()
    {
        BulkImportPlanItem item = Plan(
            Selection(rename: true, Sub("sn-new", "10.90.77.0/24")),
            Target(linkedTo: VNetA));

        Assert.False(item.WillRename);
        Assert.Empty(item.Errors);
    }

    /// <summary>An empty target is still renamed when asked - the top-up guard must not disable it.</summary>
    [Fact]
    public void AnEmptyTargetIsStillRenamedWhenRequested()
    {
        BulkImportPlanItem item = Plan(
            Selection(rename: true, Sub("sn-new", "10.90.77.0/24")),
            new ExistingSubnetSnapshot
            {
                Id = 1,
                Name = "some-old-name",
                NetworkAddress = "10.90.0.0",
                Cidr = 16,
                AzureResourceId = VNetA,
                HasChildSubnets = false
            });

        Assert.True(item.WillRename);
        Assert.Equal("vnet-a", item.NewName);
    }

    // -------------------------------------------------------------------------
    // The preview must not describe a top-up as a first import
    // -------------------------------------------------------------------------

    [Fact]
    public void ThePreviewSaysItIsAddingToAnExistingSubnet_NotImportingIntoIt()
    {
        BulkAzureVNetViewModel vnet = new()
        {
            ResourceId = VNetA,
            Name = "vnet-a",
            Ipv4AddressPrefixes = ["10.90.0.0/16"],
            Subnets =
            [
                new BulkAzureSubnetViewModel
                {
                    ResourceId = $"{VNetA}/subnets/sn-new",
                    Name = "sn-new",
                    AddressPrefix = "10.90.77.0/24",
                    Ipv4AddressPrefixes = ["10.90.77.0/24"]
                }
            ]
        };

        _planner.AnnotateAvailability([vnet], [Target(linkedTo: VNetA)]);

        BulkAzurePrefixViewModel prefix = vnet.Prefixes[0];
        Assert.True(prefix.IsSelectable);
        Assert.Equal(BulkImportAvailability.WillUpdateExisting, prefix.Status);
        Assert.Contains("add any missing subnets", prefix.Reason);
    }

    [Fact]
    public void AnEmptyTargetKeepsTheFirstImportWording()
    {
        BulkAzureVNetViewModel vnet = new()
        {
            ResourceId = VNetA,
            Name = "vnet-a",
            Ipv4AddressPrefixes = ["10.90.0.0/16"],
            Subnets = []
        };

        _planner.AnnotateAvailability([vnet], [Target(linkedTo: VNetA, hasChildren: false)]);

        Assert.Contains("Will import into existing", vnet.Prefixes[0].Reason);
    }
}
