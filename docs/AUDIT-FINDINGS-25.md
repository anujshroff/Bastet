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

## L1 — Zero-subscription state still tells the operator to 'check your Azure credentials' via the client no-subscriptions panel on both wizard pages, contradicting the corrected 24-L4 banner on the same screen [x2] strings

**Where:** src/Bastet/Views/Azure/BulkImport/_StepSubscription.cshtml:36
**Breaks:** An operator whose credential authenticates but can see no subscriptions opens Bulk Import or Reconcile. The 24-L4 server banner correctly says "Signed in to Azure, but this credential cannot see any subscriptions. Grant it access to a subscription and reload this page." — but the client's GetSubscriptions call succeeds with an empty list, which unhides the panel "No subscriptions found. Please check your Azure credentials." on the very same page. That panel is reachable ONLY when authentication succeeded (a bad credential throws and takes the error branch instead), so every time it fires its remedy is the one 24-L4 was filed to remove; the operator is told two contradictory remedies at once, breaking product rule 3. Sites: src/Bastet/Views/Azure/BulkImport/_StepSubscription.cshtml:36 (shown by _BulkScripts.cshtml:69-70 empty-list branch) and src/Bastet/Views/Azure/Reconcile/_StepSubscription.cshtml:40 (shown by _ReconcileScripts.cshtml:86-88 empty-list branch). 24-L4 fixed the rule at the two server-side CheckCredential sites while the identical rule stayed wrong at these two client-side sites.
**Repro:** Ran playwright (rig venv python) against live rig app http://127.0.0.1:5225 at HEAD 9d3c7b2, intercepting **/Azure/GetSubscriptions with {"success":true,"subscriptions":[]} (the exact response AzureService.GetSubscriptions produces for an authenticated credential with zero visible subscriptions). Observed: /Azure/BulkImport -> #bulk-no-subscriptions visible=True text='No subscriptions found. Please check your Azure credentials.'; /Azure/Reconcile -> #rec-no-subscriptions visible=True, same text. Code-verified the panel is reachable only on auth success (auth failure throws in the enumeration -> controller catch -> success:false -> error branch, AzureController.cs:24-33) and that the same state renders the contradictory 24-L4 banner via ModelState on both page shells (AzureController.cs:50-53 and 145-148, rendered by BulkImport.cshtml:14 / Reconcile.cshtml:14).
**Fix:** Replace the panel text at both sites with the 24-L4 wording, e.g. "This credential cannot see any subscriptions. Grant it access to a subscription and reload this page." — two strings, no logic change.
**Residue of:** 24-L4

## L2 — The 24-L4 tri-state's Failed branch still tells the operator to check their credentials when the credential check failed for non-credential reasons (network/ARM failure), and it shadows the controller's own connectivity message [x1] strings

**Where:** src/Bastet/Services/Azure/AzureService.cs:40
**Breaks:** AzureService.CheckCredential (rewritten by 24-L4) catches every exception from the subscription enumeration and returns CredentialCheckResult.Failed (AzureService.cs:37-41), so a transport-level failure — management endpoint unreachable (e.g. an air-gapped deployment with BASTET_AZURE_IMPORT on, product section 6), DNS failure, ARM 5xx/throttling — fires "Failed to authenticate with Azure. Please check your credentials." (AzureController.cs:48 and :143), naming a remedy that changes nothing (rule 3). The controller already has a truthful branch for exactly this case ("Error connecting to Azure. Details have been logged.", AzureController.cs:58 and :153) but it is unreachable for credential checks because the service swallows the exception first — two implementations of one distinction that disagree. 24-L4 carved the zero-subscription case out of the false "authentication failed" claim but left the transport-failure case still asserting it.
**Repro:** Scratch clone + build at 9d3c7b2; app started with SP_A creds, ASPNETCORE_ENVIRONMENT=Development, BASTET_AZURE_IMPORT=true, HTTPS_PROXY=http://127.0.0.1:9, NO_PROXY=login.microsoftonline.com. curl http://127.0.0.1:5261/Azure/BulkImport -> 200 containing "Failed to authenticate with Azure. Please check your credentials."; app log "Azure credential validation failed" followed by System.AggregateException/Azure.RequestFailedException "Connection refused (127.0.0.1:9)" (transport, creds valid). Control: same app with a deliberately wrong client secret and no proxy -> /Azure/Reconcile showed the same credential message with Azure.Identity.AuthenticationFailedException (MSAL invalid_client) in the log. Both captured PIDs killed; repo untouched.
**Fix:** Reword the Failed-branch message at AzureController.cs:48 and :143 to cover both causes, e.g. "Could not authenticate with Azure. Check the credentials and that Azure is reachable from this host." — a one-string-per-site fix. Verifier note: the originally filed primary fix (in AzureService.CheckCredential return Failed only for Azure.Identity.AuthenticationFailedException and rethrow other exceptions to reach the controllers' existing connectivity catch) was judged unsound — the live repro proves that in a network-down/air-gapped deployment the failure happens at token acquisition and Azure.Identity wraps the transport error (Connection refused) in AuthenticationFailedException, so the type filter would still emit "Please check your credentials." for exactly the non-credential outage the finding leads with, while only re-routing the rarer AAD-reachable-but-ARM-unreachable case (RequestFailedException). Exception type cannot separate the two causes at this boundary; use the reworded-message fix.
**Residue of:** 24-L4

## L3 — The indeterminate-outcome commit branch 24-L5 targeted still asserts a definite failure: a mid-commit network drop shows 'Server error: 0' in both the bulk-import and reconcile-delete commit banners [x1] strings

**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:663
**Breaks:** An operator confirms a bulk import (or a reconcile delete) and the connection drops before the response arrives — the one branch where the outcome is genuinely indeterminate (the server may have committed; the server's own indeterminate path at SubnetController.BulkAzure.cs:454 says "could not confirm whether this import was applied"). jQuery's error handler fires with xhr.status 0 and an unparsable responseText, so the fallback payload { error: "Server error: " + xhr.status } paints "Server error: 0" into the banner — asserting a definite server-side error that never happened, on exactly the branch 24-L5 existed to make honest; 24-L5 removed only the static "Commit failed:" markup prefix. Sites: src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:663 and src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:404.
**Repro:** $RIG/verify-b6p2-2-refute/repro.py via $RIG/azcli/bin/python: playwright chromium drove http://127.0.0.1:5225/Azure/BulkImport through the full wizard, route.abort('connectionfailed') on **/Subnet/BulkCreateFromAzurePlan (1 request aborted, no server write); observed BANNER FULL TEXT: "Server error: 0" and ERROR MESSAGE SPAN: "Server error: 0".
**Fix:** At both sites, branch on xhr.status === 0: show an outcome-honest message ("The server could not be reached, so it is unknown whether the change was applied. Reload the subnet list to check before retrying."); for nonzero statuses keep naming the status without asserting more than is known. String-level change in the two error handlers.
**Residue of:** 24-L5

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
