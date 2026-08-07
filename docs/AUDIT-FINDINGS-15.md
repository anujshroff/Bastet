# Bastet — Round-15 Audit Findings

**Target branch:** `audit/round-15` · **HEAD:** `d84fda0` · **Baseline:** 847 tests passing, 0 build warnings · **Date:** 2026-08-04 · **Round letter:** O (findings O1–O16)

---

## Verdict

**Eight findings need a decision from you and here is what it costs.** Read this table before anything else in the file; it is the part that cannot be delegated.

| Finding | The decision | What it costs |
|---|---|---|
| **O1** (critical) — the range guard matches prefixes byte-for-byte | When a live Azure subnet **overlaps** but does not equal a row's recorded range, should the row be withheld to review, and should it get the **Re-link** button? | Re-linking would point the row at a subnet holding a *different* range, producing `SubnetPrefixChanged` on the very next scan — the same defect on a loop. Review-only is the conservative shape, but round 14's watch list already records that a review row is **stranded**: no screen in the app can edit `AzureResourceId`. The only exit is delete-and-re-import, measured to work. Say which. |
| **O2** (critical) — VNet-level rows get no range check at all | Does the VNet-level branch get its **own status and its own index**, or is the cheap containment guard enough? | The cheap guard is defeated by a re-carve that preserves total coverage (measured). The sound version needs a new `AzureReconcileStatus` member, its client label, and a **separate** index — sharing `livePrefixOwners` would let one Re-link click permanently point a child row at a VNet. Also: rows already archived by this path are **unrecoverable**, and after a resize the importable prefix is the new one, so re-import is not guaranteed. |
| **O3** (critical) — Re-link writes a subnet id onto a VNet-level target | Anything for rows **already** corrupted? `Subnet.AzureResourceId` has no editor anywhere in the application. | The code fix is smaller than filed (four lines, verified). The residue is not: a corrupted row cannot be repaired through the UI, and once archived it cannot be repaired at all — `DeletedSubnets` has no restore and the archive drops `AzureResourceId` (round 13 `C6`). A one-off admin repair action or release-note SQL is the only route. |
| **O4** (high) — an aggregate parent silences the whole inbound check | Two sub-decisions the fix cannot make for you: does a BASTET row **exactly equal** to the VNet prefix account for everything inside it, and when **no** VNet prefix contains an Azure range, does the check fail open or closed? | Both are one line each, but `IsSubnetContainedInParent` is **strict**, so the "equal" case falls out as an artefact rather than a choice unless you state it. Fail-open on the null case is conservative for partial-RBAC inventories; fail-closed starts reporting ranges outside every declared address space. |
| **O5** (high) — a linked-but-not-fully-allocated target accounts for its own range | Accept that the new inbound item is **unclearable** when the target already has children? | Measured: the top-up import refuses with *"…covers the whole prefix, which would mark Bastet subnet 'X' fully allocated, but it already has child subnets."* The item is **true** in that state, so this is correct behaviour — but the operator only discovers the remedy by opening the import wizard. Putting it in the item's own `Reason` is the fix; leaving it is defensible. |
| **O8** (low) — a top-up renames the target back to the VNet name | Should a top-up stop renaming — and if so, the success flash must change with it. | The write suppression is one line. Leaving the flash alone makes the app announce *"Successfully renamed parent subnet to 'X'"* for a rename it did not perform — the exact anti-pattern this same file refuses at `SubnetController.Azure.cs:188-191`. Behaviour change either way. |
| **O11** (low) — bulk preview certifies a plan the commit refuses | Add the two containment rules to the planner, or only make the failure attributable? | The full fix has a measured hazard: a naive more-specific-parent test reading only `existingSubnets` would **refuse imports that commit 200 today** (counter-example built). The scoped version is correct but larger. The interim — populate `itemErrors` so the wizard can point at the offending row — is a few lines and does not fix the false `canCommit`. |
| **O13** (low) — anonymous `GET /Account/Logout` ends the IdP session | Should the remote `SignOutAsync(OpenIdConnect)` leg run for a caller with **no** session? | Gating it on `User.Identity?.IsAuthenticated == true` confines it; leaving it also cleans up an IdP session that outlived an expired Bastet cookie. Both defensible; the round is not choosing for you. |

Underneath three of those decisions sits one data-model gap worth naming on its own: **`DeletedSubnets` has no restore path and `Subnet.AzureResourceId` has no editor.** Round 13 established the first and round 14's watch list established the second. This round they stopped being observations — they are the reason O1, O2 and O3 are graded critical rather than high, because in each one the wrong output is not merely wrong, it is *terminal*.

**Read O1 first.** Round 14's `N1` added a guard so reconcile would never archive a row whose range Azure still holds. That guard compares prefix **strings**. Azure has no subnet rename, so re-organising one is delete-and-recreate, and re-carving a prefix at the same time is ordinary — and when it happens, one event produces two rows with two different verdicts. The row whose prefix string is unchanged is protected, named in a warning, and offered a Re-link. The row whose prefix was re-carved from `10.191.20.0/24` to `10.191.20.0/25` is offered for deletion with the reason *"The Azure subnet this was imported from no longer exists."* — and `grep -c "10.191.20.0/25"` over the entire saved plan JSON returns **0**. The operator approves an irreversible archive on a plan that states no fact whatsoever about the range Azure is holding at that moment. Afterwards `/Subnet/Details/1` prints `10.191.0.0 – 10.191.21.255, 5,631 IP addresses` with a **Create Subnet** button over 128 addresses ARM has assigned.

O1, O2 and O3 are one family: the guard compares strings (O1), the guard never runs on VNet-level rows (O2), and the guard's own repair button writes the wrong resource id (O3). O1 is the most ordinary trigger and is `[x2]`; O2 and O3 have the worse terminal states — in both, the scan run immediately after the archive returns `items 0, reviewItems 0, warnings []` and will do so **for ever**, because `ReportAzureRangesNoBastetSubnetRecords` is scoped to VNets named by a surviving row's `AzureResourceId` and archiving the last such row empties that set. O1 at least reports the range on the next scan.

**Four more findings put an allocated range in front of an operator as free space** (O4, O5, O6, and the residue of O1–O3). O4 is the widest: one hand-created `10.0.0.0/8` above an Azure import target — ordinary IPAM practice, and something forced parenting guarantees exists whenever an install models a top-down plan — makes the entire inbound direction vacuous for everything beneath it. Measured end state: the reconcile screen renders the green banner *"Everything imported from this subscription still exists in Azure. There is nothing to clean up."* while Azure owns two `/24`s inside the target and the target's own page offers 65,023 addresses of "free" space over them.

**Do you need to act today?** If anyone renames, re-carves or resizes Azure address space in a subscription you reconcile against — yes. O1's interim is three lines and is the difference between an operator approving a silent archive and approving one with *"Azure subnet 'X' owns 10.191.20.0/25, which no BASTET subnet records"* on the same screen as the delete checkbox. O2's interim is one guard in `EvaluateVNetLevel`. Neither changes what is deletable in the ordinary case. O7 (the stranded write lock) is not same-day unless you run behind anything that can pause the process — a cgroup freeze, a live migration, a snapshot quiesce.

**Nine of the sixteen proposed fixes were judged unsound or incomplete by a verifier and corrected.** Those corrections are marked inline and are the most valuable thing this round produced: two of them (O3 and O12) would have *reintroduced* the defect they were written to close, and one (O11) would have refused imports that succeed today. Three candidates were killed under verification and are recorded in **Refuted** so round 16 does not spend agents re-deriving them.

---

## How this audit ran

**Eight beats**, each a lens over the whole codebase rather than a directory: (1) `azure-integration` — discovery, ARM shapes, the reconcile scan against live Azure; (2) `logic-data-integrity` — the planner's and reconciler's decision rules against the writes they authorise; (3) `regression-correctness` — the round-14 delta (`N1`–`N10`) re-driven against the behaviour it claims; (4) `regression-tests` — whether the tests round 14 added actually pin what their names say; (5) `locking-lifecycle` — `sp_getapplock`, connection lifetime, DTO binding on the destructive endpoints; (6) `dead-code-residue` — code shipped but unreachable, and gates relaxed on one side only; (7) `ui-client-js` — the three wizard state machines and what the screen asserts; (8) `security-web` — authorisation, antiforgery, headers, cookies.

**Two independent passes** ran every beat without sight of each other's output, plus a **deep sweep** on beats 1, 3, 6 and 7 — the beats covering last round's delta and the surfaces where a regression would be both newest and most consequential. That is 8 × 2 + 4 = **20 finders**, all of which returned.

**Tag meaning.** `[x2]` = the same defect was found independently by **both** passes. `[x1]` = found by **one** pass only. **`[x1]` is weak evidence of absence, not evidence of weakness.** A defect on a surface only one pass happened to drive is exactly the defect that survives to production, so every `[x1]` candidate got a **second verifier on a reachability-and-consequence lens** — can a real user reach this without a crafted request, and what do they actually see and lose? Eleven of the sixteen findings in this file are `[x1]`, including all three criticals but one and both of the findings whose fix corrections would otherwise have shipped a regression.

**Adversarial verification with live reproduction.** Verifiers were told to kill findings, not confirm them. Each stood up its own application instance on its own port, its own SQL catalog, and — where the finding touched Azure — its **own** live ARM fixtures rather than replaying the finder's. Where a fix could be built, it was built and measured rather than read: `dotnet build --no-incremental`, `dotnet test`, and the scenario re-driven on the patched binary against the same database. That is how O3's fix was caught reintroducing `N1`'s defect (patched build: a deletable `VNetPrefixRemoved` with `canCommit: true` while the Azure subnet still held the range, and 847/847 tests green over it), and how O12's offered interim was caught blocking an import that commits 200 today.

**Funnel.**

| Stage | Count |
|---|---|
| Finders dispatched | 20 |
| Finders returned | 20 |
| Raw findings | 45 |
| Dropped at merge (duplicates / out of scope) | 0 |
| Candidates carried to verification | 19 |
| — found by both passes `[x2]` | 5 |
| — found by one pass `[x1]` | 14 |
| Verifier agents dispatched | 33 |
| Candidates judged | 19 |
| Survived | 16 |
| Refuted | 3 |
| Reproduced live | 18 of 19 |
| Proposed fixes judged unsound or incomplete | 9 of 16 |
| Flagged as needing an owner decision | 8 |

All five `[x2]` candidates survived; all three refutations were `[x1]`. **All sixteen surviving findings reproduced on a running instance** — nothing in this file is inferred from reading code alone. The single candidate that did not reproduce live is `C10`, refuted below on facts.

**Rig hygiene, and one thing that will block the round's commit.** Every verifier that ran the application used its own port and its own catalog, killed by captured PID rather than by pattern, and wrote nothing into `/home/anuj/code/Bastet`. Azure fixtures were `rig-r15-`-prefixed and appended to `azure-inventory.txt` for teardown. Two incidents are recorded rather than buried: one verifier lost a port bind race to another agent's instance and wrote two rows into that agent's catalog (left in place, because the catalog's owner had since built on top of them), and the box hit the 128-instance inotify limit, so a new instance needs `DOTNET_USE_POLLING_FILE_WATCHER=1` or it dies in `Program.cs:15`. Separately: four untracked files — `app.pid`, `app1.pid`, `mypid.txt`, `pid.txt` — were left in the repository root by agents across the round and were reported by six different verifiers. None belong to the codebase and they must be removed before this round commits.

---

# Critical

## O1 — Reconcile archives a row whose address range is still assigned in Azure, silently, whenever the range was re-carved: `N1`'s still-allocated guard matches prefixes byte-for-byte only `[x2]` — FIXED

_O1 is fixed and committed. `FindLiveOwnerOfRange` no longer asks only "is this exact prefix string still assigned?". The exact key stays as the cheap first test, and when it misses, a second index — `livePrefixesByVNet`, built in the same inventory pass and grouped by VNet rather than keyed by prefix — is walked for any live Azure prefix that **overlaps** the row's recorded range, in either direction. An overlapping owner routes the row to `ReviewItems` as `RangeStillAllocatedInAzure`, exactly as an exact owner already did, so it is never offered for deletion._

_All five of the verifier's corrections were taken, and the shape follows from them. (1) The method lost `static` so it can reach the primary-constructor `ipUtilityService`; no new dependency, but the signature did change, as the verifier said it would. (2) There was nothing to walk — `livePrefixOwners` is keyed `{vnetResourceId}|{prefix}` and supports exact lookup only — so rather than scanning every key and splitting on `|` per stale row, the second index is built in the same loop at a cost of one dictionary lookup and a walk of one VNet's prefixes. (3) **The shipped sentence is not reused.** It asserts "The range X is still assigned in Azure", which is false when only part of the recorded range is assigned; an overlapping owner gets its own sentence naming the live prefix, the recorded prefix, and the relationship between them. (4) `SuggestedAzureResourceId` is deliberately left unset for a non-equal owner, and the code says so at the assignment: the view renders the Re-link button on the presence of a suggestion and `RelinkAzureSubnet` 409s without one, so no suggestion means no repair route. (5) The review reason names the exit, because the row is otherwise stranded — no screen can edit `AzureResourceId`._

_**The owner decided the overlap case gets no Re-link button**, and that decision is why (4) is written as a branch rather than an unconditional assignment. Re-linking an overlapping row would point it at a subnet holding a *different* range, producing `SubnetPrefixChanged` on the very next scan — the same defect on a loop, on a column nothing can subsequently edit. An exactly-equal owner keeps the Re-link it already had; `AnExactlyEqualLiveOwner_StillOffersTheRelinkSuggestion` pins that the overlap work did not cost the repair route on the case the repair was designed for._

_Proven by live A/B on identical Azure state, both builds driven through the real endpoints against the same subscription. Fixture: `rig-o1-vnet` (`10.191.0.0/16`) with one subnet `rig-o1-sn-c` owning `10.191.20.0/24` and `10.191.22.0/24`; both imported through `POST /Subnet/BulkCreateFromAzurePlan`, baseline scan clean on both builds. The subnet was then deleted and recreated as `rig-o1-sn-c-v2` owning `10.191.20.0/25` and `10.191.22.0/24` — one re-carve, Azure having no rename. Unfixed HEAD (`a5a2c6e`, `git archive` copy, own port and catalog): `canCommit: true`, `ITEM id=2 10.191.20.0/24 SubnetDeleted "The Azure subnet this was imported from no longer exists."`, and `10.191.20.0/25` appears **0 times** in the entire plan — the operator is asked to approve an irreversible archive on a plan that states no fact about the range Azure is holding. Fixed, same Azure state: `canCommit: false`, `items: []`, both rows in `ReviewItems`, the re-carved one carrying `suggestion=(none)` and the reason "Azure subnet 'rig-o1-sn-c-v2' … now holds 10.191.20.0/25, which overlaps the recorded range 10.191.20.0/24", and the plan mentions the live `/25`. The exactly-matched row kept its Re-link suggestion in the same scan, so the two paths are visibly distinguished on one screen._

_**The regression the verifier warned about is real, was measured, and is accepted deliberately.** The widen direction — BASTET recorded a `/25`, Azure now holds the containing `/24` — is an overlap too and is now withheld to review where it was previously a deletable `SubnetPrefixChanged`. That converts a class of currently-deletable rows into stranded review rows, which is why correction (5)'s exit route is load-bearing rather than cosmetic. It is the safe direction for an IPAM: the answer to "part of this range is still assigned in Azure" should not depend on which way the containment runs. `SubnetPrefixChanged_WhereTheLiveOwnerContainsTheRecordedRange_IsWithheld` pins it so the behaviour is a decision rather than an accident._

_The interim the finding offered was **not** shipped and is not needed. It excluded rows already routed into `plan.Items` from `AccountsFor`'s suppression so the inbound pass would name the live range before the archive; with the primary fix the overlapping row never reaches `plan.Items` at all, so there is nothing to exclude. The verifier's correction to that interim — that scoping it to `ReviewItems` as well would print a falsehood — is moot for the same reason, and is recorded here only so a later round does not re-derive it._

_Index hygiene, per round 14's watch list and this round's: `livePrefixesByVNet` accumulates into `Dictionary<string, List<AzureLivePrefix>>` and never `ToDictionary`. `DuplicateRangesAcrossVNets_DoNotThrow` already covers the duplicate-prefix case and still passes against the new index._

_Not done, deliberately: nothing recovers rows already archived by this defect. `DeletedSubnets` has no restore path and the archive drops `AzureResourceId`, both carried forward on the watch list. The measured recovery is re-import — after the archive, `BulkGetVNets` reports the live prefix as `Available` on a `WillUpdateExisting` target — and the new review reason names that route._

_Tests: 847 → 852. Five in `AzureReconcilerRangeStillAllocatedTests`: three that fail against the unfixed reconciler (the re-carve is offered for deletion and `ReviewItems` is empty), one regression guard that the exact-match Re-link survives, and one over-blocking counter-test that a live range in the row's own VNet which does **not** overlap still leaves the row deletable._

---

## O2 — The range index is built only from Azure **subnet** prefixes, so a VNet-level import target whose VNet address space was resized gets no range check at all and is offered for deletion while the VNet still covers it `[x1]` — FIXED

_O2 is fixed and committed. `EvaluateVNetLevel` no longer treats "the recorded prefix string is absent from `Ipv4AddressPrefixes`" as "the space was released". When the prefix is absent but the VNet's **current** address space still overlaps the recorded range, the row becomes a new report-only status, `VNetPrefixStillCovered`, in `ReviewItems` — never deletable — with a reason naming the covering prefix and the remedy. A prefix that overlaps nothing the VNet still owns is unchanged: `VNetPrefixRemoved`, deletable, exactly as before._

_**The verifier's correction (a) decided the test, and it is the whole fix.** Containment by a single prefix is not enough: re-carving `10.190.0.0/16` into `10.190.0.0/17` + `10.190.128.0/17` releases nothing and neither `/17` contains the `/16`, so a containment guard never fires. The test is overlap in both directions, over the whole prefix list. Expand, shrink and re-carve are all one class and all three are pinned._

_**Correction (b) was right and is why `EvaluateVNetLevel` lost `static`** — it needed the primary-constructor `ipUtilityService`. **Correction (c) was taken**: `FullyAllocatingSubnetDeleted` is not reused, because its text is about a different fact and the screen renders the label; `VNetPrefixStillCovered` is a new `AzureReconcileStatus` member with its own client label ("Address space changed") in `_ReconcileScripts.cshtml`, and it is added to the review-set branch so the row lands in `ReviewItems` rather than merely being non-deletable — which is what seeds `ApplyConfirmations`' cascade-withhold set and keeps a descendant safe. `AStillCoveredTarget_ProtectsItsDescendantsFromTheCascade` pins that._

_**Correction (d) was taken by avoiding the shared index entirely.** The finding proposed putting VNet address prefixes into `livePrefixOwners`; the verifier showed that would let `FindLiveOwnerOfRange` match a VNet entry for an ordinary subnet-level row and offer a **VNet** id as a Re-link suggestion, which `RelinkAzureSubnet` writes verbatim onto a column no screen can clear. No index was added at all: `EvaluateVNetLevel` already holds the `BulkAzureVNetViewModel` and tests its `Ipv4AddressPrefixes` directly. That is smaller than the filed fix, and structurally incapable of the corruption (d) describes. **Correction (e)** is satisfied for the same reason — no new prefix-keyed structure exists to throw on a duplicate key._

_No Re-link suggestion is set, and that is deliberate rather than incidental: the VNet's resource ID never changed, so there is nothing to re-point at and the button would write the id the row already has. `AStillCoveredVNetPrefix_OffersNoRelinkSuggestion` pins it._

_Proven by live A/B, both builds against the same subscription, same moment. Fixture `rig-o2b-vnet` `10.178.0.0/16` with **no subnets at all** — chosen deliberately so O1's subnet-prefix fallback cannot mask the VNet-level verdict and this measures O2 alone — imported as a VNet-level target on both builds, then re-carved to `["10.178.0.0/17","10.178.128.0/17"]`, union byte-identical to the original. Unfixed HEAD: `canCommit: true`, `ITEM 10.180.0.0/16 VNetPrefixRemoved`, `descendantCount 0`, offered for irreversible deletion with no ARM confirmation behind it. Fixed: `canCommit: false`, `REVIEW 10.178.0.0/16 VNetPrefixStillCovered`, suggestion empty, reason "…its address space now includes 10.178.0.0/17, which overlaps that range - so the space was resized or re-carved rather than released."_

_**One measured interaction worth recording for the next round.** O1's fix narrows O2's reachable surface: a VNet-level row whose recorded range overlaps a live Azure **subnet** prefix is now already withheld by `FindLiveOwnerOfRange`'s overlap fallback. Measured directly — the audit's own trigger-1 fixture (`rig-o2-vnet` `10.180.0.0/16` expanded to `/15`, holding subnet `10.180.1.0/24`) came back as `RangeStillAllocatedInAzure` on an O1-only build. What O2 closes is the remainder, which is exactly the case the finding called reachable: a target with no live Azure-linked descendants, where nothing inside the VNet overlaps the recorded prefix. The empty-VNet fixture above is that case._

_Not done, deliberately, and this is the residue the verifier flagged as **(f)** for the owner: nothing restores rows already archived by this path, and after a resize the importable prefix is the new one, so re-import is only possible if a BASTET parent has room for it. Recovery is not guaranteed. `DeletedSubnets` still has no restore path — carried on the watch list since round 13._

_Tests: 852 → 861. Nine in a new `AzureReconcilerVNetPrefixCoverageTests`: five that fail against the unfixed reconciler (expand, shrink, re-carve-with-identical-coverage, the no-suggestion assertion, and the cascade guard), and four counter-tests — a prefix overlapping nothing stays deletable, a VNet that is gone entirely stays `VNetDeleted`, an unchanged prefix is reported nowhere, and an overlapping prefix on a **different** VNet does not withhold the deletion._

---

## O3 — The Re-link repair `N1` added writes an Azure **subnet** resource id onto a VNet-level import target; the reconciler then reclassifies that target from review-only to deletable, and its VNet can never be imported again `[x1]`

**Citation:** `src/Bastet/Services/Azure/AzureReconciler.cs:185` (the suggestion stamped at `:185-186`; consumed at `SubnetController.AzureReconcile.cs:310-313`, written at `:348`; button at `_ReconcileScripts.cshtml:304`).
**Confidence:** confirmed.

### What goes wrong

`FindLiveOwnerOfRange` accepts four statuses, two of which are VNet-level. For a VNet-level row, `VNetIdOf(snapshot.AzureResourceId)` is the VNet itself, so the index lookup finds any Azure **subnet** holding that prefix inside the VNet, and `:185-186` stamps that subnet's id into `SuggestedAzureResourceId`. The view renders a Re-link button for any review item with a non-empty suggestion; `RelinkAzureSubnet` re-derives and writes it — it checks that the plan still reports the row, never that the row is a subnet-level link.

Real inputs: VNet `rig-r15-c17-vnet` created with address space `10.98.0.0/24` and one subnet `rig-r15-c17-whole` = `10.98.0.0/24`; imported, producing BASTET row 1 with `IsFullyAllocated=1` and `AzureResourceId` = the **VNet** id. The VNet's address space is then widened to `10.98.0.0/23` — one ordinary ARM update, the subnet stays legal. The row becomes `VNetPrefixRemoved`, the range is still held, and the scan renders a review item with `isVNetLevel: true` and a **`Re-link to 'rig-r15-c17-whole'`** button. One click and the row's `AzureResourceId` — a column no screen in the application can edit or clear — is a subnet id.

Three consequences, all measured: **(a)** the reconciler now evaluates the target through `EvaluateSubnetLevel`, so when that Azure subnet is later deleted the target lands in `plan.Items` as `SubnetDeleted` with `canCommit:true`; **(b)** the bulk planner's same-VNet test (`AzureBulkImportPlanner.cs:281-288`) now fails, so that VNet prefix is permanently `Blocked`; **(c)** the refusal message names an Azure subnet id as *"Azure VNet"*.

This defeats `N1`'s own purpose: the review item exists to stop the reconciler archiving a row whose range is still allocated, and the repair it offers is what makes that row archivable.

### Reproduced

Own instance `127.0.0.1:5371`, catalog `bastet_c17ref`. The corrupting write was made by the application's own button in real Chromium — SQL was used only to build the control arm.

```
az network vnet create -g bastet-visible -n rig-r15-c17-vnet --address-prefixes 10.98.0.0/24 \
    --subnet-name rig-r15-c17-whole --subnet-prefixes 10.98.0.0/24
POST /Subnet/BulkCreateFromAzurePlan  -> {"createdTargets":1,"fullyAllocatedTargets":1}
   SQL: 1|rig-r15-c17-vnet|10.98.0.0|24|IsFullyAllocated=1|.../virtualNetworks/rig-r15-c17-vnet
az network vnet update -g bastet-visible -n rig-r15-c17-vnet --address-prefixes 10.98.0.0/23
```

Browser (`/Azure/Reconcile` → Scan → read `#rec-review-rows`):

```
review rows: 1
['rig-r15-c17-vnet', '10.98.0.0/24',
 "VNet '...' still exists but no longer has the address prefix 10.98.0.0/24. The range
  10.98.0.0/24 is still assigned in Azure to subnet 'rig-r15-c17-whole'... Re-link it to
  that Azure subnet.",
 "Re-link to 'rig-r15-c17-whole'"]
relink buttons: 1   scan JSON: isVNetLevel:true, suggestedAzureResourceId = the SUBNET id
```

After one click: `AzureResourceId = .../virtualNetworks/rig-r15-c17-vnet/subnets/rig-r15-c17-whole`.

**A/B on `BulkGetVNets`**, `IsFullyAllocated=0` in both, only `AzureResourceId` differing:

| arm | status | selectable | reason |
|---|---|---|---|
| re-linked (subnet id) | `Blocked` | false | *"…is already linked to Azure VNet '`<…>/subnets/rig-r15-c17-whole`'…"* — a subnet id called an Azure VNet |
| control (VNet id) | `WillUpdateExisting` | **true** | *"Will import into existing Bastet subnet 'rig-r15-c17-vnet'."* |

Correction to the finder: before the click the prefix was already `Blocked`, but on *"is marked as fully allocated"*, which one UI click clears. After the click it is blocked on a column no screen can edit.

**A/B on `ReconcileScan`**, identical Azure state (VNet live, `rig-r15-c17-whole` deleted and replaced by live `rig-r15-c17-lo` `10.98.0.0/25` and `rig-r15-c17-hi` `10.98.0.128/25`):

```
re-linked:  canCommit True,  ITEM 1 SubnetDeleted "The Azure subnet this was imported from
            no longer exists.",  ZERO review items
control:    canCommit False, REVIEW 0 AzureRangeNotImported x2 -- "Azure subnet
            'rig-r15-c17-lo' ... owns 10.98.0.0/25, which no BASTET subnet records.
            BASTET is reporting that range as free space."  (and the same for -hi)
```

`/Subnet/Details/1` in the re-linked arm printed *"10.98.0.0 10.98.0.255 254 IP addresses [Create Subnet]"* above `N4`'s note telling the operator to *"Run Azure Reconcile to check before allocating from these ranges"* — the check that had just gone silent. The archive was then committed: `{"success":true,"targetsDeleted":1,"subnetsArchived":1}`, `SELECT COUNT(*) FROM Subnets` → **0**, and the follow-up scan returned `items 0, reviewItems 0, warnings []` while ARM still reports the `/24` carved into two live `/25`s.

### Fix

Two halves, because the button and the endpoint are separately reachable.

**(a)** Gate the suggestion, at `AzureReconciler.cs:185-186`:

```csharp
if (AzureResourceIdentity.IsAzureSubnet(snapshot.AzureResourceId))
{
    review.SuggestedAzureResourceId = stillAllocated.ResourceId;
    review.SuggestedAzureSubnetName = stillAllocated.SubnetName;
}
```

**(b)** Defence in depth in `RelinkAzureSubnet` (`SubnetController.AzureReconcile.cs:340-350`): refuse under the lock when the row being repaired is a VNet-level link. The endpoint is `[HttpPost]` + antiforgery + Admin, so a stale page can reach it without the button.

**Cheaper interim**, one line, using a flag already on the wire — `_ReconcileScripts.cshtml:304`: `if (item.suggestedAzureResourceId && !item.isVNetLevel)`. It stops the click without touching the reconciler, but the endpoint remains postable directly.

> **The verifier found the finder's proposed fix (a) to be a regression, and measured it rather than arguing it.** The finder proposed adding `if (!AzureResourceIdentity.IsAzureSubnet(snapshot.AzureResourceId)) { return null; }` inside `FindLiveOwnerOfRange`. That makes `stillAllocated` null, which **skips the `RangeStillAllocatedInAzure` block at `:179-191` entirely**; control falls to `:193`, where `VNetPrefixRemoved` is not in the review set and lands in `plan.Items`. On the patched build the exact state HEAD reports as review-only became a **deletable** `VNetPrefixRemoved` with `canCommit:true` while the Azure subnet still held the range — `N1`'s own defect reintroduced, on the one path that removes data — and **847/847 tests passed with the regression in place**, so nothing would catch it. The finder's prose says "keep routing them to `ReviewItems`", but the code offered makes that impossible.
>
> The corrected form is the one written above, and it was built and measured: `RangeStillAllocatedInAzure` retained, `canCommit` false, withhold warning still emitted, `suggestedAzureResourceId` empty so no button renders, `dotnet test` 847/847. It is a **smaller** change than the one proposed.
>
> Two additions the finding misses. The reason string at `:184` ends *"Re-link it to that Azure subnet."*; with the suggestion suppressed there is no button, so the VNet-level branch needs its own closing sentence (correct the VNet's address space, or re-import) or the operator is told to click something that is not there. And rows already corrupted are unrecoverable through the UI, and unrecoverable at all once archived.
>
> Fix (b) is sound and worth keeping, but with the corrected (a) it becomes unreachable by construction — no suggestion means `target` is never matched at `:310-313` and the endpoint already 409s. The interim is sound; the line to edit is `:304`, not `:302`.
>
> **Three corrections to the finding, none of which weaken it.** Severity was raised **high → critical**: the measured end state is BASTET reporting two live Azure allocations as free space with the scan agreeing there is nothing to clean up. Of the two VNet-level statuses named, only `VNetPrefixRemoved` can actually match — a `VNetDeleted` row contributes no key to the index. And the *"taking its whole subtree"* limb was not measured and is structurally awkward, since a re-linkable row is fully-allocated by construction; the measured half stands on its own.

---

# High

## O4 — The reconciler's inbound check accepts any **containing** BASTET subnet as accounting for an Azure range, so an aggregate parent above the import target silences every `AzureRangeNotImported` report `[x2]`

**Citation:** `src/Bastet/Services/Azure/AzureReconciler.cs:487-497` — containment arm `:495-496`, consumed at `:447`.
**Confidence:** confirmed.

### What goes wrong

`AccountsFor` answers "does BASTET already record this Azure range?" with equality **or** containment, excluding only rows whose `AzureResourceId` is a VNet id — the import target itself. Any other containing row counts, **including an ancestor of the target**. Because `ValidateSubnetCreation` forces every subnet under its most specific container (`SubnetController.Helpers.cs:278-302`), an install that models a top-down plan — a `10.0.0.0/8` root, a regional `/12` aggregate — necessarily has such an ancestor above every Azure import target. That one hand-created row makes the whole inbound direction vacuous for everything beneath it, permanently.

The method's own remarks argue exactly this for the target: *"counting that as 'accounted for' makes the inbound check vacuous."* The exclusion was applied to the target only.

### Reproduced

Three instances of the HEAD build, own ports and catalogs, everything driven through the real antiforgery-tokened endpoints. Instance A holds a hand-created `root10` = `10.0.0.0/8`; instance B is byte-identical without it.

```
DEFECT (root10 present):   items=0 reviewItems=2
  REVIEW  rig-r15-snet-a2-noncontig  172.16.2.0/24     AzureRangeNotImported
  REVIEW  rig-r15-b2b-snet-toplevel  192.168.100.128/25 AzureRangeNotImported
  -- NOTHING for 10.20.20.0/24 (rig-r15-b2b-snet-underroot), same VNet, same scan

CONTROL (no root10, byte-identical import):  items=0 reviewItems=3
  ... plus REVIEW rig-r15-b2b-snet-underroot 10.20.20.0/24
      "...owns 10.20.20.0/24, which no BASTET subnet records.
       BASTET is reporting that range as free space."
```

The trees differ only by `root10`. Two other unrecorded ranges of the same VNet in the same scan **are** reported — they are the ones `10/8` does not contain.

**The harm, same moment**, `GET /Subnet/Details/2` on the defect instance:

```
10.20.13.0 | 10.20.255.254 | 62,207 IP addresses | Create Subnet   <-- contains 10.20.20.0/24
N4 note "Run Azure Reconcile" present: True
```

The aggregate's own page never lists `10.20.20.0/24` either, because free space is range-minus-**children**. So no page in BASTET shows the range as allocated, while the reconcile screen the note points at reports a clean subscription.

**Strongest form**, a third instance with root `10.0.0.0/8` and target `10.10.0.0/16` where only `10.10.1.0/24` was imported:

```
items=0 reviewItems=0 warnings=[]
GREEN BANNER "There is nothing to clean up." would render: True
```

while `/Subnet/Details/2` prints *"10.10.2.0 | 10.10.255.254 | 65,023 IP addresses | Create Subnet"* and Azure owns `10.10.2.0/24` + `10.10.3.0/24` inside it. The control instance reported 5 review items and the banner False.

**Fix A/B on the identical database**, only the binary differing: HEAD `reviewItems=2`, patched `reviewItems=3` (the suppressed range appears); `dotnet test` 847/847. **Deliberate-behaviour check:** a hand reserve `10.20.16.0/20` created *under* the target, which also contains the range, suppresses identically on both builds — the `N3` case the remarks defend is untouched; only the ancestor case changes.

### Fix

A containing row is evidence only when it is itself **inside the VNet address space** the Azure range belongs to; a row that contains the VNet prefix is an ancestor of the target, not an allocation record.

**Regression guard:** an `AzureReconcilerInboundTests` case with an unlinked `10.0.0.0/8` above an Azure-linked `10.20.0.0/16`, asserting an unrecorded range inside the target is still reported. Its exact sibling `ATargetContainingTheRangeIsNotEnough_OrTheCheckWouldBeVacuous` already exists and it should sit next to it. **No backfill is possible or needed** — the verdict is recomputed on every scan, so installs simply start seeing the reports they should have been getting.

> **The verifier found the rule right but the fix unapplicable as written, with three corrections.**
>
> **1. There is no `vnetNetwork, vnetCidr` at the call site.** The sketch says those values are "already in scope at `:433`"; `:433` is `foreach (string prefix in Ipv4PrefixesOf(subnet))`, i.e. the **subnet's** prefix. A VNet has a *list* of prefixes — three on the rig's own `rig-r15-vnet-a2-multi`. The fix must first select, per Azure range, the VNet prefix that contains it. **So the offered "cheaper interim, one line and no signature change" does not exist** — it needs the same lookup. What was built and measured:
>
> ```csharp
> (string Network, int Cidr)? owner = VNetPrefixContaining(vnet, parts[0], cidr);
> if (existingSubnets.Any(e => AccountsFor(e, parts[0], cidr, owner))) { continue; }
>
> private bool AccountsFor(ExistingSubnetSnapshot existing, string network, int cidr,
>                          (string Network, int Cidr)? vnetPrefix)
> {
>     if (string.Equals(existing.NetworkAddress, network, StringComparison.OrdinalIgnoreCase)
>         && existing.Cidr == cidr) { return true; }
>     if (AzureResourceIdentity.IsAzureVNet(existing.AzureResourceId) || vnetPrefix is null) { return false; }
>     return ipUtilityService.IsSubnetContainedInParent(existing.NetworkAddress, existing.Cidr,
>                                                       vnetPrefix.Value.Network, vnetPrefix.Value.Cidr)
>            && ipUtilityService.IsSubnetContainedInParent(network, cidr,
>                                                       existing.NetworkAddress, existing.Cidr);
> }
> ```
>
> Measured: 847/847 unchanged; on the identical defect database the suppressed `10.20.20.0/24` is reported and the two already-correct reports are byte-identical; the hand reserve inside the VNet prefix still suppresses exactly as at HEAD.
>
> **2. The fix's code and its own prose disagree about equality.** `IsSubnetContainedInParent` returns false whenever `childCidr <= parentCidr` (`IpUtilityService.cs:174-178`) — it is **strict**. So the sketch silently excludes a BASTET row **equal** to the VNet prefix, contradicting its own text ("contained in (or equal to) it"), and the full form and the offered interim are therefore **not** equivalent. Pick one deliberately. Excluding it is defensible, but it must be a decision, not an artefact of a helper's `<=`.
>
> **3. Define the "no containing VNet prefix" case explicitly.** Treating null as "does not account" starts reporting Azure subnet prefixes outside every declared VNet address prefix. ARM normally forbids that shape, but the reconciler also assembles inventory under partial RBAC visibility and must neither throw nor spam. Falling back to the current containment test is the conservative choice. The fix as filed is silent on it.

---

## O5 — The reconciler counts an Azure-linked import target as the record of its own range even when it is **not** marked fully allocated, so an Azure subnet owning a whole VNet prefix is never reported inbound `[x1]`

**Citation:** `src/Bastet/Services/Azure/AzureReconciler.cs:489-493`.
**Confidence:** confirmed.

### What goes wrong

`AccountsFor`'s equality arm returns `true` for **any** BASTET row whose `{NetworkAddress, Cidr}` equals the Azure range, including a VNet-level import target. Its own remark says why that is meant to be sound: *"an Azure subnet covering a whole VNet prefix is recorded by marking that very target fully allocated … so the target genuinely is the record of that range."* That premise fails whenever the target is linked but **not** fully allocated — and `ExistingSubnetSnapshot.IsFullyAllocated` is already populated (`AzureSubnetSnapshotService.cs:29`), so the information needed to be correct is in hand and unread.

Three independent routes reach that state on the unmodified build, none needing a crafted post:

1. **Bulk wizard default.** `_BulkScripts.cshtml:313-331` propagates subnet→prefix only; ticking a VNet address prefix never ticks its subnets, so `buildSelectionFromUI` emits `subnets:[]` by default. Preview returns `errors:[] warnings:[] canCommit:true`; the commit creates the target with `IsFullyAllocated=0`.
2. **Strongest, and underweighted by the finder:** import an **empty** VNet — there is nothing to tick or untick — then Azure creates a subnet covering the whole prefix. This is precisely the "Azure changed after import" case `N3` built `AzureRangeNotImported` for, and the equality arm swallows it.
3. One click on the rendered *"Mark as Not Fully Allocated"* form after a correct import.

### Reproduced

```
POST /Subnet/BulkCreateFromAzurePlan  (VNet address prefix ticked, its one subnet NOT ticked)
  -> 200 {"createdTargets":1,"createdChildSubnets":0,"fullyAllocatedTargets":0}   on an EMPTY database
SQL: 1|rig-r15-vnet-b7bfull|10.61.0.0|24|IsFullyAllocated=0|az=.../virtualNetworks/rig-r15-vnet-b7bfull
POST /Azure/ReconcileScan -> items: [], reviewItems: [], warnings: []
```

Nothing at all about `10.61.0.0/24`, which Azure has entirely assigned to `rig-r15-snet-b7bfull`.

`/Subnet/Details/1` renders *"This subnet is linked to Azure. Ranges listed here are free according to what BASTET has imported … Run Azure Reconcile to check before allocating from these ranges."* followed by **"10.61.0.0 | 10.61.0.255 | 254 IP addresses | [Create Subnet]"**. In a real browser the reconcile page renders *"Everything imported from this subscription still exists in Azure. There is nothing to clean up."*

**Control, same action, same wizard, same commit** on a VNet whose subnets do not cover the whole prefix: three `AzureRangeNotImported` items are reported. Every Azure-assigned range inside the target **is** reported; the one range that exactly equals its target is the only one silently skipped — the largest possible range to be wrong about, 254 of 254 addresses.

**The harm driven to completion:** `POST /Subnet/Create {prod-db-tier, 10.61.0.0/25, ParentSubnetId:1}` → 200, row persisted. BASTET accepted an allocation inside a `/24` Azure owns entirely, immediately after the page told the operator to run Reconcile and Reconcile said nothing.

**Counterfactual**, same database, only `:489-493` changed: the review item appears. `dotnet test` on the patched copy: **846/847**, the single failure being `AzureReconcilerInboundTests.AnAzureSubnetCoveringTheWholeVNetPrefix_IsAccountedForByTheTargetItself`, whose fixture `Target(...) -> Existing(...)` (test file `:50-63`) never sets `IsFullyAllocated` — it asserts the silence in precisely the state where its own docstring's justification does not hold.

### Fix

Honour the equality arm for a VNet-level target only once the fully-allocated import it stands for has actually happened:

```csharp
if (string.Equals(existing.NetworkAddress, network, StringComparison.OrdinalIgnoreCase)
    && existing.Cidr == cidr)
{
    return !AzureResourceIdentity.IsAzureVNet(existing.AzureResourceId) || existing.IsFullyAllocated;
}
```

Fix the pinning test with it — set `IsFullyAllocated = true` on the target so it pins the state its docstring describes — and add `ATargetLinkedButNotFullyAllocated_DoesNotAccountForTheWholePrefixSubnet`. **No cheaper interim exists that is also correct:** relying on O6's wizard fix does not cover a VNet imported before Azure created the covering subnet, and widening the Details-page note is advisory copy over an answer that is still wrong.

> **The verifier judged the fix sound and checked it rather than nodding it through.** It builds 0 warnings; it changes behaviour only for the exact-equality case with a VNet-level target; the early return loses nothing because the containment arm at `:495-496` already excludes VNet targets; and on the live rig the correct top-up import sets `IsFullyAllocated=1` and silences it — so it does not create the unsilenceable warning the method's own remark at `:393-400` warns about.
>
> **One residue the owner should see, measured rather than assumed.** If the target already has children — which is exactly the state this defect produces, because the operator allocated from the false free space — the new item **cannot** be cleared by the top-up import: the planner refuses at `AzureBulkImportPlanner.cs:578-586` with *"Cannot import VNet prefix 10.66.0.0/24: Azure subnet 'X' covers the whole prefix, which would mark Bastet subnet 'Y' fully allocated, but it already has child subnets."* The item is **true** in that state and the refusal names the remedy, so this is correct behaviour rather than a defect in the fix — but the warning persists until the conflicting child is removed, and the owner may want the item's `Reason` to say so instead of leaving the operator to discover it in the import wizard.

---

## O6 — Round 14's "already recorded" filter in the single-VNet wizard deletes the fully-encompassing Azure subnet row, so that import path is unreachable and BASTET reports an Azure-allocated range as free with nothing detecting it `[x2]`

**Citation:** `src/Bastet/Controllers/AzureController.cs:176`.
**Confidence:** confirmed.

### What goes wrong

`AzureService.GetCompatibleSubnets` correctly returns a fully-encompassing subnet with `FullyEncompassesVNetPrefix=true` — it only sets that flag when the Azure prefix's network **and** cidr equal the target's. `AzureController.GetSubnets` then filters the list against every `{NetworkAddress}/{Cidr}` in the `Subnets` table, which **by construction** contains the target's own key. The row is therefore always removed, and the wizard renders *"No compatible subnets found in this Virtual Network."* — a false statement about Azure.

The whole fully-encompassing branch downstream (`_ImportScripts.cshtml:336`, `SubnetController.Azure.cs:193-218`, `:356-381`, `:421-427`) becomes unreachable from the only UI that produces it. `/Subnet/Details/1` goes on printing the `/24` as free with a **Create Subnet** button. Nothing else catches it: the target is never Azure-linked, so it is absent from `GetAzureLinkedSubnetsAsync`, the VNet is outside `importedVNetIds`, and the reconcile scan reports *"nothing to clean up."*

On the same build and the same Azure state the **bulk** wizard, whose copy of the identical rule short-circuits the encompassing case **before** the exact-match test (`AzureBulkImportPlanner.AnnotateSubnet:360-369`), still offers the subnet as `Available`/selectable. One copy of the rule was written with the encompassing case in mind and the other was not.

`git log -S"alreadyRecorded" -- src/Bastet/Controllers/AzureController.cs` returns exactly one commit: `8afa2df` — round 14's `N4`, one commit before HEAD.

### Reproduced

Fixture: `az network vnet create -n rig-r15-vc1-vnetfull --address-prefixes 10.171.0.0/24 --subnet-name rig-r15-vc1-snetfull --subnet-prefixes 10.171.0.0/24`, and a BASTET target `10.171.0.0/24` created by an ordinary `POST /Subnet/Create`.

```
GET /Azure/GetSubnets?vnetResourceId=.../rig-r15-vc1-vnetfull&subnetId=1
  -> {"success":true,"subnets":[],"message":"No compatible subnets found in this VNet"}
```

for a VNet ARM reports as having exactly one subnet occupying the whole prefix. In real Chromium: `no-subnets visible: True | subnet-selection visible: False`, message *"No compatible subnets found in this Virtual Network. Back to VNets"* — no checkboxes, no Import button, a dead end.

`GET /Subnet/Details/1`: *"10.171.0.0 | 10.171.0.255 | 254 IP addresses | [Create Subnet]"*, and `N4`'s advisory note **absent** (`'according to what BASTET has imported' in html` → False — it is gated on `AzureResourceId`, which the refused import never stamps). `POST /Azure/ReconcileScan`: `items: [], reviewItems: [], globalErrors: [], warnings: [], canCommit: false`.

**Same build, same database, same Azure state, bulk wizard:** subnet `rig-r15-vc1-snetfull` `statusName "Available"`, `isSelectable true`, reason *"Covers the whole VNet prefix, so it marks the target fully allocated instead of being created."* The two wizards contradict each other on the same row.

**Attribution control** — a copy of the repo with one predicate changed at `:176`, built 0 warnings, run on its own port and catalog:

```
GET /Azure/GetSubnets -> [{"name":"rig-r15-vc1-snetfull","addressPrefix":"10.171.0.0/24",
                           "fullyEncompassesVNetPrefix":true}]
POST /Subnet/BatchCreateChildSubnets ... FullyEncompassesVNetPrefix=true -> 302
SQL: Id=1 IsFullyAllocated=1  Description="Fully allocated by Azure subnet 'rig-r15-vc1-snetfull'..."
     AzureResourceId=.../virtualNetworks/rig-r15-vc1-vnetfull
Details page no longer contains "Unallocated IP Ranges" or "254 IP addresses"
```

One line is the sole cause. **Fix-soundness control on the same patched build:** `N4`'s top-up filter is fully preserved — after importing `10.10.1.0/24` from a multi-subnet VNet, re-querying returns only the two not-yet-imported prefixes.

### Fix

Exempt the encompassing row from the already-recorded filter, exactly as the bulk planner's `AnnotateSubnet` does:

```csharp
azureSubnets = [.. azureSubnets.Where(a => a.FullyEncompassesVNetPrefix
                                        || !alreadyRecorded.Contains(a.AddressPrefix ?? string.Empty))];
```

It degrades safely if the target is later populated: `BatchCreateChildSubnets` already refuses an encompassing entry alongside siblings (`SubnetController.Azure.cs:210-219`) and `ValidateSubnetCanBeFullyAllocated` already refuses a parent with children (`Services/Validation/HostIpValidationService.cs:253-258`), so a top-up that re-offers the row fails with a specific message and a rollback rather than writing anything wrong.

**Better:** extract the rule — *"a range BASTET already records, unless it is the target itself"* — into one helper both the wizard endpoint and `AnnotateSubnet` call, so the two copies cannot drift again.

**Cheaper interim:** build `alreadyRecorded` from `context.Subnets.Where(s => s.Id != subnetId)`. One clause, no new concept.

> **The verifier judged the fix sound — built and measured, not read — with three non-blocking notes.** The path in the finder's text was wrong: the children guard is at `src/Bastet/Services/Validation/HostIpValidationService.cs:253-258`, not `src/Bastet/Services/`. The cheaper interim's load-bearing premise was checked: `BastetDbContext.cs:31-32` declares `HasIndex(s => new { s.NetworkAddress, s.Cidr }).IsUnique()`, so the target's own row is the only row that can carry the encompassing prefix — and the two forms cannot diverge even in principle, because `GetCompatibleVNets` only offers a VNet whose address prefix equals the target. And the fix deliberately leaves a residue worth one sentence: a **non**-encompassing Azure subnet whose prefix collides with an unrelated BASTET subnet elsewhere in the tree is still silently dropped from this list, where the bulk planner shows the same collision as `Blocked` with a reason. The shared-helper form closes that too and is the version to take.
>
> One measured behaviour confirmed harmless: after the fix, `GetSubnets` re-offers the encompassing row even when the target is already fully allocated and Azure-linked. It is unreachable through the UI (`GET /Azure/Import/1` 302s once `IsFullyAllocated` is set) and a direct re-post is idempotent — `AzureResourceId` compares equal so the repoint guard at `:391-408` does not fire, and `AppendFullyAllocatedNote` strips before re-appending (`M3`/`N8`), so no note stacks.
>
> **Regression test the fix should carry**, since neither of `N4`'s `AzureController` hunks has one: a `GetSubnets` test with a persisted target whose `{NetworkAddress, Cidr}` equals the Azure prefix, asserting the `FullyEncompassesVNetPrefix` row survives the filter while a sibling child prefix already recorded in the tree does not.
>
> **Two corrections to the finding, neither fatal.** The headline's *"the whole mark-fully-allocated import path is unreachable"* is wrong as written — only the single-VNet wizard's copy is; the bulk wizard was driven to a completed commit on the HEAD build against the same database and Azure state. And *"with nothing detecting it"* is true of the reconciler, the Details page and the single-VNet wizard, but the bulk wizard both names the condition and can repair it — and nothing on the dead-end screen says so. Severity stays **high**: the wrong output is a live Azure query answering "no compatible subnets" when the VNet holds exactly one and `GetCompatibleSubnets` returned it, and the consequence that persists is an IPAM advertising 254 free addresses over a `/24` Azure has assigned in full, with `N4`'s own caveat note suppressed. The existence of a working second route does not lower it — nothing points the operator at that route.

---

# Medium

## O7 — `sp_getapplock` acquisition has no counterpart to the release path's `DiscardPooledConnection`: an exception escaping `AcquireAppLockAsync` returns the connection to the pool with the lock possibly held, and the app then lies about why every write fails `[x1]`

**Citation:** `src/Bastet/Services/Locking/SqlServerSubnetLockingService.cs:104`.
**Confidence:** confirmed.

### What goes wrong

`:101` opens the request's EF connection; `:104` runs `sp_getapplock`. If SQL Server grants the lock but the client abandons the command before reading the response, `AcquireAppLockAsync` throws and control jumps straight past `:110-146` to the outer `finally` at `:148-151`, which calls `CloseConnectionAsync()` — EF returns the connection to SqlClient's **pool**. `DiscardPooledConnection()` is never reached: it is wired only into the release catch at `:138-145`, and `grep -rn DiscardPooledConnection src/` returns exactly two hits, the declaration and that one call site.

The SQL session stays alive and still owns `Bastet:SubnetOperations`. From that moment every subnet and host-IP write on **every replica** parks the full 30 s in `sp_getapplock` and returns *"The operation timed out due to high concurrency. Please try again."* (`SubnetController.Create.cs:138`, `Edit.cs:228`, `HostIpController.cs:165/:304/:434/:763`) or *"The operation timed out because another subnet operation is in progress. Nothing was deleted."* (`AzureReconcile.cs:221`). No operation is in progress; the statement is false and the prescribed remedy — retry — never clears it. Nothing logs that a lock was stranded, unlike the release path which logs at Error.

**The trigger is more ordinary than "an Azure SQL gateway hiccup".** A stopped process emits its TDS attention only after it resumes, so the attention ack is always prompt and the connection is never broken. There is no upper bound: **any pause of the BASTET process longer than the 60 s command timeout the code itself sets at `:183`**, landing between the `sp_getapplock` batch reaching the server and the client reading the reply, strands the lock. Producers need no Azure SQL and no gateway: a cgroup freezer or `docker pause`, a VM live-migration stun or snapshot quiesce, a suspended host, or any supervisor `SIGSTOP`.

### Reproduced

Three runs, two independent apparatus. A TCP proxy that buffers the server→client direction for the one batch containing `sp_getapplock` and flushes it when the client next speaks:

```
[1] ARM HIT  txt='EXEC @Result = sp_getapplock @Resource = @Resource, ...'
[1] s2c BUFFERED len=220                       <- server answered in 1 ms: the lock WAS granted
[1] c2s during stall len=24 dt=60.0 type=0x06  <- TDS attention, exactly the 60 s command timeout
[1] RELEASE flush 220 bytes after 60.0s
[1] POST-RELEASE s2c len=37                    <- attention ack; NO further client traffic
```

HTTP: `503 {"success":false,"error":"The operation timed out because another subnet operation is in progress. Please try again."}` after 60.038 s. SQL, app idle, 5 s later:

```
session 103  status=sleeping  lastreq=05:12:42.540
103  X  0:[Bastet:SubnetOperations]:(b83d66a6)
```

Alive, sleeping, no request running, holding the global write mutex.

**A second replica** — own process, own pool, same catalog — at 05:15:10: `POST /Subnet/Create` → 200 after **30.056 s**, *"The operation timed out due to high concurrency. Please try again."*, and `SELECT` confirms nothing was written. Polled every 10 s, the strand held continuously from 05:12:42 to 05:17:15 — **~4 min 40 s of total write denial across every replica** — and ended only when SqlClient's idle-pool pruning destroyed the connection, not by anything the app did.

**Run 3 used no proxy at all**: ordinary lock contention plus a `SIGSTOP`-class process pause over a direct TCP connection. A 150 s freeze stranded the lock exactly like the 62 s one.

**Why the same injection on `POST /Subnet/Create` does *not* strand** (a sharpening, not a rebuttal): the error-view render reuses the same physical connection with the TDS `RESETCONNECTION` bit set, and `sp_reset_connection` drops session-owned app locks. The three JSON handlers that return without touching the DB again are where it sticks. Confirmed both ways: a plain `GET /Subnet` on a poisoned replica cleared a live strand instantly.

### Fix

Give the acquire the same remedy the release already has:

```csharp
int lockResult;
try { lockResult = await AcquireAppLockAsync(SUBNET_OPERATIONS_LOCK, remainingMs); }
catch (Exception ex)
{
    logger.LogError(ex, "sp_getapplock did not return a result; discarding the pooled connection "
                      + "in case the lock was granted server-side");
    DiscardPooledConnection();
    throw;
}
```

Three lines, no behaviour change on any path that already works, and it costs one pool flush only on a path that has already failed — the same trade the maintainers documented for `DiscardPooledConnection` at `:30-39`. The `Bastet:Migration` twin needs nothing: `getLock.ExecuteNonQuery()` at `Program.cs:429` has no enclosing try/catch, so an exception there terminates the process, and process exit ends the session regardless of pooling.

**Interim if the fix is deferred** (diagnostic only): log at Critical when `AcquireAppLockAsync` throws rather than returning a negative result code. Today the only line emitted is EF's own *"Failed executing DbCommand (62,005ms) … EXEC @Result = sp_getapplock"*, which names the statement but not the consequence, so an operator cannot distinguish a stranded lock from genuine contention.

> **The verifier judged the fix sound and built it.** Applied verbatim to a `git archive HEAD` copy: identical 62 s freeze → the new Error line fires, the physical connection is destroyed 9 ms after resume, `sys.dm_tran_locks` APPLICATION = 0 rows, the next replica's write succeeds in 0.27 s. Build 0 warnings, 847/847. It correctly does **not** cover the `lockResult < 0` branch, where the lock was not granted.
>
> Two things checked that the finder did not claim, both in its favour: it also closes a second latent granted-then-throw path, because `return (int)parameters[4].Value;` at `:203` sits **outside** the try/finally and an `InvalidCastException` there would today escape with the lock held; and the `Bastet:Migration` claim is right for the reason stated.
>
> Two non-blocking notes. The fix does not make the failing request's **own** message true — that one request still says "another subnet operation is in progress" when nothing is; what it stops is that message becoming true for every other replica for the next five minutes. Making it honest is a separate change at the six controller catch sites. And `DiscardPooledConnection` flushes the entire pool for the connection string — already documented and accepted at `:30-39` for the release half; this makes it symmetric, which is the point. The finder's own interim is strictly weaker than the fix and should not ship alone.

---

# Low

## O8 — The single-VNet top-up that `N4` newly admits silently overwrites the operator's own name on the target parent — the rename the same fix deliberately suppressed in the bulk planner `[x2]`

**Citation:** `src/Bastet/Controllers/SubnetController.Azure.cs:410-412` (guard opens at `:384`).
**Confidence:** confirmed.

### What goes wrong

Round 14's `N4` relaxed the single-VNet wizard's entry gate (`AzureController.cs:45-47`) so a populated target is admitted when it already carries an `AzureResourceId`. `BatchCreateChildSubnetsCore` was **not** changed to match: it still sets `parentSubnet.Name = vnetName` unconditionally on every `isAzureImport` commit. A top-up therefore discards whatever the operator renamed the row to.

Round 14 decided the **opposite** for the bulk wizard in the same commit — `AzureBulkImportPlanner.cs:507`, `renameMatched && !exact.HasChildSubnets`, with the remark *"Renaming a target that already holds imported rows changes a label the operator has been living with."* The two commit paths now disagree about the same operation on the same row.

### Reproduced

```
POST /Subnet/Create  vc5-target 10.10.0.0/16
POST /Subnet/BatchCreateChildSubnets  (first import, one child)   -> parent named rig-r15-vnet-a1
POST /Subnet/Edit/1  Name="Production Core"  Description="hand-maintained label"
GET  /Azure/Import/1        -> 200, <title>Subnet Azure Import - BASTET</title>   (the N4-relaxed gate)
GET  /Azure/GetSubnets      -> the two not-yet-imported ranges only
POST /Subnet/BatchCreateChildSubnets  (top-up, two children)      -> 302
```

Immediately after:

```
1|rig-r15-vnet-a1|hand-maintained label            <- "Production Core" is gone
2|rig-r15-snet-a1-single|Imported from Azure VNet: rig-r15-vnet-a1
3|rig-r15-snet-a1-multi (10.10.2.0-24)|...
4|rig-r15-snet-a1-multi (10.10.3.0-24)|...
```

The operator-set Name is replaced by the Azure VNet name; the operator-set Description survives. **Asymmetry control on the same row**, name restored, rename explicitly requested: `POST /Azure/BulkImportPreview` returned `targetType=ExactMatch existingTargetName="Production Core" willRename=False newName=None errors=[]`. Bulk refuses; single-VNet performs. Repeated a second time with the redirect followed, the flash rendered *"Successfully renamed parent subnet to 'rig-r15-vnet-a1' and imported 1 child subnets."*

**Two corrections to the finding, neither fatal.** *"Silently … no message naming it"* is **false** — the new name is stated on the very next screen. The actual defect is that the **old** value is never shown, is not recorded anywhere (nothing archives a rename), and the wizard offers no way to decline, unlike the bulk path's opt-in `renameMatchedBastetSubnets` checkbox. So it is an unavoidable, unrecoverable loss of one operator-entered field, announced but not consented to. And `SubnetController.Read.cs:124-129` still gates `ViewBag.CanImportFromAzure` on `ChildSubnets.Count == 0`, so no rendered link reaches `/Azure/Import/{id}` for a populated target — the route in is the URL itself (bookmark, history, Back after the first import), which is reachable and was entered with an ordinary authenticated GET. That is one sentence of scenario, not a severity reduction. (It is also **O9**.)

### Fix

Skip the parent rename on a top-up, and change the flash with it.

> **The verifier judged the fix right in direction and wrong in three ways, all checked against the tree.**
>
> **(a) The proposed guard is not the bulk rule it claims to mirror.** Proposed: `isTopUp = !string.IsNullOrEmpty(vnetResourceId) && parentSubnet.AzureResourceId == vnetResourceId`. Bulk uses `!exact.HasChildSubnets`. They diverge in **both** directions. Forward: a target linked to this VNet but holding no children — an import whose children were later deleted, or one that marked the parent fully allocated and was then un-marked — **is** renamed by the bulk planner (`AnEmptyTargetIsStillRenamedWhenRequested` pins exactly that) and would **not** be by the proposed fix. Backward: a populated target with **no** Azure link never reaches the wizard GET but does reach the commit, because `BatchCreateChildSubnetsCore` never re-checks the GET's precondition (round 13 `C4`, unchanged); there `isTopUp` is false, so the proposed fix **still renames** the one case the bulk planner hard-errors on. Use the bulk rule verbatim: `bool targetIsPopulated = treeCache.Exists(s => s.ParentSubnetId == parentId);` — `treeCache` is already loaded one screen up and contains every subnet, so this costs nothing. Checked against the existing pins: `SubnetControllerBatchCreateTests` seeds its parent with no children, so `BatchCreateChildSubnets_WithVNetName_RenamesParentSubnet` and the long-name `InlineData` cases stay green.
>
> **(b) The fix is incomplete: it leaves the success flash asserting a rename that no longer happens.** `SubnetController.Azure.cs:490-494` builds that string from nothing but `!string.IsNullOrEmpty(vnetName) && isAzureImport`. Suppress the write without touching the message and the app announces a rename it did not perform — the precise anti-pattern this same file already refuses at `:188-191` (*"the transaction commits having written nothing while the success message still announces a rename that never happened"*). Capture whether the name actually changed and branch on it. The encompassing variant at `:491` needs no change: `ValidateSubnetCanBeFullyAllocated` refuses a parent with children, so that branch cannot fire on a top-up.
>
> **(c) The offered interim is a no-op that would ship a guard which never fires.** `parentSubnet` comes from `context.Subnets.FindAsync(parentId)` at `:307` with no `Include`, and there is no `UseLazyLoadingProxies` anywhere in `src/`, so `parentSubnet.ChildSubnets` is an empty collection at `:410` no matter how many children the row has — `!parentSubnet.ChildSubnets.Any()` is always true. The cache the interim points at is at line **329**, not 333, and is `AsNoTracking()`, so it performs no fixup onto the tracked parent either. The working expression is the `treeCache.Exists(...)` in (a) — which makes the interim and the real fix the same one-line change. **There is no cheaper interim here, just the fix.**
>
> **(d)** The finder's test observation is right but is a test-strength item, not part of the defect: `AzureBulkImportTopUpTests.Target` sets `Name = "vnet-a"` and `TargetName` returns the sanitized VNet name `"vnet-a"` unqualified for a single-prefix VNet, so `proposed == exact.Name` and `WillRename` is false with or without the `!exact.HasChildSubnets` guard — reverting it leaves 847/847 green. Giving the fixture a different name makes it load-bearing, and the equivalent assertion should be added for the single-VNet commit.

---

## O9 — Round 14's top-up relaxation was applied to `AzureController.Import` but not to the Details page's duplicate of the same gate, so the single-VNet top-up wizard has no reachable link in the whole application `[x2]`

**Citation:** `src/Bastet/Controllers/SubnetController.Read.cs:124-129`.
**Confidence:** confirmed.

### What goes wrong

The "target must be empty" predicate exists in two places. The authority, `AzureController.Import` (`:45-47`), was deliberately relaxed by `N4`. The duplicate, `ViewBag.CanImportFromAzure`, still requires `subnet.ChildSubnets.Count == 0` and was not touched — `git show 8afa2df --stat` does not list `Read.cs`. `_RoleBasedActions.cshtml:25-32` is the **only** place in the entire codebase that links to `/Azure/Import/{id}` (grep for `asp-action="Import"` and `Azure/Import` over `Views` and `wwwroot` returns exactly that one hit), and it is gated on that flag.

The two predicates are therefore mutually exclusive by construction: whenever the server would accept a top-up, the button that leads to it is not rendered. `N4`'s relaxed branch, and the server-side `alreadyRecorded` filter added alongside it (`AzureController.cs:165-176`, whose comment says *"On a top-up the target keeps the subnets a previous import created"*), cannot be reached through the application's own UI. The button disappears permanently after the first successful single-VNet import — the steady state of the feature.

### Reproduced

Real headless Chromium driving the real wizard end to end on the unmodified build:

```
STEP1 empty target:  'Subnet Azure Import' link count = 1   hrefs: ['/Azure/Import/1']
STEP2 rows offered:  3;  ticked: 1 (rig-r15-snet-a1-single 10.10.1.0/24)
STEP2 flash:         "Successfully renamed parent subnet to 'rig-r15-vnet-a1' and imported 1 child subnets."
STEP3 'Subnet Azure Import' link count = 0
STEP3 ALL /Azure/Import hrefs anywhere on page: []
STEP3 unallocated: ['10.10.0.0 | 10.10.0.255 | 255 IP addresses | Create Subnet',
                    '10.10.2.0 | 10.10.255.254 | 65,023 IP addresses | Create Subnet']
STEP4 GET /Azure/Import/1 -> HTTP 200 | title Subnet Azure Import - BASTET | no error flash
```

Unlinked, not dead — hand-typing the URL runs the whole feature: rows offered `['10.10.2.0/24','10.10.3.0/24']` (`N4`'s server-side filter correctly dropped the already-imported one), flash *"imported 2 child subnets"*, free space 65,023 → 64,511, exactly two `/24`s carved out. Independently reproduced on a second target.

**Refutation attempts that failed:** no other link site exists, and no JS anywhere builds an `/Azure/Import` URL; the reconciler offers no substitute action (6 review items, all `AzureRangeNotImported` with `subnetId=0` and `suggestedAzureResourceId=''`, so `_ReconcileScripts.cshtml:303` renders an empty Action cell); `grep -rn CanImportFromAzure test/` returns zero hits.

**Refutation that partly landed, and drives the severity correction:** `BulkGetVNets` on the same state returns the prefix as `WillUpdateExisting` with *"Will add any missing subnets to existing Bastet subnet 'X'. Subnets already imported are left untouched."*, and Bulk Azure Import is in the nav on every page. A working **linked** route to the same outcome survives.

**Severity corrected medium → low**, on consequence and not on cost or rarity. The false free-space table this finding leans on is a pre-existing, explicitly owner-accepted residue recorded in `N3`'s and `N4`'s struck entries, and it rendered identically on the **patched** build before the top-up ran — charging it here double-counts it. The real delta is that one of two repair routes is missing, on the page that shows the problem, in the steady state of every Azure-linked subnet after its first import. Below `N4` (high — no route existed at all) and above round 12's `L2` (info — a per-user role mismatch on one button).

### Fix

Make `Read.cs:124-129` compute the same predicate `AzureController.Import` enforces:

```csharp
bool isTopUp = subnet.ChildSubnets.Count != 0 && !string.IsNullOrEmpty(subnet.AzureResourceId);
ViewBag.CanImportFromAzure =
    userContextService.UserHasRole(ApplicationRoles.Admin) &&
    azureImportEnabled &&
    (subnet.ChildSubnets.Count == 0 || isTopUp) &&
    subnet.HostIpAssignments.Count == 0 &&
    !subnet.IsFullyAllocated;
```

**Better, and what would have prevented this:** this is the third copy of an Azure-import predicate in the tree (`AzureController.IsAzureImportEnabled()`, `Read.cs:121-122` and `_Layout.cshtml:47` are three copies of the flag parse; the eligibility gate is now two). Lift the eligibility test into one internal static helper both call. Then add the assertion that is missing entirely — **no test in `test/` references `CanImportFromAzure`**, which is why `N4` slipped past.

**Cheaper interim**, one line at `:127`: `(subnet.ChildSubnets.Count == 0 || !string.IsNullOrEmpty(subnet.AzureResourceId)) &&`. Restores the button exactly on the set `Import` already admits; the GET remains the authority and still refuses anything else, so a wrong click cannot write anything.

> **The verifier judged the fix sound and checked it rather than nodding it through.** Both forms are set-equivalent to `Import`'s gate on all three arms, and Details already loads `ChildSubnets`, `HostIpAssignments` and `AzureResourceId`, so neither needs an extra query. The full fix was applied in a copy: build 0 warnings, 847/847, and live on the patched instance the button appears on exactly the admitted set — the Azure-linked populated target shows it and the top-up completes from it, while the hand-built populated parent with no Azure link still hides it **and** still gets *"Subnet already has child subnets and is not linked to an Azure VNet"*, and the fully-allocated target still hides it. The one hole worth probing — does exposing the button open a re-link to a different VNet? — is already closed at `SubnetController.Azure.cs:393-405` (409 plus rollback), and `GetCompatibleVNets` requires an exact address-space match so the wizard cannot offer a second VNet. Two owner notes, both already disclosed: the button label still reads *"Subnet Azure Import"* on a top-up, and the suggested consolidation is viable because `IsAzureImportEnabled` is already `internal static` and already called cross-controller.

---

## O10 — A failed re-link paints the reconcile wizard's fail-closed *"Nothing was checked / Azure could not be read"* banner directly on top of a still-visible, still-tickable stale-subnet deletion table `[x1]`

**Citation:** `src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:210`.
**Confidence:** confirmed.

### What goes wrong

`showScanError()` is written for one caller — a scan that failed — where `runScan`'s `beforeSend` (`:174-179`) has already hidden `#rec-scan-content` and `#rec-scan-warnings`. The re-link handler reuses it at `:398`, where nothing has been hidden. `#rec-scan-error` lives **outside** `#rec-scan-content` in the markup (`_StepReview.cshtml:12` vs `:29`), so the panels stay on screen. `invalidateScan()` also stamps `disabled` on `#rec-step2-tab` — the pill for the pane the operator is looking at.

The screen then asserts, in a red banner, *"Nothing was checked."* / *"This subnet is no longer reported as holding a range that moved to another Azure subnet. Nothing was changed. Re-run the scan and review the results."* / *"Because Azure could not be read, BASTET cannot tell which resources still exist, so nothing is offered for deletion. Fix the connection and scan again."* — while the deletion table is still visible immediately below it, headed *"These BASTET subnets no longer match Azure."*, with a live checkbox and a **Next: Confirm deletion** button. All three sentences are false: Azure was read successfully, the scan succeeded, and a row **is** offered for deletion on the same screen.

The file states the invariant this breaks, at `:320-324`: *"Defence in depth: clear the failure panel where the content is revealed, so a valid plan never renders underneath a 'Nothing was checked' message left by an earlier scan."*

### Reproduced

Two triggers, and the second is **one operator with one tab and no concurrency at all** — which refutes the finder's own "requires a second admin" framing. Scan the subscription, and while the reconcile tab sits open (it has no auto-refresh and no polling), any Azure change that resolves the drift — in the verifier's run, deleting the very subnet the wizard was suggesting a re-link to — makes the server's freshly-derived plan disagree and return the documented 409 at `SubnetController.AzureReconcile.cs:317-323`.

DOM immediately after the click, against the same snapshot before it:

```
before: scanErrorVisible false, scanContentVisible true, staleSectionVisible true,
        pills [... "rec-step2-tab:nav-link active" ...]
after : scanErrorVisible TRUE,  scanContentVisible TRUE, staleSectionVisible TRUE,
        pills [... "rec-step2-tab:nav-link active disabled" ...]

staleRows        = ["rig-r15-c13v-snstale 10.113.9.0/24 Subnet deleted
                    The Azure subnet this was imported from no longer exists."]
staleCheckboxes  = [{value:"3", disabled:false, checked:false}]        <- live, tickable
```

Ticking that checkbox: `{"goConfirmDisabled": false, "goConfirmVisible": true}` — the red **Next: Confirm deletion** button **re-arms** underneath a banner reading *"nothing is offered for deletion"*. Clicking it: `{"step3Active": false, "activePane": "rec-step2", "confirmDeleteDisabled": true}`, and the only POSTs the page ever made were the scan (200) and the re-link (409). Fail-closed, but the armed button is inert. (That inert button is **O15**.)

A full-page screenshot shows all of it stacked in one viewport: the red *"Nothing was checked"* banner, then the yellow *"1 subnet(s) were withheld from deletion because their address range is still assigned in Azure…"* warning — a statement only a **successful** scan can make — then the deletion table with a ticked checkbox and an armed red button, then the review row whose Re-link button is stuck reading *"Re-linking…"*.

**Severity corrected medium → low**, on consequence ceiling and not on rarity — the trigger was **widened**, not narrowed. `DeletedSubnets` COUNT = 0, no wrong write, no wrong archive, no range misreported as free, the false claim points in the conservative direction, and recovery is two clicks. This repo filed round 13's `M2` — a false *"deleted 0 stale subnet(s)"* banner painted over a delete that **had** archived two rows — at low; this is strictly less consequential. It is above info because of the affirmative falsehood on a destructive-decision screen plus the re-armed inert destructive button.

### Fix

Give the re-link its own error surface and stop routing it through `showScanError`.

**Cheaper interim**, one line, keeps a single error surface but stops the screen contradicting itself: in `showScanError` add `$("#rec-scan-content").addClass("d-none"); $("#rec-scan-warnings").addClass("d-none");` beside the existing lines, mirroring `runScan`'s `beforeSend`. The operator loses a still-valid scan and must re-scan, but is never shown a deletion table under a banner saying nothing was checked.

> **The verifier judged the direction right and the primary fix incomplete, resting on a claim that is false for its own headline trigger.**
>
> **(a) The load-bearing justification is wrong on the 409 branches.** *"A failed re-link changed nothing, so the scan results and `lastPlan` remain exactly as valid as they were"* does not hold for the conflict at `:317-323`, which is precisely the case where the server has just re-derived the plan and found the displayed verdict **withdrawn**. Leaving `lastPlan` intact leaves a review row asserting *"the range … is still assigned in Azure to subnet X, re-link it"* over a button that now 409s on every press, and contradicts the server's own instruction. It cannot cause a wrong archive — `BulkDeleteStaleAzureSubnets` re-scans and refuses at `:62`, `:76`, `:96` and `:110` — but it is a weaker posture than HEAD, so the fix must not ship on the stated rationale.
>
> **(b) `:391` is dead.** `RelinkAzureSubnet`'s only `Ok()` is at `:374` with `success = true`; every failure branch is 403/400/404/409/503. With `dataType "json"`, only the error handler at `:398` can run. Give **that** the new surface and delete the `:391` call.
>
> **(c) Neither version restores the button label.** `beforeSend` at `:382` does `btn.text("Re-linking...")`; `complete` at `:400-405` re-enables the buttons and never puts the text back, so after **any** failure the row's Re-link button reads *"Re-linking…"* permanently. Same six lines; fix it here.
>
> **(d) The cheap interim closes only half.** It removes the contradiction and is safe — it loses nothing `invalidateScan()` had not already lost, and is a no-op for the callers where `beforeSend` already hid those panels — but it leaves `_StepReview.cshtml:14` and `:17-20` asserting *"Nothing was checked"* and *"Because Azure could not be read"* for a failure in which Azure **was** read.
>
> **Recommended shape instead:** a dedicated `showRelinkError(btn, message)` that restores the button's original label, renders the server message in a new `#rec-relink-error` alert placed **outside** `#rec-scan-content` (so a re-scan does not wipe it) with honest prose of its own, and then calls `runScan()`. `runScan`'s `beforeSend` already hides the three panels and `renderPlan` rebuilds every row, so the operator lands on a current, internally consistent screen with the reason still on it — which is exactly what the 409's own text asks for, and it closes all branches including the `:300-308` 400 (a transient ARM failure or throttle during the re-link's own re-scan) that the finder's trigger list omits and that is probably the commonest one in production. If an automatic re-scan is unwanted, the minimum sound alternative is the interim's two lines **plus** moving the two false sentences out of `_StepReview.cshtml`'s static markup into text the caller supplies.

---

## O11 — Bulk import preview reports `canCommit=true` and an Azure subnet as `Available` when an existing BASTET subnet lies inside that range; the commit then 400s and rolls back the whole multi-VNet import `[x1]`

**Citation:** `src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:733` (`DetectExistingBastetSubnetConflicts`, equality test at `:747-749`); annotation mirror at `:381-399`.
**Confidence:** confirmed.

### What goes wrong

The planner's own contract is stated at `:9-10`: *"All decisions and conflict checks are made here so the preview UI shows exactly what commit will do."* Its only BASTET-side conflict test for a planned **child** is exact address equality. `ValidateSubnetCreation`, which every write funnels through, additionally refuses a subnet that would **contain** an existing BASTET subnet (`SubnetController.Helpers.cs:305-316`) and one for which a **more specific** BASTET parent exists (`:288-301`). Neither is mirrored in the plan or in the availability annotation.

`LoadSubnetTreeForBatchAsync` loads the whole table, so the commit-side check is complete and the entire gap is on the planner side. The prefix-level gate that used to stop this (`:300`, `exact.HasChildSubnets && !isTopUp`) is deliberately relaxed for a top-up by `N4`, so the wizard now reaches the child-planning path with a populated target **by design**.

### Reproduced

Precondition built through the ordinary `/Subnet/Create` form (302 → Details, zero Azure involvement): a hand-made `handmade-half` `10.10.2.0/25` inside the Azure-linked target `10.10.0.0/16`. Azure subnet `rig-r15-snet-a1-multi` owns `10.10.2.0/24`.

```
GET /Azure/BulkGetVNets
  PREFIX 10.10.0.0/16  WillUpdateExisting  "Will add any missing subnets to existing Bastet subnet..."
  SUBNET rig-r15-snet-a1-multi 10.10.2.0/24  Available  reason=None  isSelectable=True   <-- the defect

POST /Azure/BulkImportPreview  (that prefix + an unrelated, error-free VNet in the same selection)
  canCommit=True globalErrors=[]  item errors: []  for both

POST /Subnet/BulkCreateFromAzurePlan
  400 {"success":false,
       "error":"This subnet would contain existing subnet handmade-half (10.10.2.0/25).
                This would create an invalid hierarchy.",
       "globalErrors":[], "itemErrors":[]}
DB AFTER: unchanged -- the unrelated VNet, which had no conflict of any kind, was rolled back too
```

**Control**, same selection with only `handmade-half` removed: identical plan, commit `200 {"createdTargets":1,"createdChildSubnets":2}`. The plan the planner produced was unchanged; only the containment fact differed, and the planner never looked at it.

**Second leg** (more-specific existing parent): annotation `Available`/selectable, preview `canCommit=True`, commit `400 {"error":"A more specific parent subnet exists: handmade-mid (10.20.4.0/22). Please select it instead.","itemErrors":[]}` — advice naming a parent selector that does not exist in this wizard.

**Browser, end to end:** checkbox `disabled=False`, row label carries **no badge at all** (`availabilityBadge` returns `""` for `Available`, `_BulkScripts.cshtml:172`), global-errors pane hidden, plan tree renders a green *"Create rig-r15-snet-a2-gap … 10.20.12.0/24"*, Continue-to-Commit and Confirm both enabled — then a red panel naming a BASTET subnet the operator never selected, with an empty `<ul>` beneath it because `globalErrors` and `itemErrors` are both `[]`.

For contrast, in the same `BulkGetVNets` response `AnnotatePrefix` **does** apply the containment rule at prefix level: `rig-r15-vnet-a3-overlap` `10.10.0.0/20` → `Blocked` *"Would contain existing Bastet subnet 'rig-r15-snet-a1-single' (10.10.1.0/24), which would create an invalid hierarchy."*

### Fix

Extend `DetectExistingBastetSubnetConflicts` to run the two containment tests `ValidateSubnetCreation` applies for every planned child, and mirror them in `AnnotateSubnet` so the selection UI greys the row with a reason, exactly as it already does for the exact-address collision.

**Cheaper interim, no planner change:** when `ValidateSubnetCreation` fails inside `BulkCreateFromAzurePlanCore` (`SubnetController.BulkAzure.cs:379-392` and `:457-468`), populate `itemErrors` with the failing item's `VNetName`/`VNetPrefix` instead of `Array.Empty<object>()`, so the wizard can point at the row that killed the import rather than showing a bare sentence about a subnet the operator never selected.

> **The verifier judged the interim sound and the full fix unsound as written, with three defects.**
>
> **1. The more-specific-parent leg cannot be evaluated against `existingSubnets` alone, and doing so would refuse ordinary imports that work today.** For an `AutoCreateChild`/`AutoCreateTopLevel` item the target does not exist yet when the plan is built, so the deepest existing container of a planned child is legitimately something other than its parent-to-be. Counter-example, built: BASTET holds `10.30.0.0/8`; import a new VNet prefix `10.30.0.0/16` carrying Azure subnet `10.30.1.0/24`. At commit, `ValidateSubnetCreation` sees the just-created `/16` in `treeCache` (`SubnetController.BulkAzure.cs:408` appends it) and accepts the child. A planner check reading only `existingSubnets` sees `bestParent` = the `/8` and would emit a global error for a plan that commits **200 today** — turning a preview/commit divergence into one in the other direction. The rule must be scoped to the item: refuse a planned child when an existing BASTET subnet is contained in **this item's VNet prefix** and strictly contains the planned child.
>
> **2. "For every auto-created target" duplicates a test that already exists and already fires correctly.** `DetectVNetPrefixWouldContainExistingSubnet` (`:765-794`) plus its `AnnotatePrefix` mirror (`:340-345`) already cover that case — both confirmed working. Adding it here would emit the identical global error twice. Restrict the new work to planned **children**.
>
> **3. The `AnnotateSubnet` mirror does not compile as described and is only half-mirrorable.** `AnnotateSubnet` is `private static` (`:352`) and has no `ipUtilityService`, so it must become an instance method. More importantly only the **would-contain** half can be mirrored safely: the annotation pass runs per VNet with no prefix-to-target binding for a target that does not exist yet, so the more-specific-parent half has no well-defined answer at annotation time. Mirror the would-contain half and leave the other to the plan, accepting that the selection UI is then strictly weaker than the plan — still a large improvement on the current silence.
>
> One thing the fix can safely skip: rows created earlier in the same commit. `DetectAzureSubnetOverlaps` (`:696-726`) already forbids overlapping selected Azure subnets.
>
> **The interim is sound and was checked concretely:** at `:379-392` the loop variable `item` is in scope, and at `:457-468` both `item` and `child` are, so the shaped object is available at both sites, and `showCommitError` (`_BulkScripts.cshtml:734-742`) already renders `itemErrors` as `[vNetName vNetPrefix] message`. It does **not** fix the false `canCommit=true`, so it is a mitigation and not a substitute.
>
> **Two prose overstatements corrected, neither material.** *"The selection UI shows the row green"* is wrong when measured — the row carries no badge, no colour and no reason line, just a name and a muted CIDR beside an enabled checkbox, i.e. **less** flagged than the finding says. And listing `DetectVNetPrefixWouldContainExistingSubnet`'s exact-match skip (`:773-779`) as part of the gap is wrong: that skip is correct, because on an exact match the target already exists and legitimately contains its own children. The real gap is that no containment test of any kind runs on planned children.

---

## O12 — `N4` added a fully-encompassing-on-populated-target refusal to the bulk planner but not to the availability annotation, so the selection screen offers a run the preview then refuses outright `[x1]`

**Citation:** `src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:363` (the `if (encompassesAPrefix)` block at `:363-369`; the planner's refusal at `:574-587`; `AnnotatePrefix`'s top-up branch at `:290-321`).
**Confidence:** confirmed.

### What goes wrong

`BulkImportAvailability`'s own contract (`AzureBulkImportViewModels.cs:6-10`) is *"Computed server-side so the selection UI and the planner apply one set of rules. If the UI re-derived this, the two could disagree and either disable an importable item or offer one that fails at preview."*

`N4` introduced a new hard refusal in `BuildPlanItem` — an Azure subnet covering the whole prefix cannot mark a target fully allocated when the target already has children — and noted *"The old blanket refusal of populated targets was preventing this incidentally; the top-up allowance makes it reachable, so it is now refused explicitly."* Neither `AnnotatePrefix`'s new top-up branch nor `AnnotateSubnet`'s encompassing branch (which unconditionally returns `Available`/`IsSelectable=true`) learned the rule.

The preview then returns `canCommit: false`, and because `CanCommit` is **plan-wide** (`AzureBulkImportViewModels.cs:436`), one such prefix blocks every other VNet in the same bulk run until the operator finds and deselects it.

### Reproduced

The verifier reached it with **no hand-made BASTET row at all** — every row was written by the bulk wizard itself: import `10.99.0.0/24` with its one subnet `10.99.0.0/25`, then do in Azure what operators routinely do — delete that subnet and create one covering the whole address space.

```
GET /Azure/BulkGetVNets
  PREFIX 10.77.0.0/24  WillUpdateExisting  isSelectable=True
    "Will add any missing subnets to existing Bastet subnet 'rig-r15-b6b-vnet-full'.
     Subnets already imported are left untouched."
  SUBNET rig-r15-b6b-snet-full 10.77.0.0/24  Available  isSelectable=True
    "Covers the whole VNet prefix, so it marks the target fully allocated instead of being created."

POST /Azure/BulkImportPreview -> canCommit: False, globalErrors: [], item errors:
  ["Cannot import VNet prefix 10.77.0.0/24: Azure subnet 'rig-r15-b6b-snet-full' covers the
    whole prefix, which would mark Bastet subnet 'rig-r15-b6b-vnet-full' fully allocated,
    but it already has child subnets."]
```

**Plan-wide blast radius confirmed:** a preview of two VNets returned `errors: []` for the clean one and the sentence above for the other, `canCommit: False`; the commit returned `400` and the clean VNet wrote nothing.

Browser: both checkboxes `disabled=False`, both ticked without complaint, step 3 renders the Errors panel and `#bulk-go-commit-btn disabled=True`.

**Provenance:** at `6d1a4cb` (pre-`N4`) `AnnotatePrefix:197` blocked any populated exact-match target and `BuildPlanItem:379` errored on the same condition, so the two agreed and the state was unreachable. `N4` relaxed both for top-up and added the compensating refusal to one side only.

Severity **low** stands: nothing wrong is persisted, the refusal names the offending prefix and subnet, the plan tree shows it on the same screen, and one deselect recovers. What is wrong is that the screen whose stated job is to stop the operator assembling an uncommittable run is the thing that assembled it — and it takes every other VNet in the run with it.

### Fix

In `AnnotateSubnet`, the `encompassesAPrefix` branch must look up the prefix's exact-match target and, when that target has children, return `Blocked` with the same sentence `BuildPlanItem` produces. `AnnotatePrefix`'s top-up branch should carry the matching caveat so the two screens agree.

> **The verifier judged the main fix sound and the offered interim unsound — do not ship the interim.**
>
> The interim read: *"in `AnnotatePrefix`, when the top-up allowance fires and any of the VNet's own prefixes equals one of its subnets' prefixes, return `Blocked`."* Two defects. **(a)** The top-up allowance is `isTopUp = IsSameVNet(exact, vnet)` at `:298`, computed **before and independently of** `exact.HasChildSubnets` — so the interim also blocks top-up + encompassing subnet on an **empty** target, which was measured working at HEAD (preview `canCommit True`, `willMarkFullyAllocated True`, commit `200 {"fullyAllocatedTargets": 1}`). That would disable an importable item, the other half of the very failure mode the enum's contract warns about. **(b)** *"Any of the VNet's own prefixes"* is not scoped to the prefix being annotated; on a multi-address-space VNet (the rig ships one with three) an encompassing subnet under `192.168.100.0/24` would block the unrelated, perfectly importable `10.20.0.0/16` target. A correct interim must be scoped to both the prefix **and** `exact.HasChildSubnets` — at which point it is no cheaper than the real fix.
>
> **The main fix is complete**, and it was checked: the only other refusal `BuildPlanItem` raises for an encompassing selection is `p.Subnets.Count > 1` (`:564`), which Azure cannot produce and the annotation cannot see, and every other exact-match refusal already blocks the **prefix**, which disables the subnet checkbox through `subnetBlockedByPrefix` (`_BulkScripts.cshtml:238`, `:249`). `AnnotateSubnet` is static and needs no `IIpUtilityService` — the encompassing test is a string equality and the target lookup is an exact `{NetworkAddress, Cidr}` match via the existing static `TryParseCidr`. Cleanest shape is to reuse the prefix annotation already computed in `AnnotateAvailability` (`:242-247`).
>
> **Residue the fix does not name, and the owner should decide:** after the subnet is blocked, a single-subnet VNet in this state still shows the prefix as *"Will add any missing subnets…"* and selecting it alone commits 200 having added nothing. The finding's *"`AnnotatePrefix`'s top-up branch should carry the matching caveat"* is the right instinct but is left unspecified; state the wording, or accept the no-op top-up explicitly.
>
> **The second gap bundled into this fix's text is a different defect and is filed separately as O11.** Folding it in here risks it being triaged at this severity; it is more severe, because there the preview affirmatively certifies a committable plan.

---

## O13 — `GET /Account/Logout` deletes every cookie the browser presents, not only Bastet's — an anonymous, tokenless, cross-site-triggerable request wipes co-hosted applications' session cookies `[x1]`

**Citation:** `src/Bastet/Controllers/AccountController.cs:53-57`.
**Confidence:** confirmed.

### What goes wrong

`Logout` is `[AllowAnonymous]`, is a GET, and deliberately carries no antiforgery token — the action's own remarks and `ControllerAuthorizationTests.AllowedMissingAntiForgery` record that logout CSRF is accepted because *"the worst outcome is an unwanted sign-out, with nothing read or written."* The loop then walks `Request.Cookies.Keys` — every cookie the browser sent, including ones Bastet never issued — and calls `Response.Cookies.Delete(name)`.

The stated justification for leaving the endpoint unprotected is measurably wrong on its own terms: something **is** written, and it is not Bastet's.

The load-bearing precondition is only that another application shares Bastet's **hostname**, and cookies ignore port (RFC 6265 gives no port isolation) — proved rather than assumed: the co-hosted app was on `127.0.0.1:5397` and Bastet on `127.0.0.1:5398`, and an ordinary Sign-out click on Bastet destroyed the other app's session cookie. That is the README's own quickstart shape with any second tool on another port of the same box.

### Reproduced

Header emission, anonymous cross-site GET with three cookies Bastet never issued:

```
HTTP/1.1 302 Found   Location: /Account/SignedOut
Set-Cookie: other_app_session=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/
Set-Cookie: grafana_session=;   expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/
Set-Cookie: connect.sid=;       expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/
```

Real Chromium, three contexts:

```
T1 first-party click: before [coapp_httponly path=/, coapp_scoped path=/coapp, grafana_session path=/]
                      after  [coapp_scoped]
                      DELETED BY BASTET: ['coapp_httponly', 'grafana_session']
T1' single antiforgery-shaped cookie: Bastet expires ".AspNetCore.Antiforgery.abc",
                      a name it never minted (its own is .AspNetCore.Antiforgery.bIgxla0GM5k)
T2 cross-site <img>:  deleted []       <- nothing
T3 cross-site nav:    deleted ['grafana_session']; jar empty afterwards
```

`grep -rn "Response.Cookies" src/` returns exactly one hit — this loop. No `AddSession`/`HttpContext.Session` anywhere; in Development `Program.cs:176-188` registers only `DevAuthScheme`, no `AddCookie`.

**Three corrections the write-up must carry.** The `<img src=...>` vector and the *"refreshing iframe re-kills the session every few seconds"* claim are **false** — a cookie with no explicit `SameSite` is Lax by default and is not sent on a cross-site subresource request, and Bastet additionally answers `Content-Security-Policy: frame-ancestors 'none'` and `X-Frame-Options: DENY`. What works is a cross-site **top-level navigation** (a clicked link, an attacker-page redirect); "cross-site-triggerable" survives, the embed vectors do not. The finder's curl evidence hid this because curl sends whatever `Cookie` header it is handed. *"Every cookie the browser presents"* is true of the emitted headers but not of the destruction: only host-only, `Path=/`, non-prefixed cookies actually leave the jar — `coapp_scoped` at `Path=/coapp` survived, and `__Host-`/`__Secure-` prefixed cookies survived because `Delete` emits no `Secure` attribute (measured, not reasoned; Chromium treats `127.0.0.1` as trustworthy and accepts `Secure` cookies there). A **plain** `Secure` cookie at `Path=/` **is** destroyed, so "Secure protects you" is not the rule — the prefix is. And the illustrated deployment (`https://ops.example.com/bastet` behind path-based routing) is not a working Bastet deployment: `grep -rn "PathBase|X-Forwarded-Prefix" src/` returns nothing and every URL Bastet generates is root-absolute.

### Fix

Delete the loop. `HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme, properties)` on the line below already removes the auth ticket cookie **and its `C1..Cn` chunks** through `ChunkingCookieManager`. The antiforgery and TempData cookies the loop also clears hold no session state and are re-minted on the next request. In Development there is no cookie scheme at all, so the loop is dead weight there too.

**Cheaper interim, if the belt-and-braces clear is wanted:** filter the loop to the names Bastet actually issues, resolved from options rather than guessed. **Do not** filter on a `.AspNetCore.` prefix — a co-hosted ASP.NET Core application uses the same prefix and would still be clobbered.

> **The verifier judged the headline fix sound and the interim broken as specified.**
>
> The primary fix was validated on a copy with `:53-57` removed: `dotnet test` **847 passed / 0 failed** — no test asserts the loop's behaviour (`AccountControllerLogoutTests` pins only the redirect target and that the cookie sign-out ran); and against the loop-removed **Production** build, a request carrying `.AspNetCore.Cookies=chunks-2` plus `C1`/`C2` still received all three deletions with `path=/; secure; samesite=lax; httponly`, emitted entirely by `ChunkingCookieManager`, while `coapp_session` and `grafana_session` received no `Set-Cookie` at all. The chunk case the fix depends on is covered.
>
> **One residue to state rather than assume:** with the loop gone, the two cookies Bastet does issue survive logout — `.AspNetCore.Antiforgery.<hash>` and `.AspNetCore.Mvc.CookieTempDataProvider`. Neither carries session state: the antiforgery cookie token is user-agnostic and validation compares the identity embedded in the **request** token to the current user, so a retained cookie cannot authorise a stale principal. This is stock ASP.NET Core behaviour — the framework never deletes either at sign-out. Worst case is a leftover flash message rendering once after a logout in the same browser.
>
> **The interim is unsound as specified.** `IOptionsMonitor<CookieAuthenticationOptions>.Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie.Name` is **null in Development**: `PostConfigureCookieAuthenticationOptions` is registered by `AddCookie`, and `Program.cs:177-189` never calls it. `Response.Cookies.Delete(null!)` throws `ArgumentNullException`, so **every Development logout becomes a 500**. Any implementation must skip null/empty names for all three sources. The antiforgery limb is fine. Its *"do not filter on a `.AspNetCore.` prefix"* caveat is correct and worth keeping — but note the interim is strictly more code for less benefit than deleting the loop, and re-derives what the framework already does.
>
> **Separately, and for the owner:** in Production this same anonymous GET runs `SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme)` for a caller with **no** session and 302s to the IdP end-session endpoint, ending the SSO session for every relying party — reproduced. Gating the remote leg on `User.Identity?.IsAuthenticated == true` would confine it; leaving it also cleans up an IdP session that outlived an expired Bastet cookie. That is a decision, not part of this defect.

---

## O14 — `POST /Subnet/BulkDeleteStaleAzureSubnets` throws `NullReferenceException` (HTTP 500) on a null element in `statuses` — the one null-element shape the codebase guards on every sibling path `[x1]`

**Citation:** `src/Bastet/Controllers/SubnetController.AzureReconcile.cs:87-90` (throw site `:89`).
**Confidence:** confirmed.

### What goes wrong

`AzureReconcileDeleteDto.Statuses` is `List<AzureReconcileApprovedVerdict>` — reference elements, added by round 14's `N2`. `(request.Statuses ?? [])` guards only the **collection** being null, not a null **element**. `System.Text.Json` materialises `"statuses":[null]` as a one-element list containing null, and `.GroupBy(s => s.SubnetId)` dereferences it. The throw sits **before** the action's only `try` (which starts at `:143` and catches only `TimeoutException`), so nothing in the action handles it.

Ordering matters: `approved` is computed at `:87-90`, **before** the `noLongerStale` 409 at `:96`, so even a request naming a subnet id that is not stale at all crashes rather than being refused.

The wizard's AJAX `error:` handler (`_ReconcileScripts.cshtml:544-547`) cannot `JSON.parse` an HTML body and renders *"Server error: 500"* — the exact failure mode `SubnetController.BulkAzure.cs:78-84` documents itself as existing to prevent: *"a list element can itself be null … the documented JSON API stopped answering JSON and the wizard showed 'Server error: 500' in place of the planner's own message."* That is the sentence this endpoint now earns, on the round-14 code that introduced `Statuses`.

### Reproduced

```
POST /Subnet/BulkDeleteStaleAzureSubnets
  {"subscriptionId":"f0e8d6db-...","confirmation":"approved","subnetIds":[1],"statuses":[null]}

HTTP 500, content-type: text/plain
System.NullReferenceException: Object reference not set to an instance of an object.
   at Bastet.Controllers.SubnetController.<>c.<BulkDeleteStaleAzureSubnets>b__10_2(...)
      in .../SubnetController.AzureReconcile.cs:line 89
   at System.Linq.Lookup`2.Create(...) / Enumerable.ToDictionary(...)
   at ...BulkDeleteStaleAzureSubnets(...) in .../SubnetController.AzureReconcile.cs:line 87
DB after: Subnets still holds id 1; DeletedSubnets count 0
```

Identical with `subnetIds:[9999]` (an id that is not stale), and with `statuses:[null, {valid verdict}]` — so it is the null **element**, not an empty list.

**Controls on the same binary and run** — every adjacent malformed shape stays inside the modelled-JSON envelope:

| body | result |
|---|---|
| `statuses:null` | 409 application/json |
| `statuses:[]` | 409 application/json, byte-identical |
| `subnetIds:[null]` | 400 `{"error":"No request was provided."}` |
| `subscriptionId:null` | 400 `{"error":"Azure could not be re-checked, so nothing was deleted."}` |
| `statuses:[{"subnetId":9999,"statusName":null,"reason":null}]` | 409 application/json |

One specific omission, not general fragility. Reachability was checked rather than read around: `GlobalSanitizationFilter` is the only global action filter and explicitly skips null collection elements (`:52-57`), so `:89` is genuinely the first dereference; `SubnetController` carries no `[ApiController]`, so no automatic ModelState 400 intervenes; and with a real subscription id the ARM re-scan succeeds, proven by the `statuses:null` control returning the 409 rather than the "could not be re-checked" 400.

Nothing is written and there is no authorization effect — `Subnets` and `DeletedSubnets` were selected before and after every probe and were identical, and the positive control archives correctly on the same binary. That is why this is **low**. The narrow trigger (the wizard's own `chosen.map` at `:428` cannot emit a null, so it takes a hand-built, scripted or replayed body) is one sentence of scenario and not a severity reduction — and any caller building `statuses` from a sparse array emits null.

### Fix

Drop null elements before grouping:

```csharp
Dictionary<int, AzureReconcileApprovedVerdict> approved =
    (request.Statuses ?? [])
        .Where(s => s is not null)
        .GroupBy(s => s!.SubnetId)
        .ToDictionary(g => g.Key, g => g.Last()!);
```

Fail-closed and needs no new message: a null element establishes no approved verdict, so the row falls to `approved.GetValueOrDefault(id) == null`, `VerdictMatchesApproval` returns false via its documented *"a MISSING verdict … licenses nothing"* case at `:250-254`, and the existing 409 at `:116` refuses it. Matches what `AzureBulkImportPlanner` already does on the two bulk-import paths.

**Cheaper interim** (one guard beside the siblings at `:50-53`, before the ARM round trip is paid):

```csharp
if (request.Statuses is not null && request.Statuses.Exists(s => s is null))
{
    return BadRequest(new { success = false, error = "An approved verdict entry was empty." });
}
```

> **The verifier judged the fix sound, built it, and offered two refinements.** Applied to a copy: build 0 warnings, `dotnet test` 847/847, and on a patched instance against a genuinely stale seeded row, `statuses:[null]` returns 409 modelled JSON with nothing archived, via exactly the route claimed; the correct-verdict commit still returns 200 and archives. The two `!` suppressions are redundant but compile clean.
>
> **Refinement 1: apply both, not one.** A mixed `[null, {valid}]` body **is** malformed, and refusing it with a specific message is better than silently dropping the element — measured with the primary fix alone, that body returns **200 and archives the row**. Defensible per-row, but it means a malformed body still produces a destructive write.
>
> **Refinement 2: the primary fix alone answers a malformed body with a misleading message** — *"The reason 1 of the selected subnet(s) were flagged has changed since you reviewed them. Nothing was deleted. Re-run the scan and review the results."* Nothing changed, the caller sent garbage, and re-running the scan will not help. Make the interim's *"An approved verdict entry was empty."* the primary fix and keep the LINQ filter as the second layer — the same two-layer shape the maintainers already chose on the sibling path.
>
> One thing the fix does **not** need: null `StatusName`/`Reason` are already safe — `Enum.TryParse(null, …)` returns false and `string.Equals(null, x, Ordinal)` returns false, so `:89` is the only dereference of a status element in this action.

---

# Info

## O15 — The reconcile wizard's *"Next: Confirm deletion"* re-arms on any checkbox tick after `lastPlan` was dropped, and is then a permanently live, silently inert button `[x1]`

**Citation:** `src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:331-333` (`updateGoConfirmBtn`); the inert return is at `:419`.
**Confidence:** confirmed.

### What goes wrong

`refreshDeleteButton()` (`:85-89`) was deliberately given a `hasSnapshot` conjunct, and its own comment at `:80-84` says why: gating on the typed text alone *"left the button clickable after the snapshot had been dropped, and clicking it hit a bare `return` in the click handler — no message, no spinner, no state change. A permanently live, inert button."*

Its sibling `updateGoConfirmBtn()` never got the same treatment: it gates only on `selectedIds().length === 0` and never looks at `lastPlan`. So once `invalidateScan()` has nulled `lastPlan` while the rows are still on screen — reachable through **O10** — the next tick of any `.rec-item-checkbox`, or of `#rec-select-all` (`:353-357`, which the finder omits), re-enables `#rec-go-confirm-btn`. Clicking it hits `if (ids.length === 0 || !lastPlan) { return; }` at `:419` and does nothing at all.

### Reproduced

Reached with no second browser tab, no crafted POST and no database edit: scan, then a colleague in the Azure portal deletes the subnet the wizard was suggesting a re-link to (an `az delete` run mid-review), then click Re-link → 409 → `showScanError` → `invalidateScan` drops `lastPlan` while the stale table stays on screen.

```
after the 409:  goConfirmDisabled true,  goConfirmVisibleOnScreen true
stale checkboxes on screen: 1
#rec-go-confirm-btn disabled after tick: False        <-- re-armed by updateGoConfirmBtn

=== AFTER CLICKING Next: Confirm deletion ===
{ "activePane": ["rec-step2"], "pills": [..., "rec-step3-tab:nav-link disabled"],
  "confirmCount": "0", "confirmList": "", "goConfirmDisabled": false }
```

Active pane unchanged, step-3 pill still disabled, confirmation screen never built, no message, no spinner, **no network request**. The button stays enabled, so it is clickable again and again with the same nil effect.

**Control**, same button, healthy plan: `"activePane": ["rec-step3"], "confirmCount": "1", "confirmList": "rig-r15-verc14-sn-gone (10.181.2.0/24) - Subnet deleted …"`. The button is not broken in general — only after `lastPlan` was dropped.

**Patched build**, identical scenario: `#rec-go-confirm-btn disabled after tick: True`, and Playwright's click timed out on a disabled element. Happy path on the same patched build still reaches step 3 and the full delete still commits (*"Deleted 1 stale subnet(s), archiving 1 subnet(s)…"*, row gone from `Subnets`).

**Severity corrected low → info**, on consequence and not on rarity. Nothing is written, nothing archived, no allocated range reported free, no false success banner, and no destructive path opens: the guard at `:419` correctly refuses to build a confirmation from a dropped plan, `confirmedIds` stays null so `#rec-confirm-delete-btn` stays disabled (measured throughout), and `renderPlan` is the sole writer of `lastPlan` and always rebuilds the rows with it, so no stale-plan deletion is reachable. Recovery is one click. This is strictly less than round 13's `M2` (graded low, and it produced a false *"deleted 0 stale subnet(s)"* banner **after** a real delete) and is the same shape as round 12's `L4` (graded info — a wizard button re-armed with no data consequence).

### Fix

Give `updateGoConfirmBtn` the same snapshot conjunct its sibling already has:

```javascript
$("#rec-go-confirm-btn").prop("disabled", !lastPlan || selectedIds().length === 0);
```

That is the single definition of "there is something to advance to", and it cannot drift from the `:419` guard the way it does now. No cheaper interim is needed — this is one expression. (Fixing O10 removes the only route that currently reaches it, but the gate is the invariant and should not depend on which caller happens to clear `lastPlan`.)

> **The verifier judged the one-liner correct and complete, with three additive notes.** `updateGoConfirmBtn` (`:332`) is the **only** site that ever arms the button (the element ships `disabled`, and `:56` only ever disables it), and its three callers are `:317` `renderPlan` (where `lastPlan` was just assigned and is always truthy), `:349` and `:355` — so the conjunct cannot wrongly disable the button on the happy path and it closes **both** re-arm routes.
>
> **Do not "fix" this instead by making `showScanError` hide `#rec-scan-content`** the way `runScan`'s `beforeSend` does — the Re-link buttons live inside `#rec-scan-content` and `:400-405` documents that a failed re-link must stay retryable. The minimal gate really is the right shape here. `invalidateScan`'s own `prop("disabled", true)` at `:56` becomes redundant but is harmless; keep it as defence in depth.
>
> Two adjacent residues this fix does not touch and must not be conflated with it: the static prose in `#rec-scan-error` is false when reached from a re-link 409 (that is **O10**), and after a failed re-link the Re-link button is re-enabled at `:404` but its label is never restored, so it sits enabled reading *"Re-linking…"* indefinitely (also **O10**, correction (c)).

---

## O16 — `RelinkAzureSubnet` writes `TempData["SuccessMessage"]` on a JSON-only response the wizard never redirects from, so the *"Re-linked X to Azure subnet Y"* banner surfaces later on an unrelated page `[x1]`

**Citation:** `src/Bastet/Controllers/SubnetController.AzureReconcile.cs:371`.
**Confidence:** confirmed.

### What goes wrong

Every other `TempData` writer in this app either re-renders a view that includes `Views/Shared/_TempDataAlerts.cshtml` or returns a `redirectUrl` the client navigates to. `RelinkAzureSubnet` returns bare JSON at `:374-380` with no `redirectUrl`, and the client at `_ReconcileScripts.cshtml:384-390` just calls `runScan()` — no navigation. `Views/Azure/Reconcile.cshtml` does not render `_TempDataAlerts`, so nothing reads the entry, and ASP.NET Core retains an unread `TempData` entry across request after request.

`_TempDataAlerts.cshtml:5-7` documents this exact hazard in its own header comment: *"ASP.NET Core only removes a TempData entry when it is **read**, so an unrendered message survives into the next request and can surface later on an unrelated page."* Only 8 views render that partial, and `_Layout.cshtml` is not one of them.

### Reproduced

Setup was the natural path the endpoint exists for — an Azure subnet "rename", i.e. delete-and-recreate — followed by an ordinary Re-link click.

```
POST /Subnet/RelinkAzureSubnet -> 200
  {"success":true,"subnetId":2,"azureResourceId":".../subnets/rig-r15-c16-new2", ...}
  Set-Cookie: .AspNetCore.Mvc.CookieTempDataProvider=CfDJ8I0Ofd...; path=/; samesite=lax; httponly
  redirectUrl present in body: False
(the wizard's automatic POST /Azure/ReconcileScan runs)
GET /Azure/Reconcile   [200] alert-success -> only the two STATIC panels
GET /Azure/BulkImport  [200] alert-success -> only the static hidden panel
GET /                  [200] alert-success -> []
GET /Subnet/Delete/2   [200] alert-success -> ["Re-linked 'rig-r15-c16-new' to Azure subnet
                                               'rig-r15-c16-new2'."]
GET /Subnet            [200] alert-success -> []
```

The entry survived **four** intervening requests and then rendered as a green success banner on a destructive confirmation page, clearing only once actually read.

**Stronger variant:** re-link, then a *failed* delete POST (`confirmation` not `approved`), then `GET /Subnet/Delete/1`:

```
success = ["Re-linked 'rig-r15-c16-new' to Azure subnet 'rig-r15-c16-new4'."]
error   = ["You must type 'approved' to confirm deletion."]
```

A green success banner and a red failure banner render together on the delete-confirmation page for a subnet that was never re-linked, as the response to a single click that did nothing but fail.

**Control that the mechanism is read-clears and not something else:** in the same harness the bulk import's own banner rendered on the first `GET /Subnet` and was gone on the second. (An earlier attempt was invalidated by the harness scraping its antiforgery token from a page that renders `_TempDataAlerts`, consuming the entry — itself a clean positive control.)

**Severity corrected low → info**, on consequence: the message is **true**, names an operation the operator really performed, clears on first read, misreports no allocation, and neither enables nor blocks anything. On `/Subnet/Delete/{id}` it sits above a form that still demands a typed `approved`, and its text names a re-link, not a delete. It also cannot mask a later message — `SuccessMessage` is a single key, so a subsequent genuine success overwrites it rather than queueing behind it.

This is **not** covered by round 11's standing kill of the same shape at `SubnetController.Azure.cs:490`. That one died on browser-unreachability — *"No browser reaches the JSON branch through the app"* — so the stray entry sat in an API script's cookie jar. Here the opposite was measured: a headless Chromium session, `Set-Cookie` on the re-link XHR, banner rendered in that same session five loads later. It is likewise not round 11's cross-tab watch-list property, which is structural to `CookieTempDataProvider` and unfixable per site; this is site-local, because ~30 other writers redirect to a view that consumes the entry on the very next request.

### Fix

Do not set `TempData` from an endpoint that answers AJAX with no navigation. Delete `:371-372` — the client already gives correct feedback by re-scanning, and the action is logged at `:367-369`. If a banner is wanted, return the message in the JSON body and render it inline next to the review table, the way both other wizards render their own commit outcomes.

**Cheaper interim:** keep the `TempData` write but return a `redirectUrl` and have the client navigate, matching `BulkDeleteStaleAzureSubnets` — at the cost of throwing away the reconcile results the operator is still working through, which is why the inline message is the better fix.

> **The verifier judged the primary fix sound and complete, and confirmed nothing depends on the write** — `grep -n TempData test/Bastet.Tests/Azure/SubnetControllerRelinkAzureSubnetTests.cs` returns no hits, and no other code reads `TempData["SuccessMessage"]` expecting a re-link message. Two notes. If the inline-message variant is taken, the string interpolates `target.Name` (operator-authored) and `target.SuggestedAzureSubnetName` (ARM-derived), so it must be inserted with jQuery `.text()` the way `showScanError` does, never `.html()`. And the offered interim, while functional, **degrades the feature**: it discards the reconcile results the operator is still working through and defeats the deliberate re-scan-after-re-link design the client comments at `:359-362` and `:385-387` state (*"the repair can change other rows' verdicts too, and a stale table is what makes an archive click wrong"*).
>
> One factual overstatement in the finding's evidence, corrected: *"the only writer whose response neither re-renders a `_TempDataAlerts` view nor carries a `redirectUrl`"* is wrong — `SubnetController.Azure.cs:490` is a second such writer. It is the already-adjudicated, browser-unreachable one, which is exactly what distinguishes this site rather than what undermines it.

---

# Refuted — reported by a finder, killed by the verifier

Three candidates died in verification. This section exists so round 16 does not spend agents re-deriving them. In each case the *observation* was reproduced; what failed was the **harm**.

| id | Title | file:line | Why it was killed |
|---|---|---|---|
| `C7` `[x1]` | `UseDeveloperExceptionPage` is registered above the security-header middleware, so every unhandled-exception 500 ships with none of the four security headers and none of the global no-store cache directives — on a page that renders the request's cookies and a stack trace | `src/Bastet/Program.cs:542` | **A re-raise whose one new leg is false when measured.** `docs/AUDIT-FINDINGS-10.md:532` already refuted the same finding on the same middleware and the same two mechanisms. The only addition is the `Cache-Control`/no-store claim, and it was killed with a real apparatus: two Chromium processes on one on-disk profile gave the 500 `transferSize` 57038 then 50347 (both network) while a positive control (`/css/site.css`, a 200 that also carries no explicit `Cache-Control`) gave 1225 then 0 with zero server hits. RFC 9111 §3 only permits storing a directive-less response whose status is heuristically cacheable, and RFC 9110 §15.1 does not list 500 — there is no caching for the missing directive to prevent. The other four headers protect nothing on this response: a cross-origin frame of the 500 succeeded (so the absence **is** effective) but `contentDocument` was null, and the body has **0 forms, 0 anchors, 0 `href=`, 0 `src=`** and four buttons whose only handlers are local toggles — clickjacking needs a control whose activation changes state, and there is none; `Content-Type` is explicitly `text/html; charset=utf-8`, so `nosniff` has nothing to prevent; the page originates no request, so `Referrer-Policy` has no `Referer` to withhold. `Program.cs:540` gates the page on exactly the predicate that registers `DevAuthHandler`, so there is no deployment where this 500 exists and authentication does not — everything it shows is already served to any anonymous requester, and the Cookies tab renders the requester's **own** cookies. The Production leg was measured nil: `307`, `Content-Length: 0`, `Location` only. Round 4 already priced this page class and **explicitly considered and rejected the very fix re-proposed here** (`AUDIT-FINDINGS-4.md:222-223`: *"OnStarting would also have worked and was not used — it is more machinery than an ordering change needs"*). Reproducing the render, not the harm. **Both proposed fixes were also defective and were built to prove it:** the "one line, no semantic change" interim is a measured **no-op** (the developer exception page calls `Response.Clear()` and never re-enters the pipeline, so no ordering change of any kind can fix that response), and the `OnStarting` main fix restored the four security headers but **not** `Cache-Control`/`Pragma` — the one leg the finding was built around — because those come from the MVC `ResponseCacheAttribute` filter it does not touch. What remains is comment accuracy: `Program.cs:519` says "on every response" and one response class does not get them. |
| `C10` `[x1]` | Inbound reconcile items assert *"BASTET is reporting that range as free space"* about Azure address spaces BASTET has never imported and makes no claim about, producing permanently unclearable false items alongside the real ones | `src/Bastet/Services/Azure/AzureReconciler.cs:424-430`, `:462-463` | **Both load-bearing words are wrong when measured, and the finder never reproduced it** (`reproduced: no-could-not`). *"False"*: the item's primary clause (*"which no BASTET subnet records"*) is true, and the attacked clause is true in every state that emits the item — proved by asking the application rather than reading a table: `POST /Subnet/Create` for `172.16.2.0/24` returned **302 and persisted the row**, so BASTET handed out, with zero friction, exactly the range Azure has assigned. In the second emitting state (a VNet address space imported as a VNet-level target, which `AccountsFor:495` deliberately refuses to count) the Details free-space table literally prints the range with a Create Subnet button. The argument depends on redefining "free space" as "listed in the per-subnet Unallocated IP Ranges table" — a table that by construction lists only ranges inside one subnet, so a range no subnet contains cannot appear there; that absence is the table's scope, not a false statement. *"Permanently unclearable"*: 3 items → 2 → 0 under two ordinary `/Subnet/Create` posts, no Azure import, `AzureResourceId` NULL in both rows — which is the behaviour the cited method's remarks at `:398-400` say it was written to have. The finder exercised only the bulk-import route and generalised from it. **The proposed fix is recorded here because it would introduce a real defect if applied later:** scoping the inbound walk to VNet prefixes that have a BASTET target suppresses the item for exactly the population with the least BASTET coverage and the highest chance of a silent collision — applied to the rig state it would have deleted all three warnings while Azure really owned the ranges and BASTET really would have handed them out — and it filters the imported set to Azure-linked rows, contradicting the *"every subnet, not just linked ones"* invariant the method documents at `:398-400`, which measurement showed is load-bearing. |
| `C15` `[x1]` | The Re-link button's label is never restored after a failed re-link: it stays *"Re-linking…"* and enabled, so the only control that writes `Subnet.AzureResourceId` no longer names the Azure subnet it will link to | `_ReconcileScripts.cshtml:400-405` (label overwritten at `:382`) | **The mechanism reproduces; the stated wrong output does not.** Limb (b) — "has lost the one sentence on screen that states which Azure resource the click will write" — is false **structurally**, not by fixture accident: `AzureReconciler.cs:181-186` builds the `Reason` string and `SuggestedAzureSubnetName` from the same `stillAllocated.SubnetName` in one construction, a full-tree grep confirms that block is the only writer of `SuggestedAzureResourceId` anywhere in `src/`, and the view renders a Re-link button only where that field is set. So the target is named in the **adjacent cell of the same row** for every possible input, plus in the warnings panel — both measured, both untouched by `showScanError`. Limb (a) never appears alone: both failure branches call `showScanError`, and `#rec-scan-error` lives outside `#rec-scan-content`, so the red *"Nothing was changed. Re-run the scan"* panel is on the same paint. The finder's own escalation was then driven, which is what finishes it: clicking the stale *"Re-linking…"* button posted the unchanged `subnetId`, returned 200, wrote `AzureResourceId` to exactly the target the Reason cell still named, and the follow-on scan repainted the screen as "nothing to clean up" — correct row, correct target, correct DB, correct post-state. And restoring the label would not make the button name its target any more truthfully: the label comes from the scan-time `suggestedAzureSubnetName` while the server re-derives at click time, so a restored label is equally stale and **more confidently wrong**. What remains is a cosmetic false progress string on a control that keeps working, on a screen that states the truth three times. (The label residue is real and is carried as correction (c) of **O10**, where it belongs — six lines in the same handler.) The proposed fix was also wrong as literally worded: `const original = btn.text();` inside `beforeSend` is block-scoped to that callback, so `btn.text(original)` in `complete` throws a `ReferenceError` and the fix silently no-ops; and the offered "cheaper" interim — restoring in the two failure branches only — is **worse** than the main fix, duplicating the restore at two sites, which is exactly the drift `:80-84` was written to prevent. |

---

# Watch list

Not findings. Only items a verifier could not **settle** — thin evidence, unproven reachability, or patterns worth grepping next round. Nothing reproduced is parked here; every reproduced defect above is filed at the severity its consequence warrants, regardless of fix cost.

- **Round 14's watch-list entry *"the natural trigger for N5 was never produced"* is now partly answered, and the answer widens the class.** O7's second verifier stranded the lock with **no proxy and no fault injection at all** — ordinary lock contention plus a `SIGSTOP`-class process pause over a direct TCP connection. What is still unmeasured is the original hypothesis: whether the acked-command-timeout family on real Azure SQL (10928 / 10929 / 40501) produces the same shape without a process pause. Anyone running BASTET on Azure SQL is the population where that becomes measurable.
- **`Subnet.AzureResourceId` still has no editor and `DeletedSubnets` still has no restore.** Round 14 recorded the first and round 13 the second. This round they became load-bearing for three criticals: they are why a row routed to `ReviewItems` is stranded (O1), why a corrupted row cannot be repaired (O3), and why every one of these archives is terminal (O1, O2, O3). What is **not** settled is whether any other flow silently depends on that column being immutable, and whether a one-off admin repair action would have any other consumer.
- **The inbound direction now exists, and its accounting rule has produced two findings out of two exemptions.** `AccountsFor` has exactly two arms — equality and containment — and each carries one exemption. Both exemptions turned out to be wrong for a reachable input: the containment exemption (O4) and the equality exemption (O5). Nobody swept for a **third** class of row that could satisfy `AccountsFor` without actually recording the range. That sweep is one afternoon and is the highest-yield thing round 16 could do on this surface.
- **Rules that exist in two copies keep producing findings, and nobody has enumerated them.** Four findings this round are one side of a duplicated predicate updated on the other side only: the encompassing rule in the wizard endpoint vs. `AnnotateSubnet` (O6), the import-eligibility gate in `AzureController.Import` vs. `ViewBag.CanImportFromAzure` (O9), the containment rules in `ValidateSubnetCreation` vs. the planner (O11), and the fully-encompassing-on-populated refusal in `BuildPlanItem` vs. the annotation (O12). Round 14's `N4` touched three of the four. **Grep next round for every predicate that appears in both a planner/annotation and a controller/commit path** — the availability enum's own contract at `AzureBulkImportViewModels.cs:6-10` names this failure mode explicitly, which makes each instance a self-documented violation rather than a discovery.
- **`showScanError` is written for one caller and reused by another; nobody checked for a third.** O10 is the reconcile wizard's instance. The three commit-shaped wizards in this app each have their own error surface and their own reveal/hide invariants, and only the reconcile one was driven for cross-caller reuse this round.
- **Whether any real BASTET deployment is co-hosted is unknowable from here, and it is O13's entire precondition.** The cookie behaviour is fully measured; what is not settled is the population. A single sentence in the deployment docs saying "Bastet should own its hostname" would close it more cheaply than code.
- **O14's Production error rendering was read, not driven.** The rig cannot run Production (it demands OIDC), so `UseExceptionHandler("/Error")`'s response to the unhandled `NullReferenceException` is inferred. The defect is the unhandled exception, not which page renders it, and the response is non-JSON either way — but the exact Production body is unverified.
- **Index hygiene, carried forward unchanged from round 14 because it is about to matter again.** Overlapping RFC1918 space across VNets in one subscription is normal; the rig itself ships `10.10.0.0/16` and `10.10.0.0/20` with a duplicated `10.10.1.0/24`. Any new prefix-keyed index — and O1's and O2's fixes both add one — that assumes one owner per prefix string will throw and turn a scan into *"The reconcile scan failed."* `AzureReconciler.cs` already avoids `ToDictionary` for this reason in two places.
- **`EditSubnetViewModel.Name` still has no `[SafeText]` while `CreateSubnetViewModel.Name` does**, and no path that round-trips a name through Edit was swept this round either. Carried from round 14 unexamined.
