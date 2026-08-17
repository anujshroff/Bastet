using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

public class AzureBulkImportChildNamingTests
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";

    private static string VNetId(string name) =>
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/{name}";

    private static string SubnetId(string vnetName, string subnetName) =>
        $"{VNetId(vnetName)}/subnets/{subnetName}";

    private readonly AzureBulkImportPlanner _planner =
        new(new IpUtilityService(), new InputSanitizationService());

    private static BulkAzureVNetViewModel MultiPrefixVNet(params BulkAzureSubnetViewModel[] subnets) =>
        new()
        {
            ResourceId = VNetId("multi-vnet"),
            Name = "multi-vnet",
            Ipv4AddressPrefixes = ["10.81.0.0/16"],
            Subnets = [.. subnets]
        };

    private static BulkAzureSubnetViewModel MultiPrefixRow(string prefix) =>
        new()
        {
            ResourceId = SubnetId("multi-vnet", "snet-mp"),
            Name = "snet-mp",
            AddressPrefix = prefix,
            Ipv4AddressPrefixes = ["10.81.1.0/24", "10.81.2.0/24"]
        };

    private static ExistingSubnetSnapshot Row(int id, string name, string network, int cidr, string? link) =>
        new()
        {
            Id = id,
            Name = name,
            NetworkAddress = network,
            Cidr = cidr,
            AzureResourceId = link
        };

    private ExistingSubnetSnapshot[] ImportedMultiPrefixRows(string firstName = "snet-mp (10.81.1.0-24)") =>
    [
        Row(1, "multi-vnet", "10.81.0.0", 16, VNetId("multi-vnet")),
        Row(2, firstName, "10.81.1.0", 24, SubnetId("multi-vnet", "snet-mp")),
        Row(3, "snet-mp (10.81.2.0-24)", "10.81.2.0", 24, SubnetId("multi-vnet", "snet-mp"))
    ];

    [Fact]
    public void ASingleSelectedRowOfAMultiPrefixSubnet_IsStillQualified()
    {
        BulkImportPlanViewModel plan = _planner.BuildPlan(
            new BulkImportSelectionDto
            {
                SubscriptionId = "sub-1",
                SubscriptionName = "Test Sub",
                RenameMatchedBastetSubnets = false,
                VNetPrefixes =
                [
                    new BulkImportSelectedVNetPrefixDto
                    {
                        VNetName = "multi-vnet",
                        VNetResourceId = VNetId("multi-vnet"),
                        AddressPrefix = "10.81.0.0/16",
                        VNetIpv4AddressPrefixes = ["10.81.0.0/16"],
                        Subnets =
                        [
                            new BulkImportSelectedSubnetDto
                            {
                                Name = "snet-mp",
                                AddressPrefix = "10.81.1.0/24",
                                AzureResourceId = SubnetId("multi-vnet", "snet-mp"),
                                Ipv4AddressPrefixes = ["10.81.1.0/24", "10.81.2.0/24"]
                            }
                        ]
                    }
                ]
            },
            []);

        BulkImportPlannedChildSubnet child = Assert.Single(Assert.Single(plan.Items).ChildSubnets);
        Assert.Equal("snet-mp (10.81.1.0-24)", child.Name);
    }

    [Fact]
    public void AnImportedMultiPrefixSubnetRow_IsNotOfferedARename()
    {
        BulkAzureVNetViewModel vnet = MultiPrefixVNet(
            MultiPrefixRow("10.81.1.0/24"), MultiPrefixRow("10.81.2.0/24"));

        _planner.AnnotateAvailability([vnet], ImportedMultiPrefixRows());

        Assert.False(vnet.Subnets[0].WouldRenameSubnet);
        Assert.False(vnet.Subnets[1].WouldRenameSubnet);
    }

    [Fact]
    public void AGenuinelyDriftedMultiPrefixRow_IsStillOfferedARename()
    {
        BulkAzureVNetViewModel vnet = MultiPrefixVNet(
            MultiPrefixRow("10.81.1.0/24"), MultiPrefixRow("10.81.2.0/24"));

        _planner.AnnotateAvailability([vnet], ImportedMultiPrefixRows(firstName: "renamed-by-hand"));

        Assert.True(vnet.Subnets[0].WouldRenameSubnet);
        Assert.False(vnet.Subnets[1].WouldRenameSubnet);
    }

    [Fact]
    public void AnImportedSinglePrefixRowWithAMatchingName_IsNotOfferedARename()
    {
        BulkAzureVNetViewModel vnet = new()
        {
            ResourceId = VNetId("vnet-s"),
            Name = "vnet-s",
            Ipv4AddressPrefixes = ["10.82.0.0/16"],
            Subnets =
            [
                new BulkAzureSubnetViewModel
                {
                    ResourceId = SubnetId("vnet-s", "web"),
                    Name = "web",
                    AddressPrefix = "10.82.1.0/24",
                    Ipv4AddressPrefixes = ["10.82.1.0/24"]
                }
            ]
        };

        _planner.AnnotateAvailability([vnet],
        [
            Row(1, "vnet-s", "10.82.0.0", 16, VNetId("vnet-s")),
            Row(2, "web", "10.82.1.0", 24, SubnetId("vnet-s", "web"))
        ]);

        Assert.False(vnet.Subnets[0].WouldRenameSubnet);
    }

    [Fact]
    public void ADriftedSinglePrefixRow_IsStillOfferedARename()
    {
        BulkAzureVNetViewModel vnet = new()
        {
            ResourceId = VNetId("vnet-s"),
            Name = "vnet-s",
            Ipv4AddressPrefixes = ["10.82.0.0/16"],
            Subnets =
            [
                new BulkAzureSubnetViewModel
                {
                    ResourceId = SubnetId("vnet-s", "web"),
                    Name = "web",
                    AddressPrefix = "10.82.1.0/24",
                    Ipv4AddressPrefixes = ["10.82.1.0/24"]
                }
            ]
        };

        _planner.AnnotateAvailability([vnet],
        [
            Row(1, "vnet-s", "10.82.0.0", 16, VNetId("vnet-s")),
            Row(2, "old-name", "10.82.1.0", 24, SubnetId("vnet-s", "web"))
        ]);

        Assert.True(vnet.Subnets[0].WouldRenameSubnet);
    }
}
