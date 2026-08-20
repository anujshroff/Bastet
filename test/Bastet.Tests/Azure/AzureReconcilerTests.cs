using Bastet.Models.ViewModels;
using Bastet.Services.Azure;

namespace Bastet.Tests.Azure;

public class AzureReconcilerTests
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
        int manualDescendants = 0, int hostIps = 0, int[]? descendantIds = null) =>
        new()
        {
            Id = id,
            Name = name,
            NetworkAddress = network,
            Cidr = cidr,
            AzureResourceId = azureResourceId,
            ManualDescendantCount = manualDescendants,
            HostIpCount = hostIps,
            DescendantSubnetIds = descendantIds ?? []
        };

    private AzureReconcilePlanViewModel Build(
        AzureVNetInventory inventory,
        AzureLinkedSubnetSnapshot[] linked) =>
        _reconciler.BuildPlan(SubId, inventory, linked);

    // Rule: if it is gone from Azure, delete it here.

    [Fact]
    public void AnAzureSubnetThatIsGone_IsOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.10.0.0/16"])),
            [Linked(1, "app", "10.10.1.0", 24, SubnetId("vnet-a", "sn-a"))]);

        Assert.Equal(AzureReconcileStatus.SubnetDeleted, Assert.Single(plan.Items).Status);
    }

    [Fact]
    public void AVNetThatIsGone_IsOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", ["192.168.0.0/16"])),
            [Linked(1, "target", "10.10.0.0", 16, VNetId("vnet-a"))]);

        Assert.Equal(AzureReconcileStatus.VNetDeleted, Assert.Single(plan.Items).Status);
    }

    [Fact]
    public void ARangeStillHeldByAnotherLiveVNet_IsStillOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(
                VNet("vnet-a", ["10.11.0.0/16"]),
                VNet("vnet-b", ["10.11.0.0/16"], AzSubnet("vnet-b", "sn-elsewhere", "10.11.5.0/24"))),
            [Linked(1, "app", "10.11.5.0", 24, SubnetId("vnet-a", "sn-a"))]);

        Assert.Equal(AzureReconcileStatus.SubnetDeleted, Assert.Single(plan.Items).Status);
    }

    // Rule: if its range changed in Azure, say so and offer the deletion. Re-adding is the
    // operator's own trip back through the import wizard, not something reconcile does.

    [Fact]
    public void AnAzureSubnetWhosePrefixChanged_IsOfferedForDeletionAndPointedAtTheImportWizard()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.12.0.0/16"], AzSubnet("vnet-a", "sn-a", "10.12.1.0/25"))),
            [Linked(1, "app", "10.12.1.0", 24, SubnetId("vnet-a", "sn-a"))]);

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetPrefixChanged, item.Status);
        Assert.Contains("10.12.1.0/25", item.Reason);
        Assert.Contains("Azure import wizard", item.Reason);

        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void AVNetPrefixThatWasRecarved_IsOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.13.0.0/15"])),
            [Linked(1, "target", "10.13.0.0", 16, VNetId("vnet-a"))]);

        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, Assert.Single(plan.Items).Status);
    }

    [Fact]
    public void AnAzureSubnetThatIsUnchanged_IsReportedNowhere()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.14.0.0/16"], AzSubnet("vnet-a", "sn-a", "10.14.1.0/24"))),
            [Linked(1, "app", "10.14.1.0", 24, SubnetId("vnet-a", "sn-a"))]);

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    // Rule: the one refusal is manual content in the hierarchy.

    [Fact]
    public void ASubnetHoldingAManuallyCreatedChild_IsHeldNotDeleted()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.15.0.0/16"])),
            [Linked(1, "app", "10.15.1.0", 24, SubnetId("vnet-a", "sn-a"), manualDescendants: 1)]);

        Assert.Empty(plan.Items);

        AzureReconcileItem held = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.HeldByManualContent, held.Status);
        Assert.Contains("1 subnet created here", held.Reason);
        Assert.Contains(plan.Warnings, w => w.Contains("created here rather than imported from Azure"));

        Assert.Contains("Delete it here first", held.Reason);
        Assert.DoesNotContain("Move", held.Reason);
    }

    [Fact]
    public void ASubnetHoldingAHostIpAssignment_IsHeldNotDeleted()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.16.0.0/16"])),
            [Linked(1, "app", "10.16.1.0", 24, SubnetId("vnet-a", "sn-a"), hostIps: 3)]);

        Assert.Empty(plan.Items);

        AzureReconcileItem held = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.HeldByManualContent, held.Status);
        Assert.Contains("3 host IP assignments", held.Reason);
    }

    [Fact]
    public void ASubnetWhoseOnlyDescendantsCameFromAzure_IsStillDeletable()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.17.0.0/16"])),
            [Linked(1, "app", "10.17.1.0", 24, SubnetId("vnet-a", "sn-a"), manualDescendants: 0)]);

        Assert.Equal(AzureReconcileStatus.SubnetDeleted, Assert.Single(plan.Items).Status);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void AnAncestorOfAHeldSubnet_IsAlsoWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", ["192.168.0.0/16"])),
            [
                Linked(1, "parent", "10.18.0.0", 16, VNetId("vnet-a"), descendantIds: [2]),
                Linked(2, "child", "10.18.1.0", 24, SubnetId("vnet-a", "sn-a"), hostIps: 1)
            ]);

        Assert.Empty(plan.Items);
        Assert.Contains(plan.Warnings, w => w.Contains("manually created content"));
    }

    // Never delete on incomplete information.

    [Fact]
    public void AnAbsentResourceAzureWouldNotConfirm_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.19.0.0/16"])),
            [Linked(1, "app", "10.19.1.0", 24, SubnetId("vnet-a", "sn-a"))]);

        Assert.Single(plan.Items);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [SubnetId("vnet-a", "sn-a")] = AzureResourceConfirmation.NotVisible
        });

        Assert.Empty(plan.Items);
        Assert.Contains(plan.Warnings, w => w.Contains("denied access"));
    }

    [Fact]
    public void AnAbsentResourceConfirmedDeleted_IsKept()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.20.0.0/16"])),
            [Linked(1, "app", "10.20.1.0", 24, SubnetId("vnet-a", "sn-a"))]);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [SubnetId("vnet-a", "sn-a")] = AzureResourceConfirmation.Deleted
        });

        Assert.Single(plan.Items);
    }

    [Fact]
    public void AnAncestorOfALiveAzureLinkedDescendant_IsStillOffered()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.21.0.0/16"], AzSubnet("vnet-a", "sn-live", "10.21.1.0/24"))),
            [
                Linked(1, "parent", "10.21.0.0", 16, VNetId("vnet-b"), descendantIds: [2]),
                Linked(2, "child", "10.21.1.0", 24, SubnetId("vnet-a", "sn-live"))
            ]);

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(1, item.SubnetId);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("still exist in Azure"));
    }

    [Fact]
    public void AnAncestorOfADescendantLinkedToAnotherSubscription_IsStillOffered()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", ["192.168.0.0/16"])),
            [
                Linked(1, "parent", "10.40.0.0", 16, VNetId("vnet-a"), descendantIds: [2]),
                Linked(2, "child", "10.40.1.0", 24,
                    "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/v/subnets/s")
            ]);

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(1, item.SubnetId);
        Assert.Equal(AzureReconcileStatus.VNetDeleted, item.Status);
    }

    [Fact]
    public void AnAncestorOfADescendantConfirmedStillLive_IsStillOffered()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", ["192.168.0.0/16"])),
            [
                Linked(1, "parent", "10.41.0.0", 16, VNetId("vnet-a"), descendantIds: [2]),
                Linked(2, "child", "10.41.1.0", 24, SubnetId("vnet-a", "sn-a"))
            ]);

        Assert.Equal(2, plan.Items.Count);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("vnet-a")] = AzureResourceConfirmation.Deleted,
            [SubnetId("vnet-a", "sn-a")] = AzureResourceConfirmation.Live
        });

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(1, item.SubnetId);
    }

    [Fact]
    public void AnAncestorOfAManuallyCreatedDescendant_IsStillWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.22.0.0/16"])),
            [
                Linked(1, "parent", "10.22.0.0", 16, VNetId("vnet-b"), descendantIds: [2]),
                Linked(2, "child", "10.22.1.0", 24, VNetId("vnet-c"), manualDescendants: 1)
            ]);

        Assert.Empty(plan.Items);
        Assert.Contains(plan.Warnings, w => w.Contains("manually created content"));
    }

    [Fact]
    public void AFailedScan_ReportsNothingAsDeleted()
    {
        AzureReconcilePlanViewModel plan = _reconciler.BuildPlan(
            SubId,
            new AzureVNetInventory { Success = false, ErrorMessage = "boom" },
            [Linked(1, "app", "10.22.1.0", 24, SubnetId("vnet-a", "sn-a"))]);

        Assert.False(plan.ScanSucceeded);
        Assert.Empty(plan.Items);
        Assert.Single(plan.GlobalErrors);
    }

    [Fact]
    public void AnEmptySubscription_WarnsBeforeDeletingEverything()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(),
            [Linked(1, "app", "10.23.1.0", 24, SubnetId("vnet-a", "sn-a"))]);

        Assert.Single(plan.Items);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [SubnetId("vnet-a", "sn-a")] = AzureResourceConfirmation.Deleted
        });

        Assert.Single(plan.Items);
        Assert.Contains(plan.Warnings, w => w.Contains("no VNets at all"));
    }

    [Fact]
    public void AnEmptySubscription_WhereEveryFlaggedRowIsThenWithheld_DoesNotWarnThatRowsBelowAreGone()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(),
            [Linked(1, "app", "10.23.1.0", 24, SubnetId("vnet-a", "sn-a"))]);

        Assert.Single(plan.Items);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [SubnetId("vnet-a", "sn-a")] = AzureResourceConfirmation.NotVisible
        });

        Assert.Empty(plan.Items);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("no VNets at all"));
        Assert.Contains(plan.Warnings, w => w.Contains("denied access"));
    }

    [Fact]
    public void AHeldPrefixChangedVNetRow_DoesNotNameTheDeleteRemedyItIsWithheldFrom()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.60.0.0/16"])),
            [Linked(1, "target", "10.61.0.0", 16, VNetId("vnet-a"), manualDescendants: 1)]);

        AzureReconcileItem held = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.HeldByManualContent, held.Status);
        Assert.Contains("still exists but no longer has the address prefix", held.Reason);
        Assert.DoesNotContain("Delete it here if you want to", held.Reason);
        Assert.DoesNotContain("import wizard", held.Reason);
        Assert.Contains("Delete it here first, then run the scan again.", held.Reason);
    }

    [Fact]
    public void AHeldPrefixChangedSubnetRow_DoesNotNameTheDeleteRemedyItIsWithheldFrom()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.62.0.0/16"], AzSubnet("vnet-a", "sn-a", "10.62.9.0/24"))),
            [Linked(1, "app", "10.62.1.0", 24, SubnetId("vnet-a", "sn-a"), hostIps: 2)]);

        AzureReconcileItem held = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.HeldByManualContent, held.Status);
        Assert.Contains("still exists but its address prefix is now", held.Reason);
        Assert.DoesNotContain("Delete it here if you want to", held.Reason);
        Assert.DoesNotContain("import wizard", held.Reason);
        Assert.Contains("Delete it here first, then run the scan again.", held.Reason);
    }

    [Fact]
    public void AHeldSubnetRowMerelyAbsentFromTheListing_DoesNotAssertItNoLongerExists()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", ["192.168.0.0/16"])),
            [Linked(1, "app", "10.63.1.0", 24, SubnetId("vnet-a", "sn-a"), manualDescendants: 1)]);

        AzureReconcileItem held = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.HeldByManualContent, held.Status);
        Assert.DoesNotContain("no longer exists", held.Reason);
        Assert.Contains("could not be found in this subscription's listing", held.Reason);
    }

    [Fact]
    public void ASubnetRowLinkedWithDifferentIdCasing_IsNotReportedAsDeleted()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.64.0.0/16"], AzSubnet("vnet-a", "sn-a", "10.64.1.0/24"))),
            [Linked(1, "app", "10.64.1.0", 24, SubnetId("vnet-a", "sn-a").ToUpperInvariant())]);

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void AVNetTargetLinkedWithDifferentIdCasing_IsNotReportedAsDeleted()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.65.0.0/16"])),
            [Linked(1, "target", "10.65.0.0", 16, VNetId("vnet-a").ToUpperInvariant())]);

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void AHeldRowWhoseVNetIsMerelyAbsentFromTheListing_DoesNotAssertItNoLongerExists()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", ["192.168.0.0/16"])),
            [Linked(1, "target", "10.30.0.0", 16, VNetId("vnet-a"), manualDescendants: 1)]);

        AzureReconcileItem held = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.HeldByManualContent, held.Status);
        Assert.DoesNotContain("no longer exists", held.Reason);
        Assert.Contains("could not be found in this subscription's listing", held.Reason);
    }

    [Theory]
    [InlineData("not-an-arm-id")]
    [InlineData("/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/x")]
    public void AResourceIdThatNamesNeitherAVNetNorASubnet_IsReviewedNotDeleted(string resourceId)
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.24.0.0/16"])),
            [Linked(1, "app", "10.24.1.0", 24, resourceId)]);

        Assert.Empty(plan.Items);
        AzureReconcileItem reviewed = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.UnrecognisedResourceId, reviewed.Status);
        Assert.DoesNotContain("Correct or clear", reviewed.Reason);
        Assert.Contains("will not offer it for deletion", reviewed.Reason);
    }

    [Fact]
    public void ASubnetLinkedToAnotherSubscription_IsNotTouched()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.25.0.0/16"])),
            [Linked(1, "app", "10.25.1.0", 24,
                "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/v/subnets/s")]);

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    // Reconcile does not go looking for things to import. Finding un-imported Azure ranges is
    // the bulk import wizard's job, and reconcile reporting them produces rows the operator
    // cannot act on from the reconcile screen.

    [Fact]
    public void AnAzureRangeNoBastetRowRecords_IsNotReportedByReconcile()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.26.0.0/16"], AzSubnet("vnet-a", "sn-a", "10.26.1.0/24"))),
            []);

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void AVNetNothingIsLinkedTo_IsNotReportedByReconcile()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-unlinked", ["10.27.0.0/16"], AzSubnet("vnet-unlinked", "sn-x", "10.27.1.0/24"))),
            []);

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    private static BulkAzureSubnetViewModel AzSubnetWithNoIpv4(string vnetName, string name) =>
        new() { ResourceId = SubnetId(vnetName, name), Name = name };

    [Fact]
    public void ASubnetThatLostItsIpv4Prefix_IsReportedAsARangeChange_NotAsDeleted()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.99.0.0/16"], AzSubnetWithNoIpv4("vnet-a", "dual"))),
            [Linked(1, "dual", "10.99.1.0", 24, SubnetId("vnet-a", "dual"))]);

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetPrefixChanged, item.Status);
        Assert.Contains("no longer has an IPv4 address prefix", item.Reason);
        Assert.DoesNotContain("import wizard", item.Reason);
        Assert.Contains("Delete it here", item.Reason);
        Assert.False(AzureReconciler.IsAbsenceStatus(item.Status));
    }

    [Fact]
    public void AVNetThatLostItsIpv4Space_IsReportedAsARangeChange_NotAsDeleted()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", [])),
            [Linked(1, "target", "10.99.0.0", 16, VNetId("vnet-a"))]);

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, item.Status);
        Assert.DoesNotContain("import wizard", item.Reason);
        Assert.Contains("Delete it here", item.Reason);
        Assert.False(AzureReconciler.IsAbsenceStatus(item.Status));
    }

    [Fact]
    public void TheVNetDeletedReason_NoLongerClaimsItMightJustHaveLostIpv4()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", ["192.168.0.0/16"])),
            [Linked(1, "target", "10.99.0.0", 16, VNetId("vnet-gone"))]);

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetDeleted, item.Status);
        Assert.DoesNotContain("IPv4 address space", item.Reason);
    }
}
