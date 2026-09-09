# Bastet — Round-29 Audit Findings

- Branch: `audit/round-29`
- HEAD: `c38d031405e9a75a53917eb9289914272cd920b9`
- Test baseline: 969 passed, 0 failed, 0 skipped, 0 build warnings
- Date: 2026-09-08
- Scope: Regression-only over 8891320..HEAD (round 28 residue 4 > 2)
- Beat structure: beats 6 and 7 × 2 passes
- Funnel: 7 raw → 5 merged (2 x2, 3 x1) → 5 survived, 0 refuted, 0 dropped at merge
- Round 29 filed 5 findings, of which 5 are residue of round 28's own fixes.
- `test/Bastet.Tests/SubnetManagement/SubnetDetailsModalScriptTests.cs` carries L3 and L5: 137 lines at HEAD, 101 lines at 8891320.

# Critical

None.

# High

None.

# Medium

None.

# Low

## L1 — HostIpViewSourceTests binds incidental tokens, not the rendering: a conditional wrapper on Edit/_Header brings back the empty 'Error' alert with the pin green (and a hand-rolled second rendering on Create also passes) `[x2]`

**Where:** test/Bastet.Tests/HostIpManagement/HostIpViewSourceTests.cs:39; test/Bastet.Tests/HostIpManagement/HostIpViewSourceTests.cs:33; test/Bastet.Tests/HostIpManagement/HostIpViewSourceTests.cs:34; test/Bastet.Tests/HostIpManagement/HostIpViewSourceTests.cs:35; test/Bastet.Tests/HostIpManagement/HostIpViewSourceTests.cs:36; test/Bastet.Tests/HostIpManagement/HostIpViewSourceTests.cs:40; test/Bastet.Tests/HostIpManagement/HostIpViewSourceTests.cs:41

**Breaks:** PRODUCT-MODEL §5 test rule, shape "a test that stays green with the code it guards broken". For Edit the pin only requires exactly one `asp-validation-summary="ModelOnly"` in Edit/_Header.cshtml and the absence of the tokens `ModelState.ErrorCount` and `alert-heading`. Re-wrapping the summary (src/Bastet/Views/HostIp/Edit/_Header.cshtml:8) as `@if (!ViewData.ModelState.IsValid) { <div class="alert alert-danger mb-4" role="alert"><h4>...Error</h4><div asp-validation-summary="ModelOnly" class="mb-0"></div></div> }` uses neither token and re-creates 28-L6's Edit defect: submitting Name `<b>bold</b>` (a field-level [NoHtml] error, no model-level error, so the ModelOnly tag helper emits nothing) renders a red alert whose entire content is the heading "Error" above the form, while HEAD renders no alert at all. For Create the pin counts tag helpers per file and forbids one file name, so a new partial that hand-rolls `foreach (var error in ViewData.ModelState[""]!.Errors)` (the original 28-L6 shape under a different name) restores the double rendering of model-level errors and also passes; so does the codebase's own tag-helper line added to Create/_Header.cshtml or Edit/_EditForm.cshtml, files the pin never reads (every model-level error, e.g. "This IP address is already assigned" and the concurrency-conflict sentence, then renders twice). Each mutant leaves the whole suite green. Low: a contradiction visible on one screen, and 28-L6's own defects.

**Repro:** Verifier: fresh `git archive HEAD` (c38d031) copies under rig/v2-C1/{src,src-revert,src-v7,src-v19,src-v20,src-v21}, each built with `dotnet build Bastet.sln -c Debug -p:UseSharedCompilation=false` (0 warnings, 0 errors); tests via `dotnet test test/Bastet.Tests/Bastet.Tests.csproj --no-build -p:UseSharedCompilation=false [--filter "FullyQualifiedName~HostIpViewSourceTests"]`. (1) HEAD, filter: `Test run summary: Passed!  total: 1  failed: 0  succeeded: 1  skipped: 0`. (2) Full revert of Edit/_Header.cshtml, Create.cshtml and Create/_ErrorAlert.cshtml to 8891320, full suite: `Failed!  total: 969  failed: 1  succeeded: 968`; only red test `HostIpViewSourceTests.HostIpCreateAndEdit_RenderModelLevelErrorsExactlyOnce` (`Assert.Empty() Failure: Collection was not empty` at L33) — the candidate does not rest on the full revert. (3) V7, Edit/_Header.cshtml L8 `<div asp-validation-summary="ModelOnly" class="alert alert-danger mb-4" role="alert"></div>` replaced by `@if (!ViewData.ModelState.IsValid) { <div class="alert alert-danger mb-4" role="alert"> <h4><i class="bi bi-exclamation-triangle-fill me-2"></i>Error</h4> <div asp-validation-summary="ModelOnly" class="mb-0"></div> </div> }`: full suite `Passed!  total: 969  failed: 0  succeeded: 969`. V19, Create.cshtml gains `<partial name="Create/_ModelErrors" />` after the `Create/_Header` partial plus new Create/_ModelErrors.cshtml = `@if (ViewData.ModelState[""] != null && ViewData.ModelState[""]!.Errors.Count > 0) { <div class="alert alert-danger mb-4" role="alert"> @foreach (var error in ViewData.ModelState[""]!.Errors) { <div>@error.ErrorMessage</div> } </div> }`: full suite `Passed!  total: 969  failed: 0  succeeded: 969`. V20, the exact tag-helper line the codebase already uses appended to Create/_Header.cshtml and inserted directly after `<form asp-action="Edit" method="post">` in Edit/_EditForm.cshtml (mirrors _HostIpForm.cshtml:15): pin `Passed!  total: 1`, full suite `Passed!  total: 969  failed: 0`. V21, the Edit summary inside the deleted Create/_ErrorAlert's own dismissible `@if (!ViewData.ModelState.IsValid) { <div class="alert alert-danger alert-dismissible fade show mb-4" role="alert"> ... <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button> </div> }` (condition identical to Views/Azure/BulkImport.cshtml:14 and Reconcile.cshtml:14): pin green, full suite `Passed!  total: 969  failed: 0`. (4) Live, rig/v2-C1/run-app.sh (SP1, Development, BASTET_AZURE_IMPORT=true, AZURE_TOKEN_CREDENTIALS unset), ports 5340 (V7, pid 44207), 5341 (V19, pid 44211), 5342 (HEAD control, pid 44221), 5343 (V20, pid 56593), catalogs bastet_rig_v2c1_{v7,v19,head,v20}, playwright drivers rig/v2-C1/drive.py and drive2.py, JSON rig/v2-C1/drive*-*.json, screenshots rig/v2-C1/shots/. V7: POST /HostIp/Edit for 10.66.0.10 with Name `<b>bold</b>` (field-level [NoHtml], HostIpViewModels.cs:44; redisplay via HostIpController.cs:300 → RedisplayEditAsync) → `.alert-danger` = `[{"text": "Error", "html": "<div class=\"alert alert-danger mb-4\" role=\"alert\">\n        <h4><i class=\"bi bi-exclamation-triangle-fill me-2\"></i>Error</h4>\n        \n    </div>"}]`, field_errors `["HTML tags are not allowed in host names"]` (shots/v7-edit-fielderror.png: a red box reading only "Error" above the form); HEAD :5342 same POST → `.alert-danger` = `[]`, same field error (shots/head-edit-fielderror.png). V19: POST /HostIp/Create with already-assigned 10.66.0.10 (ValidateNewHostIp → AddModelError("") at HostIpController.cs:114) → two `.alert-danger`: `<div class="alert alert-danger mb-4" role="alert">\n            <div>This IP address is already assigned</div>\n    </div>` and `<div class="alert alert-danger validation-summary-errors" role="alert"><ul><li>This IP address is already assigned</li>\n</ul></div>` (shots/v19-create-modelerror.png); HEAD → exactly one (the tag helper's). V20 :5343 (drive2.py): Create with duplicate 10.67.0.10 → two identical "This IP address is already assigned" alerts (HEAD: one); Edit posted from a second tab holding a stale RowVersion (HostIpValidationService.cs:106-109 CONCURRENCY_CONFLICT → AddModelError("") at HostIpController.cs:235) → two identical "This host IP was modified by another user while you were editing it, so it was not saved. Reload the page to see the current values, then re-apply the changes that still make sense." alerts (HEAD: one); shots/v20-d2-*.png vs head-d2-*.png. All four PIDs killed afterwards, ports free. (5) Settled-gap check: docs/AUDIT-LEDGER.md:169 (28-L6) says "pinned by HostIpViewSourceTests"; brief row "none recorded; pinned by view source"; .claude/skills/e2e/SKILL.md has no record of this surface (only generic host IP create/edit request coverage ~L747-755). Not settled. Finder (b7B): the same V7 and V19 mutants (rig/b7B/mutant-V7-_Header.cshtml, mutant-V19-Create.cshtml + mutant-V19-_ModelErrors.cshtml) in rig/b7B/src: `dotnet test/Bastet.Tests/bin/Debug/net10.0/Bastet.Tests.dll --filter-class Bastet.Tests.HostIpManagement.HostIpViewSourceTests` → `Passed! total: 1 failed: 0 succeeded: 1` for each, whole dll 969/969; controls: Edit/_Header reverted to 8891320 → `Assert.DoesNotContain() Failure: Sub-string found`; Create.cshtml + Create/_ErrorAlert.cshtml restored → `Assert.Empty() Failure: Collection was not empty`; V7 live on :5370 → edit_redisplay.alerts_danger = `[{text: "Error", ...}]` with field_errors `["HTML tags are not allowed in host names"]`, HEAD on :5371 → `[]` (rig/b7B/shots/mut-edit-redisplay.png).

**Fix:** The finder's fix (pin the Edit line verbatim plus `Assert.DoesNotContain("@if", editHeader)`, and `Assert.DoesNotContain("ModelState", ...)` over every view under Views/HostIp) was judged unsound: it is still a token pin — V20, a second `asp-validation-summary="ModelOnly"` in Create/_Header.cshtml or Edit/_EditForm.cshtml, contains no `@if` and no `ModelState`, keeps the verbatim line, passes that fix entirely and doubles every model-level error live. Filed fix, test-only: pin the behaviour the row promises — exactly one model-level rendering across every view each page is composed from, delivered by one unwrapped tag helper. In test/Bastet.Tests/HostIpManagement/HostIpViewSourceTests.cs replace the Fact with this Theory (keep FindRepoRoot/ViewPath/ReadView; drop ModelOnlySummaries and the `_ErrorAlert` file-name assertions, which this subsumes):

```csharp
private static int ModelLevelRenderings(string view) =>
    Regex.Matches(view, @"asp-validation-summary=|Html\.ValidationSummary\(|ModelState").Count;

private static IEnumerable<string> PageViews(string page) =>
    Directory.GetFiles(ViewPath($"src/Bastet/Views/HostIp/{page}"), "*.cshtml")
        .Prepend(ViewPath($"src/Bastet/Views/HostIp/{page}.cshtml"));

[Theory]
[InlineData("Create", "Create/_HostIpForm.cshtml", "<div asp-validation-summary=\"ModelOnly\" class=\"alert alert-danger\" role=\"alert\"></div>")]
[InlineData("Edit", "Edit/_Header.cshtml", "<div asp-validation-summary=\"ModelOnly\" class=\"alert alert-danger mb-4\" role=\"alert\"></div>")]
public void HostIpPage_RendersModelLevelErrorsExactlyOnce_ThroughOneUnwrappedSummary(string page, string owner, string summaryLine)
{
    Assert.Equal(1, PageViews(page).Sum(view => ModelLevelRenderings(File.ReadAllText(view))));

    string ownerView = ReadView("src/Bastet/Views/HostIp/" + owner);
    Assert.Contains(summaryLine, ownerView);
    Assert.Single(Regex.Matches(ownerView, "alert-danger"));
}
```

The first assertion pins "exactly once" over Create.cshtml + Create/*.cshtml and Edit.cshtml + Edit/*.cshtml through every Razor route to model-level errors (tag helper, Html.ValidationSummary, hand-rolled ModelState reads, any ModelState-keyed wrapper); the last two pin that the single summary is the unwrapped line carrying the alert classes itself, so no outer alert box can render around an empty summary. Verified in rig/v2-C1/HostIpViewSourceTests.corrected.cs: 0 build warnings (Assert.Single avoids xUnit2013); HEAD `Passed!  total: 2  failed: 0  succeeded: 2`; full 8891320 revert `Failed!  total: 2  failed: 2`; V7 `failed: 1` (Edit row); V19 `failed: 1` (Create row); V20 `failed: 2`; V21 `failed: 1` (Edit row). No production change; no cheaper interim.

**Residue of:** 28-L6 (pin and fix both introduced by 5ea2eb5, #190: git blame HostIpViewSourceTests.cs L28-42 and Edit/_Header.cshtml L8 → 5ea2eb52; `git log -S 'ModelOnlySummaries' -- test/` → 5ea2eb5 only)

## L2 — 28-L2 redisplay pin does not bind LastModifiedAt; a helper that drops it shows "Last Modified: Never" and stays green `[x2]` — FIXED

_Fixed on audit/round-29 (test-only). HostIpEditRedisplayTests seeds LastModifiedAt/ModifiedBy, captures the stored stamp from the AsNoTracking read-back, and asserts it on all three redisplay tests (renamed ...AndDates)._
_Swept: SubnetControllerConcurrencyRedisplayTests.cs:53,82 already binds LastModifiedAt for the subnet card; no other redisplay pin lacks it._
_Verified: build 0 warnings, suite green; pin green at HEAD, 3/3 red under the dropped L317 stamp, 2/3 red under the 8891320 controller revert; reviewer's five further helper regressions (posted-value stamp, swapped stamps, dropped Include, FindAsync reload, tracked reload) all red or caught by the suite except the recorded DbUpdateConcurrencyException site._
_Reviewed: PASS. Note adopted as fact, not fix: HostIpEditConcurrencyCatchTests already has a SQLite seam (SaveThrowingBastetDbContext) for the catch the 28-L2 row called seamless, so that site is pinnable at fix time if ever reopened._

## L3 — 28-L10's pin checks the refusal sentence's presence, not which refuse() branch carries it; a branch swap stays green `[x1]` — STRUCK

_Struck at triage under PRODUCT-MODEL §5: "A test finding exists only when a regression that puts the ledger row's own operator-visible defect back on screen leaves the entire suite green, and the surface's recorded `/e2e` drive, where one exists, would not catch it either." The recorded drive (.claude/skills/e2e/SKILL.md phase F, lines 654 and 663) asserts "typing 24.5 is refused (Create disabled, size Invalid)" and that the no-home refusal reads "No free /N block starts at or after A.B.C.D."; the branch swap and the dead-condition mutant both fail that drive._
_Not done: the two branch-bound regexes proposed by the verifiers — pin strength beyond the recorded drive is hardening, never filed._

## L4 — 28-L4 sidebar pin passes with IsAzureLinked read backwards (linked row told its CIDR is modifiable) `[x1]` — FIXED

_Fixed on audit/round-29 (test-only). SubnetEditViewSourceTests binds each sidebar sentence to its own IsAzureLinked branch with two regexes, counts the four pinned strings exactly once, and binds the form's readonly input to the form's linked branch._
_Swept: the Edit form's own IsAzureLinked branch was the one sibling of the same shape (token presence only) and is now bound; no other view-source pin was widened._
_Verified: build 0 warnings, suite green; pin red under both-site inversion, each single flip, the rules-block hoist, the 8891320 revert, the form inversion, the form content swap, a ViewData predicate, and four added-duplicate regressions; green under a whitespace reflow and an attribute reorder._
_Reviewed: first review (a) — added duplicates of a sentence outside its branch passed the pin; revised once with exactly-once counts and a looser form regex; re-review PASS. Non-blocking (c): a paraphrase outside the pin's vocabulary and a readonly→disabled tidy remain uncovered/false-red respectively, hardening by the reviewer's own account._

## L5 — CidrModalScript_RefuseResetsTheNetworkAddressToTheRangeStart accepts a reset gated inside refuse(); the 28-L9 defect returns with the pin green `[x1]` — STRUCK

_Struck at triage under PRODUCT-MODEL §5 (same sentence as L3). The recorded drive (.claude/skills/e2e/SKILL.md phase F, line 666) asserts that after an adjustment an out-of-range, empty or no-home CIDR "resets the address to the range start and hides the warning (every refusal resets ...)"; the gated reset and the keepAddress-flag mutant both fail that drive._
_Not done: the positional regex proposed by the verifiers — hardening beyond the recorded drive._

# Info

None.

# Refuted — reported by a finder, killed by the verifier

| id | title | where | reason |
|---|---|---|---|

None.
