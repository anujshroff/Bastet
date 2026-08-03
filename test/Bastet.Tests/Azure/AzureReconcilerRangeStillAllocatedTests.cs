using Bastet.Models.ViewModels;
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

    private readonly AzureReconciler _reconciler = new();

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
        _reconciler.BuildPlan(SubId, "Test Sub", inventory, linked);

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

    // -------------------------------------------------------------------------
    // Counter-tests - the reconciler must still DISCRIMINATE, not merely block
    // -------------------------------------------------------------------------

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
