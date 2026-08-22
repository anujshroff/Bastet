# Bastet — Round-25 Audit Findings

Branch: audit/round-25
Baseline HEAD: 9d3c7b2
Test baseline: 888 passing
Date: 2026-08-21
Scope: Regression-only round over the round-24 delta (338e8e0..9d3c7b2).
Round 25 filed 5 findings, of which 5 are residue of round 24's own fixes.

# Critical

(none)

# High

(none)

# Medium

(none)

# Low

## L1 — Zero-subscription state still tells the operator to 'check your Azure credentials' via the client no-subscriptions panel on both wizard pages, contradicting the corrected 24-L4 banner on the same screen [x2] strings — FIXED

_Fixed in the strings batch commit. Both panels now read "This credential cannot see any subscriptions. Grant it access to a subscription and reload this page." — the 24-L4 wording; no logic change._
_Swept: repo-wide grep for the old wording and the remedy concept ("check your credentials", "no subscriptions"); only the two panel sites existed._
_Verified: playwright drive intercepting GetSubscriptions with an authenticated empty list on both /Azure/BulkImport and /Azure/Reconcile shows the new text; build 0 warnings, 900/900 tests._
_Reviewed: PASS — reviewer re-drove empty and missing-array payloads on both pages, confirmed success:false takes the error branch so auth failure cannot reach the panel; noted the pre-existing, untouched _armClient==null branch as the only theoretical residue, co-displayed with the truthful server banner._
_Residue of: 24-L4._

## L2 — The 24-L4 tri-state's Failed branch still tells the operator to check their credentials when the credential check failed for non-credential reasons (network/ARM failure), and it shadows the controller's own connectivity message [x1] strings — FIXED

_Fixed in the strings batch commit. Both controller Failed-branch messages now read "Could not authenticate with Azure. Check the credentials and that Azure is reachable from this host."; the audit-verifier-rejected exception-type-filter fix was not implemented — AzureService's catch-all is unchanged._
_Swept: repo-wide grep; the old message existed only at AzureController.cs:48 and :143; no other site asserts authentication failure for this branch._
_Verified: new AzureCredentialBannerTests pin the exact wording on both page actions (mutation: reverting the wording reds exactly 2 tests); reviewer live-proved both firing cases — severed network (proxy to a dead port) and garbage client secret — each rendering the new banner truthfully._
_Reviewed: PASS — both clauses live-demonstrated true, no exception filter present, catch-branch message coexists without contradiction._
_Residue of: 24-L4._

## L3 — The indeterminate-outcome commit branch 24-L5 targeted still asserts a definite failure: a mid-commit network drop shows 'Server error: 0' in both the bulk-import and reconcile-delete commit banners [x1] strings — FIXED

_Fixed in the strings batch commit. Both commit error handlers now branch on xhr.status === 0: unreachable-server shows "The server could not be reached, so it is unknown whether the change was applied. Check the subnet list before retrying."; unparsable nonzero responses show "The server returned status N." — no more asserted failure on the indeterminate branch; parsable server payloads untouched._
_Swept: grep of both wizard script files; no other "Server error: N" fallback remains, and the remaining generic handlers guard read-only fetches, not writes._
_Verified: playwright full-wizard drives — route.abort shows the could-not-be-reached message; fulfil 500/502 with unparsable bodies shows "The server returned status 500./502."; parsable 400 error payload rendered verbatim once._
_Reviewed: PASS — reviewer re-drove abort/500/502, confirmed the reconcile handler byte-identical and null-safe, the subnet-list remedy truthful for both flows, and all rendering via .text()._
_Residue of: 24-L5._

## L4 — 24-L4's pin is vacuous: the tri-state CheckCredential tests pin MockAzureService's own reimplementation, and the exact fixed defect reintroduced in production survives the whole suite green [x2] — FIXED

_Fixed. New test/Bastet.Tests/Azure/AzureCredentialBannerTests.cs pins the reachable seam: BulkImport() and Reconcile() each driven through Failed / NoVisibleSubscriptions / Valid, asserting the exact page ModelState wording (and the absence of the old "Please check your credentials" phrase) or its absence._
_Swept: reviewer confirmed no plausible controller-seam reintroduction stays green — branch-flip, message-swap, and branch-deletion mutants all red._
_Verified: 900/900 green; controller !=Valid mutant reds exactly 2 tests, wording-revert mutant reds exactly 2._
_Reviewed: PASS — reviewer re-ran both mutants and verified the gap claim; one correction adopted (Dispose now clears BASTET_AZURE_IMPORT like its siblings)._
_Not done: the AzureService.cs:35 production return itself stays unpinned — AzureService wraps a concrete AzureArmClientProvider/ArmClient with no injection seam, so a unit pin needs a production change out of scope for a test-only fix; that seam is covered only by the live rig._
_Residue of: 24-L4._

# Info

## I1 — 24-I9's rewritten NetworkInputAttribute landed with no pin: gutting its entire IsValid to always-Success survives the whole suite green [x1] — FIXED

_Fixed. Both parity-test harnesses gained attribute-layer pins: malformed inputs ("not-an-ip", "10.0.0.999") rejected with the attribute's own message and a dotted-quad accepted, on CreateSubnetViewModel.NetworkAddress and CreateHostIpViewModel.IP — the attribute's only two live inputs (the Edit view models deliberately carry no NetworkInput)._
_Swept: grep confirmed no other property carries [NetworkInput]; AzureImportSubnetViewModel inherits the pinned property._
_Verified: 900/900 green; always-Success mutant reds exactly the 4 new reject cases; reviewer's subtler mutants (inverted ternary, reject-only-empty) also caught._
_Reviewed: PASS — reviewer confirmed the malformed inputs exercise the attribute itself (survive Required and sanitization) through the production GetService path._
_Residue of: 24-I9._

# Refuted

| candidate title | beat | tag | reason |
|---|---|---|---|
| (none — no candidates were killed by the verifier this round) | — | — | — |
