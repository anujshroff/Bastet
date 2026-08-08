using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;

namespace Bastet.Tests.Azure;

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

    [Fact]
    public void AWithheldRange_ProducesAWarningNamingTheAzureSubnetThatHoldsIt()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.111.0.0/16"], AzSubnet("vnet-a", "sn-a2", "10.111.5.0/24"))),
            Linked(2, "app", "10.111.5.0", 24, SubnetId("vnet-a", "sn-a")));

        Assert.Contains(plan.Warnings, w =>
            w.Contains("withheld from deletion") && w.Contains("sn-a2"));
    }

    [Fact]
    public void TheWithheldWarning_AssertsNeitherARenameNorAWholeRange()
    {

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.193.0.0/16"], AzSubnet("vnet-a", "sn-wide", "10.193.40.0/24"))),
            Linked(2, "app", "10.193.40.0", 25, SubnetId("vnet-a", "sn-old")));

        string warning = Assert.Single(plan.Warnings);

        Assert.Contains("still overlaps the range they record", warning);
        Assert.Contains("sn-wide", warning);

        Assert.DoesNotContain("what a subnet rename looks like", warning);
        Assert.DoesNotContain("still assigned in Azure under a different resource", warning);
    }

    [Fact]
    public void TheWithheldWarning_IsTrueForANonExactVNetLevelOverlap()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.200.0.0/16"], AzSubnet("vnet-a", "sn-x", "10.194.128.0/17"))),
            Linked(1, "target", "10.194.0.0", 16, VNetId("vnet-a")));

        Assert.Contains(plan.Warnings, w => w.Contains("still overlaps the range they record"));
    }

    [Fact]
    public void VNetPrefixRemoved_ButTheRangeIsStillAssignedToASubnet_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.200.0.0/16"], AzSubnet("vnet-a", "sn-x", "10.111.0.0/16"))),
            Linked(1, "target", "10.111.0.0", 16, VNetId("vnet-a")));

        Assert.Empty(plan.Items);
        Assert.Equal(AzureReconcileStatus.RangeStillAllocatedInAzure, Assert.Single(plan.ReviewItems).Status);
    }

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

    [Fact]
    public void SubnetPrefixChanged_WhereTheRowsOwnAzureSubnetStillHoldsPartOfTheRange_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(

            Live(VNet("vnet-a", ["10.231.0.0/16"], AzSubnet("vnet-a", "sn-a", "10.231.1.0/25"))),
            Linked(2, "app", "10.231.1.0", 24, SubnetId("vnet-a", "sn-a")));

        Assert.Empty(plan.Items);
        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.RangeStillAllocatedInAzure, review.Status);
        Assert.Contains("10.231.1.0/25", review.Reason);

        Assert.True(string.IsNullOrEmpty(review.SuggestedAzureResourceId));
    }

    [Fact]
    public void SubnetPrefixChanged_WhereTheRowsOwnAzureSubnetNowHoldsAWiderRange_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.232.0.0/16"], AzSubnet("vnet-a", "sn-a", "10.232.1.0/24"))),
            Linked(2, "app", "10.232.1.0", 25, SubnetId("vnet-a", "sn-a")));

        Assert.Empty(plan.Items);
        Assert.Equal(AzureReconcileStatus.RangeStillAllocatedInAzure, Assert.Single(plan.ReviewItems).Status);
    }

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

    [Fact]
    public void AnAncestorWhoseCascadeWouldArchiveAWithheldRow_IsAlsoWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.111.0.0/16"], AzSubnet("vnet-a", "sn-a2", "10.111.5.0/24"))),

            Linked(1, "target", "10.99.0.0", 16, VNetId("vnet-a"), descendantIds: [2]),

            Linked(2, "app", "10.111.5.0", 24, SubnetId("vnet-a", "sn-a")));

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>());

        Assert.Empty(plan.Items);
        Assert.Contains(plan.Warnings, w => w.Contains("withheld from deletion"));
    }
    [Fact]
    public void SubnetDeleted_ButTheRangeIsStillAssignedInADifferentVNet_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-renamed", ["10.198.0.0/16"], AzSubnet("vnet-renamed", "s1", "10.198.1.0/24"))),
            Linked(2, "app", "10.198.1.0", 24, SubnetId("vnet-doomed", "s1")));

        Assert.Empty(plan.Items);
        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.RangeStillAllocatedInAzure, review.Status);
        Assert.Contains("vnet-renamed", review.Reason);
    }

    [Fact]
    public void VNetDeleted_ButTheAddressSpaceIsStillDeclaredByADifferentVNet_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-b-renamed", ["10.199.0.0/16"])),
            Linked(1, "target", "10.199.0.0", 16, VNetId("vnet-b-doomed")));

        Assert.Empty(plan.Items);
        Assert.Equal(AzureReconcileStatus.RangeStillAllocatedInAzure, Assert.Single(plan.ReviewItems).Status);
    }

    [Fact]
    public void AWithheldCrossVNetRow_IsOfferedNoRelinkSuggestion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-renamed", ["10.198.0.0/16"], AzSubnet("vnet-renamed", "s1", "10.198.1.0/24"))),
            Linked(2, "app", "10.198.1.0", 24, SubnetId("vnet-doomed", "s1")));

        AzureReconcileItem review = Assert.Single(plan.ReviewItems);
        Assert.True(string.IsNullOrEmpty(review.SuggestedAzureResourceId));
        Assert.True(string.IsNullOrEmpty(review.SuggestedAzureSubnetName));
    }

    [Fact]
    public void AGenuinelyDeletedRangeNothingElseInTheSubscriptionCovers_IsStillOffered()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", ["10.50.0.0/16"], AzSubnet("vnet-other", "unrelated", "10.50.1.0/24"))),
            Linked(2, "app", "10.198.1.0", 24, SubnetId("vnet-doomed", "s1")));

        Assert.Single(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }
    [Fact]
    public void TheWithheldWarning_IsTrueOnTheExactArmWhereTheRangesAreIdentical()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.160.0.0/16"], AzSubnet("vnet-a", "s1b", "10.160.1.0/24"))),
            Linked(2, "app", "10.160.1.0", 24, SubnetId("vnet-a", "s1")));

        string warning = Assert.Single(plan.Warnings);

        Assert.Contains("still overlaps the range they record", warning);
        Assert.DoesNotContain("are not the same", warning);
        Assert.Equal(SubnetId("vnet-a", "s1b"), Assert.Single(plan.ReviewItems).SuggestedAzureResourceId);
    }
}
