using Bastet.Models.ViewModels;
using Bastet.Services.Security;

namespace Bastet.Services.Azure
{

    public class AzureBulkImportPlanner(
        IIpUtilityService ipUtilityService,
        IInputSanitizationService sanitizationService) : IAzureBulkImportPlanner
    {

        private const int MaxSubnetNameLength = 100;

        private static bool IsSameVNet(ExistingSubnetSnapshot existing, string? vnetResourceId) =>
            !string.IsNullOrEmpty(existing.AzureResourceId)
            && !string.IsNullOrEmpty(vnetResourceId)
            && string.Equals(existing.AzureResourceId, vnetResourceId, StringComparison.OrdinalIgnoreCase);

        private static bool IsSameVNet(ExistingSubnetSnapshot existing, BulkAzureVNetViewModel vnet) =>
            IsSameVNet(existing, vnet.ResourceId);

        private static bool HasPersistedSiblingFromSameAzureSubnet(
            ParsedSubnetSelection sub,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets) =>
            !string.IsNullOrEmpty(sub.Source.AzureResourceId)
            && existingSubnets.Any(e =>
                string.Equals(e.AzureResourceId, sub.Source.AzureResourceId, StringComparison.OrdinalIgnoreCase)
                && !(e.Cidr == sub.Cidr
                     && string.Equals(e.NetworkAddress, sub.Network, StringComparison.OrdinalIgnoreCase)));

        public BulkImportPlanViewModel BuildPlan(
            BulkImportSelectionDto selection,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets)
        {
            ArgumentNullException.ThrowIfNull(selection);
            ArgumentNullException.ThrowIfNull(existingSubnets);

            BulkImportPlanViewModel plan = new()
            {
                SubscriptionId = selection.SubscriptionId,
                SubscriptionName = selection.SubscriptionName,
                RenameMatchedBastetSubnets = selection.RenameMatchedBastetSubnets
            };

            if (selection.VNetPrefixes is null or { Count: 0 })
            {
                plan.GlobalErrors.Add("No VNet address prefixes were selected.");
                return plan;
            }

            List<ParsedPrefixSelection> parsed = [];
            foreach (BulkImportSelectedVNetPrefixDto sel in selection.VNetPrefixes)
            {

                if (sel is null)
                {
                    plan.GlobalErrors.Add("A selected VNet prefix was empty.");
                    continue;
                }

                if (!TryParseCidr(sel.AddressPrefix, out string prefixNetwork, out int prefixCidr))
                {
                    plan.GlobalErrors.Add($"VNet '{sel.VNetName}' has an invalid address prefix '{sel.AddressPrefix}'.");
                    continue;
                }

                if (!ipUtilityService.IsValidSubnet(prefixNetwork, prefixCidr))
                {
                    plan.GlobalErrors.Add(
                        $"VNet '{sel.VNetName}' prefix '{sel.AddressPrefix}' is not aligned to its CIDR boundary and cannot be imported.");
                    continue;
                }

                ParsedPrefixSelection p = new()
                {
                    Source = sel,
                    PrefixNetwork = prefixNetwork,
                    PrefixCidr = prefixCidr
                };

                foreach (BulkImportSelectedSubnetDto sub in sel.Subnets ?? [])
                {
                    if (sub is null)
                    {
                        plan.GlobalErrors.Add($"A selected subnet under VNet '{sel.VNetName}' was empty.");
                        continue;
                    }

                    if (!TryParseCidr(sub.AddressPrefix, out string subNet, out int subCidr))
                    {
                        plan.GlobalErrors.Add(
                            $"Azure subnet '{sub.Name}' under VNet '{sel.VNetName}' has an invalid address prefix '{sub.AddressPrefix}'.");
                        continue;
                    }

                    if (!ipUtilityService.IsValidSubnet(subNet, subCidr))
                    {
                        plan.GlobalErrors.Add(
                            $"Azure subnet '{sub.Name}' ({sub.AddressPrefix}) is not aligned to its CIDR boundary.");
                        continue;
                    }

                    bool isContained = ipUtilityService.IsSubnetContainedInParent(subNet, subCidr, prefixNetwork, prefixCidr);
                    bool isEqual = subCidr == prefixCidr
                        && string.Equals(subNet, prefixNetwork, StringComparison.OrdinalIgnoreCase);
                    if (!isContained && !isEqual)
                    {
                        plan.GlobalErrors.Add(
                            $"Azure subnet '{sub.Name}' ({sub.AddressPrefix}) is not contained in VNet prefix {sel.AddressPrefix}.");
                        continue;
                    }

                    p.Subnets.Add(new ParsedSubnetSelection
                    {
                        Source = sub,
                        Network = subNet,
                        Cidr = subCidr,
                        FullyEncompasses = isEqual
                    });
                }

                parsed.Add(p);
            }

            if (plan.GlobalErrors.Count > 0)
            {

                return plan;
            }

            DetectVNetPrefixOverlaps(parsed, plan);
            DetectAzureSubnetOverlaps(parsed, plan);

            HashSet<string> multiPrefixResourceIds = new(
                parsed.SelectMany(p => p.Subnets)
                    .Where(s => !s.FullyEncompasses && !string.IsNullOrEmpty(s.Source.AzureResourceId))
                    .GroupBy(s => s.Source.AzureResourceId!, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);

            foreach (ParsedPrefixSelection p in parsed)
            {
                BulkImportPlanItem item = BuildPlanItem(
                    p, existingSubnets, selection.RenameMatchedBastetSubnets,
                    multiPrefixResourceIds);
                plan.Items.Add(item);
            }

            DetectExistingBastetSubnetConflicts(parsed, existingSubnets, plan);
            DetectVNetPrefixWouldContainExistingSubnet(parsed, existingSubnets, plan);

            return plan;
        }

        public void AnnotateAvailability(
            IReadOnlyList<BulkAzureVNetViewModel> vnets,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets)
        {
            ArgumentNullException.ThrowIfNull(vnets);
            ArgumentNullException.ThrowIfNull(existingSubnets);

            foreach (BulkAzureVNetViewModel vnet in vnets)
            {
                foreach (BulkAzureSubnetViewModel subnet in vnet.Subnets)
                {
                    AnnotateSubnet(subnet, vnet, existingSubnets);
                }

                vnet.Prefixes = [.. vnet.Ipv4AddressPrefixes.Select(p => AnnotatePrefix(p, vnet, existingSubnets))];
            }
        }

        private BulkAzurePrefixViewModel AnnotatePrefix(
            string addressPrefix,
            BulkAzureVNetViewModel vnet,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets)
        {
            BulkAzurePrefixViewModel result = new() { AddressPrefix = addressPrefix };

            if (!TryParseCidr(addressPrefix, out string network, out int cidr)
                || !ipUtilityService.IsValidSubnet(network, cidr))
            {
                return Blocked(result, "This prefix is not a valid, CIDR-aligned IPv4 network.");
            }

            ExistingSubnetSnapshot? exact = existingSubnets.FirstOrDefault(e =>
                e.Cidr == cidr && string.Equals(e.NetworkAddress, network, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
            {

                if (!string.IsNullOrEmpty(exact.AzureResourceId)
                    && !string.IsNullOrEmpty(vnet.ResourceId)
                    && !string.Equals(exact.AzureResourceId, vnet.ResourceId, StringComparison.OrdinalIgnoreCase))
                {
                    return Blocked(result,
                        $"Bastet subnet '{exact.Name}' is already linked to Azure VNet '{exact.AzureResourceId}'. "
                        + $"Importing '{vnet.ResourceId}' would replace that link, so it is refused.");
                }

                bool isTopUp = IsSameVNet(exact, vnet);

                result.WouldRenameTarget = !string.Equals(
                    exact.Name, ProposedTargetName(vnet, network, cidr), StringComparison.Ordinal);

                if (exact.HasHostIpAssignments)
                {
                    return isTopUp
                        ? AlreadyImported(result,
                            $"Already imported as Bastet subnet '{exact.Name}', which has host IP assignments, so "
                            + "no subnets can be added inside it.")
                        : Blocked(result, $"Bastet subnet '{exact.Name}' already has host IP assignments.");
                }
                if (exact.IsFullyAllocated)
                {
                    if (isTopUp)
                    {
                        return AlreadyImported(result,
                            $"Already imported as Bastet subnet '{exact.Name}', which is marked fully allocated, so "
                            + "there is nothing left to add. Clear the flag on its Details page if subnets should go inside it.");
                    }

                    result.Status = BulkImportAvailability.WillUpdateExisting;
                    result.Reason = $"Will link existing Bastet subnet '{exact.Name}' to this VNet. "
                        + "It stays marked fully allocated, so no subnets will be created inside it.";
                    result.IsSelectable = true;
                    return result;
                }

                if (isTopUp && !AnySubnetCanBeAdded(vnet, network, cidr))
                {
                    return AlreadyImported(result,
                        AnySubnetCannotBeImported(vnet, network, cidr)
                            ? $"Already imported as Bastet subnet '{exact.Name}'. Every Azure subnet in this prefix is "
                              + "either already recorded or cannot be imported, so there is nothing to add."
                            : $"Already imported as Bastet subnet '{exact.Name}'. Every Azure subnet in this prefix is "
                              + "already recorded, so there is nothing to add.");
                }

                result.Status = BulkImportAvailability.WillUpdateExisting;

                result.Reason = exact.HasChildSubnets
                    ? $"Will add any missing subnets to existing Bastet subnet '{exact.Name}'. Subnets already imported are left untouched."
                    : $"Will import into existing Bastet subnet '{exact.Name}'.";
                result.IsSelectable = true;
                return result;
            }

            ExistingSubnetSnapshot? deepest = FindDeepestContainer(network, cidr, existingSubnets);
            if (deepest is not null)
            {
                if (deepest.HasHostIpAssignments)
                {
                    return Blocked(result, $"Containing Bastet subnet '{deepest.Name}' ({deepest.NetworkAddress}/{deepest.Cidr}) has host IP assignments and cannot have child subnets.");
                }
                if (deepest.IsFullyAllocated)
                {
                    return Blocked(result, $"Containing Bastet subnet '{deepest.Name}' ({deepest.NetworkAddress}/{deepest.Cidr}) is marked as fully allocated.");
                }
            }

            ExistingSubnetSnapshot? contained = existingSubnets.FirstOrDefault(e =>
                ipUtilityService.IsSubnetContainedInParent(e.NetworkAddress, e.Cidr, network, cidr));

            return contained is not null
                ? Blocked(result, $"Would contain existing Bastet subnet '{contained.Name}' ({contained.NetworkAddress}/{contained.Cidr}), which would create an invalid hierarchy.")
                : Available(result, "Will create a new Bastet subnet.");
        }

        private ExistingSubnetSnapshot? FindMoreSpecificParent(
            BulkAzureVNetViewModel vnet,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets,
            string network,
            int cidr)
        {
            foreach (string prefix in vnet.Ipv4AddressPrefixes)
            {
                if (!TryParseCidr(prefix, out string prefixNetwork, out int prefixCidr))
                {
                    continue;
                }

                if (!ipUtilityService.IsSubnetContainedInParent(network, cidr, prefixNetwork, prefixCidr))
                {
                    continue;
                }

                return existingSubnets.FirstOrDefault(e =>
                    ipUtilityService.IsSubnetContainedInParent(e.NetworkAddress, e.Cidr, prefixNetwork, prefixCidr)
                    && ipUtilityService.IsSubnetContainedInParent(network, cidr, e.NetworkAddress, e.Cidr));
            }

            return null;
        }

        private void AnnotateSubnet(
            BulkAzureSubnetViewModel subnet,
            BulkAzureVNetViewModel vnet,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets)
        {

            bool encompassesAPrefix = vnet.Ipv4AddressPrefixes
                .Any(p => string.Equals(p, subnet.AddressPrefix, StringComparison.OrdinalIgnoreCase));

            if (encompassesAPrefix)
            {

                ExistingSubnetSnapshot? encompassedTarget =
                    TryParseCidr(subnet.AddressPrefix, out string encNetwork, out int encCidr)
                        ? existingSubnets.FirstOrDefault(e =>
                            e.Cidr == encCidr
                            && string.Equals(e.NetworkAddress, encNetwork, StringComparison.OrdinalIgnoreCase))
                        : null;

                if (encompassedTarget is not null && encompassedTarget.HasChildSubnets)
                {
                    subnet.Status = BulkImportAvailability.Blocked;
                    subnet.Reason = $"Covers the whole VNet prefix, which would mark Bastet subnet "
                                    + $"'{encompassedTarget.Name}' fully allocated, but it already has child subnets.";
                    subnet.IsSelectable = false;
                    return;
                }

                if (encompassedTarget is not null && encompassedTarget.HasHostIpAssignments)
                {
                    subnet.Status = BulkImportAvailability.Blocked;
                    subnet.Reason = $"Covers the whole VNet prefix, which would mark Bastet subnet "
                                    + $"'{encompassedTarget.Name}' fully allocated, but it has host IP assignments.";
                    subnet.IsSelectable = false;
                    return;
                }

                if (encompassedTarget is not null && encompassedTarget.IsFullyAllocated)
                {
                    subnet.Status = BulkImportAvailability.AlreadyImported;
                    subnet.Reason = $"Bastet subnet '{encompassedTarget.Name}' is marked fully allocated, "
                                    + "so there is nothing to do.";
                    subnet.IsSelectable = false;
                    return;
                }

                subnet.Status = BulkImportAvailability.Available;
                subnet.Reason = "Covers the whole VNet prefix, so it marks the target fully allocated instead of being created.";
                subnet.IsSelectable = true;
                return;
            }

            if (!TryParseCidr(subnet.AddressPrefix, out string network, out int cidr))
            {
                subnet.Status = BulkImportAvailability.Blocked;
                subnet.Reason = "This subnet does not have a valid IPv4 address prefix.";
                subnet.IsSelectable = false;
                return;
            }

            ExistingSubnetSnapshot? exact = existingSubnets.FirstOrDefault(e =>
                e.Cidr == cidr && string.Equals(e.NetworkAddress, network, StringComparison.OrdinalIgnoreCase));

            if (exact is null)
            {
                ExistingSubnetSnapshot? wouldContain = existingSubnets.FirstOrDefault(e =>
                    ipUtilityService.IsSubnetContainedInParent(e.NetworkAddress, e.Cidr, network, cidr));

                if (wouldContain is not null)
                {
                    subnet.Status = BulkImportAvailability.Blocked;
                    subnet.Reason = $"Would contain existing Bastet subnet '{wouldContain.Name}' "
                                    + $"({wouldContain.NetworkAddress}/{wouldContain.Cidr}), which would create an invalid hierarchy.";
                    subnet.IsSelectable = false;
                    return;
                }

                ExistingSubnetSnapshot? moreSpecificParent = FindMoreSpecificParent(vnet, existingSubnets, network, cidr);

                if (moreSpecificParent is not null)
                {
                    subnet.Status = BulkImportAvailability.Blocked;
                    subnet.Reason = $"Bastet subnet '{moreSpecificParent.Name}' "
                                    + $"({moreSpecificParent.NetworkAddress}/{moreSpecificParent.Cidr}) sits between this "
                                    + $"VNet and {subnet.AddressPrefix}, and Azure has no such subnet. Imported subnets are "
                                    + $"placed directly under their VNet, so delete or re-carve '{moreSpecificParent.Name}' "
                                    + "before importing this one.";
                    subnet.IsSelectable = false;
                    return;
                }

                ExistingSubnetSnapshot? container = FindDeepestContainer(network, cidr, existingSubnets);

                if (container is not null && container.HasHostIpAssignments)
                {
                    subnet.Status = BulkImportAvailability.Blocked;
                    subnet.Reason = $"Containing Bastet subnet '{container.Name}' "
                                    + $"({container.NetworkAddress}/{container.Cidr}) has host IP assignments and cannot have child subnets.";
                    subnet.IsSelectable = false;
                    return;
                }

                if (container is not null && container.IsFullyAllocated)
                {
                    subnet.Status = BulkImportAvailability.Blocked;
                    subnet.Reason = $"Containing Bastet subnet '{container.Name}' "
                                    + $"({container.NetworkAddress}/{container.Cidr}) is marked as fully allocated.";
                    subnet.IsSelectable = false;
                    return;
                }

                subnet.Status = BulkImportAvailability.Available;
                subnet.Reason = null;
                subnet.IsSelectable = true;
                return;
            }

            bool sameAzureResource = !string.IsNullOrEmpty(exact.AzureResourceId)
                && string.Equals(exact.AzureResourceId, subnet.ResourceId, StringComparison.OrdinalIgnoreCase);

            subnet.Status = sameAzureResource ? BulkImportAvailability.AlreadyImported : BulkImportAvailability.Blocked;
            subnet.Reason = sameAzureResource
                ? $"Already imported as Bastet subnet '{exact.Name}'."
                : $"Bastet subnet '{exact.Name}' already uses {subnet.AddressPrefix}.";
            subnet.WouldRenameSubnet = sameAzureResource
                && !string.Equals(exact.Name, ProposedChildName(subnet), StringComparison.Ordinal);
            subnet.IsSelectable = false;
        }

        private static ExistingSubnetSnapshot? LinkedRowForSameAzureSubnet(
            ParsedSubnetSelection sub, IReadOnlyList<ExistingSubnetSnapshot> existingSubnets) =>
            string.IsNullOrEmpty(sub.Source.AzureResourceId)
                ? null
                : existingSubnets.FirstOrDefault(e =>
                    e.Cidr == sub.Cidr
                    && string.Equals(e.NetworkAddress, sub.Network, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(e.AzureResourceId)
                    && string.Equals(e.AzureResourceId, sub.Source.AzureResourceId, StringComparison.OrdinalIgnoreCase));

        private string ProposedChildName(BulkAzureSubnetViewModel subnet) =>
            TruncateAndSanitizeName(subnet.Name) is { Length: > 0 } sanitized
                ? sanitized
                : subnet.AddressPrefix.Replace('/', '_');

        private IEnumerable<BulkAzureSubnetViewModel> SubnetsWithinPrefix(
            BulkAzureVNetViewModel vnet, string prefixNetwork, int prefixCidr) =>
            vnet.Subnets.Where(s =>
                TryParseCidr(s.AddressPrefix, out string n, out int c)
                && ((c == prefixCidr && string.Equals(n, prefixNetwork, StringComparison.OrdinalIgnoreCase))
                    || ipUtilityService.IsSubnetContainedInParent(n, c, prefixNetwork, prefixCidr)));

        private bool AnySubnetCanBeAdded(BulkAzureVNetViewModel vnet, string prefixNetwork, int prefixCidr) =>
            SubnetsWithinPrefix(vnet, prefixNetwork, prefixCidr).Any(s => s.IsSelectable);

        private bool AnySubnetCannotBeImported(BulkAzureVNetViewModel vnet, string prefixNetwork, int prefixCidr) =>
            SubnetsWithinPrefix(vnet, prefixNetwork, prefixCidr)
                .Any(s => s.Status == BulkImportAvailability.Blocked);

        private string ProposedTargetName(BulkAzureVNetViewModel vnet, string prefixNetwork, int prefixCidr)
        {
            string name = TruncateAndSanitizeName(vnet.Name) is { Length: > 0 } sanitized
                ? sanitized
                : $"{prefixNetwork}_{prefixCidr}";

            return vnet.Ipv4AddressPrefixes.Count > 1
                ? SubnetNaming.WithSuffix(name, $" ({prefixNetwork}-{prefixCidr})", MaxSubnetNameLength)
                : name;
        }

        private static BulkAzurePrefixViewModel AlreadyImported(BulkAzurePrefixViewModel result, string reason)
        {
            result.Status = BulkImportAvailability.AlreadyImported;
            result.Reason = reason;
            result.IsSelectable = false;
            return result;
        }

        private static BulkAzurePrefixViewModel Blocked(BulkAzurePrefixViewModel result, string reason)
        {
            result.Status = BulkImportAvailability.Blocked;
            result.Reason = reason;
            result.IsSelectable = false;
            return result;
        }

        private static BulkAzurePrefixViewModel Available(BulkAzurePrefixViewModel result, string reason)
        {
            result.Status = BulkImportAvailability.Available;
            result.Reason = reason;
            result.IsSelectable = true;
            return result;
        }

        private ExistingSubnetSnapshot? FindDeepestContainer(
            string network,
            int cidr,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets)
        {
            ExistingSubnetSnapshot? deepest = null;
            foreach (ExistingSubnetSnapshot candidate in existingSubnets)
            {
                if (ipUtilityService.IsSubnetContainedInParent(
                    network, cidr,
                    candidate.NetworkAddress, candidate.Cidr))
                {
                    if (deepest is null || candidate.Cidr > deepest.Cidr)
                    {
                        deepest = candidate;
                    }
                }
            }

            return deepest;
        }

        private BulkImportPlanItem BuildPlanItem(
            ParsedPrefixSelection p,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets,
            bool renameMatched,
            HashSet<string> multiPrefixResourceIds)
        {
            BulkImportPlanItem item = new()
            {
                VNetName = p.Source.VNetName,
                VNetResourceId = p.Source.VNetResourceId,
                VNetPrefix = p.Source.AddressPrefix,
                PrefixNetworkAddress = p.PrefixNetwork,
                PrefixCidr = p.PrefixCidr
            };

            ExistingSubnetSnapshot? exact = existingSubnets.FirstOrDefault(s =>
                s.Cidr == p.PrefixCidr && string.Equals(s.NetworkAddress, p.PrefixNetwork, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
            {
                item.TargetType = BulkImportTargetType.ExactMatch;
                item.ExistingTargetSubnetId = exact.Id;
                item.ExistingTargetSubnetName = exact.Name;

                if (exact.HasHostIpAssignments
                    && p.Subnets.Any(s => LinkedRowForSameAzureSubnet(s, existingSubnets) is null))
                {
                    item.Errors.Add(
                        $"Cannot import VNet prefix {p.Source.AddressPrefix}: matched Bastet subnet '{exact.Name}' ({exact.NetworkAddress}/{exact.Cidr}) already has host IP assignments.");
                }
                if (exact.IsFullyAllocated
                    && p.Subnets.Any(s => !s.FullyEncompasses && LinkedRowForSameAzureSubnet(s, existingSubnets) is null))
                {
                    item.Errors.Add(
                        $"Cannot import VNet prefix {p.Source.AddressPrefix}: matched Bastet subnet '{exact.Name}' ({exact.NetworkAddress}/{exact.Cidr}) is marked as fully allocated.");
                }

                if (!string.IsNullOrEmpty(exact.AzureResourceId)
                    && !string.IsNullOrEmpty(p.Source.VNetResourceId)
                    && !string.Equals(exact.AzureResourceId, p.Source.VNetResourceId, StringComparison.OrdinalIgnoreCase))
                {
                    item.Errors.Add(
                        $"Cannot import VNet prefix {p.Source.AddressPrefix}: matched Bastet subnet '{exact.Name}' "
                        + $"({exact.NetworkAddress}/{exact.Cidr}) is already linked to Azure VNet '{exact.AzureResourceId}', "
                        + $"and importing '{p.Source.VNetResourceId}' would replace that link.");
                }

                if (renameMatched)
                {
                    string proposed = TargetName(p);
                    if (!string.Equals(proposed, exact.Name, StringComparison.Ordinal))
                    {
                        item.WillRename = true;
                        item.NewName = proposed;
                    }
                }
            }
            else
            {

                ExistingSubnetSnapshot? deepest = FindDeepestContainer(p.PrefixNetwork, p.PrefixCidr, existingSubnets);

                if (deepest is not null)
                {
                    item.TargetType = BulkImportTargetType.AutoCreateChild;
                    item.AutoCreateParentSubnetId = deepest.Id;
                    item.AutoCreateParentSubnetName = deepest.Name;
                    item.AutoCreateTargetName = TargetName(p);

                    if (deepest.HasHostIpAssignments)
                    {
                        item.Errors.Add(
                            $"Cannot import VNet prefix {p.Source.AddressPrefix}: containing Bastet subnet '{deepest.Name}' ({deepest.NetworkAddress}/{deepest.Cidr}) has host IP assignments and cannot have child subnets.");
                    }
                    if (deepest.IsFullyAllocated)
                    {
                        item.Errors.Add(
                            $"Cannot import VNet prefix {p.Source.AddressPrefix}: containing Bastet subnet '{deepest.Name}' ({deepest.NetworkAddress}/{deepest.Cidr}) is marked as fully allocated.");
                    }
                }
                else
                {
                    item.TargetType = BulkImportTargetType.AutoCreateTopLevel;
                    item.AutoCreateTargetName = TargetName(p);
                }
            }

            ParsedSubnetSelection? fullyEncompassing = p.Subnets.FirstOrDefault(s => s.FullyEncompasses);
            if (fullyEncompassing is not null)
            {

                if (p.Subnets.Count > 1)
                {
                    item.Errors.Add(
                        $"Azure subnet '{TruncateAndSanitizeName(fullyEncompassing.Source.Name)}' covers the whole of "
                        + $"{p.PrefixNetwork}/{p.PrefixCidr}, so nothing can be created inside it, but "
                        + $"{p.Subnets.Count - 1} other subnet(s) were selected from the same prefix. "
                        + "Azure does not allow overlapping subnets in a VNet, so this selection cannot be applied.");
                    return item;
                }

                if (exact is not null && exact.HasChildSubnets)
                {
                    item.Errors.Add(
                        $"Cannot import VNet prefix {p.Source.AddressPrefix}: Azure subnet "
                        + $"'{TruncateAndSanitizeName(fullyEncompassing.Source.Name)}' covers the whole prefix, which would "
                        + $"mark Bastet subnet '{exact.Name}' fully allocated, but it already has child subnets.");
                    return item;
                }

                item.WillMarkFullyAllocated = true;

                item.FullyAllocatingAzureSubnetName = TruncateAndSanitizeName(fullyEncompassing.Source.Name);
            }

            HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
            string? targetExistingName = exact?.Name;
            string? targetAutoCreatedName = item.AutoCreateTargetName;

            if (!string.IsNullOrEmpty(targetExistingName))
            {
                usedNames.Add(targetExistingName);
            }
            if (item.WillRename && !string.IsNullOrEmpty(item.NewName))
            {
                usedNames.Add(item.NewName);
            }
            if (!string.IsNullOrEmpty(targetAutoCreatedName))
            {
                usedNames.Add(targetAutoCreatedName);
            }

            foreach (ParsedSubnetSelection sub in p.Subnets)
            {
                if (sub.FullyEncompasses)
                {
                    continue;
                }

                ExistingSubnetSnapshot? linkedRow = LinkedRowForSameAzureSubnet(sub, existingSubnets);

                string baseName = TruncateAndSanitizeName(sub.Source.Name);
                if (string.IsNullOrEmpty(baseName))
                {
                    baseName = $"{sub.Network}_{sub.Cidr}";
                }
                else if (multiPrefixResourceIds.Contains(sub.Source.AzureResourceId)
                         || HasPersistedSiblingFromSameAzureSubnet(sub, existingSubnets))
                {

                    baseName = SubnetNaming.WithSuffix(
                        baseName, $" ({sub.Network}-{sub.Cidr})", MaxSubnetNameLength);
                }

                if (linkedRow is not null)
                {
                    if (!renameMatched || string.Equals(linkedRow.Name, baseName, StringComparison.Ordinal))
                    {
                        usedNames.Add(linkedRow.Name);
                        continue;
                    }

                    string renamedTo = DisambiguateName(baseName, usedNames, p.Source.VNetName);
                    usedNames.Add(renamedTo);

                    item.ChildSubnets.Add(new BulkImportPlannedChildSubnet
                    {
                        OriginalAzureName = sub.Source.Name,
                        Name = renamedTo,
                        NetworkAddress = sub.Network,
                        Cidr = sub.Cidr,
                        AzureResourceId = sub.Source.AzureResourceId,
                        WillRename = true,
                        ExistingSubnetId = linkedRow.Id
                    });
                    continue;
                }

                string finalName = DisambiguateName(baseName, usedNames, p.Source.VNetName);
                usedNames.Add(finalName);

                item.ChildSubnets.Add(new BulkImportPlannedChildSubnet
                {
                    OriginalAzureName = sub.Source.Name,
                    Name = finalName,
                    NetworkAddress = sub.Network,
                    Cidr = sub.Cidr,
                    AzureResourceId = sub.Source.AzureResourceId
                });
            }

            return item;
        }

        private void DetectVNetPrefixOverlaps(IReadOnlyList<ParsedPrefixSelection> parsed, BulkImportPlanViewModel plan)
        {
            for (int i = 0; i < parsed.Count; i++)
            {
                for (int j = i + 1; j < parsed.Count; j++)
                {
                    ParsedPrefixSelection a = parsed[i];
                    ParsedPrefixSelection b = parsed[j];

                    if (PrefixesOverlap(a.PrefixNetwork, a.PrefixCidr, b.PrefixNetwork, b.PrefixCidr))
                    {
                        plan.GlobalErrors.Add(
                            $"Selected VNet prefix {b.Source.AddressPrefix} (VNet '{b.Source.VNetName}') overlaps with {a.Source.AddressPrefix} (VNet '{a.Source.VNetName}').");
                    }
                }
            }
        }

        private void DetectAzureSubnetOverlaps(IReadOnlyList<ParsedPrefixSelection> parsed, BulkImportPlanViewModel plan)
        {

            List<(ParsedPrefixSelection prefix, ParsedSubnetSelection subnet)> all = [];
            foreach (ParsedPrefixSelection p in parsed)
            {
                foreach (ParsedSubnetSelection s in p.Subnets)
                {
                    if (s.FullyEncompasses)
                    {
                        continue;
                    }
                    all.Add((p, s));
                }
            }

            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    (ParsedPrefixSelection pa, ParsedSubnetSelection a) = all[i];
                    (ParsedPrefixSelection pb, ParsedSubnetSelection b) = all[j];

                    if (PrefixesOverlap(a.Network, a.Cidr, b.Network, b.Cidr))
                    {
                        plan.GlobalErrors.Add(
                            $"Selected Azure subnet '{b.Source.Name}' ({b.Source.AddressPrefix}, VNet '{pb.Source.VNetName}') overlaps with '{a.Source.Name}' ({a.Source.AddressPrefix}, VNet '{pa.Source.VNetName}').");
                    }
                }
            }
        }

        private void DetectExistingBastetSubnetConflicts(
            IReadOnlyList<ParsedPrefixSelection> parsed,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets,
            BulkImportPlanViewModel plan)
        {
            foreach (ParsedPrefixSelection p in parsed)
            {
                foreach (ParsedSubnetSelection s in p.Subnets)
                {
                    if (s.FullyEncompasses || LinkedRowForSameAzureSubnet(s, existingSubnets) is not null)
                    {
                        continue;
                    }

                    bool exists = existingSubnets.Any(e =>
                        e.Cidr == s.Cidr &&
                        string.Equals(e.NetworkAddress, s.Network, StringComparison.OrdinalIgnoreCase));

                    if (exists)
                    {
                        plan.GlobalErrors.Add(
                            $"Azure subnet '{s.Source.Name}' ({s.Source.AddressPrefix}, VNet '{p.Source.VNetName}') already exists in Bastet.");
                        continue;
                    }

                    foreach (ExistingSubnetSnapshot e in existingSubnets)
                    {
                        bool insideThisVNetPrefix = ipUtilityService.IsSubnetContainedInParent(
                            e.NetworkAddress, e.Cidr, p.PrefixNetwork, p.PrefixCidr);

                        if (!insideThisVNetPrefix)
                        {
                            continue;
                        }

                        if (ipUtilityService.IsSubnetContainedInParent(e.NetworkAddress, e.Cidr, s.Network, s.Cidr))
                        {
                            plan.GlobalErrors.Add(
                                $"Azure subnet '{s.Source.Name}' ({s.Source.AddressPrefix}, VNet '{p.Source.VNetName}') "
                                + $"would contain existing Bastet subnet '{e.Name}' ({e.NetworkAddress}/{e.Cidr}). "
                                + "Importing it would create an invalid hierarchy.");
                        }
                        else if (ipUtilityService.IsSubnetContainedInParent(s.Network, s.Cidr, e.NetworkAddress, e.Cidr))
                        {
                            plan.GlobalErrors.Add(
                                $"Azure subnet '{s.Source.Name}' ({s.Source.AddressPrefix}, VNet '{p.Source.VNetName}'): "
                                + $"Bastet subnet '{e.Name}' ({e.NetworkAddress}/{e.Cidr}) sits between that VNet and this "
                                + "subnet, and Azure has no such subnet. Imported subnets are placed directly under their "
                                + $"VNet, so delete or re-carve '{e.Name}' before importing this one.");
                        }
                    }
                }
            }
        }

        private void DetectVNetPrefixWouldContainExistingSubnet(
            IReadOnlyList<ParsedPrefixSelection> parsed,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets,
            BulkImportPlanViewModel plan)
        {

            foreach (ParsedPrefixSelection p in parsed)
            {
                bool exactExists = existingSubnets.Any(e =>
                    e.Cidr == p.PrefixCidr &&
                    string.Equals(e.NetworkAddress, p.PrefixNetwork, StringComparison.OrdinalIgnoreCase));
                if (exactExists)
                {
                    continue;
                }

                foreach (ExistingSubnetSnapshot e in existingSubnets)
                {

                    if (ipUtilityService.IsSubnetContainedInParent(
                        e.NetworkAddress, e.Cidr,
                        p.PrefixNetwork, p.PrefixCidr))
                    {
                        plan.GlobalErrors.Add(
                            $"VNet prefix {p.Source.AddressPrefix} (VNet '{p.Source.VNetName}') would contain existing Bastet subnet '{e.Name}' ({e.NetworkAddress}/{e.Cidr}). " +
                            "Importing it would create an invalid hierarchy.");
                    }
                }
            }
        }

        private bool PrefixesOverlap(string aNetwork, int aCidr, string bNetwork, int bCidr)
        {
            if (aCidr == bCidr && string.Equals(aNetwork, bNetwork, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ipUtilityService.IsSubnetContainedInParent(bNetwork, bCidr, aNetwork, aCidr)
                || ipUtilityService.IsSubnetContainedInParent(aNetwork, aCidr, bNetwork, bCidr);
        }

        private static bool TryParseCidr(string addressPrefix, out string network, out int cidr)
        {
            network = string.Empty;
            cidr = 0;
            if (string.IsNullOrWhiteSpace(addressPrefix))
            {
                return false;
            }

            string[] parts = addressPrefix.Split('/');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!int.TryParse(parts[1], out int parsedCidr) || parsedCidr is < 0 or > 32)
            {
                return false;
            }

            network = parts[0];
            cidr = parsedCidr;
            return true;
        }

        private string TargetName(ParsedPrefixSelection prefix)
        {
            string name = TruncateAndSanitizeName(prefix.Source.VNetName) is { Length: > 0 } sanitized
                ? sanitized
                : $"{prefix.PrefixNetwork}_{prefix.PrefixCidr}";

            return prefix.Source.VNetIpv4AddressPrefixes.Count > 1
                ? SubnetNaming.WithSuffix(
                    name, $" ({prefix.PrefixNetwork}-{prefix.PrefixCidr})", MaxSubnetNameLength)
                : name;
        }

        private string TruncateAndSanitizeName(string? rawName)
        {
            string sanitized = sanitizationService.SanitizeName(rawName);
            if (sanitized.Length > MaxSubnetNameLength)
            {
                sanitized = sanitized[..MaxSubnetNameLength];
            }
            return sanitized;
        }

        private static string DisambiguateName(string baseName, HashSet<string> usedNames, string vnetName)
        {
            if (!usedNames.Contains(baseName))
            {
                return baseName;
            }

            string vnetSuffix = vnetName ?? string.Empty;
            if (vnetSuffix.Length > 20)
            {
                vnetSuffix = vnetSuffix[..20];
            }

            string candidate = WithSuffix(baseName, $" ({vnetSuffix})");
            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }

            for (int i = 2; i <= usedNames.Count + 2; i++)
            {
                string numbered = WithSuffix(baseName, $" ({vnetSuffix} {i})");
                if (!usedNames.Contains(numbered))
                {
                    return numbered;
                }
            }

            return WithSuffix(baseName, $" ({vnetSuffix} {usedNames.Count + 3})");
        }

        private static string WithSuffix(string baseName, string suffix) =>
            SubnetNaming.WithSuffix(baseName, suffix, MaxSubnetNameLength);

        private sealed class ParsedPrefixSelection
        {
            public BulkImportSelectedVNetPrefixDto Source { get; init; } = null!;
            public string PrefixNetwork { get; init; } = string.Empty;
            public int PrefixCidr { get; init; }
            public List<ParsedSubnetSelection> Subnets { get; } = [];
        }

        private sealed class ParsedSubnetSelection
        {
            public BulkImportSelectedSubnetDto Source { get; init; } = null!;
            public string Network { get; init; } = string.Empty;
            public int Cidr { get; init; }
            public bool FullyEncompasses { get; init; }
        }
    }
}
