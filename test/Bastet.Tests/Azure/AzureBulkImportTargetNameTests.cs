using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

public class AzureBulkImportTargetNameTests
{
    private const string VNetA = "/subscriptions/test/providers/Microsoft.Network/virtualNetworks/vnet-a";
    private const string VNetB = "/subscriptions/test/providers/Microsoft.Network/virtualNetworks/vnet-b";

    private readonly AzureBulkImportPlanner _planner =
        new(new IpUtilityService(), new InputSanitizationService());

    private static BulkImportSelectedVNetPrefixDto Prefix(
        string vnetName, string vnetResourceId, string prefix, params BulkImportSelectedSubnetDto[] subs) =>
        new()
        {
            VNetName = vnetName,
            VNetResourceId = vnetResourceId,
            AddressPrefix = prefix,
            Subnets = [.. subs]
        };

    private BulkImportPlanViewModel Plan(
        IReadOnlyList<ExistingSubnetSnapshot> existing, params BulkImportSelectedVNetPrefixDto[] prefixes) =>
        _planner.BuildPlan(
            new BulkImportSelectionDto { SubscriptionId = "sub-1", VNetPrefixes = [.. prefixes] },
            existing);

    private static List<string?> TargetNames(BulkImportPlanViewModel plan) =>
        [.. plan.Items.Select(i => i.AutoCreateTargetName)];

    [Fact]
    public void AVNetWithTwoAddressPrefixes_NamesEachTargetForTheRangeItHolds()
    {
        BulkImportPlanViewModel plan = Plan(
            [],
            Prefix("vnet-a", VNetA, "10.71.0.0/16"),
            Prefix("vnet-a", VNetA, "10.72.0.0/16"));

        List<string?> names = TargetNames(plan);

        Assert.Equal(2, names.Count);
        Assert.Contains("vnet-a (10.71.0.0-16)", names);
        Assert.Contains("vnet-a (10.72.0.0-16)", names);
        Assert.Distinct(names);
    }

    [Fact]
    public void AVNetContributingASinglePrefix_KeepsItsBareName()
    {
        BulkImportPlanViewModel plan = Plan([], Prefix("vnet-a", VNetA, "10.71.0.0/16"));

        Assert.Equal("vnet-a", Assert.Single(TargetNames(plan)));
    }

    [Fact]
    public void TwoDifferentVNetsEachContributingOnePrefix_AreBothLeftBare()
    {
        BulkImportPlanViewModel plan = Plan(
            [],
            Prefix("vnet-a", VNetA, "10.71.0.0/16"),
            Prefix("vnet-b", VNetB, "10.72.0.0/16"));

        List<string?> names = TargetNames(plan);

        Assert.Contains("vnet-a", names);
        Assert.Contains("vnet-b", names);
    }

    [Fact]
    public void TheSamePrefixSelectedTwice_IsNotTreatedAsTwoPrefixes()
    {
        BulkImportPlanViewModel plan = Plan(
            [],
            Prefix("vnet-a", VNetA, "10.71.0.0/16"),
            Prefix("vnet-a", VNetA, "10.71.0.0/16"));

        Assert.All(TargetNames(plan), n => Assert.Equal("vnet-a", n));
    }

    [Fact]
    public void AnExactMatchTargetIsNotRenamedByTheQualification()
    {
        BulkImportPlanViewModel plan = Plan(
            [
                new ExistingSubnetSnapshot
                {
                    Id = 1, Name = "already-here", NetworkAddress = "10.71.0.0", Cidr = 16,
                    AzureResourceId = VNetA
                }
            ],
            Prefix("vnet-a", VNetA, "10.71.0.0/16"),
            Prefix("vnet-a", VNetA, "10.72.0.0/16"));

        BulkImportPlanItem matched = plan.Items.Single(i => i.TargetType == BulkImportTargetType.ExactMatch);

        Assert.Equal("already-here", matched.ExistingTargetSubnetName);
        Assert.False(matched.WillRename);
    }

    [Fact]
    public void TheQualifiedTargetNameSatisfiesTheAppsOwnInputRules()
    {
        BulkImportPlanViewModel plan = Plan(
            [],
            Prefix("vnet-a", VNetA, "10.71.0.0/16"),
            Prefix("vnet-a", VNetA, "10.72.0.0/16"));

        IInputSanitizationService sanitizer = new InputSanitizationService();

        Assert.All(TargetNames(plan), n => Assert.True(sanitizer.IsSafeText(n!)));
    }
}
