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

## L2 — 26-L3 wording theory pins token presence, not branch binding: swapping, deadening or un-guarding the xhr.status-0 ternary in both wizard scripts stays 4/4 green while the operator gets "The server returned status 0." on a transport failure (25-L3 in new words) `[x2]` — FIXED

_Fixed in commit "Audit 27 L2" on audit/round-27. Test-only: the theory CommitErrorHandler_BranchesOnUnreachableServer_AndNeverAssertsServerErrorZero now asserts one whitespace-tolerant regex binding guard -> condition -> unreachable-sentence arm -> status arm in that order (Assert.Matches(string, string), no new using or package) and keeps Assert.DoesNotContain("Server error: "); the two wizard scripts are unchanged._
_Swept: the regex matches exactly once per script, at the commit ajax error handler (_BulkScripts.cshtml:663-667, _ReconcileScripts.cshtml:404-408); the other two theories in the file pin static wording and the single shared error partial, not a branch, so they are not the same defect._
_Verified: build 0 warnings, suite 906/906; both theory rows red under branch swap, dead condition (`=== 0 && false`), dropped `if (!payload)` guard, and both scripts reverted to 9d3c7b2; green at HEAD and under a one-line minimal-spacing reflow._
_Reviewed: PASS (independent reviewer: nine behaviour-breaking mutants all red incl. showCommitError moved inside the guard and a loosened guard; green under reindent, CRLF, tabs and spacing variants; anchored to one match per file; compiled overload confirmed from IL); non-blocking note that `if (` and `error:` are the two non-tolerant joints, both the files' standing convention._

# Info

None.

# Refuted — reported by a finder, killed by the verifier

| candidate | tag | killed because |
|---|---|---|
| — | — | no candidate was refuted this round |
