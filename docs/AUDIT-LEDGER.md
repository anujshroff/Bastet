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
| 20 | 9 | 9 | 0 | 9 of 9 worked (1 regression-guard residue of round 19 + the 8 round-18 deferred findings, closed in-round by owner instruction) | — (backfilled by round 21) | regression-only round over the round-19 delta (one finding), then the owner folded the entire round-18 deferred-findings queue into the round: 5 fix batches, every fix independently reviewed (2 review-demonstrated failures each repaired and re-reviewed), whole-diff gate filed 1 violation (repaired, re-reviewed), full browser drive of every changed wizard behavior, both Azure counter-tests both directions; DEFERRED-FINDINGS.md emptied and deleted; this is a large unaudited delta — **the next audit runs Regression-only over it** |

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
