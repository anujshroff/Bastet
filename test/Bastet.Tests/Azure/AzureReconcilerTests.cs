using Bastet.Models.ViewModels;
using Bastet.Services.Azure;

namespace Bastet.Tests.Azure;

/// <summary>
/// Tests for the Azure reconciler. The reconciler decides what may be deleted, so the cases that
/// matter most are the ones where it must stay quiet: a failed scan, and resources that are still
/// live. A false positive here archives real data.
/// </summary>
public class AzureReconcilerTests
{
    private const string SubId = "11111111-1111-1111-1111-111111111111";
    private const string OtherSubId = "22222222-2222-2222-2222-222222222222";

    private readonly AzureReconciler _reconciler;

    public AzureReconcilerTests() => _reconciler = new AzureReconciler();

    // -------------------------------------------------------------------------
    // Builders
    // -------------------------------------------------------------------------

    private static string VNetId(string name, string subscriptionId = SubId) =>
        $"/subscriptions/{subscriptionId}/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/{name}";

    private static string SubnetId(string vnetName, string subnetName, string subscriptionId = SubId) =>
        $"{VNetId(vnetName, subscriptionId)}/subnets/{subnetName}";

    private static BulkAzureVNetViewModel VNet(string name, string[] prefixes, params BulkAzureSubnetViewModel[] subnets) =>
        new()
        {
            ResourceId = VNetId(name),
            Name = name,
            Ipv4AddressPrefixes = [.. prefixes],
            Subnets = [.. subnets]
        };

    /// <summary>
    /// Mirrors what GetVNetInventory builds: AddressPrefix is the first IPv4 prefix, and
    /// Ipv4AddressPrefixes carries all of them. Passing more than one models an Azure subnet with
    /// multiple address prefixes, GA since September 2025.
    /// </summary>
    private static BulkAzureSubnetViewModel AzSubnet(string vnetName, string name, params string[] prefixes) =>
        new()
        {
            ResourceId = SubnetId(vnetName, name),
            Name = name,
            AddressPrefix = prefixes[0],
            Ipv4AddressPrefixes = [.. prefixes]
        };

    private static AzureVNetInventory Live(params BulkAzureVNetViewModel[] vnets) =>
        new() { Success = true, VNets = [.. vnets] };

    private static AzureVNetInventory Failed(string error = "boom") =>
        new() { Success = false, ErrorMessage = error };

    private static AzureLinkedSubnetSnapshot Linked(
        int id, string name, string network, int cidr, string azureResourceId,
        bool fullyAllocated = false, int descendants = 0, int hostIps = 0, int[]? descendantIds = null) =>
        new()
        {
            Id = id,
            Name = name,
            NetworkAddress = network,
            Cidr = cidr,
            AzureResourceId = azureResourceId,
            IsFullyAllocated = fullyAllocated,
            DescendantCount = descendants,
            HostIpCount = hostIps,
            DescendantSubnetIds = descendantIds ?? []
        };

    private AzureReconcilePlanViewModel Build(
        AzureVNetInventory inventory, params AzureLinkedSubnetSnapshot[] linked) =>
        _reconciler.BuildPlan(SubId, "Test Sub", inventory, linked);

    // -------------------------------------------------------------------------
    // Fail closed - the safety property the whole feature rests on
    // -------------------------------------------------------------------------

    [Fact]
    public void ScanFailed_ReturnsNoItemsAndCannotCommit()
    {
        // A failed read tells us nothing about what exists in Azure. If this ever reports items,
        // an expired credential or a transient outage would invite deleting the entire tree.
        AzureReconcilePlanViewModel plan = Build(
            Failed("ManagedIdentityCredential authentication failed"),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a")),
            Linked(2, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        Assert.False(plan.ScanSucceeded);
        Assert.False(plan.CanCommit);
        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("Could not read VNets from Azure"));
    }

    [Fact]
    public void ScanFailed_SurfacesUnderlyingError()
    {
        AzureReconcilePlanViewModel plan = Build(Failed("credential expired"));

        Assert.Contains(plan.GlobalErrors, e => e.Contains("credential expired"));
    }

    [Fact]
    public void EmptySubscriptionWithFlaggedItems_AddsWarning()
    {
        // Azure legitimately reporting an empty subscription and pointing at the wrong subscription
        // look identical here, and the consequence is deleting everything.
        AzureReconcilePlanViewModel plan = Build(
            Live(),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a")));

        Assert.True(plan.ScanSucceeded);
        Assert.Single(plan.Items);
        Assert.Contains(plan.Warnings, w => w.Contains("no VNets at all"));
    }

    [Fact]
    public void EmptySubscriptionWithNothingLinked_AddsNoWarning()
    {
        AzureReconcilePlanViewModel plan = Build(Live());

        Assert.Empty(plan.Items);
        Assert.Empty(plan.Warnings);
        Assert.False(plan.CanCommit); // nothing to do
    }

    // -------------------------------------------------------------------------
    // VNet-level rows
    // -------------------------------------------------------------------------

    [Fact]
    public void VNetDeleted_Flagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", ["192.168.0.0/16"])),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetDeleted, item.Status);
        Assert.True(item.IsVNetLevel);
        Assert.True(plan.CanCommit);

        // The inventory drops VNets with no IPv4 address space, so an absent VNet is not proof it was
        // deleted. The reason is read straight above a Delete button and must not overstate the case.
        Assert.Contains("no longer has any IPv4 address space", item.Reason);
    }

    [Fact]
    public void VNetLiveButPrefixRemoved_Flagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "second prefix", "10.1.0.0", 16, VNetId("vnet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, item.Status);
        Assert.Contains("no longer has the address prefix", item.Reason);
    }

    [Fact]
    public void VNetAndPrefixLive_NotFlagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a")));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void MultipleRowsShareOneVNetResourceId_EachJudgedOnItsOwnPrefix()
    {
        // A VNet with two prefixes imports as two Bastet rows carrying the same resource ID.
        // Dropping one prefix must flag only that row.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "kept", "10.0.0.0", 16, VNetId("vnet-a")),
            Linked(2, "dropped", "10.1.0.0", 16, VNetId("vnet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(2, item.SubnetId);
        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, item.Status);
    }

    // -------------------------------------------------------------------------
    // Subnet-level rows
    // -------------------------------------------------------------------------

    [Fact]
    public void SubnetDeleted_Flagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetDeleted, item.Status);
        Assert.False(item.IsVNetLevel);
    }

    [Fact]
    public void SubnetPrefixChanged_Flagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.9.0/24"))),
            Linked(1, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetPrefixChanged, item.Status);
        Assert.Contains("10.0.9.0/24", item.Reason);
    }

    /// <summary>
    /// The subnet still owns the prefix Bastet recorded; it simply has another one listed first.
    /// Reading only the first prefix reports drift that has not happened, and a drift row is
    /// offered for deletion with no direct Azure read behind it. The VNet-level check ten lines
    /// above has always tested membership, which is why the same shape never bit there.
    /// </summary>
    [Fact]
    public void SubnetWithSecondIpv4Prefix_StillOwningBastetsPrefix_NotFlagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.0.0/24", "10.0.1.0/24"))),
            Linked(1, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        Assert.Empty(plan.Items);
    }

    /// <summary>
    /// The other direction, and the one that matters after E1: a genuine prefix change must still be
    /// reported. A fix that merely stopped flagging multi-prefix subnets would pass the test above
    /// and re-create the over-blocking E1 was about.
    /// </summary>
    [Fact]
    public void SubnetWithSeveralPrefixes_NoneMatchingBastet_StillFlagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.8.0/24", "10.0.9.0/24"))),
            Linked(1, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetPrefixChanged, item.Status);

        // Both live prefixes are named: telling the operator only the first would be the same
        // half-truth that produced the defect.
        Assert.Contains("10.0.8.0/24", item.Reason);
        Assert.Contains("10.0.9.0/24", item.Reason);
    }

    /// <summary>
    /// NotVisible and Unknown are both withheld, and that is correct - but they are different facts.
    /// Sharing one sentence told the operator "the credential may have lost access" when the truth was
    /// that the read failed, which sends them auditing role assignments on a healthy subscription.
    /// Unknown needs no crafted input: an ARM throttle or a transport blip mid-scan produces it.
    /// </summary>
    [Fact]
    public void UnknownVerdict_IsExplainedAsAFailedRead_NotALostCredential()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "gone", "10.9.0.0", 16, VNetId("vnet-gone")));

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("vnet-gone")] = AzureResourceConfirmation.Unknown
        });

        Assert.Empty(plan.Items);
        string warning = Assert.Single(plan.Warnings);
        Assert.Contains("could not be asked", warning);
        Assert.DoesNotContain("lost access", warning);
    }

    /// <summary>The 403 case keeps its own sentence, which is correct and actionable for it.</summary>
    [Fact]
    public void NotVisibleVerdict_StillNamesTheCredential()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "gone", "10.9.0.0", 16, VNetId("vnet-gone")));

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("vnet-gone")] = AzureResourceConfirmation.NotVisible
        });

        Assert.Empty(plan.Items);
        string warning = Assert.Single(plan.Warnings);
        Assert.Contains("denied access", warning);
        Assert.Contains("lost access to their resource group", warning);
    }

    /// <summary>
    /// Two rows withheld for different reasons must produce two sentences, not one that is wrong about
    /// half of them. This is the shape the audit measured live: a genuine 403 and an HTTP 400 named
    /// together under the credential explanation.
    /// </summary>
    [Fact]
    public void MixedWithholdReasons_ProduceSeparateWarnings()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "hidden", "10.9.0.0", 16, VNetId("vnet-hidden")),
            Linked(2, "unreadable", "10.8.0.0", 16, VNetId("vnet-unreadable")));

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("vnet-hidden")] = AzureResourceConfirmation.NotVisible,
            [VNetId("vnet-unreadable")] = AzureResourceConfirmation.Unknown
        });

        Assert.Empty(plan.Items);
        Assert.Equal(2, plan.Warnings.Count);
        Assert.Contains(plan.Warnings, w => w.Contains("denied access") && w.Contains("hidden"));
        Assert.Contains(plan.Warnings, w => w.Contains("could not be asked") && w.Contains("unreadable"));
    }

    /// <summary>
    /// A stored ID that names neither a VNet nor a subnet must never be answered as a deletion. The
    /// Azure SDK builds its request from (subscription, resource group, last path segment) and
    /// discards the provider namespace and type, so reading a resource-group or storage-account ID
    /// through the VNet accessor asks about a *different* resource - and its 404 used to read as
    /// "Azure confirms this is gone", offering the row and its whole subtree for archival.
    /// </summary>
    [Theory]
    [InlineData("/subscriptions/" + SubId + "/resourceGroups/rg")]
    [InlineData("/subscriptions/" + SubId + "/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/acct")]
    // Last segment deliberately matches a live VNet: this is the shape that answered "Live" against
    // real ARM, because the SDK asks for virtualNetworks/<last segment> whatever the type says.
    [InlineData("/subscriptions/" + SubId + "/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vnet-a")]
    public void UnrecognisedResourceId_IsReviewedNotOfferedForDeletion(string resourceId)
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "mystery", "10.0.0.0", 16, resourceId));

        Assert.Empty(plan.Items);

        AzureReconcileItem item = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.UnrecognisedResourceId, item.Status);
        Assert.DoesNotContain("no longer exists", item.Reason);
    }

    /// <summary>
    /// The guard: a real VNet ID that is genuinely absent from the listing must still be offered.
    /// A fix that routed anything unfamiliar to review would stop the reconciler doing its job.
    /// </summary>
    [Fact]
    public void GenuinelyAbsentVNet_StillOfferedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "gone", "10.9.0.0", 16, VNetId("vnet-gone")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetDeleted, item.Status);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void SubnetLive_NotFlagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.1.0/24"))),
            Linked(1, "snet-a", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        Assert.Empty(plan.Items);
    }

    // -------------------------------------------------------------------------
    // Fully encompassing VNet (VNet 10.11.0.0/24 whose only subnet is 10.11.0.0/24)
    // -------------------------------------------------------------------------

    [Fact]
    public void FullyEncompassedVNet_AllLive_NotFlagged()
    {
        // Import produces ONE Bastet row carrying the VNet's id and IsFullyAllocated; the Azure
        // subnet gets no row of its own. Nothing has drifted here.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-e", ["10.11.0.0/24"], AzSubnet("vnet-e", "default", "10.11.0.0/24"))),
            Linked(1, "vnet-e", "10.11.0.0", 24, VNetId("vnet-e"), fullyAllocated: true));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    /// <summary>
    /// The same collapsed-prefix read one check earlier: the fully-allocated marker is justified by
    /// an Azure subnet covering the target's whole prefix, and that search compared only each
    /// subnet's first prefix. A covering subnet that lists another prefix first was reported as
    /// having lost its cause. Review-only, so it can never delete anything - but it is the same
    /// defect at its second site, and the prefix list is already to hand once the first is fixed.
    /// </summary>
    [Fact]
    public void FullyEncompassedVNet_CoveringSubnetListsAnotherPrefixFirst_NotFlagged()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-e", ["10.11.0.0/24"], AzSubnet("vnet-e", "default", "10.99.0.0/24", "10.11.0.0/24"))),
            Linked(1, "vnet-e", "10.11.0.0", 24, VNetId("vnet-e"), fullyAllocated: true));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void FullyEncompassedVNet_VNetDeleted_FlaggedForDeletion()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(),
            Linked(1, "vnet-e", "10.11.0.0", 24, VNetId("vnet-e"), fullyAllocated: true));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetDeleted, item.Status);
    }

    [Fact]
    public void FullyEncompassedVNet_EncompassingSubnetDeleted_GoesToReviewItemsNotItems()
    {
        // The VNet and its prefix survive, so there is nothing to delete - but the fully-allocated
        // flag no longer has anything backing it. Report, never act: the flag can be set by hand.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-e", ["10.11.0.0/24"])),
            Linked(1, "vnet-e", "10.11.0.0", 24, VNetId("vnet-e"), fullyAllocated: true));

        Assert.Empty(plan.Items);
        AzureReconcileItem item = Assert.Single(plan.ReviewItems);
        Assert.Equal(AzureReconcileStatus.FullyAllocatingSubnetDeleted, item.Status);
        Assert.Contains("fully allocated", item.Reason);

        // Review items alone must never enable the delete button.
        Assert.False(plan.CanCommit);
    }

    [Fact]
    public void NotFullyAllocatedVNet_WithNoCoveringSubnet_NotFlagged()
    {
        // A normal VNet target whose children happen to be smaller than the prefix is not drift.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.1.0/24"))),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a"), fullyAllocated: false));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    // -------------------------------------------------------------------------
    // Scoping and matching
    // -------------------------------------------------------------------------

    [Fact]
    public void SubnetFromOtherSubscription_Ignored()
    {
        // This scan says nothing about another subscription's resources, so they are out of scope
        // rather than deleted.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "elsewhere", "172.16.0.0", 16, VNetId("vnet-z", OtherSubId)));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void ResourceIdCasingDiffers_TreatedAsLive()
    {
        // ARM resource IDs are case-insensitive; a casing difference is not a deletion.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a").ToUpperInvariant()));

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void SubnetWithoutAzureResourceId_Ignored()
    {
        // Hand-created subnets never carry a resource ID and must never be touched.
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "manual", "192.168.1.0", 24, string.Empty));

        Assert.Empty(plan.Items);
        Assert.Empty(plan.ReviewItems);
    }

    [Fact]
    public void SubscriptionIdAppearingElsewhereInPath_DoesNotCountAsInScope()
    {
        // Guards against matching the subscription with a bare substring test.
        string foreignId = $"/subscriptions/{OtherSubId}/resourceGroups/{SubId}/providers/Microsoft.Network/virtualNetworks/vnet-x";

        AzureReconcilePlanViewModel plan = Build(Live(), Linked(1, "x", "10.5.0.0", 16, foreignId));

        Assert.Empty(plan.Items);
    }

    // -------------------------------------------------------------------------
    // Cascade reporting
    // -------------------------------------------------------------------------

    [Fact]
    public void CascadeCounts_SurfacedOnItems()
    {
        // Deleting a stale VNet target archives its whole subtree, so the counts must reach the UI
        // before the user confirms.
        AzureReconcilePlanViewModel plan = Build(
            Live(),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a"), descendants: 3, hostIps: 7,
                descendantIds: [2, 3, 4]));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(3, item.DescendantCount);
        Assert.Equal(7, item.HostIpCount);
        // The subtree ids let the confirm dialog skip items an ancestor's counts already cover.
        Assert.Equal([2, 3, 4], item.DescendantSubnetIds);
    }

    [Fact]
    public void StatusName_IsSerializedAsAName_NotAnOrdinal()
    {
        // The client switches on this string; an ordinal would silently break if the enum changed.
        AzureReconcilePlanViewModel plan = Build(
            Live(),
            Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a")));

        Assert.Equal("VNetDeleted", Assert.Single(plan.Items).StatusName);
    }

    // -------------------------------------------------------------------------
    // Validation of inputs
    // -------------------------------------------------------------------------

    [Fact]
    public void NoSubscriptionSpecified_HardFails()
    {
        AzureReconcilePlanViewModel plan = _reconciler.BuildPlan(
            string.Empty, null, Live(), [Linked(1, "vnet-a", "10.0.0.0", 16, VNetId("vnet-a"))]);

        Assert.False(plan.CanCommit);
        Assert.Contains(plan.GlobalErrors, e => e.Contains("No subscription"));
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void NothingLinked_ProducesEmptyPlanThatCannotCommit()
    {
        AzureReconcilePlanViewModel plan = Build(Live(VNet("vnet-a", ["10.0.0.0/16"])));

        Assert.True(plan.ScanSucceeded);
        Assert.Empty(plan.GlobalErrors);
        Assert.False(plan.CanCommit);
    }

    // -------------------------------------------------------------------------
    // ApplyConfirmations: only a confirmed 404 may be archived
    // -------------------------------------------------------------------------

    /// <summary>
    /// A non-empty but incomplete inventory: another VNet is visible, the linked one is not. This is
    /// D3's actual shape - an RBAC-filtered listing - and unlike a wholly empty one it carries no
    /// pre-existing warning, so the warnings asserted below are only the ones under test.
    /// </summary>
    private AzureReconcilePlanViewModel PlanWithOneDeletedItem(out string resourceId)
    {
        resourceId = VNetId("vnet-a");
        return Build(
            Live(VNet("vnet-visible-to-me", ["10.9.0.0/16"])),
            Linked(1, "a", "10.0.0.0", 16, resourceId));
    }

    [Fact]
    public void ApplyConfirmations_Deleted_KeepsTheItem()
    {
        AzureReconcilePlanViewModel plan = PlanWithOneDeletedItem(out string id);
        _ = Assert.Single(plan.Items);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [id] = AzureResourceConfirmation.Deleted
        });

        _ = Assert.Single(plan.Items);
        Assert.Empty(plan.Warnings);
    }

    [Theory]
    [InlineData(AzureResourceConfirmation.NotVisible)]
    [InlineData(AzureResourceConfirmation.Unknown)]
    [InlineData(AzureResourceConfirmation.Live)]
    public void ApplyConfirmations_AnythingButDeleted_WithholdsTheItemAndExplains(
        AzureResourceConfirmation verdict)
    {
        AzureReconcilePlanViewModel plan = PlanWithOneDeletedItem(out string id);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [id] = verdict
        });

        Assert.Empty(plan.Items);
        Assert.False(plan.CanCommit);
        _ = Assert.Single(plan.Warnings);
        Assert.Contains("'a'", plan.Warnings[0]);
    }

    /// <summary>
    /// A resource ID missing from the map was never answered for, which is not permission to delete.
    /// </summary>
    [Fact]
    public void ApplyConfirmations_IdAbsentFromTheMap_WithholdsTheItem()
    {
        AzureReconcilePlanViewModel plan = PlanWithOneDeletedItem(out _);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>());

        Assert.Empty(plan.Items);
        _ = Assert.Single(plan.Warnings);
    }

    // -------------------------------------------------------------------------
    // ApplyConfirmations and the drift statuses. A confirmation answers "is it gone?", which is a
    // question only the absence statuses ask. VNetPrefixRemoved and SubnetPrefixChanged are built
    // from a listing that contained the resource, so Live is the expected answer for them and is
    // not evidence against the drift.
    // -------------------------------------------------------------------------

    /// <summary>The VNet is live and listed; only the prefix Bastet recorded is gone.</summary>
    private AzureReconcilePlanViewModel PlanWithOnePrefixRemovedItem(out string resourceId)
    {
        resourceId = VNetId("vnet-a");
        return Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "second prefix", "10.1.0.0", 16, resourceId));
    }

    /// <summary>The Azure subnet is live and listed; it has simply been re-addressed.</summary>
    private AzureReconcilePlanViewModel PlanWithOnePrefixChangedItem(out string resourceId)
    {
        resourceId = SubnetId("vnet-a", "snet-a");
        return Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"], AzSubnet("vnet-a", "snet-a", "10.0.9.0/24"))),
            Linked(1, "snet-a", "10.0.1.0", 24, resourceId));
    }

    [Theory]
    [InlineData(AzureResourceConfirmation.Live)]
    [InlineData(AzureResourceConfirmation.NotVisible)]
    [InlineData(AzureResourceConfirmation.Unknown)]
    public void ApplyConfirmations_VNetPrefixRemoved_SurvivesEveryVerdict(
        AzureResourceConfirmation verdict)
    {
        AzureReconcilePlanViewModel plan = PlanWithOnePrefixRemovedItem(out string id);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [id] = verdict
        });

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, item.Status);
        Assert.True(plan.CanCommit);
        Assert.Empty(plan.Warnings);
    }

    [Theory]
    [InlineData(AzureResourceConfirmation.Live)]
    [InlineData(AzureResourceConfirmation.NotVisible)]
    [InlineData(AzureResourceConfirmation.Unknown)]
    public void ApplyConfirmations_SubnetPrefixChanged_SurvivesEveryVerdict(
        AzureResourceConfirmation verdict)
    {
        AzureReconcilePlanViewModel plan = PlanWithOnePrefixChangedItem(out string id);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [id] = verdict
        });

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetPrefixChanged, item.Status);
        Assert.True(plan.CanCommit);
        Assert.Empty(plan.Warnings);
    }

    /// <summary>
    /// The drift rows are not submitted for confirmation at all, so they arrive with no verdict.
    /// Absence from the map must not be read as "unanswered, therefore withhold" for them.
    /// </summary>
    [Fact]
    public void ApplyConfirmations_DriftRowsAbsentFromTheMap_AreKept()
    {
        AzureReconcilePlanViewModel plan = PlanWithOnePrefixRemovedItem(out _);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>());

        _ = Assert.Single(plan.Items);
        Assert.Empty(plan.Warnings);
    }

    /// <summary>
    /// A mixed plan: the absence row is still governed by the 404-only rule while the drift row
    /// passes through untouched. Both halves must hold at once.
    /// </summary>
    [Fact]
    public void ApplyConfirmations_DriftAndAbsenceTogether_JudgedSeparately()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "drifted", "10.1.0.0", 16, VNetId("vnet-a")),
            Linked(2, "gone", "10.5.0.0", 16, VNetId("vnet-gone")));

        Assert.Equal(2, plan.Items.Count);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("vnet-gone")] = AzureResourceConfirmation.NotVisible
        });

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.VNetPrefixRemoved, item.Status);
        _ = Assert.Single(plan.Warnings);
        Assert.Contains("'gone'", plan.Warnings[0]);
        Assert.DoesNotContain("'drifted'", plan.Warnings[0]);
    }

    // -------------------------------------------------------------------------
    // VNet-vs-subnet routing. Every builder above hard-codes "resourceGroups/rg", so a resource
    // group whose own name collides with the "/subnets/" segment was never covered.
    // -------------------------------------------------------------------------

    /// <summary>
    /// A VNet living in a resource group named "subnets" has "/subnets/" in its own resource ID.
    /// Routing on that substring sends a live VNet down the subnet branch, where it matches no
    /// Azure subnet and is reported deleted - offering a healthy VNet and its children for archival.
    /// </summary>
    [Fact]
    public void VNetInResourceGroupNamedSubnets_IsNotMistakenForAnAzureSubnet()
    {
        const string rgNamedSubnets =
            $"/subscriptions/{SubId}/resourceGroups/subnets/providers/Microsoft.Network/virtualNetworks/vnet-core";

        BulkAzureVNetViewModel live = new()
        {
            ResourceId = rgNamedSubnets,
            Name = "vnet-core",
            Ipv4AddressPrefixes = ["10.20.0.0/16"]
        };

        AzureReconcilePlanViewModel plan = Build(
            Live(live),
            Linked(1, "core", "10.20.0.0", 16, rgNamedSubnets, descendants: 2));

        Assert.True(plan.ScanSucceeded);
        Assert.Empty(plan.Items);      // nothing offered for archival - the VNet is live
        Assert.False(plan.CanCommit);
    }

    /// <summary>A genuine subnet ID must still route to the subnet branch.</summary>
    [Fact]
    public void GenuineSubnetId_StillRoutesToTheSubnetBranch()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),                     // VNet live, but no subnets
            Linked(1, "child", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        AzureReconcileItem item = Assert.Single(plan.Items);
        Assert.Equal(AzureReconcileStatus.SubnetDeleted, item.Status);
        Assert.False(item.IsVNetLevel);
    }

    /// <summary>
    /// AzureResourceId is free text and can be hand-edited. ResourceIdentifier throws on malformed
    /// input, so an unparseable value must be absorbed rather than aborting the whole scan.
    /// </summary>
    [Theory]
    [InlineData("not-an-arm-id")]
    [InlineData("")]
    [InlineData("   ")]
    public void MalformedResourceId_DoesNotThrow(string malformed)
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),
            Linked(1, "odd", "10.0.1.0", 24, malformed));

        Assert.True(plan.ScanSucceeded);
        Assert.Empty(plan.GlobalErrors);
    }

    // -------------------------------------------------------------------------
    // The cascade. Archiving a target takes its whole subtree, so a target is only deletable if
    // every Azure-linked row beneath it is deletable too. A descendant this scan verified is live,
    // or explicitly withheld from deletion, must take its ancestor out of the plan with it -
    // otherwise approving the ancestor destroys exactly the rows the scan just protected.
    // -------------------------------------------------------------------------

    /// <summary>
    /// The no-RBAC variant, and the likeliest in the field: an outer VNet is deleted in Azure while
    /// an inner VNet imported beneath it is still live. The inner row never becomes an item at all
    /// (it evaluated to live), so nothing downstream can notice it - the check has to happen here.
    /// </summary>
    [Fact]
    public void BuildPlan_TargetWhoseDescendantIsStillLiveInAzure_IsWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("inner", ["10.78.128.0/17"])),                                  // outer is gone, inner is not
            Linked(1, "outer", "10.78.0.0", 16, VNetId("outer"), descendants: 1, descendantIds: [2]),
            Linked(2, "inner", "10.78.128.0", 17, VNetId("inner")));

        Assert.True(plan.ScanSucceeded);
        Assert.Empty(plan.Items);
        Assert.False(plan.CanCommit);
        Assert.Contains(plan.Warnings, w => w.Contains("still exist in Azure") && w.Contains("'outer'"));
    }

    /// <summary>
    /// The RBAC variant. The descendant is withheld by <see cref="AzureReconciler.ApplyConfirmations"/>
    /// - Azure would not confirm it is gone - and the warning says so by name. Approving the ancestor
    /// archived it anyway, including the case where the credential that would be needed to re-import
    /// it is the one ARM is refusing.
    /// </summary>
    [Theory]
    [InlineData(AzureResourceConfirmation.NotVisible)]
    [InlineData(AzureResourceConfirmation.Unknown)]
    [InlineData(AzureResourceConfirmation.Live)]
    public void ApplyConfirmations_TargetWhoseDescendantWasWithheld_IsAlsoWithheld(
        AzureResourceConfirmation verdict)
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-a", ["10.0.0.0/16"])),                                    // live VNet, no subnets
            Linked(1, "outer", "10.78.0.0", 16, VNetId("outer"), descendants: 1, descendantIds: [2]),
            Linked(2, "child", "10.0.1.0", 24, SubnetId("vnet-a", "snet-a")));

        Assert.Equal(2, plan.Items.Count);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("outer")] = AzureResourceConfirmation.Deleted,
            [SubnetId("vnet-a", "snet-a")] = verdict
        });

        Assert.Empty(plan.Items);
        Assert.False(plan.CanCommit);
        Assert.Contains(plan.Warnings, w => w.Contains("'outer'"));
    }

    /// <summary>
    /// ApplyConfirmations iterates plan.Items and never reads plan.ReviewItems, so a review-item
    /// descendant is in none of the sets built there. FullyAllocatingSubnetDeleted is produced by
    /// ordinary imports, so this is not an exotic shape.
    /// </summary>
    [Fact]
    public void ApplyConfirmations_TargetWhoseDescendantIsAReviewItem_IsAlsoWithheld()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("inner", ["10.78.128.0/17"])),                                  // live, but nothing covers the prefix
            Linked(1, "outer", "10.78.0.0", 16, VNetId("outer"), descendants: 1, descendantIds: [2]),
            Linked(2, "inner", "10.78.128.0", 17, VNetId("inner"), fullyAllocated: true));

        _ = Assert.Single(plan.ReviewItems);
        _ = Assert.Single(plan.Items);

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("outer")] = AzureResourceConfirmation.Deleted
        });

        Assert.Empty(plan.Items);
        Assert.Contains(plan.Warnings, w => w.Contains("'outer'"));
    }

    /// <summary>
    /// The ordinary case must survive both guards: when a VNet is deleted its imported subnets go
    /// with it, confirm as Deleted, and are still deletable. A guard that withheld this would make
    /// the feature useless.
    /// </summary>
    [Fact]
    public void ApplyConfirmations_TargetWhoseDescendantIsAlsoDeleted_IsStillCommittable()
    {
        AzureReconcilePlanViewModel plan = Build(
            Live(VNet("vnet-other", ["192.168.0.0/16"])),
            Linked(1, "outer", "10.78.0.0", 16, VNetId("outer"), descendants: 1, descendantIds: [2]),
            Linked(2, "child", "10.78.1.0", 24, SubnetId("outer", "snet-a")));

        _reconciler.ApplyConfirmations(plan, new Dictionary<string, AzureResourceConfirmation>
        {
            [VNetId("outer")] = AzureResourceConfirmation.Deleted,
            [SubnetId("outer", "snet-a")] = AzureResourceConfirmation.Deleted
        });

        Assert.Equal(2, plan.Items.Count);
        Assert.True(plan.CanCommit);
        Assert.Empty(plan.Warnings);
    }
}
