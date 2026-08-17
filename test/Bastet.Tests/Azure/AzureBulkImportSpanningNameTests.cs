using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

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
        new() { Name = name, AddressPrefix = prefix, AzureResourceId = resourceId, Ipv4AddressPrefixes = [prefix] };

    private static BulkImportSelectedSubnetDto SpanningSub(string prefix) =>
        new()
        {
            Name = "sn-span",
            AddressPrefix = prefix,
            AzureResourceId = SpanningSubnet,
            Ipv4AddressPrefixes = ["10.71.5.0/24", "10.72.5.0/24"]
        };

    private BulkImportPlanViewModel Plan(
        IReadOnlyList<ExistingSubnetSnapshot> existing, params BulkImportSelectedVNetPrefixDto[] prefixes) =>
        Plan(existing, false, prefixes);

    private BulkImportPlanViewModel Plan(
        IReadOnlyList<ExistingSubnetSnapshot> existing,
        bool renameMatched,
        params BulkImportSelectedVNetPrefixDto[] prefixes) =>
        _planner.BuildPlan(
            new BulkImportSelectionDto
            {
                SubscriptionId = "sub-1",
                SubscriptionName = "Test Sub",
                RenameMatchedBastetSubnets = renameMatched,
                VNetPrefixes = [.. prefixes]
            },
            existing);

    private static List<string> ChildNames(BulkImportPlanViewModel plan) =>
        [.. plan.Items.SelectMany(i => i.ChildSubnets).Select(c => c.Name)];

    [Fact]
    public void AnAzureSubnetSpanningTwoVNetPrefixes_HasBothChildrenQualified()
    {
        BulkImportPlanViewModel plan = Plan(
            [],
            Prefix("10.71.0.0/16", SpanningSub("10.71.5.0/24")),
            Prefix("10.72.0.0/16", SpanningSub("10.72.5.0/24")));

        List<string> names = ChildNames(plan);

        Assert.Equal(2, names.Count);
        Assert.Contains("sn-span (10.71.5.0-24)", names);
        Assert.Contains("sn-span (10.72.5.0-24)", names);
        Assert.Distinct(names);
    }

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

    [Fact]
    public void ThePersistedRowForTheSameRange_IsNotTreatedAsASibling()
    {
        BulkImportPlanViewModel plan = Plan(
            [
                new ExistingSubnetSnapshot
                {
                    Id = 2, Name = "renamed-by-hand", NetworkAddress = "10.72.5.0", Cidr = 24,
                    AzureResourceId = SpanningSubnet
                }
            ],
            true,
            Prefix("10.72.0.0/16", Sub("sn-span", "10.72.5.0/24", SpanningSubnet)));

        Assert.Equal("sn-span", Assert.Single(ChildNames(plan)));
    }

    [Fact]
    public void ThePersistedRowForTheSameRange_PlansNothingWhenRenamesWereNotAskedFor()
    {
        BulkImportPlanViewModel plan = Plan(
            [
                new ExistingSubnetSnapshot
                {
                    Id = 2, Name = "renamed-by-hand", NetworkAddress = "10.72.5.0", Cidr = 24,
                    AzureResourceId = SpanningSubnet
                }
            ],
            Prefix("10.72.0.0/16", Sub("sn-span", "10.72.5.0/24", SpanningSubnet)));

        Assert.Empty(ChildNames(plan));
        Assert.DoesNotContain(plan.GlobalErrors, e => e.Contains("already exists in Bastet", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOrdinarySingleSubnetImport_KeepsItsBareAzureName()
    {
        BulkImportPlanViewModel plan = Plan(
            [],
            Prefix("10.71.0.0/16", Sub("sn-plain", "10.71.5.0/24", $"{VNetA}/subnets/sn-plain")));

        Assert.Equal("sn-plain", Assert.Single(ChildNames(plan)));
    }

    [Fact]
    public void AnEncompassingSelectionDoesNotInflateTheGroup()
    {
        BulkImportPlanViewModel plan = Plan(
            [],

            Prefix("10.71.0.0/16", Sub("sn-span", "10.71.0.0/16", SpanningSubnet)),

            Prefix("10.72.0.0/16", Sub("sn-span", "10.72.5.0/24", SpanningSubnet)));

        Assert.Equal("sn-span", Assert.Single(ChildNames(plan)));
    }
}
