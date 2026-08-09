using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{

    public class AzureReconciler : IAzureReconciler
    {

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

            Dictionary<string, BulkAzureVNetViewModel> liveVNets = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> liveSubnetPrefixes = new(StringComparer.OrdinalIgnoreCase);

            HashSet<int> liveLinked = [];
            HashSet<int> notCovered = [];
            List<AzureReconcileItem> heldByManualContent = [];

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

                bool recognised = AzureResourceIdentity.IsAzureSubnet(snapshot.AzureResourceId)
                                  || AzureResourceIdentity.IsAzureVNet(snapshot.AzureResourceId);

                if (!recognised)
                {
                    plan.ReviewItems.Add(Item(snapshot, AzureReconcileStatus.UnrecognisedResourceId, true,
                        "The recorded Azure resource ID names neither a VNet nor a subnet, so nothing "
                        + "can be established about it. Correct or clear the link on this subnet."));
                    continue;
                }

                if (!BelongsToSubscription(snapshot.AzureResourceId, subscriptionId))
                {
                    notCovered.Add(snapshot.Id);
                    continue;
                }

                AzureReconcileItem? item = AzureResourceIdentity.IsAzureSubnet(snapshot.AzureResourceId)
                    ? EvaluateSubnetLevel(snapshot, liveSubnetPrefixes)
                    : EvaluateVNetLevel(snapshot, liveVNets);

                if (item is null)
                {
                    liveLinked.Add(snapshot.Id);
                    continue;
                }

                if (snapshot.ManualDescendantCount > 0 || snapshot.HostIpCount > 0)
                {
                    item.Status = AzureReconcileStatus.HeldByManualContent;
                    item.Reason = $"{item.Reason} {DescribeManualContent(snapshot)} "
                        + "BASTET will not delete it, because Azure has no record of that and it would be "
                        + "destroyed with no way to restore it. Move or delete it here first, then run the scan again.";
                    plan.ReviewItems.Add(item);
                    heldByManualContent.Add(item);
                    continue;
                }

                plan.Items.Add(item);
            }

            if (heldByManualContent.Count > 0)
            {
                plan.Warnings.Add(
                    $"{heldByManualContent.Count} subnet(s) were withheld from deletion because they hold subnets or "
                    + "host IP assignments that were created here rather than imported from Azure: "
                    + $"{NameList(heldByManualContent)}.");
            }

            WithholdTargetsWhoseCascadeIsBlocked(
                plan, liveLinked,
                "archiving them would also archive Azure-linked subnet(s) beneath them that still exist in Azure");

            WithholdTargetsWhoseCascadeIsBlocked(
                plan, notCovered,
                "archiving them would also archive Azure-linked subnet(s) beneath them that belong to a "
                + "different subscription and were not checked by this scan");

            WithholdTargetsWhoseCascadeIsBlocked(
                plan, [.. heldByManualContent.Select(i => i.SubnetId)],
                "archiving them would also archive subnet(s) beneath them that hold manually created content");

            if (inventory.VNets.Count == 0 && plan.Items.Count > 0)
            {
                plan.Warnings.Add(
                    $"Azure reported no VNets at all in this subscription, so every one of the {plan.Items.Count} Azure-linked subnet(s) below is flagged as deleted. " +
                    "Confirm the subscription is the right one and really is empty before deleting anything.");
            }

            return plan;
        }

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

                if (!IsAbsenceStatus(item.Status))
                {
                    keep.Add(item);
                    continue;
                }

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

                plan.Warnings.Add(
                    $"{stillLive.Count} Azure-linked subnet(s) were missing from the subscription listing but still exist " +
                    $"in Azure, so they have been withheld from deletion: {NameList(stillLive)}.");
            }

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

        private static string DescribeManualContent(AzureLinkedSubnetSnapshot snapshot)
        {
            string subnets = snapshot.ManualDescendantCount == 1
                ? "1 subnet created here"
                : $"{snapshot.ManualDescendantCount} subnets created here";

            string hostIps = snapshot.HostIpCount == 1
                ? "1 host IP assignment"
                : $"{snapshot.HostIpCount} host IP assignments";

            return snapshot.ManualDescendantCount > 0 && snapshot.HostIpCount > 0
                ? $"It holds {subnets} and {hostIps}."
                : snapshot.ManualDescendantCount > 0
                    ? $"It holds {subnets}."
                    : $"It holds {hostIps}.";
        }

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

        public static bool IsAbsenceStatus(AzureReconcileStatus status) =>
            status is AzureReconcileStatus.VNetDeleted or AzureReconcileStatus.SubnetDeleted;

        private static string NameList(List<AzureReconcileItem> items)
        {
            const int Max = 10;
            string names = string.Join(", ", items.Take(Max)
                .Select(i => $"'{i.Name}' ({i.NetworkAddress}/{i.Cidr})"));
            return items.Count > Max ? $"{names} and {items.Count - Max} more" : names;
        }

        private static AzureReconcileItem? EvaluateVNetLevel(
            AzureLinkedSubnetSnapshot snapshot,
            Dictionary<string, BulkAzureVNetViewModel> liveVNets)
        {
            string prefix = $"{snapshot.NetworkAddress}/{snapshot.Cidr}";

            if (!liveVNets.TryGetValue(snapshot.AzureResourceId, out BulkAzureVNetViewModel? vnet))
            {

                return Item(snapshot, AzureReconcileStatus.VNetDeleted, true,
                    "The VNet this subnet was imported from no longer exists in Azure, " +
                    "or no longer has any IPv4 address space.");
            }

            return !vnet.Ipv4AddressPrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase)
                ? Item(snapshot, AzureReconcileStatus.VNetPrefixRemoved, true,
                    $"VNet '{vnet.Name}' still exists but no longer has the address prefix {prefix}. "
                    + "Delete it here if you want to, then use the Azure import wizard to bring in the "
                    + "VNet's current address space.")
                : null;
        }

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

            if (!livePrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            {
                string live = livePrefixes.Count == 0 ? "none" : string.Join(", ", livePrefixes);
                return Item(snapshot, AzureReconcileStatus.SubnetPrefixChanged, false,
                    $"The Azure subnet still exists but its address prefix is now {live}, not {prefix}. "
                    + "Delete it here if you want to, then use the Azure import wizard to bring in its "
                    + "current prefix.");
            }

            return null;
        }

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

        private static bool BelongsToSubscription(string resourceId, string subscriptionId) =>
            resourceId.StartsWith($"/subscriptions/{subscriptionId}/", StringComparison.OrdinalIgnoreCase);
    }
}
