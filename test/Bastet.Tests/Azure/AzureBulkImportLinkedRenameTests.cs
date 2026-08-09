using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

public class AzureBulkImportLinkedRenameTests
{
    private const string VNet = "/subscriptions/test/providers/Microsoft.Network/virtualNetworks/vnet-a";
    private const string OtherVNet = "/subscriptions/test/providers/Microsoft.Network/virtualNetworks/vnet-b";
    private const string SubnetA = $"{VNet}/subnets/a";

    private readonly AzureBulkImportPlanner _planner =
        new(new IpUtilityService(), new InputSanitizationService());

    private static BulkImportSelectedVNetPrefixDto Prefix(string prefix, params BulkImportSelectedSubnetDto[] subs) =>
        new()
        {
            VNetName = "vnet-a",
            VNetResourceId = VNet,
            AddressPrefix = prefix,
            Subnets = [.. subs]
        };

    private static BulkImportSelectedSubnetDto Sub(string name, string prefix, string resourceId) =>
        new() { Name = name, AddressPrefix = prefix, AzureResourceId = resourceId };

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

    private static ExistingSubnetSnapshot Target(string name, bool fullyAllocated, string? link) =>
        new()
        {
            Id = 1,
            Name = name,
            NetworkAddress = "10.80.0.0",
            Cidr = 16,
            IsFullyAllocated = fullyAllocated,
            AzureResourceId = link
        };

    private static ExistingSubnetSnapshot Child(string name, string? link) =>
        new()
        {
            Id = 2,
            Name = name,
            NetworkAddress = "10.80.1.0",
            Cidr = 24,
            AzureResourceId = link
        };

    private static bool SaysFullyAllocated(BulkImportPlanViewModel plan) =>
        plan.Items.Any(i => i.Errors.Any(e => e.Contains("is marked as fully allocated", StringComparison.Ordinal)));

    [Fact]
    public void FullyAllocatedTargetAlreadyLinked_CanBeRenamedOnItsOwn()
    {
        BulkImportPlanViewModel plan = Plan(
            [Target("renamed-by-hand", true, VNet)], true, Prefix("10.80.0.0/16"));

        Assert.False(SaysFullyAllocated(plan));
        BulkImportPlanItem item = Assert.Single(plan.Items);
        Assert.True(item.WillRename);
        Assert.Equal("vnet-a", item.NewName);
        Assert.True(plan.CanCommit);
    }

    [Fact]
    public void FullyAllocatedTargetWithNoLink_IsStillRefused()
    {
        BulkImportPlanViewModel plan = Plan(
            [Target("renamed-by-hand", true, null)], true, Prefix("10.80.0.0/16"));

        Assert.True(SaysFullyAllocated(plan));
        Assert.False(plan.CanCommit);
    }

    [Fact]
    public void FullyAllocatedTargetLinkedToADifferentVNet_IsStillRefused()
    {
        BulkImportPlanViewModel plan = Plan(
            [Target("renamed-by-hand", true, OtherVNet)], true, Prefix("10.80.0.0/16"));

        Assert.True(SaysFullyAllocated(plan));
        Assert.False(plan.CanCommit);
    }

    [Fact]
    public void FullyAllocatedTargetAlreadyLinked_IsStillRefusedWhenASubnetWouldBeCreatedInside()
    {
        BulkImportPlanViewModel plan = Plan(
            [Target("renamed-by-hand", true, VNet)], true,
            Prefix("10.80.0.0/16", Sub("a", "10.80.1.0/24", SubnetA)));

        Assert.True(SaysFullyAllocated(plan));
        Assert.False(plan.CanCommit);
    }

    [Fact]
    public void ALinkedChildWhoseNameDrifted_IsPlannedAsARenameNotACreate()
    {
        BulkImportPlanViewModel plan = Plan(
            [Target("vnet-a", false, VNet), Child("ping", SubnetA)], true,
            Prefix("10.80.0.0/16", Sub("a", "10.80.1.0/24", SubnetA)));

        BulkImportPlannedChildSubnet child = Assert.Single(Assert.Single(plan.Items).ChildSubnets);
        Assert.True(child.WillRename);
        Assert.Equal(2, child.ExistingSubnetId);
        Assert.Equal("a", child.Name);
        Assert.True(plan.CanCommit);
    }

    [Fact]
    public void ALinkedChildWhoseNameAlreadyMatches_PlansNothing()
    {
        BulkImportPlanViewModel plan = Plan(
            [Target("vnet-a", false, VNet), Child("a", SubnetA)], true,
            Prefix("10.80.0.0/16", Sub("a", "10.80.1.0/24", SubnetA)));

        Assert.Empty(Assert.Single(plan.Items).ChildSubnets);
    }

    [Fact]
    public void ALinkedChildIsNotRenamedWhenRenamesWereNotAskedFor()
    {
        BulkImportPlanViewModel plan = Plan(
            [Target("vnet-a", false, VNet), Child("ping", SubnetA)], false,
            Prefix("10.80.0.0/16", Sub("a", "10.80.1.0/24", SubnetA)));

        Assert.Empty(Assert.Single(plan.Items).ChildSubnets);
    }

    [Fact]
    public void AnUnlinkedRowOnTheSameRange_IsStillRefusedAndNeverRenamed()
    {
        BulkImportPlanViewModel plan = Plan(
            [Target("vnet-a", false, VNet), Child("ping", null)], true,
            Prefix("10.80.0.0/16", Sub("a", "10.80.1.0/24", SubnetA)));

        Assert.Contains(plan.GlobalErrors, e => e.Contains("already exists in Bastet", StringComparison.Ordinal));
        Assert.DoesNotContain(Assert.Single(plan.Items).ChildSubnets, c => c.WillRename);
        Assert.False(plan.CanCommit);
    }

    [Fact]
    public void ARowOnTheSameRangeLinkedToADifferentAzureSubnet_IsStillRefusedAndNeverRenamed()
    {
        BulkImportPlanViewModel plan = Plan(
            [Target("vnet-a", false, VNet), Child("ping", $"{OtherVNet}/subnets/a")], true,
            Prefix("10.80.0.0/16", Sub("a", "10.80.1.0/24", SubnetA)));

        Assert.Contains(plan.GlobalErrors, e => e.Contains("already exists in Bastet", StringComparison.Ordinal));
        Assert.DoesNotContain(Assert.Single(plan.Items).ChildSubnets, c => c.WillRename);
        Assert.False(plan.CanCommit);
    }
}
