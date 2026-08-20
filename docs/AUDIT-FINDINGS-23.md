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

## L1 — Reconcile delete refusal falsely claims withheld rows are 'no longer reported as deleted in Azure' `[x1]` `strings`
**Where:** src/Bastet/Controllers/SubnetController.AzureReconcile.cs:102
**Breaks:** VNet-linked 'vnet-x' 10.0.0.0/16 with subnet-linked child 'sub-a' 10.0.1.0/24; VNet deleted in Azure; operator approves delete. The VNet re-confirms Deleted but the child's ARM read fails transiently (Unknown, AzureService.cs:275-279). ApplyConfirmations withholds sub-a, the cascade withhold removes vnet-x from plan.Items, so the 409 headline says "1 of the selected subnet(s) are no longer reported as deleted in Azure. Nothing was deleted. Re-run the scan..." — untrue (Azure still reports it deleted), contradicted by the same response's warnings on one screen (_ReconcileScripts.cshtml:417-432), and the named remedy reproduces the state.
**Repro:** Verifier ran the finder's probe (real controller/reconciler/snapshot service, Sqlite, stub: empty inventory, {vnet: Deleted, subnet: Unknown}): HTTP 409 with that headline plus the two truthful withhold warnings; subnet still present.
**Fix:** Reword the one string at SubnetController.AzureReconcile.cs:102 to what is established, e.g. "N of the selected subnet(s) are no longer offered for deletion by the re-check, so nothing was deleted. The warnings below say why; re-run the scan and review the results." — truthful for every noLongerStale bucket.
**Residue of:** none

## L2 — Client-side second implementations of CIDR containment and IP-integer arithmetic `[x1]`
**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:399; also _BulkScripts.cshtml:196, :387-413; src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml:174-287
**Breaks:** Violates §5 (IpUtilityService is the only IP arithmetic; the fix is deletion) and §4 (client must not re-derive planner decisions; 18-R10 precedent). Wizard JS ipToInt/prefixContainsCidr (:387-413, used at :196) decides which VNet prefix each Azure subnet renders and posts under — same question as planner SubnetsWithinPrefix (:455-460) and BuildPlan containment (:114-122). Details carries a free-standing engine (masks, boundaries, overlap, optimal-CIDR search) duplicating IsSubnetContainedInParent/CalculateUnallocatedRanges. Both agree today; drift would post subnets under the wrong prefix (whole-commit refusal, unactionable errors) or recommend a network/CIDR the Create POST rejects.
**Repro:** Verifier ran it live (Playwright): BulkGetVNets JSON has flat subnets[] with no subnet-to-prefix mapping, yet the DOM grouped subnets under the right prefixes with data-prefix stamped — computed only client-side. Details on 10.99.0.0/16: modal recommended /17; debug trace showed the client engine computing boundaries and overlap live. git log -S: prefixContainsCidr <- 73fc76f (#108), Details engine <- 46b3e69 (#17) — original feature code.
**Fix:** Wizard: during AnnotateAvailability stamp each inventory subnet row with the address prefix it belongs to (AnnotateSubnet already computes containment), have the client group by that served key, and delete ipToInt/prefixContainsCidr. The Details modal has no server counterpart — replacing its engine with a server-computed prefill would be a restructure (new endpoint or precomputed per-range suggestions in the view model), flagged separately rather than smuggled into the narrow fix.
**Residue of:** none

## L3 — Reconcile empty-listing warning contradicts the withheld result on the same screen `[x1]`
**Where:** src/Bastet/Services/Azure/AzureReconciler.cs:117
**Breaks:** Credential loses its RG-scoped role; listing returns 200 with zero VNets. BuildPlan flags all linked rows absent and warns 'Azure reported no VNets at all ... every one of the 3 Azure-linked subnet(s) below is flagged as deleted...'. ApplyConfirmations then withholds all 3 (403 -> NotVisible) and empties plan.Items, but the warning stays. The Review screen shows both warnings side by side with zero rows below (stale table hidden when items empty, _ReconcileScripts.cshtml:237) — the first sentence untrue on the screen it appears on.
**Repro:** Verifier ran a scratch harness against the real AzureReconciler: BuildPlan(Success=true, empty VNets, 3 linked snapshots) then ApplyConfirmations(NotVisible x3) -> Items=0 with both warnings present. Rendering confirmed at _ReconcileScripts.cshtml:207-210, 237.
**Fix:** Emit the warning after confirmations: BuildPlan records inventory emptiness (e.g. bool InventoryWasEmpty on the plan); ApplyConfirmations adds the warning from the surviving plan.Items count (skipping when zero survive). Both production callers already always run ApplyConfirmations. Update the pin at AzureReconcilerTests.cs:314.
**Residue of:** none

## L4 — Held-by-manual-content reconcile rows report RBAC-hidden live resources as "no longer exists in Azure" `[x1]`
**Where:** src/Bastet/Services/Azure/AzureReconciler.cs:94; also :91-99, :274-275, :301-302; src/Bastet/Controllers/AzureController.cs:189-191; src/Bastet/Controllers/SubnetController.AzureReconcile.cs:89-91
**Breaks:** Row linked to a VNet live in Azure but in an RG the credential cannot read. Without manual content the scan is truthful (withheld, "Azure denied access..."). Add one hand-made child and the row diverts to ReviewItems as HeldByManualContent BEFORE ConfirmProposedDeletionsAsync (which confirms plan.Items only), so the live, merely-invisible VNet is flatly reported: "The VNet this subnet was imported from no longer exists in Azure. ... Delete it here first, then run the scan again." — echoed in the bulk-delete 409. An operator believing it can hand-delete the live allocation; Bastet then reports as free space Azure holds.
**Repro:** Verifier ran it live as SP1 (SP2 CLI proves the VNet exists; SP1 gets AuthorizationFailed). Linked row alone: ReconcileScan -> truthful denied-access warning. With a manual child: reviewItems {HeldByManualContent, reason:"...no longer exists in Azure..."} — false, truthful warning gone. BulkDeleteStaleAzureSubnets -> 409 repeating the sentence.
**Fix:** Include ReviewItems whose evaluation produced an absence status (VNetDeleted/SubnetDeleted) in the id set passed to ConfirmResourcesAsync, and in ApplyConfirmations rewrite the held item's leading clause per verdict — "no longer exists" only for Deleted; for NotVisible/Unknown/Live state what is established — leaving the manual-content withhold sentence and remedy untouched. Cheaper interim (string-only): change the two Evaluate* absence reasons on the held path to state the listing fact ("could not be found in this subscription's listing") instead of asserting nonexistence.
**Residue of:** none

## L5 — Rename toggle label says renames are 'to VNet names' but the feature also renames child subnets to Azure subnet names `[x1]` `strings`
**Where:** src/Bastet/Views/Azure/BulkImport/_StepSelection.cshtml:53
**Breaks:** Toggle reads 'Rename matched Bastet subnets to VNet names', but with it on the wizard renames drifted child subnets to their Azure SUBNET names. It is the sole discovery point for child-name repair, so an operator reading it literally never enables it for drifted children; one wanting only VNet-target renames gets child renames too. Per-row reasons contradict the label on the same screen.
**Repro:** Verifier ran it live (Playwright): imported rig-vnet-multi, SQL-renamed a child; toggle OFF -> checkbox disabled, no rename offer; toggle ON -> preview 'Rename to rig-sub-a', commit 'renamed 1 child subnet(s)', DB Name back to 'rig-sub-a'. Child target from ProposedChildName (AzureBulkImportPlanner.cs:427-429); per-row reason at _BulkScripts.cshtml:263. String from 73fc76f (#108).
**Fix:** One string: change the label to 'Rename matched Bastet subnets to their Azure names' (covers both halves; per-row reasons already say which name applies).
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
