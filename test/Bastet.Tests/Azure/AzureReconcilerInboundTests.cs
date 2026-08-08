using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;

namespace Bastet.Tests.Azure;

public class AzureReconcilerInboundTests
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

    private static ExistingSubnetSnapshot Existing(
        int id, string network, int cidr, string? azureResourceId = null) =>
        new()
        {
            Id = id,
            Name = $"subnet-{id}",
            NetworkAddress = network,
            Cidr = cidr,
            AzureResourceId = azureResourceId
        };

    private static ExistingSubnetSnapshot Target(int id, string network, int cidr, string vnetName) =>
        Existing(id, network, cidr, VNetId(vnetName));

    private AzureReconcilePlanViewModel Build(
        AzureVNetInventory inventory,
        IReadOnlyList<ExistingSubnetSnapshot> existing,
        params AzureLinkedSubnetSnapshot[] linked) =>
        _reconciler.BuildPlan(SubId, "Test Sub", inventory, linked, existing);

    private static List<AzureReconcileItem> Inbound(AzureReconcilePlanViewModel plan) =>
        [.. plan.ReviewItems.Where(i => i.Status == AzureReconcileStatus.AzureRangeNotImported)];

    [Fact]
    public void ARangeAzureAssignedThatNoBastetSubnetRecords_IsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"],
                AzSubnet("vnet-a", "sn-multi", "10.90.200.0/25", "10.90.77.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a"), Existing(2, "10.90.200.0", 25, SubnetId("vnet-a", "sn-multi"))]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.90.77.0", item.NetworkAddress);
        Assert.Equal(24, item.Cidr);
        Assert.Contains("no BASTET subnet records", item.Reason);
        Assert.Contains("free space", item.Reason);
    }

    [Fact]
    public void AnInboundReportIsNeverOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.90.77.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a")]);

        Assert.Single(Inbound(plan));
        Assert.Empty(plan.Items);
        Assert.Equal(0, Assert.Single(Inbound(plan)).SubnetId);
    }

    [Fact]
    public void AnInboundReportMeansTheScanHasSomethingToReport()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.90.77.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a")]);

        Assert.NotEmpty(plan.ReviewItems);
    }

    [Fact]
    public void ARangeInsideACoarserRowThatNothingRecords_IsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.90.77.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a"), Existing(2, "10.90.64.0", 18)]);

        Assert.Single(Inbound(plan));
    }

    [Fact]
    public void ARangeFullyCoveredByRowsInsideIt_IsNotReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.60.0.0/16"], AzSubnet("vnet-a", "sn-tiled", "10.60.1.0/24"))),
            [
                Target(1, "10.60.0.0", 16, "vnet-a"),
                Existing(2, "10.60.0.0", 18),
                Existing(3, "10.60.1.0", 25),
                Existing(4, "10.60.1.128", 25)
            ]);

        Assert.Empty(Inbound(plan));
    }

    [Fact]
    public void ARangeOnlyHalfCoveredByRowsInsideIt_IsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.62.0.0/16"], AzSubnet("vnet-a", "sn-half", "10.62.1.0/24"))),
            [
                Target(1, "10.62.0.0", 16, "vnet-a"),
                Existing(2, "10.62.0.0", 18),
                Existing(3, "10.62.1.0", 25)
            ]);

        Assert.Single(Inbound(plan));
    }

    [Fact]
    public void ARangeUnderAFullyAllocatedRow_IsNotReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.91.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.91.77.0/24"))),
            [
                Target(1, "10.91.0.0", 16, "vnet-a"),
                new ExistingSubnetSnapshot
                {
                    Id = 2,
                    Name = "reserved",
                    NetworkAddress = "10.91.64.0",
                    Cidr = 18,
                    IsFullyAllocated = true
                }
            ]);

        Assert.Empty(Inbound(plan));
    }

    [Fact]
    public void ARangeCreatedByHandCarryingNoAzureLink_IsNotReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.90.77.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a"), Existing(2, "10.90.77.0", 24, azureResourceId: null)]);

        Assert.Empty(Inbound(plan));
    }

    [Fact]
    public void ARangeRecordedExactly_IsNotReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-a", "10.90.1.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a"), Existing(2, "10.90.1.0", 24, SubnetId("vnet-a", "sn-a"))]);

        Assert.Empty(Inbound(plan));
    }

    [Fact]
    public void AVNetThatWasNeverImported_ProducesNoInboundReports()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(
                VNet("vnet-imported", ["10.90.0.0/16"], AzSubnet("vnet-imported", "sn-a", "10.90.1.0/24")),
                VNet("vnet-untouched", ["10.99.0.0/16"],
                    AzSubnet("vnet-untouched", "sn-x", "10.99.1.0/24"),
                    AzSubnet("vnet-untouched", "sn-y", "10.99.2.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-imported"), Existing(2, "10.90.1.0", 24, SubnetId("vnet-imported", "sn-a"))]);

        Assert.Empty(Inbound(plan));
    }

    [Fact]
    public void OnlyTheUnaccountedPrefixOfAMultiPrefixSubnetIsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"],
                AzSubnet("vnet-a", "sn-multi", "10.90.1.0/24", "10.90.2.0/24", "10.90.3.0/24"))),
            [
                Target(1, "10.90.0.0", 16, "vnet-a"),
                Existing(2, "10.90.1.0", 24, SubnetId("vnet-a", "sn-multi")),
                Existing(3, "10.90.2.0", 24, SubnetId("vnet-a", "sn-multi"))
            ]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.90.3.0", item.NetworkAddress);
    }

    [Fact]
    public void AnAzureSubnetCoveringTheWholeVNetPrefix_IsAccountedForByTheTargetItself()
    {
        ExistingSubnetSnapshot target = Target(1, "10.90.0.0", 16, "vnet-a");
        target.IsFullyAllocated = true;

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-whole", "10.90.0.0/16"))),
            [target]);

        Assert.Empty(Inbound(plan));
    }

    [Fact]
    public void ATargetLinkedButNotFullyAllocated_DoesNotAccountForTheWholePrefixSubnet()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.61.0.0/24"], AzSubnet("vnet-a", "sn-whole", "10.61.0.0/24"))),
            [Target(1, "10.61.0.0", 24, "vnet-a")]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.61.0.0", item.NetworkAddress);
        Assert.Equal(24, item.Cidr);
    }

    [Fact]
    public void AnUnaccountedWholePrefixRange_NamesTheImportRemedyInItsReason()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.61.0.0/24"], AzSubnet("vnet-a", "sn-whole", "10.61.0.0/24"))),
            [Target(1, "10.61.0.0", 24, "vnet-a")]);

        Assert.Contains("fully allocated", Assert.Single(Inbound(plan)).Reason);
    }

    [Fact]
    public void AnUnaccountedWholePrefixRangeOnAPopulatedTarget_SaysTheChildMustGoFirst()
    {
        ExistingSubnetSnapshot target = Target(1, "10.61.0.0", 24, "vnet-a");
        target.HasChildSubnets = true;

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.61.0.0/24"], AzSubnet("vnet-a", "sn-whole", "10.61.0.0/24"))),
            [target, Existing(2, "10.61.0.0", 25)]);

        Assert.Contains("child", Assert.Single(Inbound(plan)).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ATargetContainingTheRangeIsNotEnough_OrTheCheckWouldBeVacuous()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.90.77.0/24"))),

            [Target(1, "10.90.0.0", 16, "vnet-a")]);

        Assert.Single(Inbound(plan));
    }

    [Fact]
    public void AnAncestorAboveTheImportTarget_DoesNotAccountForRangesInsideIt()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.20.0.0/16"], AzSubnet("vnet-a", "sn-unrecorded", "10.20.20.0/24"))),
            [
                Existing(1, "10.0.0.0", 8),
                Target(2, "10.20.0.0", 16, "vnet-a")
            ]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.20.20.0", item.NetworkAddress);
        Assert.Equal(24, item.Cidr);
    }

    [Fact]
    public void ARowExactlyTheSizeOfTheVNetPrefix_DoesNotAccountForRangesInsideIt()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.20.0.0/16"], AzSubnet("vnet-a", "sn-unrecorded", "10.20.20.0/24"))),
            [
                Existing(1, "10.20.0.0", 16),
                Target(2, "10.20.0.0", 16, "vnet-a")
            ]);

        Assert.Single(Inbound(plan));
    }

    [Fact]
    public void AHandReserveInsideTheVNetPrefixWithNothingUnderIt_IsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.20.0.0/16"], AzSubnet("vnet-a", "sn-unrecorded", "10.20.20.0/24"))),
            [
                Target(1, "10.20.0.0", 16, "vnet-a"),
                Existing(2, "10.20.16.0", 20)
            ]);

        Assert.Single(Inbound(plan));
    }

    [Fact]
    public void ARangeOutsideEveryVNetPrefixThatNothingRecords_IsReported()
    {
        AzureReconcilePlanViewModel plan = Build(

            Live(VNet("vnet-a", ["10.20.0.0/16"], AzSubnet("vnet-a", "sn-outside", "172.16.5.0/24"))),
            [
                Target(1, "10.20.0.0", 16, "vnet-a"),
                Existing(2, "172.16.0.0", 16)
            ]);

        Assert.Single(Inbound(plan));
    }

    [Fact]
    public void ARangeNoBastetSubnetContains_IsNotDescribedAsFreeSpace()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.20.0.0/16"], AzSubnet("vnet-a", "sn-outside", "172.16.5.0/24"))),
            [Target(1, "10.20.0.0", 16, "vnet-a")]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.DoesNotContain("free space", item.Reason);
        Assert.EndsWith("which no BASTET subnet records.", item.Reason);
    }

    [Fact]
    public void AVNetPrefixNoBastetSubnetContains_IsNotDescribedAsFreeSpace()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.101.0.0/16", "10.102.0.0/16"])),
            [Target(1, "10.101.0.0", 16, "vnet-a")]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.102.0.0", item.NetworkAddress);
        Assert.DoesNotContain("free space", item.Reason);
    }

    [Fact]
    public void AVNetPrefixInsideAHandHeldSupernet_IsStillDescribedAsFreeSpace()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.101.0.0/16", "10.102.0.0/16"])),
            [Target(1, "10.101.0.0", 16, "vnet-a"), Existing(2, "10.0.0.0", 8)]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.102.0.0", item.NetworkAddress);
        Assert.Contains("reporting that range as free space", item.Reason);
    }

    [Fact]
    public void AMultiPrefixVNet_ScopesTheContainmentTestToThePrefixHoldingTheRange()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["192.168.100.0/24", "10.20.0.0/16"],
                AzSubnet("vnet-a", "sn-unrecorded", "10.20.20.0/24"))),
            [
                Existing(1, "10.0.0.0", 8),
                Target(2, "10.20.0.0", 16, "vnet-a")
            ]);

        Assert.Single(Inbound(plan), i => i.NetworkAddress == "10.20.20.0" && i.Cidr == 24);
    }

    [Fact]
    public void TheRealMultiPrefixInventoryShape_ReportsEachRangeExactlyOnce()
    {

        string[] all = ["10.90.200.0/25", "10.90.200.128/25", "10.90.77.0/24"];
        BulkAzureSubnetViewModel Row(string primary) => new()
        {
            ResourceId = SubnetId("vnet-a", "sn-multi"),
            Name = "sn-multi",
            AddressPrefix = primary,
            Ipv4AddressPrefixes = [.. all]
        };

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], Row(all[0]), Row(all[1]), Row(all[2]))),
            [
                Target(1, "10.90.0.0", 16, "vnet-a"),
                Existing(2, "10.90.200.0", 25, SubnetId("vnet-a", "sn-multi")),
                Existing(3, "10.90.200.128", 25, SubnetId("vnet-a", "sn-multi"))
            ]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.90.77.0", item.NetworkAddress);
    }

    [Fact]
    public void AFailedScan_ProducesNoInboundReports()
    {
        AzureReconcilePlanViewModel plan = _reconciler.BuildPlan(
            SubId, "Test Sub",
            new AzureVNetInventory { Success = false, ErrorMessage = "boom" },
            [],
            [Target(1, "10.90.0.0", 16, "vnet-a")]);

        Assert.Empty(plan.ReviewItems);
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void AWholePrefixTargetWithHostIps_IsToldToRemoveThemRatherThanToImport()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.61.0.0/24"], AzSubnet("vnet-a", "sn-enc", "10.61.0.0/24"))),
            [
                new ExistingSubnetSnapshot
                {
                    Id = 1,
                    Name = "rig-enc",
                    NetworkAddress = "10.61.0.0",
                    Cidr = 24,
                    AzureResourceId = VNetId("vnet-a"),
                    HasHostIpAssignments = true
                }
            ]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Contains("refused while it has host IP assignments", item.Reason);
        Assert.DoesNotContain("Import 'sn-enc' to mark", item.Reason);
    }

    [Fact]
    public void AWholePrefixTargetWithNothingInIt_IsStillToldToImport()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.63.0.0/24"], AzSubnet("vnet-a", "sn-enc", "10.63.0.0/24"))),
            [Target(1, "10.63.0.0", 24, "vnet-a")]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Contains("Import 'sn-enc' to mark 'subnet-1' fully allocated", item.Reason);
    }

    [Fact]
    public void AWholePrefixTargetWithChildren_StillReportsTheChildrenBlocker()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.64.0.0/24"], AzSubnet("vnet-a", "sn-enc", "10.64.0.0/24"))),
            [
                new ExistingSubnetSnapshot
                {
                    Id = 1,
                    Name = "rig-enc",
                    NetworkAddress = "10.64.0.0",
                    Cidr = 24,
                    AzureResourceId = VNetId("vnet-a"),
                    HasChildSubnets = true
                }
            ]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Contains("refused while it still has child subnets", item.Reason);
    }
    [Fact]
    public void AVNetAddressPrefixNoBastetRowRecords_IsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.196.0.0/16", "10.197.0.0/16"],
                AzSubnet("vnet-a", "s1", "10.196.1.0/24"))),
            [
                Existing(1, "10.0.0.0", 8),
                Target(2, "10.196.0.0", 16, "vnet-a"),
                Existing(3, "10.196.1.0", 24, SubnetId("vnet-a", "s1"))
            ]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.197.0.0", item.NetworkAddress);
        Assert.Equal(16, item.Cidr);
        Assert.True(item.IsVNetLevel);
    }

    [Fact]
    public void AVNetAddressPrefixAnImportedRowRecordsExactly_IsNotReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.196.0.0/16"], AzSubnet("vnet-a", "s1", "10.196.1.0/24"))),
            [
                Target(1, "10.196.0.0", 16, "vnet-a"),
                Existing(2, "10.196.1.0", 24, SubnetId("vnet-a", "s1"))
            ]);

        Assert.Empty(Inbound(plan));
    }

    [Fact]
    public void AnEmptyVNetImportedAtVNetLevel_IsNotReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-empty", ["10.105.0.0/16"])),
            [Target(1, "10.105.0.0", 16, "vnet-empty")]);

        Assert.Empty(Inbound(plan));
    }

    [Fact]
    public void AVNetPrefixAddedAfterImport_IsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.196.0.0/16", "10.198.0.0/16"])),
            [Target(1, "10.196.0.0", 16, "vnet-a")]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.198.0.0", item.NetworkAddress);
    }

    [Fact]
    public void ASubnetRangeInsideAnAlreadyReportedVNetPrefix_IsNotReportedTwice()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.196.0.0/16", "10.197.0.0/16"],
                AzSubnet("vnet-a", "inner", "10.197.5.0/24"))),
            [Target(1, "10.196.0.0", 16, "vnet-a")]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.197.0.0", item.NetworkAddress);
    }
    [Fact]
    public void AFullyTiledTargetIsStillReported_ButNotAsFreeSpace()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.171.0.0/24"], AzSubnet("vnet-a", "whole", "10.171.0.0/24"))),
            [
                Target(1, "10.171.0.0", 24, "vnet-a"),
                Existing(2, "10.171.0.0", 25),
                Existing(3, "10.171.0.128", 25)
            ]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.DoesNotContain("reporting that range as free space", item.Reason);
        Assert.Contains("holds exactly that range but is not marked fully allocated", item.Reason);
    }

    [Fact]
    public void ARangeWhoseOnlyUncoveredAddressIsItsOwnNetworkAddress_IsNotReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-c", ["10.100.0.0/16"], AzSubnet("vnet-c", "default", "10.100.1.0/24"))),
            [
                Target(1, "10.100.0.0", 16, "vnet-c"),
                Existing(2, "10.100.1.1", 32),
                Existing(3, "10.100.1.2", 31),
                Existing(4, "10.100.1.4", 30),
                Existing(5, "10.100.1.8", 29),
                Existing(6, "10.100.1.16", 28),
                Existing(7, "10.100.1.32", 27),
                Existing(8, "10.100.1.64", 26),
                Existing(9, "10.100.1.128", 25)
            ]);

        Assert.Empty(Inbound(plan));
    }

    [Fact]
    public void ATiledTargetWhoseOnlyGapIsItsOwnNetworkAddress_IsNotDescribedAsFreeSpace()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-e", ["10.173.0.0/24"], AzSubnet("vnet-e", "whole", "10.173.0.0/24"))),
            [
                Target(1, "10.173.0.0", 24, "vnet-e"),
                Existing(2, "10.173.0.1", 32),
                Existing(3, "10.173.0.2", 31),
                Existing(4, "10.173.0.4", 30),
                Existing(5, "10.173.0.8", 29),
                Existing(6, "10.173.0.16", 28),
                Existing(7, "10.173.0.32", 27),
                Existing(8, "10.173.0.64", 26),
                Existing(9, "10.173.0.128", 25)
            ]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.DoesNotContain("reporting that range as free space", item.Reason);
    }

    [Fact]
    public void ARangeWithGenuinelyFreeAddressesLeftInIt_IsStillReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-d", ["10.110.0.0/16"], AzSubnet("vnet-d", "default", "10.110.1.0/24"))),
            [
                Target(1, "10.110.0.0", 16, "vnet-d"),
                Existing(2, "10.110.1.1", 32),
                Existing(3, "10.110.1.2", 31),
                Existing(4, "10.110.1.4", 30)
            ]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.110.1.0", item.NetworkAddress);
    }

    [Fact]
    public void AChildlessWholePrefixTarget_IsStillReportedAsFreeSpace()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-b", ["10.172.0.0/24"], AzSubnet("vnet-b", "whole", "10.172.0.0/24"))),
            [Target(1, "10.172.0.0", 24, "vnet-b")]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Contains("reporting that range as free space", item.Reason);
    }

    [Fact]
    public void AnItemNamingTheSubnetThatHoldsTheRange_DoesNotAlsoSayNoSubnetRecordsIt()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-g", ["10.134.0.0/24"], AzSubnet("vnet-g", "whole", "10.134.0.0/24"))),
            [Target(1, "10.134.0.0", 24, "vnet-g")]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.DoesNotContain("no BASTET subnet records", item.Reason);
        Assert.Contains("BASTET subnet 'subnet-1' holds exactly that range", item.Reason);
    }
}
