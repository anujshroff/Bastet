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
    /// P2 reversed this. Bastet holds 10.90.64.0/18 with nothing under it; Azure creates
    /// 10.90.77.0/24 inside it. The /18 does NOT record that range - open its Details page and
    /// 10.90.77.0/24 is printed as free with a Create Subnet button over it, and creating there
    /// succeeds while Azure is holding the addresses.
    ///
    /// The owner's call, taken on this round: report it. One line per Azure range Bastet has no
    /// record of, each cleared for good by importing it or creating the subnet.
    /// </summary>
    [Fact]
    public void ARangeInsideACoarserRowThatNothingRecords_IsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.90.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.90.77.0/24"))),
            [Target(1, "10.90.0.0", 16, "vnet-a"), Existing(2, "10.90.64.0", 18)]);

        Assert.Single(Inbound(plan));
    }

    /// <summary>
    /// The counter-test that decides the predicate, and the reason IsFullyAllocated cannot be it.
    /// The /18 is not fully allocated, but 10.60.1.0/25 and 10.60.1.128/25 sit inside it and
    /// together cover 10.60.1.0/24 exactly. Bastet's free-space table does not offer that /24, so
    /// nothing is being reported as free and there is nothing to say.
    ///
    /// Gating on IsFullyAllocated instead would raise an item here that cannot be cleared: creating
    /// 10.60.1.0/24 is refused because the two /25s occupy it, and the only way to silence it would
    /// be marking the /18 fully allocated, which hides real free space elsewhere.
    /// </summary>
    [Fact]
    public void ARangeFullyCoveredByRowsInsideIt_IsNotReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.60.0.0/16"], AzSubnet("vnet-a", "sn-tiled", "10.60.1.0/24"))),
            [
                Target(1, "10.60.0.0", 16, "vnet-a"),
                Existing(2, "10.60.0.0", 18),
                Existing(3, "10.60.1.0", 25),
                Existing(4, "10.60.1.128", 25)
            ]);

        Assert.Empty(Inbound(plan));
    }

    /// <summary>
    /// Partial coverage is still coverage of only part: one /25 inside the Azure /24 leaves the
    /// other half offered as free, so the range is reported.
    /// </summary>
    [Fact]
    public void ARangeOnlyHalfCoveredByRowsInsideIt_IsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.62.0.0/16"], AzSubnet("vnet-a", "sn-half", "10.62.1.0/24"))),
            [
                Target(1, "10.62.0.0", 16, "vnet-a"),
                Existing(2, "10.62.0.0", 18),
                Existing(3, "10.62.1.0", 25)
            ]);

        Assert.Single(Inbound(plan));
    }

    /// <summary>
    /// A fully-allocated row cannot receive children at all - SubnetController.Helpers refuses it -
    /// so Bastet cannot hand the range out and is not presenting it as free. Silence is right, and
    /// this is the one place the flag is sound: as a silencer, never as the test for "recorded".
    /// </summary>
    [Fact]
    public void ARangeUnderAFullyAllocatedRow_IsNotReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.91.0.0/16"], AzSubnet("vnet-a", "sn-new", "10.91.77.0/24"))),
            [
                Target(1, "10.91.0.0", 16, "vnet-a"),
                new ExistingSubnetSnapshot
                {
                    Id = 2,
                    Name = "reserved",
                    NetworkAddress = "10.91.64.0",
                    Cidr = 18,
                    IsFullyAllocated = true
                }
            ]);

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
    /// N3 shipped this as a false positive to suppress; P2 measured it as the defect. A /20 hand
    /// reserve holding nothing does not record 10.20.20.0/24 - its own Details page offers that
    /// range as free. Whether the containing row sits inside the VNet prefix or above it never
    /// mattered; whether anything under it records the range is what does.
    /// </summary>
    [Fact]
    public void AHandReserveInsideTheVNetPrefixWithNothingUnderIt_IsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.20.0.0/16"], AzSubnet("vnet-a", "sn-unrecorded", "10.20.20.0/24"))),
            [
                Target(1, "10.20.0.0", 16, "vnet-a"),
                Existing(2, "10.20.16.0", 20)               // inside the VNet prefix, contains the range
            ]);

        Assert.Single(Inbound(plan));
    }

    /// <summary>
    /// The same answer when the Azure range falls outside the VNet's declared address space, which
    /// a partially-visible subscription can produce. The old fallback stayed quiet here to avoid
    /// items nobody could clear; under the free-space test the item IS clearable - create
    /// 172.16.5.0/24 under the /16 and it goes away - so the reason for the fallback is gone.
    /// </summary>
    [Fact]
    public void ARangeOutsideEveryVNetPrefixThatNothingRecords_IsReported()
    {
        AzureReconcilePlanViewModel plan = Build(
            // The subnet's prefix sits outside the VNet's declared address space.
            Live(VNet("vnet-a", ["10.20.0.0/16"], AzSubnet("vnet-a", "sn-outside", "172.16.5.0/24"))),
            [
                Target(1, "10.20.0.0", 16, "vnet-a"),
                Existing(2, "172.16.0.0", 16)               // contains it, but nothing under it records it
            ]);

        Assert.Single(Inbound(plan));
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
