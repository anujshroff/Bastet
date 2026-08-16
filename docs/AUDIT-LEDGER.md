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
| 18 | 23 | pending | 2 | 20 of 23 | 7b75c6e (findings commit) | reconcile pending under the rebuilt skill |

## Findings

Per-finding records begin at round 18. `id` is `<round>-<finding>` and is the identifier
`Residue of:` fields cite from round 19 onward.

| id | severity | verdict | sha | what |
|---|---|---|---|---|
| 18-refuted-a | — | refuted | 7b75c6e | Free-space warning naming Bulk Azure Import on Azure-subnet-linked rows: load-bearing claim false; wizard demonstrated importing under such a row |
| 18-refuted-b | — | refuted | 7b75c6e | Migration sp_getapplock falling back to master: EF Core's own __EFMigrationsLock serialises replicas regardless; consequence not reproducible |
