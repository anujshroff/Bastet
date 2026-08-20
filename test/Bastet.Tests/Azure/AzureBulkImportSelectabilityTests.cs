using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

public class AzureBulkImportSelectabilityTests
{
    private const string VNetA = "/subscriptions/test/providers/Microsoft.Network/virtualNetworks/vnet-a";

    private readonly AzureBulkImportPlanner _planner =
        new(new IpUtilityService(), new InputSanitizationService());

    private static BulkImportSelectedSubnetDto Sub(string name, string prefix) =>
        new() { Name = name, AddressPrefix = prefix, AzureResourceId = $"{VNetA}/subnets/{name}" };

    private static BulkImportSelectionDto Selection(bool rename = false, params BulkImportSelectedSubnetDto[] subs) =>
        new()
        {
            RenameMatchedBastetSubnets = rename,
            VNetPrefixes =
            [
                new BulkImportSelectedVNetPrefixDto
                {
                    VNetName = "vnet-a",
                    VNetResourceId = VNetA,
                    AddressPrefix = "10.90.0.0/16",
                    Subnets = [.. subs]
                }
            ]
        };

    private static ExistingSubnetSnapshot Target(
        string? linkedTo, string name = "vnet-a", bool hasChildren = false, bool hasHostIps = false, bool fullyAllocated = false) =>
        new()
        {
            Id = 1,
            Name = name,
            NetworkAddress = "10.90.0.0",
            Cidr = 16,
            AzureResourceId = linkedTo,
            HasChildSubnets = hasChildren,
            HasHostIpAssignments = hasHostIps,
            IsFullyAllocated = fullyAllocated
        };

    private static BulkAzureVNetViewModel VNet(params BulkAzureSubnetViewModel[] subnets) =>
        new()
        {
            ResourceId = VNetA,
            Name = "vnet-a",
            Ipv4AddressPrefixes = ["10.90.0.0/16"],
            Subnets = [.. subnets]
        };

    private static BulkAzureSubnetViewModel AzureSub(string name, string prefix) =>
        new()
        {
            ResourceId = $"{VNetA}/subnets/{name}",
            Name = name,
            AddressPrefix = prefix,
            Ipv4AddressPrefixes = [prefix]
        };

    private BulkImportPlanItem Plan(BulkImportSelectionDto selection, params ExistingSubnetSnapshot[] existing) =>
        _planner.BuildPlan(selection, existing).Items[0];

    [Fact]
    public void AnUnlinkedFullyAllocatedExactMatch_CanBeAdoptedLinkOnly()
    {
        BulkImportPlanItem item = Plan(
            Selection(),
            Target(linkedTo: null, fullyAllocated: true));

        Assert.Empty(item.Errors);
        Assert.Equal(BulkImportTargetType.ExactMatch, item.TargetType);
    }

    [Fact]
    public void AnUnlinkedFullyAllocatedExactMatch_AcceptsItsEncompassingAzureSubnet()
    {
        BulkImportPlanItem item = Plan(
            Selection(false, Sub("sn-whole", "10.90.0.0/16")),
            Target(linkedTo: null, fullyAllocated: true));

        Assert.Empty(item.Errors);
        Assert.True(item.WillMarkFullyAllocated);
    }

    [Fact]
    public void AnUnlinkedFullyAllocatedExactMatch_AnnotatesAsSelectable()
    {
        BulkAzureVNetViewModel vnet = VNet(AzureSub("sn-whole", "10.90.0.0/16"));

        _planner.AnnotateAvailability([vnet], [Target(linkedTo: null, fullyAllocated: true)]);

        Assert.True(vnet.Prefixes[0].IsSelectable);
        Assert.NotEqual(BulkImportAvailability.Blocked, vnet.Prefixes[0].Status);
    }

    [Fact]
    public void AFullyAllocatedExactMatch_StillRefusesCreatingASubnetInsideIt()
    {
        BulkImportPlanItem item = Plan(
            Selection(false, Sub("sn-new", "10.90.5.0/24")),
            Target(linkedTo: null, fullyAllocated: true));

        Assert.Contains(item.Errors, e => e.Contains("fully allocated"));
    }

    [Fact]
    public void ATargetLinkedWithDifferentIdCasing_IsNotRefusedAsALinkReplacement()
    {
        BulkImportPlanItem item = Plan(
            Selection(),
            Target(linkedTo: VNetA.ToUpperInvariant()));

        Assert.Empty(item.Errors);
    }

    [Fact]
    public void ALinkedTargetWithHostIps_RenameOnly_IsPlannedWithoutErrors()
    {
        BulkImportPlanItem item = Plan(
            Selection(rename: true),
            Target(linkedTo: VNetA, name: "some-old-name", hasHostIps: true));

        Assert.Empty(item.Errors);
        Assert.True(item.WillRename);
        Assert.Equal("vnet-a", item.NewName);
    }

    [Fact]
    public void ALinkedTargetWithHostIps_AnnotatesAlreadyImported_NotBlocked()
    {
        BulkAzureVNetViewModel vnet = VNet();

        _planner.AnnotateAvailability([vnet], [Target(linkedTo: VNetA, name: "some-old-name", hasHostIps: true)]);

        Assert.Equal(BulkImportAvailability.AlreadyImported, vnet.Prefixes[0].Status);
        Assert.True(vnet.Prefixes[0].WouldRenameTarget);
        Assert.Contains("host IP", vnet.Prefixes[0].Reason);
    }

    [Fact]
    public void ALinkedTargetWithHostIps_StillRefusesCreatingASubnetInsideIt()
    {
        BulkImportPlanItem item = Plan(
            Selection(false, Sub("sn-new", "10.90.5.0/24")),
            Target(linkedTo: VNetA, hasHostIps: true));

        Assert.Contains(item.Errors, e => e.Contains("host IP assignments"));
    }

    [Fact]
    public void AnUnlinkedTargetWithHostIps_IsRefusedByThePlanToo()
    {
        BulkImportPlanViewModel plan = _planner.BuildPlan(
            Selection(rename: true),
            [Target(linkedTo: null, name: "hand-built", hasHostIps: true)]);

        Assert.Contains(plan.Items[0].Errors, e => e.Contains("host IP assignments"));
        Assert.False(plan.CanCommit);
    }

    [Fact]
    public void AnUnlinkedTargetWithHostIps_StaysBlocked()
    {
        BulkAzureVNetViewModel vnet = VNet();

        _planner.AnnotateAvailability([vnet], [Target(linkedTo: null, hasHostIps: true)]);

        Assert.Equal(BulkImportAvailability.Blocked, vnet.Prefixes[0].Status);
        Assert.False(vnet.Prefixes[0].IsSelectable);
    }

    [Fact]
    public void ASubnetInsideAFullyAllocatedTarget_IsNotSelectable()
    {
        BulkAzureVNetViewModel vnet = VNet(AzureSub("sn-inside", "10.90.5.0/24"));

        _planner.AnnotateAvailability([vnet], [Target(linkedTo: VNetA, fullyAllocated: true)]);

        Assert.False(vnet.Subnets[0].IsSelectable);
        Assert.Equal(BulkImportAvailability.Blocked, vnet.Subnets[0].Status);
        Assert.Contains("fully allocated", vnet.Subnets[0].Reason);
    }

    [Fact]
    public void TheEncompassingSubnetOfAHostIpTarget_IsNotSelectable()
    {
        BulkAzureVNetViewModel vnet = VNet(AzureSub("sn-whole", "10.90.0.0/16"));

        _planner.AnnotateAvailability([vnet], [Target(linkedTo: VNetA, hasHostIps: true)]);

        Assert.False(vnet.Subnets[0].IsSelectable);
        Assert.Equal(BulkImportAvailability.Blocked, vnet.Subnets[0].Status);
        Assert.Contains("host IP assignments", vnet.Subnets[0].Reason);
    }

    [Fact]
    public void ASubnetInsideAHostIpTarget_IsNotSelectable()
    {
        BulkAzureVNetViewModel vnet = VNet(AzureSub("sn-inside", "10.90.5.0/24"));

        _planner.AnnotateAvailability([vnet], [Target(linkedTo: VNetA, hasHostIps: true)]);

        Assert.False(vnet.Subnets[0].IsSelectable);
        Assert.Equal(BulkImportAvailability.Blocked, vnet.Subnets[0].Status);
        Assert.Contains("host IP", vnet.Subnets[0].Reason);
    }

    [Fact]
    public void CanCarrySubnetWork_IsFalseForExactlyTheBlockedStatus()
    {
        foreach (BulkImportAvailability status in Enum.GetValues<BulkImportAvailability>())
        {
            BulkAzurePrefixViewModel prefix = new() { Status = status };

            Assert.Equal(status != BulkImportAvailability.Blocked, prefix.CanCarrySubnetWork);
        }
    }

    [Fact]
    public void APrefixIsARenameOnlyCandidate_ExactlyWhenAlreadyImportedAndItWouldRename()
    {
        foreach (BulkImportAvailability status in Enum.GetValues<BulkImportAvailability>())
        {
            foreach (bool wouldRename in (bool[])[true, false])
            {
                BulkAzurePrefixViewModel prefix = new() { Status = status, WouldRenameTarget = wouldRename };

                Assert.Equal(
                    status == BulkImportAvailability.AlreadyImported && wouldRename,
                    prefix.RenameOnlyCandidate);
            }
        }
    }

    [Fact]
    public void ASubnetIsARenameOnlyCandidate_ExactlyWhenAlreadyImportedAndItWouldRename()
    {
        foreach (BulkImportAvailability status in Enum.GetValues<BulkImportAvailability>())
        {
            foreach (bool wouldRename in (bool[])[true, false])
            {
                BulkAzureSubnetViewModel subnet = new() { Status = status, WouldRenameSubnet = wouldRename };

                Assert.Equal(
                    status == BulkImportAvailability.AlreadyImported && wouldRename,
                    subnet.RenameOnlyCandidate);
            }
        }
    }
}
