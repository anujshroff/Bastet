using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;

namespace Bastet.Tests.Azure;

/// <summary>
/// O2. A VNet-level import target is judged by string membership of its recorded prefix in the
/// VNet's current address prefixes, and VNetPrefixRemoved is deletable without any ARM confirmation.
/// Resizing or re-carving a VNet's address space is ordinary, and when it happens the recorded range
/// is still entirely inside what the VNet owns - so the row was offered for irreversible deletion
/// while Azure still covered every address it records.
///
/// The counter-tests carry as much weight as the positives: a VNet prefix that really is gone must
/// still be deletable, or the fix trades a silent archive for a reconciler that can never clean up.
/// </summary>
public class AzureReconcilerVNetPrefixCoverageTests
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";

    private readonly AzureReconciler _reconciler = new(new IpUtilityService());

    private static string VNetId(string name) =>
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/{name}";

    private static BulkAzureVNetViewModel VNet(string name, params string[] prefixes) =>
        new() { ResourceId = VNetId(name), Name = name, Ipv4AddressPrefixes = [.. prefixes], Subnets = [] };

    private static AzureVNetInventory Live(params BulkAzureVNetViewModel[] vnets) =>
        new() { Success = true, VNets = [.. vnets] };

    private static AzureLinkedSubnetSnapshot Target(string vnetName, string network, int cidr) =>
        new()
        {
            Id = 1,
            Name = "target",
            NetworkAddress = network,
            Cidr = cidr,
            AzureResourceId = VNetId(vnetName),
            DescendantSubnetIds = []
        };

    private AzureReconcilePlanViewModel Build(AzureVNetInventory inventory, AzureLinkedSubnetSnapshot target) =>
        _reconciler.BuildPlan(SubId, "Test Sub", inventory, [target], []);

    // -------------------------------------------------------------------------
    // The defect
    // -------------------------------------------------------------------------

    /// <summary>Expand: 10.180.0.0/16 -> /15. The recorded range is wholly inside the new space.</summary>
    [Fact]
    public void AVNetPrefixExpandedToASuperset_IsWithheldNotOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", "10.180.0.0/15")),
            Target("vnet-a", "10.180.0.0", 16));

        Assert.Empty(plan.Items);

        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.VNetPrefixStillCovered, review.Status);
        Assert.Contains("10.180.0.0/15", review.Reason);
        Assert.False(plan.CanCommit);
    }

    /// <summary>
    /// The case that defeats a containment-only guard, and the reason the test is overlap rather
    /// than containment: 10.190.0.0/16 re-carved into two /17s whose union is byte-identical to the
    /// original. Nothing was released, and neither /17 contains the /16.
    /// </summary>
    [Fact]
    public void AVNetPrefixRecarvedWithIdenticalTotalCoverage_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", "10.190.0.0/17", "10.190.128.0/17")),
            Target("vnet-a", "10.190.0.0", 16));

        Assert.Empty(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetPrefixStillCovered, Assert.Single(plan.ReviewItems).Status);
    }

    /// <summary>Shrink: 10.180.0.0/16 -> /17. Part of the recorded range really was released, but
    /// part of it is still owned, so archiving still loses a live allocation record.</summary>
    [Fact]
    public void AVNetPrefixShrunkToASubset_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", "10.180.0.0/17")),
            Target("vnet-a", "10.180.0.0", 16));

        Assert.Empty(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetPrefixStillCovered, Assert.Single(plan.ReviewItems).Status);
    }

    /// <summary>
    /// Re-link is not the repair here - the VNet resource id never changed - so the row must carry
    /// no suggestion, or the button would write the id it already has.
    /// </summary>
    [Fact]
    public void AStillCoveredVNetPrefix_OffersNoRelinkSuggestion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", "10.180.0.0/15")),
            Target("vnet-a", "10.180.0.0", 16));

        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.True(string.IsNullOrEmpty(review.SuggestedAzureResourceId));
    }

    // -------------------------------------------------------------------------
    // Counter-tests - the reconciler must still discriminate
    // -------------------------------------------------------------------------

    /// <summary>The whole point. A prefix genuinely removed, overlapping nothing the VNet still
    /// owns, must remain deletable.</summary>
    [Fact]
    public void AVNetPrefixThatOverlapsNothingTheVNetStillOwns_IsStillOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", "10.200.0.0/16")),
            Target("vnet-a", "10.180.0.0", 16));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, item.Status);
        Assert.Empty(plan.ReviewItems);
    }

    /// <summary>A VNet with no IPv4 space left at all is absent from the inventory entirely, and
    /// must still read as deleted rather than as covered.</summary>
    [Fact]
    public void AVNetThatIsGoneEntirely_IsStillOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", "192.168.0.0/16")),
            Target("vnet-a", "10.180.0.0", 16));

        Assert.Equal(AzureReconcileStatus.VNetDeleted, Assert.Single(plan.Items).Status);
    }

    /// <summary>An unchanged prefix is not drift at all and must be reported nowhere.</summary>
    [Fact]
    public void AVNetPrefixStillPresentVerbatim_IsReportedNowhere()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", "10.180.0.0/16")),
            Target("vnet-a", "10.180.0.0", 16));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    /// <summary>
    /// Scoping check. An overlapping prefix on a DIFFERENT VNet must not rescue this row -
    /// overlapping RFC1918 across VNets in one subscription is normal.
    /// </summary>
    [Fact]
    public void AnOverlappingPrefixOnAnotherVNet_DoesNotWithholdTheDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", "10.200.0.0/16"), VNet("vnet-b", "10.180.0.0/15")),
            Target("vnet-a", "10.180.0.0", 16));

        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, Assert.Single(plan.Items).Status);
    }

    /// <summary>
    /// A withheld VNet-level row must protect its subtree the way every other ReviewItems row does:
    /// ApplyConfirmations seeds the cascade-withhold set from ReviewItems.
    /// </summary>
    [Fact]
    public void AStillCoveredTarget_ProtectsItsDescendantsFromTheCascade()
    {
        AzureLinkedSubnetSnapshot target = new()
        {
            Id = 1,
            Name = "target",
            NetworkAddress = "10.180.0.0",
            Cidr = 16,
            AzureResourceId = VNetId("vnet-a"),
            DescendantSubnetIds = [2]
        };

        AzureReconcilePlanViewModel plan =
            _reconciler.BuildPlan(SubId, "Test Sub", Live(VNet("vnet-a", "10.180.0.0/15")), [target], []);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>());

        Assert.Empty(plan.Items);
        Assert.Single(plan.ReviewItems);
    }
}
