using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

public class GeneratedNameSafeTextTests
{
    private const string VNetA = "/subscriptions/test/providers/Microsoft.Network/virtualNetworks/vnet-a";
    private const string MultiPrefixSubnet = $"{VNetA}/subnets/sn-multi";

    private readonly InputSanitizationService _sanitizer = new();

    private static BulkImportSelectedSubnetDto Sub(string name, string prefix) =>
        new() { Name = name, AddressPrefix = prefix, AzureResourceId = MultiPrefixSubnet };

    [Fact]
    public void EveryNameTheBulkPlannerGenerates_SatisfiesTheAppsOwnInputRules()
    {
        AzureBulkImportPlanner planner = new(new IpUtilityService(), _sanitizer);

        BulkImportPlanViewModel plan = planner.BuildPlan(
            new BulkImportSelectionDto
            {
                VNetPrefixes =
                [
                    new BulkImportSelectedVNetPrefixDto
                    {
                        VNetName = "vnet-a",
                        VNetResourceId = VNetA,
                        AddressPrefix = "10.20.0.0/16",
                        Subnets =
                        [
                            Sub("sn-multi", "10.20.40.0/24"),
                            Sub("sn-multi", "10.20.5.0/24"),
                            Sub("sn-multi", "10.20.20.0/24")
                        ]
                    }
                ]
            },
            []);

        List<string> generated =
        [
            .. plan.Items.SelectMany(i => i.ChildSubnets).Select(c => c.Name),
            .. plan.Items.Select(i => i.AutoCreateTargetName).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!)
        ];

        Assert.NotEmpty(generated);

        foreach (string name in generated)
        {
            Assert.True(SafeTextOracle.IsSafe(name),
                $"The planner generated '{name}', which the subnet naming rules reject.");
        }
    }


    [Theory]
    [InlineData("sn-multi (10.20.40.0-24)")]
    [InlineData("vnet-a (10.71.0.0-16)")]
    [InlineData("R\u00e9seau Z\u00fcrich \"Ost\"")]
    [InlineData("\u6771\u4eac")]
    [InlineData("S\u00e3o Paulo/DC1")]
    public void AParentNameSurvivesThePrefillIntact(string parentName)
    {
        string prefill = SubnetNaming.WithSuffix(parentName, "-10.20.40.0-24", 100);

        Assert.Equal($"{parentName}-10.20.40.0-24", prefill);
    }

    [Fact]
    public void ThePlannerNeverMintsAForwardSlash_SoTheSeparatorMayNotGoBack()
    {
        AzureBulkImportPlanner planner = new(new IpUtilityService(), _sanitizer);

        BulkImportPlanViewModel plan = planner.BuildPlan(
            new BulkImportSelectionDto
            {
                VNetPrefixes =
                [
                    new BulkImportSelectedVNetPrefixDto
                    {
                        VNetName = "vnet-a",
                        VNetResourceId = VNetA,
                        AddressPrefix = "10.71.0.0/16",
                        VNetIpv4AddressPrefixes = ["10.71.0.0/16", "10.72.0.0/16"],
                        Subnets = [Sub("sn-multi", "10.71.1.0/24")]
                    },
                    new BulkImportSelectedVNetPrefixDto
                    {
                        VNetName = "vnet-a",
                        VNetResourceId = VNetA,
                        AddressPrefix = "10.72.0.0/16",
                        VNetIpv4AddressPrefixes = ["10.71.0.0/16", "10.72.0.0/16"],
                        Subnets = [Sub("sn-other", "10.72.1.0/24")]
                    }
                ]
            },
            []);

        List<string> minted =
        [
            .. plan.Items.Select(i => i.AutoCreateTargetName ?? string.Empty),
            .. plan.Items.SelectMany(i => i.ChildSubnets).Select(c => c.Name)
        ];

        Assert.NotEmpty(minted);
        Assert.All(minted, name => Assert.DoesNotContain("/", name));
    }
}
