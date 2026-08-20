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
    public void AGeneratedParentNameSurvivesThePrefillIntact(string generatedParentName)
    {
        Assert.True(SafeTextOracle.IsSafe(generatedParentName));
        Assert.Equal(generatedParentName, SubnetNaming.ToSafeText(generatedParentName));
    }

    [Fact]
    public void TheForwardSlashIsStillForbidden_SoTheSeparatorMayNotGoBack()
    {
        Assert.False(SafeTextOracle.IsSafe("sn-multi (10.20.40.0/24)"));
        Assert.Equal("sn-multi (10.20.40.024)", SubnetNaming.ToSafeText("sn-multi (10.20.40.0/24)"));
    }
}
