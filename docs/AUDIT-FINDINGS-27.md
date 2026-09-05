# Bastet — Round-27 Audit Findings

branch audit/round-27 / HEAD fdcc202 / 905 tests passing at baseline (0 failed, 0 build warnings) / 2026-09-05 / delta f8bd697..fdcc202 (Regression-only: beats 6 and 7, two independent passes each; 1 verifier per [x2], 2 per [x1], a 3rd to break ties)

Round 27 filed 2 findings, of which 1 are residue of round 26's own fixes; the other 1 is residue of round 24 (24-L4).

# Critical

None.

# High

None.

# Medium

None.

# Low

## L1 — Reconcile/_ErrorAlert.cshtml's static "Could not connect to Azure." headline is rendered above the zero-subscription "Signed in to Azure, but this credential cannot see any subscriptions..." sentence on /Azure/Reconcile; BulkImport/_ErrorAlert.cshtml renders the same ModelState error with no headline `[x2]` `strings` — FIXED

_Fixed in commit "Audit 27 L1" on audit/round-27. Views/Azure/BulkImport/_ErrorAlert.cshtml moved unchanged to Views/Azure/_ErrorAlert.cshtml, Views/Azure/Reconcile/_ErrorAlert.cshtml (the only partial carrying the static "Could not connect to Azure." headline) deleted, and both BulkImport.cshtml and Reconcile.cshtml render the one shared partial; no wording, guard or special case added._
_Swept: every view iterating ModelState (HostIp/Create/_ErrorAlert, HostIp/Edit/_Header, Subnet forms) prints no static verdict above the list; grep for "could not connect" / "connect to Azure" in src returns nothing; the client-side "Error connecting to server" ajax panels are a different surface and were left alone; only AzureController renders these views, so Html.PartialAsync("_ErrorAlert") resolves via Views/Azure._
_Verified: build 0 warnings, suite 906/906 (905 + pin); pin WizardPages_RenderOneSharedErrorAlert_ThatCarriesNoConnectivityHeadline red at HEAD (Single saw 2 partials), red with the headline re-added to the shared partial, red with Reconcile re-pointed at its own copy, red with the headline inlined in Reconcile.cshtml, green on the fix; live re-drive of the finding's own repro on the fixed DLL: intercept proxy answering GET /subscriptions with {"value":[]} after a real token round-trip -> /Azure/Reconcile and /Azure/BulkImport both render exactly "Signed in to Azure, but this credential cannot see any subscriptions. Grant it access to a subscription and reload this page." with no headline; bad-secret instance renders exactly "Could not authenticate with Azure. Check the credentials and that Azure is reachable from this host." on both pages._
_Reviewed: PASS (independent reviewer: rename byte-identical, partial resolves at request time on every page rendering an _ErrorAlert incl. HostIp Create GET+POST, pin red at HEAD and under three regressions, no product-model sentence contradicted); reviewer's non-blocking note adopted: the pin now also asserts the headline is absent from both page files (mutation-verified red)._

## L2 — 26-L3 wording theory pins token presence, not branch binding: swapping, deadening or un-guarding the xhr.status-0 ternary in both wizard scripts stays 4/4 green while the operator gets "The server returned status 0." on a transport failure (25-L3 in new words) `[x2]`

**Where:** test/Bastet.Tests/Azure/AzureWizardClientWordingTests.cs:51 (lines 51-56, both theory rows, BulkImport and Reconcile); src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:663-667 (guarded `if (!payload)` / `xhr.status === 0` ternary, blame f8bd697 = 25-L3); src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:404-408 (same construct, blame f8bd697 = 25-L3)

**Breaks:** The theory CommitErrorHandler_BranchesOnUnreachableServer_AndNeverAssertsServerErrorZero (lines 51-56) is four independent substring asserts on the raw .cshtml text: contains `xhr.status === 0`, contains the unreachable sentence, contains `"The server returned status " + xhr.status`, does not contain `Server error: `. It never binds the status-0 condition to the sentence it must select. Swapping the `?` and `:` operands of the ternary at _BulkScripts.cshtml:664-666 and _ReconcileScripts.cshtml:405-407 keeps all four tokens present, so both theory rows stay green while the operator surface is the 25-L3 defect re-worded: an operator who clicks Commit (bulk import) or Confirm delete (reconcile) while the server is unreachable (xhr.status 0) gets the banner "The server returned status 0." (a definite server verdict asserted on a transport failure), and an unparsable HTTP 500 gets "The server could not be reached, so it is unknown whether the change was applied. Check the subnet list before retrying." although the server answered. Deleting the `if (!payload)` wrapper so the ternary overrides a parsed server payload (a 400 plan-divergence body with differences becomes "The server returned status 400.") is likewise green. Ledger 26-L3 claims this test pins the branching; it pins only that the tokens exist somewhere in the file.

**Repro:** yes-ran-it, clone of fdcc202 at $R/verify-C2-v1/src; `dotnet build test/Bastet.Tests/Bastet.Tests.csproj -c Debug` -> 0 warnings; runner `test/Bastet.Tests/bin/Debug/net10.0/Bastet.Tests --filter-class Bastet.Tests.Azure.AzureWizardClientWordingTests` (cshtml read from source at run time, no rebuild between script mutations). HEAD baseline: total 4 failed 0 (runs/00-head.log). M-swap (exchange the `?` and `:` object literals at _BulkScripts.cshtml:665-666 and _ReconcileScripts.cshtml:406-407; 2 files, +4/-4): total 4 failed 0 (01-swap.log). M-dead (`payload = xhr.status === 0 && false` in both): total 4 failed 0 (02-dead.log). M-dropguard (delete the `if (!payload) {}` wrapper in both, +6/-10): total 4 failed 0 (03-dropguard.log). Control, both scripts reverted to 9d3c7b2: total 4 failed 2, both rows `Assert.Contains() Failure: Sub-string not found` at AzureWizardClientWordingTests.cs:51 (04-revert.log) — the theory detects the natural revert but none of the three branch-breaking mutants. Operator-visible behaviour of the mutants, error-handler body extracted from the cshtml and evaluated in headless Chromium 151.0.7922.34 (rig pyenv playwright) against xhr fixtures {status:0, responseText:''}, {status:500, '<html>oops</html>'}, {status:400, JSON {error:'plan changed'}}: HEAD both scripts -> ['The server could not be reached, so it is unknown whether the change was applied. Check the subnet list before retrying.', 'The server returned status 500.', 'plan changed']; M-swap both scripts -> ['The server returned status 0.', 'The server could not be reached, ...', 'plan changed'] (definite server verdict on a transport failure; false unreachable claim on a reached 500); M-dropguard -> parsed 400 body overridden by 'The server returned status 400.'. Proposed fix (the canonical regex below, plus `using System.Text.RegularExpressions;`) applied to the test and rebuilt (0 warnings): HEAD 4/4 green (10-fix-head.log); + swap failed 2 (11); + dead failed 2 (12); + dropguard failed 2 (13); + 9d3c7b2 revert failed 2 (14), all `Assert.Matches() Failure: Pattern not found` at :53; whitespace reflow of the ternary onto one line stays 4/4 green (15). beat7-B's guard-less regex variant: green at HEAD, failed 2 under swap, but 4/4 green under dropguard (20-22-B-*.log). Clone restored (git status --porcelain empty at fdcc202); audited repo porcelain empty on audit/round-27. No app instance or database started.

**Fix:** Test-only. In CommitErrorHandler_BranchesOnUnreachableServer_AndNeverAssertsServerErrorZero replace the three independent Assert.Contains at lines 51-55 with one whitespace-tolerant Assert.Matches that binds guard -> condition -> true-branch -> false-branch: `new Regex(@"if \(!payload\)\s*\{\s*payload = xhr\.status === 0\s*\?\s*\{ error: ""The server could not be reached, so it is unknown whether the change was applied\. Check the subnet list before retrying\."" \}\s*:\s*\{ error: ""The server returned status "" \+ xhr\.status \+ ""\."" \};\s*\}")`, keeping Assert.DoesNotContain("Server error: ", view). Add `using System.Text.RegularExpressions;` (not among the test project's implicit usings) or use the Assert.Matches(string pattern, string) overload and drop `new Regex`. Ship this guard-binding form, not beat7-B's variant, which does not bind `if (!payload)` and stays 4/4 green under the drop-guard mutant. Verified in the clone: green at HEAD and under a whitespace reflow; red (both rows) under the swap, the dead condition, the guard drop and the 9d3c7b2 revert. The regex pins the literal single spaces inside `{ error: "..." }`, so a reformat there fails loudly rather than passing silently. No production change. Interim: none (test-only change).

**Residue of:** 26-L3

# Info

None.

# Refuted — reported by a finder, killed by the verifier

| candidate | tag | killed because |
|---|---|---|
| — | — | no candidate was refuted this round |
