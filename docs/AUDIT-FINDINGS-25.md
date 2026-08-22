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

## L4 — 24-L4's pin is vacuous: the tri-state CheckCredential tests pin MockAzureService's own reimplementation, and the exact fixed defect reintroduced in production survives the whole suite green [x2]

**Where:** test/Bastet.Tests/Azure/AzureServiceTests.cs:67
**Breaks:** The 24-L4 fix made a zero-subscription credential report NoVisibleSubscriptions with a grant-access remedy instead of "Failed to authenticate" (product rule 3). Its claimed pins (AzureServiceTests CheckCredential_* tests) call MockAzureService.CheckCredential, whose tri-state logic is written inside the mock itself (test/Bastet.Tests/TestHelpers/MockAzureService.cs:46) — a second implementation of the production decision. Reintroducing the exact pre-fix bug in production — src/Bastet/Services/Azure/AzureService.cs:35 returning Failed instead of NoVisibleSubscriptions, or src/Bastet/Controllers/AzureController.cs:46 (and :141) treating any non-Valid result as an auth failure so the operator with a subscription-less credential is again told "Failed to authenticate. Please check your credentials." — leaves all 888 tests green. The pin cannot discriminate the fix from the bug it fixed.
**Repro:** Scratch clone of 9d3c7b2 at rig/verify-b7p2-1-refute/clone. Mutation (a): sed 'return CredentialCheckResult.NoVisibleSubscriptions;' -> 'return CredentialCheckResult.Failed;' in src/Bastet/Services/Azure/AzureService.cs (line 35); dotnet test -> "Test run summary: Passed! total: 888, failed: 0". Reverted. Mutation (b): sed 'if (credential == CredentialCheckResult.Failed)' -> 'if (credential != CredentialCheckResult.Valid)' in src/Bastet/Controllers/AzureController.cs (hit both sites, lines 46 and 141); dotnet test -> "Test run summary: Passed! total: 888, failed: 0". Rig-recorded unmutated baseline at HEAD: 888/888. Clone reverted clean afterward.
**Fix:** Pin the reachable seam: add controller-level tests that build AzureController with an IAzureService stub returning each CredentialCheckResult and assert Failed produces the "Failed to authenticate" ModelState error, NoVisibleSubscriptions produces the "cannot see any subscriptions... Grant it access" error, and Valid produces neither (MockAzureService(true) with an empty subscription list already yields NoVisibleSubscriptions, so no new double is needed). Mutation-verify both controller branches and the AzureService.cs:35 return.
**Residue of:** 24-L4

# Info

## I1 — 24-I9's rewritten NetworkInputAttribute landed with no pin: gutting its entire IsValid to always-Success survives the whole suite green [x1]

**Where:** src/Bastet/Services/Security/ValidationAttributes.cs:27
**Breaks:** 24-I9 rewrote NetworkInputAttribute.IsValid (deleting RequireValidIp and the dead else branch) with no test change anywhere in the round-24 delta. A mutant replacing the whole rejection logic with 'return ValidationResult.Success;' — the attribute validating nothing on CreateSubnetViewModel.NetworkAddress (SubnetViewModels.cs:16) and the host-IP address field (HostIpViewModels.cs:18) — leaves all 888 tests green, so the attribute layer can silently stop rejecting malformed IPs. Consequence is bounded to lost defense-in-depth and a worse error surface: SubnetValidationService.ValidateSubnetFormat (IPAddress.Parse, line 49) and HostIpValidationService.IsValidIpFormat (line 319) re-validate format downstream, so no bad address reaches the database.
**Repro:** In scratch clone at 9d3c7b2 (/tmp/.../rig/verify-b7p2-2-refute/clone), replaced the ternary return in NetworkInputAttribute.IsValid (src/Bastet/Services/Security/ValidationAttributes.cs) with 'return ValidationResult.Success;', then ran 'dotnet test': result "Passed! total: 888, failed: 0, succeeded: 888, skipped: 0" — the always-Success mutant survives the entire suite green. git diff 338e8e0..9d3c7b2 confirms 24-I9 rewrote the attribute with zero test-side changes referencing NetworkInputAttribute; grep of test/ finds no test exercising the attribute. Downstream re-validation verified: SubnetValidationService.cs line 49 (System.Net.IPAddress.Parse) and HostIpValidationService.cs line 319 (IsValidIpFormat via IPAddress.TryParse), so consequence is correctly bounded to lost defense-in-depth — no bad address reaches the database, supporting Info severity.
**Fix:** Add attribute-level pins using the existing parity-test harness (Validator.TryValidateProperty with the SanitizationServiceProvider): assert NetworkInputAttribute rejects a non-IP string with "Invalid IP address format" and accepts a valid dotted-quad, on both CreateSubnetViewModel.NetworkAddress and the host-IP view model field; mutation-verify by re-applying the always-Success mutant.
**Residue of:** 24-I9

# Refuted

| candidate title | beat | tag | reason |
|---|---|---|---|
| (none — no candidates were killed by the verifier this round) | — | — | — |
