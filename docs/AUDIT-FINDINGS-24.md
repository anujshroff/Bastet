# Bastet — Round-24 Audit Findings

Branch: audit/round-24
HEAD: 338e8e0
Test baseline: 881
Date: 2026-08-18

Round 24 filed 19 findings, of which 7 are residue of previous rounds' own fixes.

# Critical

# High

# Medium

# Low

## L1 — Tags accepted by validation are silently rewritten by SanitizeTags before persisting `[x1]`
**Where:** src/Bastet/Services/Security/InputSanitizationService.cs:119 (StripHtml call; statement starts :118); via [SanitizeTags] on src/Bastet/Models/ViewModels/SubnetViewModels.cs:37 and src/Bastet/Models/ViewModels/EditSubnetViewModel.cs:37; strip pinned at test/Bastet.Tests/Security/InputSanitizationServiceTests.cs:103
**Breaks:** Tags="rack<b12>,web" passes validation (18-R7 left TagsAttribute count/length only), but SanitizeTags → StripHtml stores "rack,web" — "<b12>" silently deleted, on Create and Edit. Validation and sanitization disagree; Name/Description avoid this via [NoHtml]'s up-front refusal.
**Repro:** Verified live: POST /Subnet/Create Tags="rack<b12>,web" → 302, no error, stored "rack,web"; Edit Tags="db<x9>,prod" → stored "db,prod". f2050fa (18-R7) removed the refusal; strip pinned by the "<script>evil</script>,goodtag" InlineData.
**Fix:** Delete the StripHtml call in SanitizeTags (keep trim, splitting, join, and the count/length caps), so sanitizer and the 18-R7 validation rule accept identical tag text; re-pin the InlineData to expect the text preserved. Razor encodes tags at every sink, so display safety is unaffected (PRODUCT-MODEL §5).
**Residue of:** 18-R7

## L2 — Subnet Create form carries its own client-side IP arithmetic (mask/total/usable) duplicating IpUtilityService `[x2]`
**Where:** src/Bastet/Views/Subnet/Create/_SubnetFormScripts.cshtml:3-31 (the Details engine is parked as 23-L2-remainder, not re-filed)
**Breaks:** calculateSubnetMask/calculateTotalIPs/calculateUsableIPs (incl. /31→2, /32→1) re-implement the IpUtilityService equivalents for the live mask/total/usable display on /Subnet/Create — a third client IP engine, violating PRODUCT-MODEL §5; drift makes Create contradict the pinned authority.
**Repro:** Verified live (Playwright): Cidr 26/31/32/0 all rendered mask/total/usable matching IpUtilityService semantics with zero network requests during typing; the JS mirrors IpUtilityService.cs:9-25/71-73/75-86 line for line. Original feature code (c103bdf #34).
**Fix:** Delete the three client functions and have updateSubnetInfo index a server-computed table: serialize the 33 per-CIDR {mask, total, usable} triples (via IpUtilityService) into the Create view as JSON, so the client only looks up, never computes.
**Residue of:** none

## L3 — Held prefix-changed reconcile rows tell the operator to delete a row the same sentence block says BASTET will not delete `[x2]` `strings`
**Where:** src/Bastet/Services/Azure/AzureReconciler.cs:95 (assembly site 93-99); :293-300 (VNetPrefixRemoved reason); :320-327 (SubnetPrefixChanged reason — verifier-corrected from :330)
**Breaks:** A row linked to a live VNet/subnet whose prefix changed, holding manual content, is HeldByManualContent; its lead is item.Reason, so the operator reads "...Delete it here if you want to... BASTET will not delete it... Delete it here first, then run the scan again." — a remedy this checkbox-less review row is withheld from, contradicted in the next sentence (rule 3; same-screen contradiction). Same for SubnetPrefixChanged.
**Repro:** Verified live: rows linked to live rig-vnet-multi (re-ranged, manual child) and rig-sub-app (changed prefix, 2 host IPs); ReconcileScan → both held with the contradictory reasons; _ReconcileScripts.cshtml:247 renders item.reason verbatim. 23-L4 gave absence statuses a factual lead but kept the remedy-bearing Reason for prefix-changed.
**Fix:** Split the Evaluate* reasons into fact ("VNet 'x' still exists but no longer has the address prefix P." / "The Azure subnet still exists but its address prefix is now X, not P.") and remedy sentences; the held branch uses only the fact — the existing tail already states the true remedy. Offered items keep fact+remedy unchanged.
**Residue of:** 23-L4

## L4 — Valid credential with zero visible subscriptions reported as "Failed to authenticate with Azure" `[x1]` `strings`
**Where:** src/Bastet/Services/Azure/AzureService.cs:29-35 (root cause; post-loop return false at :35); src/Bastet/Controllers/AzureController.cs:47 (BulkImport); src/Bastet/Controllers/AzureController.cs:137 (Reconcile)
**Breaks:** A principal that authenticates but sees no subscriptions: IsCredentialValid returns false on the empty enumeration — same value as a failed login — so both pages render "Failed to authenticate with Azure. Please check your credentials." beside "No subscriptions found." (_StepSubscription.cshtml:36). Same-screen contradiction; wrong remedy (real one: a role assignment). Confidence: plausible — unestablished: that ARM returns an empty 200 enumeration rather than throwing.
**Repro:** Not-runnable (a zero-subscription principal cannot be constructed on the shared rig). Static: :35 is `return false;` after the empty await foreach; ModelState strings at :47/:137, rendered at both pages' line 14. Both identifiers from 9f220e0 (#32).
**Fix:** Have IsCredentialValid (or a small result enum) separate "could not authenticate" from "authenticated, zero subscriptions visible", and word the second branch truthfully on both pages (e.g. "Signed in to Azure, but this credential can see no subscriptions. Grant it access to a subscription and reload."), keeping the existing message for real auth failures.
**Residue of:** none

## L5 — Bulk import commit banner prefixes the indeterminate-outcome message with a false "Commit failed:" `[x1]` `strings`
**Where:** src/Bastet/Views/Azure/BulkImport/_StepCommit.cshtml:17
**Breaks:** A severed-transaction commit (SubnetController.BulkAzure.cs:450-454) answers 500 with "BASTET could not confirm whether this import was applied. Reload the subnet list to see its current state before retrying."; showCommitError (_BulkScripts.cshtml:663-686) drops it after the static <strong>Commit failed:</strong> — one banner asserting a definite failure and an unknown outcome; an operator trusting the headline retries without reloading. The other indeterminate renders (_StepConfirm.cshtml:28-32, SubnetController.Delete.cs:168-171) are bare.
**Repro:** Verified live (Playwright): routed the commit POST to fulfill 500 with the byte-exact JSON of the catch at :454 → banner: "Commit failed: BASTET could not confirm whether this import was applied. ..." Markup original (73fc76f); message from d18327e (round 16).
**Fix:** Delete the static "Commit failed:" strong from _StepCommit.cshtml:17 so the banner shows the server's message alone. Genuine-failure payloads carry self-sufficient sentences, so nothing loses meaning.
**Residue of:** none

## L6 — Reconcile review-section note claims 'The results below have been re-scanned' on the very first scan, set by JS as a constant `[x2]` `strings`
**Where:** src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:259; src/Bastet/Views/Azure/Reconcile/_StepReview.cshtml:76
**Breaks:** On the first-ever scan the Needs-review explainer ends '...then scan again. The results below have been re-scanned, so they reflect Azure as it is now.' — asserting a re-scan that never happened. renderPlan writes this constant on every successful scan; since 23-I3 deleted the only other branch it distinguishes nothing.
**Repro:** Verified live (fresh catalog, no scan ever run; one UnrecognisedResourceId row to show the review section): first scan ever → #rec-rescan-note shows the false sentence; the only remaining assignment is the constant in renderPlan. Born 65d1fc6 (round 15), relocated by 23233f2, single-branched by 23-I3.
**Fix:** Delete the $('#rec-rescan-note').text(...) assignment and the empty <span id="rec-rescan-note"> in _StepReview.cshtml:76, folding a tense-neutral sentence into the static explainer prose (e.g. 'The results below reflect Azure as of the latest scan.').
**Residue of:** none

## L7 — Bulk import wizard loadVNets has no supersession guard: a stale VNet response repaints the current tree `[x1]`
**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:104
**Breaks:** Select subscription A (slow listing), back to step 1, select B: B renders, then A's late response silently overwrites the tree and wipes ticks while selectedSubscriptionId/Name remain B — the operator imports A's resource ids under B's identity. loadVNets' handlers run unconditionally, unlike loadPreview (previewSeq) and runScan (scanSeq); the only re-triggerable AJAX in either wizard without the guard.
**Repro:** Verified live (Playwright): held the first BulkGetVNets response, re-selected, ticked a prefix, fulfilled the held response with a marker payload → tree became "STALE-MARKER-VNET 10.250.0.0/24", ticks 1→0. Two-subscription case not driven (rig SP sees one subscription) but the overwrite is proven. loadVNets from 73fc76f (#108).
**Fix:** Add 'let vnetSeq = 0;', capture 'const seq = ++vnetSeq;' in loadVNets, and return early from success/error/complete when 'seq !== vnetSeq' — identical shape to loadPreview's previewSeq guard.
**Residue of:** none

## L8 — 23-L1's reworded noLongerStale 409 headline is pinned by no test — reverting it to the refuted false wording keeps the suite green `[x2]`
**Where:** src/Bastet/Controllers/SubnetController.AzureReconcile.cs:102
**Breaks:** Reverting the headline to the pre-23-L1 claim "no longer reported as deleted in Azure" (false for NotVisible/Unknown/cascade-withheld rows) turns nothing red: the noLongerStale conflict test (SubnetControllerAzureReconcileTests.cs:125) asserts only ConflictObjectResult and row survival; no test references either phrase.
**Repro:** Verified: reverted :102-103 to the exact pre-23-L1 wording → dotnet test 881/881 green. The sibling verdictChanged and held 409s ARE message-pinned; noLongerStale is the sole unpinned headline.
**Fix:** Extend the existing noLongerStale conflict test to assert Contains "no longer offered for deletion by the latest re-check" and DoesNotContain "no longer reported as deleted in Azure", mirroring the 21-L1/22-L2 pins.
**Residue of:** 23-L1

## L9 — 23-L4's held-row wording fix is unpinned on its subnet-level branch: the 'no longer exists' falsehood can reland with the suite green `[x2]`
**Where:** src/Bastet/Services/Azure/AzureReconciler.cs:222
**Breaks:** A subnet-linked held row absent from the listing must lead with the 23-L4 'could not be found in this subscription's listing' clause. Only the VNetDeleted branch of AbsentFromListingClause is pinned; the SubnetDeleted else-branch at :222 has no test — reverting it to the pre-fix falsehood 'The Azure subnet this was imported from no longer exists.' stays green, re-asserting a live RBAC-hidden subnet is gone on half the fix's domain.
**Repro:** Verified: replaced the :222 string with the pre-fix falsehood (single occurrence) → dotnet test 881/881 passed; the one wording assertion (AzureReconcilerTests.cs:353) uses VNetId("vnet-a").
**Fix:** Test-only: add a reconciler test mirroring AHeldRowWhoseVNetIsMerelyAbsentFromTheListing_DoesNotAssertItNoLongerExists but with a subnet-level resource id (SubnetId(...)) and manual content, asserting the held Reason contains 'could not be found in this subscription's listing' and not 'no longer exists'.
**Residue of:** 23-L4

## L10 — AzureResourceIdentity.IdComparer unpinned: Ordinal mutant survives 881/881 and would make reconcile offer deletion of a live, case-differently-linked row `[x1]`
**Where:** src/Bastet/Services/Azure/AzureResourceIdentity.cs:11; src/Bastet/Services/Azure/AzureReconciler.cs:38 and :39 (the consequential join); src/Bastet/Services/Azure/AzureService.cs:196,200,207,226 (same symbol, inert today)
**Breaks:** 23-L6's ledger claims the 22-L1 casing tests pin every caller, but they exercise only IsSameResourceId; IdComparer → StringComparer.Ordinal passes the entire suite. IdComparer backs the reconciler's inventory join: under the mutant, a row whose stored id differs from ARM's casing is reported VNetDeleted/SubnetDeleted — reconcile offers to delete an allocation whose resource is live.
**Repro:** Verified: IdComparer mutant → 881/881 green; a consequence probe (live subnet in inventory, snapshot linked with SubnetId(...).ToUpperInvariant()) fails Assert.Empty under the mutant and passes restored.
**Fix:** Test-only: add reconciler casing tests mirroring the two 22-L1 planner tests — a subnet-linked row and a VNet-linked target row whose AzureResourceId casing differs from the inventory's must produce an empty plan — reddening the IdComparer→Ordinal mutant.
**Residue of:** 23-L6

# Info

## I1 — 23-L6 left two inline resource-id equality implementations in the planner instead of routing them through AzureResourceIdentity.IsSameResourceId `[x2]`
**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:197 (AnnotatePrefix, context 195-197); src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:569 (BuildPlan, context 567-569)
**Breaks:** Two link-replacement refusal checks still spell resource-id equality inline (null-guards plus string.Equals OrdinalIgnoreCase) against 23-L6's one-implementation contract. No wrong output today; the defect is the surviving duplication — the next comparison-mode change to the helper silently strands these two.
**Repro:** Verified (scratch clone): IsSameResourceId→Ordinal → 880/881 while the :569 pin ATargetLinkedWithDifferentIdCasing_IsNotRefusedAsALinkReplacement PASSED (sites stranded); filed fix verbatim → 25/881 failed; corrected fix → 881/881, and re-mutating the helper then reddens both site casing tests. Sites date to ff285cf (round 7); BulkAzure.cs:258 is Ordinal by design.
**Fix:** verifier judged the filed fix unsound: Keep both explicit IsNullOrEmpty guards at each site and replace only the equality term: at AzureBulkImportPlanner.cs:197 change "!string.Equals(exact.AzureResourceId, vnet.ResourceId, StringComparison.OrdinalIgnoreCase)" to "!AzureResourceIdentity.IsSameResourceId(exact.AzureResourceId, vnet.ResourceId)", and at :569 change "!string.Equals(exact.AzureResourceId, p.Source.VNetResourceId, StringComparison.OrdinalIgnoreCase)" to "!AzureResourceIdentity.IsSameResourceId(exact.AzureResourceId, p.Source.VNetResourceId)". The guards must NOT fold into the helper: the compare is negated, so !IsSameResourceId is true when either id is empty, which would refuse every unlinked exact-match target as a link replacement (18-R4 adoption) — applying the filed fix verbatim reddens 25 tests. With guards kept the semantics are bit-identical today (verified 881/881 green) and a future comparison-mode change to the helper propagates to both sites (verified: helper mutation reddens both site-pinning casing tests after the corrected fix).
**Residue of:** 23-L6

## I2 — Locking-service registration dispatches on a provider that is constant; sqlite and default arms are dead dispatch `[x1]`
**Where:** src/Bastet/Program.cs:115 (the switch inside the factory at :109-121)
**Breaks:** BastetDbContext is registered with UseSqlServer only (Program.cs:50,55), so ProviderName is constant; the sqlite and "_" arms can never be selected and no test exercises Program.cs DI. Drift would select the wrong lock service unnoticed; §5 duplicated-decision class, closed by deletion.
**Repro:** Verified: static grep as cited; live, a foreign session holding sp_getapplock 'Bastet:SubnetOperations' made POST /Subnet/Create stall exactly 30.055s and fail closed — behavior only SqlServerSubnetLockingService produces.
**Fix:** Replace the switch with a direct `new SqlServerSubnetLockingService(context, lockLogger)` registration (or a plain AddScoped), keeping SqliteSubnetLockingService as the test double it already is.
**Residue of:** none

## I3 — Inert client-JS scaffolding: empty handlers and vacuous re-assignments in three views `[x2]`
**Where:** src/Bastet/Views/HostIp/Create/_FormScripts.cshtml:6-9; src/Bastet/Views/Subnet/Index.cshtml:52-55; src/Bastet/Views/Subnet/Create/_SubnetFormScripts.cshtml:55-58
**Breaks:** (1) HostIp Create registers a submit listener with an empty body — dead since 841c272 (#18). (2) Subnet Index declares @section Scripts containing an empty <script></script> — round 16's comment ban left the husk. (3) Subnet Create initializeForm re-assigns values the markup carries verbatim (max='32' dup of _SubnetForm.cshtml:23, 'CIDR values: 0-32' dup of :25, networkAddressHelp dup of :17); the placeholder assignment on line 59 is NOT vacuous and stays.
**Repro:** Not-runnable (code with no effect). Confirmed by reading the views; git show 841c272 (listener comments-only from birth), d18327e (removed exactly the placeholder comment), c103bdf (introduced the assignments).
**Fix:** Delete the empty submit listener (and its form lookup), the empty @section Scripts block, and the three vacuous assignments in initializeForm (keep the line-59 placeholder assignment, or move placeholder='192.168.1.0' into the markup and delete it too).
**Residue of:** none

## I4 — Write-only subscription/rename echo properties on both plan view models, plus the parameter and payload chain that exists only to feed them `[x2]` — FIXED
_Fixed in round 24. Deleted the five write-only plan properties and their stamps, BulkImportSelectionDto.SubscriptionId/.SubscriptionName, the subscriptionName parameter from IAzureReconciler/AzureReconciler/AzureController.ReconcileScan (and the null arg in BulkDeleteStaleAzureSubnets), both clients' selectedSubscriptionName variables and payload fields; kept the DTO RenameMatchedBastetSubnets (read by BuildPlan), the bulk client's selectedSubscriptionId, the reconcile POST's subscriptionId, and AzureReconcileDeleteDto.SubscriptionId._
_Swept: reviewer-caught orphan — the subscription dropdown options' data-name stamps (both wizards) whose only reader was the deleted variable — also deleted; the live subnet-checkbox data-name pair kept; test builders updated (two AzureReconcileDeleteDto initializers were over-stripped by the sweep and restored)._
_Verified: build 0/0, 881/881; wire payloads captured live — selection POST carries exactly vNetPrefixes+renameMatchedBastetSubnets, scan POST exactly subscriptionId._
_Reviewed: independent reviewer PASS — zero surviving reads in any casing, kept members' readers intact, full live drive of both wizards (discovery→preview→commit created a row; scan succeeded)._
**Residue of:** none

## I5 — ValidateParentCanHaveChildSubnets ignores its parentId parameter entirely, and its 'hostIps = null' default has no caller `[x2]`
**Where:** src/Bastet/Services/Validation/SubnetValidationService.cs:270; src/Bastet/Services/Validation/ISubnetValidationService.cs:24; src/Bastet/Controllers/SubnetController.Helpers.cs:131
**Breaks:** The method never reads parentId — the body is purely 'if (hostIps != null && hostIps.Any()) add PARENT_HAS_HOST_IPS error'. Its single caller passes parentSubnet.Id into the ignored slot and always supplies hostIps; zero test callers, so the '= null' default is equally dead (IDE0060 exempts interface implementations).
**Repro:** Not-runnable (dead parameter surface). Body reads only hostIps; grep → 3 sites total; introduced in 841c272 with parentId already unread.
**Fix:** Change the signature (interface and implementation) to ValidateParentCanHaveChildSubnets(IEnumerable<HostIpAssignment> hostIps) and update the single caller; the 'hostIps != null &&' clause collapses with the non-nullable parameter.
**Residue of:** none

## I6 — Dead data-ip-version attribute on the Create Subnet button in the unallocated-ranges table — no reader has ever existed `[x1]`
**Where:** src/Bastet/Views/Subnet/Details/_UnallocatedRanges.cshtml:66
**Breaks:** The Create Subnet button in each unallocated-range row carries data-ip-version="4"; the click handler (_SubnetCalculationScripts.cshtml:22-25) reads network/parent-id/parent-cidr — never ip-version — and nothing else references the attribute. Not part of the 23-L2-remainder deferral — its reader never existed anywhere.
**Repro:** Not-runnable (proven statically). grep -rniE 'ip-version|ipversion' src/ test/ → exactly one hit, the writer at :66; no wildcard .data()/dataset consumers; git history: only ac45ef3 and 46b3e69 (#17), no reader even at introduction.
**Fix:** Delete the data-ip-version="4" line from the button markup at src/Bastet/Views/Subnet/Details/_UnallocatedRanges.cshtml:66.
**Residue of:** none

## I7 — ExecuteWithSubnetLockAsync's optional timeout parameter is never passed by any caller in the entire solution `[x1]`
**Where:** src/Bastet/Services/Locking/ISubnetLockingService.cs:6; src/Bastet/Services/Locking/SqlServerSubnetLockingService.cs:32; src/Bastet/Services/Locking/SqliteSubnetLockingService.cs:9; test/Bastet.Tests/TestHelpers/ControllerTestHelper.cs:42; test/Bastet.Tests/SubnetManagement/SubnetLockTimeoutTests.cs:18
**Breaks:** 'TimeSpan? timeout = null' is declared but every call site — 9 production controller sites and every test call — passes only the operation delegate. Both implementations carry '(int)(timeout?.TotalMilliseconds ?? DEFAULT_TIMEOUT_MS)' unwrap math for a value that is always null; both test doubles echo the dead signature.
**Repro:** Not-runnable. grep → 14 rows, all call sites single-argument (SubnetController Create.cs:82/Edit.cs:69/Delete.cs:107/BulkAzure.cs:37/AzureReconcile.cs:129, HostIpController.cs:106/215/353/657). Applied the proposed fix in a scratch clone → build clean, tests 881/881. Introduced 9c243fc (#41), pre-round-4.
**Fix:** Remove the timeout parameter from the interface, both implementations (keep DEFAULT_TIMEOUT_MS as the internal constant, used directly) and the two test doubles. No call site changes.
**Residue of:** none

## I8 — Unread 'prefix' local in the round-21 other-VNet-linked zero-work test drops the prefix-status assertion its sibling makes `[x1]`
**Where:** test/Bastet.Tests/Azure/AzureBulkImportZeroWorkTests.cs:141
**Breaks:** AWholePrefixSubnetOverAFullyAllocatedRowLinkedToAnotherVNet_IsNotBadgedAlreadyImported assigns 'prefix = Annotate(...)' and never reads it — the only IDE0051/52/59/60 hit in the solution. The sibling test (:124) asserts prefix.Status; this one asserts only the child row, so the 21-L1 prefix-level behavior (Blocked, not AlreadyImported) is unpinned by the very test covering it.
**Repro:** Verified: blame → all lines 85b1819c (the 21-L1 fix commit); adding the two prefix assertions in a scratch copy → filtered dotnet test 1/1 Passed.
**Fix:** Prefer asserting over deleting: add Assert.Equal(BulkImportAvailability.Blocked, prefix.Status) and Assert.False(prefix.IsSelectable) so the local is read and the 21-L1 prefix half is pinned (verified green).
**Residue of:** 21-L1

## I9 — NetworkInputAttribute's non-RequireValidIp branch and its 'false' default are production-dead — every application passes RequireValidIp = true `[x1]`
**Where:** src/Bastet/Services/Security/ValidationAttributes.cs:29,51-59; src/Bastet/Models/ViewModels/HostIpViewModels.cs:18; src/Bastet/Models/ViewModels/SubnetViewModels.cs:16
**Breaks:** The attribute declares 'RequireValidIp = false' plus an else branch validating input against SanitizeNetworkInput's output. Both applications pass RequireValidIp = true and no test instantiates the attribute, so the default and the else branch (:51-59, incl. the 'Input contains invalid characters for network input' message) are unreachable. Orphaned when e774d4f (#138) flipped the subnet application to true; live at introduction (3940c98 #43).
**Repro:** Not-runnable (unreachable by construction). grep → exactly 2 applications, both RequireValidIp = true; zero test references; git log -S → only 3940c98 and e774d4f.
**Fix:** Delete the RequireValidIp property and the else branch so the attribute always validates as an IP address, and drop the now-redundant 'RequireValidIp = true' from the two applications.
**Residue of:** none

# Refuted

| title | beat | reason |
|---|---|---|

(none)
