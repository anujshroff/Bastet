using Bastet.Controllers;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

/// <summary>
/// Both multi-prefix groupings key on an ARM resource ID with StringComparer.OrdinalIgnoreCase, then
/// collected the result into a set built by a collection expression - which constructs a plain
/// HashSet&lt;string&gt; with the DEFAULT comparer, silently discarding the case-insensitivity one line
/// above. GroupBy keeps only the first member's spelling as its key, so a sibling row spelled
/// ".../Subnets/..." failed the later Contains and kept its bare Azure name while its siblings were
/// qualified for the range they hold.
///
/// ARM resource IDs are case-insensitive, so the differing spellings are legitimate; the trigger is a
/// crafted or replayed post, which is precisely the case this naming code says it exists to handle.
/// </summary>
public class MultiPrefixResourceIdCasingTests
{
    private const string VNet = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet-a";
    private const string Lower = $"{VNet}/subnets/sn-multi";
    private static readonly string Upper = Lower.Replace("/subnets/", "/Subnets/");

    // -------------------------------------------------------------------------
    // The single-VNet wizard's server-side resolution
    // -------------------------------------------------------------------------

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
        ]);

        // Every row names the range it holds. Before the fix the rows spelled with the other casing
        // fell out of the set: one kept the bare Azure name, and a later one was disambiguated by
        // VNet name instead of by range - actively misleading about why it had been renamed.
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
        ]);

        Assert.Equal("sn-multi (10.20.40.0-24)", names[0]);
        Assert.Equal("sn-multi (10.20.5.0-24)", names[1]);
    }

    // -------------------------------------------------------------------------
    // The bulk planner's equivalent
    // -------------------------------------------------------------------------

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
}
