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

## I2 — BASTET_AZURE_IMPORT flag decision re-implemented inline in two views instead of calling the single helper `[x1]` — FIXED
_Fixed in round 23. Replaced the inline bool.TryParse in _Layout.cshtml and _UnallocatedRanges.cshtml with Bastet.Controllers.AzureController.IsAzureImportEnabled() (as _EditForm.cshtml already does)._
_Swept: grep confirms the only remaining BASTET_AZURE_IMPORT parse in src/ is the helper itself; no inline copy left under Views/._
_Verified: build 0/0, 880/880; helper does the identical parse so nav-link/remedy gating is unchanged._
_Reviewed: independent reviewer PASS — byte-identical parse, reachable from Razor, no inline copies remain._
**Residue of:** none

## I3 — Reconcile scan-failure branch writes a caveat into a section the failure path always hides `[x2]` — FIXED
_Fixed in round 23. Deleted the unreachable #rec-rescan-note assignment in showScanError; the success-path assignment stays._
_Verified statically: #rec-rescan-note lives inside #rec-scan-content, which runScan's beforeSend hides (d-none) and showScanError never unhides — the note could never be seen; build 0/0, 880/880._
_Reviewed: independent reviewer PASS — proved unreachability from the DOM/handler structure; success path intact._
**Residue of:** none

## I4 — Dead parent lookup in CIDR-modal relocation can never match `[x1]` — FIXED
_Fixed in round 23. Deleted the childSubnets.find(s => s.id === parentId) lookup and the dead parentId local; parentBoundaries now derives from startingAddress directly (behaviour-identical)._
_Verified statically: s.id is a Razor-emitted number, $('#parentId').val() is a string, so the strict-equality find always returned undefined and the || startingAddress fallback always ran; the other #parentId uses (setter, Create URL) untouched; build 0/0, 880/880._
_Reviewed: independent reviewer PASS — proved the find never matched and the replacement is identical._
**Residue of:** none

## I5 — IsSafeText survives on IInputSanitizationService with zero production callers since 18-R21 deleted SafeTextAttribute `[x1]` — FIXED
_Fixed in round 23. Deleted IsSafeText and SafeTextPattern from InputSanitizationService.cs and the IsSafeText member from IInputSanitizationService.cs; relocated the character-set oracle to test/Bastet.Tests/SafeTextOracle.cs (identical regex + null/whitespace short-circuit) and re-pointed all four test suites to SafeTextOracle.IsSafe._
_Swept: grep confirms zero IsSafeText anywhere in src/; removed orphaned _sanitizer/sanitizer locals where IsSafeText was their only use and the stranded Bastet.Services.Security using; renamed the direct test to SafeTextOracle_ValidatesCorrectly._
_Verified: build 0/0, 880/880 (no test dropped); mutating the oracle regex reddens ToSafeText_KeepsExactlyTheCharactersSafeTextAccepts, restored green._
_Reviewed: independent reviewer PASS — zero production callers, no implementer breaks, oracle behaviour-identical, discrimination reproduced._
**Residue of:** 18-R21

## I6 — Orphaned single-VNet-wizard test scaffolding in AzureControllerTests `[x2]` — FIXED
_Fixed in round 23. Deleted the uncalled ControllerWith/Parse/ControllerWithSubnets helpers, the 9-blank gap, and the never-read JsonResponse.vnets/.subnets; kept success/error/subscriptions and the view-model usings MockAzureService needs._
_Verified: build 0/0, 880/880 (no test removed); grep confirms zero references._
_Reviewed: independent reviewer PASS — zero references tree-wide, kept members still used._
**Residue of:** none

## I7 — VNetB constant in AzureBulkImportSelectabilityTests dead on arrival `[x2]` — FIXED
_Fixed in round 23. Deleted the single unused VNetB const in AzureBulkImportSelectabilityTests.cs; the used VNetB consts in TopUpTests and TargetNameTests are untouched._
_Verified: build 0/0, 880/880; grep confirms VNetB gone from the selectability file only._
_Reviewed: independent reviewer PASS._
**Residue of:** 18-R10

## I8 — Three test classes keep a write-only _sanitizationService field since the global-sanitization-filter refactor `[x2]` — FIXED
_Fixed in round 23. Deleted the write-only _sanitizationService field (declaration + ctor assignment) in SubnetRaceConditionTests, SubnetControllerCidrEditTests, SubnetHostIpInteractionTests; InputSanitizationServiceTests (which reads it) untouched._
_Verified: build 0/0, 880/880; grep confirms zero references in the three files._
_Reviewed: independent reviewer PASS._
**Residue of:** none

## I9 — The three-argument CalculateUnallocatedRanges overload has no production caller `[x1]` — FIXED
_Fixed in round 23. Deleted the 3-arg CalculateUnallocatedRanges overload from IIpUtilityService and IpUtilityService (it only delegated to the 4-arg with []); appended ', []' at the 11 test call sites in SubnetPropertyCalculationTests.cs (behaviour-identical: no host IPs)._
_Swept: the sole production caller (SubnetController.Read.cs:97) already used the 4-arg form; grep confirms no 3-arg call remains; the two genuine host-IP tests were left untouched._
_Verified: build 0/0, 880/880 (no test dropped)._
_Reviewed: independent reviewer PASS — zero production callers, all 11 sites equivalent, nothing else touched._
_Product question (recorded, not blocking): the overload could instead be kept as deliberate convenience API surface; auto-mode deleted it per §5's one-implementation preference. Owner may re-add it if the convenience form is wanted._
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
