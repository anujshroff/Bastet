# Deferred findings — the planner/wizard restructure work queue

Deferred from round 18 (owner-approved triage, 2026-08-15). Each is real, reproduced, and deliberately
not fixed narrowly: every one is an instance of duplicated implementations in
`AzureBulkImportPlanner.cs` / `_BulkScripts.cshtml` that the restructure exists to collapse. The
restructure runs as its own reviewed effort, one duplication per PR, tests landing with each slice.
Close each item by deleting an implementation, not by patching both.

Slices, from the round-18 evidence: **naming** (18-R6, 18-R11), **server-computed selectability
driving the client** (18-R10, 18-R4, 18-R5), **redisplay path** (18-R3 remainder), **commit summary**
(18-R19 remainder). Line numbers are as of round-18 HEAD f6bd1ec and will have drifted.

## 18-R4 - A hand-built fully-allocated row cannot be adopted by the VNet it exactly matches (Medium)
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:503, :217 (annotation half, moves with it), :314-328
**Breaks:** The operator has already built the row the import would produce for 10.20.0.0/24 ('dmz', fully allocated, unlinked) and the wizard refuses it. Adding only the AzureResourceId flips it to AlreadyImported: the refusal keys on provenance and names no remedy.
**Repro:** Unlinked row: Blocked, preview errored, canCommit false; the link-only UPDATE flipped it.
**Fix as filed (annotation half judged unsound):** At :503 use `exact.IsFullyAllocated && p.Subnets.Any(s => !s.FullyEncompasses && LinkedRowForSameAzureSubnet(s, existingSubnets) is null)`; at :211-217 fall through only when nothing would be created. Close structurally with the selectability slice.

## 18-R5 - A linked VNet target with host IPs cannot be renamed, while the identical fully-allocated case can (Medium)
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:207, :498-502; sibling at :211-218, :503-509
**Breaks:** Import rig-empty, rename the row, assign one host IP: the planner computes wouldRenameTarget=true, discards it and returns Blocked "already has host IP assignments", disabling the box under a "Cannot import" badge. The only remedy offered is deleting the host IP data.
**Repro:** Rename-only preview: willRename=True, newName='rig-empty', that error, canCommit false; the fully-allocated control committed renamedTargets 1.
**Fix as filed (annotation change judged unsound):** Take :498 as filed; at :207 mirror the sibling — `isTopUp ? AlreadyImported(..."so no subnets can be added inside it.") : Blocked(...)`. Close structurally with the selectability slice.

## 18-R6 - Child-subnet rename offer uses a second naming implementation, so the wizard offers renames it never performs (Low)
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:398, :383-384, :615-626 (real base name), :630, :636-652; Views/Azure/BulkImport/_BulkScripts.cshtml:149, :176, :238, :272
**Breaks:** A multi-prefix Azure subnet imports as 'snet-mp (10.81.1.0-24)' and 'snet-mp (10.81.2.0-24)' and both return wouldRenameSubnet=true, because the annotation compares the unqualified name. They render "Rename only" with enabled boxes, but ticking them previews no children and commits renamedChildSubnets 0.
**Repro:** Straight after the import all three created rows returned wouldRenameSubnet=True; preview dropped two, commit renamed 0.
**Fix:** Not implementable as filed (the plan qualifies from selection-derived `multiPrefixResourceIds`; the subnet DTO carries no `Ipv4AddressPrefixes`). Delete `ProposedChildName`, derive the offer from the plan's base-name computation, and add `Ipv4AddressPrefixes` to the DTO. The naming slice.

## 18-R9 - Toggling "Rename matched Bastet subnets to VNet names" discards the operator's entire selection (Low)
**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:358; the complete implementation is at :374-392 in the same file
**Breaks:** `#bulk-hide-imported` snapshots the checked keys around `renderVNetTree()` and restores them; `#bulk-rename-matched` re-renders with no capture, and the render empties the tree first. Every tick is lost, the only signal being "Next: Preview" going disabled.
**Repro:** Select all -> 31 checked; hide-imported on/off preserved 31; the rename toggle left 0 checked with 31 still enabled.
**Fix:** Extract the capture/restore at :375-389 into `rerenderPreservingSelection()` (snapshot, re-render, re-apply to `:not(:disabled)` boxes, `updateGoPreviewBtn()`) and call it from both handlers. Keep the snapshot key. First pull-forward candidate if the restructure slips — a Playwright repro exists to carry into the E2E suite.

## 18-R10 - isPrefixUsable re-derives IsSelectable from a status name, so renames-on enables a checkbox the server refuses (Low)
**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:169, consumed at :179-188, :249-250, :259; indistinguishable branches at Services/Azure/AzureBulkImportPlanner.cs:213-216 and :222-227
**Breaks:** `AlreadyImported` is produced both when the target is fully allocated and when every contained subnet is recorded, and only the second allows ticking a child. With 10.79.0.0/16 AlreadyImported and snet-fa Available, the rename switch alone enables snet-fa, which the preview refuses.
**Repro:** Renames off -> disabled with blocked badge; on -> enabled; previewing returned "... is marked as fully allocated". The round-18 strings-batch reviewer independently re-observed this quirk.
**Fix:** Both filed fixes judged unsound. Fix server-side: in `AnnotateSubnet` (~:330-372) mark a subnet not selectable when its exact-match container is fully allocated or holds host IPs — the headline of the selectability slice: the client consumes IsSelectable and derives nothing.

## 18-R11 - `TargetName` is a second implementation of `ProposedTargetName` that reads the client-supplied prefix list (Low)
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:840 (called at :523, :541, :557); paired implementation at :417, used by the annotation at :205
**Breaks:** `ProposedTargetName` takes the qualifier decision from the ARM-read prefix list; `TargetName` takes it from `prefix.Source.VNetIpv4AddressPrefixes`, posted by the browser and validated nowhere. Omitting that array returns two plan items both named "rig-dual-prefix", canCommit true. UI-unreachable today.
**Repro:** Same preview three ways: array present -> qualified names; omitted or one self-consistent element -> both unqualified.
**Fix:** Filed guard unsound — a one-element list satisfies it and still produces the wrong output, and it keeps both copies. Collapse `TargetName` and `ProposedTargetName` into one method taking (vnetName, prefixes, network, cidr). Sourcing prefixes from ARM is separate. The naming slice.

## 18-R3 (remainder) - Collapse the duplicated Edit concurrency redisplay paths (Medium finding; narrow half fixed in round 18)
**Where:** src/Bastet/Controllers/SubnetController.Edit.cs — the DbUpdateConcurrencyException catch and the common redisplay tail are two implementations of one redisplay decision; Controllers/HostIpController.cs concurrency branch is the sibling.
**Done in round 18:** the token refresh is gone at all three sites (a blind retry fails closed); messages rewritten true.
**Remaining:** delete the catch-site redisplay (the tail redoes it), and make the one remaining message name the differing fields and stored values so the operator can merge instead of reloading blind. The redisplay slice.

## 18-R19 (remainder) - Return the server-built commit summary instead of a client-assembled sentence (Low finding; minimal clause shipped in round 18)
**Where:** src/Bastet/Controllers/SubnetController.BulkAzure.cs (~:427-444) builds the TempData summary and separately returns six counters; Views/Azure/BulkImport/_BulkScripts.cshtml assembles its own sentence from them.
**Remaining:** return the already-built summary as a `summary` field on the Ok(...) object and set the banner from it, deleting the duplicate sentence. The commit-summary slice.
