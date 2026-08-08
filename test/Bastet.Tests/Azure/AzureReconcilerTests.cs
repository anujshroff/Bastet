using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;

namespace Bastet.Tests.Azure;

public class AzureReconcilerTests
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";
    private const string OtherSubId = "22222222-2222-2222-2222-222222222222";

    private readonly AzureReconciler _reconciler;

    public AzureReconcilerTests() => _reconciler = new AzureReconciler(new IpUtilityService());

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

    private static BulkAzureSubnetViewModel AzSubnet(string vnetName, string name, params string[] prefixes) =>
        new()
        {
            ResourceId = SubnetId(vnetName, name),
            Name = name,
            AddressPrefix = prefixes[0],
            Ipv4AddressPrefixes = [.. prefixes]
        };

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
        _reconciler.BuildPlan(SubId, "Test Sub", inventory, linked, []);

    [Fact]
    public void ScanFailed_ReturnsNoItemsAndCannotCommit()
    {

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
        Assert.False(plan.CanCommit);
    }

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

        Assert.Contains("no longer has any IPv4 address space", item.Reason);
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

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "kept", "10.0.0.0", 16, VNetId("vnet-a")),
            Linked(2, "dropped", "10.1.0.0", 16, VNetId("vnet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(2, item.SubnetId);
        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, item.Status);
    }

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
    public void SubnetWithSecondIpv4Prefix_StillOwningBastetsPrefix_NotFlagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.0.0/24", "10.0.1.0/24"))),
            Linked(1, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void SubnetWithSeveralPrefixes_NoneMatchingBastet_StillFlagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.8.0/24", "10.0.9.0/24"))),
            Linked(1, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetPrefixChanged, item.Status);

        Assert.Contains("10.0.8.0/24", item.Reason);
        Assert.Contains("10.0.9.0/24", item.Reason);
    }

    [Fact]
    public void UnknownVerdict_IsExplainedAsAFailedRead_NotALostCredential()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "gone", "10.9.0.0", 16, VNetId("vnet-gone")));

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("vnet-gone")] = AzureResourceConfirmation.Unknown
        });

        Assert.Empty(plan.Items);
        string warning = Assert.Single(plan.Warnings);
        Assert.Contains("could not be asked", warning);
        Assert.DoesNotContain("lost access", warning);
    }

    [Fact]
    public void NotVisibleVerdict_StillNamesTheCredential()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "gone", "10.9.0.0", 16, VNetId("vnet-gone")));

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("vnet-gone")] = AzureResourceConfirmation.NotVisible
        });

        Assert.Empty(plan.Items);
        string warning = Assert.Single(plan.Warnings);
        Assert.Contains("denied access", warning);
        Assert.Contains("lost access to their resource group", warning);
    }

    [Fact]
    public void MixedWithholdReasons_ProduceSeparateWarnings()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "hidden", "10.9.0.0", 16, VNetId("vnet-hidden")),
            Linked(2, "unreadable", "10.8.0.0", 16, VNetId("vnet-unreadable")));

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("vnet-hidden")] = AzureResourceConfirmation.NotVisible,
            [VNetId("vnet-unreadable")] = AzureResourceConfirmation.Unknown
        });

        Assert.Empty(plan.Items);
        Assert.Equal(2, plan.Warnings.Count);
        Assert.Contains(plan.Warnings, w => w.Contains("denied access") && w.Contains("hidden"));
        Assert.Contains(plan.Warnings, w => w.Contains("could not be asked") && w.Contains("unreadable"));
    }

    [Theory]
    [InlineData("/subscriptions/" + SubId + "/resourceGroups/rg")]
    [InlineData("/subscriptions/" + SubId + "/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/acct")]

    [InlineData("/subscriptions/" + SubId + "/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vnet-a")]
    public void UnrecognisedResourceId_IsReviewedNotOfferedForDeletion(string resourceId)
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "mystery", "10.0.0.0", 16, resourceId));

        Assert.Empty(plan.Items);

        AzureReconcileItem item = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.UnrecognisedResourceId, item.Status);
        Assert.DoesNotContain("no longer exists", item.Reason);
    }

    [Fact]
    public void GenuinelyAbsentVNet_StillOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "gone", "10.9.0.0", 16, VNetId("vnet-gone")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetDeleted, item.Status);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void SubnetLive_NotFlagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.1.0/24"))),
            Linked(1, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void FullyEncompassedVNet_AllLive_NotFlagged()
    {

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-e", ["10.11.0.0/24"], AzSubnet("vnet-e", "default", "10.11.0.0/24"))),
            Linked(1, "vnet-e", "10.11.0.0", 24, VNetId("vnet-e"), fullyAllocated: true));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void FullyEncompassedVNet_CoveringSubnetListsAnotherPrefixFirst_NotFlagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-e", ["10.11.0.0/24"], AzSubnet("vnet-e", "default", "10.99.0.0/24", "10.11.0.0/24"))),
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

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-e", ["10.11.0.0/24"])),
            Linked(1, "vnet-e", "10.11.0.0", 24, VNetId("vnet-e"), fullyAllocated: true));

        Assert.Empty(plan.Items);
        AzureReconcileItem item = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.FullyAllocatingSubnetDeleted, item.Status);
        Assert.Contains("fully allocated", item.Reason);

        Assert.False(plan.CanCommit);
    }

    [Fact]
    public void NotFullyAllocatedVNet_WithNoCoveringSubnet_NotFlagged()
    {

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.1.0/24"))),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a"), fullyAllocated: false));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void SubnetFromOtherSubscription_Ignored()
    {

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "elsewhere", "172.16.0.0", 16, VNetId("vnet-z", OtherSubId)));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void StaleAncestorOverOtherSubscriptionDescendant_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),

            Linked(1, "parent-stale", "10.90.0.0", 15, VNetId("vnet-gone"), descendants: 1, descendantIds: [2]),

            Linked(2, "child-othersub", "10.90.1.0", 24, SubnetId("vnet-visible", "snet-web", OtherSubId)));

        Assert.Empty(plan.Items);
        Assert.Contains(plan.Warnings, w => w.Contains("different subscription") && w.Contains("'parent-stale'"));
    }

    [Theory]
    [InlineData("not-an-arm-id")]

    [InlineData($"/subscriptions/{SubId}")]
    public void UnparseableResourceId_IsReviewed_NotSilentlySkipped(string resourceId)
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "broken-link", "10.90.0.0", 16, resourceId));

        Assert.Empty(plan.Items);

        AzureReconcileItem item = Assert.Single(plan.ReviewItems);
        Assert.Equal(1, item.SubnetId);
        Assert.Equal(AzureReconcileStatus.UnrecognisedResourceId, item.Status);
    }

    [Fact]
    public void StaleAncestorOverUnparseableDescendant_IsWithheldWithoutBlamingASubscription()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "parent-stale", "10.90.0.0", 15, VNetId("vnet-gone"), descendants: 1, descendantIds: [2]),
            Linked(2, "child-broken", "10.90.1.0", 24, "not-an-arm-id"));

        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.Equal(2, review.SubnetId);
        Assert.Equal(AzureReconcileStatus.UnrecognisedResourceId, review.Status);

        Assert.DoesNotContain(plan.Warnings, w => w.Contains("different subscription"));

        _ = Assert.Single(plan.Items);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("vnet-gone")] = AzureResourceConfirmation.Deleted
        });

        Assert.Empty(plan.Items);
        Assert.Contains(plan.Warnings, w => w.Contains("'parent-stale'"));
    }

    [Fact]
    public void StaleTargetWithNoOtherSubscriptionDescendant_IsStillOffered()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "parent-stale", "10.90.0.0", 15, VNetId("vnet-gone")),

            Linked(2, "elsewhere", "172.16.0.0", 16, VNetId("vnet-z", OtherSubId)));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(1, item.SubnetId);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("different subscription"));
    }

    [Fact]
    public void ResourceIdCasingDiffers_TreatedAsLive()
    {

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a").ToUpperInvariant()));

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void SubnetWithoutAzureResourceId_Ignored()
    {

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "manual", "192.168.1.0", 24, string.Empty));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void SubscriptionIdAppearingElsewhereInPath_DoesNotCountAsInScope()
    {

        string foreignId = $"/subscriptions/{OtherSubId}/resourceGroups/{SubId}/providers/Microsoft.Network/virtualNetworks/vnet-x";

        AzureReconcilePlanViewModel plan = Build(Live(), Linked(1, "x", "10.5.0.0", 16, foreignId));

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void CascadeCounts_SurfacedOnItems()
    {

        AzureReconcilePlanViewModel plan = Build(
            Live(),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a"), descendants: 3, hostIps: 7,
                descendantIds: [2, 3, 4]));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(3, item.DescendantCount);
        Assert.Equal(7, item.HostIpCount);

        Assert.Equal([2, 3, 4], item.DescendantSubnetIds);
    }

    [Fact]
    public void StatusName_IsSerializedAsAName_NotAnOrdinal()
    {

        AzureReconcilePlanViewModel plan = Build(
            Live(),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a")));

        Assert.Equal("VNetDeleted", Assert.Single(plan.Items).StatusName);
    }

    [Fact]
    public void NoSubscriptionSpecified_HardFails()
    {
        AzureReconcilePlanViewModel plan = _reconciler.BuildPlan(
            string.Empty, null, Live(), [Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a"))], []);

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

    private AzureReconcilePlanViewModel PlanWithOneDeletedItem(out string resourceId)
    {
        resourceId = VNetId("vnet-a");
        return Build(
            Live(VNet("vnet-visible-to-me", ["10.9.0.0/16"])),
            Linked(1, "a", "10.0.0.0", 16, resourceId));
    }

    [Fact]
    public void ApplyConfirmations_Deleted_KeepsTheItem()
    {
        AzureReconcilePlanViewModel plan = PlanWithOneDeletedItem(out string id);
        _ = Assert.Single(plan.Items);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [id] = AzureResourceConfirmation.Deleted
        });

        _ = Assert.Single(plan.Items);
        Assert.Empty(plan.Warnings);
    }

    [Theory]
    [InlineData(AzureResourceConfirmation.NotVisible)]
    [InlineData(AzureResourceConfirmation.Unknown)]
    [InlineData(AzureResourceConfirmation.Live)]
    public void ApplyConfirmations_AnythingButDeleted_WithholdsTheItemAndExplains(
        AzureResourceConfirmation verdict)
    {
        AzureReconcilePlanViewModel plan = PlanWithOneDeletedItem(out string id);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [id] = verdict
        });

        Assert.Empty(plan.Items);
        Assert.False(plan.CanCommit);
        _ = Assert.Single(plan.Warnings);
        Assert.Contains("'a'", plan.Warnings[0]);
    }

    [Fact]
    public void ApplyConfirmations_IdAbsentFromTheMap_WithholdsTheItem()
    {
        AzureReconcilePlanViewModel plan = PlanWithOneDeletedItem(out _);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>());

        Assert.Empty(plan.Items);
        _ = Assert.Single(plan.Warnings);
    }

    private AzureReconcilePlanViewModel PlanWithOnePrefixRemovedItem(out string resourceId)
    {
        resourceId = VNetId("vnet-a");
        return Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "second prefix", "10.1.0.0", 16, resourceId));
    }

    private AzureReconcilePlanViewModel PlanWithOnePrefixChangedItem(out string resourceId)
    {
        resourceId = SubnetId("vnet-a", "snet-a");
        return Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.9.0/24"))),
            Linked(1, "snet-a", "10.0.1.0", 24, resourceId));
    }

    [Theory]
    [InlineData(AzureResourceConfirmation.Live)]
    [InlineData(AzureResourceConfirmation.NotVisible)]
    [InlineData(AzureResourceConfirmation.Unknown)]
    public void ApplyConfirmations_VNetPrefixRemoved_SurvivesEveryVerdict(
        AzureResourceConfirmation verdict)
    {
        AzureReconcilePlanViewModel plan = PlanWithOnePrefixRemovedItem(out string id);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [id] = verdict
        });

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, item.Status);
        Assert.True(plan.CanCommit);
        Assert.Empty(plan.Warnings);
    }

    [Theory]
    [InlineData(AzureResourceConfirmation.Live)]
    [InlineData(AzureResourceConfirmation.NotVisible)]
    [InlineData(AzureResourceConfirmation.Unknown)]
    public void ApplyConfirmations_SubnetPrefixChanged_SurvivesEveryVerdict(
        AzureResourceConfirmation verdict)
    {
        AzureReconcilePlanViewModel plan = PlanWithOnePrefixChangedItem(out string id);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [id] = verdict
        });

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetPrefixChanged, item.Status);
        Assert.True(plan.CanCommit);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void ApplyConfirmations_DriftRowsAbsentFromTheMap_AreKept()
    {
        AzureReconcilePlanViewModel plan = PlanWithOnePrefixRemovedItem(out _);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>());

        _ = Assert.Single(plan.Items);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void ApplyConfirmations_DriftAndAbsenceTogether_JudgedSeparately()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "drifted", "10.1.0.0", 16, VNetId("vnet-a")),
            Linked(2, "gone", "10.5.0.0", 16, VNetId("vnet-gone")));

        Assert.Equal(2, plan.Items.Count);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("vnet-gone")] = AzureResourceConfirmation.NotVisible
        });

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, item.Status);
        _ = Assert.Single(plan.Warnings);
        Assert.Contains("'gone'", plan.Warnings[0]);
        Assert.DoesNotContain("'drifted'", plan.Warnings[0]);
    }

    [Fact]
    public void VNetInResourceGroupNamedSubnets_IsNotMistakenForAnAzureSubnet()
    {
        const string rgNamedSubnets =
            $"/subscriptions/{SubId}/resourceGroups/subnets/providers/Microsoft.Network/virtualNetworks/vnet-core";

        BulkAzureVNetViewModel live = new()
        {
            ResourceId = rgNamedSubnets,
            Name = "vnet-core",
            Ipv4AddressPrefixes = ["10.20.0.0/16"]
        };

        AzureReconcilePlanViewModel plan = Build(
            Live(live),
            Linked(1, "core", "10.20.0.0", 16, rgNamedSubnets, descendants: 2));

        Assert.True(plan.ScanSucceeded);
        Assert.Empty(plan.Items);
        Assert.False(plan.CanCommit);
    }

    [Fact]
    public void GenuineSubnetId_StillRoutesToTheSubnetBranch()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "child", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetDeleted, item.Status);
        Assert.False(item.IsVNetLevel);
    }

    [Theory]
    [InlineData("not-an-arm-id")]
    [InlineData("")]
    [InlineData("   ")]
    public void MalformedResourceId_DoesNotThrow(string malformed)
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "odd", "10.0.1.0", 24, malformed));

        Assert.True(plan.ScanSucceeded);
        Assert.Empty(plan.GlobalErrors);
    }

    [Fact]
    public void BuildPlan_TargetWhoseDescendantIsStillLiveInAzure_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("inner", ["10.78.128.0/17"])),
            Linked(1, "outer", "10.78.0.0", 16, VNetId("outer"), descendants: 1, descendantIds: [2]),
            Linked(2, "inner", "10.78.128.0", 17, VNetId("inner")));

        Assert.True(plan.ScanSucceeded);
        Assert.Empty(plan.Items);
        Assert.False(plan.CanCommit);
        Assert.Contains(plan.Warnings, w => w.Contains("still exist in Azure") && w.Contains("'outer'"));
    }

    [Theory]
    [InlineData(AzureResourceConfirmation.NotVisible)]
    [InlineData(AzureResourceConfirmation.Unknown)]
    [InlineData(AzureResourceConfirmation.Live)]
    public void ApplyConfirmations_TargetWhoseDescendantWasWithheld_IsAlsoWithheld(
        AzureResourceConfirmation verdict)
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "outer", "10.78.0.0", 16, VNetId("outer"), descendants: 1, descendantIds: [2]),
            Linked(2, "child", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        Assert.Equal(2, plan.Items.Count);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("outer")] = AzureResourceConfirmation.Deleted,
            [SubnetId("vnet-a", "snet-a")] = verdict
        });

        Assert.Empty(plan.Items);
        Assert.False(plan.CanCommit);
        Assert.Contains(plan.Warnings, w => w.Contains("'outer'"));
    }

    [Fact]
    public void ApplyConfirmations_TargetWhoseDescendantIsAReviewItem_IsAlsoWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("inner", ["10.78.128.0/17"])),
            Linked(1, "outer", "10.78.0.0", 16, VNetId("outer"), descendants: 1, descendantIds: [2]),
            Linked(2, "inner", "10.78.128.0", 17, VNetId("inner"), fullyAllocated: true));

        _ = Assert.Single(plan.ReviewItems);
        _ = Assert.Single(plan.Items);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("outer")] = AzureResourceConfirmation.Deleted
        });

        Assert.Empty(plan.Items);
        Assert.Contains(plan.Warnings, w => w.Contains("'outer'"));
    }

    [Fact]
    public void ApplyConfirmations_TargetWhoseDescendantIsAlsoDeleted_IsStillCommittable()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", ["192.168.0.0/16"])),
            Linked(1, "outer", "10.78.0.0", 16, VNetId("outer"), descendants: 1, descendantIds: [2]),
            Linked(2, "child", "10.78.1.0", 24, SubnetId("outer", "snet-a")));

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("outer")] = AzureResourceConfirmation.Deleted,
            [SubnetId("outer", "snet-a")] = AzureResourceConfirmation.Deleted
        });

        Assert.Equal(2, plan.Items.Count);
        Assert.True(plan.CanCommit);
        Assert.Empty(plan.Warnings);
    }
    [Fact]
    public void AWithholdingWarning_IdentifiesEachSubnetByItsRangeNotOnlyItsName()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.113.0.0/16"])),
            Linked(5, "app", "10.113.2.0", 24, SubnetId("vnet-a", "app")),
            Linked(6, "app", "172.16.2.0", 24, SubnetId("vnet-b", "app")));

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [SubnetId("vnet-a", "app")] = AzureResourceConfirmation.NotVisible,
            [SubnetId("vnet-b", "app")] = AzureResourceConfirmation.NotVisible
        });

        string warning = Assert.Single(plan.Warnings, w => w.Contains("withheld from deletion"));
        Assert.Contains("'app' (10.113.2.0/24)", warning);
        Assert.Contains("'app' (172.16.2.0/24)", warning);
    }
}
