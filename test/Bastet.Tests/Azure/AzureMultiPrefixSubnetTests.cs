using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;
using Bastet.Services.Security;

namespace Bastet.Tests.Azure;

/// <summary>
/// An Azure subnet may own several IPv4 prefixes - GA since September 2025 - and ARM reports the
/// singular <c>addressPrefix</c> as null once there is more than one. Both import wizards used to
/// read only the first, so the remaining prefixes were never offered, never created, and then shown
/// on the target's Details page as unallocated space with a Create Subnet button over a range Azure
/// had already assigned.
///
/// These tests pin the expansion (one selectable entry per prefix), the naming that keeps the
/// resulting Bastet rows distinct, and - just as importantly - that nothing which already worked
/// changed: single-prefix subnets, the reconciler's view of the same inventory, and the
/// already-imported/blocked annotation.
/// </summary>
public class AzureMultiPrefixSubnetTests
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";

    private static string VNetId(string name) =>
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/{name}";

    private static string SubnetId(string vnetName, string subnetName) =>
        $"{VNetId(vnetName)}/subnets/{subnetName}";

    // -------------------------------------------------------------------------
    // The expansion itself
    // -------------------------------------------------------------------------

    /// <summary>
    /// The defect, at its source: two prefixes must produce two selectable entries, not one.
    /// </summary>
    [Fact]
    public void SubnetWithTwoIpv4Prefixes_ProducesOneRowPerPrefix()
    {
        List<BulkAzureSubnetViewModel> rows = AzureService.BuildInventorySubnetRows(
            SubnetId("multi-vnet", "sn-multi"), "sn-multi", ["10.31.0.0/24", "10.31.1.0/24"]);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["10.31.0.0/24", "10.31.1.0/24"], rows.Select(r => r.AddressPrefix));

        // Same Azure resource, same Azure name: the rows are two prefixes of one subnet, and the
        // wizard shows the prefix beside the name so they are already distinguishable on screen.
        Assert.All(rows, r => Assert.Equal(SubnetId("multi-vnet", "sn-multi"), r.ResourceId));
        Assert.All(rows, r => Assert.Equal("sn-multi", r.Name));
    }

    /// <summary>
    /// Load-bearing for the reconciler: it indexes prefixes by resource id, so every row for one
    /// subnet must report that subnet's COMPLETE prefix list, not just its own prefix. If a row
    /// carried only its own, whichever row landed last in the dictionary would make the reconciler
    /// believe the subnet had lost the others - and a drift row is offered for deletion.
    /// </summary>
    [Fact]
    public void EveryExpandedRow_CarriesTheSubnetsCompletePrefixList()
    {
        List<BulkAzureSubnetViewModel> rows = AzureService.BuildInventorySubnetRows(
            SubnetId("multi-vnet", "sn-multi"), "sn-multi", ["10.31.0.0/24", "10.31.1.0/24"]);

        Assert.All(rows, r => Assert.Equal(["10.31.0.0/24", "10.31.1.0/24"], r.Ipv4AddressPrefixes));
    }

    /// <summary>The guard: the ordinary single-prefix subnet is completely unchanged.</summary>
    [Fact]
    public void SubnetWithOneIpv4Prefix_IsUnchanged()
    {
        List<BulkAzureSubnetViewModel> rows = AzureService.BuildInventorySubnetRows(
            SubnetId("vnet1", "web"), "web", ["10.0.1.0/24"]);

        BulkAzureSubnetViewModel row = Assert.Single(rows);
        Assert.Equal("web", row.Name);
        Assert.Equal("10.0.1.0/24", row.AddressPrefix);
        Assert.Equal(["10.0.1.0/24"], row.Ipv4AddressPrefixes);
    }

    /// <summary>An IPv6-only subnet has no IPv4 prefixes and must contribute no rows at all.</summary>
    [Fact]
    public void SubnetWithNoIpv4Prefixes_ProducesNoRows()
        => Assert.Empty(AzureService.BuildInventorySubnetRows(SubnetId("vnet1", "v6"), "v6", []));

    /// <summary>
    /// ARM may report the same prefix in both the singular property and the collection. Emitting it
    /// twice would offer the operator two identical rows, and the second would fail as a duplicate.
    /// </summary>
    [Fact]
    public void DuplicatePrefixesFromArm_AreCollapsed()
    {
        List<BulkAzureSubnetViewModel> rows = AzureService.BuildInventorySubnetRows(
            SubnetId("vnet1", "web"), "web", ["10.0.1.0/24", "10.0.1.0/24"]);

        Assert.Single(rows);
    }

    // -------------------------------------------------------------------------
    // Naming: two Bastet rows from one Azure subnet must not collide
    // -------------------------------------------------------------------------

    private static readonly AzureBulkImportPlanner _planner =
        new(new IpUtilityService(), new InputSanitizationService());

    private static BulkImportSelectedSubnetDto Sub(string name, string prefix, string? resourceId = null) =>
        new() { Name = name, AddressPrefix = prefix, AzureResourceId = resourceId ?? string.Empty };

    private static BulkImportSelectionDto Sel(params BulkImportSelectedSubnetDto[] subs) =>
        new()
        {
            SubscriptionId = "sub-1",
            SubscriptionName = "Test Sub",
            VNetPrefixes =
            [
                new()
                {
                    VNetName = "multi-vnet",
                    VNetResourceId = VNetId("multi-vnet"),
                    AddressPrefix = "10.31.0.0/16",
                    Subnets = [.. subs]
                }
            ]
        };

    /// <summary>
    /// Selecting both prefixes of one Azure subnet creates two Bastet subnets. Bastet's Name column
    /// carries a NON-unique index, so two rows named identically would persist silently and be
    /// indistinguishable in every list. Each row is named for the prefix it actually holds.
    /// </summary>
    [Fact]
    public void BothPrefixesOfOneSubnet_ProduceDistinctlyNamedChildren()
    {
        string id = SubnetId("multi-vnet", "sn-multi");

        BulkImportPlanViewModel plan = _planner.BuildPlan(
            Sel(Sub("sn-multi", "10.31.0.0/24", id), Sub("sn-multi", "10.31.1.0/24", id)), []);

        BulkImportPlanItem item = Assert.Single(plan.Items);
        Assert.Equal(2, item.ChildSubnets.Count);

        Assert.Equal(
            ["sn-multi (10.31.0.0/24)", "sn-multi (10.31.1.0/24)"],
            item.ChildSubnets.Select(c => c.Name).Order());

        // Both keep the true Azure name and the true Azure resource id.
        Assert.All(item.ChildSubnets, c => Assert.Equal("sn-multi", c.OriginalAzureName));
        Assert.All(item.ChildSubnets, c => Assert.Equal(id, c.AzureResourceId));
    }

    /// <summary>
    /// The guard that matters most for existing installs: a subnet contributing ONE row is named
    /// exactly as it always was. No suffix, no change.
    /// </summary>
    [Fact]
    public void SinglePrefixSubnet_KeepsItsPlainAzureName()
    {
        BulkImportPlanViewModel plan = _planner.BuildPlan(
            Sel(Sub("web", "10.31.1.0/24", SubnetId("multi-vnet", "web"))), []);

        Assert.Equal("web", Assert.Single(Assert.Single(plan.Items).ChildSubnets).Name);
    }

    /// <summary>
    /// Two DIFFERENT Azure subnets that happen to share a name are a pre-existing case handled by
    /// the VNet-name suffix. Prefix-suffixing must not take that path over.
    /// </summary>
    [Fact]
    public void TwoDistinctAzureSubnetsSharingAName_StillUseTheExistingDisambiguation()
    {
        BulkImportPlanViewModel plan = _planner.BuildPlan(
            Sel(Sub("web", "10.31.1.0/24", SubnetId("multi-vnet", "web-a")),
                Sub("web", "10.31.2.0/24", SubnetId("multi-vnet", "web-b"))), []);

        List<string> names = [.. Assert.Single(plan.Items).ChildSubnets.Select(c => c.Name)];
        Assert.Equal(2, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("web", names);
    }

    // -------------------------------------------------------------------------
    // Availability annotation must be per prefix
    // -------------------------------------------------------------------------

    private static BulkAzureVNetViewModel InventoryVNet(params BulkAzureSubnetViewModel[] subnets) =>
        new()
        {
            ResourceId = VNetId("multi-vnet"),
            Name = "multi-vnet",
            Ipv4AddressPrefixes = ["10.31.0.0/16"],
            Subnets = [.. subnets]
        };

    /// <summary>
    /// Importing one prefix must not make the other look imported. The already-exists test is keyed
    /// on {NetworkAddress, Cidr}, so the sibling prefix - which no Bastet row occupies - stays
    /// Available. This is what makes the second prefix reachable at all after a partial import.
    /// </summary>
    [Fact]
    public void ImportingOnePrefix_LeavesTheSiblingPrefixAvailable()
    {
        string id = SubnetId("multi-vnet", "sn-multi");
        List<BulkAzureSubnetViewModel> rows =
            AzureService.BuildInventorySubnetRows(id, "sn-multi", ["10.31.0.0/24", "10.31.1.0/24"]);

        // 10.31.0.0/24 has already been imported from this very Azure subnet.
        List<ExistingSubnetSnapshot> existing =
        [
            new() { Id = 7, Name = "sn-multi (10.31.0.0/24)", NetworkAddress = "10.31.0.0", Cidr = 24, AzureResourceId = id }
        ];

        _planner.AnnotateAvailability([InventoryVNet([.. rows])], existing);

        BulkAzureSubnetViewModel imported = rows.Single(r => r.AddressPrefix == "10.31.0.0/24");
        BulkAzureSubnetViewModel sibling = rows.Single(r => r.AddressPrefix == "10.31.1.0/24");

        Assert.Equal(BulkImportAvailability.AlreadyImported, imported.Status);
        Assert.False(imported.IsSelectable);

        Assert.Equal(BulkImportAvailability.Available, sibling.Status);
        Assert.True(sibling.IsSelectable);
    }

    /// <summary>
    /// The reverse guard: a prefix occupied by an UNRELATED Bastet subnet is still blocked, so the
    /// expansion cannot be used to create an overlapping row.
    /// </summary>
    [Fact]
    public void PrefixOccupiedByAnUnrelatedBastetSubnet_IsStillBlocked()
    {
        string id = SubnetId("multi-vnet", "sn-multi");
        List<BulkAzureSubnetViewModel> rows =
            AzureService.BuildInventorySubnetRows(id, "sn-multi", ["10.31.0.0/24", "10.31.1.0/24"]);

        List<ExistingSubnetSnapshot> existing =
        [
            new() { Id = 9, Name = "hand-made", NetworkAddress = "10.31.1.0", Cidr = 24 }
        ];

        _planner.AnnotateAvailability([InventoryVNet([.. rows])], existing);

        BulkAzureSubnetViewModel blocked = rows.Single(r => r.AddressPrefix == "10.31.1.0/24");
        Assert.Equal(BulkImportAvailability.Blocked, blocked.Status);
        Assert.False(blocked.IsSelectable);
    }

    // -------------------------------------------------------------------------
    // Non-regression: the reconciler sees the same reality through the new shape
    // -------------------------------------------------------------------------

    /// <summary>
    /// The reconciler shares GetVNetInventory with the wizards, so the expansion changes what it
    /// reads. It indexes prefixes by resource id with an indexer assignment, and every expanded row
    /// reports the complete list, so N rows collapse to the same answer one row gave. A Bastet row
    /// linked at the SECOND prefix must still be recognised as live - reporting drift here would
    /// offer a live subnet for deletion.
    /// </summary>
    [Fact]
    public void ReconcilerSeesNoDrift_ForARowLinkedAtTheSecondPrefix()
    {
        string id = SubnetId("multi-vnet", "sn-multi");
        List<BulkAzureSubnetViewModel> rows =
            AzureService.BuildInventorySubnetRows(id, "sn-multi", ["10.31.0.0/24", "10.31.1.0/24"]);

        AzureVNetInventory inventory = new() { Success = true, VNets = [InventoryVNet([.. rows])] };

        List<AzureLinkedSubnetSnapshot> linked =
        [
            new()
            {
                Id = 7,
                Name = "sn-multi (10.31.1.0/24)",
                NetworkAddress = "10.31.1.0",
                Cidr = 24,
                AzureResourceId = id
            }
        ];

        AzureReconcilePlanViewModel plan =
            new AzureReconciler().BuildPlan(SubId, "Test Sub", inventory, linked);

        Assert.Empty(plan.Items);
    }

    /// <summary>
    /// And the counter-test, so the one above cannot pass by the reconciler simply having gone
    /// quiet: a row linked at a prefix the Azure subnet no longer owns is still reported.
    /// </summary>
    [Fact]
    public void ReconcilerStillReportsDrift_WhenThePrefixIsGenuinelyGone()
    {
        string id = SubnetId("multi-vnet", "sn-multi");
        List<BulkAzureSubnetViewModel> rows =
            AzureService.BuildInventorySubnetRows(id, "sn-multi", ["10.31.0.0/24", "10.31.1.0/24"]);

        AzureVNetInventory inventory = new() { Success = true, VNets = [InventoryVNet([.. rows])] };

        List<AzureLinkedSubnetSnapshot> linked =
        [
            new()
            {
                Id = 8,
                Name = "stale",
                NetworkAddress = "10.31.9.0",
                Cidr = 24,
                AzureResourceId = id
            }
        ];

        AzureReconcilePlanViewModel plan =
            new AzureReconciler().BuildPlan(SubId, "Test Sub", inventory, linked);

        Assert.Equal(AzureReconcileStatus.SubnetPrefixChanged, Assert.Single(plan.Items).Status);
    }
}
