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

## L1 — Tags accepted by validation are silently rewritten by SanitizeTags before persisting `[x1]` — FIXED
_Fixed in round 24. Deleted the StripHtml call from SanitizeTags' per-tag Select (trim, split, Take(10), per-tag ≤50, 255-cap all kept), so sanitizer and the 18-R7 validation rule accept identical tag text; re-pinned the script InlineData to preservation and added a rack<b12> row._
_Swept: Create and Edit share the single [SanitizeTags] path; no other write path stores operator tags (BulkAzure sets Tags null); the one render sink is Razor-encoded, zero Html.Raw, no JS inserts Tags._
_Verified: build 0/0, 882/882; both new pins red against the unfixed code; reviewer's live browser XSS probe — <script>alert(1)</script>,x stored and rendered as encoded text, no execution, operator text round-trips verbatim._
_Reviewed: independent reviewer PASS — full sink enumeration, live XSS probe, write-path parity, caps re-run._
**Residue of:** 18-R7

## L2 — Subnet Create form carries its own client-side IP arithmetic (mask/total/usable) duplicating IpUtilityService `[x2]` — FIXED
_Fixed in round 24. _SubnetFormScripts.cshtml @injects IIpUtilityService and serializes the 33 per-CIDR {mask,total,usable} triples at render; updateSubnetInfo only indexes the table; the three client functions deleted (zero IP arithmetic remains in the file)._
_Swept: the Details engine stays parked as 23-L2-remainder (deferred), not re-touched; Html.Raw input is server-generated JSON only._
_Verified: build 0/0, 882/882; reviewer's 33/33 equivalence check against the old formulas; live drive — CIDR 26/31/32/0/invalid all render identically incl. locale commas, GET-seeded cidr still server-renders then JS takes over._
_Reviewed: independent reviewer PASS._
**Residue of:** none

## L3 — Held prefix-changed reconcile rows tell the operator to delete a row the same sentence block says BASTET will not delete `[x2]` `strings` — FIXED
_Fixed in round 24. Evaluate* gained out string fact; held prefix-changed rows lead with the fact only (absence rows keep the 23-L4 listing clause); offered rows' Reasons byte-identical (fact + remedy reproduces the old strings exactly)._
_Swept: SubnetControllerReconcileApprovedVerdictTests (scan/delete reason equality) green — offered wording unchanged._
_Verified: build 0/0, 885/885; two new held-wording tests red pre-fix, green post; reviewer's revert-check reddens exactly those two._
_Reviewed: independent reviewer PASS — six offered strings diffed character-identical, held rows now state fact + the true held remedy only._
**Residue of:** 23-L4

## L4 — Valid credential with zero visible subscriptions reported as "Failed to authenticate with Azure" `[x1]` `strings` — FIXED
_Fixed in round 24. IsCredentialValid → CheckCredential returning CredentialCheckResult {Failed, NoVisibleSubscriptions, Valid}; the empty enumeration (reachable only after auth succeeded without throwing) now renders "Signed in to Azure, but this credential cannot see any subscriptions. Grant it access to a subscription and reload this page." on both pages; real failures keep the old message._
_Swept: both page actions (BulkImport, Reconcile); MockAzureService mirrors real semantics; three AzureServiceTests updated/added._
_Verified: build 0/0, 885/885; reviewer confirmed NoVisibleSubscriptions is auth-proven by construction and the remedy is operator-reachable._
_Reviewed: independent reviewer PASS._
**Residue of:** none

## L5 — Bulk import commit banner prefixes the indeterminate-outcome message with a false "Commit failed:" `[x1]` `strings` — FIXED
_Fixed in round 24. Deleted the static Commit failed: strong from _StepCommit.cshtml; the banner shows the server's self-sufficient message alone._
_Swept: fallback payloads ("The import failed.", "Server error: N") read as complete failure sentences on their own._
_Verified: build 0/0, 885/885; reviewer drove the indeterminate 500 live — banner shows the could-not-confirm sentence with no false failure headline; genuine failures still read as failures._
_Reviewed: independent reviewer PASS._
**Residue of:** none

## L6 — Reconcile review-section note claims 'The results below have been re-scanned' on the very first scan, set by JS as a constant `[x2]` `strings` — FIXED
_Fixed in round 24. Deleted the renderPlan constant assignment and the rec-rescan-note span; the static explainer prose now ends "The results below reflect Azure as of the latest scan." — tense-neutral, true on every scan._
_Swept: no rec-rescan-note reference remains anywhere._
_Verified: build 0/0, 885/885; reviewer drove a first-ever scan live — the explainer shows the neutral sentence, "have been re-scanned" appears nowhere._
_Reviewed: independent reviewer PASS._
**Residue of:** none

## L7 — Bulk import wizard loadVNets has no supersession guard: a stale VNet response repaints the current tree `[x1]` — FIXED
_Fixed in round 24. Added vnetSeq with the byte-identical guard shape to previewSeq/scanSeq: capture on entry, early-return in success/error/complete when superseded._
_Swept: loadVNets was the only re-triggerable AJAX in either wizard without the guard (loadAzureSubscriptions fires once per page)._
_Verified: build 0/0, 882/882; reviewer reproduced the finding's stale-response probe — pre-fix the held response repainted the tree and wiped ticks (STALE-MARKER rendered, 1→0); with the fix the tree is byte-identical, ticks kept, spinner not stuck._
_Reviewed: independent reviewer PASS — probe proven non-vacuous against reverted code._
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

## I1 — 23-L6 left two inline resource-id equality implementations in the planner instead of routing them through AzureResourceIdentity.IsSameResourceId `[x2]` — FIXED
_Fixed in round 24. Routed the two link-replacement refusal checks (AnnotatePrefix and the BuildPlanItem error path) through AzureResourceIdentity.IsSameResourceId, keeping both explicit IsNullOrEmpty guards per the verifier's correction — folding them would refuse every unlinked exact-match target (18-R4 adoption) since the compare is negated._
_Swept: no other inline resource-id equality remains in the planner; the deliberate Ordinal store gate in BulkAzure.cs untouched._
_Verified: build 0/0, 881/881; helper→Ordinal mutant now reddens exactly the two site-pinning casing tests (they were stranded green pre-fix), restored green; adoption suites 45/45._
_Reviewed: independent reviewer PASS — semantics bit-identical, guard-folding trap confirmed avoided, propagation proven by mutation._
**Residue of:** 23-L6

## I2 — Locking-service registration dispatches on a provider that is constant; sqlite and default arms are dead dispatch `[x1]` — FIXED
_Fixed in round 24. Replaced the provider switch with a direct SqlServerSubnetLockingService registration; SqliteSubnetLockingService stays as the test double (live consumer: SubnetRaceConditionTests)._
_Verified: build 0/0, 881/881; reviewer round-tripped a create through the new registration against real sp_getapplock._
_Reviewed: independent reviewer PASS — UseSqlServer is the only provider, identical services resolved._
**Residue of:** none

## I3 — Inert client-JS scaffolding: empty handlers and vacuous re-assignments in three views `[x2]` — FIXED
_Fixed in round 24. Deleted the empty submit listener + form lookup (HostIp Create), the empty @section Scripts (Subnet Index), and the three vacuous initializeForm assignments; the placeholder moved into the markup (one implementation) and its JS assignment deleted._
_Swept: the surviving ipInput input listener and updateSubnetInfo path intact; layout renders Scripts with required:false so the dropped empty section is safe._
_Verified: build 0/0, 881/881; reviewer loaded /Subnet/Create live — help text, max attr, placeholder all render from markup, create round-trips._
_Reviewed: independent reviewer PASS — deleted assignments duplicated markup verbatim._
**Residue of:** none

## I4 — Write-only subscription/rename echo properties on both plan view models, plus the parameter and payload chain that exists only to feed them `[x2]` — FIXED
_Fixed in round 24. Deleted the five write-only plan properties and their stamps, BulkImportSelectionDto.SubscriptionId/.SubscriptionName, the subscriptionName parameter from IAzureReconciler/AzureReconciler/AzureController.ReconcileScan (and the null arg in BulkDeleteStaleAzureSubnets), both clients' selectedSubscriptionName variables and payload fields; kept the DTO RenameMatchedBastetSubnets (read by BuildPlan), the bulk client's selectedSubscriptionId, the reconcile POST's subscriptionId, and AzureReconcileDeleteDto.SubscriptionId._
_Swept: reviewer-caught orphan — the subscription dropdown options' data-name stamps (both wizards) whose only reader was the deleted variable — also deleted; the live subnet-checkbox data-name pair kept; test builders updated (two AzureReconcileDeleteDto initializers were over-stripped by the sweep and restored)._
_Verified: build 0/0, 881/881; wire payloads captured live — selection POST carries exactly vNetPrefixes+renameMatchedBastetSubnets, scan POST exactly subscriptionId._
_Reviewed: independent reviewer PASS — zero surviving reads in any casing, kept members' readers intact, full live drive of both wizards (discovery→preview→commit created a row; scan succeeded)._
**Residue of:** none

## I5 — ValidateParentCanHaveChildSubnets ignores its parentId parameter entirely, and its 'hostIps = null' default has no caller `[x2]` — FIXED
_Fixed in round 24. Signature collapsed to ValidateParentCanHaveChildSubnets(IEnumerable<HostIpAssignment> hostIps) on interface and implementation; single caller updated; null-guard dropped (the caller's EF collection is initialized)._
_Verified: build 0/0, 881/881._
_Reviewed: independent reviewer PASS — exactly 3 sites, non-null argument proven._
**Residue of:** none

## I6 — Dead data-ip-version attribute on the Create Subnet button in the unallocated-ranges table — no reader has ever existed `[x1]` — FIXED
_Fixed in round 24. Deleted the data-ip-version="4" attribute._
_Verified: build 0/0, 881/881; grep ip-version → zero hits; the click handler's three read attributes intact._
_Reviewed: independent reviewer PASS._
**Residue of:** none

## I7 — ExecuteWithSubnetLockAsync's optional timeout parameter is never passed by any caller in the entire solution `[x1]` — FIXED
_Fixed in round 24. Removed the TimeSpan? timeout parameter from the interface, both implementations (timeoutMs now const DEFAULT_TIMEOUT_MS), and both test doubles; no call site changed._
_Verified: build 0/0, 881/881; all 13 call sites single-argument; SqlServer remainingMs math unchanged; lock behaviour proven live via sp_getapplock round-trip._
_Reviewed: independent reviewer PASS._
**Residue of:** none

## I8 — Unread 'prefix' local in the round-21 other-VNet-linked zero-work test drops the prefix-status assertion its sibling makes `[x1]`
**Where:** test/Bastet.Tests/Azure/AzureBulkImportZeroWorkTests.cs:141
**Breaks:** AWholePrefixSubnetOverAFullyAllocatedRowLinkedToAnotherVNet_IsNotBadgedAlreadyImported assigns 'prefix = Annotate(...)' and never reads it — the only IDE0051/52/59/60 hit in the solution. The sibling test (:124) asserts prefix.Status; this one asserts only the child row, so the 21-L1 prefix-level behavior (Blocked, not AlreadyImported) is unpinned by the very test covering it.
**Repro:** Verified: blame → all lines 85b1819c (the 21-L1 fix commit); adding the two prefix assertions in a scratch copy → filtered dotnet test 1/1 Passed.
**Fix:** Prefer asserting over deleting: add Assert.Equal(BulkImportAvailability.Blocked, prefix.Status) and Assert.False(prefix.IsSelectable) so the local is read and the 21-L1 prefix half is pinned (verified green).
**Residue of:** 21-L1

## I9 — NetworkInputAttribute's non-RequireValidIp branch and its 'false' default are production-dead — every application passes RequireValidIp = true `[x1]` — FIXED
_Fixed in round 24. Deleted the RequireValidIp property and the dead else branch; the attribute always validates as an IP; the two applications dropped the redundant RequireValidIp = true._
_Verified: build 0/0, 881/881; reviewer POSTed invalid NetworkAddress live → the attribute's validation error still fires._
_Reviewed: independent reviewer PASS — zero RequireValidIp refs, both applications behaviour-identical._
**Residue of:** none

# Refuted

| title | beat | reason |
|---|---|---|

(none)
