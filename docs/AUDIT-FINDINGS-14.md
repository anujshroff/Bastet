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

## N1 — Reconcile archives a Bastet subnet whose CIDR is still allocated in Azure by a *different* Azure subnet, after which BASTET advertises the Azure-assigned range as free space — and re-import is then refused `[x2]`

**Severity:** critical · **Confidence:** confirmed
**Citation:** `src/Bastet/Services/Azure/AzureReconciler.cs:367` (and the identical hole at `:376-381`)

**Failure scenario.** Azure has no subnet rename, so re-organising one means delete-and-recreate. A VNet holds subnet `sn-a` with prefix `10.111.5.0/24`, imported into Bastet. `sn-a` is deleted and immediately recreated as `sn-a2` carrying the **same** prefix. `EvaluateSubnetLevel` keys only on the recorded ARM resource id (`:365-369`), so the Bastet row becomes `SubnetDeleted` — "The Azure subnet this was imported from no longer exists." That statement is *literally true*; the resource really is gone. What is missing is any range-level check. `ConfirmProposedDeletionsAsync` (`AzureController.cs:368-389`) asks ARM one question only — is this resource id gone — gets a genuine 404, and keeps the row. After approval the parent's Details page advertises the still-assigned `/24` as free with a **Create Subnet** button. The reconciler holds the contradicting evidence: `liveSubnetPrefixes`, built at `AzureReconciler.cs:64-71` from the same scan, contains `sn-a2 -> [10.111.5.0/24]`. The second route, `SubnetPrefixChanged` (`:376-381`), gets no direct ARM confirmation at all — `IsAbsenceStatus` (`:305-306`) covers only `VNetDeleted`/`SubnetDeleted` — so moving a prefix between two Azure subnets produces the same wrong output with even less friction.

**Reproduction** — own instance port 5196, catalog `bastet_rig14_verc7`, own live fixture `rig-14-verc7-vnet` (10.111.0.0/16):

```
BEFORE  GET /Subnet/Details/1 "Unallocated IP Ranges":
        10.111.0.0 - 10.111.4.255 (1,279)  |  10.111.7.0 - 10.111.255.254 (63,743)

az network vnet subnet delete ... -n rig-14-verc7-sn-a
az network vnet subnet create ... -n rig-14-verc7-sn-a2 --address-prefixes 10.111.5.0/24

POST /Azure/ReconcileScan ->
  items: [{subnetId:2, status:2, statusName:"SubnetDeleted",
           reason:"The Azure subnet this was imported from no longer exists."}]
  reviewItems: []  globalErrors: []  warnings: []  canCommit: true

POST /Subnet/BulkDeleteStaleAzureSubnets {"subnetIds":[2],"confirmation":"approved"}
  -> 200 {"success":true,"targetsDeleted":1,"subnetsArchived":1,"hostIpsArchived":0}

AFTER   GET /Subnet/Details/1:
        10.111.0.0 - 10.111.5.255 (1,535)   <- grew by exactly the 256 addresses ARM still assigns
```

Route B (`SubnetPrefixChanged`, no ARM confirmation at all) archived row 3 and left `/Subnet/Details/1` showing `10.111.0.0 - 10.111.255.255, 65,534 IP addresses` while ARM held three `/24`s assigned. While a sibling Azure-linked child survives, re-import is refused by both wizards: `GET /Azure/Import/1` → 302; the VNet prefix is `Blocked`/`isSelectable=false`; a hand-built `BulkImportPreview` returns `canCommit:false`. Once **all** children were archived the prefix returned to `WillUpdateExisting` — so it is un-re-importable while a sibling remains, not permanently unrecoverable.

**Fix (finder's proposal, corrected by the verifier — the original was incomplete on three counts).** Build a second index alongside `liveSubnetPrefixes` at `:64-71` mapping each live IPv4 prefix to its owning Azure subnet, and consult it before adding an item to `plan.Items`. Corrections:

1. **Do not build it with `ToDictionary`.** Two VNets in one subscription can carry overlapping address space (the rig itself ships `10.10.0.0/16` and `10.10.0.0/20`), so one prefix string has several live owners. Use `Dictionary<string, List<(ResourceId, SubnetName, VNetName)>>` with indexer/`TryAdd` accumulation — exactly as `:68` already avoids `ToDictionary` for the same reason. A `ToDictionary` here turns every scan of a subscription with duplicated private CIDRs into "The reconcile scan failed."
2. **Scope the match to the row's own VNet.** Overlapping RFC1918 across unrelated VNets is the norm; matching on the bare prefix string withholds genuinely stale rows. Parse the `/virtualNetworks/{name}/` segment out of `snapshot.AzureResourceId` (`AzureResourceIdentity` already handles these ids) and treat only a same-VNet live owner as evidence the range is still allocated — which is precisely where a rename or prefix move keeps the range.
3. **Routing to `ReviewItems` is a dead end as the application stands.** No screen can edit or clear `Subnet.AzureResourceId`; the only writers are the two import commits. Worse, `ApplyConfirmations` adds every `ReviewItem`'s `SubnetId` to the `withheld` set at `:263`, so `WithholdTargetsWhoseCascadeIsBlocked` would permanently withhold the parent VNet-level row too. **Do not ship the fail-closed half alone.** A dedicated status is also cheap and keeps the reason honest — reusing `ReviewItems` renders these rows with the misleading existing copy "Correct or clear the link on this subnet", which no screen can do.

**Interim mitigation (ship this now, under any of the three options).** One LINQ pass, ~15 lines: a `plan.Warnings` entry naming every proposed deletion whose CIDR is still live in the scanned inventory under a different Azure resource id in the same VNet — *"N subnet(s) below are proposed for deletion but their address range is still assigned in Azure to subnet 'X'; archiving them will make BASTET report an allocated range as free."* Put the same sentence on the item's own `Reason` so it appears next to the checkbox, not only in the banner. No new ARM calls, no change to what is deletable, no new stuck state.

**Decision needed from you.** Fail closed, warn loudly, or fail closed plus a new "re-link this subnet to Azure subnet X" action. Fail-closed alone strands the row and its ancestors forever (see correction 3). Fail-closed plus re-link is the correct end state and also fixes the un-re-importable-sibling case; it is the largest of the three. The warning ships under all three.

*Note on grading:* this is the same operator-visible output round 13 filed as High under M1. It is graded critical here because the wrong state is produced by an **irreversible archive** rather than by a display path, and because two independent routes reach it with zero warnings. If your scale caps range-misreporting at High, grade it there — but do not discount it for the rename trigger being uncommon.

---

# High

## N2 — Reconcile delete archives on a re-derived Azure verdict it never compares against the one the operator approved `[x1]`

**Severity:** high · **Confidence:** confirmed
**Citation:** `src/Bastet/Controllers/SubnetController.AzureReconcile.cs:78`

**Failure scenario.** The wizard deliberately carries each row's *reason* onto the last screen before the archive (`_ReconcileScripts.cshtml:396-408`: "a row whose own reason says the Azure resource still exists was confirmed under a heading saying it did not"). The commit then throws that approval away: `stillStale = plan.Items.ToDictionary(i => i.SubnetId)` and `noLongerStale = request.SubnetIds.Where(id => !stillStale.ContainsKey(id))` test only **set membership** in the re-derived plan, never that the row is stale for the reason the operator saw. `VNetDeleted`/`SubnetDeleted` are absence claims that must survive a direct ARM read; `VNetPrefixRemoved`/`SubnetPrefixChanged` are drift verdicts taken off the listing with no direct read at all (deliberately — see `AzureReconciler.cs:186-196`). A row that moves from the first class to the second between the confirmation screen and the click stays in `plan.Items`, the 409 never fires, and the subtree is archived. Real trigger: an IaC destroy/apply with a changed CIDR — ARM ids are path-based, so the recreated VNet has the same id. The endpoint's own docstring at `:15-18` promises the opposite: *"a resource that reappeared in Azure cannot cause the wrong subnets to be archived."*

**Reproduction** — own instance port 5199, catalog `bastet_rig14_v9c`, live fixture `rig-14-v9c-vnet`. Seeded Bastet subnet `V` 10.111.0.0/16 linked to the VNet, with a **manually created** child `prod-app-tier` 10.111.1.0/24 (no Azure provenance) and host IP `web01` 10.111.1.10:

```
az network vnet delete       -> rc 0 ; az network vnet show -> rc 3 (404)
POST /Azure/ReconcileScan    -> statusName "VNetDeleted",
   reason "The VNet this subnet was imported from no longer exists in Azure...",
   descendantCount 1, hostIpCount 1, canCommit true       <- what the operator approves

az network vnet create -n rig-14-v9c-vnet --address-prefixes 10.112.0.0/16   (same name = same ARM id)
re-scan (same session) -> [(1,'VNetPrefixRemoved',"VNet ... still exists but no longer has
                            the address prefix 10.111.0.0/16.")]

POST /Subnet/BulkDeleteStaleAzureSubnets {"subnetIds":[1],"confirmation":"approved"}
  -> HTTP 200 {"success":true,"targetsDeleted":1,"subnetsArchived":2,"hostIpsArchived":1}

SELECT COUNT(*) FROM Subnets            -> 0
SELECT ... FROM DeletedSubnets          -> prod-app-tier 10.111.1.0/24 ; V 10.111.0.0/16
SELECT COUNT(*) FROM HostIpAssignments  -> 0
```

The banner reads "Deleted 1 stale subnet(s)" — a staleness claim the server had disproved milliseconds earlier. There is no restore from `DeletedSubnets` (round 13 established this on the record).

**Fix (verifier: sound, with two tightenings).** Carry the approved verdict into the commit and refuse a mismatch, exactly as the bulk import commit already does for its plan (`DescribeApprovedPlanDivergences`, round 10's J2). Add `Statuses` (a per-id `{subnetId, statusName}` list) to `AzureReconcileDeleteDto`; snapshot `i.statusName` alongside `i.subnetId` at `_ReconcileScripts.cshtml:362` where `confirmedIds` is already frozen; at `SubnetController.AzureReconcile.cs:78` add the conjunct `stillStale[id].Status != approvedStatus[id]` returning the same 409 body, worded *"the reason N of the selected subnet(s) were flagged has changed since you reviewed them. Nothing was deleted. Re-run the scan."* Tightenings:

1. The status arrives as a caller-supplied string: parse defensively and treat an **unparseable** value as a divergence, not as "unverified" — mirror how `DescribeApprovedPlanDivergences` handles `TargetType`. Only a **missing** status counts as unverified-and-logged.
2. Comparing `Status` alone still lets a same-status/different-facts change through (a row approved as `SubnetPrefixChanged :: "now 10.111.1.0/24"` commits unchanged when the prefix has since moved again). If you want the guarantee the docstring claims, compare the server-derived `Reason` (or the observed Azure prefix) too, as the bulk path compares `ChildNames` in addition to `TargetType`.

Keep the refusal all-or-nothing, returned before any lock is taken, and put the mismatched ids in the `subnetIds` array so the wizard can highlight them. Do not merge the two messages — "no longer reported as deleted" and "flagged for a different reason" call for different operator actions.

**Interim mitigation (three lines, no DTO change).** At `:78`, also refuse when any selected id's re-derived status is a drift status (`SubnetPrefixChanged`/`VNetPrefixRemoved`) while `ConfirmProposedDeletionsAsync` was not asked about it — i.e. reject the absence→drift transition specifically, which is the only unprotected transition (every other class change moves the row into `ReviewItems` and out of `plan.Items`, which the existing 409 already catches). Cost: a plan that was drift-only from the start also 409s unless drift statuses are whitelisted.

**Decision needed from you.** Whether an out-of-band Azure re-create inside the confirm window becomes a 409 the operator must re-scan through (safer, converts a legitimate drift deletion into a retry), and whether the approved-status check is mandatory or optional-and-logged for callers using this as the documented direct JSON API — the same fail-open-and-log trade-off already accepted at `SubnetController.BulkAzure.cs:99-106`.

*Scope correction from the verifier:* the id-set archived equals the id-set approved, and every archived target is independently stale under the fresh verdict. The wrongness is that **consent was bound to a fact the server had disproved**, not that unselected rows were touched. Severity stays high: this is the only endpoint that deletes on the strength of what Azure reports, the removal is irreversible, and the operator would plausibly have chosen re-import over archive had the drift reason been shown.

---

## N3 — An IPv4 prefix Azure adds to an already-imported subnet is advertised by BASTET as free space, both import wizards refuse to import it, and the reconcile scan reports no differences `[x2]`

**Severity:** high · **Confidence:** confirmed
**Citation:** `src/Bastet/Services/Azure/AzureReconciler.cs:73`

**Failure scenario.** Ordinary sequence, no operator error, no crafted request. Bulk-import a VNet and its multi-prefix subnet. Azure later adds a third prefix to that same subnet (`az network vnet subnet update --address-prefixes ...`) — routine since multi-prefix subnets went GA. From that moment: the Details page lists a range **containing** the newly assigned `/24` under "Unallocated IP Ranges" with a **Create Subnet** button; `ReconcileScan` returns `items 0, reviewItems 0, warnings [], globalErrors []`; and neither import path can correct it — `GET /Azure/Import/{id}` 302s with *"Subnet must not have any child subnets or host IP assignments"* (`AzureController.cs:39-45`), and the bulk wizard marks the VNet prefix `Blocked` (`AzureBulkImportPlanner.cs:199`) which disables every subnet row underneath it, including the new row the very same response marks `Available / isSelectable: true`. The tool renders the discrepancy on one page while denying it exists on two others. Structural cause: every `AzureReconcileStatus` starts from a Bastet row carrying an `AzureResourceId` — `BuildPlan` iterates `linkedSubnets` at `:73` and never walks the inventory looking for Azure ranges BASTET has no row for.

**Reproduction** — own instance port 5342, catalog `bastet_rig14_verc1`, live fixture `rig-14-verc1-vnet` (10.90.0.0/16), subnet with prefixes `10.90.200.0/25` + `10.90.200.128/25`:

```
bulk import -> {"success":true,"createdTargets":1,"createdChildSubnets":2}
az network vnet subnet update ... --address-prefixes 10.90.200.0/25 10.90.200.128/25 10.90.77.0/24

POST /Azure/ReconcileScan -> {"scanSucceeded":true,"items":[],"reviewItems":[],
                              "globalErrors":[],"warnings":[],"canCommit":false}

GET /Subnet/Details/1 "Unallocated IP Ranges":
  10.90.0.0   10.90.199.255   51,199 IP addresses   [Create Subnet]   <- contains 10.90.77.0/24
  10.90.201.0 10.90.255.254   14,079 IP addresses   [Create Subnet]

GET /Azure/Import/1                 -> 302 /Subnet/Details/1 ("must not have any child subnets")
GET /Azure/BulkGetVNets             -> PREFIX 10.90.0.0/16 Blocked isSelectable false
                                       SN 10.90.77.0/24 Available isSelectable TRUE
POST /Azure/BulkImportPreview (crafted: ONLY the new prefix)
  -> errors ["Cannot import VNet prefix 10.90.0.0/16: matched Bastet subnet ... already has child subnets."]
POST /Subnet/BulkCreateFromAzurePlan -> HTTP 400, same error   (the gate is server-side, no API back door)

THE HARM, EXECUTED:
POST /Subnet/Create Name=team-web-block NetworkAddress=10.90.77.0 Cidr=24 ParentSubnetId=1
  -> 302 /Subnet/Details/4 ; row persisted, AzureResourceId NULL
BASTET allocated a /24 Azure had already assigned, with no warning.
POST /Azure/ReconcileScan (with the double-allocation live) -> items [] warnings [] globalErrors []
```

**Fix (finder's proposal, corrected by the verifier — part (a) was underspecified and part (b) is dangerous).**

**(a) The inbound verdict cannot be built from `BuildPlan`'s current inputs.** `BuildPlan` only receives `linkedSubnets`, and `AzureSubnetSnapshotService.GetAzureLinkedSubnetsAsync` (`:52`) filters to rows with a non-empty `AzureResourceId`. Proved false-positive: the hand-created `team-web-block` above exactly accounts for the Azure prefix but never enters `linkedSubnets`, so the proposed loop would report `10.90.77.0/24` as "not imported" forever. Two changes are required: **(i)** extend `IAzureReconciler.BuildPlan` to also take the full tree — `IAzureSubnetSnapshotService.GetExistingSubnetsAsync()` already returns every subnet and is already called by the bulk path; **(ii)** match by **containment, not `{network,cidr}` equality** — an IPAM routinely records a coarser allocation (Bastet has `10.90.64.0/18`; Azure carves `10.90.77.0/24` inside it), and that range *is* accounted for. Report only when no Bastet subnet contains the Azure prefix, and scope the walk to VNets at least one of whose prefixes maps to an existing Bastet target, or an unimported subscription produces an item per Azure subnet on every scan.

**(b) Do not relax the two import gates as proposed.** `AnnotatePrefix:197-200` / `BuildPlanItem:379-383` block an exact-match target that has children for a reason that survives this finding: the commit path also renames the target, stamps `AzureResourceId` and can set `IsFullyAllocated` (`SubnetController.Azure.cs:397-399,404,410`). Loosening them to "block only when the selection would create a row that already exists" lets an import adopt and re-stamp a target whose children were created by hand — the state round 13's C4 analysis showed lets a later reconcile cascade archive non-Azure rows. See N4 for the narrower alternative.

**(c)** The interim warning is the right first move, with the same full-tree + containment input as (a), or it fires on ranges Bastet legitimately accounts for and operators learn to ignore it.

**Also worth doing regardless:** `BulkGetVNets` returns `10.90.77.0/24` as `Available / isSelectable: true` under a prefix it marks `Blocked / isSelectable: false` **in the same response**. Whatever is decided about the gates, mark a subnet row unselectable-with-a-reason when its own VNet prefix is blocked, so the wizard stops offering a row nothing can act on.

**Interim mitigation.** A `plan.Warnings` entry (not an item) — *"Azure subnet 'X' owns 10.90.77.0/24, which no Bastet subnet records. BASTET will report that range as free."* Warnings are already rendered on the reconcile review screen, are never deletable, and `nothingToReport` (`_ReconcileScripts.cshtml:287`) already includes `warnings.length === 0`, so this correctly suppresses the "nothing to clean up" banner without touching the deletion path. ~30 lines, no schema, no UI change.

**Decision needed from you.** (1) Does the reconciler get an inbound verdict at all — a new `AzureReconcileStatus`, the wider `BuildPlan` input, and a report-only never-deletable review row? Roughly a day plus tests; the cheap half ships first and is independently useful. (2) Should there be any supported path to pull new Azure prefixes into an already-imported target — covered in N4.

*Narrative correction:* the JSON is `items 0, warnings []`, but the green banner it drives (`_StepReview.cshtml:30`) reads "Everything imported from this subscription still exists in Azure. There is nothing to clean up." — carefully scoped to the outbound direction and not literally false. The defect is that the only comparison feature in the product has **no inbound verdict at all**, so the free-space page lies unopposed.

---

## N4 — An Azure subnet that gains an IPv4 prefix after import can never be imported by either wizard, and the Details page keeps offering the Azure-assigned range as free `[x2]`

**Severity:** high · **Confidence:** confirmed
**Citation:** `src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:199` (server-side mirror at `:382`; single-VNet gate at `AzureController.cs:38-44`)

**Failure scenario.** N3 is the reconciler's blindness; this is the import side of the same dead end, and it is a separate change with a separate owner decision. `AnnotatePrefix:199` blocks the whole VNet prefix — "Bastet subnet 'X' already has child subnets. Already imported?" — and `_BulkScripts.cshtml:249` sets `.prop('disabled', !subnet.isSelectable || !prefixInfo.isSelectable)`, so the one genuinely-new subnet row is un-tickable; `buildSelectionFromUI` only walks *checked* prefixes, so it can never be submitted. A hand-crafted POST is refused server-side by the mirror at `:382`. The single-VNet wizard refuses at the door. The identical dead end is reached by a second, arguably more likely route that starts **inside the reconcile wizard**: remove a prefix in Azure, reconcile correctly offers the drift row, the operator approves and it is archived — then Azure restores the prefix. Both wizards then refuse to bring it back, and the Details page advertises the live Azure-assigned range as free.

**Reproduction** — own instance port 5219, catalog `bastet_rig14_verc2`, own fixture `rig-14-verc2-vnet` 10.90.0.0/16:

```
bulk import (real endpoints) -> {"success":true,"createdTargets":1,"createdChildSubnets":3}
az network vnet subnet update ... -n rig-14-verc2-sn-multi \
   --address-prefixes 10.90.100.0/24 10.90.101.0/24 10.90.102.0/24

GET /Azure/BulkGetVNets (verbatim):
  PREFIX 10.90.0.0/16      Blocked False  "Bastet subnet ... already has child subnets. Already imported?"
  SN  10.90.100.0/24       AlreadyImported False
  SN  10.90.101.0/24       AlreadyImported False
  SN  10.90.102.0/24       Available       True     <- offered, but un-tickable (parent blocked)

crafted POST (only the new prefix): preview canCommit False ; commit HTTP 400 (BuildPlanItem:382)
GET /Azure/Import/1 -> 302 /Subnet/Details/1
POST /Azure/ReconcileScan -> items [] reviewItems [] warnings [] globalErrors []

GET /Subnet/Details/1 "Unallocated IP Ranges":
  10.90.0.0    10.90.4.255     1,279    Create Subnet
  10.90.6.0    10.90.99.255   24,064    Create Subnet
  10.90.102.0  10.90.255.254  39,423    Create Subnet   <- over the /24 Azure just assigned

escape hatch: POST /Subnet/Create 10.90.102.0/24 under parent 1 -> 302, row persists,
              AzureResourceId NULL (only the two import paths ever write that column)
```

**Fix (verifier: incomplete — direction right, four gaps).** Narrow the `HasChildSubnets` hard stop to a **top-up import**: when the matched target already has children, keep the prefix selectable and let `AnnotateSubnet` do the discriminating it already does correctly (measured: 3 rows `AlreadyImported`, 1 `Available`). Keep the existing refusals for a target that is fully allocated, has host IPs, or is linked to a different VNet. Gaps to close first:

1. **Scope the exact-match target's side effects.** A selectable ExactMatch prefix drives `SubnetController.BulkAzure.cs:346` (`targetSubnet.AzureResourceId = sanitizedVNetResourceId`) and, when a child fully encompasses the prefix, `WillMarkFullyAllocated`. Re-stamping the same VNet id is idempotent (a different VNet stays blocked at `AzureBulkImportPlanner.cs:216-223`/`:397-401`), but marking a target `IsFullyAllocated` when it already has children is a contradictory write the existing gate has been incidentally preventing. Add an explicit refusal.
2. **The blocked prefix must become `WillUpdateExisting` with distinct preview copy** — how many children will be created, and that existing ones are untouched — not the first-import wording "Will import into existing Bastet subnet 'X'", which reads as a re-import. `BuildPlanItem`'s `renameMatched` path also needs deciding: renaming a populated target on a top-up is almost certainly not wanted.
3. **The single-VNet wizard is not fixed by this change.** `AzureController.Import` refuses on `subnet.ChildSubnets.Count != 0` before the planner is consulted. Either narrow that gate the same way, or state explicitly that top-up is bulk-wizard-only — otherwise the headline "neither wizard" stays half-true after the fix.
4. **The reconciler half (N3) is what stops this recurring and should not be optional.** Without an inbound status the model silently re-diverges on the next Azure change.

**Interim mitigation — put it on the Details page, not the wizard.** Do **not** ship the finder's proposed "extend the Blocked reason to name the un-imported prefixes" alone: it requires reordering `AnnotateAvailability` (prefixes are annotated before subnets, `:164-169`), and it puts the warning on the one screen the operator has no reason to visit while `/Subnet/Details/{id}` keeps printing the allocated range under "Unallocated IP Ranges" with a Create Subnet button. For an Azure-linked subnet, the free-space table is the thing that is wrong.

**Decision needed from you.** Whether BASTET supports a top-up import at all — importing new Azure prefixes into a target that already has children — versus keeping "a populated target is never an import destination" and only *detecting and reporting* the divergence (N3's reconciler status plus a Details-page warning), leaving the operator to create the range by hand. The first is a change to the planner's target rules plus preview copy plus the single-VNet gate; the second is smaller, weakens no existing guard, but leaves the correction manual and the resulting row permanently unlinked from Azure. **Either way the free-space lie on `/Subnet/Details/{id}` needs closing; that part is not optional.**

*Two narrative corrections:* (a) "no in-app path back to a correct model" is too strong — `POST /Subnet/Create` does create the missing range (measured); the correct statement is "no path back to an *Azure-linked* correct model, and nothing in the application tells the operator the model is wrong". (b) The multi-prefix GA feature is the common **trigger**, not the mechanism — any Azure change that adds an unmodelled range under an already-imported VNet prefix reaches the same dead end.

---

# Medium

## N5 — A failed `sp_releaseapplock` is swallowed on a documented invariant that is false: closing the connection returns the session to the pool, so the global subnet write lock stays held and every replica's writes fail for minutes with "high concurrency" `[x1]`

**Severity:** medium · **Confidence:** **plausible**
**Load-bearing step that could not be established:** no **natural** (non-injected) failure of `sp_releaseapplock` that leaves the SQL session alive was produced. Every fault applicable on the shared rig also kills the session, which releases the lock safely. The trigger class is real rather than invented — a command timeout whose ATTENTION is acked, and Azure SQL 10928/10929/40501 resource-governance errors, all return a `SqlException` on a still-open connection, and the maintainers' own comment at `Program.cs:459-461` records a release failure reproduced against a real server. **Everything downstream of the trigger was measured**, with the failure injected: that the lock survives return-to-pool, for 4+ minutes, and that the peer replica then fails for 30 s with a false message. The migration-lock half was reasoned from the same measured pooling behaviour, not executed.
**Citation:** `src/Bastet/Services/Locking/SqlServerSubnetLockingService.cs:96` (swallowing catch at `:98-101`, false comment at `:91-93`, outer finally at `:104-107`); same false comment at `Program.cs:461-463`.

**Failure scenario.** Two replicas against one database — the multi-replica shape `Program.cs` and the DataProtection key ring exist to support. Replica A serves `POST /Subnet/Create`; `sp_getapplock 'Bastet:SubnetOperations'` is taken **Session**-owned on the request's EF connection; the subnet is committed; `sp_releaseapplock` fails for any reason that leaves the connection usable. The catch logs and continues on the stated grounds that "if it is alive the outer finally closes it anyway". **It does not.** `context.Database.CloseConnectionAsync()` returns the connection to SqlClient's pool; the SQL session stays open; a Session-owned application lock is dropped only when the pooled connection is next *used* (`sp_reset_connection`) or physically destroyed. During that window every subnet/host-IP write on replica B parks 30 s inside `sp_getapplock` and fails — interactive forms with *"The operation timed out due to high concurrency. Please try again."*, Azure endpoints with 503 "another subnet operation is in progress" — none of which is true. The operator's only offered remedy ("try again") keeps failing. The same false claim governs the `Bastet:Migration` lock; on the bootstrap path that connection lives in a master-catalog pool the app never reuses, so a stranded migration lock persists for the process lifetime and later cold starts burn the full 300000 ms `@LockTimeout` before aborting with "Another replica appears to be stuck applying migrations."

**Reproduction** — scratch copy of HEAD at `$RIG/ver-c8/repo` (nothing written into the repo; `git status --porcelain -uall` empty afterwards). Only change: a one-shot `throw` at the top of `ReleaseAppLockAsync`, gated on `VERC8_FAULT=1`. Two replicas, ports 5231/5232, catalog `bastet_v14c8`:

```
POST /Subnet/Create (replica A) -> 302 /Subnet/Details/1 in 0.118 s   (row committed)
log: "Failed to release the subnet operation lock after the operation completed"

APPLOCK_TEST('public','Bastet:SubnetOperations','Exclusive','Session')
  before POST -> 1 (free)      after POST -> 0 (HELD, request finished, DbContext disposed)
sys.dm_tran_locks  -> session 96, 0:[Bastet:SubnetOperations]:(b83d66a6)
sys.dm_exec_sessions -> status 'sleeping', program_name 'EFCore/10.0.10'   <- alive in the pool

POST /Subnet/Create (replica B) -> 200 in 30.049 s,
  rendered: "The operation timed out due to high concurrency. Please try again."
  Subnets table: only vc8-a. Nothing written. Repeated 5 minutes later: identical.

Held on an idle holder: t+11s .. t+297s -> APPLOCK_TEST 0 throughout (4m12s watched).
Holder self-heals: 5 read-only GETs on replica A -> lock free, A's next create 302 in 0.026 s.
Replica B has no way to cause that; it can only block 30 s and fail.

PROPOSED FIX, same rig, same injected fault (SqlConnection.ClearPool in the catch):
  create -> 302 in 0.183 s ; log still records the failed release
  APPLOCK_TEST pre=1 post=1 post+5s=1        <- NOT stranded
  replica B create immediately after -> 302 in 0.134 s   (was 30 s / failed)
```

**Fix (primary sound and verified running; the secondary half was corrected).** In the catch at `:98`, destroy the physical connection so the SQL session — and with it the Session-owned lock — actually ends:

```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Failed to release the subnet operation lock; discarding the pooled connection so the session-owned lock is dropped");
    if (context.Database.GetDbConnection() is SqlConnection stranded)
    {
        SqlConnection.ClearPool(stranded);
    }
}
```

`using Microsoft.Data.SqlClient;` is already at the top of the file, so the fully-qualified names in the original proposal are unnecessary. Two things the proposal omitted: **(a)** `ClearPool` empties the **whole** pool for that connection string — a transient reconnect cost paid only on this error path, and there is no per-connection "do not pool" API in Microsoft.Data.SqlClient, so this is the only public mechanism; **(b)** the log message must stop asserting the false invariant, and the comments at `:91-93` and `Program.cs:461-463` must be corrected — closing an EF/ADO connection returns it to the pool, it does not end the session, and a session-owned applock outlives it.

**Correction to the secondary fix.** Do **not** add `Pooling=false` inside `MigrationLockConnectionString.Configured`: that method's documented contract is the connection string returned verbatim, `MigrationLockConnectionStringTests.cs:40` asserts `Assert.Equal(connectionString, Configured(connectionString))` and would fail, and routing it through `SqlConnectionStringBuilder` also destroys the deliberate null-in/null-out behaviour documented at `MigrationLockConnectionString.cs:31-37`. Use the same remedy as the service — `SqlConnection.ClearPool(migrationLockConnection)` inside the existing catch at `Program.cs:475-480`, before the `using` disposes it. That covers both branches uniformly and touches no unit-tested contract.

**Interim mitigation.** Raise `SqlServerSubnetLockingService.cs:100` from `LogError` to `LogCritical` and say what it means — *"the global subnet lock may be stranded on a pooled connection; restart this replica if subnet operations start timing out"* — so the 30-second "high concurrency" failures that follow are diagnosable instead of being misattributed to load.

**Decision needed from you.** Whether the pool-wide blast radius of `ClearPool` on the release-failure path is acceptable: the replica pays a burst of TCP+TLS+login handshakes right after a failed release. Alternatives: (b) leave the lock stranded and make the peer's failure honest — "the lock is held by another replica's stale session" plus a documented `sp_releaseapplock`/KILL runbook; or (c) change the lock's ownership model, which the class remarks at lines 13-20 explicitly rejected for good reasons. **(a) is cheapest and is the one verified to work.**

*Scope narrowed by the verifier:* the holding replica self-heals on its very next query, so single-replica deployments recover almost immediately. The damage that persists is **cross-replica** — replica B has its own pool and can do nothing to reset A's session, so B's writes stay broken until A happens to touch that one connection: unbounded if A is idle, drained, or scaled to zero. Medium is right — writes are denied and the error message is a lie, but nothing is corrupted and no allocated range is ever reported free.

---

# Low

## N6 — Bulk import's multi-prefix name qualification is scoped to one VNet address prefix, so an Azure subnet whose prefixes fall under different VNet prefixes persists as two Bastet rows with the identical name and the identical `AzureResourceId` `[x2]`

**Severity:** low · **Confidence:** confirmed
**Citation:** `src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:509` (grouping at `:511`, `usedNames` at `:486`, `Contains` at `:527`, `DisambiguateName` at `:533`)

**Failure scenario.** A VNet has two address prefixes (10.71.0.0/16, 10.72.0.0/16) and one Azure subnet owns one IPv4 prefix in each. `BuildPlanItem` runs once per selected VNet address prefix, and `multiPrefixResourceIds` is built only from **that prefix's** subnet rows — so each item sees exactly one row for the resource id, `Count() > 1` is false, and no prefix qualification is applied. `usedNames` is also per-item and seeded only from the item's own target names, so `DisambiguateName` cannot catch it either. Result: two child subnets with the **same name** and the **same Azure subnet resource id**, distinguishable only by CIDR — precisely the state round 13's M1 naming work exists to prevent. Reachable in one click of "Select all"; the wizard groups subnet checkboxes under prefixes by containment, so no crafted payload is needed.

**Reproduction** — own instance port 5193, catalog `bastet_rig14_verc3`, existing fixture `rig-14-b5p2-vnet`:

```
ARM: prefixes ["10.71.0.0/16","10.72.0.0/16"], subnet rig-14-b5p2-sn-span
     addressPrefix null, addressPrefixes ["10.71.5.0/24","10.72.5.0/24"]

POST /Azure/BulkImportPreview (exact payload buildSelectionFromUI produces for "select all")
  -> 200, canCommit true, globalErrors []
     item 10.71.0.0/16 -> childSubnets[0].name "rig-14-b5p2-sn-span"   (UNQUALIFIED)
     item 10.72.0.0/16 -> childSubnets[0].name "rig-14-b5p2-sn-span"   (UNQUALIFIED)

POST /Subnet/BulkCreateFromAzurePlan -> {"success":true,"createdTargets":2,"createdChildSubnets":2}
  2 |rig-14-b5p2-vnet   |10.71.0.0|16|NULL
  3 |rig-14-b5p2-sn-span|10.71.5.0|24|2
  4 |rig-14-b5p2-vnet   |10.72.0.0|16|NULL
  5 |rig-14-b5p2-sn-span|10.72.5.0|24|4
rows 3 and 5 carry the SAME AzureResourceId .../subnets/rig-14-b5p2-sn-span

Counter-test that narrows the finding: splitting an ordinary multi-prefix subnet (both prefixes
inside ONE VNet prefix) across two sessions was REFUSED — "Cannot import VNet prefix 10.10.0.0/16:
matched Bastet subnet 'rig-14-vnet-a1' ... already has child subnets." So the cross-session route
needs the same spanning fixture; the two claimed routes are one trigger, not two.
```

**Fix (verifier: part 1 sound with one correction, part 2 unsound as written, part 3 split out to N10).**

1. **Hoist the grouping out of `BuildPlanItem` into `BuildPlan`**, computing `multiPrefixResourceIds` once across every selected prefix of every VNet, and pass the set in. That is the whole of the single-session fix. **Keep the `!s.FullyEncompasses && !string.IsNullOrEmpty(s.Source.AzureResourceId)` filter that `:510` has today** — the proposal's wording drops it, and this fixture disproves M1's recorded assumption that an encompassing prefix cannot have a sibling prefix on the same Azure subnet (a subnet may equal VNet prefix 1 exactly and still hold a prefix inside VNet prefix 2). Without the filter, the encompassing selection would inflate the group to 2 and the one child that *is* created would be needlessly renamed.
2. **Do not seed `usedNames` from the existing tree.** `usedNames` feeds `DisambiguateName`, which appends the **VNet name**, not the prefix — so the cross-session second row would land as `name (vnet-name)`, a different and inconsistent shape from the single-session `name (10.72.5.0/24)`. Worse, seeding from the whole `existingSubnets` list makes every ordinary import rename any child whose Azure name matches **any** existing Bastet subnet anywhere in the tree, including unrelated branches — a broad silent rename regression in the common path, exactly what M1 was careful to avoid. **Correct targeted fix:** in `BuildPlanItem`, prefix-qualify a planned child when `existingSubnets` already contains a row whose `AzureResourceId` equals `sub.Source.AzureResourceId` and whose `{NetworkAddress, Cidr}` is not this selection's. Same `name (network/cidr)` shape, fires only for the real multi-row case, needs no DTO or ARM change (the commit never re-queries Azure and `BulkImportSelectedSubnetDto` carries no prefix count). The already-persisted first row keeps its bare name and remains unambiguous because the new row is qualified.
3. **The `TargetName(p)` half is a separate reproduced defect, filed as N10 — not deferred.** Two same-named Bastet targets for a two-address-space VNet were persisted in this very run (rows 2 and 4 of the output above). It is a different code path with its own owner decision, so it is fixed there rather than here; it is not a hole in round 13's guard, which is why it is a finding of its own rather than part of this one.

*(If you take N7's separator change, note that the qualification suffix produced here should use it too.)*

**Interim mitigation.** In `DetectExistingBastetSubnetConflicts`/`AnnotateSubnet`, warn per item when a planned child's final name equals an existing Bastet subnet name under the same target or another planned child's name in the same commit, so the preview discloses the collision before the operator confirms. Sound and cheap — but it is a disclosure, not a fix.

**Decision needed from you.** (1) Whether prefix-qualified names apply across the whole commit and across sessions — the fix changes names some installs see on their **next** import of such a VNet (nothing already persisted is renamed) — versus shipping only the preview warning. (2) The `TargetName` question — whether a target should be prefix-qualified when a VNet contributes several selected prefixes — is N10's decision, taken on its own merits.

*Consequence corrected downward by the verifier:* "no way to tell which range they are acting on from the name alone" is overstated — every render carries the address beside the name (`_SubnetTreeItem.cshtml:17-18`, `_ChildSubnets.cshtml:44-45`, `Delete/_WarningAlert.cshtml:6`, `AllHostIps.cshtml:53`, and all three reconcile lists). Every persisted range, parent link and Azure link is correct; nothing is misreported as free. This is a **broken deliberate invariant with a display-ambiguity consequence**, hence low — a judgement on consequence, not on rarity.

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
