using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

/// <summary>
/// Round 13 made an Azure subnet owning several IPv4 prefixes name each child for the range it
/// holds, because Subnet.Name is non-unique and the rows would otherwise differ only by CIDR. The
/// grouping that decided "several" was computed per plan item, and BuildPlanItem runs once per
/// selected VNet address prefix - so a subnet spanning two VNet prefixes looked single-prefix to
/// both items and the qualification was silently skipped.
/// </summary>
public class AzureBulkImportSpanningNameTests
{
    private const string VNetA = "/subscriptions/test/providers/Microsoft.Network/virtualNetworks/vnet-a";
    private const string SpanningSubnet = $"{VNetA}/subnets/sn-span";

    private readonly AzureBulkImportPlanner _planner =
        new(new IpUtilityService(), new InputSanitizationService());

    private static BulkImportSelectedVNetPrefixDto Prefix(string prefix, params BulkImportSelectedSubnetDto[] subs) =>
        new()
        {
            VNetName = "vnet-a",
            VNetResourceId = VNetA,
            AddressPrefix = prefix,
            Subnets = [.. subs]
        };

    private static BulkImportSelectedSubnetDto Sub(string name, string prefix, string resourceId) =>
        new() { Name = name, AddressPrefix = prefix, AzureResourceId = resourceId };

    private BulkImportPlanViewModel Plan(
        IReadOnlyList<ExistingSubnetSnapshot> existing, params BulkImportSelectedVNetPrefixDto[] prefixes) =>
        _planner.BuildPlan(
            new BulkImportSelectionDto
            {
                SubscriptionId = "sub-1",
                SubscriptionName = "Test Sub",
                VNetPrefixes = [.. prefixes]
            },
            existing);

    private static List<string> ChildNames(BulkImportPlanViewModel plan) =>
        [.. plan.Items.SelectMany(i => i.ChildSubnets).Select(c => c.Name)];

    /// <summary>
    /// The defect. One Azure subnet, one prefix under each of the VNet's two address prefixes: two
    /// plan items, each seeing one row, so neither qualified the name. Both rows persisted as
    /// "sn-span" carrying the same AzureResourceId.
    /// </summary>
    [Fact]
    public void AnAzureSubnetSpanningTwoVNetPrefixes_HasBothChildrenQualified()
    {
        BulkImportPlanViewModel plan = Plan(
            [],
            Prefix("10.71.0.0/16", Sub("sn-span", "10.71.5.0/24", SpanningSubnet)),
            Prefix("10.72.0.0/16", Sub("sn-span", "10.72.5.0/24", SpanningSubnet)));

        List<string> names = ChildNames(plan);

        Assert.Equal(2, names.Count);
        Assert.Contains("sn-span (10.71.5.0-24)", names);
        Assert.Contains("sn-span (10.72.5.0-24)", names);
        Assert.Distinct(names);
    }

    /// <summary>
    /// The cross-session route: this commit carries one selection for the Azure subnet, but a row
    /// from that same Azure subnet holding a different range is already in the tree.
    /// </summary>
    [Fact]
    public void ASelectionWhoseAzureSubnetAlreadyHasAPersistedSibling_IsQualified()
    {
        BulkImportPlanViewModel plan = Plan(
            [
                new ExistingSubnetSnapshot
                {
                    Id = 2, Name = "sn-span", NetworkAddress = "10.71.5.0", Cidr = 24,
                    AzureResourceId = SpanningSubnet
                }
            ],
            Prefix("10.72.0.0/16", Sub("sn-span", "10.72.5.0/24", SpanningSubnet)));

        Assert.Equal("sn-span (10.72.5.0-24)", Assert.Single(ChildNames(plan)));
    }

    /// <summary>
    /// The narrowing that keeps the ordinary path untouched. Seeding from the whole tree instead
    /// would rename any child whose Azure name merely matched some unrelated Bastet subnet.
    /// </summary>
    [Fact]
    public void AnUnrelatedBastetSubnetWithTheSameName_DoesNotCauseARename()
    {
        BulkImportPlanViewModel plan = Plan(
            [
                new ExistingSubnetSnapshot
                {
                    Id = 2, Name = "sn-span", NetworkAddress = "192.168.0.0", Cidr = 24,
                    AzureResourceId = null
                }
            ],
            Prefix("10.72.0.0/16", Sub("sn-span", "10.72.5.0/24", SpanningSubnet)));

        Assert.Equal("sn-span", Assert.Single(ChildNames(plan)));
    }

    /// <summary>Re-importing the very same range is not a sibling, so no qualification.</summary>
    [Fact]
    public void ThePersistedRowForTheSameRange_IsNotTreatedAsASibling()
    {
        BulkImportPlanViewModel plan = Plan(
            [
                new ExistingSubnetSnapshot
                {
                    Id = 2, Name = "sn-span", NetworkAddress = "10.72.5.0", Cidr = 24,
                    AzureResourceId = SpanningSubnet
                }
            ],
            Prefix("10.72.0.0/16", Sub("sn-span", "10.72.5.0/24", SpanningSubnet)));

        Assert.Equal("sn-span", Assert.Single(ChildNames(plan)));
    }

    /// <summary>An ordinary single-prefix import keeps the exact name it has always had.</summary>
    [Fact]
    public void AnOrdinarySingleSubnetImport_KeepsItsBareAzureName()
    {
        BulkImportPlanViewModel plan = Plan(
            [],
            Prefix("10.71.0.0/16", Sub("sn-plain", "10.71.5.0/24", $"{VNetA}/subnets/sn-plain")));

        Assert.Equal("sn-plain", Assert.Single(ChildNames(plan)));
    }

    /// <summary>
    /// The filter the verifier said to keep. An encompassing selection marks the target fully
    /// allocated instead of creating a child, so counting it would inflate the group and needlessly
    /// rename the one child that IS created. Reachable because a subnet may equal one VNet prefix
    /// exactly and still hold a prefix inside another.
    /// </summary>
    [Fact]
    public void AnEncompassingSelectionDoesNotInflateTheGroup()
    {
        BulkImportPlanViewModel plan = Plan(
            [],
            // sn-span covers the whole of 10.71.0.0/16...
            Prefix("10.71.0.0/16", Sub("sn-span", "10.71.0.0/16", SpanningSubnet)),
            // ...and also holds a prefix inside the VNet's other address space
            Prefix("10.72.0.0/16", Sub("sn-span", "10.72.5.0/24", SpanningSubnet)));

        // Only one child is created; it must not be renamed on the strength of the encompassing row.
        Assert.Equal("sn-span", Assert.Single(ChildNames(plan)));
    }
}
