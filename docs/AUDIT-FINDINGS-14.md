# Bastet — Round-14 Audit Findings

**Target branch:** `audit/round-14` · **HEAD:** `6d1a4cb` · **Baseline:** 771 tests passing, 0 build warnings · **Date:** 2026-08-02 · **Round letter:** N (findings N1–N10)

---

## Verdict

**Nine findings need a decision from you, and here is what each costs.** Read this list first; nothing below it is safe to skim past.

| Finding | The decision | What it costs |
|---|---|---|
| **N1** (critical) — reconcile archives a row whose CIDR is still assigned in Azure | Should reconcile **fail closed** (never offer a deletion whose range is still live in Azure), **warn loudly and still allow it**, or **fail closed plus a new "re-link to Azure subnet X" action**? | Warn-only: ~15 lines, one LINQ pass, ships today, leaves the wrong archive one click away. Fail-closed alone: safe but strands the row in `ReviewItems` forever *and* permanently withholds its ancestors — do not ship alone. Fail-closed + re-link action: correct end state, largest of the three, also fixes N3/N4's un-re-importable case. The range-still-allocated **warning is worth shipping under all three**. |
| **N2** (high) — archive executed on an approval the server's own re-scan had disproved | Should an out-of-band Azure re-create inside the wizard's confirm window become a **409 the operator must re-scan through**? And is the approved-status check **mandatory** or **optional-and-logged** for direct JSON API callers? | ~20 lines across DTO, view and controller plus one unit test. Cost: a legitimate drift deletion becomes a retry. The same fail-open-and-log trade-off you already accepted for bulk import at `SubnetController.BulkAzure.cs:99-106`. |
| **N3** (high) — Azure prefix added to an already-imported subnet is invisible and advertised as free | Does the reconciler get an **inbound verdict** at all (new `AzureReconcileStatus`, wider `BuildPlan` input, report-only review row)? | Full: ~1 day plus tests. Cheap half — a plan **warning**, ~30 lines, no schema, no UI change — can ship first and is independently useful. |
| **N4** (high) — the same range can then never be imported by either wizard | Should there be **any** supported path to pull new Azure prefixes into an already-imported target — and if so, a **new narrow "top-up" action** rather than relaxing the two "target must be empty" gates? | Relaxing the gates is cheaper but re-opens the adopt-and-re-stamp blast radius; the narrow action is smaller in blast radius, larger in code. Either way, **closing the free-space lie on `/Subnet/Details/{id}` is not optional**. |
| **N5** (medium) — stranded global write lock; peers fail for minutes with a false "high concurrency" message | Is the **pool-wide blast radius of `SqlConnection.ClearPool`** on the release-failure path acceptable? | (a) Accept the pool clear — verified working, one burst of reconnect handshakes on an error path only. (b) Leave it stranded and make the peer's error honest plus a KILL runbook. (c) Change the lock ownership model — explicitly rejected by the class remarks. (a) is cheapest and is the one that was measured to work. |
| **N6** (low) — one Azure subnet persists as two Bastet rows with the identical name and identical resource id | Should prefix-qualified names apply **across the whole commit and across sessions**? | Changes names some installs see on their **next** import (nothing already persisted is renamed); the alternative is preview-warning only. |
| **N7** (low) — generated names contain `/`, which the app's own Create form forbids; the prefill persists a garbled false address | Pick the suffix format: **(a)** change the separator to `-` so generated names satisfy SafeText (three test assertions, no rename migration — the code shipped one commit ago), or **(b)** keep `/` and relax `[SafeText]` on `CreateSubnetViewModel.Name`, widening a shared input class. | (a) is two string literals plus tests. (b) has a security-review implication. Doing neither leaves the app generating names it refuses on input. |
| **N8** (low) — an unstrippable fully-allocated note re-creates M3's stacking defect | Do anything about rows that **already** carry a two-line note? The code fix alone leaves them permanently un-repairable except by hand-editing the description. | A backfill, or accept the residue — the same call round 13 already made explicitly for stacked notes. The code fix itself is one line. |
| **N10** (low) — a two-address-space VNet persists as two Bastet targets with the identical name and identical VNet resource id | Should `TargetName` be prefix-qualified when a VNet contributes several selected prefixes? | Changes names some installs see on their **next** import of such a VNet (nothing already persisted is renamed); the alternative is a preview warning and a manual rename. Reproduced on every multi-address-space VNet import, so it is not conditional on anything unusual. |

**Read this first: N1.** Reconcile will archive a Bastet subnet whose CIDR is **still assigned in Azure** — because an Azure subnet cannot be renamed, so renaming one means delete-and-recreate, and the reconciler keys only on the recorded ARM resource id. It was driven end to end on a live subscription: after approval, `/Subnet/Details/1` printed the range as free with a **Create Subnet** button over a `/24` ARM still holds. The reconciler had the contradicting evidence in its own hand — `liveSubnetPrefixes`, built from the same scan. Two routes (`SubnetDeleted` and `SubnetPrefixChanged`), both silent, no warning anywhere in the flow, and the archive has no restore.

Three more findings put an **allocated range in front of an operator as free space with a button to allocate from it** (N1, N3, N4). In N3 the harm was not described but *executed*: a `/24` Azure had already committed was handed out by BASTET with no warning, and `ReconcileScan` — the one feature whose job is to compare the two — returned `items 0, warnings []`. N2 is the other side of the same coin: an irreversible archive of a subtree, including a child subnet and a host IP that carry no Azure provenance at all, performed on an approval whose stated premise the server had disproved milliseconds earlier.

**Do you need to act today?** If you run reconcile against a subscription where anyone renames or re-creates subnets — yes. N1's interim warning is ~15 lines and removes the silent part immediately. N3's interim warning is ~30 lines. Both are additive, neither changes what is deletable. The rest (N5–N10) is not same-day.

Six candidates were killed by verification and are recorded in **Refuted** so round 15 does not re-report them — including two dead-code observations and two "the rename is invisible" claims that were false when measured.

---

## How this audit ran

**Eight beats**, each a lens over the whole codebase rather than a directory: (1) the Azure import surface, (2) the Azure reconcile surface, (3) cross-feature interaction between import, reconcile and the IPAM core, (4) infrastructure — locking, migrations, connection lifetime, hosting shape, (5) naming, sanitisation and identity of persisted rows, (6) the round-13 delta itself (multi-prefix import, `FullyAllocatedNote`, `ResolveImportNames`), (7) free-space and allocation arithmetic as rendered to an operator, (8) authorisation, antiforgery and the direct-JSON-API surface.

**Two independent passes** ran every beat without sight of each other's output, plus a **deep sweep** on beats 1, 3, 6 and 7 — the beats covering the round-13 delta and the free-space arithmetic, i.e. where a regression would be both newest and most consequential. 20 finders were dispatched, 20 returned.

**Tag meaning.** `[x2]` = the same defect was found independently by **both** passes. `[x1]` = found by **one** pass only. **`[x1]` is weak evidence of absence, not evidence of weakness** — a defect on a surface only one pass happened to drive is exactly the defect that survives to production. Every `[x1]` candidate therefore got **more** scrutiny, not less: a second verifier on a reachability lens (can a real user, with no crafted request or with only the crafted requests the code itself claims to defend against, reach this?), and a third verifier whenever those two disagreed. Four of the nine findings that came through the funnel are `[x1]` (N2, N5, N7, N9), including the medium-severity stranded-lock defect (N5) that only one pass reached.

**Adversarial verification with live reproduction.** Verifiers were instructed to kill findings, not confirm them: each stood up its own app instance on its own port, its own SQL catalog, and where the finding touched Azure its **own** live ARM fixtures rather than replaying the finder's. Every surviving finding records what was actually run and what came back. Verifiers also re-assessed the proposed fix independently — five of the nine fixes were judged **incomplete or unsound** and were corrected; those corrections are among the most valuable output of this round and are marked inline.

**Funnel.**

| Stage | Count |
|---|---|
| Finders dispatched | 20 |
| Finders returned | 20 |
| Raw findings | 35 |
| Dropped at merge (duplicates / out of scope) | 7 |
| Candidates carried to verification | 15 |
| — found by both passes `[x2]` | 8 |
| — found by one pass `[x1]` | 7 |
| Verifiers dispatched | 23 |
| Survived verification | 9 |
| Refuted | 6 |
| Promoted from the watch list at the citation check | 1 |
| Findings filed (N1–N10) | 10 |
| Reproduced live | 10 |
| Flagged as needing an owner decision | 9 |

All ten findings reproduced on a running instance. Nothing in this file is inferred from reading code alone. N10 did not come through the finder/verifier funnel: it was parked in the watch list despite having been persisted and observed in N6's own reproduction, and the citation check moved it into Low as a finding of its own.

---

# Critical

## N1 — Reconcile archives a Bastet subnet whose CIDR is still allocated in Azure by a *different* Azure subnet `[x2]` — FIXED

_N1 is fixed and committed. The reconciler now answers the question the four stale statuses never asked: is the RANGE still assigned in Azure, even though the resource that carried it is gone? `BuildPlan` builds a second index alongside `liveSubnetPrefixes` mapping each live IPv4 prefix to the Azure subnet holding it, and any row that would have been offered for deletion while its range is still assigned becomes a new report-only status, `RangeStillAllocatedInAzure`, in `ReviewItems` — never deletable — plus a plan warning naming the Azure subnet that holds it._

_All three of the verifier's corrections were taken. The index accumulates into `Dictionary<string, List<AzurePrefixOwner>>` rather than `ToDictionary`, because one prefix legitimately has several owners and a duplicate-key throw would turn a whole scan into "The reconcile scan failed"; `DuplicateRangesAcrossVNets_DoNotThrow` pins it with three VNets carrying the same `10.10.1.0/24`. The match is scoped to the row's own VNet via a new `AzureResourceIdentity.VNetIdOf`, because overlapping RFC1918 across unrelated VNets is the norm — `TheSameRangeAllocatedInADifferentVNet_DoesNotWithholdTheDeletion` proves an unrelated VNet's identical range does not withhold a genuine deletion. And the fail-closed half was **not** shipped alone: the owner chose warn + fail closed + re-link, so `POST /Subnet/RelinkAzureSubnet` now repairs the link in place, which is what makes withholding a correction rather than the permanent dead end correction 3 warned about._

_The re-link endpoint takes **no resource ID from the caller** — only the Bastet subnet id. It re-scans Azure, re-derives the new link from the fresh plan, accepts only a row that plan itself reports as `RangeStillAllocatedInAzure`, and re-checks the row's link under the subnet lock before writing. So neither a stale browser view nor a crafted post can point a Bastet subnet at an arbitrary Azure resource; the range that moved decides what the row links to. `TheNewLinkIsDerivedFromAzure_NotFromAnythingTheCallerSupplied` pins that with a decoy subnet in the same VNet._

_Proven by A/B against a clone of the unfixed commit `77560af`, both builds driven through the same live Azure fixture (`rec14-n1-vnet` 10.111.0.0/16, subnet `rec14-n1-sn-a` 10.111.5.0/24 deleted and recreated as `rec14-n1-sn-a2` with the same prefix — Azure has no rename). Unfixed: the scan reported `items: [(2, "SubnetDeleted")]` with no warning, `BulkDeleteStaleAzureSubnets` returned **200 `subnetsArchived: 1`**, and `/Subnet/Details/1` then advertised `10.111.0.0 - 10.111.255.255, 65,534 IP addresses` as free — up from 65,278, a difference of exactly the 256 addresses ARM still assigns. Fixed, same fixture: `items: []`, `reviewItems: [(2, "RangeStillAllocatedInAzure", "rec14-n1-sn-a2")]`, the archive attempt returned **409 with nothing deleted**, free space was unchanged, the re-link returned 200, and the following scan was completely clean — `items []`, `reviewItems []`, `warnings []`. The re-link endpoint returns 404 on the unfixed build; it did not exist._

_Not done, and deliberately: nothing backfills rows already archived by this defect. `DeletedSubnets` has no restore path (round 13 established that on the record), so a row archived before this fix is recovered by re-importing it, which the re-link path does not change. The `SubnetPrefixChanged` route still takes no direct ARM read — `IsAbsenceStatus` is unchanged — because the range check now covers the case that made that gap dangerous, and widening confirmation to drift statuses would withhold every drift row permanently, which `ApplyConfirmations` documents at length as the reason it does not._

_Tests: 771 → 792. Nine in `AzureReconcilerRangeStillAllocatedTests` (four of which fail against the unfixed reconciler, and five counter-tests that a genuinely deleted range stays deletable), nine in `SubnetControllerRelinkAzureSubnetTests`, and three added automatically by `ControllerAuthorizationTests`, which enumerates controller actions by reflection and so picked the new endpoint up on its own._

---

# High

## N2 — Reconcile delete archives on a re-derived Azure verdict it never compares against the one the operator approved `[x1]` — FIXED

_N2 is fixed and committed. `AzureReconcileDeleteDto` gained `Statuses`, a per-row `{subnetId, statusName, reason}` snapshot taken where `confirmedIds` is already frozen in `_ReconcileScripts.cshtml`, so both describe the same reviewed plan. The commit now refuses the whole batch when any selected row's re-derived verdict differs from the approved one, returning a 409 worded separately from the existing "no longer reported as deleted" refusal — deliberately not merged, because the two call for different operator actions: the first means the row is fine, the second means it is still wrong but wrong in a way they have not seen and might well answer by re-importing rather than archiving._

_Both of the verifier's tightenings were taken, and the owner chose the strict reading of each. The **reason** is compared as well as the status, so a same-status/different-facts change — a prefix that has moved again, re-deriving as `SubnetPrefixChanged` both times while naming a different live prefix — is caught; `ApprovedWithTheSameStatusButADifferentReason_IsRefused` pins it. An **unparseable** status name is a divergence rather than "unverified", mirroring how `DescribeApprovedPlanDivergences` handles `TargetType`; a `[Theory]` covers `"NotAStatus"`, `""` and `"42"`. And the check is **mandatory**, not optional-and-logged: a request naming no verdict at all is refused, because an omitted verdict is exactly what a replayed or hand-built post carries. The refusal is all-or-nothing, returned before any lock is taken, and puts the mismatched ids in `subnetIds` so the wizard can highlight them._

_The interim mitigation was **not** taken. It rejected the absence-to-drift transition specifically, which is narrower than the defect: comparing the approved verdict covers that transition and every other, at the same cost, without the interim's side effect of 409ing a plan that was drift-only from the start._

_Making the check mandatory is a contract change, so three existing tests that drove a successful delete without naming a verdict were updated to supply one. They derive it from a real scan through a new `AzureReconcileApproval.ForAsync` helper rather than hardcoding a status — a test that hardcodes what it expects would keep passing if the reconciler started reporting something else, which is the very drift this check exists to catch._

_Proven by A/B on the same live Azure, driving the exact IaC destroy/apply the finding describes: scan while the VNet is deleted (operator approves `VNetDeleted`), recreate the VNet at the same ARM id with prefix `10.112.0.0/16` — ARM ids are path-based, so the id is unchanged — then commit carrying the original approval. Unfixed `77560af`: the server re-derived `VNetPrefixRemoved` and still returned **HTTP 200 `targetsDeleted: 1, subnetsArchived: 1`**, taking the database from 1 subnet / 1 archived to 0 / 2. Fixed, same sequence: **HTTP 409**, `subnetIds: [1]`, "The reason 1 of the selected subnet(s) were flagged has changed since you reviewed them", and the database unchanged at 2 subnets / 0 archived. The 409 named only row 1; row 2, whose `SubnetDeleted` verdict was genuinely unchanged, was not falsely implicated._

_One measurement was discarded rather than recorded: the first fixed-build run also returned 200, because the app process had been started before the N2 edit and was still serving N1-era code. It was rebuilt and re-run before anything was concluded._

_Tests: 792 → 800. Eight in `SubnetControllerReconcileApprovedVerdictTests`, including the counter-test that a verdict which still matches archives normally — without it this fix would be indistinguishable from breaking the feature._

---

## N3 — An IPv4 prefix Azure adds to an already-imported subnet is invisible to reconcile and advertised as free space `[x2]` — FIXED

_N3 is fixed and committed. The reconciler had no inbound direction at all: every status started from a BASTET row and asked what Azure said about it, so an Azure range BASTET has no row for was invisible to all of them. `AzureReconcileStatus.AzureRangeNotImported` is that verdict, reported into `ReviewItems` — report-only, never deletable, because it names no BASTET subnet and the absence of one **is** the finding._

_The owner chose the full inbound verdict over the warning-only half, so `IAzureReconciler.BuildPlan` now also takes the whole subnet tree (`IAzureSubnetSnapshotService.GetExistingSubnetsAsync`, which already existed and was already called by the bulk path), and `AzureReconciler` takes `IIpUtilityService` for the containment maths — the same shape `AzureBulkImportPlanner` already has, and still pure in the sense that matters: no EF, no Azure calls._

_Both of the verifier's corrections to part (a) were taken and both are pinned by tests. **Containment, not equality**: an IPAM routinely records a coarser allocation than Azure carves out of it, so a BASTET `10.90.64.0/18` accounts for an Azure `10.90.77.0/24` inside it — `ARangeContainedByACoarserBastetSubnet_IsNotReported`. **The whole tree, not just linked rows**: only the two import paths ever write `AzureResourceId`, so a range the operator created by hand carries none, and matching against linked rows alone would report it forever after they had already done exactly what the report asked — `ARangeCreatedByHandCarryingNoAzureLink_IsNotReported`. The walk is scoped to VNets that have actually been imported, or pointing the scan at an untouched subscription produces an item per Azure subnet on every scan._

_Two corrections were mine, not the audit's, and both were found by running the thing rather than reading it. First, **a VNet-level import target contains every range in its VNet by construction**, so counting a target's containment as "accounted for" made the check vacuous — it could never report anything. Targets are excluded from the containment arm but still honoured on exact match, because an Azure subnet covering a whole VNet prefix is recorded by marking that target fully allocated rather than by creating a child. Two tests pin both halves of that. Second, **`GetVNetInventory` emits one row per prefix and every row carries the complete prefix list** (round 13's `BuildInventorySubnetRows`), so walking rows x prefixes visits an n-prefix subnet n-squared times: the first live run reported one unaccounted range **three times**. Deduped, with `TheRealMultiPrefixInventoryShape_ReportsEachRangeExactlyOnce` built from the real inventory shape rather than a hand-simplified fixture that hides it._

_Part (b) of the finder's proposal — relaxing the two import gates — was **not** done here; it is N4's decision and is answered there._

_Proven by A/B on live Azure. Fixture `rec14-n3-vnet` 10.90.0.0/16 with subnet `rec14-n3-sn-multi` carrying `10.90.200.0/25` + `10.90.200.128/25`, bulk-imported into both builds (`createdTargets: 1, createdChildSubnets: 2`), then `az network vnet subnet update` added `10.90.77.0/24` to that same subnet — ARM confirmed the multi-prefix shape with singular `addressPrefix: null` and `addressPrefixes` populated. Unfixed `77560af`: `items [], reviewItems [], warnings []` — the one feature whose job is comparing the two systems reported nothing at all. Fixed: one `AzureRangeNotImported` review row for `10.90.77.0/24` reading "Azure subnet 'rec14-n3-sn-multi' in VNet 'rec14-n3-vnet' owns 10.90.77.0/24, which no BASTET subnet records. BASTET is reporting that range as free space."_

_Still true after this fix, and deliberately: `/Subnet/Details/{id}` continues to print the range under Unallocated IP Ranges with a Create Subnet button. Closing that is N4's half of the same defect and is answered there — this finding is the detection._

_Tests: 800 → 812. Twelve in `AzureReconcilerInboundTests`, seven of which are false-positive tests: an inbound report that fires on ranges BASTET legitimately accounts for is a warning operators learn to ignore, which is worse than no warning._

---

## N4 — An Azure subnet that gains an IPv4 prefix after import can never be imported by either wizard `[x2]` — FIXED

_N4 is fixed and committed. The owner chose the narrow top-up action over detect-and-report-only, so a populated target is no longer refused outright — but the allowance is deliberately narrow: **the target must already be linked to THIS VNet**. That is what separates topping up an import that has happened from adopting a subtree somebody built by hand, which is the adopt-and-re-stamp blast radius the verifier warned about. A populated target with no Azure link, or one linked to a different VNet, is still refused, and both refusals are pinned by tests._

_All four of the verifier's gaps were closed. **(1)** A populated target can no longer be marked fully allocated: the commit marks the flag INSTEAD of creating children, so the existing rows would have been stranded under a target claiming nothing more fits — the old blanket refusal was preventing this incidentally, and the top-up made it reachable, so it is now refused explicitly. Host IPs and the fully-allocated flag remain hard refusals. **(2)** The prefix now previews as `WillUpdateExisting` with distinct copy — "Will add any missing subnets to existing Bastet subnet 'X'. Subnets already imported are left untouched." — rather than the first-import wording, and `renameMatched` no longer renames a populated target, because renaming a label the operator has been living with is not what a run to add one missing subnet is for. **(3)** The single-VNet wizard was narrowed the same way rather than left half-fixed: `AzureController.Import` admits a populated target when it is Azure-linked, and `GetAzureSubnets` now filters out ranges BASTET already records, server-side, so a top-up there cannot re-offer a subnet a previous import created. **(4)** The reconciler half is N3 and shipped with it._

_One part of this finding could not be done as specified, and the reason matters. "Closing the free-space lie on `/Subnet/Details/{id}`" cannot mean asking Azure on page render: **BASTET is self-hosted and must keep working with no outbound internet**, so making the free-space table depend on reaching ARM would break air-gapped deployments and hang the page whenever Azure is slow. Two things close it instead. The top-up import closes it *properly* — once the range is imported it is a real subnet and stops being listed as free, which is what the measurement below shows. And for the window before that, an offline-safe note now sits above the table on any Azure-linked subnet, saying the ranges are free according to what BASTET has imported and linking to Azure Reconcile, which is the feature that establishes the rest._

_Proven by A/B on live Azure, continuing N3's fixture (`rec14-n3-vnet`, subnet `rec14-n3-sn-multi` with `10.90.77.0/24` added after import). The wizard annotation first, and it reproduces the contradiction the finding describes: unfixed `77560af` returned `PREFIX 10.90.0.0/16 Blocked selectable=False` while the row underneath read `SN 10.90.77.0/24 Available selectable=True` — offered and un-tickable in the same response. Fixed: `PREFIX 10.90.0.0/16 WillUpdateExisting selectable=True` with the top-up copy. Then the commit: unfixed returned **HTTP 400** "matched Bastet subnet 'rec14-n3-vnet' already has child subnets" with free space unchanged; fixed returned **HTTP 200 `createdChildSubnets: 1`**, after which `/Subnet/Details/1` reports `10.90.0.0-10.90.76.255`, `10.90.78.0-10.90.199.255`, `10.90.201.0-10.90.255.254` — 65,278 addresses free before, 65,022 after, a difference of exactly the 256 the `/24` holds, with the range carved out rather than advertised. The reconcile scan that reported `AzureRangeNotImported` before the top-up returns `items [], reviewItems [], warnings []` after it._

_Tests: 812 → 822. Ten in `AzureBulkImportTopUpTests`, six of which are refusals — the allowance is only correct if adoption, a different VNet, host IPs, the fully-allocated flag, the fully-allocated marking and the rename all stay refused._

## N5 — A failed `sp_releaseapplock` is swallowed on a documented invariant that is false `[x1]` — FIXED

_N5 is fixed and committed. The swallow stays — every guarded path has already committed by then, and an exception raised in a `finally` replaces the one in flight — but it is no longer swallowed on a false premise. `CloseConnectionAsync` returns the connection to SqlClient's **pool**; the SQL session stays open, and a Session-owned application lock is only dropped when that pooled connection is next reused or physically destroyed. On a failed release the physical connection is now discarded via `SqlConnection.ClearPool`, which ends the session and the lock with it._

_The comments that asserted the false invariant are corrected in both places — `SqlServerSubnetLockingService` ("if it is alive the outer finally closes it anyway") and `Program.cs` ("if it is alive the using block closes it here") — and the log message no longer implies the lock takes care of itself. Those comments were the reason the defect survived review; leaving them would have re-taught the next reader the thing that was wrong._

_The verifier's correction to the secondary fix was taken. `Pooling=false` was **not** added to `MigrationLockConnectionString.Configured`: that method's contract is the connection string returned verbatim, `MigrationLockConnectionStringTests` asserts exactly that, and routing it through `SqlConnectionStringBuilder` would also destroy its documented null-in/null-out behaviour. The migration lock uses the same `ClearPool` remedy in its existing catch instead, which covers both branches uniformly and touches no unit-tested contract. The discard is itself wrapped, because it runs inside the catch of a release that already failed and must not throw from there._

_The owner accepted the pool-wide blast radius knowingly: `ClearPool` empties the whole pool for that connection string, so the replica pays a burst of reconnect handshakes — on an error path only. Microsoft.Data.SqlClient exposes no per-connection "do not pool" switch, so this is the only public mechanism, and the alternative is denying every write on every replica until something happens to reuse that one connection, which a peer replica cannot cause._

_Proven on a rig, not by a unit test, because the suite runs SQLite and this is real SQL Server locking behaviour. Two scratch copies — the fixed tree and a clone of `77560af` — each with a one-shot injected release failure gated on an env var, run against the real SQL Server container, one replica each plus a peer replica on the same catalog. Both builds accepted the create (302) and both logged the failed release exactly once, so the fault fired identically. **Unfixed:** `APPLOCK_TEST('public','Bastet:SubnetOperations','Exclusive','Session')` returned **0 — held** after the request had completed and the DbContext was disposed, and the peer replica's `POST /Subnet/Create` returned HTTP 200 after **30.06 seconds** rendering "The operation timed out due to high concurrency. Please try again." with the row never written. **Fixed:** the same query returned **1 — free**, and the peer replica's identical write returned 302 in **0.15 seconds** with the row persisted._

_Not done, and deliberately: the interim mitigation of raising the log to `LogCritical` was dropped rather than shipped alongside. It existed to make the 30-second "high concurrency" failures diagnosable while the lock stayed stranded; with the lock no longer stranded there are no such failures to diagnose, and a Critical line for a condition the process has just repaired would be noise. The natural (non-injected) trigger remains unproduced — that stays on the watch list, where the audit put it._

_Tests: unchanged at 822. Nothing was added, because nothing in the suite can reach this: SQLite has no `sp_getapplock`, and the defect is in what SqlClient does with a connection after it is closed._

## N6 — Bulk import's multi-prefix name qualification is scoped to one VNet address prefix `[x2]` — FIXED

_N6 is fixed and committed. The `multiPrefixResourceIds` grouping is hoisted out of `BuildPlanItem` into `BuildPlan` and computed once across every selected prefix of every VNet, then passed in — so an Azure subnet owning one prefix under `10.71.0.0/16` and another under `10.72.0.0/16` is now seen as multi-prefix by both items instead of looking single-prefix to each. The owner chose qualification across the whole commit and across sessions over the preview-warning alternative._

_The verifier's correction to part 1 was taken: the `!s.FullyEncompasses && !string.IsNullOrEmpty(s.Source.AzureResourceId)` filter is kept exactly as it was. `AnEncompassingSelectionDoesNotInflateTheGroup` pins why — a subnet may equal one VNet prefix exactly and still hold a prefix inside another, so dropping the filter would inflate the group and needlessly rename the one child that is actually created._

_Part 2 was implemented as the verifier's **corrected** version, not as proposed. `usedNames` is **not** seeded from the existing tree: that would rename any child whose Azure name merely matched some unrelated Bastet subnet anywhere in the tree — a broad silent rename in the ordinary path — and `DisambiguateName` appends the VNet name rather than the range, so a cross-session second row would land in a different shape from the single-session one. Instead a planned child is qualified when the tree already holds a row with the **same `AzureResourceId` and a different `{NetworkAddress, Cidr}``_. Fires only for the real multi-row case, keeps one shape, needs no DTO or ARM change. `AnUnrelatedBastetSubnetWithTheSameName_DoesNotCauseARename` and `ThePersistedRowForTheSameRange_IsNotTreatedAsASibling` pin both edges._

_Part 3 — the `TargetName` half — is N10 and is fixed there, on its own merits._

_Proven by A/B at the unit level, which is where this defect lives: the new test file was copied unchanged into a clone of `77560af` and run. Against the unfixed planner **2 of 6 fail** — `AnAzureSubnetSpanningTwoVNetPrefixes_HasBothChildrenQualified` (both children came back as the bare `sn-span`) and `ASelectionWhoseAzureSubnetAlreadyHasAPersistedSibling_IsQualified` — while the other 4 pass on both builds, because they assert behaviour the fix had to preserve rather than change._

_One incidental correction: the hoisted set is constructed with an explicit `StringComparer.OrdinalIgnoreCase` rather than the collection expression it replaced. That is N9's defect, and this finding could not move the line without either fixing it or knowingly re-writing it wrong. N9 covers the remaining site and pins both._

_Tests: 822 → 828. Four of the six are counter-tests._

---

## N7 — Round 13's name qualification builds subnet names containing `/`, the one character the app's own name rules forbid; the create-from-unallocated-range prefill then silently rewrites `(10.20.40.0/24)` to `(10.20.40.024)` and persists that false token `[x1]`

**Severity:** low · **Confidence:** confirmed
**Citation:** `src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:530` and `src/Bastet/Controllers/SubnetController.Azure.cs:276`

**Failure scenario.** Bulk import creates rows named `rig-14-sn-a2-multi3 (10.20.40.0/24)`. `Subnet.Name` accepts them (Edit applies only `[NoHtml]`/`[SanitizeName]`), but `CreateSubnetViewModel.Name` carries `[SafeText]`, whose class `[a-zA-Z0-9\s\-_.,!?@#$%&()+=]` excludes `/`. Two operator-visible consequences: **(1)** the name the app generated is a name the app's own Create form refuses; **(2)** the Details page's **Create Subnet** button on an unallocated range navigates to `/Subnet/Create?parentId=…`, where `SubnetController.Create.cs:76` runs `SubnetNaming.ToSafeText(parentSubnet.Name)` precisely to avoid that rejection — and `ToSafeText` **deletes** the `/` rather than rejecting, so the prefilled default becomes `rig-14-sn-a2-multi3 (10.20.40.024)-10.20.40.0-25`. An operator who accepts the default — which is what that button exists for — persists it. The rule is written verbatim in a comment in the very controller that prefills the form, at `SubnetController.Create.cs:67-68`: *`"-{cidr}" and not "/{cidr}": [SafeText] on CreateSubnetViewModel.Name forbids "/"`*. This is the exact failure round 4's D19/D8 fixed; round 13 reintroduced the character, and `test/Bastet.Tests/Azure/AzureMultiPrefixImportCommitTests.cs:124-125` now pins the slashed form.

**Reproduction** — own instance port 5891, catalog `bastet_rig14_verc12b`, live ARM:

```
POST /Subnet/BulkCreateFromAzurePlan -> {"success":true,"createdTargets":1,"createdChildSubnets":5}
  2|rig-14-sn-a2-multi3 (10.20.40.0/24)|10.20.40.0|24|1        (etc.)

GET /Subnet/Details/2 renders
  <button class="create-subnet-btn" data-network="10.20.40.0" data-parent-id="2" data-parent-cidr="24">
  and navigates to /Subnet/Create?networkAddress=..&cidr=..&parentId=..

GET /Subnet/Create?networkAddress=10.20.40.0&cidr=25&parentId=2 ->
  value="rig-14-sn-a2-multi3 (10.20.40.024)-10.20.40.0-25"      <- the "/" was deleted

POST /Subnet/Create with that default -> 302 /Subnet/Details/7
  7|rig-14-sn-a2-multi3 (10.20.40.024)-10.20.40.0-25|10.20.40.0|25|2

POST /Subnet/Create Name="rig-14-sn-a2-multi3 (10.20.40.0/24)-child"
  -> 200 with field error "Subnet name contains invalid characters"
```

**Fix (verifier: sound, with two additions).** Change the suffix at both sites from `$" ({network}/{cidr})"` to a separator the SafeText class admits — `$" ({network}-{cidr})"`, matching the convention `Create.cs:81` already uses. Verified: `-`, `.`, `(`, `)` are all inside the class, and the prefill then composes the coherent `rig-14-sn-a2-multi3 (10.20.40.0-24)-10.20.40.0-25`. These two are the only name-producing `/` sites in the repo (all other `({x}/{y})` interpolations are validation or error messages, none written to `Subnet.Name`). Additions:

1. **The pinned assertions are in three places, not two** — `AzureMultiPrefixImportCommitTests.cs:124-125` **and** `AzureMultiPrefixSubnetTests.cs:142`, plus fixture names at `:207`/`:270` for consistency.
2. **Add the recurrence guard that is missing.** `SubnetNamingSafeTextTests` pins `ToSafeText` character-by-character, but nothing asserts that a **generated** name satisfies `IsSafeText` — which is why round 13 reintroduced the character round 4 removed. Assert `new InputSanitizationService().IsSafeText(name)` over the planner's `BulkImportPlannedChildSubnet.Name` and `ResolveImportNames`' output. That is the cheap thing that stops a third occurrence.

**Do not take the finder's own interim instead of the fix.** Making `ToSafeText` map `/` to `-` would leave the app still generating names its Create form rejects, and changes long-standing behaviour for hand-typed parents ("Prod/Web" → "Prod-Web"), breaking the pinned `InlineData` at `SubnetNamingSafeTextTests.cs:42`. As a stop-gap *alongside* the decision it is acceptable — it stops the prefill inventing `(10.20.40.024)` — but it is not a substitute.

**Decision needed from you.** The suffix format is a naming/product call. Either **(a)** change the separator to `-` so generated names satisfy the app's own SafeText class — cosmetically different, three test assertions, and **no rename migration is needed because the code shipped one commit ago; the window to change it freely is now** — or **(b)** keep `/` in stored names and relax `[SafeText]` on `CreateSubnetViewModel.Name` to admit it (which Edit already effectively allows), accepting the security-review implication of widening a shared input class. Doing neither leaves the app generating names it refuses on input.

*Harm corrected downward:* `10.20.40.024` is unparseable gibberish, not a plausible-but-wrong range, so an operator reads it as a mangled name rather than as a false allocation. `NetworkAddress`/`Cidr` are correct everywhere and Details renders `10.20.40.0/25` truthfully. Nothing in the IPAM data model is wrong — hence low, not the "allocated range shown free" class.

---

## N8 — `FullyAllocatedNote.For` can build a note that `FullyAllocatedNote.Strip` is structurally unable to remove, so M3's stacking defect returns in full `[x2]`

**Severity:** low · **Confidence:** confirmed
**Citation:** `src/Bastet/Services/FullyAllocatedNote.cs:23` (`Strip` at `:36-48`, `IsNote` at `:76-82`)

**Failure scenario.** `For` interpolates the Azure subnet name with no whitespace normalisation, while `Strip`/`IsNote` split the description on `\n` and require a **single line** to both start with the prefix and end with the suffix. A name containing a newline therefore produces a note spanning two lines, neither of which satisfies both anchors, so no later `Strip` can ever remove it. `AzureImportSubnetViewModel.Name` inherits `[SafeText]` from `CreateSubnetViewModel` (`SubnetViewModels.cs:11`), whose class admits `\s` — which includes newline — and `SanitizeName` only trims the ends. An Admin posts `Subnet/BatchCreateChildSubnets` with `isAzureImport=true`, a fully-encompassing entry, and a name of `sn-A<LF>sn-B`. Result: **(1)** after `HostIp/SetAllocationStatus IsFullyAllocated=false` the row has `IsFullyAllocated=0` while its description still reads "Fully allocated by Azure subnet '...' which encompasses the entire address space." — the exact contradiction M3's un-mark mirror exists to eliminate; **(2)** each import→un-mark→import cycle appends another copy, which is M3's original defect verbatim. Azure subnet names cannot contain newlines, so the trigger is a crafted or replayed POST by an Admin — the same threat model `ResolveImportNames` is explicitly settled server-side against (`SubnetController.Azure.cs:244-247`).

**Reproduction** — own instance port 5361, catalog `bastet_rig14_advc5`. Name posted as a literal `sn-A<LF>sn-B` via `--data-urlencode 'subnets[0].Name@nl.txt'`; the server accepted it (302, no ModelState error):

```
SELECT Id, IsFullyAllocated, LEN(Description), REPLACE(Description,CHAR(10),'<LF>') FROM Subnets WHERE Id=1

  baseline        1|0|19 |Ops owns this range
  after import 1  1|1|107|Ops owns this range<LF>Fully allocated by Azure subnet 'sn-A<LF>sn-B' which ...
  after un-mark   1|0|107|<IDENTICAL — the note SURVIVED SetAllocationStatus IsFullyAllocated=false>
  after import 2  1|1|195|<operator line + TWO identical notes>
  after cycle 3   1|1|283|<operator line + THREE identical notes>
  final un-mark   1|0|283

Growth 88 chars per cycle, exactly M3's original arithmetic. Control: the operator line
"Ops owns this range" is preserved throughout, so Strip works normally — it is specifically
the two-line note it cannot see. Rendered Details shows three copies on a row whose
IsFullyAllocated is 0.
```

**Fix (verifier: sound).** One line at `FullyAllocatedNote.cs:23` — normalise the name before interpolation, e.g. `azureSubnetName?.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ')`. Every note becomes single-line by construction, so `Strip`'s whole-line anchoring becomes total without loosening it. `For` is the single choke point both call sites go through; the null case is unchanged; the four `[Theory]` cases pinning operator prose exercise `Strip`, not `For`, so they are untouched. Add a test asserting `Strip(Append(null, "a\nb", 1000))` is empty. **Do not** take the "cheaper interim" of collapsing newlines at the two producers — it leaves the helper able to build an unstrippable note for any future caller, as the finder himself notes. **Do not** "fix" this by tightening the `[SafeText]` pattern; that class is shared with host names and subnet names across the app.

**Decision needed from you.** Whether to do anything about rows that already carry an unstrippable two-line note. None can exist without someone having already sent a crafted POST, and round 13 explicitly declined a backfill for the analogous stacked-note residue — but the code fix alone leaves those rows permanently un-repairable except by hand-editing the description.

*Bounded harm:* no IPAM correctness impact (the `IsFullyAllocated` flag itself is written correctly, no range is shown free), no data loss (the overflow branch at `:71-73` keeps operator text whole and growth is capped at `MaxSubnetDescriptionLength`), and the row is hand-repairable via Edit. What is wrong is a free-text field asserting the opposite of the row's state, permanently un-removable by the app, plus unbounded restacking. A shade worse than M3's own Info rating because the residue is now un-strippable rather than self-healing.

---

## N9 — Both new `multiPrefixResourceIds` sets are built with a collection expression, silently discarding the `StringComparer.OrdinalIgnoreCase` on the `GroupBy` immediately above `[x1]`

**Severity:** low · **Confidence:** confirmed
**Citation:** `src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:509` (grouping `:511`, `Contains` `:527`) and `src/Bastet/Controllers/SubnetController.Azure.cs:250`

**Failure scenario.** `HashSet<string> multiPrefixResourceIds = [.. …GroupBy(s => s.Source.AzureResourceId, StringComparer.OrdinalIgnoreCase)…]` — the collection expression constructs a plain `HashSet<string>` with `EqualityComparer<string>.Default`, so the later `Contains` is case-**sensitive** even though the grouping that filled the set was case-insensitive. `GroupBy` keeps only the first member's spelling as `g.Key`, so every sibling row whose ARM id differs in case fails the `Contains` test and is not prefix-qualified. ARM resource ids are case-insensitive, so this is a legitimate variation; the wizards echo one server response, which puts the trigger at a crafted or replayed POST — precisely the case `ResolveImportNames`' own remarks say it exists to handle ("a crafted or replayed post carries whatever names it likes"). The hardening added for crafted posts is itself defeated by a crafted post. The intent is unambiguous: `used`, built three lines below the same collection expression at `SubnetController.Azure.cs:257`, `usedNames` at `AzureBulkImportPlanner.cs:486`, and both dictionaries at `AzureReconciler.cs:45-46` are all explicitly `OrdinalIgnoreCase`. These two collection expressions are the only ordinal resource-id collections in the Azure code.

**Reproduction** — own instance port 5211, catalog `bastet_rig14_vc11`. Only variable between runs is the casing of the `subnets`/`Subnets` segment on rows 2 and 3:

```
POST /Azure/BulkImportPreview
CONTROL (all ids identically spelled):
  'rig-14-sn-a2-multi3 (10.20.40.0/24)'  'rig-14-sn-a2-multi3 (10.20.5.0/24)'  'rig-14-sn-a2-multi3 (10.20.20.0/24)'
MIXED (rows 2-3 use .../Subnets/...):
  'rig-14-sn-a2-multi3 (10.20.40.0/24)'
  'rig-14-sn-a2-multi3'                    <- lost its prefix qualification
  'rig-14-sn-a2-multi3 (rig-14-vnet-a2)'   <- disambiguated by VNet, not by range

POST /Subnet/BatchCreateChildSubnets (same mixed casing) -> 302; persisted:
  3|rig-14-sn-a2-multi3|10.20.5.0|24        <- bare Azure name in the database
Control on the same endpoint with identical spellings: all three rows qualified by range.
```

**Fix (verifier: sound, compiled and run).** Replace both collection expressions with an explicit constructor: `HashSet<string> multiPrefixResourceIds = new([.. …], StringComparer.OrdinalIgnoreCase);` at `AzureBulkImportPlanner.cs:509` and `SubnetController.Azure.cs:250`. Verified in a throwaway net10.0 project: it compiles (the collection expression cannot convert to `int`, so the capacity overload is not a candidate) and yields `contains a/b = True` for a set built from a group keyed `A/b`. Two lines, no behavioural risk to the identical-casing path. **Do not take the offered interim** of `ToLowerInvariant` on ingest: that changes the `AzureResourceId` Bastet persists and displays on every import path — a data change with its own blast radius (`BelongsToSubscription` `StartsWith` checks, existing mixed-case rows) — and is strictly more expensive and more dangerous than the two-line comparer correction.

*Consequence corrected downward — strike the "distinguishable only by CIDR" claim.* Exactly **one** row per group loses its qualification (the `used`/`usedNames` fallback catches every later sibling), traced across 2-, 3- and 4-row groups and every ordering of spellings, so batch names stay mutually distinct and **no name collision occurs**. No prefix is dropped, `AzureResourceId`/`NetworkAddress`/`Cidr` are all correct, no range is shown free, and the reconciler is case-insensitive so nothing escalates there. The surviving harm is a documented naming rule applied **inconsistently** — one row keeps the bare Azure name, and in the bulk path a sibling is disambiguated by VNet name rather than by the range it holds, which is actively misleading about why it was renamed.

---

## N10 — Every selected VNet address prefix creates a target named for the bare VNet, so a VNet with two address prefixes persists as two Bastet subnets with the identical name and the identical `AzureResourceId` *(promoted from the watch list by the citation check)*

**Severity:** low · **Confidence:** confirmed
**Citation:** `src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:728` (callers at `:427` and `:444`; the commit that persists it at `src/Bastet/Controllers/SubnetController.BulkAzure.cs:365-369` and `:394-398`)

**Failure scenario.** `TargetName` returns the sanitised VNet name and nothing else — it never references which of the VNet's address prefixes the target holds. `BuildPlanItem` runs once per selected VNet address prefix, so every item for the same VNet carries the identical `AutoCreateTargetName`, and the commit creates one Bastet subnet per item with no cross-item name check: `usedNames` (`:486`) is per-item and only guards *child* names. A VNet with two address prefixes therefore persists **two top-level Bastet subnets with the same name**, both stamped with the same VNet `AzureResourceId`, distinguishable only by network address. Reachable in one click of "Select all"; no crafted payload. This is N6 one level up the tree: N6 is two same-named children of one Azure *subnet*, this is two same-named targets of one Azure *VNet*, and unlike N6 it fires on **every** multi-address-space VNet import, not only on a prefix-spanning subnet.

**Reproduction** — the same run recorded under N6 (own instance port 5193, catalog `bastet_rig14_verc3`, fixture `rig-14-b5p2-vnet`, prefixes 10.71.0.0/16 and 10.72.0.0/16). Rows 2 and 4 of that output *are* this defect, persisted:

```
POST /Subnet/BulkCreateFromAzurePlan -> {"success":true,"createdTargets":2,"createdChildSubnets":2}
  2 |rig-14-b5p2-vnet|10.71.0.0|16|NULL      <- two targets, identical Name,
  4 |rig-14-b5p2-vnet|10.72.0.0|16|NULL      <- identical AzureResourceId (the VNet)
```

**Fix.** Qualification has to be decided across the commit, not inside one item, exactly as N6's part 1 concludes: in `BuildPlan`, when more than one address prefix of the same VNet is selected, qualify each item's `AutoCreateTargetName` with the prefix it holds — the same `name (network-cidr)` shape N6 and N7 settle on, so all three stay consistent. Do **not** route this through `DisambiguateName`: it appends the *VNet name*, which is precisely the token that is already identical here, and its numeric fallback would produce `vnet`/`vnet (2)`, which says nothing about which range the row holds. The `ExactMatch` branch is unaffected — it adopts an existing row and does not name anything.

**Decision needed from you.** Whether to prefix-qualify `TargetName` when a VNet contributes several selected prefixes. It changes the names some installs see on their **next** import of a multi-address-space VNet (nothing already persisted is renamed). The alternative is a preview warning only — the wizard discloses that two targets will be created with the same name and the operator renames one afterwards by hand.

*Consequence:* low, on the same reading as N6 — every persisted range, parent link and Azure link is correct, nothing is misreported as free, and every render carries the address beside the name. What is wrong is a duplicated display label that no screen can explain.

*Why this is a finding and not a watch-list item:* it was parked in the watch list as "pre-existing on every build" and excluded from N6's fix as "out of scope", while the two rows above had already been **persisted and observed** in N6's own reproduction. Age, fix cost and blast radius are not severity inputs and are not grounds for withholding a reproduced defect from the findings; it is filed here at the severity its consequence warrants. Its `TargetName` half is therefore removed from N6's fix list and from the watch list.

---

# Refuted — reported by a finder, killed by the verifier

These were reported and then killed under verification. They are recorded so round 15 does not spend effort re-reporting them.

| id | Title | file:line | Why it was killed |
|---|---|---|---|
| R1 | Round 13's multi-prefix name qualification is scoped to a single request, so importing an Azure subnet's prefixes in two passes persists two Bastet rows under the identical name `[x1]` | `src/Bastet/Controllers/SubnetController.Azure.cs:250` | The claimed consequence is nil by the reporter's own measurement and by independent re-measurement: allocation data correct, no name-based lookup anywhere in the codebase, the address rendered beside the name on all six surfaces, reconciler reports 0 items. The "invariant" it says is defeated does not exist — two ordinary POSTs to `/Subnet/Create` with the same `Name` both succeed and persist identically-named siblings with **no Azure involvement at all**, because `Subnet.Name` is deliberately non-unique and no validator checks it. The state is sanctioned, reachable in two clicks without the cited code, and harmless: a duplicated display label, i.e. the "not a runtime defect, but the shipped fix is inconsistent" shape. (Distinct from **N6**, which survived: there the two rows share the same `AzureResourceId` and the qualification the code deliberately applies is silently skipped in a single commit.) |
| R2 | `GetCompatibleSubnets` treats `addressPrefix` and `addressPrefixes` as mutually exclusive while `ExtractIpv4Prefixes` unions them, so the single-VNet wizard would silently drop every extra prefix if ARM ever populated both `[x1]` | `src/Bastet/Services/Azure/AzureService.cs:172` | The cited branch cannot be entered with the stated input. ARM at api-version 2024-05-01 **refuses to store** `addressPrefix` and a multi-entry `addressPrefixes` simultaneously — that exact shape was PUT twice (new subnet, and onto an existing multi-prefix subnet) and ARM discarded the plural both times, and rejected duplicate plural entries with `DuplicateAddressPrefixesFound`. ARM is the sole producer of the `SubnetData` this method reads, so no real caller can reach the divergent path. End to end, `/Azure/GetSubnets` correctly returns both prefixes of a multi-prefix subnet, so the claimed harm (a dropped prefix later advertised as free space) does not occur. What remains is a code-consistency observation between a defensive extractor and one that mirrors the platform invariant. |
| R3 | The single-VNet import wizard displays one name and persists another: it posts the bare Azure subnet name and the server silently rewrites every multi-prefix row, with nothing on screen saying so `[x2]` | `src/Bastet/Views/Azure/Import/_ImportScripts.cshtml:330` (the posted `subnets[i].Name`; the label showing the same bare Azure name is at `:338`) | The headline consequence — the rename being invisible, "nothing on screen or in the success flash saying so" — is **false when measured**. The commit redirects to `Details/{parentId}` (`SubnetController.Azure.cs:483-486`), and that page's Child Subnets table lists `rig-14-sn-a1-multi2 (10.10.10.0/24)` and `… (10.10.30.0/24)` beside their CIDRs, in the same click, before the operator can act. The premise "the success flash is the only confirmation this wizard produces" is wrong. The write is correct, deliberate (round 13 M1, documented at `SubnetController.Azure.cs:236-247`), reversible via Edit, and preserves `AzureResourceId` verbatim. Both named second-order harms are empty: Bastet has no subnet search at all, and no code keys on `Subnet.Name` (the reconciler keys on resource id, the bulk planner on `{NetworkAddress, Cidr}`). What remains is one line of advisory UI copy on a row whose write is truthful and announced on the following screen — the exact disposition that killed round 13's C1 in this same file (round 13 cited `:338`, the label; the write this finding is about is the hidden input at `:330`). |
| R4 | Dead `ExtractIpv4Prefix` (first-prefix-only) survives beside the plural replacement M1 introduced `[x2]` | `src/Bastet/Services/Azure/AzureService.cs:411` | True observation, absent consequence. The method has a provably empty in-edge set (private static, zero call sites repo-wide; deleting it builds 0 warnings and passes 771/771), so no execution path in the shipping product reaches it: no request produces a wrong byte, no row is wrong, no range is misreported. Measured live against the very fixture cited, HEAD emits all three prefixes correctly — the "drops two /24s" output reported is the finder **hand-evaluating a method nobody calls**, not an observation of the software. The sole stated consequence is conditional on a future edit that does not exist. Round 11 killed `IInputSanitizationService.SanitizeString` on identical reasoning, and half of this is a re-raise: `docs/AUDIT-FINDINGS-7.md:881` already reports the same orphaned `<summary>` on the same method pair and records it killed at info. Refuted on **absence of any defect**, not on scope, cost or rarity — the deletion is trivial and was proved safe; it is a cleanup-sweep item. |
| R5 | Dead `TruncateForName` in the bulk import planner is a non-sanitizing near-duplicate of the live `TruncateAndSanitizeName` `[x2]` | `src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:790` | Dead private helper, zero callers, no reflection reachability: it never executes, so the software produces no wrong output or state because of it. The stated consequence is explicitly conditional on a hypothetical future edit ("A future planner edit that picks it…") — a bug someone might write later, not a bug present at HEAD. The supporting argument that "nothing in the build flags an unreferenced private method" is a tooling-gap remark of the same family. Refuted because there is no defect, not on scope, cost or rarity. |
| R6 | `IIpUtilityService` exposes a second `CalculateUnallocatedRanges` overload with no production caller that computes free space ignoring host IP assignments `[x1]` | `src/Bastet/Services/IIpUtilityService.cs:48` | No defect to reach. Full-repo grep (all extensions, excluding `bin`/`obj`/`.git`): the only non-test, non-declaration hit for `CalculateUnallocatedRanges` in the entire tree is `src/Bastet/Controllers/SubnetController.Read.cs:102`, which calls the **4-argument** form passing `subnet.HostIpAssignments`. No reflection dispatch anywhere in `src/`, no plugin surface, no name-based resolution; `IIpUtilityService` is an internal DI abstraction, not a published package. So no request, click or query in any deployment executes `IpUtilityService.cs:224`. The finder's "reproduction" consisted of writing a new test file that calls the overload directly — that is the finder supplying the caller whose absence is the whole finding. The claimed consequence is explicitly about code that does not exist. It is additionally not even latently wrong: the overload's XML doc on both interface and implementation states "taking into account child subnets" — it answers a different, well-defined question correctly, and empty host IPs is the documented semantic of that signature. |

---

# Watch list

Not findings. Only items a verifier could not **settle** — thin evidence, unproven reachability, or patterns that will bite later. Nothing reproduced is parked here; every reproduced defect above is filed at the severity its consequence warrants, regardless of fix cost.

- **No screen anywhere can edit or clear `Subnet.AzureResourceId`.** Grepped across every controller and view: the only writers are the two import commits. This is not itself a defect, but it is load-bearing for N1 — it is why routing a still-allocated row to `ReviewItems` strands it forever, and why the "re-link to Azure subnet X" action is the shape a correct fix eventually needs. Unsettled: whether any other flow silently depends on that column being immutable.
- **The natural trigger for N5 was never produced.** No non-injected `sp_releaseapplock` failure that leaves the SQL session alive was observed, and pausing the shared container to force one was out of bounds. The trigger class (acked command timeout; Azure SQL 10928/10929/40501) is argued from documented behaviour plus the maintainers' own comment at `Program.cs:459-461`, not measured. Anyone running BASTET on Azure SQL is the population where this becomes measurable.
- **The `Bastet:Migration` half of N5 was reasoned, not executed.** The pooling behaviour it rests on was measured for the subnet lock; the startup-abort consequence ("Another replica appears to be stuck applying migrations", after a 300000 ms wait) was not driven.
- **Overlapping RFC1918 space across VNets in one subscription is normal and the code is only partly ready for it.** `AzureReconciler.cs:68` already avoids `ToDictionary` for this reason; the rig itself ships `10.10.0.0/16` and `10.10.0.0/20` in one subscription. Any new prefix-keyed index (N1's fix is the immediate example) that assumes one owner per prefix string will throw and turn a scan into "The reconcile scan failed." This will bite again.
- **`EditSubnetViewModel.Name` has no `[SafeText]` while `CreateSubnetViewModel.Name` does** (`EditSubnetViewModel.cs:42-47` vs `SubnetViewModels.cs:8-14`). N7 is one consequence of that asymmetry; whether the divergence is deliberate was not established, and other paths that round-trip a name through Edit were not swept.
- **Nothing asserts that an application-*generated* name satisfies the application's own input rules.** `SubnetNamingSafeTextTests` pins `ToSafeText` character-by-character but never checks a produced name. That gap is exactly how round 13 reintroduced the character round 4 removed (N7). N7's fix proposes the guard; until it lands, a third occurrence is not prevented by anything.
- **`AzureReconcileStatus` has no inbound direction at all.** N3 and N4 are the two reproduced consequences of that; what was *not* settled is how many other operator-facing statements ("nothing to clean up", the free-space table, `IsFullyAllocated`) are scoped to the outbound direction without saying so on screen.
