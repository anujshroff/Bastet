# Bastet Audit Ledger

Append-only record of every audit round and every finding verdict. This file — not commit history —
is the loop's memory: main is squash-merged, so per-finding commits and messages do not survive to
it. The ledger is written at reconcile close-out and read by the next round's briefing agent (for
residue attribution by finding id) and by reconcile triage (for prior verdicts on anything
re-derived).

Rules:

- **Facts only.** A row carries what happened, never why anyone believed it. Reasoning inherited
  from old rounds is what poisoned the deleted findings files; the ledger structurally must not
  hold an argument.
- **Append-only.** Rows are added when a verdict is terminal (fixed / refuted / struck / inverted /
  deferred), never edited. A deferred finding gets a second row when it is later closed.
- Findings still being worked live in `docs/AUDIT-FINDINGS-<N>.md`, not here.

## Rounds

Per-finding identity before round 18 did not survive the squash merges; those rounds are recorded
at round granularity only.

| round | filed | fixed | refuted | residue of prior round | merge sha | note |
|---|---|---|---|---|---|---|
| 4 | ~45 | ~45 | — | — | bf120d6 | first skill-driven round; ~half dead-code removals |
| 5 | 14 | 14 | 18 | — | 8cefc64 | |
| 6 | 18 | 18 | 7 | — | 0de1293 | migration-lock fix reverted next PR (73f68ac) |
| 7 | 13 | 13 | 7 | — | ff285cf | |
| 8 | 7 | 7 | 1 | — | a8f669b | |
| 9 | 8 | 8 | 0 | — | dcc15ab | |
| 10 | 9 | 9 | 2 | — | 09cee3d | |
| 11 | 5 | 5 | 14 | — | e03ae51 | |
| 12 | 4 | 4 | 8 | — | 78fc4c9 | |
| 13 | 3 | 3 | 6 | — | 6d1a4cb | owner re-rated round 6's F2 medium→high after 4 rounds unpriced |
| 14 | 9 | 9 | 6 | — | 8afa2df | began the withhold widening later reverted |
| 15 | 16 | 16 | 3 | — | 65d1fc6 | AzureReconciler.cs peaked at 911 lines (baseline 203) |
| 16 | 15 + 20 re-audit | 35 | 7 | 11 of 15 | d18327e | first measured residue; deleted findings files 3–15 |
| — | — | — | — | — | 23233f2 | mass revert: rounds 14–16 withhold/re-link/inbound machinery and single-VNet wizard deleted |
| 17 | 21 | 21 | 3 | 12 of 21 | f4a0a87 | |
| 18 | 23 | 17 | 2 | 20 of 23 | f2050fa (#177) | first round under the rebuilt skill: every fix independently reviewed; whole-diff gate filed 1 violation (repaired) + 1 recorded product question; 6 findings deferred to the planner/wizard restructure |
| 19 | 4 | 4 | 0 | 4 of 4 | 6a21950 (#178) | regression-only round over the round-18 delta; all four findings were regression-guard gaps in round-18's own tests, all mutation-verified, all fixed; gate: zero diff-review findings, 41/41 rig checks, both Azure counter-tests both directions |
| 20 | 9 | 9 | 0 | 9 of 9 worked (1 regression-guard residue of round 19 + the 8 round-18 deferred findings, closed in-round by owner instruction) | 6fb8557 (#179) | regression-only round over the round-19 delta (one finding), then the owner folded the entire round-18 deferred-findings queue into the round: 5 fix batches, every fix independently reviewed (2 review-demonstrated failures each repaired and re-reviewed), whole-diff gate filed 1 violation (repaired, re-reviewed), full browser drive of every changed wizard behavior, both Azure counter-tests both directions; DEFERRED-FINDINGS.md emptied and deleted; this is a large unaudited delta — **the next audit runs Regression-only over it** |
| 21 | 4 | 4 | 0 | 4 of 4 (structural: regression-only scope) | 85b1819 (#180) | regression-only round over the round-20 delta; 1 wizard-badge regression (18-R4 residue) + 3 regression-guard gaps (18-R10, 18-R3-rem ×2), all Low, all mutation-verified; every fix independently reviewed; whole-diff gate filed 1 finding (repaired, re-reviewed); 32/32 rig checks across app sweep and Azure end-to-end, both counter-tests both directions; residue count 4 > 2 — **the next audit runs Regression-only over this delta** |
| 22 | 2 | 2 | 0 | 2 of 2 (structural: regression-only scope) | adebf6f (#181) | regression-only round over the round-21 delta; both findings regression-guard gaps (21-L1 casing term, 21-L3 silent direction), fixes test-only, every mutant re-driven at the gate; every fix independently reviewed; whole-diff gate: zero findings on both lenses; 32/32 rig checks (17 app sweep + 15 Azure end-to-end), both counter-tests both directions; residue count 2 is not above the gate threshold — **discovery reopens next round** |
| 23 | 17 | 16 | 1 | 2 of 17 (18-R21, 18-R10) | 338e8e0 (#183) | first full Standard discovery round since round 18 (residue reopened at round 22); 8 beats × 2 passes + deep sweep on beat 6, 28 raw → 18 merged → 17 survived + 1 killed by the audit verifier (23-refuted-a); at reconcile 16 fixed + 1 refuted (23-I1); 1 finding split (L2: wizard kernel fixed, Details-engine remainder deferred to DEFERRED-FINDINGS.md); every fix independently reviewed (all PASS, incl. live browser + Azure drives); both whole-diff gate lenses GATE-CLEAN; live-rig gate: all core areas + I10 pages render, security headers on normal+error, log clean, both Azure counter-tests both directions; residue 2 of 17 is low and not above the gate threshold — **discovery stays open next round** |
| 24 | 19 | 19 | 0 | 7 of 19 (23-L4 ×2, 23-L6 ×2, 23-L1, 18-R7, 21-L1) | 9d3c7b2 (#184) | second Standard discovery round; 39 raw → 19 merged (9 x2) → 19 survived, 0 killed by the audit verifier; at reconcile 19 fixed, 0 refuted, 0 deferred; dominant residue class: round-23 wording/equality fixes left without permanent pins (closed by 4 mutation-verified test-only pins); every fix independently reviewed (all PASS incl. live XSS probe on the tags fix and a stale-response race repro); whole-diff gate both lenses GATE-CLEAN; live-rig gate: all areas render, security headers on 200s, logs clean, both Azure counter-tests both directions incl. the new held-row wording; residue 7 > 2 — **the next audit runs Regression-only over this delta** |
| 25 | 5 | 5 | 0 | 5 of 5 (structural: regression-only scope; 24-L4 ×3, 24-L5, 24-I9) | f8bd697 (#185) | regression-only round over the round-24 delta; 3 strings-class wording fixes (one batch commit) + 2 test-only pin fixes; audit verifier killed 0 candidates; every fix independently reviewed (all PASS, incl. live transport-failure and bad-credential drives; one reviewer hygiene correction adopted); whole-diff gate both lenses GATE-CLEAN, zero repair iterations; live-rig gate: all areas render with security headers, logs clean, both Azure counter-tests both directions (RBAC-hidden rows withheld with a warning naming them; deleted VNet still offered and archived); the AzureService.cs:35 tri-state return remains unpinnable without a production seam (recorded in 25-L4); residue 5 > 2 — **the next audit runs Regression-only over this delta** |
| 26 | 3 | 3 | 0 | 3 of 3 (structural: regression-only scope; 25-L1 ×2, 25-L3) | 5a9caa2 (#186) | regression-only round over the round-25 delta; mid-round owner rulings (PRODUCT-MODEL §8, round 26) reshaped the loop: zero-finding rounds are the terminal state, test gaps close at fix time (pin or ledger row + /e2e record), recorded gaps never re-filed — both skills and §5 updated in the round's base commit; 1 production fix (grant-access panel fired on the no-credential branch; GetSubscriptions null branch now fails like its siblings, browser-verified both wizards) + 2 test-only pins closing 25-L1/25-L3's unpinned client surfaces; every fix independently reviewed (all (c) acceptable); whole-diff gate both lenses GATE-CLEAN, zero repairs; live-rig gate: 14/14 app sweep with headers, logs clean (403 warns are the counter-test by design), Azure end-to-end both counter-tests both directions (RBAC-hidden rows withheld with a warning naming them; deleted VNet still offered and archived); residue 3 > 2 — **the next audit runs Regression-only over this delta** |
| 27 | 2 | 2 | 0 | 1 of 2 (26-L3; the other is 24-L4 residue) | 1d4c81b (#188) | regression-only round over the round-26 delta (gate: round 26 residue 3 > 2); 4 raw → 2 merged (both x2) → 2 survived, 0 killed by the audit verifier; 1 production fix (two wizard error-banner partials collapsed into one shared Views/Azure/_ErrorAlert, deleting the reconcile page's static "Could not connect to Azure." headline; source-content pin) + 1 test-only fix (26-L3 wording theory now binds the xhr.status-0 branching; mutation-verified); every fix independently reviewed (both PASS, one non-blocking note adopted and mutation-verified); whole-diff gate both lenses GATE-CLEAN, zero repair iterations; live-rig gate: 28/28 app-area content assertions with security headers, logs 0 fail (warns: withheld-row 403s by design, pre-existing EF include notice, Development DataProtection notice), Azure end-to-end both counter-tests both directions (RBAC-hidden rows withheld with a warning naming them; deleted VNet offered and archived); round-26 residue 1 is not above the gate threshold — **discovery reopens next round** |
| 28 | 12 | 12 | 0 | 4 of 12 (23-L2-remainder ×3: L9, L11, L12; round 17 ×1: L3) | 5ea2eb5 (#190) | first Standard discovery round since round 24 (gate reopened at round 27); 8 beats × 2 passes + 2 deep sweeps on beat 6, 20 raw → 15 merged (4 x2) → 12 survived (all Low) + 3 refuted by the audit verifier (28-refuted-a/b/c, dead-code Infos struck under the round-26 rulings); at reconcile 12 fixed, 0 refuted, 0 deferred, 0 struck; every fix independently reviewed (all (c) acceptable, one wording correction adopted); whole-diff gate both lenses GATE-CLEAN, zero repair iterations; live-rig gate: 23/23 app-area content assertions with security headers on 200 and 404, logs 0 fail (warns: EF split-query notice, Development DataProtection notice, the deliberate 403 probe), Azure end-to-end both counter-tests both directions (hidden principal withholds visible-group rows with warnings naming them; deleted VNet offered and archived); 3 of the 4 residue findings came out of the round-27-to-28 delta (#189, the 23-L2 remainder cleanup), the fourth from a round-17 client string; residue 4 > 2 — **the next audit runs Regression-only over this delta** |
| 29 | 5 | 5 | 0 | 5 of 5 (structural: regression-only scope; 28-L6, 28-L2, 28-L10, 28-L4, 28-L9) | — | regression-only round over the round-28 delta (gate: round 28 residue 4 > 2); beats 6 and 7 × 2 passes, 7 raw → 5 merged (2 x2) → 5 survived, 0 refuted, every candidate reproduced by two independent verifiers (three for L3) with the mutated build driven live; all five were test-only pin gaps in round-28 tests, no product defect in the delta (two ephemeral owner-run audits of the same delta also found nothing); at reconcile 5 fixed, 0 refuted, 0 deferred (L3/L5 struck at triage under a since-replaced §5 clause, reversed by the owner and fixed); L1/L2/L4 independently reviewed (L1 and L4 revised once after demonstrated failures), L3/L5 taken on the audit verifiers' validation of the identical assertions without a separate reconcile reviewer; whole-diff gate: 1 correctness-lens and 7 product-model-lens doc-text findings, one repair iteration, zero code findings; live-rig gate: 28/28 app-area content assertions with security headers on 200 and 404, logs 0 fail, Azure end-to-end both counter-tests both directions (hidden principal withholds visible-group rows with a warning naming them; deleted VNet offered and archived; prefix change reported); owner rulings recorded in PRODUCT-MODEL §8: the audit files product defects only and never reads anything under test/ (absolute, superseding), the regression-tests beat deleted; residue 5 > 2 — **the next audit runs Regression-only over this delta**, beat 6 only |
## Findings

Per-finding records begin at round 18. `id` is `<round>-<finding>` and is the identifier
`Residue of:` fields cite from round 19 onward.

The round-18 rows' sha column holds branch-local commits that did not survive the squash merge —
they identify which one-commit-per-finding change each row describes, but resolve nowhere; every
round-18 change reached `main` in `f2050fa` (#177). From round 19 on, new rows carry no sha — the
round number is the durable key (`git log main --grep "Audit <N>"` finds the merge), and each
reconcile close-out backfills the *previous* round's Rounds-table cell with its merge sha and PR
number once they exist.

| id | severity | verdict | sha | what |
|---|---|---|---|---|
| 18-refuted-a | — | refuted | 7b75c6e | Free-space warning naming Bulk Azure Import on Azure-subnet-linked rows: load-bearing claim false; wizard demonstrated importing under such a row |
| 18-refuted-b | — | refuted | 7b75c6e | Migration sp_getapplock falling back to master: EF Core's own __EFMigrationsLock serialises replicas regardless; consequence not reproducible |
| 18-R1 | High | fixed | 172d4aa | Deleted the cross-subscription and confirmed-still-live cascade withholds that pinned a deleted row |
| 18-R2 | Medium | fixed | 23bc286 | Delete-scope guard now counts subtree host IPs instead of a wall-clock watermark; open product question: refuse on any churn via IP-set? |
| 18-R3 | Medium | fixed | b34496c | Narrow half: concurrency retry fails closed (token no longer refreshed); redisplay-path collapse deferred |
| 18-R3-rem | Medium | deferred | 8a7a6e3 | Redisplay-path collapse + field-diff message, in DEFERRED-FINDINGS.md |
| 18-R4 | Medium | deferred | 8a7a6e3 | Hand-built fully-allocated row adoption; selectability slice |
| 18-R5 | Medium | deferred | 8a7a6e3 | Linked target with host IPs rename asymmetry; selectability slice |
| 18-R6 | Low | deferred | 8a7a6e3 | Second child-naming implementation; naming slice |
| 18-R7 | Low | fixed | 103ecf2 | Tags accept ordinary operator text; only count/length limits remain |
| 18-R8 | Low | fixed | 4349ee2 | No Add Child Subnet on /32; gate predicates collapsed to one property |
| 18-R9 | Low | deferred | 8a7a6e3 | Rename toggle discards selection; first pull-forward candidate |
| 18-R10 | Low | deferred | 8a7a6e3 | Client re-derives IsSelectable; selectability slice headline |
| 18-R11 | Low | deferred | 8a7a6e3 | TargetName duplicate reading client-supplied prefixes; naming slice |
| 18-R12 | Low | fixed | 6d31bc7 | CIDR modal relabelled Maximum usable IPs |
| 18-R13 | Low | fixed | 6d31bc7 | Fully-allocated refusal made cause-free, names the Details remedy |
| 18-R14 | Low | fixed | ae8d636 | Flag-off free-space warning closes as prose |
| 18-R15 | Low | fixed | 6d31bc7 + 6343448 | CIDR refusal names one remedy, gated on the import feature flag (gate repair) |
| 18-R16 | Low | fixed | c64ff85 | Import wizard named only when it can import (no-IPv4 branches) |
| 18-R17 | Low | fixed | c64ff85 | Unrecognised-link reason ends at what is true; no unlink control invented |
| 18-R18 | Low | fixed | 6d31bc7 | Blocked-by-prefix fallback worded for both firing cases |
| 18-R19 | Low | fixed | 682e436 | Commit banner reports renamed child subnets; summary dedup deferred |
| 18-R19-rem | Low | deferred | 8a7a6e3 | Server-built summary field replacing the client-assembled sentence |
| 18-R20 | Info | fixed | efc8acf | Structurally-zero host-IP cascade count deleted |
| 18-R21 | Info | fixed | d56bea5 | Orphaned SafeTextAttribute deleted; tests re-pinned to NoHtml |
| 18-R22 | Info | fixed | e6eef15 | Unread reconcile CanCommit deleted |
| 18-R23 | Info | fixed | 3d1d427 | Ignored parameter and unread ParentSubnetId removed from the tree view model |
| 19-S1 | Low | fixed | — | Delete-scope counter-test gained a re-review-then-delete second act; a watermark reland now goes red, making the §8 18-R2 claim true |
| 19-S2 | Low | fixed | — | HostIp edit's fail-closed concurrency redisplay gained its sibling stale-token test |
| 19-S3 | Low | fixed | — | Fully-allocated toggle gate lifted into CanMarkFullyAllocated; vacuous test re-pointed, every term mutation-load-bearing |
| 19-S4 | Low | fixed | — | CIDR-refusal test pins both feature-flag branches explicitly, serialized in AzureFeatureFlagCollection |
| 20-S1 | Low | fixed | — | ChildSubnets terms of both Details action gates (CanMarkFullyAllocated and the wholly uncovered CanAddHostIp) pinned by mutation-proven tests; 19-S3's every-term-load-bearing claim was untrue for the ChildSubnets term |
| 18-R4 | Medium | fixed | — | Unlinked fully-allocated exact match now offered for link-only adoption (WillUpdateExisting); refusal no longer keys on provenance |
| 18-R5 | Medium | fixed | — | Linked host-IP target now AlreadyImported with rename offered; creation inside still refused; gate repair restored the unlinked-target plan refusal |
| 18-R10 | Low | fixed | — | isPrefixUsable deleted; client consumes server-computed CanCarrySubnetWork and RenameOnlyCandidate; subnets under fully-allocated/host-IP containers Blocked server-side |
| 18-R6 | Low | fixed | — | Child names and rename offers computed by one method from the subnet's own ARM prefix list (Ipv4AddressPrefixes now on the selection DTO); phantom multi-prefix rename offers gone |
| 18-R11 | Low | fixed | — | TargetName deleted; ProposedTargetName is the single target-naming implementation for annotation and plan |
| 18-R9 | Low | fixed | — | rerenderPreservingSelection extracted and bound to both wizard toggles; selection survives all flips (browser-verified) |
| 18-R3-rem | Medium | fixed | — | Edit concurrency catch redisplay deleted; one unified message names each differing stored value; HostIp sibling message aligned |
| 18-R19-rem | Low | fixed | — | Commit response carries the server-built summary; banner set from it; client-assembled sentence deleted |
| 21-L1 | Low | fixed | — | Fully-allocated encompassed branch link-gates AlreadyImported again; unlinked or other-VNet-linked target now Blocked, badge and hidden-row summary truthful |
| 21-L2 | Low | fixed | — | CanCarrySubnetWork and both RenameOnlyCandidate pinned by whole-domain tests, six mutants verified red |
| 21-L3 | Low | fixed | — | Description/Tags/CIDR concurrency-message clauses pinned, including per-field empty-wording attribution added as the whole-diff gate repair |
| 21-L4 | Low | fixed | — | HostIp Edit DbUpdateConcurrencyException catch message pinned via a save-throwing context driving the real catch path |
| 22-L1 | Low | fixed | — | Case-insensitive Azure resource-id equality pinned at IsSameVNet and both inline compares; two tests, three Ordinal mutants each red; no production change |
| 22-L2 | Low | fixed | — | Silent direction of the Description and CIDR concurrency-message clauses pinned with absence asserts mirroring the Name/Tags pins |
| 23-refuted-a | — | refuted | — | Bulk import marks a target fully allocated without the base ValidateSubnetCanBeFullyAllocated validation: the write is not unvalidated — commit re-runs the planner and refuses unless plan.CanCommit, and WillMarkFullyAllocated is set only after the planner refuses children and host IPs; no reachable failure exists and the finding admitted it ("none exists at HEAD") — killed by the audit verifier |
| 23-M1 | Medium | fixed | — | Client CSS/JS loaded only from cdn.jsdelivr.net; vendored bootstrap 5.3.8 / bootstrap-icons 1.13.1 / jquery 4.0.0 / jquery-validation 1.21.0 / -unobtrusive 4.0.0 into wwwroot/lib (sha384 matched the tag integrity) and repointed all six tags; air-gapped browser drive: zero external requests, client layer works |
| 23-L1 | Low | fixed | — | noLongerStale reconcile-delete 409 headline reworded from "no longer reported as deleted in Azure" (false for NotVisible/Unknown/cascade-withhold) to "no longer offered for deletion by the latest re-check" |
| 23-L2 | Low | fixed | — | Wizard client re-derived which VNet prefix each Azure subnet belongs to (ipToInt/prefixContainsCidr); server now stamps BulkAzureSubnetViewModel.ContainingPrefixes via the single IsSubnetContainedInParent, client groups by it, the two client fns deleted; live wizard drive confirmed correct grouping/import |
| 23-L2-remainder | Low | deferred | — | Details CIDR-modal free-standing client IP engine (no server counterpart) → docs/DEFERRED-FINDINGS.md; structural restructure, not smuggled into the reconcile round |
| 23-L3 | Low | fixed | — | Empty-listing reconcile warning contradicted the withheld (0-row) result on the same screen; BuildPlan records InventoryWasEmpty, ApplyConfirmations emits the warning only when surviving rows remain |
| 23-L4 | Low | fixed | — | Held-by-manual-content rows asserted RBAC-hidden live resources "no longer exists in Azure"; absence-derived held rows now say "could not be found in this subscription's listing", confirmed-Deleted plan.Items wording unchanged |
| 23-L5 | Low | fixed | — | Rename toggle label "to VNet names" was false for child subnets; changed to "to their Azure names" (covers both target and child rename) |
| 23-L6 | Low | fixed | — | Azure resource-id case-insensitive equality inlined at ~12 sites; single AzureResourceIdentity.IsSameResourceId + IdComparer, all joins routed through it, 22-L1 casing test now pins every caller |
| 23-I1 | Info | refuted | — | "BuildPlan cascade-withhold is dead machinery" — false: deleting it reddens two tests pinning the Rule-2 manual-content ancestor-withhold at the BuildPlan boundary; belt-and-suspenders on the correct axis, not wrong-axis machinery |
| 23-I2 | Info | fixed | — | BASTET_AZURE_IMPORT flag re-parsed inline in _Layout.cshtml and _UnallocatedRanges.cshtml; both now call AzureController.IsAzureImportEnabled() |
| 23-I3 | Info | fixed | — | Deleted the unreachable showScanError #rec-rescan-note assignment (note lives inside the d-none'd scan-content the failure path never unhides) |
| 23-I4 | Info | fixed | — | Deleted the CIDR-modal childSubnets.find(s => s.id === parentId) dead lookup (number-vs-string strict-eq never matched); parentBoundaries derived from startingAddress directly |
| 23-I5 | Info | fixed | — | Deleted production-dead IsSafeText + SafeTextPattern (no caller since 18-R21 removed SafeTextAttribute); relocated the safe-text oracle to test/Bastet.Tests/SafeTextOracle.cs |
| 23-I6 | Info | fixed | — | Deleted orphaned single-VNet-wizard test scaffolding in AzureControllerTests (ControllerWith/Parse/ControllerWithSubnets + unread JsonResponse.vnets/.subnets) |
| 23-I7 | Info | fixed | — | Deleted the dead VNetB constant in AzureBulkImportSelectabilityTests (used-VNetB in other files untouched) |
| 23-I8 | Info | fixed | — | Deleted the write-only _sanitizationService field in three test classes (dead since the global-sanitization-filter refactor #44) |
| 23-I9 | Info | fixed | — | Deleted the unused 3-arg CalculateUnallocatedRanges overload (delegated to the 4-arg with []); 11 test sites pass [] explicitly; product question recorded, not blocking |
| 23-I10 | Info | fixed | — | Deleted 13 view-model properties mapped every request but never rendered (OriginalId/OriginalParentId, ModifiedBy, deleted-host-IP Id/OriginalSubnetId/CreatedBy/LastModifiedAt/ModifiedBy); Razor compilation + live drive confirmed nothing rendered |
| 24-L1 | Low | fixed | — | SanitizeTags no longer strips HTML from tags the 18-R7 validation accepted; operator text round-trips verbatim, Razor-encoded at the one sink; pins re-pointed to preservation |
| 24-L2 | Low | fixed | — | Subnet Create form's third client IP engine deleted; the view serves a 33-entry per-CIDR {mask,total,usable} table computed by IpUtilityService, client only indexes it |
| 24-L3 | Low | fixed | — | Held prefix-changed reconcile rows lead with the fact only (Evaluate* out fact); offered rows byte-identical; the withheld remedy no longer named |
| 24-L4 | Low | fixed | — | CheckCredential tri-state (Failed/NoVisibleSubscriptions/Valid); zero-subscription credential now told to grant access, not that auth failed |
| 24-L5 | Low | fixed | — | Static "Commit failed:" prefix deleted from the bulk-commit banner; the indeterminate-outcome message no longer asserts a definite failure |
| 24-L6 | Low | fixed | — | First-scan "have been re-scanned" constant deleted; static explainer reads "The results below reflect Azure as of the latest scan." |
| 24-L7 | Low | fixed | — | loadVNets gained the vnetSeq supersession guard (same shape as previewSeq/scanSeq); stale VNet responses can no longer repaint the tree |
| 24-L8 | Low | fixed | — | noLongerStale 409 headline pinned both directions (contains the true wording, refutes the reverted one) |
| 24-L9 | Low | fixed | — | 23-L4's subnet-level listing clause pinned; reverting the SubnetDeleted branch to "no longer exists" now reds a test |
| 24-L10 | Low | fixed | — | AzureResourceIdentity.IdComparer pinned by two reconciler casing tests; the Ordinal mutant that would offer a live row for deletion fails exactly those two |
| 24-I1 | Info | fixed | — | Two planner link-replacement equality checks routed through IsSameResourceId with guards kept (folding them would refuse unlinked exact-match adoption); helper mutation now propagates to both site pins |
| 24-I2 | Info | fixed | — | Locking-service DI switch on a constant provider replaced with a direct SqlServerSubnetLockingService registration |
| 24-I3 | Info | fixed | — | Inert client-JS deleted (empty submit listener, empty Scripts section, three markup-duplicating assignments); placeholder moved into markup |
| 24-I4 | Info | fixed | — | Write-only subscription/rename echo chain deleted: five plan properties, two DTO fields, the reconciler subscriptionName parameter, both clients' variables/payload fields, and the orphaned dropdown data-name stamps |
| 24-I5 | Info | fixed | — | ValidateParentCanHaveChildSubnets drops the unread parentId and dead default; signature now takes the host-IP collection alone |
| 24-I6 | Info | fixed | — | Dead data-ip-version attribute deleted from the unallocated-ranges Create Subnet button |
| 24-I7 | Info | fixed | — | Never-passed timeout parameter removed from ExecuteWithSubnetLockAsync across interface, both implementations, and both test doubles |
| 24-I8 | Info | fixed | — | The unread prefix local in the round-21 zero-work test now asserts Blocked/not-selectable, pinning the 21-L1 prefix half |
| 24-I9 | Info | fixed | — | NetworkInputAttribute always validates as an IP; the production-dead RequireValidIp property and else branch deleted |
| 25-L1 | Low | fixed | — | Client no-subscriptions panels on both wizard pages reworded to the 24-L4 grant-access remedy; the contradictory check-your-credentials remedy is gone |
| 25-L2 | Low | fixed | — | Controller credential-Failed message covers both causes ("Could not authenticate with Azure. Check the credentials and that Azure is reachable from this host."); the audit-verifier-rejected exception-type filter was not implemented |
| 25-L3 | Low | fixed | — | Both wizard commit error handlers branch on xhr.status 0: unreachable-server shows outcome-unknown wording pointing at the subnet list, unparsable nonzero responses show the bare status; "Server error: 0" gone |
| 25-L4 | Low | fixed | — | AzureCredentialBannerTests pin the credential tri-state at both controller page actions with exact wording; controller-branch and wording mutants red; the AzureService.cs:35 return itself has no unit seam (ArmClient) and stays live-rig-only |
| 25-I1 | Info | fixed | — | NetworkInputAttribute pinned at its two live inputs (CreateSubnetViewModel.NetworkAddress, CreateHostIpViewModel.IP) via the parity harnesses; always-Success mutant reds exactly the 4 new cases |
| 26-L1 | Low | fixed | — | GetSubscriptions' null-credential branch threw the GetVNetInventory classification instead of returning []; the false grant-access panel no longer fires on the no-credential branch (browser-verified both wizards); pinned by AzureServiceNullClientTests driving the real provider under an invalid AZURE_TOKEN_CREDENTIALS |
| 26-L2 | Low | fixed | — | 25-L1's grant-access panel wording in both _StepSubscription.cshtml pinned both directions by AzureWizardClientWordingTests source-content theories; the pin is the durable record for this seamless client surface |
| 26-L3 | Low | fixed | — | 25-L3's xhr.status-0 commit-error branching in both wizard *Scripts.cshtml pinned (branch marker, both sentences, "Server error: " absent) by the same test file; revert to 9d3c7b2 reds exactly the four wording theories |
| 27-L1 | Low | fixed | — | Two wizard error-banner partials collapsed into one shared Views/Azure/_ErrorAlert.cshtml (the BulkImport one, moved byte-identical); Reconcile/_ErrorAlert.cshtml and its static "Could not connect to Azure." headline deleted, so the NoVisibleSubscriptions banner no longer contradicts itself; pinned by WizardPages_RenderOneSharedErrorAlert_ThatCarriesNoConnectivityHeadline (one partial under Views/Azure, both pages reference it, headline absent from the partial and both pages; four mutants red) |
| 27-L2 | Low | fixed | — | CommitErrorHandler_BranchesOnUnreachableServer_AndNeverAssertsServerErrorZero now asserts one whitespace-tolerant regex binding the if (!payload) guard, the xhr.status === 0 condition and both arms in order; branch-swap, dead-condition, dropped-guard and 9d3c7b2-revert mutants red in both wizard scripts, cosmetic reflow green; test-only, the 26-L3 claim is now true |
| 23-L2-remainder | Low | fixed | — | Details CIDR-modal client IP engine deleted; IpUtilityService.SuggestChildSubnets stamps SubnetDetailsViewModel.ChildSubnetSuggestions (per-range recommended CIDR + per-CIDR lowest free aligned block), the modal only indexes it; modal navigation now built from Url.Action + URLSearchParams (the last hand-built route in the app); per-button parent data, four dead hidden inputs, the unreachable overlap branch and the debug logging deleted; the header Add Child Subnet link (a second entry point bypassing the suggestion, and an unactionable page on a fully carved unflagged parent) deleted; pinned by the suggestion property theory, six edge tests, controller tests incl. every suggested block accepted by Create, and six source-content pins on the four Details partials (12 mutants red); client behaviour driven live in the browser and recorded in /e2e |
| 28-refuted-a | — | refuted | — | SqliteSubnetLockingService production-dead since 24-I2: fact reproduced but not a finding under §5 as ruled in round 26 (no operator-visible behaviour) |
| 28-refuted-b | — | refuted | — | Hidden form inputs posted to Subnet Edit / HostIp Create never read: half false (HostIp Create's subnet-gone branch renders them) and dead-input cleanup is not a finding |
| 28-refuted-c | — | refuted | — | UpdateHostIpDto annotations never execute: accurate but a slice of the §7-accepted ValidateHostIpUpdate branch, no reachable wrong output |
| 28-L1 | Low | fixed | — | Create refusal named a container (host IPs / fully allocated) the validator then refuses; ValidateSubnetCreation now states the fact via RefuseWhenContainerCannotHoldChildren before naming bestParent in either branch; pinned by SubnetCreateContainerRefusalTests (4 refusal rows + 2 controls) |
| 28-L2 | Low | fixed | — | HostIp Edit redisplay blanked the subnet card on lock timeout / validation / save error; one RedisplayEditAsync helper reloads the row for all four redisplay returns (conflict-branch inline reload deleted); pinned by HostIpEditRedisplayTests (3 of 4 sites; the DbUpdateConcurrencyException catch has no SQLite seam - recorded here); a host IP deleted between GET and POST now answers 404 like the GET |
| 28-L3 | Low | fixed | — | Rename-only prefix reason replaced the planner's reason with "Everything in this prefix is already imported" above a Cannot-import row; the client now appends the rename sentence to prefixInfo.reason (the subnet-row shape); no unit seam (live-plan client render) - durable record is this row plus the /e2e phase F "Rename-only keeps the planner's reason" assertion; browser-verified against live Azure in the drifted, host-IP and fully-allocated branches |
| 28-L4 | Low | fixed | — | Edit sidebar described CIDR as modifiable on an Azure-linked row whose form fixes it; _InformationSidebar declares the page model and branches on the same Model.IsAzureLinked as the form; pinned by SubnetEditViewSourceTests |
| 28-L5 | Low | fixed | — | CIDR modal read its number input with parseInt (24.5 approved, 2e1 refused); modal reads valueAsNumber and posts prop('valueAsNumber') so the lookup table decides; sibling Create-form preview indexes cidrInfo by valueAsNumber; pinned by SubnetDetailsModalScriptTests.CidrModalScript_ReadsTheCidrInputAsANumber, recorded in /e2e phase F |
| 28-L6 | Low | fixed | — | HostIp Create rendered model-level errors twice and Edit showed an empty "Error" alert; Create/_ErrorAlert.cshtml deleted, Edit/_Header uses the single ModelOnly summary; pinned by HostIpViewSourceTests |
| 28-L7 | Low | fixed | — | Deleted-host-IPs "Back to Host IPs" landed on the guarded Index refusal; the page's only back link is Back to Subnet (role branch and @inject deleted); pinned by DeletedHostIpsViewSourceTests |
| 28-L8 | Low | fixed | — | Five wizard read handlers printed "Error connecting to server: " + errorThrown (blank on transport failure, HTTP reason on 5xx) and _StepReview blamed Azure for every scan failure; readErrorMessage(xhr) branches on status 0 in both scripts, _StepReview says "Fix the problem shown above"; pinned by AzureWizardClientWordingTests (function shape, call-site count, old text absent) |
| 28-L9 | Low | fixed | — | CIDR modal kept the adjusted address and its note on screen while refusing; refuse() resets the address to the range start and hides the note for every refusal; pinned by CidrModalScript_RefuseResetsTheNetworkAddressToTheRangeStart, /e2e phase F sentence extended |
| 28-L10 | Low | fixed | — | Refusal sentence claimed a parent-wide absence while the table searches at or after the clicked range; sentence is now "No free /N block starts at or after A.B.C.D." (fact only, no remedy); pin updated, /e2e phase F record corrected |
| 28-L11 | Low | fixed | — | Test-only: IpArithmeticPropertyTests.Layouts gained a short-tail row so the stale-carry (scope-hoist) mutant of SuggestChildSubnets reddens |
| 28-L12 | Low | fixed | — | Test-only: CidrModalScript_OnlyIndexesTheServerSuggestionTable binds the lookup and every #subnetSizeDisplay write and widens the client-arithmetic denylist; mutants P6/P7 redden |
| 29-L1 | Low | fixed | — | HostIpViewSourceTests pins exactly two model-level renderings across every view a HostIp page can compose from (Views root, Shared, HostIp) and the unwrapped summary line per page; the round-28 pin bound file names and two tokens |
| 29-L2 | Low | fixed | — | HostIpEditRedisplayTests seeds and asserts LastModifiedAt on all three redisplay tests; HostIpEditConcurrencyCatchTests already carries a SQLite seam for the catch 28-L2 called seamless |
| 29-L3 | Low | fixed | — | CidrModalScript_OnlyIndexesTheServerSuggestionTable binds the no-home sentence to the address === null branch and the sibling branch to strict equality; struck at triage, reversed by the owner |
| 29-L4 | Low | fixed | — | SubnetEditViewSourceTests binds each sidebar sentence to its IsAzureLinked branch with exactly-once counts and the form's readonly input to the linked branch |
| 29-L5 | Low | fixed | — | CidrModalScript_RefuseResetsTheNetworkAddressToTheRangeStart pins the two reset statements at the head of refuse() positionally; struck at triage, reversed by the owner |
