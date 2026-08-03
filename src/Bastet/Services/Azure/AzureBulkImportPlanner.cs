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
        /// Maximum length for <see cref="Models.Subnet.Name"/>; matches the [MaxLength(100)] attribute
        /// on the entity, which is wide enough for any Azure subnet name (Azure allows 80).
        /// </summary>
        private const int MaxSubnetNameLength = 100;

        /// <summary>
        /// True when an existing Bastet subnet is already the import target of this very VNet.
        /// </summary>
        /// <remarks>
        /// This is what separates a top-up - adding the subnets an already-imported VNet has gained -
        /// from adopting a subtree somebody built by hand. Only the former re-stamps a resource ID
        /// the row already carries, which is a no-op; the latter would claim rows nobody imported
        /// and pull them into a later reconcile cascade.
        /// </remarks>
        private static bool IsSameVNet(ExistingSubnetSnapshot existing, string? vnetResourceId) =>
            !string.IsNullOrEmpty(existing.AzureResourceId)
            && !string.IsNullOrEmpty(vnetResourceId)
            && string.Equals(existing.AzureResourceId, vnetResourceId, StringComparison.OrdinalIgnoreCase);

        private static bool IsSameVNet(ExistingSubnetSnapshot existing, BulkAzureVNetViewModel vnet) =>
            IsSameVNet(existing, vnet.ResourceId);

        /// <summary>
        /// True when a row from the SAME Azure subnet, holding a DIFFERENT range, is already in the
        /// tree - so this commit's row needs qualifying even though this commit only carries one
        /// selection for that Azure subnet.
        /// </summary>
        /// <remarks>
        /// Deliberately narrow. Seeding the disambiguator from the whole existing tree instead would
        /// rename any child whose Azure name merely matched some unrelated Bastet subnet anywhere -
        /// a broad silent rename in the ordinary path - and would append the VNet name rather than
        /// the range, giving a second row a different shape from the first. Keying on the Azure
        /// resource ID fires only for the real multi-row case and keeps the one shape.
        ///
        /// The already-persisted first row keeps its bare name and stays unambiguous, because the
        /// row being added is the one that gets qualified.
        /// </remarks>
        private static bool HasPersistedSiblingFromSameAzureSubnet(
            ParsedSubnetSelection sub,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets) =>
            !string.IsNullOrEmpty(sub.Source.AzureResourceId)
            && existingSubnets.Any(e =>
                string.Equals(e.AzureResourceId, sub.Source.AzureResourceId, StringComparison.OrdinalIgnoreCase)
                && !(e.Cidr == sub.Cidr
                     && string.Equals(e.NetworkAddress, sub.Network, StringComparison.OrdinalIgnoreCase)));

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

            if (selection.VNetPrefixes is null or { Count: 0 })
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
                // A null entry in the list is as reachable as a null list: both come from the body.
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
            // Computed across the WHOLE commit, not per item. BuildPlanItem runs once per selected
            // VNet address prefix, so a per-item grouping only ever sees that prefix's rows: an Azure
            // subnet owning one prefix under 10.71.0.0/16 and another under 10.72.0.0/16 looked
            // single-prefix to both items, and the qualification that exists to keep those rows
            // distinguishable was silently skipped - two children with the same name AND the same
            // AzureResourceId, differing only by CIDR.
            //
            // The FullyEncompasses / non-empty-resource-id filter is kept exactly as it was: an
            // encompassing selection marks the target fully allocated instead of creating a child,
            // so counting it would inflate the group and needlessly rename the one child that IS
            // created. (A subnet may equal one VNet prefix exactly and still hold a prefix inside
            // another, so this is reachable rather than theoretical.)
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
                    p, existingSubnets, selection.RenameMatchedBastetSubnets, multiPrefixResourceIds);
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
                vnet.Prefixes = [.. vnet.Ipv4AddressPrefixes.Select(p => AnnotatePrefix(p, vnet, existingSubnets))];

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
                // Matching on address says nothing about which Azure resource the row came from:
                // two VNets in one subscription may carry the same prefix, which is ordinary in
                // hub-and-spoke and dev/prod topologies. Importing this one would replace the
                // recorded link, after which reconcile measures the row against a VNet it was never
                // imported from and offers it - and its subtree - for deletion when that VNet goes.
                // Name both resources: "blocked" alone does not explain a same-prefix collision.
                //
                // Checked FIRST because the top-up allowance below turns on it.
                if (!string.IsNullOrEmpty(exact.AzureResourceId)
                    && !string.IsNullOrEmpty(vnet.ResourceId)
                    && !string.Equals(exact.AzureResourceId, vnet.ResourceId, StringComparison.OrdinalIgnoreCase))
                {
                    return Blocked(result,
                        $"Bastet subnet '{exact.Name}' is already linked to Azure VNet '{exact.AzureResourceId}'. "
                        + $"Importing '{vnet.ResourceId}' would replace that link, so it is refused.");
                }

                // TOP-UP. A populated target used to be refused outright, which left an Azure subnet
                // that gained a prefix after import impossible to import by any route while BASTET
                // went on advertising the Azure-assigned range as free space.
                //
                // Narrowed rather than removed: the allowance requires the target to be linked to
                // THIS VNet already, so this is a continuation of an import that has happened, not
                // the adoption of a hand-built subtree. Adoption is what re-stamps AzureResourceId
                // on rows nobody imported and puts them inside a later reconcile cascade.
                bool isTopUp = IsSameVNet(exact, vnet);

                if (exact.HasChildSubnets && !isTopUp)
                {
                    return Blocked(result,
                        $"Bastet subnet '{exact.Name}' already has child subnets and is not linked to this VNet. Already imported?");
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
                // Distinct copy: "Will import into existing Bastet subnet 'X'" reads as a first
                // import and would be a lie about a target that already holds rows.
                result.Reason = exact.HasChildSubnets
                    ? $"Will add any missing subnets to existing Bastet subnet '{exact.Name}'. Subnets already imported are left untouched."
                    : $"Will import into existing Bastet subnet '{exact.Name}'.";
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
            bool renameMatched,
            IReadOnlySet<string> multiPrefixResourceIds)
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

                // Hard fail (5b) if the matched Bastet subnet is non-empty - EXCEPT for a top-up,
                // where the target is already linked to this same VNet. See AnnotatePrefix for why
                // the allowance is narrowed to that case rather than dropped.
                if (exact.HasChildSubnets && !IsSameVNet(exact, p.Source.VNetResourceId))
                {
                    item.Errors.Add(
                        $"Cannot import VNet prefix {p.Source.AddressPrefix}: matched Bastet subnet '{exact.Name}' ({exact.NetworkAddress}/{exact.Cidr}) already has child subnets and is not linked to this VNet.");
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

                // See AnnotatePrefix: an exact address match may still be a different Azure VNet,
                // and silently repointing the link is what makes reconcile archive the row later.
                if (!string.IsNullOrEmpty(exact.AzureResourceId)
                    && !string.IsNullOrEmpty(p.Source.VNetResourceId)
                    && !string.Equals(exact.AzureResourceId, p.Source.VNetResourceId, StringComparison.OrdinalIgnoreCase))
                {
                    item.Errors.Add(
                        $"Cannot import VNet prefix {p.Source.AddressPrefix}: matched Bastet subnet '{exact.Name}' "
                        + $"({exact.NetworkAddress}/{exact.Cidr}) is already linked to Azure VNet '{exact.AzureResourceId}', "
                        + $"and importing '{p.Source.VNetResourceId}' would replace that link.");
                }

                // Never on a top-up. Renaming a target that already holds imported rows changes a
                // label the operator has been living with, for a run whose purpose is to add the
                // one subnet that was missing.
                if (renameMatched && !exact.HasChildSubnets)
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
                // 2) Find deepest containing Bastet subnet
                ExistingSubnetSnapshot? deepest = FindDeepestContainer(p.PrefixNetwork, p.PrefixCidr, existingSubnets);

                if (deepest is not null)
                {
                    item.TargetType = BulkImportTargetType.AutoCreateChild;
                    item.AutoCreateParentSubnetId = deepest.Id;
                    item.AutoCreateParentSubnetName = deepest.Name;
                    item.AutoCreateTargetName = TargetName(p);

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
                    item.AutoCreateTargetName = TargetName(p);
                }
            }

            // 3) Determine fully-encompassing child (if any)
            ParsedSubnetSelection? fullyEncompassing = p.Subnets.FirstOrDefault(s => s.FullyEncompasses);
            if (fullyEncompassing is not null)
            {
                // The commit treats "marks the target fully allocated" and "creates children" as
                // mutually exclusive - it marks the target and then `continue`s past child creation.
                // The planner used to populate both independently, so a selection carrying an
                // encompassing subnet *and* siblings previewed a list of children that the commit
                // then silently refused to create, reported success, and left the target flagged
                // fully allocated so they could never be added later without clearing the flag.
                //
                // Rejected rather than silently emptying the child list, because Azure cannot
                // produce this selection: subnets within a VNet may not overlap, so a subnet
                // covering the whole VNet prefix leaves no room for siblings. Reaching here means
                // the post was crafted or corrupted, and quietly dropping part of it would hide
                // that.
                if (p.Subnets.Count > 1)
                {
                    item.Errors.Add(
                        $"Azure subnet '{TruncateAndSanitizeName(fullyEncompassing.Source.Name)}' covers the whole of "
                        + $"{p.PrefixNetwork}/{p.PrefixCidr}, so nothing can be created inside it, but "
                        + $"{p.Subnets.Count - 1} other subnet(s) were selected from the same prefix. "
                        + "Azure does not allow overlapping subnets in a VNet, so this selection cannot be applied.");
                    return item;
                }

                // A target that already holds children cannot also be "fully allocated by one Azure
                // subnet" - the two describe different states, and the commit marks the flag INSTEAD
                // of creating children, so the existing rows would be stranded under a target
                // claiming nothing more fits. The old blanket refusal of populated targets was
                // preventing this incidentally; the top-up allowance makes it reachable, so it is
                // now refused explicitly.
                if (exact is not null && exact.HasChildSubnets)
                {
                    item.Errors.Add(
                        $"Cannot import VNet prefix {p.Source.AddressPrefix}: Azure subnet "
                        + $"'{TruncateAndSanitizeName(fullyEncompassing.Source.Name)}' covers the whole prefix, which would "
                        + $"mark Bastet subnet '{exact.Name}' fully allocated, but it already has child subnets.");
                    return item;
                }

                item.WillMarkFullyAllocated = true;

                // Sanitized like every other Azure-derived name here. This one lands in the target's
                // Description via AppendFullyAllocatedNote, and every other write to that column in
                // the commit guarantees it is HTML-stripped - this was the single assignment that
                // skipped it, quietly making that invariant false. The value arrives raw because
                // GlobalSanitizationFilter does not descend into the nested selection list, so the
                // planner is where it has to be handled.
                item.FullyAllocatingAzureSubnetName = TruncateAndSanitizeName(fullyEncompassing.Source.Name);
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
                else if (multiPrefixResourceIds.Contains(sub.Source.AzureResourceId)
                         || HasPersistedSiblingFromSameAzureSubnet(sub, existingSubnets))
                {
                    // An Azure subnet owning several IPv4 prefixes contributes one selection per
                    // prefix, all carrying the same Azure name, and Subnet.Name has a non-unique
                    // index - so without this they persist as rows distinguishable only by CIDR.
                    // Name each for the range it actually holds. A subnet contributing a single
                    // selection with no persisted sibling is untouched, so ordinary imports keep the
                    // exact names they have always had.
                    baseName = SubnetNaming.WithSuffix(
                        baseName, $" ({sub.Network}/{sub.Cidr})", MaxSubnetNameLength);
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

        /// <summary>
        /// The auto-created target subnet's name, with the same empty-name fallback the child names
        /// already had. TruncateAndSanitizeName strips markup, so a VNet name that is entirely markup
        /// sanitizes to empty - and ValidateSubnetCreation never inspects Name, so an empty one was
        /// persisted while every interactive write path refuses it. EditSubnetViewModel carries a
        /// comment about exactly this hazard ("StripHtml can empty a name outright, defeating
        /// [Required]"); this was the one write with the same sanitizer output and no equivalent guard.
        /// </summary>
        private string TargetName(ParsedPrefixSelection prefix) =>
            TruncateAndSanitizeName(prefix.Source.VNetName) is { Length: > 0 } name
                ? name
                : $"{prefix.PrefixNetwork}_{prefix.PrefixCidr}";

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
        /// staying within <see cref="MaxSubnetNameLength"/>. Falls back to numeric suffixes if even
        /// the suffixed name collides.
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

            string candidate = WithSuffix(baseName, $" ({vnetSuffix})");
            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }

            // Fall back to numeric suffix. Every attempt keeps its suffix, so the candidates are all
            // distinct; with at most usedNames.Count names taken, one of these attempts is free.
            for (int i = 2; i <= usedNames.Count + 2; i++)
            {
                string numbered = WithSuffix(baseName, $" ({vnetSuffix} {i})");
                if (!usedNames.Contains(numbered))
                {
                    return numbered;
                }
            }

            // Unreachable by the counting argument above; keeps the method total.
            return WithSuffix(baseName, $" ({vnetSuffix} {usedNames.Count + 3})");
        }

        /// <summary>
        /// Appends <paramref name="suffix"/> within the name limit by shortening the base name.
        /// Shared with the Create form's generated name so the two cannot drift apart.
        /// </summary>
        private static string WithSuffix(string baseName, string suffix) =>
            SubnetNaming.WithSuffix(baseName, suffix, MaxSubnetNameLength);

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
