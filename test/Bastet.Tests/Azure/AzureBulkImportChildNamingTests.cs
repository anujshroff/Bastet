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

    private static BulkImportSelectedSubnetDto Pick(string vnetName, string subnetName, string prefix) =>
        new()
        {
            Name = subnetName,
            AddressPrefix = prefix,
            AzureResourceId = SubnetId(vnetName, subnetName),
            Ipv4AddressPrefixes = [prefix]
        };

    private BulkImportPlanItem PlanOne(
        string vnetName, string vnetPrefix, bool renames,
        IReadOnlyList<ExistingSubnetSnapshot> existing, params BulkImportSelectedSubnetDto[] subnets) =>
        Assert.Single(_planner.BuildPlan(
            new BulkImportSelectionDto
            {
                RenameMatchedBastetSubnets = renames,
                VNetPrefixes =
                [
                    new BulkImportSelectedVNetPrefixDto
                    {
                        VNetName = vnetName,
                        VNetResourceId = VNetId(vnetName),
                        AddressPrefix = vnetPrefix,
                        VNetIpv4AddressPrefixes = [vnetPrefix],
                        Subnets = [.. subnets]
                    }
                ]
            },
            existing).Items);

    private static BulkAzureVNetViewModel SinglePrefixVNet(string vnetName, string vnetPrefix, string subnetName, string subnetPrefix) =>
        new()
        {
            ResourceId = VNetId(vnetName),
            Name = vnetName,
            Ipv4AddressPrefixes = [vnetPrefix],
            Subnets =
            [
                new BulkAzureSubnetViewModel
                {
                    ResourceId = SubnetId(vnetName, subnetName),
                    Name = subnetName,
                    AddressPrefix = subnetPrefix,
                    Ipv4AddressPrefixes = [subnetPrefix]
                }
            ]
        };

    [Theory]
    [InlineData("vnet-same", "vnet-same")]
    [InlineData("vnet-case", "VNET-CASE")]
    public void AChildNamedLikeItsVNet_IsCreatedUnderItsAzureName(string vnetName, string subnetName)
    {
        BulkImportPlanItem item = PlanOne(vnetName, "10.83.0.0/16", renames: false, [],
            Pick(vnetName, subnetName, "10.83.1.0/24"));

        Assert.Equal(subnetName, Assert.Single(item.ChildSubnets).Name);
    }

    [Fact]
    public void AChildNamedLikeAVNetCreatedInsideAHandBuiltContainer_IsCreatedUnderItsAzureName()
    {
        BulkImportPlanItem item = PlanOne("vnet-nest", "10.87.1.0/24", renames: false,
            [Row(1, "hand-container", "10.87.0.0", 16, null)], Pick("vnet-nest", "vnet-nest", "10.87.1.0/26"));

        Assert.Equal(BulkImportTargetType.AutoCreateChild, item.TargetType);
        Assert.Equal("vnet-nest", item.AutoCreateTargetName);
        Assert.Equal("vnet-nest", Assert.Single(item.ChildSubnets).Name);
    }

    [Fact]
    public void AChildNamedLikeTheHandBuiltRowItsVNetAdopts_IsCreatedUnderItsAzureName()
    {
        BulkImportPlanItem item = PlanOne("vnet-adopt", "10.84.0.0/16", renames: false,
            [Row(1, "Prod", "10.84.0.0", 16, null)], Pick("vnet-adopt", "prod", "10.84.1.0/24"));

        Assert.Equal(BulkImportTargetType.ExactMatch, item.TargetType);
        Assert.Equal("prod", Assert.Single(item.ChildSubnets).Name);
    }

    [Fact]
    public void AChildNamedLikeTheNameItsTargetIsRenamedTo_IsCreatedUnderItsAzureName()
    {
        BulkImportPlanItem item = PlanOne("vnet-r", "10.85.0.0/16", renames: true,
            [Row(1, "drifted", "10.85.0.0", 16, VNetId("vnet-r"))], Pick("vnet-r", "vnet-r", "10.85.1.0/24"));

        Assert.Equal("vnet-r", item.NewName);
        Assert.Equal("vnet-r", Assert.Single(item.ChildSubnets).Name);
    }

    [Fact]
    public void ALinkedChildStillCarryingTheOldVNetSuffix_IsRenamedToItsAzureName_AndIsThenNoLongerOffered()
    {
        const string vnet = "vnet-same";
        ExistingSubnetSnapshot target = Row(1, vnet, "10.86.0.0", 16, VNetId(vnet));
        target.HasChildSubnets = true;
        ExistingSubnetSnapshot suffixed = Row(2, $"{vnet} ({vnet})", "10.86.1.0", 24, SubnetId(vnet, vnet));

        BulkAzureVNetViewModel before = SinglePrefixVNet(vnet, "10.86.0.0/16", vnet, "10.86.1.0/24");
        _planner.AnnotateAvailability([before], [target, suffixed]);
        Assert.True(before.Subnets[0].WouldRenameSubnet);

        BulkImportPlannedChildSubnet rename = Assert.Single(
            PlanOne(vnet, "10.86.0.0/16", renames: true, [target, suffixed], Pick(vnet, vnet, "10.86.1.0/24")).ChildSubnets);
        Assert.True(rename.WillRename);
        Assert.Equal(2, rename.ExistingSubnetId);
        Assert.Equal(vnet, rename.Name);

        ExistingSubnetSnapshot renamed = Row(2, vnet, "10.86.1.0", 24, SubnetId(vnet, vnet));
        BulkAzureVNetViewModel after = SinglePrefixVNet(vnet, "10.86.0.0/16", vnet, "10.86.1.0/24");
        _planner.AnnotateAvailability([after], [target, renamed]);
        Assert.False(after.Subnets[0].WouldRenameSubnet);
        Assert.Empty(PlanOne(vnet, "10.86.0.0/16", renames: true, [target, renamed], Pick(vnet, vnet, "10.86.1.0/24")).ChildSubnets);
    }
}
