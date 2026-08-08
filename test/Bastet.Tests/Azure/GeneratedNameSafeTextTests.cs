using Bastet.Controllers;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

public class GeneratedNameSafeTextTests
{
    private const string VNetA = "/subscriptions/test/providers/Microsoft.Network/virtualNetworks/vnet-a";
    private const string MultiPrefixSubnet = $"{VNetA}/subnets/sn-multi";

    private readonly IInputSanitizationService _sanitizer = new InputSanitizationService();

    private static BulkImportSelectedSubnetDto Sub(string name, string prefix) =>
        new() { Name = name, AddressPrefix = prefix, AzureResourceId = MultiPrefixSubnet };

    [Fact]
    public void EveryNameTheBulkPlannerGenerates_SatisfiesTheAppsOwnInputRules()
    {
        AzureBulkImportPlanner planner = new(new IpUtilityService(), _sanitizer);

        BulkImportPlanViewModel plan = planner.BuildPlan(
            new BulkImportSelectionDto
            {
                SubscriptionId = "sub-1",
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
            Assert.True(_sanitizer.IsSafeText(name),
                $"The planner generated '{name}', which the app's own [SafeText] rules reject.");
        }
    }

    [Fact]
    public void EveryNameResolveImportNamesGenerates_SatisfiesTheAppsOwnInputRules()
    {
        List<AzureImportSubnetViewModel> subnets =
        [
            new() { Name = "sn-multi", NetworkAddress = "10.20.40.0", Cidr = 24, AzureResourceId = MultiPrefixSubnet },
            new() { Name = "sn-multi", NetworkAddress = "10.20.5.0", Cidr = 24, AzureResourceId = MultiPrefixSubnet },
            new() { Name = "sn-multi", NetworkAddress = "10.20.20.0", Cidr = 24, AzureResourceId = MultiPrefixSubnet }
        ];

        Dictionary<int, string> names = SubnetController.ResolveImportNames(subnets, []);

        Assert.Equal(3, names.Count);

        foreach (string name in names.Values)
        {
            Assert.True(_sanitizer.IsSafeText(name),
                $"ResolveImportNames generated '{name}', which the app's own [SafeText] rules reject.");
        }
    }

    [Theory]
    [InlineData("sn-multi (10.20.40.0-24)")]
    [InlineData("vnet-a (10.71.0.0-16)")]
    public void AGeneratedParentNameSurvivesThePrefillIntact(string generatedParentName)
    {
        Assert.True(_sanitizer.IsSafeText(generatedParentName));
        Assert.Equal(generatedParentName, SubnetNaming.ToSafeText(generatedParentName));
    }

    [Fact]
    public void TheForwardSlashIsStillForbidden_SoTheSeparatorMayNotGoBack()
    {
        Assert.False(_sanitizer.IsSafeText("sn-multi (10.20.40.0/24)"));
        Assert.Equal("sn-multi (10.20.40.024)", SubnetNaming.ToSafeText("sn-multi (10.20.40.0/24)"));
    }
}
