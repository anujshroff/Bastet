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

## O3 — The Re-link repair `N1` added writes an Azure **subnet** resource id onto a VNet-level import target; the reconciler then reclassifies that target from review-only to deletable, and its VNet can never be imported again `[x1]` — FIXED

_O3 is fixed and committed. The Re-link suggestion is now gated on the row's own link being an Azure **subnet**: `canRelink = stillAllocated.Exact && AzureResourceIdentity.IsAzureSubnet(snapshot.AzureResourceId)`. A VNet-level row still becomes `RangeStillAllocatedInAzure` and is still withheld from deletion — it simply carries no suggestion, so the view renders no button and `RelinkAzureSubnet` cannot match it._

_**The finder's proposed fix was a regression and was not used.** It added `if (!IsAzureSubnet(...)) return null;` inside `FindLiveOwnerOfRange`, which makes `stillAllocated` null, skips the `RangeStillAllocatedInAzure` block entirely, and drops the row through to `plan.Items` as a **deletable** `VNetPrefixRemoved` while the range is still assigned — `N1`'s own defect reintroduced on the one path that removes data, with 847/847 still green. The verifier built that and measured it. The corrected form gates the **suggestion** rather than the lookup, and is smaller than what was filed._

_**The reason text was corrected with it.** The shipped sentence ends "Re-link it to that Azure subnet."; with no button that is an instruction to click something that is not there. A VNet-level row now gets its own closing sentence naming why re-link is not offered and what to do instead. `AVNetLevelRowWithNoRelink_DoesNotTellTheOperatorToRelink` pins it._

_**Fix (b) — a second guard inside `RelinkAzureSubnet` — was deliberately not shipped, and the verifier's own measurement is why.** The endpoint selects its target from `plan.ReviewItems` requiring `Status == RangeStillAllocatedInAzure` **and** a non-empty `SuggestedAzureResourceId` (`SubnetController.AzureReconcile.cs:310-313`). With the suggestion suppressed, a VNet-level row can never match, and the endpoint already returns 409. Adding an unreachable guard on a path that cannot be entered is precisely the residue these rounds keep finding, so the reachability is recorded here instead: if a later edit ever keys the button or the lookup off the status rather than the suggestion, this gate is the thing that must be re-checked._

_Proven by live A/B on the finding's own fixture. `rig-o3-vnet` created with address space `10.98.0.0/24` and one subnet `rig-o3-whole` covering the whole prefix, imported on both builds as a VNet-level target (`fullyAllocatedTargets: 1`), then the address space widened to `10.98.0.0/23` — one ordinary ARM update. Unfixed HEAD: `REVIEW 10.98.0.0/24 RangeStillAllocatedInAzure isVNetLevel=true`, suggestion = `…/virtualNetworks/rig-o3-vnet/subnets/rig-o3-whole` — **a subnet id offered as the repair for a VNet link** — and the Re-link button renders. Fixed: no suggestion, no button._

_**One thing the A/B also settled, and it changes how reachable this was.** On the fixed build that same row now reads `VNetPrefixStillCovered`, not `RangeStillAllocatedInAzure` — O2's fix catches it one step earlier, because the widened `/23` overlaps the recorded `/24`. Since ARM will not let a subnet hold a prefix outside its VNet's address space, **every ARM-reachable route into the VNet-level branch of `FindLiveOwnerOfRange` is now closed by O2**, and this gate is what keeps it closed for rows that arrive by other means — a `VNetDeleted` row, or an `AzureResourceId` written by the Admin API, which is free text. The unit tests exercise the gate directly rather than relying on that route._

_Not done, deliberately, and this was the owner's call: nothing repairs rows whose `AzureResourceId` this defect already corrupted. `Subnet.AzureResourceId` has no editor anywhere in the application and `DeletedSubnets` has no restore, so a corrupted row is repaired by deleting and re-importing it, and a row already archived cannot be repaired at all. Both remain on the watch list. The owner chose the code fix alone over adding an admin repair action, which would have been new feature work beyond this finding._

_Tests: 861 → 863. Two in `AzureReconcilerRangeStillAllocatedTests`, both of which fail against the ungated build — one asserting a VNet-level row carries neither `SuggestedAzureResourceId` nor `SuggestedAzureSubnetName`, one asserting its reason no longer tells the operator to re-link._

---

# High

## O4 — The reconciler's inbound check accepts any **containing** BASTET subnet as accounting for an Azure range, so an aggregate parent above the import target silences every `AzureRangeNotImported` report `[x2]` — FIXED

_O4 is fixed and committed. `AccountsFor`'s containment arm now accepts a containing BASTET row as evidence only when that row is itself **inside** the VNet address prefix the Azure range belongs to. A row that contains the VNet prefix is an ancestor of the import target, not an allocation record, and counting it made the whole inbound direction vacuous for everything beneath it._

_**The fix as filed did not exist and could not be applied.** Its sketch used `vnetNetwork, vnetCidr` "already in scope at `:433`"; `:433` iterates the **subnet's** prefixes, and a VNet has a list of address prefixes — the rig's own `rig-r15-vnet-a2-multi` ships three. A new `VNetPrefixContaining(vnet, network, cidr)` selects, per Azure range, the VNet prefix that actually holds it, and `AccountsFor` takes it as a fourth parameter. **The "cheaper interim, one line and no signature change" the finding offered therefore does not exist** — it needs the same lookup — and nothing of that shape was shipped. `AMultiPrefixVNet_ScopesTheContainmentTestToThePrefixHoldingTheRange` pins that the containing prefix is chosen rather than the first one taken._

_**Both edges the verifier said must be decided were decided by the owner, and both are pinned by tests rather than left to fall out of a helper.** A BASTET row **exactly** the size of the VNet prefix does not account for ranges inside it — `IsSubnetContainedInParent` returns false when `childCidr <= parentCidr`, so the strictness now expresses a decision instead of an accident (`ARowExactlyTheSizeOfTheVNetPrefix_DoesNotAccountForRangesInsideIt`). And when **no** VNet prefix contains the range, the check **fails open**: it falls back to the plain containment test rather than reporting. ARM normally forbids a subnet outside its VNet's address space, but the reconciler also assembles inventory under partial RBAC visibility, and reporting every such range would produce items nobody can clear (`WhenNoVNetPrefixContainsTheRange_AContainingRowStillAccountsForIt`)._

_Proven by live A/B on fresh catalogs, both builds against the same Azure state, everything driven through the real antiforgery-tokened endpoints. Fixture: `rig-o4-vnet` `10.20.0.0/16` holding one Azure subnet `rig-o4-unrecorded` `10.20.20.0/24` that is deliberately **not** imported. On each build, an operator-shaped setup: `POST /Subnet/Create` for a hand-made `root10` `10.0.0.0/8` (HTTP 200 on both), then the VNet address prefix imported as a target. Unfixed HEAD: **0** inbound items, and the reconcile screen's green *"Everything imported from this subscription still exists in Azure. There is nothing to clean up."* banner would render — while Azure owns a `/24` inside the target. Fixed, same Azure state and the same two rows: **1** inbound item, `"Azure subnet 'rig-o4-unrecorded' … owns 10.20.20.0/24, which no BASTET subnet records. BASTET is reporting that range as free space."`, and the green banner does not render._

_The deliberate-behaviour control from the finding was kept and is now a unit test rather than a one-off measurement: a hand reserve created **inside** the VNet prefix, which also contains the range, still suppresses the report on both builds. That is the `N3` case the method's own remarks defend; only the ancestor case changed._

_No backfill is possible or needed — the inbound verdict is recomputed on every scan, so installs simply start seeing reports they should always have been getting. The consequence is that an install with a top-down aggregate above its Azure targets may see a batch of inbound items on its first scan after upgrading. Every one of them is true._

_Tests: 863 → 868. Five in `AzureReconcilerInboundTests`, sitting next to `ATargetContainingTheRangeIsNotEnough_OrTheCheckWouldBeVacuous` as the finding asked: three fail against the unfixed reconciler (the ancestor case, the equal-size edge, and the multi-prefix scoping) and two are counter-tests for the decided edges._

---

## O5 — The reconciler counts an Azure-linked import target as the record of its own range even when it is **not** marked fully allocated, so an Azure subnet owning a whole VNet prefix is never reported inbound `[x1]` — FIXED

_O5 is fixed and committed. `AccountsFor`'s equality arm now honours the premise its own remark rests on: an Azure subnet covering a whole VNet prefix is recorded by marking that target **fully allocated**, so the target is the record of that range only once that has actually happened. The arm returns `!IsAzureVNet(existing.AzureResourceId) || existing.IsFullyAllocated`. `ExistingSubnetSnapshot.IsFullyAllocated` was already populated and simply unread._

_**The pinning test was corrected, not worked around.** `AnAzureSubnetCoveringTheWholeVNetPrefix_IsAccountedForByTheTargetItself` built its target without setting `IsFullyAllocated`, so it pinned the silence in precisely the state where its own docstring's justification does not hold — which is why the defect survived. Its fixture now sets the flag, and the docstring says why. That is a test-strength correction and is recorded as one._

_**The owner asked for the remedy to be in the item's own `Reason`**, and it is, in two forms because the two states call for different actions. When the unrecorded range is exactly a VNet-level target's own prefix and that target has no children: *"It covers the whole of BASTET subnet 'X'. Import it to mark that subnet fully allocated."* When the target already has children — the state this defect produces, because the operator allocated from the false free space — the top-up import refuses outright, so the item says so instead of sending them to a wizard that will reject them: *"…Importing it would mark that subnet fully allocated, which is refused while it still has child subnets, so remove the children that conflict with it first."* The verifier established that this item is **true** and unclearable in that state, so naming the remedy is the whole of what was missing._

_Proven by live A/B, both builds against the same Azure fixture `rig-o5-vnet` `10.61.0.0/24` whose single Azure subnet `rig-o5-whole` covers the entire prefix. Imported through the bulk wizard's **default** selection — VNet prefix ticked, no subnets ticked, which is what `buildSelectionFromUI` emits — giving `createdTargets: 1, fullyAllocatedTargets: 0` on both. Unfixed HEAD: **0** inbound items and the green *"nothing to clean up"* banner, for a `/24` Azure has assigned in full. Fixed, same state: **1** inbound item naming the range and the remedy, banner suppressed._

_The harm was then driven to completion on both: `POST /Subnet/Create {prod-db-tier, 10.61.0.0/25, ParentSubnetId: 1}` returned **200** and persisted on each build — BASTET accepts an allocation inside a range Azure owns entirely. The difference is what reconcile says afterwards: HEAD still reports nothing at all, while the fixed build reports the range **and** switches to the populated-target wording, because the target now has a child. That switch is the owner's requested behaviour, measured rather than asserted._

_The finding's claim that no cheaper interim exists that is also correct was checked and stands: relying on O6's wizard fix does not cover a VNet imported before Azure created the covering subnet, and widening the Details-page note is advisory copy over an answer that is still wrong._

_Tests: 868 → 871. Three new in `AzureReconcilerInboundTests`, all failing against the unfixed reconciler — the linked-but-not-fully-allocated target, the remedy sentence, and the populated-target variant of it — plus the corrected fixture on the existing pinning test._

---

## O6 — Round 14's "already recorded" filter in the single-VNet wizard deletes the fully-encompassing Azure subnet row, so that import path is unreachable and BASTET reports an Azure-allocated range as free with nothing detecting it `[x2]` — FIXED

_O6 is fixed and committed. `AzureController.GetSubnets` exempts the fully-encompassing row from the already-recorded filter — `a.FullyEncompassesVNetPrefix || !alreadyRecorded.Contains(...)` — exactly as `AzureBulkImportPlanner.AnnotateSubnet` already did by short-circuiting the encompassing case before its exact-match test. One line; one copy of a duplicated rule catching up with the other._

_The premise the fix rests on was checked rather than assumed: `BastetDbContext` declares `HasIndex(s => new { s.NetworkAddress, s.Cidr }).IsUnique()`, and `GetCompatibleVNets` only offers a VNet whose address prefix equals the target's, so the target's own row is the only row that can ever carry the encompassing prefix. The exemption therefore cannot re-admit anything else._

_It degrades safely if the target is later populated: `BatchCreateChildSubnets` already refuses an encompassing entry alongside siblings, and `ValidateSubnetCanBeFullyAllocated` (`Services/Validation/HostIpValidationService.cs:253-258` — the finding's path for this was wrong) already refuses a parent with children, so a top-up that re-offers the row fails with a specific message and a rollback rather than writing anything wrong._

_Proven by live A/B, both builds against the same Azure fixture `rig-o6-vnet` `10.171.0.0/24` whose one subnet `rig-o6-snetfull` occupies the whole prefix, with a BASTET target created by an ordinary `POST /Subnet/Create`. Unfixed HEAD: `GET /Azure/GetSubnets` returned `subnets: []` and `"No compatible subnets found in this VNet"` — a false statement about a VNet ARM reports as holding exactly one, and a dead end with no checkboxes and no Import button. Fixed, same VNet and same target: the row is offered, carrying `fullyEncompassesVNetPrefix: true`._

_The downstream branch was then driven to completion on the fixed build to prove it is reachable again, not merely listed: `POST /Subnet/BatchCreateChildSubnets` with `FullyEncompassesVNetPrefix=true` returned 200, and the row became `IsFullyAllocated=1` with `AzureResourceId` stamped with the VNet id. `GET /Subnet/Details/3` afterwards contains neither "Unallocated IP Ranges" nor "254 IP addresses" — the range Azure has assigned in full is no longer advertised as free space. On HEAD none of that is reachable from this wizard._

_**The shared-helper form the verifier recommended was not shipped, and this is a recommendation to the owner rather than a decision taken.** Extracting "a range BASTET already records, unless it is the target itself" into one helper called by both `GetSubnets` and `AnnotateSubnet` would also close the residue the verifier named — a **non**-encompassing Azure subnet whose prefix collides with an unrelated BASTET subnet elsewhere in the tree is still silently dropped from this list, where the bulk planner shows the same collision as `Blocked` with a reason. Closing that means this endpoint returning a status and reason per row and the wizard rendering them, which is a change to the endpoint's contract and the wizard's markup, beyond what this finding reproduced. It is the fourth instance this round of a rule living in two copies, and it is on the watch list as such._

_Two corrections to the finding, both recorded because they change what the reader should conclude. The headline's "the whole mark-fully-allocated import path is unreachable" is wrong as written — only the single-VNet wizard's copy was; the bulk wizard reached a completed commit on the HEAD build against the same state. And "with nothing detecting it" is true of the reconciler, the Details page and this wizard, but the bulk wizard both names the condition and can repair it. Nothing on the dead-end screen said so, which is why the severity stands._

_Tests: 871 → 872. One in `AzureControllerTests`, failing against the unfixed controller: a persisted target whose `{NetworkAddress, Cidr}` equals the Azure prefix, asserting the `FullyEncompassesVNetPrefix` row survives the filter **and** that a sibling child prefix already recorded in the tree is still dropped, so `N4`'s top-up filter is pinned alongside the exemption. Neither of `N4`'s `AzureController` hunks carried a test, which is why this slipped past._

---

# Medium

## O7 — `sp_getapplock` acquisition has no counterpart to the release path's `DiscardPooledConnection`: an exception escaping `AcquireAppLockAsync` returns the connection to the pool with the lock possibly held, and the app then lies about why every write fails `[x1]` — FIXED

_O7 is fixed and committed, and this is the one finding this round whose end-to-end stall was **not** re-reproduced live — that is stated plainly below rather than glossed._

_The acquire now has the same remedy the release already had. `AcquireAppLockAsync` is wrapped so that an exception escaping it logs at Error and calls `DiscardPooledConnection()` before rethrowing, destroying the physical connection rather than returning it to SqlClient's pool with the session still holding `Bastet:SubnetOperations`. `grep -rn DiscardPooledConnection src/` now returns three hits — the declaration and both call sites — where it returned two, and the asymmetry that was the whole defect is gone._

_It deliberately does **not** cover the `lockResult < 0` branch: there the lock was not granted, so nothing is stranded and flushing the pool would cost a reconnect on an ordinary contention timeout. The verifier checked that distinction and it is preserved._

_The `Bastet:Migration` twin needs nothing, for the reason the finding gave and the verifier confirmed: `getLock.ExecuteNonQuery()` at `Program.cs:429` has no enclosing try/catch, so an exception there terminates the process, and process exit ends the session regardless of pooling._

_**What was measured, and what was not.** The mechanism the fix rests on was measured on the rig's own SQL Server 2022: a `Session`-owned `sp_getapplock` returns 0 and `sys.dm_tran_locks` shows one `APPLICATION` row held by that session for as long as the session lives, dropping to zero only when the session ends. That is exactly why returning the connection to the pool - which keeps the session alive - strands the lock, and why destroying the connection is what clears it._

_**Not measured this round:** the end-to-end stall. A TDS proxy was built to withhold the server's reply to the `sp_getapplock` batch past the 60s command timeout, the unfixed build was run behind it on its own port and catalog, and the injection did not fire - the writes completed normally and the proxy never matched the batch on the wire. Rather than keep extending the apparatus, the fix is recorded as resting on the finding's own three reproductions (two independent apparatus, including one with **no** proxy and no fault injection at all — ordinary contention plus a `SIGSTOP`-class pause) and on the verifier's build-and-measure of this exact patch: identical 62s freeze, the new Error line firing, the physical connection destroyed 9ms after resume, `sys.dm_tran_locks` APPLICATION = 0 rows, and the next replica's write succeeding in 0.27s. Anyone re-checking this should start from the finding's proxy, not the one attempted here._

_Two things the verifier found in the fix's favour and worth keeping on the record: it also closes a second latent granted-then-throw path, because `return (int)parameters[4].Value;` sits outside the try/finally and an `InvalidCastException` there would today escape with the lock held; and the finder's own interim (log at Critical) is strictly weaker than the fix and was not shipped alongside it._

_Not done, deliberately and in scope: the failing request's **own** message still says "another subnet operation is in progress" when nothing is. Making that honest is a separate change at six controller catch sites, and it is not what this finding reproduced. What the fix stops is that message becoming true for every other replica for the next several minutes._

_Tests: 872 → 872, no change. The behaviour is a pooled-connection lifetime on a real SQL Server session; the suite runs SQLite, where `SqliteSubnetLockingService` is used instead, so there is no place in the existing infrastructure for a test that would fail against the unfixed code. That is why the measurement above is recorded as prose, per the standing rule for anything a permanent test cannot reach._

---

# Low

## O8 — The single-VNet top-up that `N4` newly admits silently overwrites the operator's own name on the target parent — the rename the same fix deliberately suppressed in the bulk planner `[x2]` — FIXED

_O8 is fixed and committed. `BatchCreateChildSubnetsCore` no longer renames a target that already holds subnets, and the success flash was changed with it._

_**The guard is the bulk rule verbatim, not the one the finding proposed.** Filed: `isTopUp = !string.IsNullOrEmpty(vnetResourceId) && parentSubnet.AzureResourceId == vnetResourceId`. Shipped: `bool targetIsPopulated = treeCache.Exists(s => s.ParentSubnetId == parentId);` — which is `!exact.HasChildSubnets` as `AzureBulkImportPlanner` already applies it. The verifier showed the two diverge in **both** directions, and both matter: a target linked to this VNet but holding no children **is** renamed by the bulk planner (`AnEmptyTargetIsStillRenamedWhenRequested` pins exactly that) and would not have been by the filed guard; and a populated target with **no** Azure link never reaches the wizard GET but does reach this commit, because `BatchCreateChildSubnetsCore` never re-checks the GET's precondition — there `isTopUp` is false, so the filed guard would still rename the one case the bulk planner hard-errors on. `treeCache` is already loaded one screen up and holds every subnet, so the correct rule costs nothing._

_**The offered interim was a no-op and was not shipped.** `parentSubnet` comes from `context.Subnets.FindAsync(parentId)` with no `Include`, and there is no lazy loading anywhere in `src/`, so `parentSubnet.ChildSubnets` is always empty and `!parentSubnet.ChildSubnets.Any()` is always true. The guard would have shipped and never fired. There was no cheaper interim here, only the fix._

_**The flash was incomplete in the finding and is fixed here.** Suppressing the write alone would leave `SubnetController.Azure.cs` announcing *"Successfully renamed parent subnet to 'X'"* for a rename it did not perform — the precise anti-pattern this same file already refuses a few lines above, where a transaction commits having written nothing while the message still claims a rename. The commit now records whether the name actually changed and says *"Successfully imported N child subnets."* when it did not. The encompassing branch needs no caveat: `ValidateSubnetCanBeFullyAllocated` refuses a parent with children, so it cannot fire on a top-up._

_Proven by live A/B, both builds against the same Azure fixture `rig-o8-vnet` `10.88.0.0/16` with three subnets. On each build: a target created by an ordinary `POST /Subnet/Create`, a first import through `POST /Subnet/BatchCreateChildSubnets` (which correctly renamed the empty target to the VNet name), the row then renamed to `Production Core`, and finally a **top-up** import of a third subnet posted to the same endpoint — the reachable route, since the commit path never re-checks the GET's gate. Unfixed HEAD: the parent's name came back **`rig-o8-vnet`** — the operator's label discarded. Fixed, identical inputs: **`Production Core`**, preserved, with the child still imported._

_The finding's own two corrections are carried: *"silently"* was wrong (the new name is stated on the very next screen), and the real defect is that the **old** value is never shown, is not recorded anywhere, and cannot be declined. Neither changes the severity._

_The verifier's note (d) is a test-strength item and is answered: `AzureBulkImportTopUpTests.Target` names its fixture so that `proposed == exact.Name` and `WillRename` is false with or without the bulk guard, which means reverting that guard leaves the suite green. The equivalent assertion now exists for the single-VNet commit, with a fixture whose name genuinely differs, so this path is pinned even though the bulk one still is not. Renaming that bulk fixture is a separate test-strength change and was left alone to stay in scope._

_Tests: 872 → 874. `BatchCreateChildSubnets_OnAPopulatedTarget_DoesNotRenameTheParent` fails against the unfixed controller (the name is overwritten) and also asserts the flash does not claim a rename; `BatchCreateChildSubnets_OnAnEmptyTarget_StillRenamesTheParent` is the counter-test that the guard is population rather than top-up, and passes on both builds._

---

## O9 — Round 14's top-up relaxation was applied to `AzureController.Import` but not to the Details page's duplicate of the same gate, so the single-VNet top-up wizard has no reachable link in the whole application `[x2]` — FIXED

_O9 is fixed and committed. `ViewBag.CanImportFromAzure` now computes the same predicate `AzureController.Import` enforces, including `N4`'s top-up allowance: `bool isTopUp = subnet.ChildSubnets.Count != 0 && !string.IsNullOrEmpty(subnet.AzureResourceId);` and `(subnet.ChildSubnets.Count == 0 || isTopUp)`. That expression is copied from the authority verbatim so the two read identically side by side._

_Proven by live A/B on a target in exactly the steady state the finding describes — `Production Core`, three imported children, Azure-linked, left behind by O8's own reproduction. Unfixed HEAD: `GET /Subnet/Details/1` contains **zero** links matching `Azure/Import`, while `GET /Azure/Import/1` on the same build returns **HTTP 200**. Fixed, same row: exactly one link, `/Azure/Import/1`, and the same 200 behind it. The two predicates were mutually exclusive by construction and now agree._

_Both forms the finding offered were set-equivalent to the authority on all three arms, and the fuller one was taken because it names `isTopUp` the way `AzureController.Import` does, which is what makes the duplication visible to the next reader. Details already loads `ChildSubnets`, `HostIpAssignments` and `AzureResourceId`, so neither form costs an extra query._

_The one hole worth probing — whether exposing the button opens a re-link to a **different** VNet — is already closed at `SubnetController.Azure.cs` (409 plus rollback when an existing Azure link points elsewhere), and `GetCompatibleVNets` requires an exact address-space match so the wizard cannot offer a second VNet. Nothing new is reachable that the server was not already accepting._

_**The consolidation the finding recommended was not done**, and this is a recommendation to the owner rather than a decision taken. This is the third copy of an Azure-import predicate in the tree — `AzureController.IsAzureImportEnabled()`, `Read.cs`'s own flag parse and `_Layout.cshtml` are three copies of the feature-flag parse, and the eligibility gate is now two. Lifting the eligibility test into one internal static helper both callers use is viable (`IsAzureImportEnabled` is already `internal static` and already called cross-controller) but it is a refactor beyond what this finding reproduced, and it is on the watch list as one of four duplicated-predicate findings this round._

_Two owner notes carried forward unchanged, both already disclosed by the verifier: the button label still reads *"Subnet Azure Import"* on a top-up rather than something that says "add the rest", and the gate remains duplicated until the consolidation above is taken._

_Tests: 874 → 879. A new `SubnetDetailsAzureImportGateTests` with five cases pinning the two predicates as set-equivalent on every arm — populated-and-Azure-linked (the defect; the only one that fails against the unfixed build), empty, populated-with-no-Azure-link, fully-allocated, and host-IPs-assigned. **No test referenced `CanImportFromAzure` at all before this**, which is why `N4` slipped past; that gap is now closed._

---

## O10 — A failed re-link paints the reconcile wizard's fail-closed *"Nothing was checked / Azure could not be read"* banner directly on top of a still-visible, still-tickable stale-subnet deletion table `[x1]` — FIXED

_O10 is fixed and committed. A failed re-link no longer routes through `showScanError`. It has its own surface: `showRelinkError(btn, originalLabel, message)` restores the button's label, renders the server's message in a new `#rec-relink-error` alert placed **outside** `#rec-scan-content`, and then calls `runScan()` so the operator lands on a current, internally consistent screen with the reason still on it._

_**The verifier's objection (a) is why the re-scan is part of the fix rather than optional.** The finding's rationale — "a failed re-link changed nothing, so the scan results remain exactly as valid as they were" — does not hold on the 409 at `SubnetController.AzureReconcile.cs:317-323`, which is precisely the branch where the server has re-derived the plan and found the displayed verdict **withdrawn**. Leaving `lastPlan` intact there would leave a review row asserting "the range is still assigned, re-link it" over a button that now 409s on every press, and would contradict the server's own instruction to re-run the scan. Re-scanning is what makes the new panel's closing sentence true._

_**(b) `:391` was dead and is gone in substance.** `RelinkAzureSubnet`'s only `Ok()` sets `success = true` and every failure branch is a 4xx/503, so with `dataType: "json"` only the error handler can run. The success-handler branch is kept as an explicit guard with a comment saying so, rather than deleted, because a future 200-with-`success:false` would otherwise fall through in silence._

_**(c) the button label is restored, and the finding's own proposed fix for it would not have worked.** `const original = btn.text()` inside `beforeSend` is block-scoped to that callback, so the restore would throw a `ReferenceError` and silently no-op — the refuted candidate `C15` recorded that. The capture is therefore in the click handler, outside the ajax options, and the comment says why._

_**(d) the two false sentences were moved out of the shared markup rather than left.** `_StepReview.cshtml`'s `#rec-scan-error` still says "Nothing was checked." and "Because Azure could not be read…", which is right for a failed **scan**; the new `#rec-relink-error` says only what actually happened. The cheap interim (hiding `#rec-scan-content` inside `showScanError`) was **not** shipped: it removes the contradiction but leaves both sentences asserting things that are false for this failure, and the verifier explicitly warned against that shape because the Re-link buttons live inside `#rec-scan-content` and a failed re-link must stay retryable._

_Proven by A/B in real headless Chromium, both builds driven end to end against live Azure. Fixture `rig-o10-vnet` `10.95.0.0/16` with subnet `rig-o10-a` `10.95.1.0/24`, imported; `a` then deleted and recreated as `b` with the same prefix, producing a review row and a **`Re-link to 'rig-o10-b'`** button on both builds. The verdict was then withdrawn underneath the open tab — `b` deleted in Azure while the wizard sat there, no second browser tab and no crafted post — and the button clicked, producing the documented 409._

_Unfixed HEAD: the red **"Nothing was checked."** panel visible `True`, the dedicated panel `False`, the step-2 pill **disabled under the operator** `True`, and **1** Re-link button left permanently reading *"Re-linking…"*. Fixed, same fixture and same withdrawal: scan-error panel `False`, dedicated re-link panel `True` carrying the server's own sentence *"This subnet is no longer reported as holding a range that moved to another Azure subnet. Nothing was changed."*, step-2 pill **not** disabled, and **0** buttons stuck. The stale table is on screen in the fixed run and that is now correct rather than contradictory: the automatic re-scan repopulated it, and the row really is stale once `b` is gone._

_Server messages reach the DOM through `.text()`, never `.html()` — the string interpolates an operator-authored subnet name and an ARM-derived Azure name._

_Tests: 879 → 879. This is wizard state-machine behaviour in a browser, which the xUnit suite has no infrastructure to reach; the measurement above is recorded as prose per the standing rule. The `#rec-relink-error` panel and `showRelinkError` are exercised by the browser run, not by a unit test._

---

## O11 — Bulk import preview reports `canCommit=true` and an Azure subnet as `Available` when an existing BASTET subnet lies inside that range; the commit then 400s and rolls back the whole multi-VNet import `[x1]` — FIXED

_O11 is fixed and committed. `DetectExistingBastetSubnetConflicts` now runs, for every planned **child**, the two containment tests `ValidateSubnetCreation` applies at commit: would this child contain an existing BASTET subnet, and does a more specific existing BASTET parent already contain it. The would-contain half is mirrored into `AnnotateSubnet`, so the selection screen greys the row with a reason instead of offering a run the preview then refuses._

_**Both tests are scoped to the item's own VNet prefix, and that scoping is the fix rather than a detail.** The verifier built the counter-example: with BASTET holding `10.30.0.0/8`, importing a new VNet prefix `10.30.0.0/16` carrying Azure subnet `10.30.1.0/24` **commits 200 today**, because at commit time the just-created `/16` is in the tree cache and becomes the child's parent. A check reading `existingSubnets` unscoped would see `bestParent` as the `/8` and refuse a plan that works — turning a preview/commit divergence into one in the other direction. `AnAncestorOutsideTheVNetPrefix_DoesNotRefuseAnImportThatCommitsToday` pins that, and it passes on both builds._

_**The auto-created-target leg the finding asked for was deliberately not added.** `DetectVNetPrefixWouldContainExistingSubnet` and its `AnnotatePrefix` mirror already cover it and both were confirmed working; adding it here would emit the identical global error twice. The new work is restricted to planned children._

_**The mirror is half a mirror, on purpose.** `AnnotateSubnet` was `private static` and became an instance method to reach `ipUtilityService`, as the verifier said it must. Only the would-contain half is mirrorable: the annotation pass runs per VNet with no prefix-to-target binding for a target that does not exist yet, so the more-specific-parent test has no well-defined answer at annotation time and is left to the plan. The selection UI is therefore strictly weaker than the preview — which is a large improvement on the previous silence rather than a complete answer, and the code says so where a later reader will find it._

_The interim the finding offered — populating `itemErrors` at the two commit sites — was **not** shipped. It is sound and the verifier checked it concretely, but it does not fix the false `canCommit`, and with the plan now refusing these selections the bare-sentence-with-an-empty-list response is no longer reachable through the wizard._

_Proven by live A/B on the same Azure fixture and the same database contents. `rig-o11-vnet` `10.44.0.0/16` with Azure subnet `rig-o11-multi` `10.44.2.0/24`; the precondition built through the ordinary `/Subnet/Create` form with zero Azure involvement — a hand-made `handmade-half` `10.44.2.0/25` inside the target. Unfixed HEAD: the selection screen offers `rig-o11-multi` as **`Available`, `isSelectable: true`, `reason: null`** — no badge, no colour, no reason line — and the preview returns **`globalErrors: []`**, while the commit 400s naming a BASTET subnet the operator never selected. Fixed, identical inputs: the selection screen shows **`Blocked`, `isSelectable: false`**, reason *"Would contain existing Bastet subnet 'handmade-half' (10.44.2.0/25), which would create an invalid hierarchy."*, and the preview's `globalErrors` carries the same fact naming both the Azure subnet and the BASTET one._

_One honest limit on that measurement: this fixture's preview returned `canCommit: false` on **both** builds, because the target is an exact match that already has a child and the prefix is refused for that separate reason. What the A/B therefore demonstrates is the silence — HEAD reporting no error at all about the conflict that kills the commit — rather than the `canCommit: true` the finding reproduced with its own fixture. The unit tests cover the `canCommit` half directly, on a plan with no other refusal in it._

_Two prose overstatements in the finding are corrected on the record: the selection UI did not show the row "green" — it carried no badge, no colour and no reason at all, i.e. **less** flagged than the finding said; and `DetectVNetPrefixWouldContainExistingSubnet`'s exact-match skip is correct, not part of the gap, because on an exact match the target already exists and legitimately contains its own children._

_Tests: 879 → 883. Three fail against the unfixed planner — the would-contain leg, the more-specific-parent leg, and the annotation mirror — and one is the scoping counter-test that passes on both._

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

## O15 — The reconcile wizard's *"Next: Confirm deletion"* re-arms on any checkbox tick after `lastPlan` was dropped, and is then a permanently live, silently inert button `[x1]` — FIXED

_O15 is fixed and committed. `updateGoConfirmBtn` gained the snapshot conjunct its sibling `refreshDeleteButton` was deliberately given: `$("#rec-go-confirm-btn").prop("disabled", !lastPlan || selectedIds().length === 0);`. That is now the single definition of "there is something to advance to", so it cannot drift from the `if (ids.length === 0 || !lastPlan) { return; }` guard in the click handler the way it had._

_The verifier's three additive notes were all checked against the file and hold. `updateGoConfirmBtn` is the **only** site that ever arms the button — the element ships `disabled` and `invalidateScan` only ever disables it — and its three callers are `renderPlan` (where `lastPlan` was just assigned and is always truthy), and the two checkbox handlers, so the conjunct cannot wrongly disable the button on the happy path and it closes **both** re-arm routes, including `#rec-select-all` which the finder omitted. `invalidateScan`'s own `prop("disabled", true)` becomes redundant and was kept as defence in depth, as the verifier recommended._

_**The alternative fix the verifier warned against was not taken:** making `showScanError` hide `#rec-scan-content` would have broken the retryability the Re-link buttons depend on, and those buttons live inside that container. O10 was fixed on its own terms instead._

_**What was measured, and what could not be.** O10's browser run is the same apparatus and was watched for this too. On the fixed build, after the re-link 409, ticking a stale row **does** arm "Next: Confirm deletion" — and that is correct, not a residual defect: `showRelinkError` re-scans, `renderPlan` reassigns `lastPlan`, and the button is advancing to a confirmation built from a plan that genuinely exists. Clicking it reaches step 3 normally._

_The state this finding is about — rows on screen with `lastPlan` already nulled — is **no longer reachable on the fixed build**, which the finding itself predicted: "fixing O10 removes the only route that currently reaches it, but the gate is the invariant and should not depend on which caller happens to clear `lastPlan`." Every remaining `showScanError` caller is a scan path, and `runScan`'s `beforeSend` hides `#rec-scan-content` before any of them can fire, so there is no way to leave tickable rows above a dropped plan. That is why this is shipped as an invariant rather than as a closure of a live route, and why the entry says so instead of claiming a reproduction it does not have._

_The finding's own severity reasoning stands unchanged: nothing is written, nothing archived, no range misreported, no destructive path opened — the `:419` guard already refused to build a confirmation from a dropped plan and `confirmedIds` stayed null throughout, so `#rec-confirm-delete-btn` was never armed. This is a correctness-of-state fix, not a data fix._

_Tests: 879 → 879. One expression in a Razor-hosted script; the xUnit suite has no infrastructure that reaches the wizard's state machine, and the browser evidence is recorded above as prose._

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
