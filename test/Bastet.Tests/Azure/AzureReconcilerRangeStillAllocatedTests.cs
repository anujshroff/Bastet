using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;

namespace Bastet.Tests.Azure;

/// <summary>
/// Azure has no subnet rename, so re-organising one means delete-and-recreate. The reconciler keys
/// only on the recorded ARM resource id, so the Bastet row goes stale while the range it records is
/// still assigned in Azure under a new id - and archiving it makes BASTET advertise an allocated
/// range as free space with a Create Subnet button over it.
///
/// The counter-tests matter as much as the positives: a genuinely deleted resource must STILL be
/// offered and deletable, and overlapping RFC1918 space in another VNet must not withhold anything.
/// An over-blocking reconciler is a different defect, not a fix.
/// </summary>
public class AzureReconcilerRangeStillAllocatedTests
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";

    private readonly AzureReconciler _reconciler = new(new IpUtilityService());

    private static string VNetId(string name) =>
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/{name}";

    private static string SubnetId(string vnetName, string subnetName) =>
        $"{VNetId(vnetName)}/subnets/{subnetName}";

    private static BulkAzureSubnetViewModel AzSubnet(string vnetName, string name, params string[] prefixes) =>
        new()
        {
            ResourceId = SubnetId(vnetName, name),
            Name = name,
            AddressPrefix = prefixes[0],
            Ipv4AddressPrefixes = [.. prefixes]
        };

    private static BulkAzureVNetViewModel VNet(string name, string[] prefixes, params BulkAzureSubnetViewModel[] subnets) =>
        new()
        {
            ResourceId = VNetId(name),
            Name = name,
            Ipv4AddressPrefixes = [.. prefixes],
            Subnets = [.. subnets]
        };

    private static AzureVNetInventory Live(params BulkAzureVNetViewModel[] vnets) =>
        new() { Success = true, VNets = [.. vnets] };

    private static AzureLinkedSubnetSnapshot Linked(
        int id, string name, string network, int cidr, string azureResourceId,
        int[]? descendantIds = null) =>
        new()
        {
            Id = id,
            Name = name,
            NetworkAddress = network,
            Cidr = cidr,
            AzureResourceId = azureResourceId,
            DescendantSubnetIds = descendantIds ?? []
        };

    private AzureReconcilePlanViewModel Build(
        AzureVNetInventory inventory, params AzureLinkedSubnetSnapshot[] linked) =>
        _reconciler.BuildPlan(SubId, "Test Sub", inventory, linked, []);

    // -------------------------------------------------------------------------
    // The defect - a range still assigned in Azure must never be offered for deletion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Route A: sn-a deleted and recreated as sn-a2 carrying the same prefix. The resource id is
    /// genuinely gone, so SubnetDeleted is literally true - but the /24 is still assigned.
    /// </summary>
    [Fact]
    public void SubnetDeleted_ButTheRangeIsStillAssignedInTheSameVNet_IsWithheldAndReviewable()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.111.0.0/16"], AzSubnet("vnet-a", "sn-a2", "10.111.5.0/24"))),
            Linked(2, "app", "10.111.5.0", 24, SubnetId("vnet-a", "sn-a")));

        Assert.Empty(plan.Items);

        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.RangeStillAllocatedInAzure, review.Status);
        Assert.Contains("sn-a2", review.Reason);
        Assert.Contains("10.111.5.0/24", review.Reason);
    }

    /// <summary>Route B: the prefix moved to a different Azure subnet. No ARM read stands behind
    /// this status at all, so it reaches the same wrong output with even less friction.</summary>
    [Fact]
    public void SubnetPrefixChanged_ButTheRangeMovedToAnotherAzureSubnet_IsWithheldAndReviewable()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.111.0.0/16"],
                AzSubnet("vnet-a", "sn-a", "10.111.9.0/24"),
                AzSubnet("vnet-a", "sn-b", "10.111.5.0/24"))),
            Linked(2, "app", "10.111.5.0", 24, SubnetId("vnet-a", "sn-a")));

        Assert.Empty(plan.Items);
        Assert.Equal(AzureReconcileStatus.RangeStillAllocatedInAzure, Assert.Single(plan.ReviewItems).Status);
    }

    /// <summary>The operator must be told, not just silently denied the deletion.</summary>
    [Fact]
    public void AWithheldRange_ProducesAWarningNamingTheAzureSubnetThatHoldsIt()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.111.0.0/16"], AzSubnet("vnet-a", "sn-a2", "10.111.5.0/24"))),
            Linked(2, "app", "10.111.5.0", 24, SubnetId("vnet-a", "sn-a")));

        Assert.Contains(plan.Warnings, w =>
            w.Contains("still assigned in Azure") && w.Contains("sn-a2"));
    }

    /// <summary>A VNet-level target whose prefix is still carved up in Azure is the same defect one
    /// level up: the VNet address prefix is gone, but a subnet still holds the exact range.</summary>
    [Fact]
    public void VNetPrefixRemoved_ButTheRangeIsStillAssignedToASubnet_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.200.0.0/16"], AzSubnet("vnet-a", "sn-x", "10.111.0.0/16"))),
            Linked(1, "target", "10.111.0.0", 16, VNetId("vnet-a")));

        Assert.Empty(plan.Items);
        Assert.Equal(AzureReconcileStatus.RangeStillAllocatedInAzure, Assert.Single(plan.ReviewItems).Status);
    }

    /// <summary>
    /// O1. The guard above matched prefix STRINGS, so re-carving a range while re-creating the
    /// subnet - one ordinary Azure operation, there being no rename - defeated it. Both defences
    /// went quiet at once and the row was offered for irreversible deletion on a plan that stated
    /// no fact at all about the range Azure was holding at that moment.
    /// </summary>
    [Fact]
    public void SubnetDeleted_ButTheRangeWasRecarvedUnderANewId_IsWithheldAndNamesTheLivePrefix()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.191.0.0/16"], AzSubnet("vnet-a", "sn-c-v2", "10.191.20.0/25"))),
            Linked(2, "app", "10.191.20.0", 24, SubnetId("vnet-a", "sn-c")));

        Assert.Empty(plan.Items);

        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.RangeStillAllocatedInAzure, review.Status);
        Assert.Contains("10.191.20.0/25", review.Reason);
        Assert.Contains("sn-c-v2", review.Reason);
    }

    /// <summary>
    /// The owner's call on O1: an overlapping owner gets NO Re-link button. Re-linking would point
    /// the row at a subnet holding a different range, producing SubnetPrefixChanged on the very next
    /// scan - the same defect on a loop, on a column no screen can edit. The reason must instead
    /// name the exit that was measured to work.
    /// </summary>
    [Fact]
    public void AnOverlappingLiveOwner_OffersNoRelinkSuggestionAndNamesTheExit()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.191.0.0/16"], AzSubnet("vnet-a", "sn-c-v2", "10.191.20.0/25"))),
            Linked(2, "app", "10.191.20.0", 24, SubnetId("vnet-a", "sn-c")));

        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.True(string.IsNullOrEmpty(review.SuggestedAzureResourceId));
        Assert.True(string.IsNullOrEmpty(review.SuggestedAzureSubnetName));
        Assert.Contains("delete", review.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An exactly-equal live owner keeps the Re-link it already had. Overlap handling must not cost
    /// the repair route on the case the repair was designed for.
    /// </summary>
    [Fact]
    public void AnExactlyEqualLiveOwner_StillOffersTheRelinkSuggestion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.111.0.0/16"], AzSubnet("vnet-a", "sn-a2", "10.111.5.0/24"))),
            Linked(2, "app", "10.111.5.0", 24, SubnetId("vnet-a", "sn-a")));

        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.Equal(SubnetId("vnet-a", "sn-a2"), review.SuggestedAzureResourceId);
        Assert.Equal("sn-a2", review.SuggestedAzureSubnetName);
    }

    /// <summary>
    /// The other overlap direction: Bastet recorded a /25 and Azure now holds the containing /24.
    /// Deliberately withheld too - for an IPAM the safe answer to "part of this range is still
    /// assigned" is the same whichever way the containment runs.
    /// </summary>
    [Fact]
    public void SubnetPrefixChanged_WhereTheLiveOwnerContainsTheRecordedRange_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.191.0.0/16"],
                AzSubnet("vnet-a", "sn-w", "10.191.40.0/24"))),
            Linked(2, "app", "10.191.40.0", 25, SubnetId("vnet-a", "sn-old")));

        Assert.Empty(plan.Items);
        Assert.Equal(AzureReconcileStatus.RangeStillAllocatedInAzure, Assert.Single(plan.ReviewItems).Status);
    }

    /// <summary>
    /// O3. FindLiveOwnerOfRange accepts VNet-level statuses too, and for a VNet-level row the index
    /// lookup finds any Azure SUBNET holding that prefix inside the VNet - so the suggestion, and
    /// therefore the Re-link button, offered to stamp a SUBNET resource id onto a row whose link is
    /// a VNet. That reclassifies the row from review-only to deletable, permanently blocks its VNet
    /// from being imported again, and writes a column no screen in the application can edit.
    /// </summary>
    [Fact]
    public void AVNetLevelRow_IsNeverOfferedASubnetResourceIdAsARelinkSuggestion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.200.0.0/16"], AzSubnet("vnet-a", "sn-x", "10.111.0.0/16"))),
            Linked(1, "target", "10.111.0.0", 16, VNetId("vnet-a")));

        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.RangeStillAllocatedInAzure, review.Status);
        Assert.True(review.IsVNetLevel);
        Assert.True(string.IsNullOrEmpty(review.SuggestedAzureResourceId));
        Assert.True(string.IsNullOrEmpty(review.SuggestedAzureSubnetName));
    }

    /// <summary>
    /// With no button to click, the reason must not end by telling the operator to click one.
    /// </summary>
    [Fact]
    public void AVNetLevelRowWithNoRelink_DoesNotTellTheOperatorToRelink()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.200.0.0/16"], AzSubnet("vnet-a", "sn-x", "10.111.0.0/16"))),
            Linked(1, "target", "10.111.0.0", 16, VNetId("vnet-a")));

        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.DoesNotContain("Re-link it to that Azure subnet", review.Reason);
        Assert.Contains("10.111.0.0/16", review.Reason);
    }

    // -------------------------------------------------------------------------
    // Counter-tests - the reconciler must still DISCRIMINATE, not merely block
    // -------------------------------------------------------------------------

    /// <summary>
    /// The over-blocking counter-test for O1. A live range in the row's OWN VNet that does not
    /// overlap the recorded range must leave the row deletable - otherwise the fix trades a silent
    /// archive for a reconciler that can never clean anything up.
    /// </summary>
    [Fact]
    public void ALiveRangeInTheSameVNetThatDoesNotOverlap_LeavesTheRowDeletable()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.191.0.0/16"],
                AzSubnet("vnet-a", "sn-neighbour", "10.191.21.0/24"))),
            Linked(2, "app", "10.191.20.0", 24, SubnetId("vnet-a", "sn-c")));

        Assert.Equal(AzureReconcileStatus.SubnetDeleted, Assert.Single(plan.Items).Status);
        Assert.Empty(plan.ReviewItems);
    }

    /// <summary>The whole point of the feature. A genuinely deleted subnet whose range nothing in
    /// Azure holds any more must still be offered for deletion.</summary>
    [Fact]
    public void SubnetDeleted_AndTheRangeIsAssignedNowhere_IsStillOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.111.0.0/16"], AzSubnet("vnet-a", "other", "10.111.9.0/24"))),
            Linked(2, "app", "10.111.5.0", 24, SubnetId("vnet-a", "sn-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetDeleted, item.Status);
        Assert.Empty(plan.ReviewItems);
    }

    /// <summary>
    /// Overlapping RFC1918 across unrelated VNets is the norm - the audit rig itself ships
    /// 10.10.0.0/16 and 10.10.0.0/20 in one subscription. Matching on the bare prefix string would
    /// withhold genuinely stale rows on the strength of an unrelated VNet's address space.
    /// </summary>
    [Fact]
    public void TheSameRangeAllocatedInADifferentVNet_DoesNotWithholdTheDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(
                VNet("vnet-a", ["10.111.0.0/16"]),
                VNet("vnet-b", ["10.111.0.0/16"], AzSubnet("vnet-b", "sn-elsewhere", "10.111.5.0/24"))),
            Linked(2, "app", "10.111.5.0", 24, SubnetId("vnet-a", "sn-a")));

        Assert.Equal(AzureReconcileStatus.SubnetDeleted, Assert.Single(plan.Items).Status);
        Assert.Empty(plan.ReviewItems);
    }

    /// <summary>
    /// The index must accumulate, never ToDictionary: one prefix string legitimately has several
    /// owners across a subscription. A duplicate-key throw here turns every scan of a subscription
    /// with duplicated private space into "The reconcile scan failed" - the failure mode
    /// AzureReconciler.cs already avoids at the subnet-prefix index for exactly this reason.
    /// </summary>
    [Fact]
    public void DuplicateRangesAcrossVNets_DoNotThrow()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(
                VNet("vnet-a", ["10.10.0.0/16"], AzSubnet("vnet-a", "sn-1", "10.10.1.0/24")),
                VNet("vnet-b", ["10.10.0.0/20"], AzSubnet("vnet-b", "sn-2", "10.10.1.0/24")),
                VNet("vnet-c", ["10.10.0.0/16"], AzSubnet("vnet-c", "sn-3", "10.10.1.0/24"))),
            Linked(5, "unrelated", "192.168.0.0", 24, SubnetId("vnet-a", "gone")));

        Assert.True(plan.ScanSucceeded);
        Assert.Empty(plan.GlobalErrors);
    }

    /// <summary>A live row is still live - the new index must not turn a healthy subnet into an item.</summary>
    [Fact]
    public void ASubnetThatStillExistsWithItsRecordedPrefix_IsReportedNowhere()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.111.0.0/16"], AzSubnet("vnet-a", "sn-a", "10.111.5.0/24"))),
            Linked(2, "app", "10.111.5.0", 24, SubnetId("vnet-a", "sn-a")));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
        Assert.Empty(plan.Warnings);
    }

    /// <summary>
    /// A withheld row must also protect its ancestors: approving a target archives the whole
    /// subtree, so an ancestor whose cascade would take a still-allocated row must come off the
    /// list too. This is what ApplyConfirmations' withheld set already does for ReviewItems.
    /// </summary>
    [Fact]
    public void AnAncestorWhoseCascadeWouldArchiveAWithheldRow_IsAlsoWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.111.0.0/16"], AzSubnet("vnet-a", "sn-a2", "10.111.5.0/24"))),
            // the target: its VNet prefix is gone from Azure, so it is genuinely stale...
            Linked(1, "target", "10.99.0.0", 16, VNetId("vnet-a"), descendantIds: [2]),
            // ...but archiving it would take the child whose range Azure still holds
            Linked(2, "app", "10.111.5.0", 24, SubnetId("vnet-a", "sn-a")));

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>());

        Assert.Empty(plan.Items);
        Assert.Contains(plan.Warnings, w => w.Contains("withheld from deletion"));
    }
}
