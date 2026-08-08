using Bastet.Models;
using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{
    /// <summary>
    /// Default <see cref="IAzureReconciler"/> implementation. Pure (no DB, no Azure calls) so the
    /// rules that decide what may be deleted can be tested exhaustively, mirroring
    /// <see cref="AzureBulkImportPlanner"/>.
    /// </summary>
    public class AzureReconciler(IIpUtilityService ipUtilityService) : IAzureReconciler
    {
        /// <inheritdoc/>
        public AzureReconcilePlanViewModel BuildPlan(
            string subscriptionId,
            string? subscriptionName,
            AzureVNetInventory inventory,
            IReadOnlyList<AzureLinkedSubnetSnapshot> linkedSubnets,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets)
        {
            ArgumentNullException.ThrowIfNull(inventory);
            ArgumentNullException.ThrowIfNull(linkedSubnets);
            ArgumentNullException.ThrowIfNull(existingSubnets);

            AzureReconcilePlanViewModel plan = new()
            {
                SubscriptionId = subscriptionId,
                SubscriptionName = subscriptionName,
                ScanSucceeded = inventory.Success
            };

            // Fail closed. Without a successful read we know nothing about what exists in Azure, and
            // every absent resource would look deleted. Never offer anything for deletion from here.
            if (!inventory.Success)
            {
                plan.GlobalErrors.Add(
                    $"Could not read VNets from Azure, so nothing can be reported as deleted: {inventory.ErrorMessage ?? "unknown error"}");
                return plan;
            }

            if (string.IsNullOrWhiteSpace(subscriptionId))
            {
                plan.GlobalErrors.Add("No subscription was specified.");
                return plan;
            }

            // ARM resource IDs are case-insensitive.
            Dictionary<string, BulkAzureVNetViewModel> liveVNets = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> liveSubnetPrefixes = new(StringComparer.OrdinalIgnoreCase);

            // Subnets this scan positively verified are still present in Azure. Collected so a
            // target sitting above one is never offered for deletion: see the cascade guard below.
            HashSet<int> liveLinked = [];

            // Subnets this scan skipped because they belong to another subscription. Out of scope is
            // not the same as unprotected: nothing was established about these rows, yet archiving an
            // ancestor archives them all the same, so they get their own cascade guard below.
            HashSet<int> notCovered = [];

            // Rows withheld because the range they record is still assigned in Azure under another
            // resource ID. Collected so the operator gets one warning naming what holds each range,
            // rather than only a silently shorter list.
            List<AzurePrefixOwner> rangeStillAllocated = [];

            // Which Azure subnet holds a given range, scoped to one VNet. Azure has no subnet
            // rename, so re-organising one means delete-and-recreate: the recorded resource ID goes
            // genuinely 404 while the range it named is still assigned under a new ID. Keyed by
            // {vnetResourceId}|{prefix} rather than by the bare prefix because overlapping RFC1918
            // across unrelated VNets is normal, and a bare-prefix match would withhold rows that
            // really are stale.
            //
            // Accumulates into a list; never ToDictionary. One prefix legitimately has several
            // owners even within this narrower key - a subnet can be listed twice by a paged read,
            // and the same VNet ID can appear more than once in a malformed inventory. A duplicate
            // key throw here turns the whole scan into "The reconcile scan failed", which is the
            // exact failure mode the subnet-prefix index above already avoids.
            Dictionary<string, List<AzurePrefixOwner>> livePrefixOwners = new(StringComparer.OrdinalIgnoreCase);

            // The same live prefixes again, grouped by VNet instead of keyed by exact prefix string.
            // The index above answers "is this exact range still assigned?" in one lookup; this one
            // answers "does anything still assigned OVERLAP this range?", which the exact key cannot
            // - and re-carving a prefix while re-creating the subnet is an ordinary Azure operation,
            // there being no rename. Built in the same pass so the fallback costs one dictionary
            // lookup and a walk of one VNet's prefixes, not a scan of every prefix in the
            // subscription per stale row.
            Dictionary<string, List<AzureLivePrefix>> livePrefixesByVNet = new(StringComparer.OrdinalIgnoreCase);

            foreach (BulkAzureVNetViewModel vnet in inventory.VNets)
            {
                if (!string.IsNullOrEmpty(vnet.ResourceId))
                {
                    liveVNets[vnet.ResourceId] = vnet;
                }

                foreach (BulkAzureSubnetViewModel subnet in vnet.Subnets)
                {
                    if (!string.IsNullOrEmpty(subnet.ResourceId))
                    {
                        liveSubnetPrefixes[subnet.ResourceId] = Ipv4PrefixesOf(subnet);
                    }

                    if (string.IsNullOrEmpty(vnet.ResourceId))
                    {
                        continue;
                    }

                    foreach (string prefix in Ipv4PrefixesOf(subnet))
                    {
                        string key = PrefixKey(vnet.ResourceId, prefix);

                        if (!livePrefixOwners.TryGetValue(key, out List<AzurePrefixOwner>? owners))
                        {
                            owners = [];
                            livePrefixOwners[key] = owners;
                        }

                        AzurePrefixOwner owner = new(subnet.ResourceId ?? string.Empty, subnet.Name, vnet.Name);
                        owners.Add(owner);

                        string[] parts = prefix.Split('/');

                        if (parts.Length == 2 && int.TryParse(parts[1], out int prefixCidr))
                        {
                            if (!livePrefixesByVNet.TryGetValue(vnet.ResourceId, out List<AzureLivePrefix>? byVNet))
                            {
                                byVNet = [];
                                livePrefixesByVNet[vnet.ResourceId] = byVNet;
                            }

                            byVNet.Add(new AzureLivePrefix(prefix, parts[0], prefixCidr, owner));
                        }
                    }
                }
            }

            foreach (AzureLinkedSubnetSnapshot snapshot in linkedSubnets)
            {
                if (string.IsNullOrEmpty(snapshot.AzureResourceId))
                {
                    continue;
                }

                // Recognise the ID before scoping it. BelongsToSubscription is a StartsWith over
                // "/subscriptions/{id}/", so a value that is not a parseable ARM ID at all - a typo,
                // a truncation, a migrated string; AzureResourceId is free text and the app's own
                // Admin API will write one - fails it for *every* subscription. Scoping first
                // therefore sent those rows to notCovered and they never reached the
                // UnrecognisedResourceId arm below that exists precisely for them: the scan reported
                // them in no list at all, and where one sat beneath a stale ancestor the cascade
                // guard withheld it saying the descendant "belongs to a different subscription",
                // which is not true of a row that names no subscription and cannot be acted on,
                // because rescanning any other subscription will never surface it either.
                //
                // Reported on every subscription's scan now rather than none, which is correct - it
                // is in no subscription - and it lands in ReviewItems, which is never offered for
                // deletion.
                bool recognised = AzureResourceIdentity.IsAzureSubnet(snapshot.AzureResourceId)
                                  || AzureResourceIdentity.IsAzureVNet(snapshot.AzureResourceId);

                if (!recognised)
                {
                    plan.ReviewItems.Add(Item(snapshot, AzureReconcileStatus.UnrecognisedResourceId, true,
                        "The recorded Azure resource ID names neither a VNet nor a subnet, so nothing "
                        + "can be established about it. Correct or clear the link on this subnet."));
                    continue;
                }

                // Only reconcile what this scan actually covers. A subnet belonging to another
                // subscription is out of scope, not stale - but it still has to be protected from an
                // ancestor's cascade, because an unasked question is not a deletion either.
                if (!BelongsToSubscription(snapshot.AzureResourceId, subscriptionId))
                {
                    notCovered.Add(snapshot.Id);
                    continue;
                }

                // Two-way: the unrecognised case is handled above, before scoping. An ID that is
                // neither a VNet nor a subnet used to fall down the VNet branch, where absence from
                // the listing reads as VNetDeleted - a claim nothing established, on the one path
                // that removes data.
                AzureReconcileItem? item = AzureResourceIdentity.IsAzureSubnet(snapshot.AzureResourceId)
                    ? EvaluateSubnetLevel(snapshot, liveSubnetPrefixes)
                    : EvaluateVNetLevel(snapshot, liveVNets);

                if (item is null)
                {
                    // Evaluated against a successful read and found live: the VNet or Azure subnet
                    // is there and still carries the recorded prefix. Nothing downstream ever sees
                    // this row again - it becomes neither an item nor a review item - so the only
                    // place it can protect an ancestor from the cascade is here.
                    liveLinked.Add(snapshot.Id);
                    continue;
                }

                // Before offering anything for deletion, ask the question the statuses above cannot:
                // is the RANGE still assigned in Azure, even though the resource that carried it is
                // not? A rename, or a prefix moved between two subnets, produces exactly that - and
                // archiving on it makes the parent's Details page advertise an allocated range as
                // free with a Create Subnet button over it. The evidence was always in hand; nothing
                // consulted it.
                LiveRangeOwner? stillAllocated = FindLiveOwnerOfRange(snapshot, item, livePrefixOwners, livePrefixesByVNet);

                if (stillAllocated is not null)
                {
                    // Two different facts, two different sentences. Reusing the exact-match sentence
                    // for an overlapping owner would assert that the whole recorded range is still
                    // assigned, which is false when only part of it is - and this text sits directly
                    // above a decision about irreversible archiving.
                    // Re-link repairs a row whose link is an Azure SUBNET that was replaced. It is
                    // not a repair for a VNet-level row: the index only ever holds subnet prefixes,
                    // so the suggestion offered to a VNet-level row is a SUBNET id, and writing it
                    // re-points the target at a child of its own VNet. The reconciler would then
                    // judge it through EvaluateSubnetLevel and offer it for deletion the moment
                    // that subnet went away, the bulk planner would block its VNet prefix for ever,
                    // and no screen in the application can edit AzureResourceId back.
                    bool canRelink = stillAllocated.Exact
                                     && AzureResourceIdentity.IsAzureSubnet(snapshot.AzureResourceId);

                    string reason = canRelink
                        ? $"{item.Reason} The range {snapshot.NetworkAddress}/{snapshot.Cidr} is still assigned in Azure "
                          + $"to subnet '{stillAllocated.Owner.SubnetName}' in VNet '{stillAllocated.Owner.VNetName}', so archiving this "
                          + "subnet would make BASTET report an allocated range as free. Re-link it to that Azure subnet."
                        : stillAllocated.Exact
                        ? $"{item.Reason} The range {snapshot.NetworkAddress}/{snapshot.Cidr} is still assigned in Azure "
                          + $"to subnet '{stillAllocated.Owner.SubnetName}' in VNet '{stillAllocated.Owner.VNetName}', so archiving this "
                          + "subnet would make BASTET report an allocated range as free. Re-link is not offered for a VNet-level "
                          + "import, because that would link this subnet to a child of its own VNet: correct the VNet's address "
                          + "space, or delete this subnet and import the current prefix again."
                        : $"{item.Reason} Azure subnet '{stillAllocated.Owner.SubnetName}' in VNet "
                          + $"'{stillAllocated.Owner.VNetName}' now holds {stillAllocated.LivePrefix}, which overlaps the "
                          + $"recorded range {snapshot.NetworkAddress}/{snapshot.Cidr}, so archiving this subnet would make "
                          + "BASTET report an allocated range as free. Re-link is not offered because the live range is not "
                          + "the recorded one: correct this subnet to match Azure, or delete it and import the current range "
                          + "again.";

                    AzureReconcileItem review = Item(snapshot, AzureReconcileStatus.RangeStillAllocatedInAzure, item.IsVNetLevel, reason);

                    // Deliberately left unset for an overlapping owner. The view renders the Re-link
                    // button on the presence of a suggestion (_ReconcileScripts.cshtml), and
                    // RelinkAzureSubnet 409s without one, so no suggestion means no repair route -
                    // which is the intent. Re-linking here would point the row at a subnet holding a
                    // DIFFERENT range, producing SubnetPrefixChanged on the very next scan, on a
                    // column no screen in the application can edit afterwards.
                    if (canRelink)
                    {
                        review.SuggestedAzureResourceId = stillAllocated.Owner.ResourceId;
                        review.SuggestedAzureSubnetName = stillAllocated.Owner.SubnetName;
                    }

                    plan.ReviewItems.Add(review);
                    rangeStillAllocated.Add(stillAllocated.Owner);
                    continue;
                }

                if (item.Status is AzureReconcileStatus.FullyAllocatingSubnetDeleted
                    or AzureReconcileStatus.UnrecognisedResourceId
                    or AzureReconcileStatus.VNetPrefixStillCovered)
                {
                    plan.ReviewItems.Add(item);
                }
                else
                {
                    plan.Items.Add(item);
                }
            }

            // INBOUND. Everything above walks Bastet rows and asks what Azure says about them, so
            // every verdict starts from a row that already exists. An Azure range BASTET has no row
            // for is invisible to all of it - the scan reports "nothing to clean up" while the
            // parent's Details page offers the range as free with a Create Subnet button over it.
            ReportAzureRangesNoBastetSubnetRecords(plan, inventory, existingSubnets);

            if (rangeStillAllocated.Count > 0)
            {
                // Direction-neutral and cause-free, deliberately. This sentence covers three shapes
                // that O1's original wording asserted a single cause for, and it sits under the
                // heading "Check this before deleting anything":
                //  - a rename: a different Azure resource holds exactly the recorded range;
                //  - a re-carve: a live resource holds part of it, or MORE than it (the overlap test
                //    is bidirectional - Bastet can record a /25 that Azure re-created as a /24), so
                //    "part of the range" understates it just as "the whole range" overstated it;
                //  - a VNet-level row, where no Azure subnet was re-carved at all and naming a cause
                //    would be inventing one the reconciler never established.
                // What is true of all three is that the live range and the recorded range are not
                // the same, and that archiving would report allocated space as free.
                plan.Warnings.Add(
                    $"{rangeStillAllocated.Count} subnet(s) were withheld from deletion because a live Azure resource "
                    + "still overlaps the range they record, so the recorded range and the live range are not the same. "
                    + "Archiving them would make BASTET report an allocated range as free space: "
                    + $"{OwnerList(rangeStillAllocated)}.");
            }

            WithholdTargetsWhoseCascadeIsBlocked(
                plan, liveLinked,
                "archiving them would also archive Azure-linked subnet(s) beneath them that still exist in Azure");

            // Separately worded on purpose: these rows were never read, so claiming they "still exist
            // in Azure" would assert something this scan did not establish.
            WithholdTargetsWhoseCascadeIsBlocked(
                plan, notCovered,
                "archiving them would also archive Azure-linked subnet(s) beneath them that belong to a "
                + "different subscription and were not checked by this scan");

            // An empty subscription and a subscription we failed to enumerate properly look the same
            // from here, and the consequence of being wrong is deleting everything.
            if (inventory.VNets.Count == 0 && plan.Items.Count > 0)
            {
                plan.Warnings.Add(
                    $"Azure reported no VNets at all in this subscription, so every one of the {plan.Items.Count} Azure-linked subnet(s) below is flagged as deleted. " +
                    "Confirm the subscription is the right one and really is empty before deleting anything.");
            }

            return plan;
        }

        /// <inheritdoc/>
        public void ApplyConfirmations(
            AzureReconcilePlanViewModel plan,
            IReadOnlyDictionary<string, AzureResourceConfirmation> confirmations)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(confirmations);

            if (plan.Items.Count == 0)
            {
                return;
            }

            List<AzureReconcileItem> keep = [];
            List<AzureReconcileItem> notVisible = [];
            List<AzureReconcileItem> unknown = [];
            List<AzureReconcileItem> stillLive = [];

            foreach (AzureReconcileItem item in plan.Items)
            {
                // A confirmation answers one question: is the resource gone? Only the absence
                // statuses ask it. A drift row was produced *because* the resource was found in the
                // listing with a different prefix, so Live is the expected answer for it and says
                // nothing about the drift - and a NotVisible or Unknown answer says nothing either,
                // because the prefix comparison already came from a successful read of that VNet.
                // Judging drift rows on this verdict withholds every one of them, permanently.
                if (!IsAbsenceStatus(item.Status))
                {
                    keep.Add(item);
                    continue;
                }

                // Absent from the map counts as unconfirmed. Only an explicit 404 survives.
                AzureResourceConfirmation verdict =
                    confirmations.TryGetValue(item.AzureResourceId, out AzureResourceConfirmation c)
                        ? c
                        : AzureResourceConfirmation.Unknown;

                switch (verdict)
                {
                    case AzureResourceConfirmation.Deleted:
                        keep.Add(item);
                        break;
                    case AzureResourceConfirmation.Live:
                        stillLive.Add(item);
                        break;
                    case AzureResourceConfirmation.NotVisible:
                        notVisible.Add(item);
                        break;
                    default:
                        // Unknown is a different fact from NotVisible and gets its own message. The
                        // action is identical - both are withheld - but "the credential lost access"
                        // is a guess when the truth is that Azure could not be asked, and an ARM
                        // throttle or a transport blip mid-scan produces Unknown on a subscription
                        // whose permissions are perfectly intact.
                        unknown.Add(item);
                        break;
                }
            }

            plan.Items = keep;

            if (notVisible.Count > 0)
            {
                plan.Warnings.Add(
                    $"{notVisible.Count} Azure-linked subnet(s) were missing from the subscription listing, and Azure denied " +
                    "access when asked about them directly - the credential may have lost access to their resource group. " +
                    $"They have been withheld from deletion: {NameList(notVisible)}.");
            }

            if (unknown.Count > 0)
            {
                plan.Warnings.Add(
                    $"{unknown.Count} Azure-linked subnet(s) were missing from the subscription listing, and Azure could not " +
                    "be asked about them - the read failed rather than answering. Nothing is wrong with the subnet itself; " +
                    $"try the scan again. They have been withheld from deletion: {NameList(unknown)}.");
            }

            if (stillLive.Count > 0)
            {
                // The listing and a direct read disagreed - most likely the resource was filtered
                // out of the list rather than removed. Either way it exists, so it is not deletable.
                plan.Warnings.Add(
                    $"{stillLive.Count} Azure-linked subnet(s) were missing from the subscription listing but still exist " +
                    $"in Azure, so they have been withheld from deletion: {NameList(stillLive)}.");
            }

            // Withholding a row means nothing while an ancestor that would archive it is still on
            // offer: approving the ancestor takes the whole subtree, including everything protected
            // above. ReviewItems belongs in the set as well - the loop above walks plan.Items only,
            // so a FullyAllocatingSubnetDeleted or UnrecognisedResourceId descendant appears in none
            // of the lists built from it, and ordinary imports produce the former.
            HashSet<int> withheld =
            [
                .. notVisible.Select(i => i.SubnetId),
                .. unknown.Select(i => i.SubnetId),
                .. stillLive.Select(i => i.SubnetId),
                .. plan.ReviewItems.Select(i => i.SubnetId)
            ];

            WithholdTargetsWhoseCascadeIsBlocked(
                plan, withheld,
                "archiving them would also archive subnet(s) beneath them that were withheld from deletion");
        }

        /// <summary>
        /// Drops every remaining item whose subtree contains a protected subnet, because archiving a
        /// target archives its whole subtree. <paramref name="protectedSubnetIds"/> holds the rows
        /// that must not be destroyed; <paramref name="because"/> completes the warning sentence.
        /// </summary>
        private static void WithholdTargetsWhoseCascadeIsBlocked(
            AzureReconcilePlanViewModel plan,
            HashSet<int> protectedSubnetIds,
            string because)
        {
            if (protectedSubnetIds.Count == 0 || plan.Items.Count == 0)
            {
                return;
            }

            List<AzureReconcileItem> blocked =
                [.. plan.Items.Where(i => i.DescendantSubnetIds.Any(protectedSubnetIds.Contains))];

            if (blocked.Count == 0)
            {
                return;
            }

            plan.Items.RemoveAll(blocked.Contains);
            plan.Warnings.Add(
                $"{blocked.Count} subnet(s) were withheld from deletion because {because}: {NameList(blocked)}.");
        }

        /// <summary>
        /// True for the statuses that assert the Azure resource no longer exists, and so are the only
        /// ones a direct read can confirm or contradict. Public so the caller that decides which IDs
        /// to read applies exactly the same rule <see cref="ApplyConfirmations"/> does, rather than
        /// restating it and letting the two drift apart.
        /// </summary>
        public static bool IsAbsenceStatus(AzureReconcileStatus status) =>
            status is AzureReconcileStatus.VNetDeleted or AzureReconcileStatus.SubnetDeleted;

        /// <summary>
        /// Reports every IPv4 range Azure has assigned inside an imported VNet that no Bastet subnet
        /// accounts for. Report-only: these rows name no Bastet subnet, so there is nothing to
        /// delete and nothing here is ever deletable.
        /// </summary>
        /// <remarks>
        /// Two things make this correct rather than noisy, and both were wrong in the finding's
        /// original proposal:
        ///
        /// CONTAINMENT, NOT EQUALITY. An IPAM routinely records a coarser allocation than Azure
        /// carves out of it - Bastet holds 10.90.64.0/18 and Azure creates 10.90.77.0/24 inside it.
        /// That range IS accounted for. Comparing {network, cidr} for equality would report it
        /// forever, and an operator who cannot silence a warning stops reading warnings.
        ///
        /// EVERY subnet, not just linked ones. A range created by hand carries no AzureResourceId -
        /// only the two import paths ever write that column - so matching against linked rows alone
        /// would report a range the operator had already corrected, permanently.
        ///
        /// Scoped to VNets that have actually been imported, or an unimported subscription produces
        /// an item per Azure subnet on every scan.
        /// </remarks>
        private void ReportAzureRangesNoBastetSubnetRecords(
            AzureReconcilePlanViewModel plan,
            AzureVNetInventory inventory,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets)
        {
            HashSet<string> importedVNetIds = new(
                existingSubnets
                    .Select(s => AzureResourceIdentity.VNetIdOf(s.AzureResourceId))
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => id!),
                StringComparer.OrdinalIgnoreCase);

            // GetVNetInventory emits ONE ROW PER PREFIX for a multi-prefix Azure subnet, and every
            // one of those rows carries the COMPLETE prefix list (round 13's BuildInventorySubnetRows,
            // deliberately, so the reconciler's resource-id index is safe whichever row lands last).
            // Walking rows x prefixes therefore visits an n-prefix subnet n^2 times, and without
            // this set the same range is reported n times over.
            HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);

            foreach (BulkAzureVNetViewModel vnet in inventory.VNets)
            {
                if (string.IsNullOrEmpty(vnet.ResourceId) || !importedVNetIds.Contains(vnet.ResourceId))
                {
                    continue;
                }

                foreach (BulkAzureSubnetViewModel subnet in vnet.Subnets)
                {
                    foreach (string prefix in Ipv4PrefixesOf(subnet))
                    {
                        string[] parts = prefix.Split('/');

                        if (parts.Length != 2 || !int.TryParse(parts[1], out int cidr))
                        {
                            continue;
                        }

                        if (!reported.Add($"{subnet.ResourceId}|{prefix}"))
                        {
                            continue;
                        }

                        if (IsRangeRecordedByBastet(existingSubnets, parts[0], cidr))
                        {
                            continue;
                        }

                        // When the unrecorded range is exactly a VNet-level target's own prefix, the
                        // remedy is the fully-allocated import, and it is worth naming: otherwise
                        // the operator only discovers it by opening the import wizard. If that
                        // target already has children the top-up refuses outright
                        // (AzureBulkImportPlanner: "…covers the whole prefix, which would mark
                        // Bastet subnet 'X' fully allocated, but it already has child subnets"), so
                        // the item is true and unclearable until the conflicting child is removed.
                        // Say which of the two it is rather than sending them to a wizard that will
                        // refuse.
                        ExistingSubnetSnapshot? wholePrefixTarget = existingSubnets.FirstOrDefault(e =>
                            AzureResourceIdentity.IsAzureVNet(e.AzureResourceId)
                            && !e.IsFullyAllocated
                            && string.Equals(e.NetworkAddress, parts[0], StringComparison.OrdinalIgnoreCase)
                            && e.Cidr == cidr);

                        string remedy = wholePrefixTarget is null
                            ? string.Empty
                            : wholePrefixTarget.HasChildSubnets
                                ? $" It covers the whole of BASTET subnet '{wholePrefixTarget.Name}'. Importing it would mark "
                                  + "that subnet fully allocated, which is refused while it still has child subnets, so remove "
                                  + "the children that conflict with it first."
                                : $" It covers the whole of BASTET subnet '{wholePrefixTarget.Name}'. Import it to mark that "
                                  + "subnet fully allocated.";

                        plan.ReviewItems.Add(new AzureReconcileItem
                        {
                            // No Bastet row exists - that is the whole point of the item. Zero is
                            // safe in the withheld set below because no real subnet has that id.
                            SubnetId = 0,
                            Name = subnet.Name,
                            NetworkAddress = parts[0],
                            Cidr = cidr,
                            AzureResourceId = subnet.ResourceId ?? string.Empty,
                            Status = AzureReconcileStatus.AzureRangeNotImported,
                            Reason = $"Azure subnet '{subnet.Name}' in VNet '{vnet.Name}' owns {prefix}, "
                                     + "which no BASTET subnet records. BASTET is reporting that range as free space."
                                     + remedy,
                            IsVNetLevel = false
                        });
                    }
                }
            }
        }

        /// <summary>
        /// True when BASTET already records an Azure range - it either IS a Bastet row, or no part
        /// of it is presented as free space.
        /// </summary>
        /// <remarks>
        /// ASK BASTET'S OWN QUESTION, NOT A PROXY FOR IT. The item this gates asserts "BASTET is
        /// reporting that range as free space", so the test has to be whether the free-space
        /// computation actually offers any of it - the same computation that renders the Details
        /// page. Anything weaker answers a different question and gets a different answer.
        ///
        /// Two proxies were tried and both are wrong, in opposite directions:
        ///
        /// "Any containing row records it" (the original) is too weak. ValidateSubnetCreation
        /// forces every subnet under its most specific container, so an install modelling a
        /// top-down plan - a 10/8 root, a regional /18 aggregate - necessarily has a containing row
        /// above every import target. One ordinary hand-created aggregate with no children then
        /// silenced the whole inbound direction beneath it, while its own Details page printed the
        /// Azure-owned range as free with a Create Subnet button over it.
        ///
        /// "The containing row must be marked IsFullyAllocated" is too strong, and it is the one
        /// that looks right. A partially tiled parent - a /18 holding 10.60.1.0/25 and
        /// 10.60.1.128/25 - is not fully allocated, yet its free-space table correctly does not
        /// offer 10.60.1.0/24. Under that gate the /24 becomes an item that is false on the item's
        /// own wording, cannot be cleared by creating the subnet (the two /25s occupy it), and can
        /// only be silenced by marking the /18 fully allocated, which hides genuine free space
        /// elsewhere. An operator who cannot silence a warning stops reading warnings.
        ///
        /// So: the deepest containing row is the one an operator would allocate from, and the range
        /// is recorded exactly when that row's free space does not reach into it. IsFullyAllocated
        /// survives only as a silencer, where it is sound - nothing can be created under such a row
        /// at all.
        ///
        /// Every item this raises is clearable by the action its text implies, which is the property
        /// the weaker tests could not offer: "offered as free" is precisely the condition under
        /// which creating or importing a subnet for that range succeeds.
        /// </remarks>
        private bool IsRangeRecordedByBastet(
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets,
            string network,
            int cidr)
        {
            ExistingSubnetSnapshot? exact = existingSubnets.FirstOrDefault(e =>
                string.Equals(e.NetworkAddress, network, StringComparison.OrdinalIgnoreCase)
                && e.Cidr == cidr);

            if (exact is not null)
            {
                // ...but only once the fully-allocated import it stands for has actually happened.
                // An Azure subnet covering a whole VNet prefix is recorded by marking the target
                // fully allocated; when the target is linked and NOT marked, nothing recorded it,
                // and this is the largest range it is possible to be wrong about. Three routes
                // reach that state without any crafted request: the bulk wizard's default selection
                // ticks no subnets, an empty VNet imported before Azure created the covering subnet,
                // and one click on "Mark as Not Fully Allocated".
                return !AzureResourceIdentity.IsAzureVNet(exact.AzureResourceId) || exact.IsFullyAllocated;
            }

            // The row the operator would allocate from: the most specific one containing the range.
            // Containment is strict, so an exactly-equal row is not a container - that case is the
            // equality arm above.
            ExistingSubnetSnapshot? deepest = existingSubnets
                .Where(e => ipUtilityService.IsSubnetContainedInParent(network, cidr, e.NetworkAddress, e.Cidr))
                .OrderByDescending(e => e.Cidr)
                .FirstOrDefault();

            if (deepest is null)
            {
                // No row contains it at all, so no row records it.
                return false;
            }

            if (deepest.IsFullyAllocated)
            {
                // Nothing can be created under a fully-allocated row (SubnetController.Helpers
                // refuses it outright), so BASTET cannot hand this range out and does not present
                // it as available. This is the only sound use of the flag here - as a silencer,
                // never as the positive test for "recorded", which is what it cannot support.
                return true;
            }

            // The question the item's own text asks: does BASTET present any part of this range as
            // free space? Answered with the SAME computation that renders the Details page, so the
            // reconciler and the screen cannot disagree - the range is recorded exactly when no
            // part of it is left unallocated by the rows sitting inside it.
            List<Subnet> rowsInsideTheRange = [.. existingSubnets
                .Where(e => ipUtilityService.IsSubnetContainedInParent(e.NetworkAddress, e.Cidr, network, cidr))
                .Select(e => new Subnet { NetworkAddress = e.NetworkAddress, Cidr = e.Cidr })];

            return !ipUtilityService.CalculateUnallocatedRanges(network, cidr, rowsInsideTheRange).Any();
        }

        /// <summary>
        /// True when an Azure prefix string overlaps a row's recorded range, in either direction.
        /// A prefix that cannot be parsed overlaps nothing: the caller's fallback is the deletable
        /// status, and inventing coverage from a malformed string would withhold a real deletion.
        /// </summary>
        private bool OverlapsRecorded(string azurePrefix, AzureLinkedSubnetSnapshot snapshot)
        {
            string[] parts = azurePrefix.Split('/');

            return parts.Length == 2
                   && int.TryParse(parts[1], out int cidr)
                   && (ipUtilityService.IsSubnetContainedInParent(parts[0], cidr, snapshot.NetworkAddress, snapshot.Cidr)
                       || ipUtilityService.IsSubnetContainedInParent(snapshot.NetworkAddress, snapshot.Cidr, parts[0], cidr));
        }

        /// <summary>An Azure subnet that currently holds a given IPv4 range.</summary>
        private sealed record AzurePrefixOwner(string ResourceId, string SubnetName, string VNetName);

        /// <summary>One live Azure prefix, pre-split so the overlap test does not re-parse it per row.</summary>
        private sealed record AzureLivePrefix(string Prefix, string Network, int Cidr, AzurePrefixOwner Owner);

        /// <summary>
        /// A live Azure prefix still covering some or all of a stale row's recorded range.
        /// <paramref name="Exact"/> distinguishes the two cases the caller must treat differently:
        /// an exactly-equal owner is a rename and Re-link repairs it, an overlapping owner is a
        /// re-carve and Re-link would point the row at a range it does not record.
        /// </summary>
        private sealed record LiveRangeOwner(AzurePrefixOwner Owner, string LivePrefix, bool Exact);

        /// <summary>Index key: a range is only comparable within the VNet that carries it.</summary>
        private static string PrefixKey(string vnetResourceId, string prefix) => $"{vnetResourceId}|{prefix}";

        /// <summary>
        /// The live Azure subnet still holding this row's range under a *different* resource ID, or
        /// null. Only asked for rows already judged stale, and only within the row's own VNet.
        /// </summary>
        /// <remarks>
        /// The row's own resource ID is excluded from the EXACT arm only, where it is the degenerate
        /// case of a subnet reported twice by a paged read and treating the row as its own evidence
        /// would withhold every genuine deletion.
        ///
        /// It is NOT excluded from the overlap arm, and that distinction is load-bearing. The
        /// justification for excluding it - "a row whose own resource still owns the range is not
        /// stale in the first place" - is true only of the exact range: EvaluateSubnetLevel tests
        /// membership of the EXACT recorded prefix, so a row can be stale while its own resource
        /// still holds a subset or superset. Narrowing a subnet in place preserves the ARM resource
        /// id, so the subnet still holding the range is the row's own - and excluding it offered the
        /// row for irreversible archive while Azure held the range.
        ///
        /// EQUALITY IS NOT ENOUGH. Matching prefix strings only asks "is this exact range still
        /// assigned?", and Azure has no subnet rename - re-organising one is delete-and-recreate,
        /// and re-carving the prefix while doing so is ordinary. One such event produces two rows
        /// with opposite verdicts: the row whose prefix string survived is protected, and the
        /// re-carved one is offered for irreversible archive on a plan that states no fact about
        /// the range Azure is holding. So the exact key is kept as the cheap first test and an
        /// overlap test in both directions is the fallback. Overlap, not containment one way: a
        /// re-carve can narrow (/24 -> /25) or widen (/25 -> /24), and for an IPAM the safe answer
        /// to "part of this range is still assigned" is the same either way.
        /// </remarks>
        private LiveRangeOwner? FindLiveOwnerOfRange(
            AzureLinkedSubnetSnapshot snapshot,
            AzureReconcileItem item,
            Dictionary<string, List<AzurePrefixOwner>> livePrefixOwners,
            Dictionary<string, List<AzureLivePrefix>> livePrefixesByVNet)
        {
            // FullyAllocatingSubnetDeleted and UnrecognisedResourceId are review-only already and
            // delete nothing, so re-routing them would only muddy the reason they carry.
            if (item.Status is not (AzureReconcileStatus.VNetDeleted
                or AzureReconcileStatus.VNetPrefixRemoved
                or AzureReconcileStatus.SubnetDeleted
                or AzureReconcileStatus.SubnetPrefixChanged))
            {
                return null;
            }

            string? vnetId = AzureResourceIdentity.VNetIdOf(snapshot.AzureResourceId);

            if (vnetId is null)
            {
                return null;
            }

            string recorded = $"{snapshot.NetworkAddress}/{snapshot.Cidr}";
            string key = PrefixKey(vnetId, recorded);

            if (livePrefixOwners.TryGetValue(key, out List<AzurePrefixOwner>? owners))
            {
                AzurePrefixOwner? exact = owners.FirstOrDefault(o =>
                    !string.Equals(o.ResourceId, snapshot.AzureResourceId, StringComparison.OrdinalIgnoreCase));

                if (exact is not null)
                {
                    return new LiveRangeOwner(exact, recorded, true);
                }
            }

            if (!livePrefixesByVNet.TryGetValue(vnetId, out List<AzureLivePrefix>? candidates))
            {
                return null;
            }

            // The row's own resource is NOT excluded here, unlike the equality arm above. Narrowing
            // a subnet in place - `az network vnet subnet update --address-prefixes` - preserves the
            // ARM resource id, so the subnet still holding the range IS the row's own resource. Only
            // the degenerate same-resource-same-prefix case is excluded, and the exact index above
            // has already answered that one.
            AzureLivePrefix? overlapping = candidates.FirstOrDefault(c =>
                !(string.Equals(c.Owner.ResourceId, snapshot.AzureResourceId, StringComparison.OrdinalIgnoreCase)
                  && string.Equals(c.Prefix, recorded, StringComparison.OrdinalIgnoreCase))
                && (ipUtilityService.IsSubnetContainedInParent(c.Network, c.Cidr, snapshot.NetworkAddress, snapshot.Cidr)
                    || ipUtilityService.IsSubnetContainedInParent(snapshot.NetworkAddress, snapshot.Cidr, c.Network, c.Cidr)));

            return overlapping is null ? null : new LiveRangeOwner(overlapping.Owner, overlapping.Prefix, false);
        }

        /// <summary>Comma-separated "'subnet' in VNet 'vnet'", capped so a warning stays readable.</summary>
        private static string OwnerList(List<AzurePrefixOwner> owners)
        {
            const int Max = 10;
            string names = string.Join(", ", owners.Take(Max).Select(o => $"'{o.SubnetName}' in VNet '{o.VNetName}'"));
            return owners.Count > Max ? $"{names} and {owners.Count - Max} more" : names;
        }

        /// <summary>Comma-separated subnet names, capped so a warning stays readable.</summary>
        private static string NameList(List<AzureReconcileItem> items)
        {
            const int Max = 10;
            string names = string.Join(", ", items.Take(Max).Select(i => $"'{i.Name}'"));
            return items.Count > Max ? $"{names} and {items.Count - Max} more" : names;
        }

        /// <summary>
        /// A row whose recorded resource ID is a VNet: the target a VNet address prefix was imported into.
        /// </summary>
        private AzureReconcileItem? EvaluateVNetLevel(
            AzureLinkedSubnetSnapshot snapshot,
            Dictionary<string, BulkAzureVNetViewModel> liveVNets)
        {
            string prefix = $"{snapshot.NetworkAddress}/{snapshot.Cidr}";

            if (!liveVNets.TryGetValue(snapshot.AzureResourceId, out BulkAzureVNetViewModel? vnet))
            {
                // The inventory only carries VNets that still have IPv4 address space, so an absent
                // VNet means either it is gone or it has none left. Both justify removing the import,
                // but they are not the same fact and this reason sits directly above a Delete button.
                return Item(snapshot, AzureReconcileStatus.VNetDeleted, true,
                    "The VNet this subnet was imported from no longer exists in Azure, " +
                    "or no longer has any IPv4 address space.");
            }

            if (!vnet.Ipv4AddressPrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            {
                // The prefix string is gone, which is not the same as the space being released.
                // Resizing a VNet's address range, or re-carving it into several prefixes, is
                // ordinary - and VNetPrefixRemoved is deletable with no ARM confirmation behind it
                // (IsAbsenceStatus covers only the two "deleted" statuses), while the range index
                // FindLiveOwnerOfRange consults is built from SUBNET prefixes and so has no entry
                // for a VNet address prefix at all. Both defences are therefore silent here, and
                // the row is archived while Azure still covers every address it records.
                //
                // Overlap, not containment by a single prefix: re-carving 10.190.0.0/16 into
                // 10.190.0.0/17 + 10.190.128.0/17 releases nothing, and neither /17 contains the
                // /16, so a containment test never fires. A shrink is the same class in reverse.
                string? covering = vnet.Ipv4AddressPrefixes.FirstOrDefault(p => OverlapsRecorded(p, snapshot));

                if (covering is not null)
                {
                    return Item(snapshot, AzureReconcileStatus.VNetPrefixStillCovered, true,
                        $"VNet '{vnet.Name}' no longer has the address prefix {prefix}, but its address space "
                        + $"now includes {covering}, which overlaps that range - so the space was resized or "
                        + "re-carved rather than released. Archiving this subnet would remove BASTET's only "
                        + "record of a range Azure still covers. Correct the recorded range to match the VNet's "
                        + "current address space, or delete this subnet and import the current prefix again.");
                }

                return Item(snapshot, AzureReconcileStatus.VNetPrefixRemoved, true,
                    $"VNet '{vnet.Name}' still exists but no longer has the address prefix {prefix}.");
            }

            // The VNet and the prefix are both live. The only remaining drift is a fully-allocated
            // marker whose cause has disappeared: import sets it when an Azure subnet covers the
            // target's whole prefix, so if no such subnet remains, whatever justified it is gone.
            // Report only - the flag can also be set by hand, so we must not act on it.
            if (snapshot.IsFullyAllocated
                && !vnet.Subnets.Any(s => Ipv4PrefixesOf(s).Contains(prefix, StringComparer.OrdinalIgnoreCase)))
            {
                return Item(snapshot, AzureReconcileStatus.FullyAllocatingSubnetDeleted, true,
                    $"Marked fully allocated, but no Azure subnet in VNet '{vnet.Name}' covers {prefix} any more. " +
                    "Nothing needs deleting; review whether it should still be marked fully allocated.");
            }

            return null;
        }

        /// <summary>
        /// A row whose recorded resource ID is an Azure subnet: an imported child.
        /// </summary>
        private static AzureReconcileItem? EvaluateSubnetLevel(
            AzureLinkedSubnetSnapshot snapshot,
            Dictionary<string, List<string>> liveSubnetPrefixes)
        {
            string prefix = $"{snapshot.NetworkAddress}/{snapshot.Cidr}";

            if (!liveSubnetPrefixes.TryGetValue(snapshot.AzureResourceId, out List<string>? livePrefixes))
            {
                return Item(snapshot, AzureReconcileStatus.SubnetDeleted, false,
                    "The Azure subnet this was imported from no longer exists.");
            }

            // Membership, not equality, and for the same reason the VNet-level check above uses it:
            // an Azure subnet may own several IPv4 prefixes, and the one Bastet recorded need not be
            // the first. Comparing against a single collapsed value reports drift on a subnet that
            // still owns the prefix - and a drift row is offered for deletion with no Azure read
            // behind it.
            if (!livePrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            {
                string live = livePrefixes.Count == 0 ? "none" : string.Join(", ", livePrefixes);
                return Item(snapshot, AzureReconcileStatus.SubnetPrefixChanged, false,
                    $"The Azure subnet still exists but its address prefix is now {live}, not {prefix}.");
            }

            return null;
        }

        /// <summary>
        /// Every IPv4 prefix an inventory subnet owns. GetVNetInventory populates the list, but a
        /// caller that only sets the scalar must not silently compare against an empty set, so the
        /// scalar is the fallback.
        /// </summary>
        private static List<string> Ipv4PrefixesOf(BulkAzureSubnetViewModel subnet) =>
            subnet.Ipv4AddressPrefixes.Count > 0
                ? subnet.Ipv4AddressPrefixes
                : string.IsNullOrEmpty(subnet.AddressPrefix) ? [] : [subnet.AddressPrefix];

        private static AzureReconcileItem Item(
            AzureLinkedSubnetSnapshot snapshot,
            AzureReconcileStatus status,
            bool isVNetLevel,
            string reason) =>
            new()
            {
                SubnetId = snapshot.Id,
                Name = snapshot.Name,
                NetworkAddress = snapshot.NetworkAddress,
                Cidr = snapshot.Cidr,
                AzureResourceId = snapshot.AzureResourceId,
                Status = status,
                Reason = reason,
                IsVNetLevel = isVNetLevel,
                DescendantCount = snapshot.DescendantCount,
                HostIpCount = snapshot.HostIpCount,
                DescendantSubnetIds = snapshot.DescendantSubnetIds
            };

        /// <summary>
        /// True when an ARM resource ID sits under the given subscription. Matches the
        /// "/subscriptions/{id}/" segment rather than a bare substring, so a subscription ID that
        /// happens to appear elsewhere in the path cannot produce a false match.
        /// </summary>
        private static bool BelongsToSubscription(string resourceId, string subscriptionId) =>
            resourceId.StartsWith($"/subscriptions/{subscriptionId}/", StringComparison.OrdinalIgnoreCase);
    }
}
