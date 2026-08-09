# Bastet - Round-17 Audit Findings

branch `audit/round-17` / HEAD `425ec8d` / 752 tests passing / 2026-08-08

Reviewed with the owner against the product model: **Bastet is the authority and answers from its own
records. Azure is not authoritative in Bastet at all - it is a source you import from, which is what
the import wizard is for. Azure and Bastet state are compared in exactly two places: import (can this
be added?) and reconcile (can this be deleted?).** Findings that argued Bastet should know about Azure
space it never imported are struck; see the bottom of this file.

16 findings were filed (Q22 was found by the owner during review, not by the round). 3 are fixed
(Q1, Q3, Q22), 13 stand, 5 were struck as invalid, and 2 more are flagged as edge cases for the owner
to accept or drop.

# Critical

# High

## Q3 - FIXED - Wizard offered "Rename only" on a fully-allocated target, then refused that same rename `[x2]`
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:495 (unconditional refusal); :213 (WouldRenameTarget set before the fully-allocated branch); src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:144-148, :200-217, :340; src/Bastet/Models/ViewModels/AzureBulkImportViewModels.cs:206 (CanCommit is all-or-nothing)
**Breaks:** A collapsed target (one Bastet row, IsFullyAllocated, linked) that the operator has renamed comes back `wouldRenameTarget:true`, so the wizard enables its checkbox, badges it "Rename only" and promises "The only change would be renaming the Bastet subnet to match the VNet name." Preview then hits `if (exact.IsFullyAllocated)` at :495 and errors "is marked as fully allocated", CanCommit false. Full allocation is irrelevant to a rename, which puts nothing inside the target. Because CanCommit is all-or-nothing and Select all ticks the row, one such target refuses the entire batch with a 400.
**Repro:** Rename a fully-allocated linked row, reopen the wizard, tick "Rename matched Bastet subnets to VNet names". Checkbox enables, badge reads "Rename only", preview errors, commit disabled. Same flow on a non-fully-allocated target commits cleanly.
**Fix:** Same feature as Q22 - fix together. At :495 guard the refusal with what it protects: `if (exact.IsFullyAllocated && p.Subnets.Any(s => !s.FullyEncompasses))`. Do not delete WouldRenameTarget/isRenameOnly - renames must stay offered.
**Residue of:** 23233f2 (Mass Claude Audit Mess Cleanup #167)

## Q10 - Reconcile destroys the finding, not just the delete: a linked row's range change is never reported `[x1]`
**Where:** src/Bastet/Services/Azure/AzureReconciler.cs:245-266 (`plan.Items.RemoveAll(blocked.Contains)` at :263), reached from :117, :121, :126 and :223
**Breaks:** Reconcile exists to report linked resources that are gone and linked resources whose range changed. When a row has any descendant the scan proved healthy, WithholdTargetsWhoseCascadeIsBlocked deletes the whole item and leaves one warning saying only that it was withheld from deletion - no status, no reason, no remedy. The item's own true text ("VNet 'X' still exists but no longer has the address prefix 10.200.0.0/16. Delete it here if you want to, then use the Azure import wizard...") is thrown away. Bastet keeps asserting the stale range and every rescan repeats the contentless warning. Withholding the delete is defensible; withholding the report is not.
**Repro:** Import a VNet 10.200.0.0/16 with subnet 10.200.1.0/24 linked; re-range the VNet in Azure to 10.200.0.0/15. Scan returns `items: []` and one warning naming the row. "no longer has the address prefix" appears nowhere. Higher consequence: delete the VNet in Azure entirely and the VNetDeleted item is produced and destroyed, so the operator is never told the VNet is gone.
**Fix:** In that helper, move each blocked item into `plan.ReviewItems` with Status and Reason preserved, as the HeldByManualContent path at :95-104 already does. Append a factual sentence naming what blocks the cascade. Deletion stays blocked - `stillStale` is built from `plan.Items` only.
**Residue of:** a8f669b (Audit 8 Cleanup #152)

# Medium

## Q22 - FIXED - "Rename matched Bastet subnets to VNet names" renamed the VNet target only; child subnets were never offered `[owner]`
**Where:** src/Bastet/Models/ViewModels/AzureBulkImportViewModels.cs:104-106 (WillRename/NewName exist only on BulkImportPlanItem, the target); :147-159 (BulkImportPlannedChildSubnet has no rename field at all); src/Bastet/Controllers/SubnetController.BulkAzure.cs:335 onward (the child loop only creates - an already-imported child never enters item.ChildSubnets); src/Bastet/Views/Azure/BulkImport/_StepSelection.cshtml:54 (the control's label says "subnets")
**Breaks:** The control is labelled "Rename matched Bastet subnets to VNet names" and renames exactly one row: the VNet target. Imported child subnets whose Bastet names have drifted from their Azure names are shown as "Already imported as Bastet subnet 'ping'", not selectable, with no rename offered - and no other surface corrects them either, since reconcile never edits a row. The operator's only remedy is editing every child by hand, which is precisely what the toggle exists to avoid on a large import.
**Repro:** Import a VNet with two subnets, rename both Bastet children (`a` -> `ping`, `b` -> `pong`), reopen the wizard and turn the rename switch on. The target renames; both children stay `ping` and `pong` with no offer. Verified live against rig-batch-17 10.60.0.0/16.
**Fix:** Give BulkImportPlannedChildSubnet the same WillRename/NewName the target has; when the switch is on, emit already-imported children as rename candidates instead of dropping them at annotation; rename them in the commit loop rather than skipping. Q3 is the same feature failing on the other axis - fix them together, and make the label match whatever the final scope is.
**Residue of:** none - the rename feature has only ever covered the target.

## Q4 - Unallocated-range rows contradict themselves: the size does not match the start/end it prints `[x2]`
**Where:** src/Bastet/Services/IpUtilityService.cs:306-315 (head branch, count at :313); :230-255 (no-children early return, `subnetSize - 2`); :330-349 (tail branch, `lastIp--`); src/Bastet/Views/Subnet/Details/_UnallocatedRanges.cshtml:38-40, :46-52
**Breaks:** The head branch prints `AddressCount = Start - currentPosition - 1` over a span of `Start - currentPosition`. On the live rig, 10.20.0.0/16 with child 10.20.1.0/24 renders `10.20.0.0 | 10.20.0.255 | 255 IP addresses` - 256 addresses labelled 255. Worse, 10.100.0.0/24 with child 10.100.0.1/32 renders `10.100.0.0 | 10.100.0.0 | 0 IP addresses` with a live Create Subnet button, and creating 10.100.0.0/32 succeeds. The no-children return prints `subnetSize - 2` over a full span; the tail branch drops the last address. Every branch reports less allocatable space than ValidateSubnetCreation accepts.
**Repro:** /Subnet/Details on any subnet with a head or tail gap. 10.0.0.0/24 + child 10.0.0.64/26 -> `10.0.0.0 | 10.0.0.63 | 63 IP addresses`; POST 10.0.0.0/26 -> 302 and the row vanishes.
**Fix:** Put all three branches on one axis so `AddressCount == EndIp - StartIp + 1` everywhere: delete the `currentPosition == startIp && cidr < 31` special case at :306-315 (the existing else already emits the true span), collapse :230-255 to `AddressCount = subnetSize`, and drop `if (cidr < 31) { lastIp--; }` at :330-349. Rewrite the counts pinned in SubnetPropertyCalculationTests.cs:185/:201/:222/:235/:248. Product question for the owner: if the Size column is meant to be usable hosts, the arithmetic is right and the display is wrong - then rename the column and stop seeding Create Subnet from an excluded address.
**Residue of:** bf120d6 (Audit 4 Cleanup #141)

## Q7 - Import refuses a subnet whose Bastet parent is more specific than the VNet prefix; the Create form accepts the identical range `[x1]`
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:368-378 (AnnotateSubnet); :724-730 (same refusal as a commit GlobalError); src/Bastet/Controllers/SubnetController.BulkAzure.cs:360, :385 (children always parented to `targetSubnet.Id`); :438 (FindDeepestContainer, already available)
**Breaks:** With Bastet holding 10.20.0.0/16 and a hand-made 10.20.4.0/22 under it, Azure subnet 10.20.5.0/24 is Blocked "Has a more specific existing Bastet parent 'covers-app' (10.20.4.0/22), so it cannot be imported into this VNet prefix" - naming no remedy. POST /Subnet/Create with ParentSubnetId=covers-app creates the identical range without complaint, so the IPAM itself has no objection. The refusal exists only because the commit path hardcodes the parent to the VNet target.
**Repro:** Base validation probed directly: POST /Subnet/Create 10.20.5.0/24 under 10.20.0.0/16 -> "A more specific parent subnet exists: covers-app (10.20.4.0/22). Please select it instead." Under covers-app -> 302.
**Fix:** Not a one-liner, and worth its own decision. Resolve each planned child's parent with the existing FindDeepestContainer at :438, record it on BulkImportPlannedChildSubnet, and use it in place of the hardcoded `ParentSubnetId = targetSubnet.Id` at :360/:385; only then delete :368-378 and :724-730. Do NOT remove the sibling containment guard at :359-365 - the manual form enforces that one too. Interim: reword the refusal to name the remedy ("create it under 'covers-app' manually").
**Residue of:** none

## Q8 - Delete confirmation binds the reviewed subtree but not the reviewed row's own range `[x1]`
**Where:** src/Bastet/Controllers/SubnetController.Delete.cs:176-183; :32-43; src/Bastet/Views/Subnet/Delete/_DeleteConfirmationForm.cshtml; src/Bastet/Models/Subnet.cs:44 (`[Timestamp] RowVersion`, already present)
**Breaks:** The stale-scope check re-derives only MaxDescendantSubnetId and MaxSubtreeHostIpTicks. Both are 0 for a childless leaf and stay 0 however the target row itself is mutated. An operator who reviewed and approved archiving `DMZ 10.77.0.0/24` can have a `/16` archived instead if another operator widened it in between - 256x the reviewed space, irreversibly, and the success banner reports the row's new name. The reversible Edit path already refuses on a RowVersion mismatch, so the irreversible operation is weaker than the reversible one.
**Repro:** GET /Subnet/Delete/1 on a /24, widen to /16 via Edit, replay the original delete body -> 302, row archived as /16, no refusal.
**Fix:** Carry the row's RowVersion in DeleteSubnetViewModel and as a hidden field; inside DeleteConfirmedCore take the stale branch when the loaded RowVersion is non-null and the posted value is null or differs. Guard on the loaded value so SQLite (no `[Timestamp]` value generator) does not fail every delete.
**Residue of:** d18327e (Audit 16 Cleanup #165)

## Q12 - Two disagreeing implementations of the multi-prefix naming rule `[x2]`
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:134-141 (multiPrefixVNetIds, built from the selection); :803-814 (TargetName); :403-412 (ProposedTargetName, keyed off `vnet.Ipv4AddressPrefixes.Count > 1`); :213-214
**Breaks:** TargetName qualifies a name when the *selection* holds more than one prefix of that VNet; ProposedTargetName qualifies when the *VNet* has more than one. Importing a two-prefix VNet one prefix at a time therefore creates two top-level rows both named `rig-multi` - disjoint ranges, indistinguishable in the tree. Importing both at once gives the qualified names. Same VNet, same end state, different names depending only on how many boxes were ticked. Conversely, on a correctly qualified row the annotation sets WouldRenameTarget and the wizard promises a rename BuildPlan does not perform (renamedTargets 0).
**Repro:** Import 10.30.0.0/16 alone, then 172.16.0.0/20 alone, from the same two-prefix VNet -> two rows named `rig-multi`. Both in one selection -> `rig-multi (10.30.0.0-16)` and `rig-multi (172.16.0.0-20)`.
**Fix:** One rule, keyed off the VNet's own prefix count. Add the VNet's IPv4 prefix list to BulkImportSelectedVNetPrefixDto, populate it in buildSelectionFromUI from `vnets[vIdx].ipv4AddressPrefixes`, have TargetName use it, and delete multiPrefixVNetIds so ProposedTargetName is the single implementation.
**Residue of:** 23233f2 (Mass Claude Audit Mess Cleanup #167)

## Q13 - Toggling "Rename matched Bastet subnets to VNet names" silently discards the whole selection and leaves Preview enabled `[x2]`
**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:333 (the handler); :349-367 (the `#bulk-hide-imported` sibling that does it correctly)
**Breaks:** The handler calls `invalidatePlan(); renderVNetTree();`. renderVNetTree empties the tree and rebuilds every checkbox unticked. Unlike its sibling it neither snapshots/restores the ticked state nor calls updateGoPreviewBtn. The operator curates a selection, ticks the rename switch - precisely because they want renames applied to that batch - and the tree silently empties while "Next: Preview" stays enabled. Pressing it posts `{"vNetPrefixes":[]}` and step 3 reads "Cannot import: No VNet address prefixes were selected."
**Repro:** Select all (12/13 prefixes, 14/15 subnets), tick `#bulk-rename-matched` -> 0/13 and 0/15 ticked, preview button still enabled, no message.
**Fix:** Extract the snapshot / renderVNetTree / restore / updateGoPreviewBtn sequence at :350-366 into one named function and call it from both handlers.
**Residue of:** none

## Q16 - Wizard blocks linking a not-yet-linked target purely because it has children `[x1]`
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:216-220; :485-489 (the same rule as a per-item commit error)
**Breaks:** The refusal keys on whether the row is already linked to *this* VNet, not on any conflict. A hand-created Bastet row with any child cannot be linked to its Azure VNet - "already has child subnets and is not linked to this VNet. Already imported?" - a false diagnosis naming no remedy; the only escape is deleting the operator's own subnet. The identical state with the link already present is advertised: "Will add any missing subnets to existing Bastet subnet 'X'."
**Repro:** Hand-create 172.16.0.0/20 with a non-overlapping child, then try to import the matching Azure VNet prefix -> Blocked. Delete the child -> the byte-identical selection commits.
**Fix:** Delete the branch at :216-220 and the error at :485-489. The per-subnet detectors (:263-268, :356-378, :707-731, :752-763) already refuse every overlapping and containment shape, and ValidateSubnetCreation still runs at commit. Four tests pin the removed behaviour: AzureBulkImportTopUpTests.cs:73 and AzureBulkImportPlannerTests.cs:156/:732/:977. Do NOT touch AzureBulkImportTopUpTests:114 - different guard.
**Residue of:** 8afa2df (Audit 14 Cleanup #160)

## Q14 - "Only show what would change" hides WillUpdateExisting, suppressing the work of linking an existing Bastet row `[x1]`
**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:164-172 (prefixHasWork), :192, :296-304 (the empty-state banner); src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:241-247
**Breaks:** prefixHasWork returns true only for a contained selectable subnet, `statusName === "Available"`, or rename-only. A prefix whose exact-match Bastet row is not yet linked comes back WillUpdateExisting with IsSelectable true - selecting it writes the VNet resource id onto the row - but with no selectable contained subnets it is hidden by the filter. When it is the only survivor the banner asserts the hidden prefixes are "either already imported or cannot be imported", both false. The row then never gets an AzureResourceId, so reconcile can never track that VNet.
**Repro:** Hand-create a Bastet row matching an Azure VNet prefix exactly, AzureResourceId null. Wizard shows "Will update existing"; tick the filter and it vanishes.
**Fix:** In prefixHasWork add `if (prefixInfo.statusName === "WillUpdateExisting") { return true; }`. It cannot over-fire - the only other route to that status already returned true on the selectable-subnet test.
**Residue of:** none

# Low

## Q11 - The Create form refuses a subnet name the Edit form stores `[x2]`
**Where:** src/Bastet/Models/ViewModels/SubnetViewModels.cs:11 (`[SafeText]` on Create only); src/Bastet/Models/ViewModels/EditSubnetViewModel.cs:23 (no counterpart); src/Bastet/Services/Security/InputSanitizationService.cs:11
**Breaks:** SafeTextPattern is `^[a-zA-Z0-9\s\-_.,!?@#$%&()+=]*$`, so `/`, `:`, `;`, quotes, brackets and every non-ASCII letter are refused by Create and accepted by Edit. Rename a subnet to "core/edge (site A)" and it saves; create its sibling "core/edge (site B)" and it is refused with "Subnet name contains invalid characters" while the tree displays a name holding that character, put there by the app. Not a security defect - `[NoHtml]` is on both forms and names render encoded at every sink.
**Repro:** Create refuses `Prod: DC1`, `Zürich core`, `core/edge (site B)`; Edit accepts all three on an existing row.
**Fix:** Delete `[SafeText]` from CreateSubnetViewModel.Name so both write paths agree on `[Required][StringLength][NoHtml][SanitizeName]`. Adding it to Edit instead would make every already-stored non-conforming row uneditable, since Edit gates the save on ModelState and prefills Name from the row. Add the parity assertion so a future divergence fails the build. Do this in one commit with Q18.
**Residue of:** 8cefc64 (Audit 5 Cleanup #142)

## Q18 - NoHtml rejects ordinary operator text as "HTML tags" `[x2]`
**Where:** src/Bastet/Services/Security/InputSanitizationService.cs:14 (`HtmlTagPattern` = `<[^>]*>`); src/Bastet/Services/Security/ValidationAttributes.cs:21-23; test/Bastet.Tests/Security/SubnetViewModelValidationParityTests.cs:62
**Breaks:** Any `<` followed later by any `>` is treated as a tag, so "HQ <-> DR" and "temp < 5 and load > 3" are refused with "HTML tags are not allowed", a cause the operator cannot find or remove. The rule is order-dependent: "reserve if load > 50% and temp < 5" is accepted, same characters reversed. It is not what makes the app safe - markup in a name renders fully encoded at every sink.
**Repro:** Description `temp < 5 and load > 3` -> refused. Description `reserve if load > 50% and temp < 5` -> 302, stored verbatim.
**Fix:** Tighten HtmlTagPattern to `</?[A-Za-z][^>]*>` so only element-shaped input counts, and change the parity test's input at line 62 to a genuinely tag-shaped string. Pair with Q11 - the name half stays refused otherwise, because `[SafeText]` admits neither bracket.
**Residue of:** none

## Q19 - The only legitimate reconcile refusal tells the operator to "Move" the content, an operation the app does not offer `[x1]`
**Where:** src/Bastet/Services/Azure/AzureReconciler.cs:100; src/Bastet/Views/Azure/Reconcile/_StepReview.cshtml:76
**Breaks:** There is no move. A host IP's address is readonly and its SubnetId is a hidden field the Edit POST never applies; a subnet has no ParentSubnetId on EditSubnetViewModel and no parent selector. Delete is the only remedy, and it works.
**Repro:** Both sentences render in the same response. GET /HostIp/Edit renders no `<select>` and no "move"; POSTing a changed SubnetId returns 302 and changes nothing.
**Fix:** Both sites, no behaviour change: "Delete it here first, then run the scan again." / "The reason column says which. Delete that content here first, then scan again."
**Residue of:** none

## Q6 - Reconcile step 1 advertises an un-imported-range hunt whose code was deleted `[x2]`
**Where:** src/Bastet/Views/Azure/Reconcile/_StepSubscription.cshtml:6
**Breaks:** The step-1 blurb promises reconcile will report "plus Azure ranges BASTET does not record". No producer for that exists - 23233f2 deleted ReportAzureRangesNoBastetSubnetRecords, correctly, because finding un-imported Azure space is the import wizard's job. The sentence is a leftover promise for a feature that was removed on purpose.
**Repro:** The sentence renders verbatim on GET /Azure/Reconcile; a scan over a partially imported subscription returns `items: []`.
**Fix:** Delete the clause. State only that BASTET reports Azure-linked rows whose VNet or subnet no longer exists, and rows whose Azure resource no longer holds the recorded range.
**Residue of:** d18327e (Audit 16 Cleanup #165)

## Q9 - The free-space panel's "Run Azure Reconcile" link renders for users who cannot open it `[x1]`
**Where:** src/Bastet/Views/Subnet/Details/_UnallocatedRanges.cshtml:19; src/Bastet/Controllers/AzureController.cs:11 (RequireAdminRole) and :133-136 (403 when BASTET_AZURE_IMPORT is off); src/Bastet/Views/Shared/_Layout.cshtml:46-60 (the correct gating pattern)
**Breaks:** The link is gated on nothing. A View/Edit/Delete-role user lands on AccessDenied; with the flag off everyone including Admins gets a 403 reading "Azure Import feature is not enabled", while the nav six lines away correctly hides the same link.
**Repro:** Flag off -> Details renders `<a href="/Azure/Reconcile">` while the nav omits it; following it gives 302 -> /Error/403.
**Fix:** Render the link only when the flag is set and the user is Admin, expressed inline exactly as _Layout.cshtml:46 does (`IsAzureImportEnabled` is internal to Bastet.dll and views compile separately, so a view cannot call it). Keep the warning sentence unconditional.
**Residue of:** none (origin 8afa2df, Audit 14 Cleanup #160)

## Q15 - Host IP delete reports a definite failure for a write whose outcome is unknown `[x2]`
**Where:** src/Bastet/Controllers/HostIpController.cs:407 (blanket catch over the archive transaction); src/Bastet/Controllers/SubnetController.Delete.cs:200-216 (the sibling that classifies); src/Bastet/Services/Data/SqlSaveOutcome.cs:31
**Breaks:** If the connection is severed at CommitAsync, SQL Server may have committed: the assignment is archived and the address free, while the only handler asserts "Error deleting host IP. Details have been logged." - a statement that it did not happen. Every sibling destructive path was given the classifier; this one was not.
**Repro:** Hold `TABLOCKX` on DeletedHostIpAssignments and delete a host IP -> the definite-failure banner, with a SqlException that IsIndeterminateTransaction returns true for. The same fault on /Subnet/Delete correctly reports "BASTET could not confirm whether this subnet was deleted."
**Fix:** Add `catch (Exception ex) when (SqlSaveOutcome.IsIndeterminateTransaction(ex))` before the generic catch, with the sibling's wording, redirecting to AllHostIps (the current redirect target 404s once the archive lands, and `subnetId` is declared inside the try).
**Residue of:** d18327e (Audit 16 Cleanup #165)

# Info

## Q20 - Azure services and view models carry write-only members `[x2]`
**Where:** src/Bastet/Services/Azure/AzureService.cs:11, :16 (the IIpUtilityService ctor parameter and field - the assembly's only CA1823); src/Bastet/Models/ViewModels/AzureReconcileViewModels.cs:23 (snapshot IsFullyAllocated), :73 (item IsVNetLevel); AzureBulkImportViewModels.cs:69 and AzureReconcileViewModels.cs:104 (IsFeatureEnabled, rendered by no view)
**Breaks:** No user-visible failure. The cost is a later reader taking them for live inputs - IsVNetLevel reads as if the reconcile UI still distinguished VNet-level rows, which it does not.
**Repro:** `-p:AnalysisMode=AllEnabledByDefault` reports one CA1823. Greps find no reader for IsVNetLevel or IsFeatureEnabled outside the writes.
**Fix:** Pure deletions; IIpUtilityService stays registered for its real consumers.
**Residue of:** 23233f2 for the first three; the IsFeatureEnabled pair predates it and never had a reader.

## Q21 - Orphaned usings and members left by the deletion of the single-VNet wizard `[x1]`
**Where:** src/Bastet/Controllers/SubnetController.Azure.cs:1-5,7 (six dead usings); src/Bastet/Controllers/AzureController.cs:1, :3, :7 (three more, including Bastet.Data and EntityFrameworkCore for a DbContext it no longer takes); src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:271-296 (FindMoreSpecificParent's unread `subnet` parameter)
**Breaks:** No runtime consequence. 23233f2 cut SubnetController.Azure.cs from 401 lines to 36 and AzureController.cs from 450 to 212 without touching their using lists.
**Repro:** Deleting the nine usings and the unused parameter builds 0 warnings / 0 errors.
**Fix:** Two commits: the usings, then the parameter and its one call site. The sanitization cluster the original finding also proposed removing (SanitizeString, EncodeHtml, RemoveDangerousScripts, DangerousScriptPattern) is left alone - removing SanitizeString alone orphans three more members.
**Residue of:** mixed

# Flagged as edge cases - owner to accept or drop

## Q5 - Reconcile cannot report an IPv4 range removed from a dual-stack Azure resource `[x1]`
**Where:** src/Bastet/Services/Azure/AzureService.cs:118-121, :170-181; src/Bastet/Controllers/AzureController.cs:88
**Breaks:** The inventory drops VNets and subnets with no IPv4 prefix, so a resource whose IPv4 range was removed (leaving it IPv6-only) never reaches `liveSubnetPrefixes`. It is classified as an absence, the confirmation step asks ARM directly, ARM says it exists, and the item is withheld with "still exist in Azure, so they have been withheld from deletion". The row is pinned forever and no operator action clears it. Legitimate under the product model - Bastet has a link and Azure changed - but it needs a dual-stack VNet and an IPv4 prefix removal to reach.
**Fix:** Stop dropping these from the shared inventory; emit the row with both prefix fields empty so `Ipv4PrefixesOf` returns [] and the "now none" wording fires. Apply the zero-IPv4 filter in AzureController.BulkGetVNets only.
**Residue of:** none

## Q17 - The migration app lock is taken in `master` on a cold start, so it blocks unrelated deployments `[x1]`
**Where:** src/Bastet/Program.cs:307-326, :236, :245, :339-345
**Breaks:** sp_getapplock is database-scoped. When the catalog does not yet exist the code falls back to a `master` connection and holds `Bastet:Migration` there for the whole migration. A second, unrelated Bastet deployment on the same SQL instance, also starting for the first time, blocks up to the 300 s timeout and then aborts with "Another replica appears to be stuck applying migrations" - false, and it points at the wrong system. Meanwhile for the case the lock is *for* - two replicas of one deployment against an existing catalog - EF's own `__EFMigrationsLock` already covers it. Needs two deployments cold-starting on one server.
**Fix:** On the 4060 path, take the master lock only around CREATE DATABASE and release it; then take `Bastet:Migration` in the configured catalog and hold that across Migrate(). Name the catalog in the failure message.
**Residue of:** none

# Struck - reported by the round, invalid under the product model

| Id | Title | Why struck |
| --- | --- | --- |
| Q1 | Prefix declared "Every Azure subnet ... is already recorded" when a contained subnet is blocked | **Fixed.** The message now reads "either already recorded or cannot be imported" when a contained Azure subnet is blocked. The rest of the finding - that the filter hides the blocked row, and that the free-space table offers space Azure has assigned - is invalid: the filter is correct (a blocked row would change nothing if ticked), and Bastet does not and should not know about un-imported Azure space. |
| Q2 | Fully-allocated exact-match reclassified as AlreadyImported, "hiding unimported Azure subnets" | Same invalid premise. The rename half is Q3. What remains is one string that credits an Azure subnet for a fully-allocated flag the operator set by hand - a wording nit, not worth a finding. |
| Q6 (site 3) | Subnet Details banner points at reconcile "which gives a false all-clear" | Invalid. Reconcile is not supposed to find un-imported Azure space. The banner's warning sentence is true and the link target is Q9. |
| Q9 (second half) | "The check it names does not answer the question" | Same invalid premise. Only the gating half stands. |
| - | Reconcile scan-failure staleness warning is unreachable | Refuted in-round: three dead lines, no wrong output. |
| - | AzureReconcilePlanViewModel.CanCommit is a gate nothing consults | Refuted in-round: every clause independently enforced server- and client-side. |
| - | Children-only CalculateUnallocatedRanges overload reports host IPs as free | Refuted in-round: the overload has no production caller and the claimed output does not occur. |
