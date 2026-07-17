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
        /// <summary>
        /// Segment that distinguishes an Azure subnet's resource ID from its VNet's. Same test the
        /// subnet Details view uses to decide how to build its Azure portal link.
        /// </summary>
        private const string SubnetSegment = "/subnets/";

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
            Dictionary<string, string> liveSubnetPrefixes = new(StringComparer.OrdinalIgnoreCase);

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
                        liveSubnetPrefixes[subnet.ResourceId] = subnet.AddressPrefix;
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

                AzureReconcileItem? item = snapshot.AzureResourceId.Contains(SubnetSegment, StringComparison.OrdinalIgnoreCase)
                    ? EvaluateSubnetLevel(snapshot, liveSubnetPrefixes)
                    : EvaluateVNetLevel(snapshot, liveVNets);

                if (item is null)
                {
                    continue;
                }

                if (item.Status == AzureReconcileStatus.FullyAllocatingSubnetDeleted)
                {
                    plan.ReviewItems.Add(item);
                }
                else
                {
                    plan.Items.Add(item);
                }
            }

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
                return Item(snapshot, AzureReconcileStatus.VNetDeleted, true,
                    $"The VNet this subnet was imported from no longer exists in Azure.");
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
                && !vnet.Subnets.Any(s => string.Equals(s.AddressPrefix, prefix, StringComparison.OrdinalIgnoreCase)))
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
            Dictionary<string, string> liveSubnetPrefixes)
        {
            string prefix = $"{snapshot.NetworkAddress}/{snapshot.Cidr}";

            if (!liveSubnetPrefixes.TryGetValue(snapshot.AzureResourceId, out string? livePrefix))
            {
                return Item(snapshot, AzureReconcileStatus.SubnetDeleted, false,
                    "The Azure subnet this was imported from no longer exists.");
            }

            if (!string.Equals(livePrefix, prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Item(snapshot, AzureReconcileStatus.SubnetPrefixChanged, false,
                    $"The Azure subnet still exists but its address prefix is now {livePrefix}, not {prefix}.");
            }

            return null;
        }

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
