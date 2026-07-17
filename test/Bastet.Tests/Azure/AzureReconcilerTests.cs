using Bastet.Models.ViewModels;
using Bastet.Services.Azure;

namespace Bastet.Tests.Azure;

/// <summary>
/// Tests for the Azure reconciler. The reconciler decides what may be deleted, so the cases that
/// matter most are the ones where it must stay quiet: a failed scan, and resources that are still
/// live. A false positive here archives real data.
/// </summary>
public class AzureReconcilerTests
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";
    private const string OtherSubId = "22222222-2222-2222-2222-222222222222";

    private readonly AzureReconciler _reconciler;

    public AzureReconcilerTests() => _reconciler = new AzureReconciler();

    // -------------------------------------------------------------------------
    // Builders
    // -------------------------------------------------------------------------

    private static string VNetId(string name, string subscriptionId = SubId) =>
        $"/subscriptions/{subscriptionId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/{name}";

    private static string SubnetId(string vnetName, string subnetName, string subscriptionId = SubId) =>
        $"{VNetId(vnetName, subscriptionId)}/subnets/{subnetName}";

    private static BulkAzureVNetViewModel VNet(string name, string[] prefixes, params BulkAzureSubnetViewModel[] subnets) =>
        new()
        {
            ResourceId = VNetId(name),
            Name = name,
            Ipv4AddressPrefixes = [.. prefixes],
            Subnets = [.. subnets]
        };

    private static BulkAzureSubnetViewModel AzSubnet(string vnetName, string name, string prefix) =>
        new() { ResourceId = SubnetId(vnetName, name), Name = name, AddressPrefix = prefix };

    private static AzureVNetInventory Live(params BulkAzureVNetViewModel[] vnets) =>
        new() { Success = true, VNets = [.. vnets] };

    private static AzureVNetInventory Failed(string error = "boom") =>
        new() { Success = false, ErrorMessage = error };

    private static AzureLinkedSubnetSnapshot Linked(
        int id, string name, string network, int cidr, string azureResourceId,
        bool fullyAllocated = false, int descendants = 0, int hostIps = 0, int[]? descendantIds = null) =>
        new()
        {
            Id = id,
            Name = name,
            NetworkAddress = network,
            Cidr = cidr,
            AzureResourceId = azureResourceId,
            IsFullyAllocated = fullyAllocated,
            DescendantCount = descendants,
            HostIpCount = hostIps,
            DescendantSubnetIds = descendantIds ?? []
        };

    private AzureReconcilePlanViewModel Build(
        AzureVNetInventory inventory, params AzureLinkedSubnetSnapshot[] linked) =>
        _reconciler.BuildPlan(SubId, "Test Sub", inventory, linked);

    // -------------------------------------------------------------------------
    // Fail closed - the safety property the whole feature rests on
    // -------------------------------------------------------------------------

    [Fact]
    public void ScanFailed_ReturnsNoItemsAndCannotCommit()
    {
        // A failed read tells us nothing about what exists in Azure. If this ever reports items,
        // an expired credential or a transient outage would invite deleting the entire tree.
        AzureReconcilePlanViewModel plan = Build(
            Failed("ManagedIdentityCredential authentication failed"),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a")),
            Linked(2, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        Assert.False(plan.ScanSucceeded);
        Assert.False(plan.CanCommit);
        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("Could not read VNets from Azure"));
    }

    [Fact]
    public void ScanFailed_SurfacesUnderlyingError()
    {
        AzureReconcilePlanViewModel plan = Build(Failed("credential expired"));

        Assert.Contains(plan.GlobalErrors, e => e.Contains("credential expired"));
    }

    [Fact]
    public void EmptySubscriptionWithFlaggedItems_AddsWarning()
    {
        // Azure legitimately reporting an empty subscription and pointing at the wrong subscription
        // look identical here, and the consequence is deleting everything.
        AzureReconcilePlanViewModel plan = Build(
            Live(),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a")));

        Assert.True(plan.ScanSucceeded);
        Assert.Single(plan.Items);
        Assert.Contains(plan.Warnings, w => w.Contains("no VNets at all"));
    }

    [Fact]
    public void EmptySubscriptionWithNothingLinked_AddsNoWarning()
    {
        AzureReconcilePlanViewModel plan = Build(Live());

        Assert.Empty(plan.Items);
        Assert.Empty(plan.Warnings);
        Assert.False(plan.CanCommit); // nothing to do
    }

    // -------------------------------------------------------------------------
    // VNet-level rows
    // -------------------------------------------------------------------------

    [Fact]
    public void VNetDeleted_Flagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", ["192.168.0.0/16"])),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetDeleted, item.Status);
        Assert.True(item.IsVNetLevel);
        Assert.True(plan.CanCommit);
    }

    [Fact]
    public void VNetLiveButPrefixRemoved_Flagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "second prefix", "10.1.0.0", 16, VNetId("vnet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, item.Status);
        Assert.Contains("no longer has the address prefix", item.Reason);
    }

    [Fact]
    public void VNetAndPrefixLive_NotFlagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a")));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void MultipleRowsShareOneVNetResourceId_EachJudgedOnItsOwnPrefix()
    {
        // A VNet with two prefixes imports as two Bastet rows carrying the same resource ID.
        // Dropping one prefix must flag only that row.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "kept", "10.0.0.0", 16, VNetId("vnet-a")),
            Linked(2, "dropped", "10.1.0.0", 16, VNetId("vnet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(2, item.SubnetId);
        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, item.Status);
    }

    // -------------------------------------------------------------------------
    // Subnet-level rows
    // -------------------------------------------------------------------------

    [Fact]
    public void SubnetDeleted_Flagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetDeleted, item.Status);
        Assert.False(item.IsVNetLevel);
    }

    [Fact]
    public void SubnetPrefixChanged_Flagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.9.0/24"))),
            Linked(1, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetPrefixChanged, item.Status);
        Assert.Contains("10.0.9.0/24", item.Reason);
    }

    [Fact]
    public void SubnetLive_NotFlagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.1.0/24"))),
            Linked(1, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        Assert.Empty(plan.Items);
    }

    // -------------------------------------------------------------------------
    // Fully encompassing VNet (VNet 10.11.0.0/24 whose only subnet is 10.11.0.0/24)
    // -------------------------------------------------------------------------

    [Fact]
    public void FullyEncompassedVNet_AllLive_NotFlagged()
    {
        // Import produces ONE Bastet row carrying the VNet's id and IsFullyAllocated; the Azure
        // subnet gets no row of its own. Nothing has drifted here.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-e", ["10.11.0.0/24"], AzSubnet("vnet-e", "default", "10.11.0.0/24"))),
            Linked(1, "vnet-e", "10.11.0.0", 24, VNetId("vnet-e"), fullyAllocated: true));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void FullyEncompassedVNet_VNetDeleted_FlaggedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(),
            Linked(1, "vnet-e", "10.11.0.0", 24, VNetId("vnet-e"), fullyAllocated: true));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetDeleted, item.Status);
    }

    [Fact]
    public void FullyEncompassedVNet_EncompassingSubnetDeleted_GoesToReviewItemsNotItems()
    {
        // The VNet and its prefix survive, so there is nothing to delete - but the fully-allocated
        // flag no longer has anything backing it. Report, never act: the flag can be set by hand.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-e", ["10.11.0.0/24"])),
            Linked(1, "vnet-e", "10.11.0.0", 24, VNetId("vnet-e"), fullyAllocated: true));

        Assert.Empty(plan.Items);
        AzureReconcileItem item = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.FullyAllocatingSubnetDeleted, item.Status);
        Assert.Contains("fully allocated", item.Reason);

        // Review items alone must never enable the delete button.
        Assert.False(plan.CanCommit);
    }

    [Fact]
    public void NotFullyAllocatedVNet_WithNoCoveringSubnet_NotFlagged()
    {
        // A normal VNet target whose children happen to be smaller than the prefix is not drift.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.1.0/24"))),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a"), fullyAllocated: false));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    // -------------------------------------------------------------------------
    // Scoping and matching
    // -------------------------------------------------------------------------

    [Fact]
    public void SubnetFromOtherSubscription_Ignored()
    {
        // This scan says nothing about another subscription's resources, so they are out of scope
        // rather than deleted.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "elsewhere", "172.16.0.0", 16, VNetId("vnet-z", OtherSubId)));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void ResourceIdCasingDiffers_TreatedAsLive()
    {
        // ARM resource IDs are case-insensitive; a casing difference is not a deletion.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a").ToUpperInvariant()));

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void SubnetWithoutAzureResourceId_Ignored()
    {
        // Hand-created subnets never carry a resource ID and must never be touched.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "manual", "192.168.1.0", 24, string.Empty));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void SubscriptionIdAppearingElsewhereInPath_DoesNotCountAsInScope()
    {
        // Guards against matching the subscription with a bare substring test.
        string foreignId = $"/subscriptions/{OtherSubId}/resourceGroups/{SubId}/providers/Microsoft.Network/virtualNetworks/vnet-x";

        AzureReconcilePlanViewModel plan = Build(Live(), Linked(1, "x", "10.5.0.0", 16, foreignId));

        Assert.Empty(plan.Items);
    }

    // -------------------------------------------------------------------------
    // Cascade reporting
    // -------------------------------------------------------------------------

    [Fact]
    public void CascadeCounts_SurfacedOnItems()
    {
        // Deleting a stale VNet target archives its whole subtree, so the counts must reach the UI
        // before the user confirms.
        AzureReconcilePlanViewModel plan = Build(
            Live(),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a"), descendants: 3, hostIps: 7,
                descendantIds: [2, 3, 4]));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(3, item.DescendantCount);
        Assert.Equal(7, item.HostIpCount);
        // The subtree ids let the confirm dialog skip items an ancestor's counts already cover.
        Assert.Equal([2, 3, 4], item.DescendantSubnetIds);
    }

    [Fact]
    public void StatusName_IsSerializedAsAName_NotAnOrdinal()
    {
        // The client switches on this string; an ordinal would silently break if the enum changed.
        AzureReconcilePlanViewModel plan = Build(
            Live(),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a")));

        Assert.Equal("VNetDeleted", Assert.Single(plan.Items).StatusName);
    }

    // -------------------------------------------------------------------------
    // Validation of inputs
    // -------------------------------------------------------------------------

    [Fact]
    public void NoSubscriptionSpecified_HardFails()
    {
        AzureReconcilePlanViewModel plan = _reconciler.BuildPlan(
            string.Empty, null, Live(), [Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a"))]);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("No subscription"));
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void NothingLinked_ProducesEmptyPlanThatCannotCommit()
    {
        AzureReconcilePlanViewModel plan = Build(Live(VNet("vnet-a", ["10.0.0.0/16"])));

        Assert.True(plan.ScanSucceeded);
        Assert.Empty(plan.GlobalErrors);
        Assert.False(plan.CanCommit);
    }
}
