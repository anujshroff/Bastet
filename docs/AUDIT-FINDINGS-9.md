# Bastet — Round-9 Audit Findings

| | |
|---|---|
| Round | **9** (finding letter **I** — findings are `I1` … `I8`) |
| Branch | `audit/round-9` |
| HEAD | `a8f669b` — *"Audit 8 Cleanup (#152)"*, identical tree to `main` |
| Build | `dotnet build --no-incremental` → **0 warnings, 0 errors** |
| Tests | **690 passed**, 0 failed, 0 skipped |
| Working tree | clean at start and at finish — this file is the round's only change |
| Date | 2026-07-29 |

Every line number below was re-derived against the working tree at `a8f669b` while writing this file.

---

## Verdict

**Eight findings: no critical, two high, two medium, four low, no info. Nothing was refuted at
verification.** All eight were reproduced on the live rig — real SQL Server 2022, real ARM, real
headless Chromium — and for all eight the proposed fix was *built and measured* in a copy outside the
repository rather than argued from source.

Read **I1** first. Azure reconcile's review-item cascade guard — the one round 8 shipped as H1 — lives
inside `ApplyConfirmations`, and `ApplyConfirmations` is only reached when the plan contains at least
one *absence*-status item. A plan made entirely of prefix **drift** (`VNetPrefixRemoved`,
`SubnetPrefixChanged`) takes an early return at `AzureController.cs:381` and the guard never runs at
all, so approving one drifted ancestor archives a descendant the same scan had just verified live in
Azure. The A/B is the part to look at: same two rows, same Azure state, and adding one *unrelated*
stale subnet elsewhere in the tree flips the server from `200 subnetsArchived:2` to `409 Conflict,
nothing deleted`. Safety is decided by whether some other subnet happens to be stale.

Then **I2**, the same harm through a different hole: a descendant whose `AzureResourceId` belongs to
another subscription is `continue`d past at `AzureReconciler.cs:77`, so it joins none of the protected
sets, is named nowhere in the plan, and is archived by its stale ancestor unexamined. Both I1 and I2
destroy rows unrecoverably — `DeletedSubnets` archives no `AzureResourceId` and there is no restore
path anywhere in the app.

**I3** is the write surface: one ordinary 40-subnet Azure import re-reads the whole `Subnets` table
twice per child while holding the single global write lock, so on a 200k-subnet deployment every other
write in the application is refused for 40 s with *"The operation timed out due to high concurrency"*
— and nothing was written. **I4** is the third instance of the round-8 H2/H3 shape, in the one wizard
round 8 did not touch: the reconcile wizard paints a superseded scan's stale-subnet table and its green
*"nothing to clean up"* banner on top of the current scan's *"Nothing was checked"* failure panel, and
the archive can be driven from that screen.

The four lows are ordered by consequence: **I5** purges more archive records than its own confirmation
page states; **I6** turns sign-out into an HTTP 500 that leaves the session alive; **I7** turns every
routine OIDC callback failure, including a declined consent prompt, into an unhandled exception and a
500; **I8** renders `Showing 51-40 of 40` over an empty table, and serves page 1's rows for a page
number large enough to overflow the `int` skip.

**Verifier corrections are the other thing worth reading.** Three severities were corrected, all
downward (I3 high → medium, I6 and I7 medium → low). One citation was re-anchored (I3). On five
findings a proposed fix or interim was built, measured, and found unsound, incomplete or backwards, and
replaced or dropped — including I1, where the finder's own proposed regression test **passes against
pristine HEAD** and its proposed interim is strictly *more* expensive than the real fix.

---

## How this audit ran

**Two passes per beat.** Every beat was worked by two independent finder agents that did not see each
other's output. Twenty finder agents ran; twenty returned. The beats represented in the surviving set
are `azure`, `security`, `ui`, `locking`, `regression`, `regtests` and `deadcode`.

**What the tags mean.**

- **`[x2]`** — both independent passes of the beat found it. Independent agreement is decent evidence
  that the code, not the reader, is the problem.
- **`[x1]`** — one pass found it and the other did not. Absence is weak evidence, so **every `[x1]`
  got a second verifier** on a reachability-and-consequence lens as well as a mechanism lens. That is
  why the `[x1]` findings below carry `2/2` verifier votes and the `[x2]` findings carry `1/1`.
  `[x1]` warrants **more** scrutiny during reconciliation, not less: this round's `[x1]` set contains
  the app-wide write outage (I3) and both purge/sign-out defects.

**Verification.** Every candidate went to a verifier whose brief was to *kill* it. A verifier ran its
own instance on its own port against its own SQL catalog, from an unmodified `a8f669b` tree exported
with `git archive` or `cp -a` — never from the repository working directory — and then built the
proposed fix in that copy and measured it (`dotnet build --no-incremental`, `dotnet test`, and the same
live request replayed against the patched build). All eight candidates survived unanimously.

**The funnel.**

| | |
|---|---|
| Finder agents launched / returned | 20 / 20 |
| Raw findings reported | 26 |
| Candidates after dedup, merge and brief screening | **8** — 4 `[x2]`, 4 `[x1]` |
| Survived verification | **8** |
| Refuted by a verifier | **0** |
| Reproduced live | **8** |
| Not runnable | **0** |
| Baseline | `a8f669b` on `main`, 690 tests |

The 18 raw findings that did not become candidates died at the **merge**, not at a verifier:
duplicates of each other, and re-files of things the round-9 brief lists as accepted, deliberately not
done, or refuted in rounds 5-8. That is why the refuted table below is empty — no candidate reached a
verifier and lost.

---

# Critical

None.

---

# High

## I1 [x2] — Reconcile's review-item cascade guard is skipped whenever the plan holds no absence-status item, so a prefix-drift target archives a descendant the same scan verified live in Azure

**file:line** — `src/Bastet/Controllers/AzureController.cs:381` (`if (absenceClaims.Length == 0) { return; }`).
The guard it skips is `src/Bastet/Services/Azure/AzureReconciler.cs:240-242`.

**Confidence: confirmed.**

**Corrected by the verifier:** the finder's fix parts 2 and 3 were **measured wrong and replaced** —
its proposed reconciler-level regression test *passes* against pristine HEAD, its `liveLinked`
widening covers only one of the two `ReviewItems` statuses, and its "cheaper interim" is strictly
*more* expensive than the real fix (it breaks an existing test's arrange). Severity and citation stand.

### Failure scenario

Round 8's H1 shipped two cascade guards. The second — over `notVisible ∪ unknown ∪ stillLive ∪
plan.ReviewItems` at `AzureReconciler.cs:240-242` — lives inside `ApplyConfirmations`, and
`ApplyConfirmations` has exactly one production caller: `AzureController.ConfirmProposedDeletionsAsync`,
which returns at `AzureController.cs:381-384` when no plan item carries an absence status.
`VNetPrefixRemoved` and `SubnetPrefixChanged` are not absence statuses, so **a plan whose items are all
drift never runs that guard at all.**

The first guard (`AzureReconciler.cs:124-126`) cannot cover the gap: `liveLinked` is populated only on
the `item is null` branch at `:103-111`, whereas `FullyAllocatingSubnetDeleted` is emitted at `:322`
*after* `EvaluateVNetLevel` has positively verified that both the VNet and the recorded prefix are
live, and is routed to `plan.ReviewItems` instead. A review-item descendant is therefore in **neither**
protected set on a drift-only plan.

Real inputs, all rows written by the shipped import path and all drift produced by ordinary Azure
operations. Two Azure VNets in `bastet-visible`: `rig-r9-vc4a-hub` (`10.96.0.0/15`, no subnets) and
`rig-r9-vc4a-fa` (`10.97.0.0/16`, one subnet `rig-r9-vc4a-fa-all` covering the whole prefix). Two
posts to `/Subnet/BulkCreateFromAzurePlan` produced Bastet id 8 `rig-r9-vc4a-hub` `10.96.0.0/15` and
id 9 `rig-r9-vc4a-fa` `10.97.0.0/16` auto-nested as its child with `IsFullyAllocated=1`. Then, in
Azure only: delete the covering subnet, and widen the hub's address space `10.96.0.0/15` →
`10.100.0.0/15`.

`POST /Azure/ReconcileScan` answers `canCommit:true`, `warnings:[]`, one item (`8`,
`VNetPrefixRemoved`, `descendantSubnetIds:[9]`) and one review item (`9`,
`FullyAllocatingSubnetDeleted`, *"Nothing needs deleting; review whether it should still be marked
fully allocated."*). `POST /Subnet/BulkDeleteStaleAzureSubnets {subnetIds:[8],confirmation:"approved"}`
answers **200** `{"targetsDeleted":1,"subnetsArchived":2}`. `Subnets` is then empty and
`DeletedSubnets` holds both rows — while `az network vnet show rig-r9-vc4a-fa` still returns
`prefixes:["10.97.0.0/16"], provisioningState Succeeded`. The row destroyed is one whose VNet and
exact prefix the same scan had just verified live, and `DeletedSubnets` carries no `AzureResourceId`
column and there is no restore path.

The decisive A/B: with one entirely unrelated absence-status row added elsewhere in the tree, the
identical pair scans as `warnings:["1 subnet(s) were withheld from deletion because archiving them
would also archive subnet(s) beneath them that were withheld from deletion: 'vc4a-outer'."]` and the
identical delete POST answers **409 Conflict**, nothing archived. Same tree, same Azure state, opposite
safety verdict, decided by whether some *other* subnet happens to be stale.

There is no second line of defence: `SubnetController.AzureReconcile.cs:74` calls the same
`ConfirmProposedDeletionsAsync` and takes the same early return. **Both** `plan.ReviewItems` statuses
are affected — the `UnrecognisedResourceId` variant reproduces identically at HEAD (`warnings:[]`,
`canCommit:true`, then 200 `subnetsArchived:2`).

In Chromium the operator gets no signal at all: `#rec-scan-warnings` stays `d-none` (warnings is
empty), the stale table offers the ancestor with only *"Also archives 1 child subnet(s)"*, the confirm
screen names only the ancestor (`"vc4a-inner" in body` → **False**), and the separate review table
asserts *"Nothing here can be fixed by deleting anything … BASTET will not change them
automatically"* about the very row about to be archived. The two tables are never cross-referenced.

### How it was reproduced

Own port 5303, own catalog `bastet_r9_vc4a`, app run from an unmodified `a8f669b` export with SP1
credentials and `AZURE_TOKEN_CREDENTIALS` unset (real `DefaultAzureCredential` → real ARM).

```
# organic leg - no hand-written SQL anywhere
az network vnet create -g bastet-visible -n rig-r9-vc4a-hub --address-prefixes 10.96.0.0/15
az network vnet create -g bastet-visible -n rig-r9-vc4a-fa  --address-prefixes 10.97.0.0/16 \
    --subnet-name rig-r9-vc4a-fa-all --subnet-prefixes 10.97.0.0/16
POST /Subnet/BulkCreateFromAzurePlan   (hub, no subnets)   -> {"success":true,"createdTargets":1,"fullyAllocatedTargets":0}
POST /Subnet/BulkCreateFromAzurePlan   (fa + its subnet)   -> {"success":true,"createdTargets":1,"fullyAllocatedTargets":1}
POST /Azure/ReconcileScan                                  -> canCommit False, items [], reviewItems [], warnings []
az network vnet subnet delete -g bastet-visible --vnet-name rig-r9-vc4a-fa -n rig-r9-vc4a-fa-all
az network vnet update       -g bastet-visible -n rig-r9-vc4a-hub --address-prefixes 10.100.0.0/15
POST /Azure/ReconcileScan
POST /Subnet/BulkDeleteStaleAzureSubnets {"subnetIds":[8],"confirmation":"approved", ...}
sqlcmd: SELECT COUNT(*) FROM dbo.Subnets; SELECT OriginalId,Name,OriginalParentId FROM dbo.DeletedSubnets
```

Observed, after the two Azure edits:

```
planner-written rows: 8|rig-r9-vc4a-hub|10.96.0.0|15|NULL|0    9|rig-r9-vc4a-fa|10.97.0.0|16|8|1
scan   -> canCommit True, warnings []
  ITEM   8 rig-r9-vc4a-hub VNetPrefixRemoved [9]  "VNet 'rig-r9-vc4a-hub' still exists but no longer
                                                   has the address prefix 10.96.0.0/15."
  REVIEW 9 rig-r9-vc4a-fa FullyAllocatingSubnetDeleted  "...no Azure subnet in VNet 'rig-r9-vc4a-fa'
                                                   covers 10.97.0.0/16 any more. Nothing needs deleting..."
delete -> HTTP/1.1 200 OK {"success":true,"targetsDeleted":1,"subnetsArchived":2,"hostIpsArchived":0}
SQL    -> Subnets 0 rows; DeletedSubnets 8 rig-r9-vc4a-hub, 9 rig-r9-vc4a-fa (OriginalParentId 8)
az network vnet show rig-r9-vc4a-fa -> prefixes ["10.97.0.0/16"], provisioningState Succeeded
```

A/B control (only difference: one unrelated `SubnetDeleted` row `172.31.7.0/24` in a separate root) →
`warnings ["1 subnet(s) were withheld … 'vc4a-outer'."]`, and the identical delete POST → **409
Conflict**, *"1 of the selected subnet(s) are no longer reported as deleted in Azure. Nothing was
deleted."*, `Subnets` untouched, `DeletedSubnets` 0.

Chromium (`requestAnimationFrame` deleted first): warnings panel `is_visible False`, class
`alert alert-warning d-none`; one offered checkbox; confirm count 1, cascade *"This also archives 1
child subnet(s) and 0 host IP assignment(s)"*; `"vc4a-inner" in body` → **False**; commit banner
*"Deleted 1 stale subnet(s), archiving 2 subnet(s)…"*. Live schema: `DeletedSubnets` has 14 columns
(`Id … ModifiedBy`) — no `AzureResourceId`, no `IsFullyAllocated`.

Fix measured on the patched build: scan → `canCommit False`, `warnings ["…withheld… 'vc4a-outer'."]`,
identical delete POST → **409** with both rows intact; same for the `UnrecognisedResourceId` variant;
the rig's own `seed-reconcile-fixture.sql` still yields its documented baseline (2 orphans proposed,
live control not proposed, `warnings []`).

### Fix

Make the guard unconditional by skipping only the ARM round trip. In
`AzureController.ConfirmProposedDeletionsAsync`, replace the early return at `:381-384`:

```csharp
// No absence claim means nothing to ask Azure about, so the ARM round trip is skipped - but
// ApplyConfirmations must still run: it also applies the cascade guard that protects review
// items, and those are independent of any confirmation.
IReadOnlyDictionary<string, AzureResourceConfirmation> confirmations =
    absenceClaims.Length == 0
        ? new Dictionary<string, AzureResourceConfirmation>(StringComparer.OrdinalIgnoreCase)
        : await azureService.ConfirmResourcesAsync(absenceClaims);

reconciler.ApplyConfirmations(plan, confirmations);
```

Measured sufficient on its own: 0 warnings / 0 errors, `dotnet test` **690/690 with no test edits at
all**, both `ReviewItems` statuses now withheld on both the scan and the delete path, and no ARM calls
added — so "a healthy scan costs nothing" is preserved. An empty map is safe because a non-absence item
takes the `!IsAbsenceStatus` → keep path at `AzureReconciler.cs:166-170`.

**Do not take the finder's part 2 as written.** "Add `FullyAllocatingSubnetDeleted` rows to
`liveLinked`" covers only one of the two `ReviewItems` statuses; the `UnrecognisedResourceId`
descendant was reproduced being archived identically, and that status is never verified live so it does
not belong in `liveLinked`. If service-level defence-in-depth is still wanted, add a **second,
separately-worded** call after `AzureReconciler.cs:124-126` rather than widening the `liveLinked` one:

```csharp
WithholdTargetsWhoseCascadeIsBlocked(
    plan, [.. plan.ReviewItems.Select(i => i.SubnetId)],
    "archiving them would also archive subnet(s) beneath them that need review rather than deletion");
```

— which keeps the existing warning honest, since `UnrecognisedResourceId` rows were never shown to
"still exist in Azure".

**Do not take the finder's part 3 as written.** The proposed reconciler-level regression test
(drift-only `plan.Items`, review-item descendant, `ApplyConfirmations` with no confirmations) was built
and run against pristine HEAD source: it **passes**, because `ApplyConfirmations` is itself correct. The
defect is at the seam, so the regression test must drive the **controller**.
`test/Bastet.Tests/Azure/SubnetControllerAzureReconcileTests.cs` already calls
`_controller.BulkDeleteStaleAzureSubnets(...)` directly (line 96) and is the right home; there is no
`InternalsVisibleTo` anywhere, so `ConfirmProposedDeletionsAsync` is not callable from a test and the
public action is the only route. Assert that a plan whose only item is `VNetPrefixRemoved` over a
review-item descendant is refused and that the descendant row survives.

**Interim: there is none cheaper.** The finder's proposed one-liner at `AzureReconciler.cs:124` —
passing `[.. liveLinked, .. plan.ReviewItems.Select(i => i.SubnetId)]` — was measured to fail
`AzureReconcilerTests.ApplyConfirmations_TargetWhoseDescendantIsAReviewItem_IsAlsoWithheld` at
`AzureReconcilerTests.cs:866` (`Assert.Single() Failure: The collection was empty`), because that
test's arrange asserts `Assert.Single(plan.Items)` *before* calling `ApplyConfirmations` and the guard
now fires earlier. It therefore costs a test edit the controller fix does not. The usual display-only
stopgap also does not apply: `plan.Warnings` is empty in this scenario, so adding the missing warnings
block to `_StepConfirm.cshtml` would render nothing.

---

## I2 [x2] — An Azure-linked descendant belonging to another subscription is skipped by `BuildPlan` and never joins the protected set, so a stale ancestor archives it unexamined and unmentioned

**file:line** — `src/Bastet/Services/Azure/AzureReconciler.cs:77`
(`if (!BelongsToSubscription(snapshot.AzureResourceId, subscriptionId)) { continue; }`, block `:77-80`).

**Confidence: confirmed.** No verifier correction — the proposed fix was built and measured, including
an over-withholding control.

### Failure scenario

`BuildPlan` `continue`s at `:77` for any snapshot whose `AzureResourceId` does not sit under the
scanned subscription ("out of scope, not stale"). Such a row is therefore not evaluated, not added to
`liveLinked` (`:109`), not an `Item`, and not a `ReviewItem` — so it appears in **none** of the sets
passed to `WithholdTargetsWhoseCascadeIsBlocked` (`:124` `liveLinked`; `:232-238` `notVisible ∪ unknown
∪ stillLive ∪ ReviewItems`). Its ancestor is still offered for deletion, and archiving the ancestor
archives it. Azure was never asked about it — which is exactly the `unknown` state the code
deliberately protects at `:189-196` ("an unanswered question is not a deletion"). Here it is not
protected, and the plan does not even mention the row.

Real inputs, live rig: Bastet row 1 `vc5a-parent-stale` `10.90.0.0/15` linked to a VNet that was never
created → `VNetDeleted`, offered. Child row 2 `vc5a-child-othersub` `10.90.1.0/24` linked to
`/subscriptions/11111111-2222-3333-4444-555555555555/resourceGroups/bastet-visible/providers/Microsoft.Network/virtualNetworks/rig-r9-vnet-visible/subnets/rig-r9-snet-web`
— a real, live Azure subnet under a second subscription GUID.

`POST /Azure/ReconcileScan` answers `canCommit:true`, `warnings:[]`, `reviewItems:[]`, one item
(`1`, `VNetDeleted`, `descendantSubnetIds:[2]`). Row 2 is named nowhere.
`POST /Subnet/BulkDeleteStaleAzureSubnets {subnetIds:[1]}` answers **200**
`{"targetsDeleted":1,"subnetsArchived":2}` and archives row 2, destroying its `AzureResourceId`
(`DeletedSubnets` has zero `%Azure%` columns) with no restore path.

This is not a hand-built tree shape. `BulkCreateFromAzurePlan` performs **no ARM read** — it re-plans
against the database and trusts the posted ids (`SubnetController.BulkAzure.cs:100-121`, `:265-288`) —
and `FindDeepestContainer` (`AzureBulkImportPlanner.cs:329-349`) parents purely on address containment
with no subscription test. Multi-subscription estates plus hub/supernet reservations are the ordinary
enterprise shape, and `GET /Azure/GetSubscriptions` feeds the wizard every subscription the credential
can see. A VNet moved between subscriptions after import (`az resource move` rewrites the subscription
GUID in the resource id) produces the same row.

### How it was reproduced

Own instance from unmodified `a8f669b` on port 5334, catalog `bastet_r9_vc5a2`, SP1 through the
production `DefaultAzureCredential` path (`AZURE_TOKEN_CREDENTIALS` unset, `BASTET_AZURE_IMPORT=true`).

```
# leg 1 - minimal contrast, two seeded rows
POST /Azure/ReconcileScan            (subscriptionId=f0e8d6db-..., subscriptionName=Main)
POST /Subnet/BulkDeleteStaleAzureSubnets {"subnetIds":[1],"confirmation":"approved", ...}
# control - ONE token changed, nothing else
UPDATE Subnets SET AzureResourceId=REPLACE(AzureResourceId,'11111111-2222-3333-4444-555555555555',
                                                            'f0e8d6db-...') WHERE Id=2;
# leg 2 - tree shape produced by the app's own write path
POST /Azure/BulkImportPreview   (foreign-subscription VNet prod-spoke-vnet 10.89.0.0/16 + child prod-app 10.89.1.0/24)
POST /Subnet/BulkCreateFromAzurePlan
POST /Azure/ReconcileScan ; POST /Subnet/BulkDeleteStaleAzureSubnets {"subnetIds":[3], ...}
SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('DeletedSubnets') AND name LIKE '%Azure%';
```

Observed:

```
leg 1 scan   -> canCommit True, globalErrors [], warnings [], reviewItems [],
                ITEM 1 vc5a-parent-stale VNetDeleted descIds=[2] hostIps=0     (row 2 named nowhere)
leg 1 delete -> HTTP 200 {"success":true,"targetsDeleted":1,"subnetsArchived":2,"hostIpsArchived":0}
                Subnets: no rows. DeletedSubnets: OriginalId 2 vc5a-child-othersub, OriginalId 1 vc5a-parent-stale
CONTROL      -> canCommit False, warnings ["1 subnet(s) were withheld from deletion because archiving
                them would also archive Azure-linked subnet(s) beneath them that still exist in Azure:
                'vc5a-parent-stale'."] ; delete -> HTTP 409, Subnets=2, DeletedSubnets=0
leg 2        -> BulkImportPreview canCommit True, targetType AutoCreateChild, parent hub-reservation, errors []
                BulkCreateFromAzurePlan HTTP 200 {"createdTargets":1,"createdChildSubnets":1}
                rows: 3 hub-reservation 10.88.0.0/13 (sub f0e8d6db) ; 4 prod-spoke-vnet 10.89.0.0/16 parent 3
                      (sub 11111111) ; 5 prod-app 10.89.1.0/24 parent 4 (sub 11111111)   - no warning, no check
                scan -> ITEM 3 hub-reservation VNetPrefixRemoved descIds=[4,5], warnings []
                delete {"subnetIds":[3]} -> HTTP 200 {"subnetsArchived":3}; DeletedSubnets holds 5, 4, 3
SCHEMA       -> 0   (DeletedSubnets carries no AzureResourceId column)
```

Fix leg: build 0 warnings / 0 errors, `dotnet test` **690/690**. Patched build on the identical leg-1
seed → `canCommit False`, `warnings ["1 subnet(s) were withheld from deletion because archiving them
would also archive Azure-linked subnet(s) beneath them that belong to a different subscription and were
not checked by this scan: 'vc5a-parent-stale'."]`, delete → **409**, both rows intact.
Over-withholding control on the patched build (live root, one live child, one genuinely-deleted child,
plus an unrelated standalone foreign-subscription row) → `canCommit True`, `warnings []`, exactly the
one genuinely-deleted orphan offered. The reconciler still does its job.

The only synthetic element is that the second subscription GUID names no real subscription — irrelevant
to the executed path, because `BelongsToSubscription` (`:395`) is a pure `StartsWith("/subscriptions/{id}/")`
test that returns false and `continue`s before any ARM interaction.

### Fix

In `BuildPlan`, collect the ids skipped at `:77` into a `notCovered` `HashSet<int>` and pass it to
`WithholdTargetsWhoseCascadeIsBlocked` alongside the existing `liveLinked` pass, with its own message —
*"archiving them would also archive Azure-linked subnet(s) beneath them that belong to a different
subscription and were not checked by this scan"*. Same shape as the two existing calls, so nothing new
is invented and the row is named in the warning.

**Interim** (cheaper, non-blocking, does not prevent the loss): when a plan item's
`DescendantSubnetIds` intersects the skipped set, add a `plan.Warnings` entry naming the descendant and
its subscription. The review screen renders `plan.warnings` already, so this removes the silence
without changing any verdict.

---

# Medium

## I3 [x1] — Azure/batch child-subnet import re-reads the whole `Subnets` table twice per created child while holding the global write lock, so one ordinary import refuses every other write in the app

**file:line** — `src/Bastet/Controllers/SubnetController.Helpers.cs:214`
(`List<Subnet> allSubnets = await context.Subnets.ToListAsync();`).
Called twice per child from `SubnetController.Azure.cs:284` (pre-flight) and `:393` (per child), inside
the lock taken at `:233`.

**Confidence: confirmed.** Votes 2/2.

**Corrected by the verifiers:** severity **high → medium** and the citation re-anchored to
`Helpers.cs:214` — ASP.NET Core's default `FormOptions.ValueCountLimit` caps one batch at **145
children**, which bounds the outage at ~150 s instead of unbounded and invalidates the finder's
200-children evidence rows; and the finder's hoist, while measured to work, is incomplete on three
counts (see Fix).

### Failure scenario

`ValidateSubnetCreation` issues an unfiltered, tracking `context.Subnets.ToListAsync()` at
`Helpers.cs:214` on every call. `BatchCreateChildSubnetsCore` calls it twice per submitted child — once
in the pre-flight loop (`SubnetController.Azure.cs:284`) and once immediately before each insert
(`:393`) — and the whole method runs inside `ExecuteWithSubnetLockAsync` (`:233`), i.e. holding the
single global `Bastet:SubnetOperations` lock that gates **every** write in the application. An N-child
import therefore performs **2N full-table loads**, serially, in one transaction, inside the global
writer mutex.

Concrete case on the live rig: a deployment holding 200,001 `Subnets` rows (round 8's own watch list
sizes deployments at 20k-200k), an admin imports a 40-subnet Azure VNet — exactly what the wizard's
"Select All Subnets" posts to `POST /Subnet/BatchCreateChildSubnets`. The import succeeds after
**40.02 s** (one measurement) / **45.89 s** (the other verifier's box) of lock hold. Meanwhile three
completely unrelated authorized writes are **refused with nothing written**:

| rival request | result | wrong outcome |
|---|---|---|
| `POST /Subnet/Create` `10.101.5.0/24` | 200, form re-rendered | *"The operation timed out due to high concurrency. Please try again."* — reachable only from `catch (TimeoutException)` (`Create.cs:138`); `COUNT(*)` for the row = 0 |
| `POST /Subnet/Delete/43` | 302 back to `/Subnet/Delete/43` at 30.01 s | the timeout branch (`Delete.cs:110-113`); row 43 still present, `DeletedSubnets` 0 |
| `POST /HostIp/SetAllocationStatus` | 302 at 30.01 s | `IsFullyAllocated` still 0 |
| `POST /Subnet/BulkCreateFromAzurePlan` | 503 at 30.07 s | *"another subnet operation is in progress"* |

The 30 s budget is `DEFAULT_TIMEOUT_MS` (`src/Bastet/Services/Locking/SqlServerSubnetLockingService.cs:23`),
a private const, and no production call site passes the optional `TimeSpan` — an operator cannot raise
it. All ten lock sites share the one mutex, so the whole write surface is unavailable for the duration:
subnet create/edit/delete, host-IP create/edit/delete, the allocation toggle, both Azure import commits
and the reconcile archive. Reads are unaffected (`GET /Subnet` answered 200 in 2.87 s during a 40 s
hold). Nothing in either request explains the failure: the importer sees success, the victim sees
"high concurrency" when there was no concurrency to speak of.

Cost is O(children x existing rows), so it degrades monotonically: the 30 s budget is crossed at
roughly `children x rows ≈ 6e6` — about 120 children at 50k rows, 60 at 100k, 30 at 200k. The same
construction is in the bulk-import commit, `BulkCreateFromAzurePlanCore`, one full-table load per
created row (`SubnetController.BulkAzure.cs:214` target, `:291` child) inside the lock taken at `:45`.

### How it was reproduced

Own port 5305 / catalog `bastet_r9_vc6a` (and independently 5305-equivalent by the second verifier),
HEAD built Release into a copy; the repository was never written to.

```
# seed: 200,000 volume /32s in 172.16.0.0/12 + one import target 10.246.0.0/18 with no children
docker exec bastet-audit-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PW" -C -N \
  -d bastet_r9_vc6a -i /tmp/vc6a_seed.sql -v WANT=200000        # -> total_subnets 200001

# one threading.Barrier: the shipped import POST plus rival writes delayed 0.4 s.
# drive.py posts the exact 7 hidden inputs per child that _ImportScripts.cshtml:304-310 emits.
python3 drive.py  40 1 10.104.0.0 0.4 26     # 40 x /26 + rival POST /Subnet/Create
python3 drive2.py 40 1 10.106.0.0 0.4        # + BulkCreateFromAzurePlan + BulkDeleteStaleAzureSubnets
python3 drive.py 145 1 10.108.0.0 0.4 26     # 145 = the most children one form post can carry
python3 drive.py 160 1 10.103.0.0 0.4 26     # 160 -> refused by the form binder
python3 parity.py 1                          # 6 guard cases, HEAD vs patched, byte-compared
```

Observed:

```
Kestrel: "POST /Subnet/BatchCreateChildSubnets - 302 0 - 44122.3909ms"
         "POST /Subnet/Create - 200 - text/html - 44098.6210ms"   (200 text/html = the form re-rendered
                                                                   with the error, not the 302 of success)
EF log census, one 40-child import: 291 Executed DbCommand, 41 INSERT INTO [Subnets], and exactly 80
unfiltered "SELECT [s].[Id], ... FROM [Subnets] AS [s]" with no WHERE = 2N whole-table loads.
Sum of Executed DbCommand durations: 208 ms -> the 40 s is client-side materialization and change
tracking burned while holding the mutex, not SQL time.

same request body, table size the only variable:
  201 rows / 20 children   0.61 s      50,001 / 20    6.43 s      50,001 / 120  28.70 s (rival scraped in)
  200,001 / 10            10.45 s      200,001 / 40  45.89 s      200,001 / 145 142.20 s (rival refused)
refusals land at exactly 30.005 s = DEFAULT_TIMEOUT_MS.

160 children -> HTTP 400 {"":["Failed to read the request form. Form value count limit 1024 exceeded."]}
                before any lock is taken.  145 accepted / 146 rejected.
```

Patched build (hoisted tree cache), same tree and same bodies:

```
200,001 / 40   45.89 s -> 3.86 s   rival POST /Subnet/Create succeeded in 3.80 s
200,001 / 145 142.20 s -> 9.02 s   rival succeeded in 8.93 s
second verifier, independently: 40.02 s -> 3.74 s, and ALL THREE rivals succeeded
  (Create 302 -> /Subnet/Details/200246; Delete 302 -> /Subnet i.e. the Index success branch, row
   archived; SetAllocationStatus wrote IsFullyAllocated=1)
guard parity byte-identical to HEAD on all six cases; full rollback on each refusal
dotnet test 690/690; build 0 warnings 0 errors
```

Timing spread is acknowledged: one verifier's holds ran ~35% longer than the other's on the same box,
and a refused `Create` sometimes returns later than 30 s because the re-rendered form builds a parent
dropdown of 200k `<option>` elements. The linear `children x rows` shape is identical across six data
points on each box, so the conclusion is robust to load.

### Fix

Hoist the tree read out of the per-child loop: give `ValidateSubnetCreation` an optional pre-loaded
tree (`List<Subnet>? treeCache = null`, used in place of the `Helpers.cs:214` query), load it once with
`AsNoTracking()` at the top of `BatchCreateChildSubnetsCore`, and append each newly created row
(`Id`/`Name`/`NetworkAddress`/`Cidr`/`ParentSubnetId`) to that list right after its `SaveChangesAsync`
so batch-internal overlap detection keeps working. `AsNoTracking` is safe: `bestParent` and
`potentialChildSubnet` are read only for `.Id`/`.Name`/`.NetworkAddress`/`.Cidr`, never mutated, and
the two tracked reads at `Helpers.cs:137` and `:256` are untouched. The duplicate check at
`Helpers.cs:201` must stay a real indexed query — it uses the unique `{NetworkAddress, Cidr}` index and
sees rows inserted earlier in the same transaction.

Three things the finder's version gets wrong, all measured:

1. **It does not compile as described.** `SubnetController.Azure.cs` has no
   `using Microsoft.EntityFrameworkCore;`, so `context.Subnets.AsNoTracking()` fails with CS1061. Add
   the using, or load the cache through a small private helper in `Helpers.cs` (which already has the
   using) — better, because both batch paths need it.
2. **The bulk sibling is under-specified.** In `BulkCreateFromAzurePlanCore` the cache must be appended
   for **created target subnets** (`SubnetController.BulkAzure.cs:241`) as well as created children
   (`:316`). `orderedItems` is sorted by `PrefixCidr` ascending (`:98`) precisely so a containing item
   runs first; append only children and a later item stops seeing an earlier item's freshly created
   target. The `ExactMatch` branch (rename `:147`, link `:181`) needs no append — it changes neither
   `NetworkAddress` nor `Cidr` — but the cached row then carries the pre-rename `Name`, so an error
   message quoting `bestParent.Name` can print a stale name. Cosmetic; worth a comment.
3. **It moves the threshold rather than removing the shape.** Even hoisted, `Azure.cs:271-291` and
   `:384-419` each run `ValidateSubnetCreation` over every entry, so the batch still makes 2N in-memory
   passes over the whole tree — 3.74 s at 40 children / 200k rows, extrapolating to ~13 s at the
   145-child ceiling. Complete it by dropping the redundant **pre-flight** pass at `:271-291` (keep the
   per-child pass at `:384-419`, which is the one that sees rows created earlier in the batch, and keep
   the encompassing-entry validation at `:301-326`), or by indexing the cached tree (bucket by /8 or
   /16, or sort by network integer) so each validation is a bounded lookup. With the pre-flight pass
   removed the batch drops from 2N scans to N.

**Interim** (free, but not a fix): add `AsNoTracking()` to the single query at `Helpers.cs:214`.
Measured `40.02 s -> 33.69 s` (16%) at 200k/40, and `32.63 s -> 28.48 s` / `15.74 s -> 12.24 s` on a
50k tree — inside the claimed 13-22%. All three rivals were **still refused** at 30.02 s. Take it as an
improvement, never as the fix.

---

## I4 [x2] — Reconcile wizard's `runScan` has no staleness guard, so a superseded scan paints its stale-subnet table and its "nothing to clean up" clean bill on top of the current scan's "Nothing was checked" failure panel

**file:line** — `src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:148` (`function runScan()`),
callbacks at `:161` `complete`, `:164` `success`, `:171` `error`.

**Confidence: confirmed.**

**Corrected by the verifier:** the proposed fix is sound and complete (built, measured, reverted,
re-measured), but the proposed **interim is unsound and was dropped** — its stated premise
("`#rec-scan-btn` is the only caller, so no second scan can be in flight") is false, because
`$("#rec-subscription-select").on("change")` at `:127-129` unconditionally re-enables the button.

### Failure scenario

`runScan()` fires `POST /Azure/ReconcileScan` with no request sequence number, and its `complete`
(`:161`), `success` (`:164`) and `error` (`:171`) callbacks all act unconditionally — whichever response
arrives last wins. This is the exact defect round 8 fixed in the other two wizards with `subnetSeq`
(`_ImportScripts.cshtml`) and `previewSeq` (`_BulkScripts.cshtml`). `#rec-scan-error` is hidden in
exactly one place, `runScan`'s own `beforeSend` (`:157`); `renderPlan` (`:205`) never clears it. So when
a slow scan lands after a newer scan has failed, both panels are on screen at once.

Concrete run, two genuinely stale rows seeded (`rig-r9-orphan-gone` `10.90.9.0/24`,
`rig-r9-orphan-novnet` `10.90.10.0/24`): the operator clicks "Next: Scan", sees nothing happen (a real
ARM-backed scan of 32 proposed rows measured 0.90-1.46 s here, and a real deployment is slower — one
ARM listing plus one direct read per proposed row), uses the still-enabled step-1 pill and clicks Scan
again; the second request dies on the wire (dropped connection, proxy 502, or ARM 429, which
`GetVNetInventory` turns into `scanSucceeded:false` → the same `showScanError` path). Step 2 then renders

> **Nothing was checked.** Error connecting to server: … Because Azure could not be read, BASTET cannot
> tell which resources still exist, so nothing is offered for deletion. Fix the connection and scan
> again.

and, directly beneath it, the fully populated *"These BASTET subnets no longer match Azure"* table with
both rows, live checkboxes and an enabled "Next: Confirm deletion" button. `_StepReview.cshtml:10-11`
states the opposite as the design contract: *"Shown when the scan itself failed. No rows and no delete
option are rendered in that case: an unanswered question must never look like 'everything was
deleted'."*

With zero stale rows the same reorder produces the mirror-image lie: the green *"Everything imported
from this subscription still exists in Azure. There is nothing to clean up."* banner (`:259`) under
"Nothing was checked" — breaking the invariant its own comment asserts at `:252-257` (*"a statement of
fact, so it may only appear when the scan actually established that"*).

The operator can then walk straight through — `lastPlan` (`:206`) and `confirmedIds` (`:314`) come from
the superseded plan — and the archive proceeds (`DeletedSubnets` archives no `AzureResourceId` and has
no restore path). What is **not** wrong is *which* rows get archived: `BulkDeleteStaleAzureSubnets`
re-scans and re-confirms every posted id (`SubnetController.AzureReconcile.cs:55-92`), so a row the
newest scan would withhold is refused with 409. The destructive direction fails closed. The defect is
that the wizard renders a delete affordance, a clean bill of health and a confirmation screen from a
scan it has itself declared failed, and performs an irreversible archive from that screen.

The reverse ordering is also wrong and was **not** in the candidate: a superseded *failure* landing
after a valid plan paints "Nothing was checked" over 2 live rows, marks the step-2 pill
`nav-link active disabled`, and `showScanError` → `invalidateScan()` nulls `lastPlan` — so ticking rows
enables "Next: Confirm deletion" but clicking it hits the bare `return` at `:307` and nothing happens.
That is the "permanently live, inert button" pathology the comment at `:66-71` records as already fixed
once for the delete button.

### How it was reproduced

Own pristine instance (`rsync -a --exclude .git --exclude bin --exclude obj` out of the repo, then
`dotnet build`, `dotnet run --no-build --no-launch-profile` on `http://localhost:5396`,
`BASTET_AZURE_IMPORT=true`, SP1 creds, catalog `bastet_r9_vc7a_b`, seeded with
`rig/seed-reconcile-fixture.sql`), driven by real Chromium via the rig's Playwright.

```
repro_c.py   # NO interception at all: counts real ReconcileScan requests for (a) a plain double-click
             # and (b) Scan -> click #rec-step1-tab -> Scan
repro_a.py   # holds scan #1's REAL 200 (route.fetch then fulfill later; response bytes unaltered) and
             # drops scan #2's connection; snapshots the panels before and after release; then
             # Select all -> Next: Confirm -> type "approved" -> Delete.  Run twice: with the two
             # stale rows, and with them already archived (clean-bill variant)
repro_b.py   # reverse ordering: scan #1 held then dropped, scan #2 answered immediately
```

Observed:

```
repro_c (no interception): mode=dblclick -> 1 request (gap 0 ms)
                          mode=pill     -> 2 requests (gap 215 ms)
real scan latency, 32 proposed rows: 1.46 s / 0.90 s / 1.30 s

repro_a, error-last, HEAD source:
[1.52] scan#1 real 200, 1135 bytes -- HOLDING delivery
[1.94] scan#2 -> dropping connection ; FAIL ReconcileScan net::ERR_CONNECTION_FAILED
[3.94] after scan #2 failed:            scanErrorVisible=True  scanContentVisible=False staleRows=[]
[6.95] after superseded scan #1 painted: scanErrorVisible=True scanContentVisible=True
       staleSectionVisible=True staleRows=[['','rig-r9-orphan-gone','10.90.9.0/24','Subnet deleted',...],
                                           ['','rig-r9-orphan-novnet','10.90.10.0/24','Subnet deleted',...]]
walked on: goConfirmDisabled=False -> step3Active=True count=2 -> "Warning! You are about to delete 2
subnet(s) that no longer match Azure." -> POST {"subnetIds":[3,4],"confirmation":"approved"} -> 200
{"targetsDeleted":2,"subnetsArchived":2}; DeletedSubnets gained OriginalId 3 and 4, DeletedBy dev@example.com

clean-bill variant: scanErrorVisible=True scanContentVisible=True nothingStaleVisible=True
  -> " Nothing was checked. / ... / Everything imported from this subscription still exists in Azure.
       There is nothing to clean up."

repro_b, success-last: after the superseded failure landed -> scanErrorVisible=True,
  scanContentVisible=True, rows still both, step2pill "nav-link active disabled",
  "after select-all goConfirmDisabled=False" but "step3 active = False"  (enabled button, inert)
```

Fix leg (proposed diff applied to the private copy; page served 5 occurrences of `scanSeq`):
error-last → `scanErrorVisible=True scanContentVisible=False staleRows=[]`; success-last → the valid
plan intact, no error painted, `step3 active = True`. Reverted the view to HEAD, rebuilt (0
occurrences of `scanSeq`), re-ran: broken again exactly as above.

### Fix

Give `runScan` the guard the other two wizards already carry. Add `let scanSeq = 0;` beside `lastPlan`
(`:9`), `const seq = ++scanSeq;` as the first line of `runScan` (`:149`), and open `complete` (`:161`),
`success` (`:164`) and `error` (`:171`) with `if (seq !== scanSeq) { return; }` — byte-for-byte the
shape at `_BulkScripts.cshtml:448/462/471`. For defence in depth, have `renderPlan` clear the failure
panel where it reveals the content: add `$("#rec-scan-error").addClass("d-none");` next to
`$("#rec-scan-content").removeClass("d-none");` at `:275`, so the success path does not depend on its
own `beforeSend` having run last. That addition is safe because `renderPlan`'s `showScanError` branches
(`:210`, `:216`) return before reaching `:275`.

Complete for this wizard: `loadSubscriptions` (`:82`) has a single load-time caller (`:452`) so cannot
reorder, and the commit POST (`:376`) disables its own button in `beforeSend` (`:396`).

**No interim.** The finder's two-line "make the button the mutex" (disable `#rec-scan-btn` in
`beforeSend`, re-enable in `complete`) does not close the window: `$("#rec-subscription-select").on("change")`
at `:127-129` unconditionally does `$("#rec-scan-btn").prop("disabled", !$(this).val())`, so an
operator who clicks the step-1 pill mid-scan and picks a **different** subscription re-enables the
button and starts a second overlapping scan — and that variant is worse, because
`selectedSubscriptionId` then names subscription B while the repainted table describes A. (This is a
code-level correction; the rig has one subscription, so a second real `change` event could not be
fired.) A sound interim would need an in-flight flag the change handler respects, which is the seq
guard with extra steps. Ship the seq guard.

---

# Low

## I5 [x1] — "Purge All" counts the archive before it computes the bound it posts, so the purge destroys more records than the confirmation page states

**file:line** — `src/Bastet/Controllers/SubnetController.Delete.cs:277` (`CountAsync()`) versus `:284`
(`MaxAsync`); identical twin at `src/Bastet/Controllers/HostIpController.cs:595` / `:602`.

**Confidence: confirmed.** Votes 2/2.

**Corrected by a verifier:** the harm framing. The view comment (*"anything archived while the operator
was reading it survives"*) is **not** violated — the bound is a snapshot taken at purge-GET render
time, which is H4's deliberate design. What is violated is the XML doc at `PurgeAllViewModels.cs:4-6`
(*"so the purge destroys exactly the records the operator was shown a count of"*): the count printed on
an irreversible confirmation screen can be lower than what the purge destroys.

### Failure scenario

H4 bounds the purge to `Id <= confirmedMaxId`. The GET breaks its own invariant with its query order:
`count` is read at `Delete.cs:277` and `maxId` only at `:284`, in a separate round trip. Anything
archived between the two is **inside the bound and outside the count**, so the POST at `:314-316`
destroys rows the page never mentioned — permanently, since `DeletedSubnets` is the only copy and there
is no restore path. `Id` is IDENTITY and nothing else in the tree deletes from `DeletedSubnets` (the
only other writer is `ArchiveSubnetSubtreeAsync` at `:214`), so `COUNT(*) WHERE Id <= maxId` is the
true scope.

Archives arrive in bursts, atomically: `ArchiveSubnetSubtreeAsync` (`:171`) queues a whole subtree and
`DeleteConfirmedCore` (`:132-142`) commits it in one transaction, so a single ordinary cascade delete
lands its entire batch in the gap or not at all.

End to end on HEAD, one captured render: the page said *"You are about to permanently delete **6200**
archived subnet record(s)"* with `<input name="confirmedMaxId" value="6400">`; posting that exact form
answered 302 to `/Subnet/DeletedSubnets` with *"Permanently purged **6400** deleted subnet
record(s)."*; `SELECT COUNT(*) FROM DeletedSubnets WHERE Id<=6400` = **0**, min surviving `Id` = 6401.
200 archive records destroyed that the page never counted. The host-IP twin behaves identically: page
stated **7800**, form posted `confirmedMaxId=8100`, banner *"Permanently purged 8100 deleted host IP
record(s)."*, min surviving `Id` = 8101.

A second manifestation of the same window: when a *concurrent* admin's purge lands in the gap, the GET
renders `count > 0` beside `confirmedMaxId=0` (1398 of 2630 renders under that workload), and the POST
guard at `:307` then refuses the operator's own form with *"The purge scope was missing from the
form"* — although the form carried exactly what the GET rendered.

### How it was reproduced

Own HEAD build and own instance on port 5307, catalog `bastet_r9_vc8a`; the repository was never
touched.

```
cp -a /home/anuj/code/Bastet/. $W/head/ ; rm -rf $W/head/**/{bin,obj} ; dotnet build --no-incremental
ASPNETCORE_URLS=http://localhost:5307 BASTET_CONNECTION_STRING="...Database=bastet_r9_vc8a..." \
  BASTET_AUTO_MIGRATE=true dotnet run --no-build --no-launch-profile

# writer inside the SQL container: 300 x { BEGIN TRAN; INSERT ... TOP (500) ...; COMMIT; WAITFOR '00:00:00.120' }
sqlcmd -d bastet_r9_vc8a -i /tmp/insert_burst.sql
# 6 threads GET /Subnet/PurgeAllDeletedSubnets, scraping the stated count and the hidden confirmedMaxId
$RIG/pw/bin/python $W/poll2.py 30 6 $W/pairs_head2.csv
# true scope per captured render:
SELECT COUNT(*) pairs, SUM(CASE WHEN s.true_scope > p.stated THEN 1 ELSE 0 END) under_stating,
       MAX(s.true_scope-p.stated) max_excess
  FROM #p p CROSS APPLY (SELECT COUNT(*) AS true_scope FROM DeletedSubnets d WHERE d.Id <= p.maxid) s;
# and the same race driven by 40 real POST /Subnet/Delete/{id} cascades (parent + 100 children)
```

Observed:

```
HEAD: 296 distinct renders - 67 under-state the true purge scope, 229 exact, 0 over-state,
      max excess exactly 500 (one whole archive transaction inside the bound, outside the count)
with REAL cascade deletes as the writer: 9 of 40 landed in the window, each under-stating by exactly
      the whole 101-row batch (e.g. stated 30000 / confirmedMaxId 30101)
quiescent control: 1676 renders, ZERO divergence  -> not an artefact of the harness
window measured at ~0.6 ms on a 2,000-row archive and several ms at 30,000 rows (it widens with archive
      size: rows appended past the COUNT scan's position are missed without blocking it)
end to end: stated 6200 / posted 6400 -> banner "Permanently purged 6400"; COUNT WHERE Id<=6400 = 0
host-IP twin: stated 7800 / posted 8100 -> banner "Permanently purged 8100"; min surviving Id 8101
concurrent-purge variant: 1398 of 2630 renders had count>0 with confirmedMaxId=0

patched (MaxAsync first, then CountAsync(d => d.Id <= maxId), both twins), identical workload:
  231 distinct renders, 0 disagreeing, max_abs_diff 0
  build 0 warnings; dotnet test 690 passed / 0 failed / 0 skipped
  empty archive still redirects with "There are no deleted subnet records to purge." / "...host IP records..."
  quiescent renders exact on a non-contiguous Id space left by a prior purge
```

### Fix

Read the bound first, then count inside it, in both twins:

```csharp
int maxId = await context.DeletedSubnets.MaxAsync(d => (int?)d.Id) ?? 0;
int count = await context.DeletedSubnets.CountAsync(d => d.Id <= maxId);
if (count == 0) { ... }
```

and the same at `HostIpController.cs:595-603` against `DeletedHostIpAssignments`. The rendered count
then equals the POST's scope by construction, with no lock and no extra query. It also closes the
`count > 0` beside `confirmedMaxId=0` variant for free (`maxId == 0` now implies `count == 0`, so the
honest "there are no deleted records to purge" redirect fires).

**No interim** — the fix is a two-line reorder in each twin. **Do not reach for a lock or a
transaction:** round 8 measured `ExecuteWithSubnetLockAsync` on these POSTs to make a
currently-correct ordering wrong, and the brief lists it as never to be re-proposed. The ordering alone
closes it.

---

## I6 [x1] — Logout accepts a `returnUrl` Kestrel cannot write into `Location`, so sign-out answers 500 with the session cookie intact

**file:line** — `src/Bastet/Controllers/AccountController.cs:34` (the single
`!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)` guard, whose `target` feeds both the
Production `SignOut` at `:55-58` and the Development `Redirect` at `:62`).

**Confidence: confirmed.** Votes 2/2.

**Corrected by the verifiers:** severity **medium → low** (the app never generates a triggering URL —
`_Layout.cshtml:77` and `Account/AccessDenied.cshtml:14` are the only logout links and neither sends a
`returnUrl` — and recovery is one click on the nav item); the finder's *"logout silently does not log
out"* is wrong, the user sees a red "Server Error" page; and its explanation of the green suite is
half wrong — the `IUrlHelper.IsLocalUrl` mock returns exactly what the real helper returns for a
non-ASCII input, so it is *sufficient* to pin this defect. What hides it is that no test in the suite
writes an HTTP header.

### Failure scenario

`Url.IsLocalUrl` is the only test applied to the caller-supplied `returnUrl`, and its character check
is `char.IsControl` — category Cc only — so every non-ASCII character passes. That raw value reaches a
response header, which Kestrel refuses to write.

Concrete input, anonymous, no session needed: `GET /Account/Logout?returnUrl=/caf%C3%A9`. Also
reproduced with the euro sign, Cyrillic, an emoji, U+2028, and the realistic shapes
`/Subnet?q=café` and `/Subnet/Details/3?name=Übersicht`.

In **Production** the value becomes `SignOut(new AuthenticationProperties { RedirectUri = target },
Cookies, OIDC)`. `CookieAuthenticationHandler.HandleSignOutAsync` appends the auth-cookie deletion
**and then** assigns `Response.Headers.Location`, which throws
`InvalidOperationException: Invalid non-ASCII or control character in header: 0x00E9`.
`UseExceptionHandler("/Error")` catches it and calls `Response.Clear()`, discarding the `Set-Cookie`
that would have deleted `.AspNetCore.Cookies`; the OIDC end-session redirect is never issued at all.

Wrong output: **HTTP 500** with the generic "Server Error" page, **zero `Set-Cookie` headers and no
`Location`**. Proven with a genuine cookie-auth session (a harness-only `TestSignIn`/`RigMintCookie`
action added to a scratch copy; `Logout` itself untouched): the control `?returnUrl=/Subnet` returns
302, empties the cookie jar, and `GET /Subnet` afterwards is 302 to the IdP; the defect
`?returnUrl=/caf%C3%A9` returns 500, the jar **still holds** `.AspNetCore.Cookies`, and `GET /Subnet`
afterwards is **200** with `/Account/Roles` still rendering `harness-user` / `Admin`. `AddCookie`
(`Program.cs:198-205`) configures no `SessionStore`, so the ticket is entirely self-contained in the
cookie and that discarded `Set-Cookie` is the only revocation mechanism — the user stays signed in for
the remaining sliding hour of `ExpireTimeSpan`, and the IdP session is untouched. The same request in
Development 500s from `Redirect(target)` at `:62`.

No upstream guard: `Logout` is `[AllowAnonymous]` and deliberately has no antiforgery token, and
`GlobalSanitizationFilter.SanitizeObject` returns immediately for `typeof(string)`, so a top-level
`string? returnUrl` is never touched. This is the **non-ASCII half only** — header injection and open
redirect are correctly refused (`/%0d%0aX-Injected:%20yes`, `/%09evil.example`, `/%00abc`,
`//evil.example`, `https://evil.example` all still 302 to the IdP end-session with the cookie
deletion). It is also **not** a re-file of round 7's G8, which was the missing sign-out handler in
Development.

The codebase already contains the exact predicate needed — `Services/Security/HttpHeaderValue.IsValid`,
whose doc comment says *"Kestrel rejects any non-ASCII or control character (tab excepted) when it
writes response headers"* — and `Program.cs:483` applies it to the one **config** value written to a
header, but nothing applies it to the one **caller-supplied** value written to a header.

### How it was reproduced

`git archive HEAD | tar -x` into scratch (repository never written to), `dotnet build --no-incremental`
→ 0 warnings, then `dotnet run --no-build --no-launch-profile` with
`ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_URLS=http://localhost:5300`, catalog
`bastet_r9_vc1a`, `BASTET_OIDC_*` pointed at the rig tenant. Production confirmed: anonymous
`GET /Subnet` → 302 to `login.microsoftonline.com/.../oauth2/v2.0/authorize`.

```
curl -D - -H 'Cookie: .AspNetCore.Cookies=fakevalue; extra=1' 'http://localhost:5300/Account/Logout?returnUrl=/Subnet'
curl -D - -H 'Cookie: .AspNetCore.Cookies=fakevalue; extra=1' 'http://localhost:5300/Account/Logout?returnUrl=/caf%C3%A9'
# matrix over 8 non-ASCII shapes + the bounding set /%0d%0aX-Injected:%20yes /%09evil.example /%00abc
#   //evil.example https%3A%2F%2Fevil.example /Subnet %7E%2FSubnet no-returnUrl
# real-session proof, second copy with one harness-only sign-in action:
COOKIE=$(curl -D - -o /dev/null http://localhost:5300/Account/TestSignIn | grep -oP '^Set-Cookie: \.AspNetCore\.Cookies=\K[^;]+')
printf '# Netscape HTTP Cookie File\nlocalhost\tFALSE\t/\tFALSE\t2000000000\t.AspNetCore.Cookies\t%s\n' "$COOKIE" > jar-def.txt
curl -b jar-def.txt -c jar-def.txt 'http://localhost:5300/Account/Logout?returnUrl=/caf%C3%A9'
curl -b jar-def.txt http://localhost:5300/Subnet
```

Observed:

```
control: HTTP/1.1 302 Found
         Set-Cookie: .AspNetCore.Cookies=; expires=Thu, 01 Jan 1970 ...; secure; samesite=lax; httponly
         Location: https://login.microsoftonline.com/.../oauth2/v2.0/logout?post_logout_redirect_uri=...
defect:  HTTP/1.1 500 Internal Server Error, Content-Type: text/html, NO Set-Cookie, NO Location
  prod.log: System.InvalidOperationException: Invalid non-ASCII or control character in header: 0x00E9
    at HttpHeaders.ThrowInvalidHeaderCharacter -> HttpResponseHeaders...set_Location
    -> CookieAuthenticationHandler.ApplyHeaders -> HandleSignOutAsync -> SignOutResult.ExecuteAsync
matrix: 500 with setcookies=0 for all 8 non-ASCII shapes (log shows 0x2028 for U+2028, so char.IsControl
        really does miss it); 302 with cookie deletion + IdP Location for all 5 bounding inputs and all
        3 controls
real session: mint -> 411-byte cookie; GET /Subnet 200; /Account/Roles renders harness-user / Admin
  control logout -> 302, jar has 0 .AspNetCore.Cookies, then GET /Subnet -> 302 (signed out)
  defect  logout -> 500, jar STILL has .AspNetCore.Cookies, then GET /Subnet -> 200, roles still rendered
  (independently repeated in real Chromium, which stores the Secure cookie because localhost is trustworthy)
Development arm: ?returnUrl=/Subnet -> 302 ; ?returnUrl=/caf%C3%A9 -> 500 (text/plain developer page)
fix leg: build 0 warnings; dotnet test 690 passed / 0 failed; every previously-500 input -> 302 with
  setcookies=1; every previously-refused input still refused; grep -c "Invalid non-ASCII" fixed.log = 0;
  cookie-jar run -> 302, jar emptied, subsequent /Subnet -> 302.  Adding two [InlineData] cases to the
  existing theory fails 2/692 on pristine HEAD and passes 692/692 with the fix.
```

Also swept for sibling paths: 16 other endpoints with non-ASCII path/query/route input produced no
header exception (the framework's own `QueryString.Create` / `PathString.ToUriComponent` percent-encode),
and `AccountController.cs:62` is the only other `Redirect(` in `src/`, reading the same `target`.
Logout is the unique site where a raw caller string reaches `Response.Headers.Location`.

### Fix

Add the project's own header-legality check as a second conjunct on the same guard, so a value that
cannot be written as a header falls back to the anonymous `SignedOut` page and sign-out still completes:

```csharp
string target = !string.IsNullOrEmpty(returnUrl)
        && Url.IsLocalUrl(returnUrl)
        && Bastet.Services.Security.HttpHeaderValue.IsValid(returnUrl)
    ? returnUrl
    : Url.Action(nameof(SignedOut), "Account") ?? "/Account/SignedOut";
```

One line, reusing `Services/Security/HttpHeaderValue.cs` — written for precisely this Kestrel rule and
already used at `Program.cs:483` — and it cannot narrow anything that works today, because every URL
the app itself generates is ASCII. It fixes both the Production and Development branches, since both
read `target`. Extend `HttpHeaderValueTests` with a non-ASCII case and drop the `IsLocalUrl` mock in
`AccountControllerLogoutTests` for one test that passes `/café` through the real helper.

If internationalized return paths must actually be honoured rather than dropped, percent-encode
instead — but only *after* `IsLocalUrl` has passed, and over the path and query segments rather than the
whole string, so `?`, `&`, `=` and `/` survive.

**No interim is cheaper** — the conjunct above *is* the one-line change. The only weaker alternative,
wrapping the tail of `Logout` in a `try/catch` that falls through to `/Account/SignedOut`, is strictly
more code and leaves an exception in the log on every occurrence.

---

## I7 [x1] — No `OnRemoteFailure` handler: every OIDC callback failure (declined consent, expired correlation cookie, reloaded callback) answers HTTP 500 with an unhandled exception

**file:line** — `src/Bastet/Program.cs:237` (`options.Events.OnTicketReceived`, the **only** event
assigned in the `AddOpenIdConnect` block at `:207-244`).

**Confidence: confirmed.** Votes 2/2.

**Corrected by the verifiers:** severity **medium → low**; the finder's *"no in-app way forward … stuck
until they clear cookies manually"* is **false** (the 500 body carries `<a href="/">Return to Home</a>`
and `GET /` immediately re-challenges with a fresh correlation cookie — measured); *"unbounded log
volume"* is bounded at ~10 lines / ~1 kB per request; and the proposed fix's **destination is wrong for
3 of the 4 triggers** and was replaced (see Fix).

### Failure scenario

Non-Development deployments authenticate via the OpenIdConnect handler at `Program.cs:207-244`. The only
event wired up is `OnTicketReceived` (`:237`); `OnRemoteFailure` is never set (`grep -rn
"OnRemoteFailure|AuthenticationFailureException|RemoteFailure|SkipUnrecognizedRequests"` over `src/`,
`test/` and `docs/` returns nothing), so `RemoteAuthenticationHandler` rethrows and
`UseExceptionHandler("/Error")` (`:506`) answers 500. The framework's escape hatch is not wired either:
`OpenIdConnectHandler.HandleAccessDeniedErrorAsync` redirects only when `AccessDeniedPath` is set on
the **OpenIdConnect** options, and the tree's only `AccessDeniedPath` is the **cookie** handler's
(`:200`) — the same one-handler-over trap round 4's D38 documented.

`/signin-oidc` is claimed inside `UseAuthentication`, i.e. before `UseAuthorization`, routing, model
binding and antiforgery, so the `RequireAuthenticatedUser()` fallback policy, every `[Authorize]` and
every validator are downstream of the throw and stop nothing.

Four routine triggers, all reproduced live against a Production instance pointed at real Entra
metadata:

| trigger | what the user did | result |
|---|---|---|
| `error=access_denied&error_description=AADSTS65004…&state=<valid>` posted back to `/signin-oidc` | clicked **Cancel / Decline** at the Microsoft prompt (`response_mode=form_post`, which the challenge really requests) | 500, `OpenIdConnectProtocolException` — correlation validated first, so this is not a mislabelled correlation failure |
| same POST with the `Cookie` header omitted | left the sign-in page open past the correlation cookie's **15-minute** lifetime, or finished in another tab | 500, `AuthenticationFailureException: Correlation failed` |
| same POST with a consumed `code` | reloaded or bookmarked the callback URL | 500 both attempts, `invalid_grant AADSTS9002313` from the real Entra token endpoint |
| bare `GET /signin-oidc` | anything anonymous | 500, `message.State is null or empty` |

The page reads *"Server Error / Status Code: 500 / An unexpected error occurred on the server."* — the
user is told the server broke when they simply declined. Each occurrence also writes a `fail:`-level
"An unhandled exception has occurred" entry with a stack trace, which Production's
`SetMinimumLevel(Warning)` does not filter, so an unauthenticated caller can generate Error-level log
volume on a public path at will (measured 1030 bytes/request; 20 anonymous GETs → 20 x 500 and exactly
200 log lines). Not reachable in Development, where `DevAuthHandler` replaces the OIDC handler
entirely — which is why eight rounds driven on the Development instance could not see it.

Nothing leaks and nothing is written: all security headers are present on the 500
(`X-Content-Type-Options`, `Referrer-Policy`, `Content-Security-Policy: frame-ancestors 'none'`,
`X-Frame-Options: DENY`, `Cache-Control: no-cache,no-store`) and the body is generic; authentication
still fails closed. `/signout-callback-oidc`, `/signout-callback-oidc?state=garbage`, `/signout-oidc`
and `/signout-oidc?sid=x` all answer 200 anonymously and throw nothing, so the gap is `/signin-oidc`
only.

### How it was reproduced

Own Production instance of the unmodified tree on port 5301, catalog `bastet_r9_vc2a`,
`BASTET_OIDC_CLIENT_ID=bastet-probe`, `BASTET_OIDC_AUTHORITY=https://login.microsoftonline.com/<rig
tenant>/v2.0`, `dotnet run --no-build --no-launch-profile`; nothing written into the repository.
A genuine challenge was captured first, then the callback replayed. Correlation and nonce cookies are
`secure`, so they were delivered by an explicit `Cookie` header rather than curl's jar — otherwise
every probe would silently have become the correlation-failure case.

```
curl -s -c oidc1.txt -D h1.txt -o /dev/null http://localhost:5301/Subnet
STATE=$(grep -oP '^Location: \K.*' h1.txt | grep -oP '(?<=[?&])state=\K[^&]*')
CORR=$(awk '$6 ~ /^\.AspNetCore\.Correlation\./ {print $6"="$7}' oidc1.txt)
NONCE=$(awk '$6 ~ /^\.AspNetCore\.OpenIdConnect\.Nonce\./ {print $6"="$7}' oidc1.txt)

(1) curl -i http://localhost:5301/signin-oidc
(2) curl -X POST http://localhost:5301/signin-oidc -H "Cookie: $CORR; $NONCE" \
      --data-urlencode error=access_denied \
      --data-urlencode "error_description=AADSTS65004: User declined to consent to access the app." \
      --data-urlencode "state=$STATE"
(3) same POST with code=<stale code> instead of error=..., run twice
(4) same POST with the Cookie header omitted
(5) curl -i http://localhost:5301/                     # is there a way forward after the 500?
(6) for i in $(seq 1 20); do curl -o /dev/null -w '%{http_code} ' .../signin-oidc; done
```

Observed:

```
challenge is real: 302 to .../oauth2/v2.0/authorize?...&response_mode=form_post&state=CfDJ8...
  Set-Cookie .AspNetCore.Correlation.* path=/signin-oidc secure samesite=none, expires exactly 15 min out
(1) 500, <title>Server Error - BASTET</title>;  AuthenticationFailureException: ... message.State is null or empty
(2) 500;  fail: OpenIdConnectHandler[12] "Message contains error: 'access_denied'..." THEN
          ExceptionHandlerMiddleware[1] ---> OpenIdConnectProtocolException: ... 'AADSTS65004...'
          (no "Correlation failed" line -> correlation validated, the failure came after it)
(3) 500 both attempts; ---> OpenIdConnectProtocolException 'invalid_grant' AADSTS9002313
          at OpenIdConnectHandler.RedeemAuthorizationCodeAsync
(4) 500; ---> AuthenticationFailureException: Correlation failed.
(5) the 500 body contains <a href="/" class="btn btn-primary">Return to Home</a>, and GET / answers 302
    to the authorize endpoint with exactly 1 fresh Set-Cookie: .AspNetCore.Correlation.*
(6) 20 x 500 and exactly 200 log lines (~10 lines with a stack trace per anonymous request, at Error level)

fix switch (B), the finder's version verbatim: all triggers -> 302 /Account/AccessDenied, unhandled
  exceptions 0 -- but that page reads "You do not have permission to access this resource. Your account
  doesn't have the necessary roles..." which is false for 3 of the 4 triggers
fix switch (C), the correction below: all four triggers -> 302 /Account/SignInFailed, that page 200s with
  "Sign-in Not Completed / You were not signed in. / Try signing in again", unhandled exceptions 0,
  4 failures cost 19 log lines total.  Build: 0 warnings, 0 errors.
```

### Fix

Assign `OnRemoteFailure` beside `OnTicketReceived` and take over the response, but send the user to a
**dedicated anonymous page**, not to `AccessDenied`:

```csharp
// in the AddOpenIdConnect options block, beside OnTicketReceived (Program.cs:237-243)
options.Events.OnRemoteFailure = context =>
{
    // Declined or pending consent, an expired/consumed correlation cookie and a replayed callback
    // are normal events, not server faults. Unhandled they escape RemoteAuthenticationHandler as
    // AuthenticationFailureException, so UseExceptionHandler answers HTTP 500 "An unexpected error
    // occurred on the server" and writes a stack trace at Error level for anything an anonymous
    // caller can send to /signin-oidc.
    context.HttpContext.RequestServices
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Bastet.Authentication")
        .LogWarning("OIDC sign-in did not complete: {Reason}", context.Failure?.Message);
    context.Response.Redirect("/Account/SignInFailed");
    context.HandleResponse();
    return Task.CompletedTask;
};
```

Log the **message**, not the exception object: passing `context.Failure` re-creates the 10-line stack
trace per anonymous request, just relabelled Warning. Then in `AccountController` (which already hosts
`AccessDenied`/`SignedOut`) add an `[AllowAnonymous] SignInFailed()` action with a doc comment stating
why it must not challenge, and `Views/Account/SignInFailed.cshtml`: *"Sign-in Not Completed / You were
not signed in."* plus the three causes in plain words and a "Try signing in again" link to `/`.

**Do not forget the allow-list.** `test/Bastet.Tests/Security/ControllerAuthorizationTests.cs` asserts
that every `[AllowAnonymous]` action appears in its `AllowedAnonymousActions` list with a reason
(*"is marked [AllowAnonymous] but is not in the allow-list"*), so add `["AccountController.SignInFailed"]`
there or the suite fails. That is a whole test file the "diff to the options block" framing does not
mention.

**Interim** (`options.SkipUnrecognizedRequests = true;`) closes only the unauthenticated half —
measured: the bare `GET /signin-oidc` stops 500ing and falls through to a normal challenge. The
declined-consent callback still returned 500 under it, because that failure happens after state and
correlation have both validated. Not worth taking on its own.

---

## I8 [x2] — `AllHostIps` and `AllDeletedHostIps` never clamp `page` to the last page: an over-range page renders an inverted "Showing 51-40 of 40" banner over an empty table, and a very large one overflows the `int` skip and serves page 1's rows

**file:line** — `src/Bastet/Controllers/HostIpController.cs:476` (`.Skip((page - 1) * pageSize)` in
`AllHostIps`) and the twin at `:526` (`AllDeletedHostIps`); the floors are `:444` / `:509` and
`totalCount` is computed at `:472` / `:518`. `grep -rn "Skip((page"` returns exactly those two sites.

**Confidence: confirmed.**

**Corrected by the verifier:** nothing in the finding itself — but note that its **fix and its interim
choose opposite semantics** (the clamp shows the *last* page; the interim shows a correctly *empty*
page), and the interim leaves the inverted banner in place, so it closes only the wrong-rows half. Take
the clamp; do not ship the interim as the whole fix.

### Failure scenario

`page` is only floored (`page = Math.Max(1, page)`) and never clamped to the number of pages, and the
skip is computed in `int` with `pageSize = 50`. `TotalPages` exists on both view models
(`AllHostIpViewModels.cs:12`, `AllDeletedHostIpViewModels.cs:12`) but is consulted only by the pager
arms, never to bound `page`. The views recompute the same unclamped product from `Model.CurrentPage`
(`AllHostIps.cshtml:23`, `AllDeletedHostIps.cshtml:34`) and clamp only the *end* of the range, so the
range inverts.

With 4 host IPs and `GET /HostIp/AllHostIps?page=45000000`: `(45000000-1)*50 = 2,249,999,950` overflows
`int` to `-2,044,967,346`; `Enumerable.Skip` treats a negative count as 0, so the action returns the
**first** 50 rows while `CurrentPage` stays 45000000, and the header renders
`Showing -2044967345--2044967296 of 4`. The wrongness is not just the label: it is inconsistent with the
correct behaviour on the same endpoint — `?page=2` on the same 4-row dataset correctly returns zero
rows, `?page=45000000` returns page 1. Both listings are affected and both are
`[Authorize(Policy = "RequireViewRole")]`.

Reachable with **zero URL editing**: the app's own pager emitted `href="/HostIp/AllDeletedHostIps?page=2"`
for both Next and Last while the archive held 61 rows, and `?page=2` then correctly read
*"Showing 51-61 of 61"* with 11 rows. A second admin submitted the Purge All form it already had open
(`confirmedMaxId=40`, rendered by the app); H4's shipped scoping left 21 rows; reloading the *same
pager-supplied URL* returned 200 with *"Showing 51-21 of 21 deleted host IP assignment(s)."* over a
table containing only its header row.

No unhandled exception, no write, no disclosure — the rows the overflow serves are page 1's rows the
same caller already sees at `?page=1` — and the pager emits only First/Previous/Next/Last, so there is
no unbounded loop. Impact is a self-contradictory page: it asserts N records exist and shows none, or
claims a negative range while showing page 1. `grep -rniE "paginat|CurrentPage|Skip\(\(page"` over
`docs/` returns zero hits, so no prior round considered and left this.

### How it was reproduced

Pristine `a8f669b` build, own instance on port 5302 against catalog `bastet_r9_vc3a` (4 live host IPs
in one /24; archive seeded then grown to 61 by SQL). Repository never modified.

```
# (A) label inversion, driven entirely through the app's own forms - no URL editing
curl -s -c $J http://localhost:5302/HostIp/PurgeAllDeletedHostIps        # archive 40, confirmedMaxId=40
#   ... 21 more rows archived -> 61 ...
curl -s -b $J http://localhost:5302/HostIp/AllDeletedHostIps             # pager emits ?page=2 for Next/Last
curl -s -b $J "http://localhost:5302/HostIp/AllDeletedHostIps?page=2"    # correct: 51-61 of 61, 11 rows
curl -s -b $J -X POST http://localhost:5302/HostIp/PurgeAllDeletedHostIps \
     -H "RequestVerificationToken: $TOKEN" --data-urlencode "__RequestVerificationToken=$TOKEN" \
     --data-urlencode "confirmation=approved" --data-urlencode "confirmedMaxId=40"
curl -s -b $J "http://localhost:5302/HostIp/AllDeletedHostIps?page=2"    # reload of the SAME pager URL

# (B) int overflow
for u in "AllHostIps?page=1" "AllHostIps?page=2" "AllHostIps?page=45000000" "AllHostIps?page=999999999" \
         "AllHostIps?page=2147483647" "AllHostIps?page=0" "AllHostIps?page=-5" \
         "AllDeletedHostIps?page=45000000"; do ... done

# (C) real Chromium (Playwright), capturing console + pageerror
# (D) clamp applied in a copy: dotnet build --no-incremental ; dotnet test ; re-run (B)
```

Observed:

```
(A) POST -> 302 ; SELECT COUNT(*) FROM DeletedHostIpAssignments = 21 (was 61)
    reload of the unchanged pager-supplied URL -> http=200
      BANNER: "Showing 51-21 of 21 deleted host IP assignment(s)."
      data rows (<td><code>): 0 ;  <tr> occurrences: 1 (the header row only)
      pager: First -> ?page=1, Previous -> ?page=1, Next disabled, Last disabled

(B) AllDeletedHostIps?page=1        200 datarows=40  [Showing 1-40 of 40]
    AllDeletedHostIps?page=2        200 datarows=0   [Showing 51-40 of 40]     <- inverted, empty
    AllHostIps?page=2               200 datarows=0   [Showing 51-4 of 4]       <- correctly empty
    AllHostIps?page=45000000        200 datarows=4   [Showing -2044967345--2044967296 of 4]  <- page 1's rows
    AllDeletedHostIps?page=45000000 200 datarows=40  [Showing -2044967345--2044967296 of 40]
    AllHostIps?page=999999999       200 datarows=4   [Showing -1539607651--1539607602 of 4]
    AllHostIps?page=2147483647      200 datarows=4   [Showing -99--50 of 4]
    AllHostIps?page=0 and ?page=-5  200 datarows=4   [Showing 1-4 of 4]        (lower bound is fine)

(C) Chromium: status 200, banner " Showing -2044967345--2044967296 of 4 host IP assignment(s).",
    4 tbody rows rendered, console errors/warnings []  (screenshots kept in the verifier's scratch dir)

(D) build 0 warnings 0 errors; dotnet test 690 passed / 0 failed / 0 skipped (no test pins either action).
    Fixed build, 61-row archive: page=1 -> 50 rows "1-50 of 61"; page=2 -> 11 rows "51-61 of 61";
    page=3 and page=45000000 -> 11 rows "51-61 of 61". 4-row live set: every out-of-range value renders
    "Showing 1-4 of 4" with 4 rows and an all-disabled pager. No negative range survives anywhere.
```

### Fix

Clamp the requested page to the real range once `totalCount` is known, and use the clamped value for
both the query and the view model so the label, the rows and the pager agree:

```csharp
int totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));
page = Math.Clamp(page, 1, totalPages);
```

placed before the `Skip`/`Take` and before `CurrentPage = page`. Apply to both
`HostIpController.cs:441-499` and `:506-588`, which are twins. `Math.Clamp` cannot throw because
`Math.Max(1, …)` guarantees `totalPages >= 1`, and post-clamp `(page-1)*pageSize <= totalCount`, so the
overflow is structurally gone. No view change is needed — the views derive from the now-clamped
`CurrentPage`. Note the intended behaviour change: `?page=2` on a 4-row set becomes page 1 rather than
an empty page 2, which is the only way label, rows and pager can agree.

**Interim** (one line at each of `:476` and `:526`, no restructuring) — do the arithmetic in `long` and
cap it, so an absurd page becomes a correctly *empty* page instead of page 1:

```csharp
.Skip((int)Math.Min((long)(page - 1) * pageSize, totalCount))
```

(`totalCount` is already computed a few lines above in both actions, and `page >= 1` is guaranteed so
there is no underflow). This closes the wrong-rows half only; the header label still reads oddly until
the clamp lands.

---

# Info

None.

---

# Refuted — reported by a finder, killed by the verifier

| Candidate | Verdict | Reason |
|---|---|---|
| *(none)* | — | **No candidate was refuted this round.** All 8 that reached a verifier survived, unanimously: the four `[x2]` at 1/1 and the four `[x1]` at 2/2. |

The kill happened one stage earlier. Of **26** raw findings, **18** did not become candidates — they
died at the merge as duplicates of one another or as re-files of items the round-9 brief lists as
accepted-and-open, deliberately not done, or refuted in rounds 5-8. Nothing was dropped for being
unreproducible: `reproducedLive` is 8 of 8 and `notRunnable` is 0.

What the verifiers *did* kill was parts of what survived, and that is where the value is: three
severities (all downward), one citation, and on five findings a proposed fix or interim. Two are worth
singling out because a naive reader will re-propose them.

| Proposed by the finder | Measured outcome |
|---|---|
| **I1**, part 3: a reconciler-level regression test (drift-only `plan.Items`, review-item descendant, `ApplyConfirmations` with no confirmations) | **Built and run against pristine HEAD: it passes.** `ApplyConfirmations` is correct; the defect is at the controller seam. The test pins nothing. |
| **I1**, interim: `[.. liveLinked, .. plan.ReviewItems.Select(i => i.SubnetId)]` at `AzureReconciler.cs:124` | **More expensive than the real fix.** Fails `AzureReconcilerTests.ApplyConfirmations_TargetWhoseDescendantIsAReviewItem_IsAlsoWithheld` at `:866` (`Assert.Single() Failure: The collection was empty`) because that test's arrange runs before `ApplyConfirmations`. |
| **I7**, fix: redirect `OnRemoteFailure` to `/Account/AccessDenied` | **Wrong destination for 3 of 4 triggers.** That page tells a user whose correlation cookie expired that their account lacks roles. Replaced with a dedicated `SignInFailed` page. |
| **I4**, interim: disable `#rec-scan-btn` for the scan's duration | **Premise false.** `$("#rec-subscription-select").on("change")` (`:127-129`) re-enables it, and the resulting variant is worse — the repainted table then describes a different subscription. Dropped. |
| **I3**, fix: hoist the tree read | **Works but incomplete on three counts** — does not compile as described (missing `using`), under-specifies the bulk sibling's append points, and leaves 2N in-memory passes. Extended. |

---

# Watch list — not findings, but worth knowing

Round 8's list, trimmed, plus what this round's verifiers established on the way past. Several items are
the *reason* a nearby defect is worth more than it looks.

### Carried forward

- **`DeletedSubnets` archives neither `AzureResourceId` nor `IsFullyAllocated`**, the deleted-subnets
  table renders neither `Tags` nor `OriginalParentId`, and **there is no restore path anywhere in the
  app.** Re-confirmed from the live schema this round: 14 columns, `Id … ModifiedBy`, zero matching
  `%Azure%`. This is what makes I1, I2, I4 and I5 unrecoverable.
- **"There is no test for this" is not a finding.** Still no `WebApplicationFactory`, no integration
  host, no JS test harness. That shape has been refuted in five consecutive rounds.
- **Entry gates are not row invariants.** A `Blocked` bulk-planner row, a refused `GET
  /Azure/Import/{id}` (*"must not have any child subnets or host IP assignments"*, which also fires for
  a subnet carrying host IPs), and a hidden Import-from-Azure button are all **expected** on a
  correctly imported subnet. This killed round 8's only refuted candidate, and I1's verifier had to
  clear it explicitly.
- **The purge POST does not require its confirmation page at all** — antiforgery tokens are
  per-session. By design; a different question from scoping (I5 is the GET's query order).
- **Do not re-file the purge lock gap**, and do not propose `ExecuteWithSubnetLockAsync` on the two
  purge POSTs: built and measured in round 8 to make a currently-correct ordering wrong, and round 6
  had already left it deliberately.
- **`_StepConfirm.cshtml` has no warnings block** — `#rec-scan-warnings` exists only in
  `_StepReview.cshtml`, so a scan warning never reaches the screen that performs the archive. Still a
  real gap, still deferred — and I1 sharpens it: in the drift-only case `plan.Warnings` is empty, so
  the block would render nothing. It is not a substitute for the guard.
- **The same click-time-versus-response-time split exists in `loadVNets`** (`_BulkScripts.cshtml`).
  Not filed: only the subscription *label* can disagree.
- **After H6's fix, `_SubnetCalculationScripts.cshtml`'s overlap arm has no remaining visitor.**
  Defence-in-depth for a case `findOptimalCidr` makes impossible — **do not tidy it away.**
- **`/Azure/BulkImportPreview` latency scales with `existing x selected`** — ~0.06 ms per (selected
  prefix x 1000 existing subnets): 39 ms at 20k/1, **7 247 ms** at 200k/600. A lock-free read endpoint,
  so distinct from I3.
- **`GlobalSanitizationFilter` runs after model binding and validation**, and
  `SanitizeObject` returns immediately for `typeof(string)` (`Filters/GlobalSanitizationFilter.cs:44`),
  so a top-level `string?` action parameter is never sanitized. That is how I6's `returnUrl` arrives
  raw. Any new `[Sanitize*]` attribute needs a matching validator.
- **`MockAzureService.DefaultConfirmation` is `Deleted`** — any test touching the confirmation path
  must set the verdict explicitly.
- **EF Core's `SqlServerDatabaseCreator.Exists()` misreads SQL 4060** the same way the bootstrap did;
  any fix in that `catch` must abort startup, not log. **F15 / the migration lock:** the lock opens the
  configured catalog first and falls back to `master` only on 4060 — do not re-propose an unconditional
  `master` scope. `Program.cs`'s crafted exception for a failed `master` open is effectively
  unreachable on SQL Server.
- **Accepted and unchanged:** ForwardedHeaders trust-all with `AllowedHosts: "*"`; the Development
  `DevAuthHandler` bypass; `CollectDescendants` without a cycle guard; the blind `catch { }` around the
  DataProtectionKeys probe; the DataProtection key ring persisted unencrypted; **C20**, the reconcile
  check/act window; the unreachable IP-change branch in `ValidateHostIpUpdate` (the one place applying
  the network/broadcast reservations without the `cidr < 31` guard — a trap for whoever makes that field
  editable).
- **Deliberately left, small:** the equality-vs-membership prefix check on the VNet-resource-id stamp;
  the bulk import reading only a multi-prefix Azure subnet's first prefix; `findOptimalCidr`'s loop
  bound and the six CIDR→mask copies across four files; `AnnotatePrefix` cannot return
  `AlreadyImported` (4 046 brute-forced outcomes); the three cheap test gaps; the eleven controller
  sites that `RedirectToAction` to `/Error/{code}` instead of answering in place;
  `HostIpController.DeletedHostIps(int subnetId)` binding `subnetId = 0` → `NotFound()`; migration
  `.Designer.cs` snapshots holding old column widths on purpose; the committed rig tenant ID at
  `Properties/launchSettings.json:41`; three expected CodeQL log-forging alerts on `main`;
  `Max Pool Size` unset everywhere; `Logging__LogLevel__*` outranking `BASTET_LOG_LEVEL_*`;
  `SaveTokens = true` with no scope gate; `success` not being uniform across the Azure AJAX endpoints;
  `AZURE_TOKEN_CREDENTIALS=dev` excluding `EnvironmentCredential`; ARM ids being path-based and
  surviving delete-and-recreate.
- **Rig hazards.** `pkill -f "Bastet.dll"` kills every instance on the box — match on `ASPNETCORE_URLS`
  or a captured PID. Headless Chromium never ticks `requestAnimationFrame`, so delete
  `window.requestAnimationFrame` before any animation assertion. jQuery 4.0.0 dispatches an aborted
  request's `error`/`complete` handlers **synchronously inside `.abort()`**, so any `.abort()`-based
  staleness interim is placement-sensitive. Several `bastet-visible` VNets share `10.20.0.0/16`.
- **Round-7 and round-8 line-number citations have already moved and will move again. Re-derive every
  line before citing it.**

### New in round 9

- **`FormOptions.ValueCountLimit` (default 1024) caps one `BatchCreateChildSubnets` post at 145
  children** — measured 145 accepted, 146 rejected with `400 {"":["Failed to read the request form.
  Form value count limit 1024 exceeded."]}` before any lock is taken (the wizard emits 7 hidden inputs
  per child plus 4 form fields plus the token). It does **not** apply to `BulkCreateFromAzurePlan`,
  which binds `[FromBody]` JSON. This is what bounds I3.
- **Nothing checks a cancellation token on the batch import.** A 145-child import committed in full
  after the client aborted at 120 s (Kestrel 499, 150 261 ms) — an operator giving up does not shorten
  the lock hold.
- **On a single replica the 30 s lock timeout expires in the in-process `SemaphoreSlim` `_localGate`**
  (`Services/Locking/SqlServerSubnetLockingService.cs:57`), not in `sp_getapplock`. Both throw
  `TimeoutException` and render the identical message; the `sp_getapplock` attribution only applies
  across replicas. `DEFAULT_TIMEOUT_MS` (`:23`) is a private const no call site overrides.
- **`Helpers.cs:214` is the only whole-table load re-issued per item inside the lock.** `Edit`'s
  `allOtherSubnets` (`Edit.cs:113`) and `Delete`'s tree read are once per request;
  `HostIpController`'s two `ToListAsync` sites (`:448`, `:521`) are read-only pages outside the lock.
  Reads are unaffected by a held lock (`GET /Subnet` 200 in 2.87 s during a 40 s hold).
- **.NET's `Url.IsLocalUrl` rejects only category-Cc characters** (`char.IsControl`), so U+2028 and
  every non-ASCII character pass. All header-injection shapes are correctly refused — checked byte by
  byte over `0x00`-`0x1F` and `0x7F`.
- **`OpenIdConnectHandler.HandleAccessDeniedErrorAsync` only redirects when `AccessDeniedPath` is set
  on the OIDC options.** The tree's only `AccessDeniedPath` is the **cookie** handler's
  (`Program.cs:200`) — the one-handler-over trap round 4's D38 documented.
  `/signout-callback-oidc`, `/signout-oidc` and their garbage-parameter variants all answer 200
  anonymously and throw nothing.
- **`ControllerAuthorizationTests` gates every `[AllowAnonymous]` action** against an allow-list that
  requires a reason. Any new anonymous action (I7's `SignInFailed`) fails the suite until it is listed.
- **`AzureReconcilerTests.ApplyConfirmations_TargetWhoseDescendantIsAReviewItem_IsAlsoWithheld`
  (`:866`) asserts `Assert.Single(plan.Items)` *before* calling `ApplyConfirmations`,** so any guard
  moved earlier into `BuildPlan` fails that arrange; relax it to `Assert.Empty` if that is ever done.
  And `SubnetFromOtherSubscription_Ignored` (`:457-467`) uses a standalone row with no ancestor, so it
  passes with or without I2's fix — it never exercises a multi-subscription tree.
- **`BulkCreateFromAzurePlan` performs no ARM read.** It re-plans against the database and trusts the
  posted ids (`SubnetController.BulkAzure.cs:100-121`, `:265-288`), and `FindDeepestContainer`
  (`AzureBulkImportPlanner.cs:329-349`) parents purely on address containment with no subscription
  test. That is how a foreign-subscription VNet gets nested under a local reservation (I2).
  Relatedly, `IsFullyAllocated` is settable by a plain `RequireEditRole` UI POST
  (`HostIpController.cs:723`), so no Azure path is needed to produce the row shape I1 destroys.
- **The reconcile wizard offers no way out during a scan:** `#rec-back-to-subscription-btn` lives
  inside the `d-none`'d `#rec-scan-content` and the step-1 pill is never disabled, which is why I4's
  double scan is an ordinary two-gesture action. A plain double-click on `#rec-scan-btn` yields only
  **one** request (`activateTab` moves the button out from under the cursor) — the reachable overlap
  path is Scan → step-1 pill → Scan.
- **`ExecuteDeleteAsync` in the purge POSTs reports what it actually deleted**, which is how I5's two
  numbers were caught disagreeing. Only two such purge sites exist in the tree.
- **Rig hazard, this round:** two verifiers found their *assigned* port and catalog already held by a
  live process from a colliding label. Check listeners and catalogs before starting, do not clobber a
  sibling's instance, and kill only by captured PID.
