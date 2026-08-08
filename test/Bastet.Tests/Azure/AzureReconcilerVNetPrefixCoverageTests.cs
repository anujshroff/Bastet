using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;

namespace Bastet.Tests.Azure;

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

    [Fact]
    public void AVNetPrefixRecarvedWithIdenticalTotalCoverage_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", "10.190.0.0/17", "10.190.128.0/17")),
            Target("vnet-a", "10.190.0.0", 16));

        Assert.Empty(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetPrefixStillCovered, Assert.Single(plan.ReviewItems).Status);
    }

    [Fact]
    public void AVNetPrefixShrunkToASubset_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", "10.180.0.0/17")),
            Target("vnet-a", "10.180.0.0", 16));

        Assert.Empty(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetPrefixStillCovered, Assert.Single(plan.ReviewItems).Status);
    }

    [Fact]
    public void AStillCoveredVNetPrefix_OffersNoRelinkSuggestion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", "10.180.0.0/15")),
            Target("vnet-a", "10.180.0.0", 16));

        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.True(string.IsNullOrEmpty(review.SuggestedAzureResourceId));
    }

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

    [Fact]
    public void AVNetThatIsGoneEntirely_IsStillOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", "192.168.0.0/16")),
            Target("vnet-a", "10.180.0.0", 16));

        Assert.Equal(AzureReconcileStatus.VNetDeleted, Assert.Single(plan.Items).Status);
    }

    [Fact]
    public void AVNetPrefixStillPresentVerbatim_IsReportedNowhere()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", "10.180.0.0/16")),
            Target("vnet-a", "10.180.0.0", 16));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void AnOverlappingPrefixOnAnotherVNet_DoesNotWithholdTheDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", "10.200.0.0/16"), VNet("vnet-b", "10.180.0.0/15")),
            Target("vnet-a", "10.180.0.0", 16));

        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, Assert.Single(plan.Items).Status);
    }

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
