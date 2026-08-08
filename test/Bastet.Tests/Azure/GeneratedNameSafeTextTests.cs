using Bastet.Controllers;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

/// <summary>
/// The guard that was missing. `SubnetNamingSafeTextTests` pins `ToSafeText` character by character,
/// but nothing asserted that a name the application GENERATES satisfies the application's own input
/// rules - which is exactly how round 13 reintroduced the "/" that round 4 removed.
///
/// The consequence was not cosmetic: the Details page's Create Subnet button prefills the child name
/// from its parent through `SubnetNaming.ToSafeText`, which DELETES a forbidden character rather than
/// rejecting it, so "(10.20.40.0/24)" silently became the false token "(10.20.40.024)" and an
/// operator accepting the default persisted it.
/// </summary>
public class GeneratedNameSafeTextTests
{
    private const string VNetA = "/subscriptions/test/providers/Microsoft.Network/virtualNetworks/vnet-a";
    private const string MultiPrefixSubnet = $"{VNetA}/subnets/sn-multi";

    private readonly IInputSanitizationService _sanitizer = new InputSanitizationService();

    private static BulkImportSelectedSubnetDto Sub(string name, string prefix) =>
        new() { Name = name, AddressPrefix = prefix, AzureResourceId = MultiPrefixSubnet };

    /// <summary>
    /// Every child name the bulk planner produces must be a name the Create form would accept.
    /// This is the assertion whose absence let the character back in.
    /// </summary>
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

    /// <summary>The same guard over the single-VNet wizard's server-side name resolution.</summary>
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

    /// <summary>
    /// The prefill an operator actually sees. It composes the parent's name through ToSafeText, so a
    /// generated parent name carrying a forbidden character loses it silently rather than being
    /// rejected - which is what produced "(10.20.40.024)".
    /// </summary>
    [Theory]
    [InlineData("sn-multi (10.20.40.0-24)")]
    [InlineData("vnet-a (10.71.0.0-16)")]
    public void AGeneratedParentNameSurvivesThePrefillIntact(string generatedParentName)
    {
        Assert.True(_sanitizer.IsSafeText(generatedParentName));
        Assert.Equal(generatedParentName, SubnetNaming.ToSafeText(generatedParentName));
    }

    /// <summary>The character that caused it, pinned so its return is a test failure.</summary>
    [Fact]
    public void TheForwardSlashIsStillForbidden_SoTheSeparatorMayNotGoBack()
    {
        Assert.False(_sanitizer.IsSafeText("sn-multi (10.20.40.0/24)"));
        Assert.Equal("sn-multi (10.20.40.024)", SubnetNaming.ToSafeText("sn-multi (10.20.40.0/24)"));
    }
}
