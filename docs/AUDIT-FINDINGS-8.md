# Bastet — Round-8 Audit Findings

| | |
|---|---|
| Round | **8** (finding letter **H**) |
| Target branch | `audit/round-8` |
| Baseline HEAD | `ff285cf` — *"Audit 7 Cleanup (#150)"*, identical to `main` |
| Build at baseline | `dotnet build --no-incremental` → 0 warnings, 0 errors |
| Tests at baseline | `dotnet test` → **677 passed, 0 failed, 0 skipped** |
| Working tree | clean at start and at finish |
| Date | 2026-07-28 |

Every line number below was re-derived against the working tree at `ff285cf` while writing this file.

---

## Verdict

**Seven findings: one high, two medium, three low, one info.** All seven were reproduced against the
live rig — real SQL Server 2022, real ARM, real headless Chromium — not argued from source. One
candidate was refuted.

Read **H1** first. Azure reconcile's confirm cascade archives every descendant of a stale target
without checking that descendant's own Azure verdict, so a single approved delete destroys Bastet
rows that the *same scan* proved are still live in Azure, or that the same response explicitly said
had been *withheld from deletion*. It reproduces with no concurrency, no crafted request and no RBAC
trickery, on the ordinary operator path, and `DeletedSubnets` does not archive `AzureResourceId`, so
the link is destroyed with no in-app restore.

Then **H2** and **H3**, which are the same class as each other: both Azure import wizards fire
overlapping AJAX requests with no ordering guard, and in both the browser ends up committing
something the screen was not showing. H2 writes a child `AzureResourceId` belonging to a VNet the
parent is not linked to — silently, and unrepairable by every route checked. H3 commits a plan the
operator never reviewed.

**H4** and **H5** are operator-facing: an unbounded "Purge All" that ignores the count its own
confirmation ceremony states, and a startup bootstrap that misreads SQL error 4060 and then tells the
operator two successively wrong things about a database full of their data. **H6** is a false
"overlap" message on the Details Create-Subnet modal, introduced by round 7's G11 reorder. **H7** is a
completion banner that cannot distinguish "I linked a subnet" from "I did nothing".

Round 7's fix set is where round 8's defects were expected to be, and one is: **H6 is residue of
G11**. The other six are older code that no previous round drove hard enough in a browser or against
a real SQL Server.

---

## How this audit ran

**The beats.** Twenty finder agents were spread over the surface: Azure integration (import wizards,
bulk planner, reconciler, ARM boundary), UI and client-side JavaScript, logic and data integrity,
locking and lifecycle, security and sanitization, regression correctness against the round-7 fix set,
and a deep sweep over everything else.

**Two passes plus a deep sweep.** Each beat was worked by two independent finder passes that did not
see each other's output, plus a sweep pass over the whole tree. That is what `[x2]` and `[x1]` mean:

- **`[x2]`** — two independent passes found it. Independent agreement is strong evidence that the
  defect is visible from more than one angle.
- **`[x1]`** — one pass found it and the other did not. Absence is weak evidence, so every `[x1]`
  candidate was given a **second verifier working a reachability-and-consequence lens** —
  can a real user get here from a realistic starting state, and what is actually damaged? — precisely
  because "the other pass missed it" and "the other pass was right" look identical from the outside.
  Two of the four `[x1]` candidates came back materially changed (H4 reframed, H5 strengthened), and
  one came back dead (the `BatchCreateChildSubnets` candidate, in the refuted table below — this round
  raised no finding numbered outside H1-H7).

**The verification scheme.** Every candidate got at least one verifier on a **refutation lens**: is
the citation real, is the path reachable, are the inputs acceptable, does the consequence follow, is
it already fixed, is it a duplicate of something accepted or previously refuted. Verifiers ran on
their own port, their own SQL catalog and their own publish, and reproduced independently rather than
re-reading the finder's transcript. **Where a verifier corrected a finder, the correction is what is
written below.**

**The funnel.**

| | |
|---|---|
| Finder agents | 20 |
| Raw findings reported | 18 |
| Candidates after dedup/merge | 8 — **4 `[x2]`, 4 `[x1]`** |
| Verifier agents | 12 |
| Judged | 8 |
| **Survived** | **7** |
| Refuted | 1 |
| Reproduced against the live rig | **7 of 7** |
| Not runnable | 0 |

**Corrections applied by verifiers.** Severity: H1 critical → high, H4 medium → low, H7 low → info.
Proposed fixes: H1, H2, H3 and H5 corrected as incomplete; H4's corrected as **unsound** (it was
built, run, and measured to make one currently-correct ordering wrong). H4's *mechanism* was also
refuted and the finding reframed around what actually reproduces.

---

# Critical

None.

---

# High

## H1 [x2] — Azure reconcile archives descendants the same scan proved live, or explicitly withheld from deletion

_H1 is fixed and committed. `AzureReconciler` now withholds any target whose subtree contains a
subnet the same scan says must not be destroyed, in **both** places that knowledge exists and nowhere
else. In `BuildPlan`, every linked row that evaluated to live — the ones that become neither an item
nor a review item, and so are invisible downstream — is collected into a `liveLinked` set, and any
item whose `DescendantSubnetIds` meets it is dropped with a warning naming it. In
`ApplyConfirmations`, the same is done for `notVisible ∪ unknown ∪ stillLive ∪ plan.ReviewItems`.
`ReviewItems` is in that set for the reason the finding gives: the confirmation loop walks
`plan.Items` only, so a `FullyAllocatingSubnetDeleted` descendant — which ordinary imports produce —
appears in none of the lists built there._

_The two removals are one private helper, `WithholdTargetsWhoseCascadeIsBlocked`, rather than the two
inline copies the finding sketched. They differ only in which ids are protected and in the clause
explaining why, so a single implementation is what stops the live-descendant guard and the withheld
guard drifting apart — the same failure mode that produced this finding, where `ApplyConfirmations`
knew about withholding and the cascade did not._

_Verified at both levels. Six tests were written first and five of them failed against the unfixed
code with `Collection was not empty` — the target surviving in `plan.Items` — covering the live
descendant, all three withholding verdicts (`NotVisible`, `Unknown`, `Live`) and a review-item
descendant. The sixth is the control that must **not** move: a VNet deleted together with its own
imported children stays committable, and it passed before and after. Then end to end on the live rig,
real ARM and SQL Server 2022, driving the shipped endpoints: `rig-h1-inner` (10.78.128.0/17, live,
carrying `rig-h1-inner-a`) nested under `rig-h1-outer` (10.78.0.0/16) by two ordinary bulk imports,
then only the outer VNet deleted in Azure._

```
                       unfixed (ff285cf)                       fixed
scan                   canCommit True, warnings []             canCommit False, warning names 'rig-h1-outer'
approve subnetIds:[1]  200 {"subnetsArchived":2}               409 "no longer reported as deleted in Azure"
Subnets after          (no rows)                               1 rig-h1-outer, 2 rig-h1-inner
DeletedSubnets after   2                                       0
rig-h1-inner-a in ARM  10.78.130.0/24 Succeeded                10.78.130.0/24 Succeeded
```

_The finding's step 3 — that nothing further is needed server-side, because the existing
`noLongerStale` gate answers 409 and already carries `plan.Warnings` — is correct and was confirmed
rather than assumed: the 409 body above carries the withholding sentence verbatim, so the operator is
told why without any new plumbing._

_Two things in the finding were deliberately **not** done, both out of scope for the defect.
Interim **(C)**, the display-only descendant count, is moot now that a blocked target is never offered:
there is nothing to warn about on a screen that cannot be reached. Adding a warnings block to
`_StepConfirm.cshtml` is a real gap and stays on the watch list, but it is a separate change to a view
this fix does not touch, and with the cascade guard in place no withheld row can reach that screen._

_Test count 677 → 683 (+6). `dotnet build --no-incremental` 0 warnings, 0 errors._

---

# Medium

## H2 [x2] — Single-VNet import wizard has no staleness guard on `loadSubnets`, so an out-of-order response posts one VNet's subnets under another VNet's identity

**Citation.** `src/Bastet/Views/Azure/Import/_ImportScripts.cshtml:250` (`loadSubnets`' `success`
handler), against `:61-62` (identity written synchronously on click) and `:224` (`loadSubnets` keeps
no token and no jqXHR).

**Confidence.** Confirmed.

**Scenario.** `#select-vnet-btn` writes the hidden `vnetName` / `vnetResourceId` fields synchronously
at `:61-62` and then calls `loadSubnets(selectedVNetId)` at `:63`. The `success` handler at `:250`
empties and rebuilds `#subnet-list` (`:254-255`) with **no** comparison against the currently selected
VNet, and `/Azure/GetSubnets` does live per-VNet ARM reads, so latency varies.

An admin on a Bastet subnet whose prefix is shared by two Azure VNets — a topology the source itself
contemplates (*"Two VNets in one subscription may share a prefix"*,
`SubnetController.Azure.cs:331-332`) — picks VNet A, goes back via the still-enabled step-2 pill,
picks VNet B, and gets **A's subnet rows repainted over B's identity** when A's response lands last.
`Views/Azure/Import/_SubnetList.cshtml` prints no VNet name on step 3, so nothing on screen
contradicts it.

Ticking a row and pressing Import posts children stamped with A's ARM ids alongside B's
`vnetName`/`vnetResourceId`. The server accepts it — no site compares a child's `AzureResourceId`
against `vnetResourceId`; the child stamp is a straight copy at `SubnetController.Azure.cs:409` — and
reports success. The resulting row is unrepairable: the wizard refuses re-entry, G1 refuses a crafted
relink, `Edit` cannot bind the column, and `DeletedSubnets` does not archive it. If the wrongly
stamped VNet is later deleted in Azure, reconcile archives the parent **and its whole subtree**,
including children that are alive in the other VNet.

**Reproduction.** Live app on 127.0.0.1:5403, catalog `bastet_audit_203`, headless Chromium, real ARM,
**no proxy and no injected delay** — the superseded response landed last on natural latency alone:

```
trial 2: resp-order=[(1.431,'rig-uip2-twin',200),(1.512,'rig-vnet-alpha',200)]
         rows=['rig-snet-alpha-web','-app','-data','-import-only'] hidden vnetName='rig-uip2-twin' STALE=True
...
DELAY=0.0  trials=8  stale-repaints=5
```

Carried through to the write (again unmanipulated). Captured `POST /Subnet/BatchCreateChildSubnets`,
URL-decoded:

```
parentId=1  vnetName=rig-uip2-twin  isAzureImport=true
vnetResourceId=.../virtualNetworks/rig-uip2-twin
subnets[0].Name=rig-snet-alpha-web  subnets[0].NetworkAddress=10.20.1.0  subnets[0].Cidr=24
subnets[0].Description=Imported from Azure VNet: rig-uip2-twin
subnets[0].AzureResourceId=.../virtualNetworks/rig-vnet-alpha/subnets/rig-snet-alpha-web
-> 302 /Subnet/Details/1
   "Successfully renamed parent subnet to 'rig-uip2-twin' and imported 1 child subnets."
```

Database (`bastet_audit_203`):

```
1 | rig-uip2-twin      | 10.20.0.0/16 | parent=- | arm=.../virtualNetworks/rig-uip2-twin
2 | rig-snet-alpha-web | 10.20.1.0/24 | parent=1 | arm=.../virtualNetworks/rig-vnet-alpha/subnets/rig-snet-alpha-web
```

All three candidate correctors checked and silent: `POST /Azure/ReconcileScan` on the corrupted tree
returned `items:[] reviewItems:[] warnings:[] canCommit:false` (both ARM ids exist, so the app's own
consistency checker says nothing); `GET /Azure/Import/1` now 302s away with *"Subnet must not have any
child subnets or host IP assignments"*; a crafted relink POST was refused by G1
(`SubnetController.Azure.cs:338-350`).

Window width, measured to bound severity: with a 1.5 s human-paced pause between the two clicks,
**0 hits in 6** — `GetSubnets` answers in ~200 ms here, so the window is roughly the latency
difference. `AzureService.GetCompatibleSubnets` still issues `vnetResource.Get()` **plus** a full
`GetSubnets()` enumeration per VNet (the shape G3 deliberately left), so a VNet with hundreds of
subnets, or a 429 with SDK retry backoff, puts it in multi-second territory.

**Fix.** *The finder's fix was corrected as incomplete: it guarded `success` only.* The `error`
handler (`:323-326`) and `complete` (`:327-329`) stay unguarded, so a superseded request that fails at
the transport paints *"Error connecting to server: …"* over the current VNet's correctly rendered
rows, and its `complete` hides `#subnet-loading` while the current request is still in flight.

```js
let subnetSeq = 0;
function loadSubnets(vnetResourceId, vnetName) {
    const seq = ++subnetSeq;
    $.ajax({
        url: '@Url.Action("GetSubnets", "Azure")',
        type: "GET",
        data: { vnetResourceId: vnetResourceId, subnetId: @Model.SubnetId },
        dataType: "json",
        beforeSend: function () { /* unchanged, incl. G10's reset */ },
        success: function (result) {
            if (seq !== subnetSeq) { return; }          // superseded - drop it
            $("#vnet-name").val(vnetName);              // identity comes from the response
            $("#vnet-resource-id").val(vnetResourceId); // that populated the rows
            /* ...existing body... */
        },
        error: function (xhr, status, error) { if (seq !== subnetSeq) { return; } /* ...existing... */ },
        complete: function () { if (seq !== subnetSeq) { return; } $("#subnet-loading").hide(); }
    });
}
```

with `:63` becoming `loadSubnets(selectedVNetId, selectedVNetName)`. `:61-62` may stay — `beforeSend`
hides `#subnet-selection`, so the form is unreachable between the click and the accepted response;
what fixes the defect is the **accepted response** writing the identity fields.

**Cheaper interim:** retain the jqXHR and `.abort()` the previous one **at the very top of
`loadSubnets`, before the new `$.ajax` call**. Verified against the pinned jQuery 4.0.0: the aborted
request's handlers are dispatched synchronously inside `.abort()`
(`['--about to call abort()--','error(abort)','complete','--abort() returned--']`), so the new
request's `beforeSend` undoes them in the same task and nothing is painted. Placing the abort inside
`beforeSend` instead reverses the order and leaves the red banner up — the interim is only correct as
written, or with `if (status === "abort") { return; }` as the first line of `error`.

**Optional defence-in-depth (not the finder's, and not the round-6 F1 check that was declined):** when
`vnetResourceId` is supplied, require every child `AzureResourceId` to start with
`vnetResourceId + "/subnets/"` (`OrdinalIgnoreCase`) and reject otherwise. Pure string test, no ARM
round trip. It must be conditional on `vnetResourceId` being non-empty, because the documented plain
JSON caller may post `AzureResourceId` without one.

---

## H3 [x1] — Bulk Azure import commits a plan the operator never saw: the preview pane renders the last response to arrive, Confirm posts the last selection clicked

**Citation.** `src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:447` (`renderPlan(result.plan)`
in `loadPreview`'s success handler), with `:387` (`lastSelection = selection`, set synchronously on
the click) and `:543` (`$("#bulk-go-commit-btn").prop("disabled", !plan.canCommit)`).

**Confidence.** Confirmed. `grep -n "abort\|seq"` over the whole 632-line file returns nothing: no
sequence token, no retained jqXHR, no `.abort()`. `git log` on the file shows no guard was ever
removed.

**Scenario.** An admin ticks VNet prefix `10.40.0.0/16` (`rig-vnet-gamma`) and presses *Next:
Preview*. The preview is slow. Rather than wait they click the **step-2 pill** — which stays enabled
during a load (`activateTab` never re-disables a visited pill, and `#bulk-back-to-selection-btn` sits
*inside* `#bulk-preview-content`, which is `d-none` while the spinner runs, so the pill is the only
route back) — untick gamma, tick `10.31.0.0/16` (`rig-vnet-beta`) with both its subnets and the
"rename matched" switch, and press *Next: Preview* again.

The second response renders first. The first response then lands and repaints the pane with the gamma
plan, re-enabling *Continue to Commit* from **gamma's** `canCommit`. The operator approves that
screen. Confirm Import posts `lastSelection`, which is **beta**.
`SubnetController.BulkAzure.cs:60-75` re-plans the *posted* selection, finds it internally consistent,
and commits it. It never sees the rendered plan, so it cannot detect the divergence. Step 4 shows no
recap.

**Wrong output.** Banner: *"Created 0 VNet target(s), 2 child subnet(s), renamed 1 target(s)"*. In
`bastet_audit_204` the operator's hand-made `Prod Core 10.31.0.0/16` was **renamed** to
`rig-vnet-beta` (description still "Hand made by the operator"), **permanently stamped** with
`AzureResourceId`, and two children created under it. `10.40.0.0/16` — the plan actually on screen —
was never created.

**Reproduction.** Real Chromium against the live app on 127.0.0.1:5404. The **only** thing forced is
arrival order: `page.route` on `**/Azure/BulkImportPreview` does `resp = await route.fetch()` (the
live app answers with its own bytes), sleeps 6 s for request #1 only, then
`route.fulfill(response=resp)`. Request #2 passes straight through. No source patched, no body
altered.

```
[t+ 1.53s] CLICK 1: ticked rig-vnet-gamma 10.40.0.0/16, pressing 'Next: Preview'
[t+ 2.62s] clicked the step-2 pill; step2 pane visible = True     (request still in flight)
[t+ 2.79s] CLICK 2: gamma unticked; ticked rig-vnet-beta 10.31.0.0/16 + 2 subnets + rename switch
[t+ 3.08s] SCREEN after response #2: rig-vnet-beta / Exact match "Prod Core" -> rename
[t+ 7.59s] preview RESPONSE #1 delivered (plan for rig-vnet-gamma)
[t+10.64s] SCREEN: "New top-level create rig-vnet-gamma (10.40.0.0/16) / No child subnets selected."
              commit button disabled? False
[t+10.95s] COMMIT. POST body vNetPrefixes = [rig-vnet-beta 10.31.0.0/16 + mgmt + svc]

1 | rig-vnet-beta      | 10.31.0.0/16 | arm=.../virtualNetworks/rig-vnet-beta | desc=Hand made by the operator
2 | rig-snet-beta-mgmt | 10.31.1.0/24 | parent=1
3 | rig-snet-beta-svc  | 10.31.2.0/24 | parent=1
```

The ordering premise was then measured **with nothing forced at all**. `/Azure/BulkImportPreview` is
`GetExistingSubnetsAsync()` plus several O(existing) passes per selected prefix, so latency scales
with `existing x selected` — 39 ms at 20 000 subnets / 1 prefix, **7 247 ms** at 200 000 / 600. Two
ordinary requests, heavy preview first, light preview `GAP` seconds later:

```
gap=1.0s -> light finished t+1.24s ; heavy finished t+5.15s   INVERTED=True
gap=2.0s -> light finished t+2.24s ; heavy finished t+6.69s   INVERTED=True
gap=3.0s -> light finished t+3.23s ; heavy finished t+5.92s   INVERTED=True
```

That is literally the "select all, too much, go back and pick one" flow. Separately, 310 concurrent
preview pairs on a warm small deployment completed **102 out of order**: the application imposes no
ordering whatsoever. On a small tree the precondition is a transient stall on request #1 — pool wait
(G5 measured 15 s pool timeouts in this app), a cold load-balanced replica, a DB failover — which is
exactly what makes an operator abandon the spinner, so the two conditions are correlated.

Contrast, which is what makes this a defect rather than house style: the reconcile wizard sets
`lastPlan = plan` **inside** `renderPlan` (`_ReconcileScripts.cshtml:205-206`), so its screen and its
payload always come from one response.

**Fix.** *The finder's fix was corrected as incomplete (it guarded `success` only) and its interim was
corrected as placement-dependent.* A stale `error` is reachable today and paints *"Error connecting to
server:"* over a current, valid plan — measured: `after response #2 rendered a valid plan:
content=True errorPanel=False` then `after stale request #1 FAILED: content=True errorPanel=True`. A
stale `complete` hides the spinner of a request still in flight (jQuery always runs `complete`).

```js
let previewSeq = 0;
function loadPreview(selection) {
    const seq = ++previewSeq;
    $.ajax({
        /* ... unchanged ... */
        success:  function (result) { if (seq !== previewSeq) { return; } /* unchanged */ },
        error:    function (xhr, status, error) { if (seq !== previewSeq) { return; } /* unchanged */ },
        complete: function ()       { if (seq !== previewSeq) { return; }
                                      $("#bulk-preview-loading").addClass("d-none"); }
    });
}
```

**Cheaper interim:** retain the jqXHR and `.abort()` it **at the top of `loadPreview`, before the new
`$.ajax` call**, plus `if (status === "abort") { return; }` as the first line of `error`. The abort
alone is only correct at that exact placement — measured with the abort reached after the new
`beforeSend`, jQuery 4.0.0 fired `error(status=abort)` then `complete` and left
`errorText='Error connecting to server: abort'` on screen. The `status === "abort"` line makes the
interim placement-independent and costs one line.

---

# Low

## H4 [x1] — "Purge All" ignores the scope its own confirmation page states: the POST round-trips nothing and deletes whatever exists at execution time

**Citation.** `src/Bastet/Controllers/SubnetController.Delete.cs:299` (`ExecuteDeleteAsync`), with the
GET at `:275-284` and the view text at `Views/Subnet/PurgeAllDeletedSubnets.cshtml:17`. Twin:
`src/Bastet/Controllers/HostIpController.cs:617`, GET at `:593-602`.

**Confidence.** Confirmed — for the reframed defect. *The finding as filed blamed the missing global
subnet lock; that mechanism is refuted (see the fix, and the reproduction below with zero
concurrency). The defect does not depend on it.*

**Scenario.** The POST's whole signature is `PurgeAllDeletedSubnetsConfirmed(string confirmation)`.
Nothing from the GET is round-tripped, so the delete is unbounded, while the page the operator typed
`approved` into says *"You are about to permanently delete **1** archived subnet record(s). After
purging, these records will be gone forever — there is no recovery."*

**One admin, one session, two tabs, zero concurrency.** Tab 1 opens
`/Subnet/PurgeAllDeletedSubnets`, which renders `permanently delete <strong>1</strong> archived subnet
record(s)`. In tab 2 the same admin (or a Delete-role colleague) deletes `10.50.0.0/16` and its 10
children, archiving 11 more rows. **Five seconds later** tab 1 submits the form it already had open:
12 archive records destroyed, banner *"Permanently purged 12 deleted subnet record(s)"*, and the
deleting tab's own success banner promised an archive that no longer exists.

**Reproduction.** HEAD build, SQL Server 2022, catalogs `bastet_audit_207` / `bastet_audit_607`.
Sequential, no overlap at all:

```
BEFORE:                Subnets=11 DeletedSubnets=1
tab-1 page said:       permanently delete <strong>1</strong> archived subnet record(s)
tab-2 delete HTTP=302  Subnets=0  DeletedSubnets=12
(sleep 5)
tab-1 purge  HTTP=302  banner: Permanently purged 12 deleted subnet record(s)
FINAL:                 Subnets=0  DeletedSubnets=0
```

At 900 children the page said `1` and 902 rows were destroyed. Host-IP twin, same shape: page said
`permanently delete 1 archived host IP record(s)`, another tab archived 4, five seconds later the
stale form reported *"Permanently purged 5 deleted host IP record(s)"*.

Second, concrete consequence: `HostIpController.cs:522` loads `DeletedSubnets` so
`/HostIp/AllDeletedHostIps` can name the subnet each archived IP belonged to. Purging the subnet
archive blinds the host-IP archive:

```
before purge:  3 rows render "net-b (deleted)"  10.61.0.0  /24
after purge:   3 rows render "Unknown"          "Unknown"  cidr 0
```

No live data is touched, no corruption occurs (`DeletedSubnets` after each of eight runs was only ever
0, 901 or 902 — never partial), and both sides are admin-gated; hence **low**. It is not info, because
data really is destroyed unrecoverably (there is no restore path anywhere in the app, so the archive
is the only record that a subnet existed, who deleted it and when) and the app itself already gates
the same operation on the same count one HTTP request earlier — `GET` with an empty archive 302s away
with *"There are no deleted subnet records to purge."*

**Fix.** *The finder's fix — wrapping both POSTs in `ExecuteWithSubnetLockAsync` — was built, run and
measured **unsound**, independently by both verifiers. Do not add the lock.* It prevents nothing (the
loss above needs no concurrency) and it converts the one ordering HEAD handles **correctly** into
total loss:

```
(HEAD,         purge at +109ms)  waited   12ms -> DeletedSubnets=901  B: "purged 1"
(PATCHED-LOCK, purge at +108ms)  waited 1185ms -> DeletedSubnets=0    B: "purged 902"
```

A purge issued before the archive inserts finishes first and takes exactly the records the page
promised; the lock parks it behind the entire delete.

Bound the delete to the set the operator was shown — the finder's own cheaper interim, promoted to the
fix:

- `Models/ViewModels/PurgeAllViewModels.cs`: add `public int MaxId { get; set; }` to both view models.
- GET: `int maxId = await context.DeletedSubnets.MaxAsync(d => (int?)d.Id) ?? 0;` into the model.
- View: `<input type="hidden" name="confirmedMaxId" value="@Model.MaxId" />`.
- POST: `PurgeAllDeletedSubnetsConfirmed(string confirmation, int? confirmedMaxId)` →
  `context.DeletedSubnets.Where(d => d.Id <= confirmedMaxId).ExecuteDeleteAsync()`. Same for
  `DeletedHostIpAssignments`.
- Bind it as `int?` and re-render the confirmation when it is null or `<= 0` — otherwise a POST
  without the field silently reports *"Permanently purged 0 deleted subnet record(s)"* and does
  nothing. (One verifier hit this by accident.)

Soundness of the bound: production is SQL Server only (`Program.cs:72` and `:79` are both
`UseSqlServer`), `Id` is `IDENTITY`, and `DELETE` — unlike `TRUNCATE` — never reseeds it, so any row
archived after the GET necessarily has `Id > MaxId`. Measured on the patched build:

```
purge at +108ms  confirmedMaxId=10825  waited 22ms -> DeletedSubnets=901  B: "purged 1"
purge at +458ms  confirmedMaxId=11727  waited 13ms -> DeletedSubnets=901  B: "purged 1"
sequential       confirmedMaxId=12629             -> DeletedSubnets=901  B: "purged 1"
```

Free side effect visible in those timings: the bounded `DELETE` is a clustered-index seek that never
touches the uncommitted tail rows, so the purge stops blocking (13-22 ms versus 285-696 ms).

**Cheaper interim, no schema or view-model change, and it keeps "Purge All" meaning all:** post the
rendered `Count` back as a hidden field and refuse when
`await context.DeletedSubnets.CountAsync() != confirmedCount`, redirecting to the GET with *"the
archive changed since this page was rendered — review and confirm again."* Rows are only ever **added**
to these two tables outside the purge itself, so count equality is a reliable optimistic check.

`grep -rn "PurgeAll" test/` returns 0 hits, so there is no test fallout. **Caveat for whoever writes
the regression test:** the suite is SQLite, where a plain `INTEGER PRIMARY KEY` reuses the highest
rowid after the max row is deleted, so a SQLite test must not assume monotonicity across a purge —
assert on rows inserted while the archive is non-empty.

---

## H5 [x1] — Migration bootstrap misreads SQL 4060: a database that exists but cannot be opened is treated as missing, so startup aborts with two successively wrong diagnostics

**Citation.** `src/Bastet/Program.cs:305` (block `:305-326`); the consequence lands at `:371`
(`dbContext.Database.Migrate()`).

**Confidence.** Confirmed. The one step the finder inferred — that the `:305` branch is what puts the
lock connection on `master` — was measured directly, twice and by two different methods.

**Scenario.** SQL Server raises error 4060 with **byte-identical text** for three different
conditions: the catalog does not exist, the login has no user inside an existing catalog, and the
catalog is offline. `Program.cs:305` treats 4060 as the bootstrap path unconditionally, contradicting
its own comment at `:307-309` (*"the catalog is not there yet, so this is the bootstrap path"*).

With `BASTET_AUTO_MIGRATE=true`, the lock connection falls back to `master` — which **always succeeds
on a stock SQL Server**, because `guest` holds `CONNECT` there — so the crafted
`InvalidOperationException` at `:319-324` never fires, and `Migrate()` at `:371` issues
`CREATE DATABASE [<catalog>]` against a database that already holds the operator's data. Startup
aborts (exit 134) with `SqlException 262 "CREATE DATABASE permission denied in database 'master'"`.
Acting on that message's own advice (`ALTER SERVER ROLE dbcreator ADD MEMBER`) produces
`SqlException 1801 "Database '<catalog>' already exists. Choose a different database name."` — whose
literal advice would abandon the production catalog. Neither message names the cause. The **same
deployment with `BASTET_AUTO_MIGRATE=false` reports it accurately**, which is what makes this
Bastet's wrong output rather than SQL Server's.

**Reproduction.** Rig SQL Server 2022, unmodified shipped binary (byte-copy of the reference publish),
`ASPNETCORE_ENVIRONMENT=Production`. Reached from three independent starting states:

*Run A — orphaned database user after a restore or failover*, on a deployment that was serving
`10.44.0.0/16` a minute earlier (README "Database Setup" done correctly, then the login recreated
without `WITH SID`, which is `sp_change_users_login`'s entire reason to exist):

```
PROCESS EXITED rc=134 after 11.06s
fail: Microsoft.EntityFrameworkCore.Database.Command[20102]
      CREATE DATABASE [bastet_audit_606];
Unhandled exception. Microsoft.Data.SqlClient.SqlException: CREATE DATABASE permission denied in database 'master'.
   at Program.<Main>$(String[] args) in /home/anuj/code/Bastet/src/Bastet/Program.cs:line 371
Error Number:262,State:1,Class:14
```

Not one line names the login, the user, or the fact that the database exists and holds the operator's
subnets. *Run B* — follow that advice: `Error Number:1801 … Database 'bastet_audit_606' already
exists.` *Run C — nothing misconfigured at all*, login `sa`, database `SET OFFLINE` for a maintenance
window: same 4060 at login, same `CREATE DATABASE`, same 1801.

*Counterfactual*: identical broken deployment with `BASTET_AUTO_MIGRATE=false` →
`GET /Subnet` 500 with the **accurate** message *"Cannot open database … Login failed for user
'bastet_c7r_app'."* *Control*: `ALTER USER [bastet_c7r_app] WITH LOGIN = [bastet_c7r_app];` and
nothing else → *"Application started"*, `GET /Subnet` 200. The only fault was the missing database
user, which is exactly what neither message named.

*Direct proof the `:305` branch fires* — 30 ms DMV sampling during Run A:

```
APPLOCK 52  master  0:[Bastet:Migration]:(4afee137) X GRANT
SESSION 52  master  login=bastet_c7r_app  prog=Core Microsoft SqlClient Data Provider
```

`Configured()` returns the connection string verbatim, so the only path that yields a `master` lock
connection is `:305 -> :312`. Corroborated by an external hold on `sp_getapplock 'Bastet:Migration'`:
startup took 11.13 s unblocked, **27.83 s** with the lock held **in master**, and 11.14 s with the
same resource held in another catalog.

Damage sweep afterwards: `Bastet tables in master: 0`, no stray database, `Subnets` intact,
`__EFMigrationsHistory: 6`, `APPLICATION locks on the server: 0`. `CREATE DATABASE` on this path can
only end as 262 or 1801 — there is no data-loss path, which is why this is **low**. It is not info:
the code takes an action the operator never asked for against a live catalog, throws away a correct
diagnostic the same deployment produces with auto-migrate off, and its literal advice makes the
operator grant the application account a server-level `dbcreator` right that does not help.

**Fix.** *The finder's fix — `SELECT DB_ID(@catalog)`, treating non-NULL as "exists" — was corrected:
it does not work as written, for two reasons both verifiers measured independently. `DB_ID` only
answers because `VIEW ANY DATABASE` is granted to `public` by default; deny it to the application
login (ordinary hardening) and the probe returns NULL for a database that plainly exists, so the fix
silently no-ops and both wrong messages come back — demonstrated end to end with a build carrying the
`DB_ID` probe. And `DB_ID` returns `smallint`, so the natural `probeResult is int` test never matches.* `HAS_DBACCESS` returns
`int`, keeps answering under `DENY VIEW ANY DATABASE`, and separates the states correctly in every
condition measured (0 = exists but unusable, including offline and including for `sa`; NULL = absent;
1 = healthy, so it does not over-fire).

```csharp
SqlConnection bootstrapConnection;
try
{
    bootstrapConnection = Open(MigrationLockConnectionString.MasterBootstrap(connectionString));
}
catch (SqlException bootstrapException)
{
    throw new InvalidOperationException(/* the existing :319-324 message, unchanged */, bootstrapException);
}

// 4060 is also what a login gets when the catalog EXISTS but cannot be opened - byte-identical text.
// HAS_DBACCESS answers 0 (exists, not usable) / NULL (no such database) and, unlike DB_ID or
// sys.databases, keeps answering when VIEW ANY DATABASE has been revoked from public. It returns
// int, where DB_ID returns smallint. Fail open: if the probe cannot run, keep today's bootstrap path.
string configuredCatalog = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
int? catalogAccess = null;
try
{
    using SqlCommand probe = new("SELECT HAS_DBACCESS(@catalog)", bootstrapConnection);
    probe.Parameters.AddWithValue("@catalog", configuredCatalog);
    catalogAccess = probe.ExecuteScalar() is int access ? access : null;
}
catch (SqlException) { /* probe unavailable - behave exactly as before */ }

if (catalogAccess == 0)
{
    bootstrapConnection.Dispose();
    throw new InvalidOperationException(
        $"The configured database '{configuredCatalog}' exists on this server but could not be opened, "
        + "which SQL Server reports as error 4060 using the same text it uses for a database that does "
        + "not exist. Either the login in BASTET_CONNECTION_STRING has no user inside that database "
        + $"(CREATE USER inside '{configuredCatalog}', then db_owner for BASTET_AUTO_MIGRATE=true - if "
        + "the database was restored or failed over, the user may be orphaned: ALTER USER ... WITH "
        + "LOGIN), or the database is offline or recovering. Do not grant the login permission to "
        + "create databases; it does not need it.");
}

return bootstrapConnection;
```

The message must name **both** causes: `HAS_DBACCESS` is also 0 for an offline database, even for
`sa`, so wording that asserts "this login has no user inside it" would be confidently wrong in Run C.

This form was built in a private worktree, published and run: correct message on the default server,
**same** correct message under `DENY VIEW ANY DATABASE` (where `DB_ID` fails), genuine bootstrap
(`sa`, catalog absent) still creates the database and serves `GET /Subnet` 200, and a genuinely absent
catalog with a login that cannot create databases still gets the original 262 — the probe does not
over-fire. `dotnet build` 0 warnings / 0 errors, `dotnet test` 677 passed.

**Constraint on any fix:** EF Core's `SqlServerDatabaseCreator.Exists()` makes the *same* 4060
misreading independently — that is why `Migrate()` issues `CREATE DATABASE` at all — so a fix at
`:305` must **abort startup**. One that merely logs and continues is overruled by EF three lines
later.

**Cheaper interim:** remember that the `master` fallback fired and wrap `dbContext.Database.Migrate()`
at `:371` so any failure on that path adds *"the configured database '<name>' could not be opened; if
it already exists this login is not a user inside it, or the database is offline — grant access or
bring it online rather than creating the database."* A few lines, no extra round trip, robust to
metadata-visibility settings because it probes nothing. Strictly weaker — it fires only *after* the
pointless `CREATE DATABASE` — but it removes the misdirection toward `dbcreator`.

---

## H6 [x2] — G11's branch reorder makes a cleared CIDR field in the Details Create-Subnet modal report a non-existent overlap

**Citation.** `src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml:142` (the range arm
that fails to catch `NaN`). The wrong text is written at `:154-155`; the comment that states the
opposite of what the code does is at `:145-146`.

**Confidence.** Confirmed.

**Scenario.** Open `/Subnet/Details/1` for a parent `10.0.0.0/16` with one child `10.0.0.0/18`, click
the Unallocated Ranges "Create Subnet" button (network `10.0.64.0`, prefilled CIDR 18), then **clear
the CIDR box** — or type any non-numeric text into it. `#cidrInput` is `<input type="number">`, so the
element value is `''` and `parseInt('')` is `NaN`. `NaN < minCidr` and `NaN > maxCidr` are both false,
so the range arm at `:142` is skipped and control falls to the `else` at `:151`, which since `ff285cf`
is the **overlap** arm.

Actual wrong output, observed: `#cidrValidationFeedback` = *"This CIDR would create a subnet that
overlaps with existing subnets."* with computed `display: block` (visible, not merely in the DOM), and
`#subnetSizeDisplay` = *"Invalid - Would overlap"* — while the page's own `wouldOverlap` is false. The
same output appears on `/Subnet/Details/2`, a subnet with **no children at all**: the page asserts an
overlap with a set that is empty.

Before `ff285cf` the same input produced *"Please enter a valid CIDR value within the allowed range."*
/ *"Invalid"*. G11's reorder moved the false sentence off the out-of-range input, which it correctly
fixed, and onto the empty field.

**Reproduction.** Playwright/Chromium driving the published `ff285cf` build on 127.0.0.1:5402 against
SQL Server catalog `bastet_audit_202`, fixtures created through the app's own Create form. Real
keystrokes (`click`, `Control+a`, `Delete`) on the shipped button:

```
== A. modal just opened ==   rawValue "18"  classes "form-control"  sizeDisplay "16,382"  btn enabled
== B. field CLEARED ==       rawValue ""    parseInt "NaN"
                             classes  "form-control is-invalid"
                             feedback "This CIDR would create a subnet that overlaps with existing subnets."
                             feedbackDisplay "block"   feedbackVisible true
                             sizeDisplay "Invalid - Would overlap"   createBtnDisabled true
== C. typed 5 (minCidr 17) == feedback "Please enter a valid CIDR value within the allowed range."
== D. typed 18 ==             classes "form-control is-valid"  sizeDisplay "16,382"  btn enabled
page errors: []
```

The same scripts against a `6a1fe75` worktree published separately: `== B. field CLEARED ==` gave
*"Please enter a valid CIDR value within the allowed range." / "Invalid"*, and `== C. typed 5 ==` gave
the overlap sentence. The builds swap exactly. That run also settles the one value not directly
readable at HEAD: on `6a1fe75` the cleared field does **not** take `else if (wouldOverlap)`, proving
`wouldOverlap === false` for the `NaN` input — so the overlap claim at HEAD contradicts the page's own
overlap computation.

A 46-input census (`''`, `0..40`, `abc`, `-4`, `1e2`, `18.5`) across three topologies (1 child, 0
children, 3 children fragmented) shows the overlap arm is now reached by **exactly two inputs on every
topology** — `''` and `abc` (an `<input type="number">` reports `value === ''` for non-numeric text, so
typing letters is a second trigger) — and by no numeric input at all:

```
Details/1  OVERLAP-ARM 2 inputs: '' and 'abc'   RANGE-ARM 27   SUCCESS-ARM 17
Details/2  OVERLAP-ARM 2 inputs: '' and 'abc'   RANGE-ARM 30   SUCCESS-ARM 14
Details/3  OVERLAP-ARM 2 inputs: '' and 'abc'   RANGE-ARM 27   SUCCESS-ARM 17
```

Nothing is written and `#createSubnetBtn` is correctly disabled in both arms, hence **low**. It is not
info: it is a false factual assertion, visible, on the main path an operator uses to carve a child out
of an unallocated range — the identical class of defect G11 itself was accepted for in round 7.

**Fix.** Verified **sound** as filed, applied and measured in a worktree:

```diff
-            } else if (cidrValue < minCidr || cidrValue > maxCidr) {
+            } else if (isNaN(cidrValue) || cidrValue < minCidr || cidrValue > maxCidr) {
                 // Range before overlap: a CIDR that is both out of range and overlapping was
                 // reported as an overlap, which is not the objection the operator can act on.
-                // A cleared field still lands here - parseInt gives NaN, every comparison is
-                // false - and still gets the generic message, as before.
+                // isNaN is tested explicitly because a cleared field parses to NaN and every
+                // NaN comparison is false, so the range test alone would drop it through to the
+                // overlap arm below - claiming a collision with a sibling that need not exist.
```

Measured on the fixed build: cleared field on `/Subnet/Details/1` and `/2` gives *"Please enter a valid
CIDR value within the allowed range." / "Invalid"*, button disabled. Census, arm by arm:
`SUCCESS-ARM 17/14/17` (identical to HEAD), `RANGE-ARM 29/32/29` (HEAD 27/30/27, `+2` = exactly the two
`NaN` inputs), `OVERLAP-ARM 0` (HEAD 2). **No numeric path moves**, as it must not, since `isNaN(x)` is
false for every value `parseInt` can return other than `NaN`. `dotnet build --no-incremental` 0
warnings / 0 errors; `dotnet test` 677 passed.

The comment rewrite is required, and is not quite what the finder proposed: after the fix the
comment's *conclusion* ("a cleared field lands here") becomes true, but its stated *mechanism* ("every
comparison is false", offered as the reason it lands here) remains exactly backwards — that is why it
did **not** land here. Reword as above rather than deleting.

**No cheaper interim exists** — this one-token change is already the floor. Note for whoever applies
it: `:151-155` is already unreachable for every numeric input at HEAD, so the fix removes the arm's
only visitor. **Do not "tidy" the arm away**; it is defence-in-depth for a case `findOptimalCidr`
currently makes impossible.

---

# Info

## H7 [x2] — Bulk import's commit-success banner never reads `linkedTargets`, so a link-only import reports every count as zero

**Citation.** `src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:588`.

**Confidence.** Confirmed.

**Scenario.** Bastet holds a hand-made subnet `Prod Core 10.20.0.0/16`, no `AzureResourceId`, no
children. An admin ticks only the VNet prefix `10.20.0.0/16` in the bulk wizard (badge *"Will update
existing"*), selects no child subnets, leaves rename off, and commits. The server stamps the VNet's
ARM id onto `Prod Core` and answers
`{"createdTargets":0,"createdChildSubnets":0,"renamedTargets":0,"linkedTargets":1,"fullyAllocatedTargets":0}`.

`:588` renders *"Bulk import completed. Created 0 VNet target(s), 0 child subnet(s), renamed 0
target(s), marked 0 target(s) as fully allocated."* — four counters, `linkedTargets` absent.
`grep -rn "linkedTargets" src/Bastet/Views src/Bastet/wwwroot test/` returns **no hits**. G1 added the
counter to the JSON response and to the TempData banner, but `:588` is original wizard code
(`73fc76f2`) that G1 never touched.

**The sharpest form of the wrong output:** re-committing the identical selection now writes nothing
and returns `linkedTargets: 0` — and `:588` renders **byte-identical text**. The completion screen
cannot distinguish *"I just stamped an ARM id onto Prod Core"* from *"I did absolutely nothing"*.

**Reproduction.** Headless Chromium against the live app, port 5401, catalog `bastet_audit_201`. Built
by clicking, not by crafting a post:

```
TICKING: bulk-prefix-3-0  10.20.0.0/16  "Will update existing"
CHECKED SUBNET BOXES AFTER TICKING PREFIX: []
STEP 3 PREVIEW: Exact match Bastet subnet "Prod Core" (10.20.0.0/16) / No child subnets selected.
COMMIT BUTTON DISABLED: False
COMMIT BANNER (t+0.11s):
 ' Bulk import completed. Created 0 VNet target(s), 0 child subnet(s), renamed 0 target(s), marked 0 target(s) as fully allocated.'
COMMIT RESPONSE: {"success":true,...,"createdTargets":0,"createdChildSubnets":0,"renamedTargets":0,"linkedTargets":1,"fullyAllocatedTargets":0}
CONSOLE: (empty)
```

The write landed — `sqlcmd` shows `Prod Core` carrying
`azid=.../virtualNetworks/rig-regressp2-dup`. Re-posting the same selection returned all counters zero
and produced identical banner text.

**Why info and not low:** `:592-594` schedules an **unconditional** `window.location.href =
result.redirectUrl` on a 2000 ms timer, and `TempData["SuccessMessage"]`
(`SubnetController.BulkAzure.cs:324-329`) carries the complete sentence. Measured: banner at t+0.11 s,
correct sentence on `/Subnet` at t+2.17 s:

```
POST-REDIRECT alert-success: "Bulk import succeeded: created 0 VNet target subnet(s), created 0 Azure
child subnet(s), renamed 0 target(s), linked 1 existing target(s) to Azure, and marked 0 target(s) as
fully allocated."
```

The only plausible reaction to the wrong banner ("it did nothing, do it again") is a tested no-op: 200,
all counters zero, no second write, no G1 conflict.

**Fix.** One line, rendering verified in the pinned Chromium:

```js
` Created ${result.createdTargets} VNet target(s), ${result.createdChildSubnets} child subnet(s), renamed ${result.renamedTargets} target(s), linked ${result.linkedTargets} existing target(s) to Azure, marked ${result.fullyAllocatedTargets} target(s) as fully allocated.`
```

Counter order then matches `TempData["SuccessMessage"]` exactly, so the two summaries of one commit
agree. `linkedTargets` is always present on the success path
(`SubnetController.BulkAzure.cs:338`). No cheaper interim exists — one line is already the floor. It
touches an inline template literal only, so plain-HTTP and air-gapped hosting are unaffected.

**Do not bundle** the finder's optional extra (naming the pending link in the preview's `ExactMatch`
branch): step 2 already says *"Will import into existing Bastet subnet 'Prod Core'."* under a "Will
update existing" badge, so the decision point is not uninformed. Separate call.

---

# Refuted

| Candidate | What it claimed | Why it was killed |
|---|---|---|
| **`BatchCreateChildSubnets` renames and Azure-links a target subnet that already has child subnets** — `src/Bastet/Controllers/SubnetController.Azure.cs:329`, filed low, `[x1]` | That the "target must have no children" precondition is enforced by the wizard entry gate (`AzureController.cs:41-46`) and by the bulk planner (`AzureBulkImportPlanner.cs:199`, `:382`) but not by the write, making it an asymmetry rather than a policy choice; and that the operator's chosen name is destroyed with no undo. | **Not reproduced as a defect — the mechanism reproduces byte for byte but the resulting state is harmless, and the load-bearing premise is factually false.** Both verifiers killed it independently, one by curl and one by driving the shipped wizard in real Chromium. **(1)** The end state is one Bastet reaches through fully sanctioned operations in a different order: importing into an *empty* subnet and then creating the manual child afterwards was offered and accepted at every step and produces rows structurally identical in every column. `ValidateSubnetCreation` (`SubnetController.Helpers.cs:114-285`) does not test `AzureResourceId`, nor does `SubnetDetailsViewModel.CanAddChildSubnet` (`Models/ViewModels/SubnetViewModels.cs:114`) — a manual child under an Azure-linked parent is a supported operation, created live. There is no invariant to restore. **(2)** Both corroborating signals fire on *every* successful import: `GET /Azure/Import/{id}` on a legitimately imported subnet answers the identical *"Subnet must not have any child subnets or host IP assignments"*, and `BulkGetVNets` annotates it `Blocked` with the identical *"…already has child subnets. Already imported?"* — wording that describes a completed import. These are entry gates, not row invariants. The rename harm is likewise not caused by the missing check: it is the endpoint's documented purpose (`SubnetController.Azure.cs:104-110`) and cost the sanctioned import its name identically. **(3)** The "asymmetry" premise is **false**: with the other legitimate wizard shape — a `FullyEncompassesVNetPrefix` entry — a fully-allocated parent, which `GET /Azure/Import/{id}` refuses, **is** renamed and Azure-linked. The write path enforces *none* of the three entry preconditions; two merely coincide with `ValidateSubnetCreation`'s universal child-creation rules under one of the two batch shapes. Additionally, the one parent write that would produce a genuinely contradictory row (`IsFullyAllocated=true` beside children) is already guarded at `:314`, and a colliding batch rolls back completely, so the realistic stale-form race fails closed. **Fix also unsound:** the proposed blanket `AnyAsync(s => s.ParentSubnetId == parentId)` refusal sits *before* the `isAzureImport` branch, so it would break the plain non-Azure JSON batch-create API documented at `:104-110`, whose ordinary case is adding children to a parent that already has some. This lands squarely in the shape that has died five rounds running: the wrong output exists only if a render-time affordance is read as a row invariant, and the state produced is otherwise permitted. One pass reported it; the pass that did not find it was right. |

---

# Watch list

Not findings. Known, accepted or deferred. Knowing these stops a later round filing something already
understood — and several are the *reason* a nearby defect is worth more than it looks.

## Accepted and still open — never re-raised, at any severity

1. **ForwardedHeaders trust-all with `AllowedHosts: "*"`** (`Program.cs:260-267`).
2. **The Development-only `DevAuthHandler` bypass** (`Program.cs:177-189`).
3. **`GlobalSanitizationFilter` skipping nested `System.*` collections.**
4. **`CollectDescendants` lacking a cycle guard** (`SubnetController.Helpers.cs:92`).
5. **The unreachable IP-change branch in `ValidateHostIpUpdate`.**
6. **The blind `catch {}` around the DataProtectionKeys probe** (`Program.cs:105-109`).
7. **C20 — the Azure reconcile check/act window**, documented in-file at
   `SubnetController.AzureReconcile.cs:98-110`. H1 is **not** this: in both H1 reproductions the
   archived subtree matched `DescendantSubnetIds` exactly, so C20's own stated closure would not catch
   it.

## Carried forward from rounds 4-7

- **The unreachable IP-change branch in `ValidateHostIpUpdate` is the one place applying the
  network/broadcast reservations without the `cidr < 31` guard** the other two sites carry. A trap for
  whoever makes that field editable.
- **`GlobalSanitizationFilter` runs after model binding and validation.** Any new `[Sanitize*]`
  attribute needs a matching validator.
- **`MockAzureService.DefaultConfirmation` is `Deleted`.** Any test touching the confirmation path must
  set the verdict explicitly.
- **Still no `WebApplicationFactory`, no integration host, no JS test harness.** Most of this round's
  findings are in that category. **"There is no test for this" is not a finding** — that shape has been
  refuted in four consecutive rounds.
- **Migration `.Designer.cs` snapshots contain old column widths on purpose.** Correct and frozen.
- **A real Azure tenant ID is committed** at `Properties/launchSettings.json:41`. Not a credential.
- **The equality-vs-membership prefix check on the VNet-resource-id stamp in
  `SubnetController.Azure.cs`** — deliberately not implemented in round 6 (it needs an ARM read inside
  a transactional write). H2's optional server-side hardening is a *string* test, not this.
- **The bulk import reads only a multi-prefix Azure subnet's first prefix.** Closing it means creating
  several Bastet subnets from one Azure subnet — a feature change. The prefix list is already carried
  on the inventory view model.
- **`findOptimalCidr`'s loop bound**, the `site.js` consolidation of the CIDR→mask copies, and the
  per-prefix "already imported" sentence — all deliberately left.
- **`AnnotatePrefix` cannot return `AlreadyImported`** — established over 4,046 brute-forced planner
  outcomes.
- **The usable-IP calculation's three copies agree at every CIDR 0-32.** The drift is in the CIDR→mask
  copies: six across four files, two fixed by F16.
- **Three cheap test gaps, each with a free fix**: a `SubnetDeleted` case for `IsAbsenceStatus`; E4's
  five call-site orderings; E9's `Count > 1` boundary. **Watch-list items, not findings.**
- **`DeletedSubnets` does not archive `AzureResourceId` or `IsFullyAllocated`**, the deleted-subnets
  table renders neither `Tags` nor `OriginalParentId`, and there is **no restore path anywhere in the
  app**. Confirmed again this round from the live schema. **H1, H2, H3 and H4 all depend on this** — it
  is what makes each of them unrecoverable.
- **`AZURE_TOKEN_CREDENTIALS=dev`, which the launch profiles set, excludes `EnvironmentCredential`.**
- **`success` is not uniform across the Azure AJAX endpoints.** `/Azure/BulkGetVNets` reports an Azure
  read failure as `success:false`; `/Azure/ReconcileScan` reports the same failure as `success:true`
  with the reason inside the plan. Both conventions coexist deliberately.
- **`pkill -f "Bastet.dll"` kills every instance on the box.** Match on `ASPNETCORE_URLS` or a PID.
- **Headless Chromium never ticks `requestAnimationFrame`**, so jQuery's fx queue never drains and
  every animation assertion is a false pass unless `window.requestAnimationFrame` is deleted first.
- **Three CodeQL log-forging alerts are open on `main` and are expected to stay open.** True positives
  resolved by a mechanism CodeQL cannot see (the sanitizing console formatter).
- **F15 / the migration lock.** The lock opens the configured catalog first and falls back to `master`
  only on SQL 4060. **Do not propose re-applying an unconditional `master` scope.** The documented
  mid-bootstrap mixed-scope window is accepted. H5 narrows the *fallback*, it does not widen the scope.
- **ARM ids are path-based and survive delete-and-recreate.** Measured twice; the "but recreate breaks
  it" objection to G1's fix is empirically false.
- **`GetVNetInventory` was 1+N by construction** — G3 removed the per-VNet call.
  `GetCompatibleSubnets` still has the 1-call shape by design, and H2 records what that costs in
  latency.
- **The queued-writer connection-pool amplification (G5) is closed by the gate, but `Max Pool Size` is
  still unset anywhere in the repo, README or appsettings.**
- **`Logging__LogLevel__*` outranks every `BASTET_LOG_LEVEL_*` knob.** Measured; the README says so.
- **`SaveTokens = true` has no scope gate.** Why G7 needed `OnTicketReceived` and not just the scope
  deletion.
- **The DataProtection key ring is persisted unencrypted in the application database** (accepted;
  `Program.cs:115-120`, confirmed by the startup warning *"No XML encryptor configured"*).
- **`HostIpController.DeletedHostIps(int subnetId)` does exist**, on route
  `HostIp/DeletedHostIps/{subnetId}`; without an id it binds `subnetId = 0` and returns `NotFound()`.
  Round 7's note to the contrary was wrong, and correcting it is documentation, not a finding.
- **Round-7 line-number citations have already moved**, and round-8's will move again. Re-derive every
  line before citing it.
- **Still open, deliberately (carried from round 7's sweep):** eleven controller sites `RedirectToAction`
  to `/Error/{code}` rather than answering the status at the requested URL, so the original URL is
  replaced and the client pays a round trip. Answering in place would touch five controllers. Not
  filed this round either.

## New in round 8

- **Entry gates are not row invariants.** A `Blocked` bulk-planner row, a refused
  `GET /Azure/Import/{id}` (*"Subnet must not have any child subnets or host IP assignments"*), and a
  hidden Import-from-Azure button are all **expected** on any correctly imported subnet, because every
  single-VNet import creates children under its target. Do not read any of them as evidence of
  corruption. This is what killed the one refuted candidate.
- **The single-VNet import write path enforces none of the three wizard-entry preconditions directly.**
  Not just "already has children": a **fully allocated** target is also renamed and Azure-linked when
  the batch carries a `FullyEncompassesVNetPrefix` entry, reproduced live. The host-IP and
  fully-allocated refusals for an *ordinary* batch come from `ValidateSubnetCreation` acting on the
  children being created, not from any test of the target. Any future round re-deriving an "asymmetry"
  here should stop at this bullet.
- **EF Core's `SqlServerDatabaseCreator.Exists()` misreads SQL 4060 the same way `Program.cs:305`
  does.** Any fix at `:305` must abort startup; one that logs and continues is overruled at `:371`.
- **`Program.cs:319-324`'s crafted `InvalidOperationException` is effectively unreachable on SQL
  Server**, because `guest` holds `CONNECT` in `master` and cannot be disabled there. It fires only for
  an Azure SQL contained user with no login in `master` — where its advice is at least right.
- **The purge POST does not require the confirmation page at all.** Antiforgery tokens are per-session,
  so a token harvested from `/Subnet/Create` drives `/Subnet/PurgeAllDeletedSubnets` to completion. The
  ceremony is neither scoping nor gating. (H4 fixes the scoping half; the rest is by design.)
- **Round 6's clean bill already recorded the purge lock gap** (*"the only unguarded writes are archive
  table purges"*) **and left it — correctly.** Adding `ExecuteWithSubnetLockAsync` there was built and
  measured to make one currently-correct ordering wrong. Do not re-file the lock gap.
- **jQuery 4.0.0 dispatches an aborted request's `error` and `complete` handlers synchronously inside
  `.abort()`.** Measured on the live pages. Any `.abort()`-based staleness interim in this codebase is
  therefore **placement-sensitive**: called before the new `$.ajax`, the new `beforeSend` undoes the
  paint and nothing shows; called from inside `beforeSend`, it leaves *"Error connecting to server:
  abort"* on screen. Add `if (status === "abort") { return; }` to be placement-independent.
- **The same click-time-versus-response-time split exists in `loadVNets`** —
  `_BulkScripts.cshtml:104-105` (set on the click) versus `:137` (set from the response). Not filed:
  the rendered tree and the `vnets` array come from the same response, so only the subscription
  **label** can disagree, and the planner merely copies `SubscriptionId` onto the plan view model.
- **`/Azure/BulkImportPreview` latency scales with `existing x selected`**, measured at roughly
  0.06 ms per (selected prefix x 1 000 existing subnets): 39 ms at 20 000 subnets / 1 prefix,
  **7 247 ms** at 200 000 / 600. Relevant to H3, and to anyone sizing a large deployment.
- **`_StepConfirm.cshtml` has no warnings block.** `#rec-scan-warnings` exists only in
  `_StepReview.cshtml:23`, so any scan warning — including *"withheld from deletion"* — never reaches
  the screen that performs the archive. Worth fixing alongside H1 regardless of which H1 interim is
  taken.
- **After H6's fix, `_SubnetCalculationScripts.cshtml:151-155` has no remaining visitor**, because no
  numeric input reaches it (46-input census across three topologies). It is defence-in-depth for a case
  `findOptimalCidr` currently makes impossible — **do not tidy it away**.
- **Rig hazard, not a product defect:** three VNets in `bastet-visible` now share the prefix
  `10.20.0.0/16` (`rig-vnet-alpha`, `rig-uip2-twin`, `rig-regressp2-dup`) because sibling agents
  created fixtures. Convenient for H2, but a later round reading `10.20.0.0/16` in a transcript should
  not assume it means `rig-vnet-alpha`.
