using Bastet.Models;
using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{

    public class AzureReconciler(IIpUtilityService ipUtilityService) : IAzureReconciler
    {

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

            List<AzurePrefixOwner> rangeStillAllocated = [];

            Dictionary<string, List<AzurePrefixOwner>> livePrefixOwners = new(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, List<AzureLivePrefix>> livePrefixesByVNet = new(StringComparer.OrdinalIgnoreCase);

            List<AzureSubscriptionPrefix> livePrefixesInSubscription = [];

            HashSet<string> recordedVNetIds = new(
                linkedSubnets
                    .Select(l => AzureResourceIdentity.VNetIdOf(l.AzureResourceId))
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(v => v!),
                StringComparer.OrdinalIgnoreCase);

            foreach (BulkAzureVNetViewModel vnet in inventory.VNets)
            {
                if (!string.IsNullOrEmpty(vnet.ResourceId))
                {
                    liveVNets[vnet.ResourceId] = vnet;

                    foreach (string vnetPrefix in vnet.Ipv4AddressPrefixes)
                    {
                        string[] vnetParts = vnetPrefix.Split('/');

                        if (vnetParts.Length == 2 && int.TryParse(vnetParts[1], out int vnetPrefixCidr))
                        {
                            livePrefixesInSubscription.Add(new AzureSubscriptionPrefix(
                                vnetPrefix, vnetParts[0], vnetPrefixCidr,
                                new AzurePrefixOwner(vnet.ResourceId, vnet.Name, vnet.Name),
                                vnet.ResourceId, true));
                        }
                    }
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

                            livePrefixesInSubscription.Add(new AzureSubscriptionPrefix(
                                prefix, parts[0], prefixCidr, owner, vnet.ResourceId, false));
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

                LiveRangeOwner? stillAllocated = FindLiveOwnerOfRange(snapshot, item, livePrefixOwners, livePrefixesByVNet, livePrefixesInSubscription, liveVNets, recordedVNetIds);

                if (stillAllocated is not null)
                {

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
                        : stillAllocated.OwnerIsVNetAddressSpace
                        ? $"{item.Reason} VNet '{stillAllocated.Owner.VNetName}' declares the address space "
                          + $"{stillAllocated.LivePrefix}, which overlaps the recorded range "
                          + $"{snapshot.NetworkAddress}/{snapshot.Cidr}, so archiving this subnet would make BASTET "
                          + "report an allocated range as free. Re-link is not offered because that VNet is not the one "
                          + "this subnet was imported from: delete this BASTET subnet and import the current range again."
                        : $"{item.Reason} Azure subnet '{stillAllocated.Owner.SubnetName}' in VNet "
                          + $"'{stillAllocated.Owner.VNetName}' now holds {stillAllocated.LivePrefix}, which overlaps the "
                          + $"recorded range {snapshot.NetworkAddress}/{snapshot.Cidr}, so archiving this subnet would make "
                          + "BASTET report an allocated range as free. Re-link is not offered because the live range is not "
                          + "the recorded one: either restore the Azure subnet at the recorded prefix, or delete this "
                          + "BASTET subnet and import the current range again - its recorded range cannot be edited "
                          + "while it is linked to Azure.";

                    AzureReconcileItem review = Item(snapshot, AzureReconcileStatus.RangeStillAllocatedInAzure, item.IsVNetLevel, reason);

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

            ReportAzureRangesNoBastetSubnetRecords(plan, inventory, existingSubnets);

            if (rangeStillAllocated.Count > 0)
            {

                plan.Warnings.Add(
                    $"{rangeStillAllocated.Count} subnet(s) were withheld from deletion because a live Azure resource "
                    + "still overlaps the range they record. "
                    + "Archiving them would make BASTET report an allocated range as free space: "
                    + $"{OwnerList(rangeStillAllocated)}.");
            }

            WithholdTargetsWhoseCascadeIsBlocked(
                plan, liveLinked,
                "archiving them would also archive Azure-linked subnet(s) beneath them that still exist in Azure");

            WithholdTargetsWhoseCascadeIsBlocked(
                plan, notCovered,
                "archiving them would also archive Azure-linked subnet(s) beneath them that belong to a "
                + "different subscription and were not checked by this scan");

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

            HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);

            foreach (BulkAzureVNetViewModel vnet in inventory.VNets)
            {
                if (string.IsNullOrEmpty(vnet.ResourceId) || !importedVNetIds.Contains(vnet.ResourceId))
                {
                    continue;
                }

                List<(string Network, int Cidr)> reportedVNetPrefixes = [];

                foreach (string vnetPrefix in vnet.Ipv4AddressPrefixes)
                {
                    string[] vnetParts = vnetPrefix.Split('/');

                    if (vnetParts.Length != 2 || !int.TryParse(vnetParts[1], out int vnetCidr))
                    {
                        continue;
                    }

                    if (existingSubnets.Any(e =>
                            string.Equals(e.NetworkAddress, vnetParts[0], StringComparison.OrdinalIgnoreCase)
                            && e.Cidr == vnetCidr)
                        || IsRangeRecordedByBastet(existingSubnets, vnetParts[0], vnetCidr))
                    {
                        continue;
                    }

                    if (!reported.Add($"{vnet.ResourceId}|{vnetPrefix}"))
                    {
                        continue;
                    }

                    reportedVNetPrefixes.Add((vnetParts[0], vnetCidr));

                    plan.ReviewItems.Add(new AzureReconcileItem
                    {
                        SubnetId = 0,
                        Name = vnet.Name,
                        NetworkAddress = vnetParts[0],
                        Cidr = vnetCidr,
                        AzureResourceId = vnet.ResourceId ?? string.Empty,
                        Status = AzureReconcileStatus.AzureRangeNotImported,
                        Reason = $"VNet '{vnet.Name}' declares the address space {vnetPrefix}, "
                                 + "which no BASTET subnet records."
                                 + (BastetOffersAnyOf(existingSubnets, vnetParts[0], vnetCidr)
                                    ? " BASTET is reporting that range as free space."
                                    : string.Empty),
                        IsVNetLevel = true
                    });
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

                        if (reportedVNetPrefixes.Any(p =>
                                (p.Cidr == cidr && string.Equals(p.Network, parts[0], StringComparison.OrdinalIgnoreCase))
                                || ipUtilityService.IsSubnetContainedInParent(parts[0], cidr, p.Network, p.Cidr)))
                        {
                            continue;
                        }

                        if (IsRangeRecordedByBastet(existingSubnets, parts[0], cidr))
                        {
                            continue;
                        }

                        ExistingSubnetSnapshot? wholePrefixTarget = existingSubnets.FirstOrDefault(e =>
                            AzureResourceIdentity.IsAzureVNet(e.AzureResourceId)
                            && !e.IsFullyAllocated
                            && string.Equals(e.NetworkAddress, parts[0], StringComparison.OrdinalIgnoreCase)
                            && e.Cidr == cidr);

                        string remedy = wholePrefixTarget is null
                            ? string.Empty
                            : wholePrefixTarget.HasHostIpAssignments
                                ? $" Importing '{subnet.Name}' would mark '{wholePrefixTarget.Name}' fully allocated, which "
                                  + "is refused while it has host IP assignments, so remove those first."
                                : wholePrefixTarget.HasChildSubnets
                                ? $" Importing '{subnet.Name}' would mark '{wholePrefixTarget.Name}' fully allocated, which "
                                  + "is refused while it still has child subnets, so remove the children that conflict with "
                                  + "it first."
                                : $" Import '{subnet.Name}' to mark '{wholePrefixTarget.Name}' fully allocated.";

                        plan.ReviewItems.Add(new AzureReconcileItem
                        {

                            SubnetId = 0,
                            Name = subnet.Name,
                            NetworkAddress = parts[0],
                            Cidr = cidr,
                            AzureResourceId = subnet.ResourceId ?? string.Empty,
                            Status = AzureReconcileStatus.AzureRangeNotImported,
                            Reason = (wholePrefixTarget is null
                                        ? $"Azure subnet '{subnet.Name}' in VNet '{vnet.Name}' owns {prefix}, "
                                          + WhatBastetRecordsOf(existingSubnets, parts[0], cidr)
                                        : $"Azure subnet '{subnet.Name}' in VNet '{vnet.Name}' owns {prefix}. BASTET subnet "
                                          + $"'{wholePrefixTarget.Name}' holds exactly that range but is not marked fully "
                                          + "allocated, so BASTET does not record it as allocated."
                                          + (BastetOffersAnyOf(existingSubnets, parts[0], cidr)
                                             ? " BASTET is reporting that range as free space."
                                             : string.Empty))
                                     + remedy,
                            IsVNetLevel = false
                        });
                    }
                }
            }
        }

        private bool AnyRowContains(
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets, string network, int cidr) =>
            existingSubnets.Any(e =>
                (e.Cidr == cidr && string.Equals(e.NetworkAddress, network, StringComparison.OrdinalIgnoreCase))
                || ipUtilityService.IsSubnetContainedInParent(network, cidr, e.NetworkAddress, e.Cidr));

        private string WhatBastetRecordsOf(
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets, string network, int cidr) =>
            !AnyRowContains(existingSubnets, network, cidr)
                ? "which no BASTET subnet records."
                : BastetOffersAnyOf(existingSubnets, network, cidr)
                    ? "which no BASTET subnet records. BASTET is reporting that range as free space."
                    : "which no BASTET subnet records as its own range.";

        private bool BastetOffersAnyOf(
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets, string network, int cidr)
        {
            if (!AnyRowContains(existingSubnets, network, cidr))
            {
                return false;
            }

            List<Subnet> rowsInsideTheRange = [.. existingSubnets
                .Where(e => ipUtilityService.IsSubnetContainedInParent(e.NetworkAddress, e.Cidr, network, cidr))
                .Select(e => new Subnet { NetworkAddress = e.NetworkAddress, Cidr = e.Cidr })];

            return ipUtilityService.CalculateUnallocatedRanges(network, cidr, rowsInsideTheRange)
                .Any(r => r.AddressCount > 0);
        }

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

                return !AzureResourceIdentity.IsAzureVNet(exact.AzureResourceId) || exact.IsFullyAllocated;
            }

            ExistingSubnetSnapshot? deepest = existingSubnets
                .Where(e => ipUtilityService.IsSubnetContainedInParent(network, cidr, e.NetworkAddress, e.Cidr))
                .OrderByDescending(e => e.Cidr)
                .FirstOrDefault();

            if (deepest is null)
            {

                return false;
            }

            if (deepest.IsFullyAllocated)
            {

                return true;
            }

            List<Subnet> rowsInsideTheRange = [.. existingSubnets
                .Where(e => ipUtilityService.IsSubnetContainedInParent(e.NetworkAddress, e.Cidr, network, cidr))
                .Select(e => new Subnet { NetworkAddress = e.NetworkAddress, Cidr = e.Cidr })];

            return !ipUtilityService.CalculateUnallocatedRanges(network, cidr, rowsInsideTheRange)
                .Any(r => r.AddressCount > 0);
        }

        private bool OverlapsRecorded(string azurePrefix, AzureLinkedSubnetSnapshot snapshot)
        {
            string[] parts = azurePrefix.Split('/');

            return parts.Length == 2
                   && int.TryParse(parts[1], out int cidr)
                   && (ipUtilityService.IsSubnetContainedInParent(parts[0], cidr, snapshot.NetworkAddress, snapshot.Cidr)
                       || ipUtilityService.IsSubnetContainedInParent(snapshot.NetworkAddress, snapshot.Cidr, parts[0], cidr));
        }

        private sealed record AzurePrefixOwner(string ResourceId, string SubnetName, string VNetName);

        private sealed record AzureLivePrefix(string Prefix, string Network, int Cidr, AzurePrefixOwner Owner);

        private sealed record AzureSubscriptionPrefix(
            string Prefix, string Network, int Cidr, AzurePrefixOwner Owner, string VNetResourceId, bool IsVNetAddressSpace);

        private sealed record LiveRangeOwner(AzurePrefixOwner Owner, string LivePrefix, bool Exact, bool OwnerIsVNetAddressSpace = false);

        private static string PrefixKey(string vnetResourceId, string prefix) => $"{vnetResourceId}|{prefix}";

        private LiveRangeOwner? FindLiveOwnerOfRange(
            AzureLinkedSubnetSnapshot snapshot,
            AzureReconcileItem item,
            Dictionary<string, List<AzurePrefixOwner>> livePrefixOwners,
            Dictionary<string, List<AzureLivePrefix>> livePrefixesByVNet,
            List<AzureSubscriptionPrefix> livePrefixesInSubscription,
            Dictionary<string, BulkAzureVNetViewModel> liveVNets,
            HashSet<string> recordedVNetIds)
        {

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

            if (livePrefixesByVNet.TryGetValue(vnetId, out List<AzureLivePrefix>? candidates))
            {
                AzureLivePrefix? overlapping = candidates.FirstOrDefault(c =>
                    !(string.Equals(c.Owner.ResourceId, snapshot.AzureResourceId, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(c.Prefix, recorded, StringComparison.OrdinalIgnoreCase))
                    && OverlapsRange(c.Network, c.Cidr, snapshot));

                if (overlapping is not null)
                {
                    return new LiveRangeOwner(overlapping.Owner, overlapping.Prefix, false);
                }
            }

            if (liveVNets.ContainsKey(vnetId))
            {
                return null;
            }

            List<AzureSubscriptionPrefix> elsewhereMatches = [.. livePrefixesInSubscription.Where(c =>
                !string.Equals(c.VNetResourceId, vnetId, StringComparison.OrdinalIgnoreCase)
                && !recordedVNetIds.Contains(c.VNetResourceId)
                && OverlapsRange(c.Network, c.Cidr, snapshot))];

            AzureSubscriptionPrefix? elsewhere =
                elsewhereMatches.FirstOrDefault(c => !c.IsVNetAddressSpace) ?? elsewhereMatches.FirstOrDefault();

            return elsewhere is null
                ? null
                : new LiveRangeOwner(elsewhere.Owner, elsewhere.Prefix, false, elsewhere.IsVNetAddressSpace);
        }

        private bool OverlapsRange(string network, int cidr, AzureLinkedSubnetSnapshot snapshot) =>
            (cidr == snapshot.Cidr
             && string.Equals(network, snapshot.NetworkAddress, StringComparison.OrdinalIgnoreCase))
            || ipUtilityService.IsSubnetContainedInParent(network, cidr, snapshot.NetworkAddress, snapshot.Cidr)
            || ipUtilityService.IsSubnetContainedInParent(snapshot.NetworkAddress, snapshot.Cidr, network, cidr);

        private static string OwnerList(List<AzurePrefixOwner> owners)
        {
            const int Max = 10;
            string names = string.Join(", ", owners.Take(Max).Select(o => $"'{o.SubnetName}' in VNet '{o.VNetName}'"));
            return owners.Count > Max ? $"{names} and {owners.Count - Max} more" : names;
        }

        private static string NameList(List<AzureReconcileItem> items)
        {
            const int Max = 10;
            string names = string.Join(", ", items.Take(Max)
                .Select(i => $"'{i.Name}' ({i.NetworkAddress}/{i.Cidr})"));
            return items.Count > Max ? $"{names} and {items.Count - Max} more" : names;
        }

        private AzureReconcileItem? EvaluateVNetLevel(
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

            if (!vnet.Ipv4AddressPrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            {

                string? covering = vnet.Ipv4AddressPrefixes.FirstOrDefault(p => OverlapsRecorded(p, snapshot));

                if (covering is not null)
                {
                    return Item(snapshot, AzureReconcileStatus.VNetPrefixStillCovered, true,
                        $"VNet '{vnet.Name}' no longer has the address prefix {prefix}, but its address space "
                        + $"now includes {covering}, which overlaps that range - so the space was resized or "
                        + "re-carved rather than released. Archiving this subnet would remove BASTET's only "
                        + "record of a range Azure still covers. Either restore the VNet's original address prefix "
                        + "in Azure, or delete this BASTET subnet and import the current prefix again - its recorded "
                        + "range cannot be edited while it is linked to Azure.");
                }

                return Item(snapshot, AzureReconcileStatus.VNetPrefixRemoved, true,
                    $"VNet '{vnet.Name}' still exists but no longer has the address prefix {prefix}.");
            }

            if (snapshot.IsFullyAllocated
                && !vnet.Subnets.Any(s => Ipv4PrefixesOf(s).Contains(prefix, StringComparer.OrdinalIgnoreCase)))
            {
                return Item(snapshot, AzureReconcileStatus.FullyAllocatingSubnetDeleted, true,
                    $"Marked fully allocated, but no Azure subnet in VNet '{vnet.Name}' covers {prefix} any more. " +
                    "Nothing needs deleting; review whether it should still be marked fully allocated.");
            }

            return null;
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
                    $"The Azure subnet still exists but its address prefix is now {live}, not {prefix}.");
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
