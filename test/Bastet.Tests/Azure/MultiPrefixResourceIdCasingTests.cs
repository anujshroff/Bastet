using Bastet.Controllers;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

public class MultiPrefixResourceIdCasingTests
{
    private const string VNet = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-a";
    private const string Lower = $"{VNet}/subnets/sn-multi";
    private static readonly string Upper = Lower.Replace("/subnets/", "/Subnets/");

    private static AzureImportSubnetViewModel Row(string network, int cidr, string resourceId) =>
        new() { Name = "sn-multi", NetworkAddress = network, Cidr = cidr, AzureResourceId = resourceId };

    [Fact]
    public void ResolveImportNames_QualifiesEveryRowWhateverTheResourceIdCasing()
    {
        Dictionary<int, string> names = SubnetController.ResolveImportNames(
        [
            Row("10.20.40.0", 24, Lower),
            Row("10.20.5.0", 24, Upper),
            Row("10.20.20.0", 24, Upper)
        ], []);

        Assert.Equal("sn-multi (10.20.40.0-24)", names[0]);
        Assert.Equal("sn-multi (10.20.5.0-24)", names[1]);
        Assert.Equal("sn-multi (10.20.20.0-24)", names[2]);
    }

    [Fact]
    public void ResolveImportNames_IsUnaffectedWhenEverySpellingMatches()
    {
        Dictionary<int, string> names = SubnetController.ResolveImportNames(
        [
            Row("10.20.40.0", 24, Lower),
            Row("10.20.5.0", 24, Lower)
        ], []);

        Assert.Equal("sn-multi (10.20.40.0-24)", names[0]);
        Assert.Equal("sn-multi (10.20.5.0-24)", names[1]);
    }

    [Fact]
    public void TheBulkPlanner_QualifiesEveryChildWhateverTheResourceIdCasing()
    {
        AzureBulkImportPlanner planner = new(new IpUtilityService(), new InputSanitizationService());

        BulkImportPlanViewModel plan = planner.BuildPlan(
            new BulkImportSelectionDto
            {
                SubscriptionId = "sub-1",
                VNetPrefixes =
                [
                    new BulkImportSelectedVNetPrefixDto
                    {
                        VNetName = "vnet-a",
                        VNetResourceId = VNet,
                        AddressPrefix = "10.20.0.0/16",
                        Subnets =
                        [
                            new BulkImportSelectedSubnetDto { Name = "sn-multi", AddressPrefix = "10.20.40.0/24", AzureResourceId = Lower },
                            new BulkImportSelectedSubnetDto { Name = "sn-multi", AddressPrefix = "10.20.5.0/24", AzureResourceId = Upper }
                        ]
                    }
                ]
            },
            []);

        List<string> names = [.. plan.Items.SelectMany(i => i.ChildSubnets).Select(c => c.Name)];

        Assert.Contains("sn-multi (10.20.40.0-24)", names);
        Assert.Contains("sn-multi (10.20.5.0-24)", names);
    }

    [Fact]
    public void ResolveImportNames_QualifiesARowWhosePersistedSiblingSharesItsAzureSubnet()
    {
        List<Subnet> persisted =
        [
            new() { Id = 2, Name = "sn-multi", NetworkAddress = "10.88.20.0", Cidr = 24, AzureResourceId = Lower }
        ];

        Dictionary<int, string> names = SubnetController.ResolveImportNames(
        [
            Row("10.88.21.0", 24, Lower)
        ], persisted);

        Assert.Equal("sn-multi (10.88.21.0-24)", names[0]);
    }

    [Fact]
    public void ResolveImportNames_LeavesAnOrdinaryImportUnqualified()
    {
        List<Subnet> persisted =
        [
            new() { Id = 2, Name = "unrelated", NetworkAddress = "10.88.20.0", Cidr = 24, AzureResourceId = Upper + "-other" }
        ];

        Dictionary<int, string> names = SubnetController.ResolveImportNames(
        [
            Row("10.88.21.0", 24, Lower)
        ], persisted);

        Assert.Equal("sn-multi", names[0]);
    }

    [Fact]
    public void ResolveImportNames_DoesNotTreatTheRowsOwnRecordAsASibling()
    {
        List<Subnet> persisted =
        [
            new() { Id = 2, Name = "sn-multi", NetworkAddress = "10.88.21.0", Cidr = 24, AzureResourceId = Lower }
        ];

        Dictionary<int, string> names = SubnetController.ResolveImportNames(
        [
            Row("10.88.21.0", 24, Lower)
        ], persisted);

        Assert.Equal("sn-multi", names[0]);
    }
}
