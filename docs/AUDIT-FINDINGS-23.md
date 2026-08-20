# Bastet — Round-23 Audit Findings

Branch: `audit/round-23`
HEAD: `adebf6f`
Test baseline: 878
Date: 2026-08-17

Round 23 filed 17 findings, of which 2 are residue of previous rounds' own fixes.

# Critical

# High

# Medium

## M1 — All client CSS/JS loads only from cdn.jsdelivr.net; air-gapped deployments lose the client layer `[x2]`
**Where:** src/Bastet/Views/Shared/_Layout.cshtml:101; also _Layout.cshtml:10,12,103; src/Bastet/Views/Shared/_ValidationScriptsPartial.cshtml:6,8
**Breaks:** PRODUCT-MODEL §6 requires air-gapped deployments to keep working. Without reach to cdn.jsdelivr.net, jQuery/Bootstrap/bootstrap-icons/jquery-validation all fail: every page throws '$ is not defined'; navbar dropdowns never open (Deleted Subnets, All Host IPs, All Deleted Host IPs, My Roles, Logout unreachable via UI); Details Create modal, tree expand/collapse, tooltips dead; both Azure wizards hang; app unstyled. CRUD forms still submit.
**Repro:** Verifier: Playwright aborting cdn.jsdelivr.net vs unblocked control. Blocked: '$ is not defined' pageerrors, dropdown never gets .show, unstyled navbar, Details modal never shows, /Azure/BulkImport frozen. Control: all work. wwwroot holds only css/site.css and js/site.js — no local fallback. git log -S 'cdn.jsdelivr' -> e05c3b1 (#11), 73fc76f (#108), pre-audit.
**Fix:** Vendor the five libraries (bootstrap 5.3.8 css+bundle js, bootstrap-icons 1.13.1 with font files, jquery 4.0.0, jquery-validation 1.21.0, jquery-validation-unobtrusive 4.0.0) into wwwroot/lib and point the six tags at the local copies (stock ASP.NET Core template layout). Cheaper interim: keep CDN tags with local fallbacks (window.jQuery || document.write, link-onerror for CSS).
**Residue of:** none

# Low

## L1 — Reconcile delete refusal falsely claims withheld rows are 'no longer reported as deleted in Azure' `[x1]` `strings` — FIXED
_Fixed in round 23. Reworded the noLongerStale 409 headline to "N of the selected subnet(s) are no longer offered for deletion by the latest re-check, so nothing was deleted. Re-run the scan and review the results and any warnings shown." — definitionally true for every bucket (not in plan.Items = not offered), where the old "no longer reported as deleted in Azure" was false for the NotVisible/Unknown/cascade-withhold cases._
_Swept: all five paths that reach noLongerStale (Live/NotVisible/Unknown/cascade/never-in-plan); held rows return earlier and never surface here; no test pinned the string._
_Verified: build 0/0, 880/880._
_Reviewed: independent reviewer PASS — traced every path, confirmed the new wording is true in all and the old false in at least three, remedy reachable._
**Residue of:** none

## L2 — Client-side second implementations of CIDR containment and IP-integer arithmetic `[x1]`
**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:399; also _BulkScripts.cshtml:196, :387-413; src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml:174-287
**Breaks:** Violates §5 (IpUtilityService is the only IP arithmetic; the fix is deletion) and §4 (client must not re-derive planner decisions; 18-R10 precedent). Wizard JS ipToInt/prefixContainsCidr (:387-413, used at :196) decides which VNet prefix each Azure subnet renders and posts under — same question as planner SubnetsWithinPrefix (:455-460) and BuildPlan containment (:114-122). Details carries a free-standing engine (masks, boundaries, overlap, optimal-CIDR search) duplicating IsSubnetContainedInParent/CalculateUnallocatedRanges. Both agree today; drift would post subnets under the wrong prefix (whole-commit refusal, unactionable errors) or recommend a network/CIDR the Create POST rejects.
**Repro:** Verifier ran it live (Playwright): BulkGetVNets JSON has flat subnets[] with no subnet-to-prefix mapping, yet the DOM grouped subnets under the right prefixes with data-prefix stamped — computed only client-side. Details on 10.99.0.0/16: modal recommended /17; debug trace showed the client engine computing boundaries and overlap live. git log -S: prefixContainsCidr <- 73fc76f (#108), Details engine <- 46b3e69 (#17) — original feature code.
**Fix:** Wizard: during AnnotateAvailability stamp each inventory subnet row with the address prefix it belongs to (AnnotateSubnet already computes containment), have the client group by that served key, and delete ipToInt/prefixContainsCidr. The Details modal has no server counterpart — replacing its engine with a server-computed prefill would be a restructure (new endpoint or precomputed per-range suggestions in the view model), flagged separately rather than smuggled into the narrow fix.
**Residue of:** none

## L3 — Reconcile empty-listing warning contradicts the withheld result on the same screen `[x1]` — FIXED
_Fixed in round 23. BuildPlan now records plan.InventoryWasEmpty instead of emitting the warning; ApplyConfirmations emits it at the end only when plan.InventoryWasEmpty && plan.Items.Count > 0 (surviving rows), so a listing that emptied to zero withheld rows no longer warns about rows that are not shown._
_Swept: both production callers (ReconcileScan, BulkDeleteStaleAzureSubnets) always run ApplyConfirmations via ConfirmProposedDeletionsAsync; the pre-existing AnEmptySubscription test rewritten to the full BuildPlan+ApplyConfirmations(Deleted) flow._
_Verified: build 0/0, 880/880; new test AnEmptySubscription_WhereEveryFlaggedRowIsThenWithheld_DoesNotWarnThatRowsBelowAreGone fails when the surviving-count guard is dropped (mutation) and when the emission is put back in BuildPlan (reviewer's scratch revert)._
_Reviewed: independent reviewer PASS — reproduced the bug in a scratch revert, confirmed no new machinery and the join key stays the resource id._
**Residue of:** none

## L4 — Held-by-manual-content reconcile rows report RBAC-hidden live resources as "no longer exists in Azure" `[x1]` — FIXED
_Fixed in round 23. On the held path, when the underlying evaluation produced an absence status, the leading clause is now AbsentFromListingClause() = "...could not be found in this subscription's listing." (truthful whether the resource is deleted or merely RBAC-hidden), keeping the manual-content description and "Delete it here first" remedy. Narrower and more correct than the filed fix: held rows are always withheld (§3's one refusal), so confirming their deleted-vs-hidden verdict changes no action — only the false wording needed fixing, and softening only the held path leaves the confirmed-Deleted plan.Items wording accurate._
_Swept: EvaluateVNetLevel/EvaluateSubnetLevel untouched (confirmed-deleted rows still say "no longer exists"); only absence-derived held rows softened (prefix-changed held rows keep their wording); the BulkDelete 409 inherits held[id].Reason so it is fixed too._
_Verified: build 0/0, 880/880; new test AHeldRowWhoseVNetIsMerelyAbsentFromTheListing_DoesNotAssertItNoLongerExists fails when the held lead reverts to item.Reason (mutation + reviewer's scratch revert)._
_Reviewed: independent reviewer PASS — reproduced, confirmed truthfulness in both cases, no §1 Rule-1 over-withhold, no new machinery._
**Residue of:** none

## L5 — Rename toggle label says renames are 'to VNet names' but the feature also renames child subnets to Azure subnet names `[x1]` `strings` — FIXED
_Fixed in round 23. Changed the toggle label to "Rename matched Bastet subnets to their Azure names" — accurate for both the VNet target (ProposedTargetName) and drifted child subnets (ProposedChildName), where "to VNet names" was false for the child rows._
_Swept: confirmed via the planner and commit path that the toggle renames both target and child rows to their Azure names; no test pinned the label._
_Verified: build 0/0, 880/880._
_Reviewed: independent reviewer PASS — confirmed both halves rename to Azure names, §4 "the control says subnets and must mean it" satisfied._
**Residue of:** none

## L6 — Azure resource-id case-insensitive equality inline at ~12 sites; Ordinal mutants at the 9 unpinned sites survive the suite `[x2]` — FIXED
_Fixed in round 23. Added AzureResourceIdentity.IsSameResourceId + IdComparer (both OrdinalIgnoreCase, null-guarded to match the old IsSameVNet); routed IsSameVNet, planner :29/sameAzureResource/LinkedRowForSameAzureSubnet, the two AzureReconciler dict comparers, the four AzureService comparers, and BulkAzure :73/:245/:359 through them._
_Swept: every AzureResourceId/VNetResourceId equality in the Azure code; left planner :197 (present&&present&&≠, a different decision), BulkAzure :258 (deliberate Ordinal store), Reconciler :343 (StartsWith prefix) untouched; all NetworkAddress/name compares are IP/name strings, not ids._
_Verified: build 0/0, dotnet test 878/878; mutating IsSameResourceId to Ordinal turns the 22-L1 casing test red (single helper now pins every caller), restored green._
_Reviewed: independent reviewer PASS — confirmed every converted site preserves semantics (both-null flips unreachable or strictly safer), not-changed sites are genuinely different decisions, mutation pin reproduced._
**Residue of:** none

# Info

## I1 — BuildPlan's manual-content cascade-withhold call is provably dead machinery `[x1]` — REFUTED
_The finding's load-bearing claim is false: the call at :113-115 is NOT zero-coverage dead code. Deleting it turns two tests red — AzureReconcilerTests.AnAncestorOfAHeldSubnet_IsAlsoWithheld and .AnAncestorOfAManuallyCreatedDescendant_IsStillWithheld — which pin the Rule-2 manual-content ancestor-withhold at the BuildPlan boundary. The verifier's "no test pins the BuildPlan-side warning" is wrong._
_It is redundant with the ApplyConfirmations :209 withhold in the full production flow (a parent confirmed Deleted is still withheld there because its held descendant sits in plan.ReviewItems), but that is belt-and-suspenders on the CORRECT (manual-content) axis — §3's one legitimate refusal — not the wrong-axis machinery §1/§3 want removed. Provenance 23233f2 (the mass revert that cleaned up the withhold mess) kept it deliberately._
_Refuting, not converting into an invented harder fix (delete + rewrite two safety tests to the full flow), which for an Info line-count cleanup would risk a vacuous safety test — the residue pattern the skill exists to prevent._
**Residue of:** none

## I2 — BASTET_AZURE_IMPORT flag decision re-implemented inline in two views instead of calling the single helper `[x1]`
**Where:** src/Bastet/Views/Shared/_Layout.cshtml:46; also src/Bastet/Views/Subnet/Details/_UnallocatedRanges.cshtml:5
**Breaks:** The single implementation is AzureController.IsAzureImportEnabled() (AzureController.cs:201), called by every controller Azure surface and _EditForm.cshtml:28. Two views paste their own bool.TryParse over the env var: _Layout.cshtml:46 (nav links) and _UnallocatedRanges.cshtml:5 (the 'Run a Bulk Azure Import' remedy sentence). Per §5, if the helper's parse semantics change, nav link and remedy diverge from the endpoints they point at. All three parse identically today; the defect is the duplication.
**Repro:** Verifier ran the filed grep: helper, the _EditForm.cshtml:28 helper call, and exactly two inline copies at the cited lines. git blame: _Layout.cshtml:46 <- 73fc76f (#108); _UnallocatedRanges.cshtml:5 <- f4a0a878 (Audit 17 merge, pre-ledger-ids). Live drift demo blocked by a toolchain fault independent of the finding; duplication and fix pattern verified in-tree at HEAD.
**Fix:** Replace both inline parses with Bastet.Controllers.AzureController.IsAzureImportEnabled() (as _EditForm.cshtml:28 already does) and delete the local bool declarations.
**Residue of:** none

## I3 — Reconcile scan-failure branch writes a caveat into a section the failure path always hides `[x2]`
**Where:** src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:172
**Breaks:** On scan failure showScanError sets #rec-rescan-note to "The results below could not be re-scanned, so they may not reflect Azure as it is now." — but every caller runs after runScan's beforeSend added d-none to #rec-scan-content (containing the note's span, _StepReview.cshtml:76) and showScanError never unhides it, so the sentence can never be seen. Visible behavior stays coherent; the assignment is dead mechanism.
**Repro:** Verifier ran it (Playwright): aborted ReconcileScan, clicked Scan -> {errorVisible:true, contentHasDnone:true, noteText set, noteVisible:false}. Re-fulfilled ReconcileScan successfully -> {contentHasDnone:false, noteText:"...have been re-scanned...", noteVisible:true} — line 262/264 is the only path surfacing the note.
**Fix:** Delete the unreachable assignment at _ReconcileScripts.cshtml:172-173. The success-path assignment at line 262 stays; optionally its text becomes the span's static content in _StepReview.cshtml since only one state remains.
**Residue of:** none

## I4 — Dead parent lookup in CIDR-modal relocation can never match `[x1]`
**Where:** src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml:224
**Breaks:** In findCompatibleNetworkAddress the childSubnets.find(s => s.id === parentId) lookup is doubly dead: parentId is a string (hidden #parentId) while s.id is a Razor-emitted number, and childSubnets holds the current subnet's children while parentId names the current subnet itself. The || startingAddress fallback always runs and computes the right parent range — pure dead code, no operator-visible misbehavior.
**Repro:** Verifier: not-runnable as a failure (no observable misbehavior). Node check with the file's own helpers: find -> undefined with string id and with Number(parentId); fallback boundaries from three different starts at /16 all -> 10.10.0.0 - 10.10.255.255. data-parent-id="@Model.Id" (_UnallocatedRanges.cshtml:65) is the only filler of #parentId. git log -S parentNetwork -> 46b3e69 (#17) only.
**Fix:** Delete the find() lookup and derive parentBoundaries directly from the existing normalization: const parentBoundaries = getSubnetBoundaries(startingAddress, parentCidr); (identical behavior, one implementation instead of a dead one plus a fallback).
**Residue of:** none

## I5 — IsSafeText survives on IInputSanitizationService with zero production callers since 18-R21 deleted SafeTextAttribute `[x1]`
**Where:** src/Bastet/Services/Security/InputSanitizationService.cs:33; also :10-11 (SafeTextPattern regex), :33-41; src/Bastet/Services/Security/IInputSanitizationService.cs:8
**Breaks:** IsSafeText and its SafeTextPattern regex have no caller in src/. The last production caller, SafeTextAttribute, was deleted by 18-R21 (f2050fa), leaving the service method and interface member behind — a second, production-dead implementation of the safe-character-set rule (production twin: SubnetNaming.ToSafeText), alive only as a test oracle in four test suites.
**Repro:** Verifier ran it: grep -> only the two declarations plus four test files. Deleted member and regex in a scratch copy: app builds 0 errors; test project fails CS1061 only at the four test files. git log -S "IsSafeText(" -> f2050fa, bf120d6, 3940c98; f2050fa^ confirms both production callers removed by f2050fa.
**Fix:** Delete IsSafeText from both files (with the SafeTextPattern regex) and move the character-set oracle into the test tree (a one-line test-local regex or helper next to SubnetNamingSafeTextTests), preserving the generated-name-safety assertions unchanged.
**Residue of:** 18-R21

## I6 — Orphaned single-VNet-wizard test scaffolding in AzureControllerTests `[x2]`
**Where:** test/Bastet.Tests/Azure/AzureControllerTests.cs:240; also :246, :263, :316, :317
**Breaks:** Five uncalled members: private ControllerWith(IAzureService) (:240), static Parse(IActionResult) (:246), ControllerWithSubnets(...) (:263, followed by a nine-blank-line gap), plus JsonResponse.vnets (:316) and .subnets (:317), never read by any assertion. At d18327e these had 4-6 references; the mass revert 23233f2 deleted the single-VNet wizard tests that called them and left the declarations, inviting a future test to resurrect the deleted wizard's testing style.
**Repro:** Verifier ran it: grep of test/ -> only the declarations, no reads of vnets/subnets. Reference counts across bf120d6, d18327e, 23233f2, HEAD: ControllerWith/Parse 4,4,1,1; ControllerWithSubnets 0,6,1,1. Deleted all five plus the gap in a scratch export: build 0 errors; dotnet test 878/878 (with -p:UseSharedCompilation=false to dodge the box's poisoned shared Roslyn compiler).
**Fix:** Delete ControllerWith, Parse, ControllerWithSubnets, the blank-line gap, and the vnets/subnets properties of the private JsonResponse class (the AzureVNetViewModel/AzureSubnetViewModel usings stay: MockAzureService still needs those types). Suite must stay 878 green.
**Residue of:** none

## I7 — VNetB constant in AzureBulkImportSelectabilityTests dead on arrival `[x2]`
**Where:** test/Bastet.Tests/Azure/AzureBulkImportSelectabilityTests.cs:11
**Breaks:** The suite declares two VNet resource-id constants but every fixture builder keys off VNetA only; VNetB has exactly one occurrence — its declaration — at every revision since the file was created in the round-20 merge 6fb8557. It reads as if a second-VNet scenario is covered when none is.
**Repro:** Verifier ran it: scratch clone; sed -i '11d' (grep -c VNetB -> 0); build 0 errors; filtered test run 16/16 green. git log --follow --diff-filter=A -> 6fb8557; occurrence count 1 at 6fb8557/85b1819/adebf6f; git blame -L11,11 -> 6fb85570.
**Fix:** Delete the VNetB constant (one line).
**Residue of:** 18-R10

## I8 — Three test classes keep a write-only _sanitizationService field since the global-sanitization-filter refactor `[x2]`
**Where:** test/Bastet.Tests/HostIpManagement/SubnetHostIpInteractionTests.cs:23; also :36; test/Bastet.Tests/SubnetManagement/SubnetControllerCidrEditTests.cs:21,32; test/Bastet.Tests/SubnetManagement/SubnetRaceConditionTests.cs:24,35
**Breaks:** Each class constructs an InputSanitizationService into a private readonly _sanitizationService field no test method reads. The reads were removed by pre-audit PR #44 (3e5360d, 'Use global action filter for sanitization'); dead weight since.
**Repro:** Verifier ran it: grep -> exactly 6 hits at the cited lines, declaration + assignment only. git log -S -> 3e5360d, 3940c98 only; 3e5360d~1 vs 3e5360d: 15 -> 2 references. Applied the fix (6 lines deleted): filtered tests 51/51, full suite 878/878.
**Fix:** Delete the field declaration and the constructor assignment in all three test classes (six lines total).
**Residue of:** none

## I9 — The three-argument CalculateUnallocatedRanges overload has no production caller `[x1]`
**Where:** src/Bastet/Services/IIpUtilityService.cs:22; also src/Bastet/Services/IpUtilityService.cs:175-176
**Breaks:** The only production call site is SubnetController.Read.cs:97, using the four-argument form; the three-argument overload is called nowhere in src — superseded when host-IP awareness was added to Details. It survives only because 11 test call sites use it as shorthand for 'no host IPs'. Pre-audit (#11); the weakest item filed — filed for the verifier to weigh rather than silently skipped.
**Repro:** Verifier ran it: deleted the overload at adebf6f; app builds 0 errors; test project fails with exactly 11 errors, all in SubnetPropertyCalculationTests.cs (192,212,264,285,308,324,343,360,370,373,383). Applied ', []' at those sites: 79/79 and 878/878 green. git log -S -> e05c3b1 (#11).
**Fix:** Delete the three-argument overload from IIpUtilityService and IpUtilityService and append ', []' at the 11 test call sites; alternatively, if the owner prefers keeping the convenience overload as deliberate API surface, record that and close with no change.
**Residue of:** none

## I10 — Twelve view-model properties mapped on every request but never rendered anywhere `[x1]`
**Where:** src/Bastet/Models/ViewModels/DeletedSubnetViewModel.cs:6 (verifier citation: primary correct; the filed Delete.cs:265 is wrong — that is the live Description mapping; OriginalParentId is at Delete.cs:266); also DeletedSubnetViewModel.cs:16; HostIpViewModels.cs:13; AllHostIpViewModels.cs:26; DeletedHostIpViewModels.cs:15,18,22-24; AllDeletedHostIpViewModels.cs:15,26-28; SubnetController.Delete.cs:261,266; SubnetController.Read.cs:94; HostIpController.cs:57,483,526,531-533,630,633,637-639
**Breaks:** DeletedSubnetsViewModel.OriginalId/.OriginalParentId, HostIpViewModel.ModifiedBy, AllHostIpItemViewModel.ModifiedBy, DeletedHostIpViewModel.Id/.OriginalSubnetId/.CreatedBy/.LastModifiedAt/.ModifiedBy, AllDeletedHostIpItemViewModel.Id/.CreatedBy/.LastModifiedAt/.ModifiedBy are populated on every page load but appear in no view, JS, controller read or test (the OriginalId assert at SubnetControllerAzureReconcileTests.cs:211 is on the DeletedSubnet entity, alive via HostIpController.cs:547). Dead mapping surface where a future rendering bug can hide. Pre-audit (841c272 #18, e05c3b1 #11); never rendered in any revision.
**Repro:** Verifier ran it: deleted the 13 properties + mapping lines at adebf6f (Delete.cs:261,266; Read.cs:94; HostIpController.cs:57,483,526,531-533,630,633,637-639); build 0 errors; 878/878 tests. Drove the mutated app live: all six affected pages returned 200 with expected rows.
**Fix:** Delete the properties and their object-initializer mapping lines; same shape as the fixed 18-R23. verifier judged the filed fix unsound: The fix cites "SubnetController.Delete.cs:261,265-266 viewmodel mapping only" — line 265 is `Description = ds.Description,` and Description IS rendered by DeletedSubnets/_SubnetTable.cshtml; deleting it would silently blank the Description column on the DeletedSubnets page. The correct mapping lines are 261 (OriginalId) and 266 (OriginalParentId) only. Otherwise the fix is verified sound exactly as prosed: delete the thirteen properties and their object-initializer lines (SubnetController.Delete.cs:261,266; SubnetController.Read.cs:94; HostIpController.cs:57,483,526,531-533,630,633,637-639), keeping the DeletedSubnet archive-entity writes at Delete.cs:230-231 and the entity read at HostIpController.cs:547 — applied and proven green (build 0 errors, 878/878 tests, all six pages render correctly live).
**Residue of:** none

# Refuted

| title | beat | reason |
|---|---|---|
| Bulk import marks a target fully allocated without the base ValidateSubnetCanBeFullyAllocated validation | logic-data | The write is not unvalidated: commit re-runs the planner and refuses unless plan.CanCommit; WillMarkFullyAllocated (:636) is set only after the planner refuses children (:627) and host IPs (:549). No reachable failure exists and the finding admits it ("none exists at HEAD") — pure "if the implementations ever drift", the "could be a problem if..." the finding contract rejects. Not a demonstrated defect; would be Low in any case. |
