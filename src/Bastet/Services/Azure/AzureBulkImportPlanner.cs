using Bastet.Models.ViewModels;
using Bastet.Services.Security;

namespace Bastet.Services.Azure
{
    /// <summary>
    /// Default <see cref="IAzureBulkImportPlanner"/> implementation. Pure (no DB),
    /// uses <see cref="IIpUtilityService"/> for IP math and <see cref="IInputSanitizationService"/>
    /// for safe naming. All decisions and conflict checks are made here so the
    /// preview UI shows exactly what commit will do.
    /// </summary>
    public class AzureBulkImportPlanner(
        IIpUtilityService ipUtilityService,
        IInputSanitizationService sanitizationService) : IAzureBulkImportPlanner
    {
        /// <summary>
        /// Maximum length for <see cref="Models.Subnet.Name"/>; matches the [MaxLength(50)] attribute on the entity.
        /// </summary>
        private const int MaxSubnetNameLength = 50;

        /// <inheritdoc/>
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

            if (selection.VNetPrefixes.Count == 0)
            {
                plan.GlobalErrors.Add("No VNet address prefixes were selected.");
                return plan;
            }

            // -------------------------------------------------------------
            // Step 1: parse and validate every selected VNet prefix and Azure subnet up front
            // -------------------------------------------------------------
            List<ParsedPrefixSelection> parsed = [];
            foreach (BulkImportSelectedVNetPrefixDto sel in selection.VNetPrefixes)
            {
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

                foreach (BulkImportSelectedSubnetDto sub in sel.Subnets)
                {
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

                    // Each Azure subnet must be contained in (or equal to) its VNet prefix.
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
                // Don't bother computing the rest — input is malformed
                return plan;
            }

            // -------------------------------------------------------------
            // Step 2: cross-prefix overlap detection (selection-vs-selection)
            // -------------------------------------------------------------
            DetectVNetPrefixOverlaps(parsed, plan);
            DetectAzureSubnetOverlaps(parsed, plan);

            // -------------------------------------------------------------
            // Step 3: determine target Bastet subnet for each VNet prefix and check Bastet conflicts
            // -------------------------------------------------------------
            foreach (ParsedPrefixSelection p in parsed)
            {
                BulkImportPlanItem item = BuildPlanItem(p, existingSubnets, selection.RenameMatchedBastetSubnets);
                plan.Items.Add(item);
            }

            // -------------------------------------------------------------
            // Step 4: cross-checks involving existing Bastet tree
            // -------------------------------------------------------------
            DetectExistingBastetSubnetConflicts(parsed, existingSubnets, plan);
            DetectVNetPrefixWouldContainExistingSubnet(parsed, existingSubnets, plan);

            return plan;
        }

        // -------------------------------------------------------------------
        // Availability annotation (drives the selection UI)
        // -------------------------------------------------------------------

        /// <inheritdoc/>
        public void AnnotateAvailability(
            IReadOnlyList<BulkAzureVNetViewModel> vnets,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets)
        {
            ArgumentNullException.ThrowIfNull(vnets);
            ArgumentNullException.ThrowIfNull(existingSubnets);

            foreach (BulkAzureVNetViewModel vnet in vnets)
            {
                vnet.Prefixes = [.. vnet.Ipv4AddressPrefixes.Select(p => AnnotatePrefix(p, existingSubnets))];

                foreach (BulkAzureSubnetViewModel subnet in vnet.Subnets)
                {
                    AnnotateSubnet(subnet, vnet, existingSubnets);
                }
            }
        }

        /// <summary>
        /// A VNet prefix becomes one Bastet target: either an existing subnet it matches exactly, or
        /// a newly created one. Blocked whenever <see cref="BuildPlanItem"/> would record an error.
        /// </summary>
        private BulkAzurePrefixViewModel AnnotatePrefix(
            string addressPrefix,
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
                // Mirrors the hard failures in BuildPlanItem for an ExactMatch target
                if (exact.HasChildSubnets)
                {
                    return Blocked(result, $"Bastet subnet '{exact.Name}' already has child subnets. Already imported?");
                }
                if (exact.HasHostIpAssignments)
                {
                    return Blocked(result, $"Bastet subnet '{exact.Name}' already has host IP assignments.");
                }
                if (exact.IsFullyAllocated)
                {
                    return Blocked(result, $"Bastet subnet '{exact.Name}' is marked as fully allocated.");
                }

                result.Status = BulkImportAvailability.WillUpdateExisting;
                result.Reason = $"Will import into existing Bastet subnet '{exact.Name}'.";
                result.IsSelectable = true;
                return result;
            }

            // Mirrors the AutoCreateChild hard failures in BuildPlanItem: the auto-created target's
            // parent must be eligible to receive children.
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

            // Mirrors DetectVNetPrefixWouldContainExistingSubnet
            ExistingSubnetSnapshot? contained = existingSubnets.FirstOrDefault(e =>
                ipUtilityService.IsSubnetContainedInParent(e.NetworkAddress, e.Cidr, network, cidr));

            return contained is not null
                ? Blocked(result, $"Would contain existing Bastet subnet '{contained.Name}' ({contained.NetworkAddress}/{contained.Cidr}), which would create an invalid hierarchy.")
                : Available(result, "Will create a new Bastet subnet.");
        }

        /// <summary>
        /// An Azure subnet becomes a child of its prefix's target - unless it covers the whole
        /// prefix, in which case it is never created and only marks the target fully allocated.
        /// </summary>
        private static void AnnotateSubnet(
            BulkAzureSubnetViewModel subnet,
            BulkAzureVNetViewModel vnet,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets)
        {
            // Encompassing subnets are excluded from the duplicate check in
            // DetectExistingBastetSubnetConflicts because they are never created. Without this, such
            // a subnet would always look like a duplicate of its own target once that target exists.
            bool encompassesAPrefix = vnet.Ipv4AddressPrefixes
                .Any(p => string.Equals(p, subnet.AddressPrefix, StringComparison.OrdinalIgnoreCase));

            if (encompassesAPrefix)
            {
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

            // Bastet requires {NetworkAddress, Cidr} to be unique, so the address is what blocks the
            // import. The resource ID only tells us whether we are the ones who put it there.
            ExistingSubnetSnapshot? exact = existingSubnets.FirstOrDefault(e =>
                e.Cidr == cidr && string.Equals(e.NetworkAddress, network, StringComparison.OrdinalIgnoreCase));

            if (exact is null)
            {
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
            subnet.IsSelectable = false;
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

        /// <summary>
        /// Finds the deepest (largest CIDR) existing Bastet subnet that strictly contains the given
        /// network — the subnet an auto-created child would be parented under.
        /// </summary>
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

        // -------------------------------------------------------------------
        // Plan item construction
        // -------------------------------------------------------------------
        private BulkImportPlanItem BuildPlanItem(
            ParsedPrefixSelection p,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets,
            bool renameMatched)
        {
            BulkImportPlanItem item = new()
            {
                VNetName = p.Source.VNetName,
                VNetResourceId = p.Source.VNetResourceId,
                VNetPrefix = p.Source.AddressPrefix,
                PrefixNetworkAddress = p.PrefixNetwork,
                PrefixCidr = p.PrefixCidr
            };

            // 1) Exact match?
            ExistingSubnetSnapshot? exact = existingSubnets.FirstOrDefault(s =>
                s.Cidr == p.PrefixCidr && string.Equals(s.NetworkAddress, p.PrefixNetwork, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
            {
                item.TargetType = BulkImportTargetType.ExactMatch;
                item.ExistingTargetSubnetId = exact.Id;
                item.ExistingTargetSubnetName = exact.Name;

                // Hard fail (5b) if the matched Bastet subnet is non-empty.
                if (exact.HasChildSubnets)
                {
                    item.Errors.Add(
                        $"Cannot import VNet prefix {p.Source.AddressPrefix}: matched Bastet subnet '{exact.Name}' ({exact.NetworkAddress}/{exact.Cidr}) already has child subnets.");
                }
                if (exact.HasHostIpAssignments)
                {
                    item.Errors.Add(
                        $"Cannot import VNet prefix {p.Source.AddressPrefix}: matched Bastet subnet '{exact.Name}' ({exact.NetworkAddress}/{exact.Cidr}) already has host IP assignments.");
                }
                if (exact.IsFullyAllocated)
                {
                    item.Errors.Add(
                        $"Cannot import VNet prefix {p.Source.AddressPrefix}: matched Bastet subnet '{exact.Name}' ({exact.NetworkAddress}/{exact.Cidr}) is marked as fully allocated.");
                }

                if (renameMatched)
                {
                    string proposed = TruncateAndSanitizeName(p.Source.VNetName);
                    if (!string.Equals(proposed, exact.Name, StringComparison.Ordinal))
                    {
                        item.WillRename = true;
                        item.NewName = proposed;
                    }
                }
            }
            else
            {
                // 2) Find deepest containing Bastet subnet
                ExistingSubnetSnapshot? deepest = FindDeepestContainer(p.PrefixNetwork, p.PrefixCidr, existingSubnets);

                if (deepest is not null)
                {
                    item.TargetType = BulkImportTargetType.AutoCreateChild;
                    item.AutoCreateParentSubnetId = deepest.Id;
                    item.AutoCreateParentSubnetName = deepest.Name;
                    item.AutoCreateTargetName = TruncateAndSanitizeName(p.Source.VNetName);

                    // The auto-created target's parent must be eligible to receive children
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
                    item.AutoCreateTargetName = TruncateAndSanitizeName(p.Source.VNetName);
                }
            }

            // 3) Determine fully-encompassing child (if any)
            ParsedSubnetSelection? fullyEncompassing = p.Subnets.FirstOrDefault(s => s.FullyEncompasses);
            if (fullyEncompassing is not null)
            {
                item.WillMarkFullyAllocated = true;
                item.FullyAllocatingAzureSubnetName = fullyEncompassing.Source.Name;
            }

            // 4) Build planned child subnets (excluding the fully-encompassing one)
            HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
            string? targetExistingName = exact?.Name;
            string? targetAutoCreatedName = item.AutoCreateTargetName;

            // Reserve the target's own name so child subnets don't collide with it visually
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
                    continue; // do not create as child; instead mark target fully allocated
                }

                string baseName = TruncateAndSanitizeName(sub.Source.Name);
                if (string.IsNullOrEmpty(baseName))
                {
                    baseName = $"{sub.Network}_{sub.Cidr}";
                }

                string finalName = DisambiguateName(baseName, usedNames, p.Source.VNetName);
                usedNames.Add(finalName);

                item.ChildSubnets.Add(new BulkImportPlannedChildSubnet
                {
                    OriginalAzureName = sub.Source.Name,
                    Name = finalName,
                    NetworkAddress = sub.Network,
                    Cidr = sub.Cidr,
                    FullyEncompassesTarget = false,
                    AzureResourceId = sub.Source.AzureResourceId
                });
            }

            return item;
        }

        // -------------------------------------------------------------------
        // Conflict detection helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Detect overlaps between any two selected VNet IPv4 prefixes.
        /// Equal prefixes from different VNets and one-contains-the-other both qualify.
        /// </summary>
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

        /// <summary>
        /// Detect overlaps between any two selected Azure subnets, even across VNets.
        /// </summary>
        private void DetectAzureSubnetOverlaps(IReadOnlyList<ParsedPrefixSelection> parsed, BulkImportPlanViewModel plan)
        {
            // Flatten everything so each comparison is straightforward.
            List<(ParsedPrefixSelection prefix, ParsedSubnetSelection subnet)> all = [];
            foreach (ParsedPrefixSelection p in parsed)
            {
                foreach (ParsedSubnetSelection s in p.Subnets)
                {
                    if (s.FullyEncompasses)
                    {
                        continue; // these are not created; only mark the target fully allocated
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

        /// <summary>
        /// Hard-fail if any selected Azure subnet's network/CIDR already exists in Bastet (anywhere in the tree).
        /// Bastet enforces global uniqueness of network/CIDR; importing a duplicate would fail at commit anyway,
        /// so we surface it during preview.
        /// </summary>
        private static void DetectExistingBastetSubnetConflicts(
            IReadOnlyList<ParsedPrefixSelection> parsed,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets,
            BulkImportPlanViewModel plan)
        {
            foreach (ParsedPrefixSelection p in parsed)
            {
                foreach (ParsedSubnetSelection s in p.Subnets)
                {
                    if (s.FullyEncompasses)
                    {
                        continue; // these don't get created
                    }

                    bool exists = existingSubnets.Any(e =>
                        e.Cidr == s.Cidr &&
                        string.Equals(e.NetworkAddress, s.Network, StringComparison.OrdinalIgnoreCase));

                    if (exists)
                    {
                        plan.GlobalErrors.Add(
                            $"Azure subnet '{s.Source.Name}' ({s.Source.AddressPrefix}, VNet '{p.Source.VNetName}') already exists in Bastet.");
                    }
                }
            }
        }

        /// <summary>
        /// Hard-fail if any VNet prefix would, when created in Bastet, contain an existing Bastet subnet
        /// (which would create an invalid hierarchy, e.g. importing 10.0.0.0/16 when 10.0.0.0/24 already exists
        /// without 10.0.0.0/16 also existing).
        /// </summary>
        private void DetectVNetPrefixWouldContainExistingSubnet(
            IReadOnlyList<ParsedPrefixSelection> parsed,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets,
            BulkImportPlanViewModel plan)
        {
            // Only matters when the prefix is being *created* (not when it's an exact match).
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
                    // Would the new VNet target contain this existing subnet?
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

        // -------------------------------------------------------------------
        // Utility helpers
        // -------------------------------------------------------------------

        /// <summary>Returns true when two IPv4 CIDR prefixes overlap (one contains the other, or they are equal).</summary>
        private bool PrefixesOverlap(string aNetwork, int aCidr, string bNetwork, int bCidr)
        {
            if (aCidr == bCidr && string.Equals(aNetwork, bNetwork, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Either a contains b, or b contains a.
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

        private string TruncateAndSanitizeName(string? rawName)
        {
            string sanitized = sanitizationService.SanitizeName(rawName);
            if (sanitized.Length > MaxSubnetNameLength)
            {
                sanitized = sanitized[..MaxSubnetNameLength];
            }
            return sanitized;
        }

        /// <summary>
        /// If <paramref name="baseName"/> is already used, append a VNet suffix to disambiguate,
        /// preserving the 50-character limit. Falls back to numeric suffixes if even the suffixed
        /// name collides.
        /// </summary>
        private static string DisambiguateName(string baseName, HashSet<string> usedNames, string vnetName)
        {
            if (!usedNames.Contains(baseName))
            {
                return baseName;
            }

            // Trim VNet name to a short suffix
            string vnetSuffix = vnetName ?? string.Empty;
            if (vnetSuffix.Length > 20)
            {
                vnetSuffix = vnetSuffix[..20];
            }

            string candidate = TruncateForName($"{baseName} ({vnetSuffix})");
            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }

            // Fall back to numeric suffix
            for (int i = 2; i < int.MaxValue; i++)
            {
                string numbered = TruncateForName($"{baseName} ({vnetSuffix} {i})");
                if (!usedNames.Contains(numbered))
                {
                    return numbered;
                }
            }

            // Practically unreachable
            return baseName;
        }

        private static string TruncateForName(string s) =>
            s.Length > MaxSubnetNameLength ? s[..MaxSubnetNameLength] : s;

        // -------------------------------------------------------------------
        // Internal scratch types — keep them private so the planner's surface area is just BuildPlan().
        // -------------------------------------------------------------------
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
