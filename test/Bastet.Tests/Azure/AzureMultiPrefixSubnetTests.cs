using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

public class AzureMultiPrefixSubnetTests
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";

    private static string VNetId(string name) =>
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/{name}";

    private static string SubnetId(string vnetName, string subnetName) =>
        $"{VNetId(vnetName)}/subnets/{subnetName}";

    [Fact]
    public void SubnetWithTwoIpv4Prefixes_ProducesOneRowPerPrefix()
    {
        List<BulkAzureSubnetViewModel> rows = AzureService.BuildInventorySubnetRows(
            SubnetId("multi-vnet", "sn-multi"), "sn-multi", ["10.31.0.0/24", "10.31.1.0/24"]);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["10.31.0.0/24", "10.31.1.0/24"], rows.Select(r => r.AddressPrefix));

        Assert.All(rows, r => Assert.Equal(SubnetId("multi-vnet", "sn-multi"), r.ResourceId));
        Assert.All(rows, r => Assert.Equal("sn-multi", r.Name));
    }

    [Fact]
    public void EveryExpandedRow_CarriesTheSubnetsCompletePrefixList()
    {
        List<BulkAzureSubnetViewModel> rows = AzureService.BuildInventorySubnetRows(
            SubnetId("multi-vnet", "sn-multi"), "sn-multi", ["10.31.0.0/24", "10.31.1.0/24"]);

        Assert.All(rows, r => Assert.Equal(["10.31.0.0/24", "10.31.1.0/24"], r.Ipv4AddressPrefixes));
    }

    [Fact]
    public void SubnetWithOneIpv4Prefix_IsUnchanged()
    {
        List<BulkAzureSubnetViewModel> rows = AzureService.BuildInventorySubnetRows(
            SubnetId("vnet1", "web"), "web", ["10.0.1.0/24"]);

        BulkAzureSubnetViewModel row = Assert.Single(rows);
        Assert.Equal("web", row.Name);
        Assert.Equal("10.0.1.0/24", row.AddressPrefix);
        Assert.Equal(["10.0.1.0/24"], row.Ipv4AddressPrefixes);
    }

    [Fact]
    public void SubnetWithNoIpv4Prefixes_ProducesNoRows()
        => Assert.Empty(AzureService.BuildInventorySubnetRows(SubnetId("vnet1", "v6"), "v6", []));

    [Fact]
    public void DuplicatePrefixesFromArm_AreCollapsed()
    {
        List<BulkAzureSubnetViewModel> rows = AzureService.BuildInventorySubnetRows(
            SubnetId("vnet1", "web"), "web", ["10.0.1.0/24", "10.0.1.0/24"]);

        Assert.Single(rows);
    }

    private static readonly AzureBulkImportPlanner _planner =
        new(new IpUtilityService(), new InputSanitizationService());

    private static BulkImportSelectedSubnetDto Sub(string name, string prefix, string? resourceId = null) =>
        new() { Name = name, AddressPrefix = prefix, AzureResourceId = resourceId ?? string.Empty };

    private static BulkImportSelectionDto Sel(params BulkImportSelectedSubnetDto[] subs) =>
        new()
        {
            SubscriptionId = "sub-1",
            SubscriptionName = "Test Sub",
            VNetPrefixes =
            [
                new()
                {
                    VNetName = "multi-vnet",
                    VNetResourceId = VNetId("multi-vnet"),
                    AddressPrefix = "10.31.0.0/16",
                    Subnets = [.. subs]
                }
            ]
        };

    [Fact]
    public void BothPrefixesOfOneSubnet_ProduceDistinctlyNamedChildren()
    {
        string id = SubnetId("multi-vnet", "sn-multi");

        BulkImportPlanViewModel plan = _planner.BuildPlan(
            Sel(Sub("sn-multi", "10.31.0.0/24", id), Sub("sn-multi", "10.31.1.0/24", id)), []);

        BulkImportPlanItem item = Assert.Single(plan.Items);
        Assert.Equal(2, item.ChildSubnets.Count);

        Assert.Equal(
            ["sn-multi (10.31.0.0-24)", "sn-multi (10.31.1.0-24)"],
            item.ChildSubnets.Select(c => c.Name).Order());

        Assert.All(item.ChildSubnets, c => Assert.Equal("sn-multi", c.OriginalAzureName));
        Assert.All(item.ChildSubnets, c => Assert.Equal(id, c.AzureResourceId));
    }

    [Fact]
    public void SinglePrefixSubnet_KeepsItsPlainAzureName()
    {
        BulkImportPlanViewModel plan = _planner.BuildPlan(
            Sel(Sub("web", "10.31.1.0/24", SubnetId("multi-vnet", "web"))), []);

        Assert.Equal("web", Assert.Single(Assert.Single(plan.Items).ChildSubnets).Name);
    }

    [Fact]
    public void TwoDistinctAzureSubnetsSharingAName_StillUseTheExistingDisambiguation()
    {
        BulkImportPlanViewModel plan = _planner.BuildPlan(
            Sel(Sub("web", "10.31.1.0/24", SubnetId("multi-vnet", "web-a")),
                Sub("web", "10.31.2.0/24", SubnetId("multi-vnet", "web-b"))), []);

        List<string> names = [.. Assert.Single(plan.Items).ChildSubnets.Select(c => c.Name)];
        Assert.Equal(2, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("web", names);
    }

    private static BulkAzureVNetViewModel InventoryVNet(params BulkAzureSubnetViewModel[] subnets) =>
        new()
        {
            ResourceId = VNetId("multi-vnet"),
            Name = "multi-vnet",
            Ipv4AddressPrefixes = ["10.31.0.0/16"],
            Subnets = [.. subnets]
        };

    [Fact]
    public void ImportingOnePrefix_LeavesTheSiblingPrefixAvailable()
    {
        string id = SubnetId("multi-vnet", "sn-multi");
        List<BulkAzureSubnetViewModel> rows =
            AzureService.BuildInventorySubnetRows(id, "sn-multi", ["10.31.0.0/24", "10.31.1.0/24"]);

        List<ExistingSubnetSnapshot> existing =
        [
            new() { Id = 7, Name = "sn-multi (10.31.0.0-24)", NetworkAddress = "10.31.0.0", Cidr = 24, AzureResourceId = id }
        ];

        _planner.AnnotateAvailability([InventoryVNet([.. rows])], existing);

        BulkAzureSubnetViewModel imported = rows.Single(r => r.AddressPrefix == "10.31.0.0/24");
        BulkAzureSubnetViewModel sibling = rows.Single(r => r.AddressPrefix == "10.31.1.0/24");

        Assert.Equal(BulkImportAvailability.AlreadyImported, imported.Status);
        Assert.False(imported.IsSelectable);

        Assert.Equal(BulkImportAvailability.Available, sibling.Status);
        Assert.True(sibling.IsSelectable);
    }

    [Fact]
    public void PrefixOccupiedByAnUnrelatedBastetSubnet_IsStillBlocked()
    {
        string id = SubnetId("multi-vnet", "sn-multi");
        List<BulkAzureSubnetViewModel> rows =
            AzureService.BuildInventorySubnetRows(id, "sn-multi", ["10.31.0.0/24", "10.31.1.0/24"]);

        List<ExistingSubnetSnapshot> existing =
        [
            new() { Id = 9, Name = "hand-made", NetworkAddress = "10.31.1.0", Cidr = 24 }
        ];

        _planner.AnnotateAvailability([InventoryVNet([.. rows])], existing);

        BulkAzureSubnetViewModel blocked = rows.Single(r => r.AddressPrefix == "10.31.1.0/24");
        Assert.Equal(BulkImportAvailability.Blocked, blocked.Status);
        Assert.False(blocked.IsSelectable);
    }

    [Fact]
    public void ReconcilerSeesNoDrift_ForARowLinkedAtTheSecondPrefix()
    {
        string id = SubnetId("multi-vnet", "sn-multi");
        List<BulkAzureSubnetViewModel> rows =
            AzureService.BuildInventorySubnetRows(id, "sn-multi", ["10.31.0.0/24", "10.31.1.0/24"]);

        AzureVNetInventory inventory = new() { Success = true, VNets = [InventoryVNet([.. rows])] };

        List<AzureLinkedSubnetSnapshot> linked =
        [
            new()
            {
                Id = 7,
                Name = "sn-multi (10.31.1.0-24)",
                NetworkAddress = "10.31.1.0",
                Cidr = 24,
                AzureResourceId = id
            }
        ];

        AzureReconcilePlanViewModel plan =
            new AzureReconciler(new IpUtilityService()).BuildPlan(SubId, "Test Sub", inventory, linked, []);

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void ReconcilerStillReportsDrift_WhenThePrefixIsGenuinelyGone()
    {
        string id = SubnetId("multi-vnet", "sn-multi");
        List<BulkAzureSubnetViewModel> rows =
            AzureService.BuildInventorySubnetRows(id, "sn-multi", ["10.31.0.0/24", "10.31.1.0/24"]);

        AzureVNetInventory inventory = new() { Success = true, VNets = [InventoryVNet([.. rows])] };

        List<AzureLinkedSubnetSnapshot> linked =
        [
            new()
            {
                Id = 8,
                Name = "stale",
                NetworkAddress = "10.31.9.0",
                Cidr = 24,
                AzureResourceId = id
            }
        ];

        AzureReconcilePlanViewModel plan =
            new AzureReconciler(new IpUtilityService()).BuildPlan(SubId, "Test Sub", inventory, linked, []);

        Assert.Equal(AzureReconcileStatus.SubnetPrefixChanged, Assert.Single(plan.Items).Status);
    }
}
