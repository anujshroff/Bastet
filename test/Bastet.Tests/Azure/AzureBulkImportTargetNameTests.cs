using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

/// <summary>
/// N6 one level up the tree. BuildPlanItem runs once per selected VNet address prefix and TargetName
/// returned the sanitised VNet name and nothing else, so a VNet with two address prefixes persisted
/// two top-level Bastet subnets with the identical name and the identical VNet resource id,
/// distinguishable only by network address.
///
/// Unlike N6 this fires on EVERY multi-address-space VNet import, not only on a subnet that spans
/// two prefixes - one click of "Select all", no crafted payload.
/// </summary>
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

    /// <summary>The defect, as persisted in the audit's own reproduction.</summary>
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

    /// <summary>A VNet contributing one prefix keeps the bare name it has always had.</summary>
    [Fact]
    public void AVNetContributingASinglePrefix_KeepsItsBareName()
    {
        BulkImportPlanViewModel plan = Plan([], Prefix("vnet-a", VNetA, "10.71.0.0/16"));

        Assert.Equal("vnet-a", Assert.Single(TargetNames(plan)));
    }

    /// <summary>
    /// Two different VNets that happen to be selected together are not the same VNet, so neither is
    /// qualified - the grouping is by resource id, not by how many prefixes the commit carries.
    /// </summary>
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

    /// <summary>
    /// The same prefix listed twice is one prefix, not two - a duplicated selection must not trigger
    /// qualification. (The overlap check refuses the commit; the naming must not misreport either.)
    /// </summary>
    [Fact]
    public void TheSamePrefixSelectedTwice_IsNotTreatedAsTwoPrefixes()
    {
        BulkImportPlanViewModel plan = Plan(
            [],
            Prefix("vnet-a", VNetA, "10.71.0.0/16"),
            Prefix("vnet-a", VNetA, "10.71.0.0/16"));

        Assert.All(TargetNames(plan), n => Assert.Equal("vnet-a", n));
    }

    /// <summary>
    /// The ExactMatch branch adopts an existing row and names nothing, so a matched target is never
    /// renamed by this - the qualification applies only to targets the import creates.
    /// </summary>
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

    /// <summary>Generated target names must satisfy the app's own input rules, same as child names.</summary>
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
