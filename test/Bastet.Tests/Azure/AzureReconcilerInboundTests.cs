using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;

namespace Bastet.Tests.Azure;

/// <summary>
/// The inbound direction. Every other reconcile verdict starts from a BASTET row and asks what Azure
/// says about it, so a range Azure has assigned that BASTET has no row for was invisible to all of
/// them: the scan reported nothing while the parent's Details page offered the range as free space
/// with a Create Subnet button over it.
///
/// The false-positive tests carry as much weight as the positive one. An inbound report that fires
/// on ranges BASTET legitimately accounts for is a warning operators learn to ignore, which is worse
/// than no warning at all.
/// </summary>
public class AzureReconcilerInboundTests
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";

    private readonly AzureReconciler _reconciler = new(new IpUtilityService());

    private static string VNetId(string name) =>
        $"/subscriptions/{SubId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/{name}";

    private static string SubnetId(string vnetName, string subnetName) =>
        $"{VNetId(vnetName)}/subnets/{subnetName}";

    private static BulkAzureSubnetViewModel AzSubnet(string vnetName, string name, params string[] prefixes) =>
        new()
        {
            ResourceId = SubnetId(vnetName, name),
            Name = name,
            AddressPrefix = prefixes[0],
            Ipv4AddressPrefixes = [.. prefixes]
        };

    private static BulkAzureVNetViewModel VNet(string name, string[] prefixes, params BulkAzureSubnetViewModel[] subnets) =>
        new()
        {
            ResourceId = VNetId(name),
            Name = name,
            Ipv4AddressPrefixes = [.. prefixes],
            Subnets = [.. subnets]
        };

    private static AzureVNetInventory Live(params BulkAzureVNetViewModel[] vnets) =>
        new() { Success = true, VNets = [.. vnets] };

    private static ExistingSubnetSnapshot Existing(
        int id, string network, int cidr, string? azureResourceId = null) =>
        new()
        {
            Id = id,
            Name = $"subnet-{id}",
            NetworkAddress = network,
            Cidr = cidr,
            AzureResourceId = azureResourceId
        };

    /// <summary>The target row an import creates, which is what marks a VNet as imported.</summary>
    private static ExistingSubnetSnapshot Target(int id, string network, int cidr, string vnetName) =>
        Existing(id, network, cidr, VNetId(vnetName));

    private AzureReconcilePlanViewModel Build(
        AzureVNetInventory inventory,
        IReadOnlyList<ExistingSubnetSnapshot> existing,
        params AzureLinkedSubnetSnapshot[] linked) =>
        _reconciler.BuildPlan(SubId, "Test Sub", inventory, linked, existing);

    private static List<AzureReconcileItem> Inbound(AzureReconcilePlanViewModel plan) =>
        [.. plan.ReviewItems.Where(i => i.Status == AzureReconcileStatus.AzureRangeNotImported)];

    // -------------------------------------------------------------------------
    // The defect
    // -------------------------------------------------------------------------

    /// <summary>
    /// Azure adds a prefix to a subnet in an imported VNet - routine since multi-prefix subnets went
    /// GA. Nothing in BASTET records it, so the free-space table hands it out.
    /// </summary>
    [Fact]
    public void ARangeAzureAssignedThatNoBastetSubnetRecords_IsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"],
                AzSubnet("vnet-a", "sn-multi", "10.90.200.0/25", "10.90.77.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a"), Existing(2, "10.90.200.0", 25, SubnetId("vnet-a", "sn-multi"))]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.90.77.0", item.NetworkAddress);
        Assert.Equal(24, item.Cidr);
        Assert.Contains("no BASTET subnet records", item.Reason);
        Assert.Contains("free space", item.Reason);
    }

    /// <summary>Report-only. There is no BASTET row to delete, and nothing here may ever be one.</summary>
    [Fact]
    public void AnInboundReportIsNeverOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.90.77.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a")]);

        Assert.Single(Inbound(plan));
        Assert.Empty(plan.Items);
        Assert.Equal(0, Assert.Single(Inbound(plan)).SubnetId);
    }

    /// <summary>
    /// The scan can no longer claim a clean bill while an Azure range is unaccounted for - the
    /// review list is one of the things the "nothing to clean up" banner is gated on.
    /// </summary>
    [Fact]
    public void AnInboundReportMeansTheScanHasSomethingToReport()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.90.77.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a")]);

        Assert.NotEmpty(plan.ReviewItems);
    }

    // -------------------------------------------------------------------------
    // False positives - each of these would make the report worthless
    // -------------------------------------------------------------------------

    /// <summary>
    /// An IPAM routinely records a coarser allocation than Azure carves out of it. Bastet holds
    /// 10.90.64.0/18; Azure creates 10.90.77.0/24 inside it. That range IS accounted for.
    /// Equality matching would report it forever.
    /// </summary>
    [Fact]
    public void ARangeContainedByACoarserBastetSubnet_IsNotReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.90.77.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a"), Existing(2, "10.90.64.0", 18)]);

        Assert.Empty(Inbound(plan));
    }

    /// <summary>
    /// The correction that killed the finder's original proposal: only the two import paths ever
    /// write AzureResourceId, so a range the operator created by hand is absent from the linked
    /// rows. Matching against linked subnets alone would report it forever, after they had already
    /// done exactly what the report asked for.
    /// </summary>
    [Fact]
    public void ARangeCreatedByHandCarryingNoAzureLink_IsNotReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.90.77.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a"), Existing(2, "10.90.77.0", 24, azureResourceId: null)]);

        Assert.Empty(Inbound(plan));
    }

    [Fact]
    public void ARangeRecordedExactly_IsNotReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-a", "10.90.1.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a"), Existing(2, "10.90.1.0", 24, SubnetId("vnet-a", "sn-a"))]);

        Assert.Empty(Inbound(plan));
    }

    /// <summary>
    /// Scoping. Without it, pointing the scan at a subscription BASTET has never imported produces
    /// an item per Azure subnet on every scan - noise that buries the real reports.
    /// </summary>
    [Fact]
    public void AVNetThatWasNeverImported_ProducesNoInboundReports()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(
                VNet("vnet-imported", ["10.90.0.0/16"], AzSubnet("vnet-imported", "sn-a", "10.90.1.0/24")),
                VNet("vnet-untouched", ["10.99.0.0/16"],
                    AzSubnet("vnet-untouched", "sn-x", "10.99.1.0/24"),
                    AzSubnet("vnet-untouched", "sn-y", "10.99.2.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-imported"), Existing(2, "10.90.1.0", 24, SubnetId("vnet-imported", "sn-a"))]);

        Assert.Empty(Inbound(plan));
    }

    /// <summary>Only the unaccounted prefix of a multi-prefix subnet is reported, not the whole subnet.</summary>
    [Fact]
    public void OnlyTheUnaccountedPrefixOfAMultiPrefixSubnetIsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"],
                AzSubnet("vnet-a", "sn-multi", "10.90.1.0/24", "10.90.2.0/24", "10.90.3.0/24"))),
            [
                Target(1, "10.90.0.0", 16, "vnet-a"),
                Existing(2, "10.90.1.0", 24, SubnetId("vnet-a", "sn-multi")),
                Existing(3, "10.90.2.0", 24, SubnetId("vnet-a", "sn-multi"))
            ]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.90.3.0", item.NetworkAddress);
    }

    /// <summary>
    /// An Azure subnet covering a whole VNet prefix is recorded by marking the TARGET fully
    /// allocated, not by creating a child - so the target genuinely is the record of that range and
    /// an exact match against it must count, even though a target's containment does not.
    ///
    /// The fixture sets IsFullyAllocated deliberately: that is the state the docstring's
    /// justification describes, and without it this test pinned the silence in exactly the state
    /// where the justification does not hold (O5).
    /// </summary>
    [Fact]
    public void AnAzureSubnetCoveringTheWholeVNetPrefix_IsAccountedForByTheTargetItself()
    {
        ExistingSubnetSnapshot target = Target(1, "10.90.0.0", 16, "vnet-a");
        target.IsFullyAllocated = true;

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-whole", "10.90.0.0/16"))),
            [target]);

        Assert.Empty(Inbound(plan));
    }

    /// <summary>
    /// O5. The equality arm returned true for ANY row whose address equals the Azure range,
    /// including a VNet-level target that is linked but NOT marked fully allocated. The remark that
    /// justifies the arm says the target "is the record of that range" precisely because the
    /// fully-allocated import happened - so when it has not happened, the largest possible range to
    /// be wrong about is the one range silently skipped.
    /// </summary>
    [Fact]
    public void ATargetLinkedButNotFullyAllocated_DoesNotAccountForTheWholePrefixSubnet()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.61.0.0/24"], AzSubnet("vnet-a", "sn-whole", "10.61.0.0/24"))),
            [Target(1, "10.61.0.0", 24, "vnet-a")]);   // IsFullyAllocated defaults to false

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.61.0.0", item.NetworkAddress);
        Assert.Equal(24, item.Cidr);
    }

    /// <summary>
    /// The owner asked for the remedy to be in the item's own Reason rather than left for the
    /// operator to discover in the import wizard.
    /// </summary>
    [Fact]
    public void AnUnaccountedWholePrefixRange_NamesTheImportRemedyInItsReason()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.61.0.0/24"], AzSubnet("vnet-a", "sn-whole", "10.61.0.0/24"))),
            [Target(1, "10.61.0.0", 24, "vnet-a")]);

        Assert.Contains("fully allocated", Assert.Single(Inbound(plan)).Reason);
    }

    /// <summary>
    /// And when the target already has children the top-up import cannot clear it - the planner
    /// refuses. The item is true in that state, so the Reason must say what to do about it instead
    /// of sending the operator to a wizard that will refuse.
    /// </summary>
    [Fact]
    public void AnUnaccountedWholePrefixRangeOnAPopulatedTarget_SaysTheChildMustGoFirst()
    {
        ExistingSubnetSnapshot target = Target(1, "10.61.0.0", 24, "vnet-a");
        target.HasChildSubnets = true;

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.61.0.0/24"], AzSubnet("vnet-a", "sn-whole", "10.61.0.0/24"))),
            [target, Existing(2, "10.61.0.0", 25)]);

        Assert.Contains("child", Assert.Single(Inbound(plan)).Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The counterpart, and the reason a target's containment cannot count: the target holds the
    /// whole VNet prefix, so if containment by a target were enough, nothing inside any imported
    /// VNet could ever be reported and this whole check would be vacuous.
    /// </summary>
    [Fact]
    public void ATargetContainingTheRangeIsNotEnough_OrTheCheckWouldBeVacuous()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.90.77.0/24"))),
            // The target contains 10.90.77.0/24 by construction. Nothing else records it.
            [Target(1, "10.90.0.0", 16, "vnet-a")]);

        Assert.Single(Inbound(plan));
    }

    /// <summary>
    /// O4. The exclusion above was applied to the import target only, so ANY other containing row
    /// counted - including an ancestor of the target. ValidateSubnetCreation forces every subnet
    /// under its most specific container, so an install that models a top-down plan (a 10/8 root, a
    /// regional aggregate) necessarily has such an ancestor above every Azure import target, and
    /// that one hand-created row makes the whole inbound direction vacuous beneath it, permanently.
    /// </summary>
    [Fact]
    public void AnAncestorAboveTheImportTarget_DoesNotAccountForRangesInsideIt()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.20.0.0/16"], AzSubnet("vnet-a", "sn-unrecorded", "10.20.20.0/24"))),
            [
                Existing(1, "10.0.0.0", 8),                 // hand-created aggregate, no Azure link
                Target(2, "10.20.0.0", 16, "vnet-a")        // the import target
            ]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.20.20.0", item.NetworkAddress);
        Assert.Equal(24, item.Cidr);
    }

    /// <summary>
    /// The owner's call on the equality edge: a row exactly the size of the VNet address prefix is
    /// an ancestor of the target, not an allocation record, so it does not account for ranges inside
    /// it. IsSubnetContainedInParent is strict, so this falls out of the same test - pinned here so
    /// it stays a decision rather than an artefact of a helper's comparison operator.
    /// </summary>
    [Fact]
    public void ARowExactlyTheSizeOfTheVNetPrefix_DoesNotAccountForRangesInsideIt()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.20.0.0/16"], AzSubnet("vnet-a", "sn-unrecorded", "10.20.20.0/24"))),
            [
                Existing(1, "10.20.0.0", 16),               // same size as the VNet prefix, no link
                Target(2, "10.20.0.0", 16, "vnet-a")
            ]);

        Assert.Single(Inbound(plan));
    }

    /// <summary>
    /// The false-positive counter-test, and the behaviour N3 deliberately shipped: a hand reserve
    /// created INSIDE the VNet's address space really does record the range, and must keep
    /// suppressing the report. Only the ancestor case changes.
    /// </summary>
    [Fact]
    public void AHandReserveInsideTheVNetPrefix_StillAccountsForTheRange()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.20.0.0/16"], AzSubnet("vnet-a", "sn-unrecorded", "10.20.20.0/24"))),
            [
                Target(1, "10.20.0.0", 16, "vnet-a"),
                Existing(2, "10.20.16.0", 20)               // inside the VNet prefix, contains the range
            ]);

        Assert.Empty(Inbound(plan));
    }

    /// <summary>
    /// The owner's call on the null case: when no VNet address prefix contains the Azure range, fall
    /// back to the containment test rather than reporting. ARM normally forbids that shape, but the
    /// reconciler also assembles inventory under partial RBAC visibility, and reporting ranges
    /// outside every declared address prefix would spam items nobody can clear.
    /// </summary>
    [Fact]
    public void WhenNoVNetPrefixContainsTheRange_AContainingRowStillAccountsForIt()
    {
        AzureReconcilePlanViewModel plan = Build(
            // The subnet's prefix sits outside the VNet's declared address space.
            Live(VNet("vnet-a", ["10.20.0.0/16"], AzSubnet("vnet-a", "sn-outside", "172.16.5.0/24"))),
            [
                Target(1, "10.20.0.0", 16, "vnet-a"),
                Existing(2, "172.16.0.0", 16)               // records it, but is outside any VNet prefix
            ]);

        Assert.Empty(Inbound(plan));
    }

    /// <summary>
    /// A multi-prefix VNet must pick the prefix that actually contains the range, not the first one.
    /// </summary>
    [Fact]
    public void AMultiPrefixVNet_ScopesTheContainmentTestToThePrefixHoldingTheRange()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["192.168.100.0/24", "10.20.0.0/16"],
                AzSubnet("vnet-a", "sn-unrecorded", "10.20.20.0/24"))),
            [
                Existing(1, "10.0.0.0", 8),                 // ancestor of the /16 prefix
                Target(2, "10.20.0.0", 16, "vnet-a")
            ]);

        Assert.Single(Inbound(plan));
    }

    /// <summary>
    /// The real inventory shape, which a hand-built fixture hides. GetVNetInventory emits one row
    /// per prefix for a multi-prefix Azure subnet and every row carries the COMPLETE prefix list, so
    /// walking rows x prefixes visits an n-prefix subnet n^2 times. Caught live: a three-prefix
    /// subnet reported its one unaccounted range three times.
    /// </summary>
    [Fact]
    public void TheRealMultiPrefixInventoryShape_ReportsEachRangeExactlyOnce()
    {
        // Three rows for one Azure subnet, each carrying all three prefixes - exactly what
        // AzureService.BuildInventorySubnetRows produces.
        string[] all = ["10.90.200.0/25", "10.90.200.128/25", "10.90.77.0/24"];
        BulkAzureSubnetViewModel Row(string primary) => new()
        {
            ResourceId = SubnetId("vnet-a", "sn-multi"),
            Name = "sn-multi",
            AddressPrefix = primary,
            Ipv4AddressPrefixes = [.. all]
        };

        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], Row(all[0]), Row(all[1]), Row(all[2]))),
            [
                Target(1, "10.90.0.0", 16, "vnet-a"),
                Existing(2, "10.90.200.0", 25, SubnetId("vnet-a", "sn-multi")),
                Existing(3, "10.90.200.128", 25, SubnetId("vnet-a", "sn-multi"))
            ]);

        AzureReconcileItem item = Assert.Single(Inbound(plan));
        Assert.Equal("10.90.77.0", item.NetworkAddress);
    }

    /// <summary>
    /// A failed scan establishes nothing about Azure, so it must not produce inbound reports either -
    /// the same fail-closed rule the deletion path has always had.
    /// </summary>
    [Fact]
    public void AFailedScan_ProducesNoInboundReports()
    {
        AzureReconcilePlanViewModel plan = _reconciler.BuildPlan(
            SubId, "Test Sub",
            new AzureVNetInventory { Success = false, ErrorMessage = "boom" },
            [],
            [Target(1, "10.90.0.0", 16, "vnet-a")]);

        Assert.Empty(plan.ReviewItems);
        Assert.Empty(plan.Items);
    }
}
