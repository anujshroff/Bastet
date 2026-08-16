# Bastet - Round-18 Audit Findings

branch audit/round-18
HEAD f6bd1ec
test baseline 820 passing
date 2026-08-15
Round 18 filed 23 findings, of which 20 are residue of round 17's own fixes.

## Triage — dispositions approved by the owner, 2026-08-15

Fix this round, machinery-deletions before string fixes in shared files; re-cite line numbers after
R1 moves AzureReconciler.cs:

- **fix:** R1 (High; deletes machinery), R3 (narrow half only: remove the RowVersion refresh at all
  three sites; the redisplay-path collapse is deferred), R7, R8, R20, R21, R22, R23 (verified
  deletions / small gating), R12, R13, R15, R17, R18 (`strings` batch), R14, R16 (small
  conditionals, not in the strings batch), R19 (minimal variant only: the one clause at :675),
  R2 (own reviewed item, outside restructure scope, riskiest — do last).
- **defer to the planner/wizard restructure**, transplanted whole with repros into
  docs/DEFERRED-FINDINGS.md: R4, R5, R6, R9, R10, R11, plus R3's and R19's structural halves —
  each is an instance of the duplication the restructure exists to collapse.

## Critical

None.

## High

## R1 - Reconcile pins a row Azure confirmed deleted, because a descendant is in another subscription `[x2]` — FIXED
_Fixed. Deleted the `notCovered` cross-subscription cascade withhold and removed `stillLive` from the ApplyConfirmations cascade set; the manual-content and notVisible/unknown withholds remain._
_Swept: "notCovered", "different subscription", "not checked by this scan" across src/ and test/ — no other site; views render Warnings generically._
_Verified: two new tests failed pre-fix, pass post-fix; build 0 warnings; 822/822._
_Reviewed: pass — reviewer traced transitive ManualDescendantCount/HostIpCount in AzureSubnetSnapshotService and delete-time Conflict re-check; archives, not destroys._

## Medium

## R2 - Delete scope guard keys host IP additions on a wall clock, so a not-newer insert is archived silently `[x1]`
**Where:** src/Bastet/Controllers/SubnetController.Delete.cs:187, :81-91, :41-42, :143-148; Models/ViewModels/DeleteSubnetViewModel.cs:18, :22; Views/Subnet/Delete/_DeleteConfirmationForm.cshtml:5-7
**Breaks:** The subnet half compares database identities, the host IP half a UtcNow watermark with strict `>`. A host IP inserted by a clock-behind replica never trips the guard and is archived against a review that never showed it. Not established: that deployments run replicas with drifting clocks (skew was shimmed in).
**Repro:** Two instances, one shimmed +120s: the POST archived 2 host IPs after a page saying 1.
**Fix:** Filed fix unsound — its interim (`>` to `>=`) refuses every delete of a subnet holding host IPs. Have `MaxSubtreeHostIpTicksAsync` return the subtree count, feed `HostIpCount` and the guard from it (deleting `CountAllDescendantHostIps`), post it as a nullable field joined to :143, and drop the watermark.
**Residue of:** d18327e (Audit 16 #165), which added the watermark and guard.

## R3 - Edit concurrency redisplay refreshes RowVersion, so the retry it instructs reverts the other commit `[x1]`
**Where:** src/Bastet/Controllers/SubnetController.Edit.cs:178 (msg :192-195), :255 (msg :259-263); Controllers/HostIpController.cs:244 (msg :252-255); Views/Subnet/Edit/_EditForm.cshtml and the HostIp Edit partial
**Breaks:** A opens Edit on 172.16.0.0/12; B saves /13, freeing 172.24.0.0/13. A's stale POST throws, but the redisplay swaps in the current RowVersion while keeping A's fields and calling them current, so Save again reverts the row and discards B's change.
**Repro:** Redisplay showed Cidr 12 with a refreshed RowVersion; the retry returned 302 and SQL showed /12. HostIp identical.
**Fix:** Filed fix unsound as scoped — it threads Current* fields into two redisplay paths that are already duplicates. Delete Edit.cs:170-190 first (all redone at :227-269), then name the differing fields and stored values in the one remaining message.
**Residue of:** Partly — the token refresh is original (3480e52); the untrue sentence is d18327e.

## R4 - A hand-built fully-allocated row cannot be adopted by the VNet it exactly matches `[x1]`
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:503, :217 (annotation half, moves with it), :314-328
**Breaks:** The operator has already built the row the import would produce for 10.20.0.0/24 ('dmz', fully allocated, unlinked) and the wizard refuses it. Adding only the AzureResourceId flips it to AlreadyImported: the refusal keys on provenance and names no remedy.
**Repro:** Unlinked row: Blocked, preview errored, canCommit false; the link-only UPDATE flipped it.
**Fix:** Annotation half unsound (its fall-through lands these on WillUpdateExisting/IsSelectable=true). At :503 use `exact.IsFullyAllocated && p.Subnets.Any(s => !s.FullyEncompasses && LinkedRowForSameAzureSubnet(s, existingSubnets) is null)`; at :211-217 fall through only when nothing would be created.
**Residue of:** f4a0a87 (Audit 17 #170), which relaxed the linked half only.

## R5 - A linked VNet target with host IPs cannot be renamed, while the identical fully-allocated case can `[x1]`
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:207, :498-502; sibling at :211-218, :503-509
**Breaks:** Import rig-empty, rename the row, assign one host IP: the planner computes wouldRenameTarget=true, discards it and returns Blocked "already has host IP assignments", disabling the box under a "Cannot import" badge. The only remedy offered is deleting the host IP data.
**Repro:** Rename-only preview: willRename=True, newName='rig-empty', that error, canCommit false; the fully-allocated control committed renamedTargets 1.
**Fix:** Filed annotation change unsound (its `!isTopUp` guard falls through to IsSelectable=true whenever the VNet has unrecorded subnets). Take :498 as filed; at :207 mirror the sibling — `isTopUp ? AlreadyImported(..."so no subnets can be added inside it.") : Blocked(...)`.
**Residue of:** f4a0a87 (commit path) and 23233f2 (annotation), which relaxed the sibling.

## Low

## R6 - Child-subnet rename offer uses a second naming implementation, so the wizard offers renames it never performs `[x2]`
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:398, :383-384, :615-626 (real base name), :630, :636-652; Views/Azure/BulkImport/_BulkScripts.cshtml:149, :176, :238, :272
**Breaks:** A multi-prefix Azure subnet imports as 'snet-mp (10.81.1.0-24)' and 'snet-mp (10.81.2.0-24)' and both return wouldRenameSubnet=true, because the annotation compares the unqualified name. They render "Rename only" with enabled boxes, but ticking them previews no children and commits renamedChildSubnets 0.
**Repro:** Straight after the import all three created rows returned wouldRenameSubnet=True; preview dropped two, commit renamed 0.
**Fix:** Filed fix not implementable as written (the plan qualifies from selection-derived `multiPrefixResourceIds`; the subnet DTO carries no `Ipv4AddressPrefixes`). Delete `ProposedChildName`, derive the offer from the plan's base-name computation, and add `Ipv4AddressPrefixes` to the DTO.
**Residue of:** f4a0a87, which added child renames with their own naming helper.

## R7 - Tags are still held to the strict SafeText allowlist round 17 removed from names and descriptions `[x2]`
**Where:** src/Bastet/Services/Security/ValidationAttributes.cs:117 (closes both consumers), Models/ViewModels/SubnetViewModels.cs:36, EditSubnetViewModel.cs:36
**Breaks:** POST /Subnet/Create with Name and Description both "10.0.0.0/8" is accepted while the identical Tags value is refused and the create fails — likewise "owner:netops", "Zürich", "site's-gw", "HQ <-> DR". Every Tags sink is Razor-encoded.
**Repro:** Each string errored on Tags alone, Create and Edit; with :117-120 deleted, 820/820.
**Fix:** Delete the `IsSafeText` check from `TagsAttribute.IsValid` (:117-120) and the then-vestigial sanitizer guard at :95-99, leaving MaxTags/MaxTagLength. Do not also delete `SafeTextAttribute` — `IsSafeText` has live callers and tests.
**Residue of:** f4a0a87 — rule is original (3940c98); round 17 freed the name fields only.

## R8 - "Add Child Subnet" is offered on a /32, which the create form must always refuse, and with the wrong reason `[x1]`
**Where:** src/Bastet/Models/ViewModels/SubnetViewModels.cs:95, Views/Subnet/Details/_ChildSubnets.cshtml:8, _HostIpAssignments.cshtml:110, _UnallocatedRanges.cshtml:61 (correct)
**Breaks:** On a /32 with no host IPs the button links to /Subnet/Create?parentId=N and every submission is refused for containment, when the real rule is that a /32 has no room for a child — so the operator cannot tell why.
**Repro:** One Details response carried both the anchor and an empty Action cell; Cidr 32 and 31 gave the containment error, 33 the range error.
**Fix:** Filed fix incomplete — adding `Cidr < 32` to `CanAddChildSubnet` also hides "Mark as Fully Allocated" on every /32, which the server accepts. Add the term at :95, drop the redundant `Model.CanAddChildSubnet &&` at :110, and have :61 read `Model.CanAddChildSubnet`.
**Residue of:** Round 6 (0de1293) added the `Cidr < 32` gate to one entry point only.

## R9 - Toggling "Rename matched Bastet subnets to VNet names" discards the operator's entire selection `[x2]`
**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:358; the complete implementation is at :374-392 in the same file
**Breaks:** `#bulk-hide-imported` snapshots the checked keys around `renderVNetTree()` and restores them; `#bulk-rename-matched` re-renders with no capture, and the render empties the tree first. Every tick is lost, the only signal being "Next: Preview" going disabled.
**Repro:** Select all -> 31 checked; hide-imported on/off preserved 31; the rename toggle left 0 checked with 31 still enabled.
**Fix:** Extract the capture/restore at :375-389 into `rerenderPreservingSelection()` (snapshot, re-render, re-apply to `:not(:disabled)` boxes, `updateGoPreviewBtn()`) and call it from both handlers. Keep the snapshot key.
**Residue of:** 23233f2 added the re-render without the restore; f4a0a87 fixed the button instead.

## R10 - isPrefixUsable re-derives IsSelectable from a status name, so renames-on enables a checkbox the server refuses `[x2]`
**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:169, consumed at :179-188, :249-250, :259; indistinguishable branches at Services/Azure/AzureBulkImportPlanner.cs:213-216 and :222-227
**Breaks:** `AlreadyImported` is produced both when the target is fully allocated and when every contained subnet is recorded, and only the second allows ticking a child. With 10.79.0.0/16 AlreadyImported and snet-fa Available, the rename switch alone enables snet-fa, which the preview refuses.
**Repro:** Renames off -> disabled with blocked badge; on -> enabled; previewing returned "... is marked as fully allocated".
**Fix:** Filed fix unsound (its `CanContainNewSubnets` flag is true on a branch whose precondition is that nothing can be added; its interim re-breaks round 17's child renames). Fix server-side: in `AnnotateSubnet` (~:330-372) mark a subnet not selectable when its exact-match container is fully allocated or holds host IPs.
**Residue of:** f4a0a87 introduced the `renameOn && "AlreadyImported"` clause; 440e0c9 extracted it.

## R11 - `TargetName` is a second implementation of `ProposedTargetName` that reads the client-supplied prefix list `[x1]`
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:840 (called at :523, :541, :557); paired implementation at :417, used by the annotation at :205
**Breaks:** `ProposedTargetName` takes the qualifier decision from the ARM-read prefix list; `TargetName` takes it from `prefix.Source.VNetIpv4AddressPrefixes`, posted by the browser and validated nowhere. Omitting that array returns two plan items both named "rig-dual-prefix", canCommit true. UI-unreachable today.
**Repro:** Same preview three ways: array present -> qualified names; omitted or one self-consistent element -> both unqualified.
**Fix:** Filed guard unsound — a one-element list satisfies it and still produces the wrong output, and it keeps both copies. Collapse `TargetName` and `ProposedTargetName` into one method taking (vnetName, prefixes, network, cidr). Sourcing prefixes from ARM is separate.
**Residue of:** f4a0a87, which made the copies agree via the client DTO rather than deleting one.

## R12 - The Create-Subnet CIDR modal prints usable hosts under the label "Resulting subnet size" `[x2]`
**Where:** src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml:141, :96, :118, :123 (errors, same span); _CidrInputModal.cshtml:23 (label); pattern at _UnallocatedRanges.cshtml:46-47
**Breaks:** On Details for 10.99.0.0/16 the free-space table round 17 split into Size and Max Usable IPs shows 65,536 / 65,534, while the modal on the same screen calls 254 the "subnet size" of a 256-address block. The count is not monotonic; the span also renders "Invalid IP addresses".
**Repro:** `#cidrInput`: /24 -> 254, /30 -> 2, /31 -> 2, /99 -> "Invalid", beside a table reading 65,536 / 65,534.
**Fix:** Cheapest correct version: relabel _CidrInputModal.cshtml:23 to "Maximum usable IPs:" — one string, no script change. If both numbers are wanted, add a second span worded as the table words it, and clear both at all three error sites.
**Residue of:** Original (46b3e69), reaffirmed by round 5 (8cefc64), left by round 17.

## R13 - A hand-marked fully-allocated target is refused with a sentence naming a cause that did not happen `[x1]`
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:215, :319 (same cause on the subnet row); :217 and the commit twin :508 state only the fact
**Breaks:** The flag arrives either from the import or from the Details toggle, and the annotation asserts the first: after ticking it by hand on rig-rd-fa 10.79.0.0/16, the wizard says an Azure subnet covering the prefix marked it fully allocated with nothing left to add. Neither is true.
**Repro:** Imported the prefix with no subnets, set the flag by POST /HostIp/SetAllocationStatus: that reason came back, Description NULL.
**Fix:** Filed fix unsound — `ExistingSubnetSnapshot` carries no Description, and the note is absent whenever truncated or edited, so inferring provenance prints a fresh false cause. Replace :214-216 with a cause-free sentence naming the remedy (clear the flag on Details), and de-cause :319.
**Residue of:** 23233f2 wrote this wording; the enclosing guard predates it (8afa2df).

## R14 - With the import flag off, the free-space warning still tells every reader to get a Bulk Azure Import run `[x1]`
**Where:** src/Bastet/Views/Subnet/Details/_UnallocatedRanges.cshtml:31; the flag is already computed at :5 and :24
**Breaks:** A deployment imports Azure space then turns BASTET_AZURE_IMPORT off. An Admin opening Details for a still-linked subnet is told to ask an administrator to run a Bulk Azure Import, while that page 403s for everyone — round 17 wrote one else for two suppression reasons.
**Repro:** Flag false rendered the sentence while /Azure/BulkImport 403'd for the Admin identity; flag true rendered the link.
**Fix:** Replace the bare `@else` at :31 with `@else if (azureImportEnabled)` and no trailing else, so the paragraph closes as prose when the feature is off. Do not substitute "ask an administrator to enable the feature" — an env var is not a remedy the app offers.
**Residue of:** f4a0a87 — blame puts the whole if/else pair on that commit.

## R15 - The Azure-linked CIDR refusal names two remedies the operator may be unable to reach `[x1]`
**Where:** src/Bastet/Controllers/SubnetController.Edit.cs:84 (message :81-86); Views/Subnet/Edit/_EditForm.cshtml:27-28 (the same two remedies, printed on the form before any POST); the equivalents at AzureReconciler.cs:289, :311 sit inside a gated screen
**Breaks:** Edit is open to Edit|Delete|Admin, but the refusal says to re-import or to delete and recreate. Re-import is an Admin-only page that 403s whenever the flag is off (which does not clear AzureResourceId); delete is a Delete-role page an Edit-only operator has no button for.
**Repro:** Flag false: 403 for the Admin identity, the CIDR POST rendered that sentence, Details suppressed its own link.
**Fix:** Filed conditional fix unsound — it adds a third copy of the flag/role predicate, in another layer, for a message that links nothing. End both sites at "Change the prefix in Azure, then ask an administrator to re-import it." No test pins the text.
**Residue of:** Round 6 (0de1293) wrote it; round 17 fixed only the _UnallocatedRanges site.

## R16 - Reconcile's prefix-removed message points at a wizard round 17 taught to hide the VNet `[x1]`
**Where:** src/Bastet/Services/Azure/AzureReconciler.cs:288, :309-312 (same remedy, unactionable when the subnet has no IPv4 prefix); Views/Azure/Reconcile/_StepReview.cshtml:38 must not contradict it
**Breaks:** A VNet-linked row whose prefix is gone is told to delete it and re-import the VNet's current address space. When the VNet has lost all IPv4 prefixes — the case round 17 added this path for — BulkGetVNets:83 filters it out, so the wizard never lists it.
**Repro:** rig-rd-loss set to IPv6 only: scan returned VNetPrefixRemoved with that reason; BulkGetVNets no longer listed the VNet.
**Fix:** Branch the closing sentence on `vnet.Ipv4AddressPrefixes.Count` (the live VNet is in scope): keep the wording when IPv4 space remains, otherwise say the VNet has no IPv4 space left and name no wizard. Keep the delete clause. Do not relax the :83 filter.
**Residue of:** f4a0a87 removed the zero-IPv4 skip from `GetVNetInventory`.

## R17 - Reconcile tells the operator to "correct or clear the link on this subnet" - the app has no way to do either `[x1]`
**Where:** src/Bastet/Services/Azure/AzureReconciler.cs:74; Views/Azure/Reconcile/_StepReview.cshtml:72-77 (blurb telling both categories to delete content)
**Breaks:** A row whose AzureResourceId parses but names neither a VNet nor a subnet gets that instruction, and no such control exists: the Edit view model has no AzureResourceId, and the only writer is the import commit, which refuses to re-link and never clears. The blurb is false here too.
**Repro:** An NSG-linked row scanned as UnrecognisedResourceId with both false sentences on screen; over-posting the id left it unchanged.
**Fix:** Filed replacement unsound — delete-and-re-import destroys a record on information the app admits it lacks. End the reason at what is true: BASTET cannot check this row and will not offer it for deletion. Scope the _StepReview sentence to manual content; add no unlink control.
**Residue of:** Round 6 (0de1293) wrote the sentence; the _StepReview half is f4a0a87.

## R18 - "Blocked by VNet prefix" is printed beneath a prefix the same screen calls "Already imported" `[x1]`
**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:278 (guard :276, badge :267-269); _StepSelection.cshtml:10 (legend carrying the same claim)
**Breaks:** `isPrefixUsable` is false for a prefix that is either Blocked or AlreadyImported with renames off, but badge and reason are worded only for the first. A fully-allocated linked row reads "Already imported ... nothing left to add" while four child rows beneath say its prefix cannot be imported.
**Repro:** Marked the imported prefix fully allocated and re-opened the wizard: both sentences rendered as siblings.
**Fix:** Filed fix unsound — reusing `availabilityBadge(prefixInfo.statusName)` would stamp "Already imported" on subnets never imported. Change strings only: :278 to "The VNet prefix above cannot be selected, so this subnet cannot be imported either.", legend to match.
**Residue of:** none.

## R19 - Bulk import commit banner never reads renamedChildSubnets, so a rename-only import reports all zeros `[x2]`
**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:675 (five of six counters); the server returns six at Controllers/SubnetController.BulkAzure.cs:439-444, worded correctly at :427-433
**Breaks:** An operator whose only outstanding work is a drifted child name sees, for the two seconds before the redirect, "Created 0 VNet target(s), 0 child subnet(s), renamed 0 target(s), linked 0 ..., marked 0 ..." — an all-zero summary of a commit that did rename a subnet.
**Repro:** Commit returned renamedChildSubnets 1 and SQL confirmed the rename; the in-page banner read all zeros.
**Fix:** Better than the filed one-liner: return the already-built TempData summary as a `summary` field on the Ok(...) object and set the banner from it, deleting the duplicate sentence. Minimal alternative: add the renamed-child clause at :675.
**Residue of:** f4a0a87, which added the sixth counter and updated one consumer.

## Info

## R20 - Reconcile confirm screen's host-IP cascade count is structurally always 0 `[x1]` — FIXED
_Fixed. Deleted the hostIpCount clause in cascadeNote, the confirm-step hostIps accumulator, and `AzureReconcileItem.HostIpCount` with its assignment; `hostIpsArchived` untouched._
_Swept: no remaining reader of item.hostIpCount in src/ or test/; snapshot.HostIpCount (transitive, feeds the divert) kept._
_Verified: build 0 warnings, 822/822; structural-always-0 confirmed via the :91 divert and transitive snapshot counts. Client-side display — no unit test can reach it; covered by /e2e's wizard pass._
_Reviewed: pass — reviewer confirmed no path puts a nonzero-hostIp item into plan.Items and nothing is stranded._

## R21 - SafeTextAttribute is applied to nothing; two tests pin it as a rule the POST no longer applies `[x1]`
**Where:** src/Bastet/Services/Security/ValidationAttributes.cs:27-43; test/Bastet.Tests/SubnetManagement/SubnetCreateGetPrefillTests.cs:105, :111, :143, :150-154; Azure/GeneratedNameSafeTextTests.cs:56 (same claim)
**Breaks:** Round 17 removed the last three `[SafeText]` usages and nothing discovers ValidationAttribute subclasses by reflection, so the class runs nowhere. Its only references assert the prefilled name is refused by the rule its own POST applies — stricter than the app enforces.
**Repro:** POST /Subnet/Create with Name="Prod/Web: core (site B)" wrote the row; `<b>bold</b>` hit `[NoHtml]`, so attributes do run.
**Fix:** Delete `SafeTextAttribute` (:27-43). In SubnetCreateGetPrefillTests drop the IsSafeText assertions and the `SafeTextServiceProvider` helper, keep the `Assert.Equal` checks, and rename both tests. Fix the same claim at GeneratedNameSafeTextTests.cs:56.
**Residue of:** f4a0a87, which deleted the last usages and orphaned the class.

## R22 - AzureReconcilePlanViewModel.CanCommit is serialised on every scan but no code reads it `[x2]`
**Where:** src/Bastet/Models/ViewModels/AzureReconcileViewModels.cs:95; `BulkImportPlanViewModel.CanCommit` (AzureBulkImportViewModels.cs:208) is used and must stay
**Breaks:** The client gates Go on `selectedIds().length` and Delete on the typed "approved" plus a snapshot, and SubnetController.AzureReconcile.cs re-derives its refusals at :84, :97, :110 without consulting it — while a reader assumes the gate is server-computed.
**Repro:** Scan body carried "canCommit":false; deleting line 95 out of tree built clean, 820/820.
**Fix:** Delete the property — a computed getter with no setter that nothing binds; the only effect is one fewer field in the scan JSON. Do not wire it up for symmetry: reconcile commits a chosen selection, so a plan-wide gate would enable Go with nothing ticked.
**Residue of:** none — born unread at aedd0bd, copied from the bulk-import plan.

## R23 - BuildSubnetTreeViewModel ignores its allSubnets parameter and sets a ParentSubnetId no view renders `[x1]`
**Where:** src/Bastet/Controllers/SubnetController.Helpers.cs:258, :269, :279; Controllers/SubnetController.Read.cs:25 (only caller); Models/ViewModels/SubnetViewModels.cs:65
**Breaks:** The body walks `subnet.ChildSubnets` and only forwards `allSubnets` to itself, so the caller materialises a whole-table list to satisfy a signature. `SubnetTreeViewModel.ParentSubnetId` is read by no view and no test.
**Repro:** Both removals on a clean export: 0 warnings, 820/820, byte-identical /Subnet HTML in an A/B.
**Fix:** Drop the parameter, its forward at :279 and the argument at Read.cs:25 (keep the local, used at :20); delete the assignment at :269 and the property at SubnetViewModels.cs:65. Note that the whole-table load at Read.cs:16-18 is load-bearing: EF fixup populates ChildSubnets below level 2.
**Residue of:** none.

## Refuted - reported by a finder, killed by the verifier

| title | where | tag | reason |
| --- | --- | --- | --- |
| Free-space warning names a Bulk Azure Import remedy on Azure-subnet-linked rows, where the wizard can never add anything | Views/Subnet/Details/_UnallocatedRanges.cshtml:27 | x2 | Load-bearing claim false, and the verifier made the wizard do it: the cited rules (`FindMoreSpecificParent`:260, `DetectExistingBastetSubnetConflicts`:718) only refuse a row between a VNet target and that VNet's own subnets. A new Azure VNet 10.30.2.0/25 inside an Azure-subnet-linked row planned AutoCreateChild under it, committed, and shrank that row's free range 256 -> 128. The finder read a transient "nothing left to import" state as structural. |
| Migration sp_getapplock lands in a different database when the catalog does not exist yet, so cold-starting replicas do not exclude each other | src/Bastet/Program.cs:310 | x1 | Mechanism reproduces (:235-308 falls back to `master` on SQL 4060) but the consequence does not: EF Core 10.0.11 takes its own `__EFMigrationsLock` on the configured connection before reading migration history, so replicas serialise there regardless. Verified an instance passing its own lock then waiting in X on that lock with no tables created, finishing cleanly. Attribution also wrong — 73f68ac. |
