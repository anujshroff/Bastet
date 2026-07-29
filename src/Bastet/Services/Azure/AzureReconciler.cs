using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{
    /// <summary>
    /// Default <see cref="IAzureReconciler"/> implementation. Pure (no DB, no Azure calls) so the
    /// rules that decide what may be deleted can be tested exhaustively, mirroring
    /// <see cref="AzureBulkImportPlanner"/>.
    /// </summary>
    public class AzureReconciler : IAzureReconciler
    {
        /// <inheritdoc/>
        public AzureReconcilePlanViewModel BuildPlan(
            string subscriptionId,
            string? subscriptionName,
            AzureVNetInventory inventory,
            IReadOnlyList<AzureLinkedSubnetSnapshot> linkedSubnets)
        {
            ArgumentNullException.ThrowIfNull(inventory);
            ArgumentNullException.ThrowIfNull(linkedSubnets);

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
                }
            }

            foreach (AzureLinkedSubnetSnapshot snapshot in linkedSubnets)
            {
                if (string.IsNullOrEmpty(snapshot.AzureResourceId))
                {
                    continue;
                }

                // Only reconcile what this scan actually covers. A subnet belonging to another
                // subscription is out of scope, not stale.
                if (!BelongsToSubscription(snapshot.AzureResourceId, subscriptionId))
                {
                    continue;
                }

                // Three-way, not two. An ID that is neither a VNet nor a subnet used to fall down
                // the VNet branch, where absence from the listing reads as VNetDeleted - a claim
                // nothing established, on the one path that removes data. It is reported for review
                // instead, so the operator can correct the row rather than have it silently offered
                // for archival.
                AzureReconcileItem? item;
                if (AzureResourceIdentity.IsAzureSubnet(snapshot.AzureResourceId))
                {
                    item = EvaluateSubnetLevel(snapshot, liveSubnetPrefixes);
                }
                else if (AzureResourceIdentity.IsAzureVNet(snapshot.AzureResourceId))
                {
                    item = EvaluateVNetLevel(snapshot, liveVNets);
                }
                else
                {
                    item = Item(snapshot, AzureReconcileStatus.UnrecognisedResourceId, true,
                        "The recorded Azure resource ID names neither a VNet nor a subnet, so nothing "
                        + "can be established about it. Correct or clear the link on this subnet.");
                }

                if (item is null)
                {
                    // Evaluated against a successful read and found live: the VNet or Azure subnet
                    // is there and still carries the recorded prefix. Nothing downstream ever sees
                    // this row again - it becomes neither an item nor a review item - so the only
                    // place it can protect an ancestor from the cascade is here.
                    liveLinked.Add(snapshot.Id);
                    continue;
                }

                if (item.Status is AzureReconcileStatus.FullyAllocatingSubnetDeleted
                    or AzureReconcileStatus.UnrecognisedResourceId)
                {
                    plan.ReviewItems.Add(item);
                }
                else
                {
                    plan.Items.Add(item);
                }
            }

            WithholdTargetsWhoseCascadeIsBlocked(
                plan, liveLinked,
                "archiving them would also archive Azure-linked subnet(s) beneath them that still exist in Azure");

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
        private static AzureReconcileItem? EvaluateVNetLevel(
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
