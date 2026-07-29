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

_H2 is fixed and committed. `loadSubnets` now takes a sequence number from a module-level
`subnetSeq`, and **all three** jQuery callbacks return early when they are not the newest request:
`success` so a superseded response cannot repaint rows under the current VNet's identity, `error` so a
superseded transport failure cannot paint "Error connecting to server:" over a valid list, and
`complete` so it cannot hide the spinner of a request still in flight. The accepted `success` also
writes `#vnet-name` and `#vnet-resource-id` from its own arguments, so the identity always comes from
the response that populated the rows rather than from the click that started it. `:61-62` were left in
place, as the finding allows — `beforeSend` hides `#subnet-selection`, so the form is unreachable
between the click and the accepted response, and what actually fixes the defect is the accepted
response writing the identity._

_The finder's fix was taken with the verifier's correction, which was right: guarding `success` alone
leaves both other legs reachable, and the `error` leak is not theoretical — it reproduced here on the
first attempt. The `.abort()` interim was **not** taken. It is placement-sensitive against the pinned
jQuery 4.0.0 (the aborted request's handlers run synchronously inside `.abort()`), so it is correct
only at one exact call site or with an extra `status === "abort"` line; the sequence guard is the same
size, needs no such caveat, and is what the reconcile wizard already does. The optional server-side
prefix check was also not taken: it is a different finding's territory, sits on the watch list as a
deliberate round-6 decision, and would need a conditional for the documented plain-JSON caller._

_Verified in real Chromium against the live app on 127.0.0.1:5812, SQL Server 2022, real ARM, with two
Azure VNets deliberately sharing `10.20.0.0/16` (`rig-h2-alpha` with three subnets, `rig-h2-twin` with
one) and a hand-made Bastet subnet on the same prefix. Only the **arrival order** is manipulated —
`page.route` fetches the live app's own bytes for request #1, holds them 5 s, then fulfils; request #2
passes straight through. Async, so both are genuinely in flight._

```
                              unfixed (ff285cf)                          fixed
A superseded response last    rows alpha-web/app/data, identity          rows twin-only, identity
                              'rig-h2-twin'   STALE_REPAINT=True         'rig-h2-twin'   False
B superseded request fails    errorPanel True, "Error connecting         errorPanel False
                              to server: " over valid twin rows
C control, one pick           3 alpha rows, identity 'rig-h2-alpha'      identical
page errors                   []                                        []
```

_The description each imported child carries still reads `selectedVNetName`, the click-time closure
variable. Checked rather than assumed: `loadSubnets` is called from exactly one place, so under the
guard the newest request is always the newest click and the two agree. Left alone to keep the change
to the defect._

_Test count unchanged at 683 — there is no JS test harness in this repo, which the watch list already
records, so the verification is the browser run above. `dotnet build --no-incremental` 0 warnings,
0 errors._

---

## H3 [x1] — Bulk Azure import commits a plan the operator never saw: the preview pane renders the last response to arrive, Confirm posts the last selection clicked

_H3 is fixed and committed, the same way and for the same reason as H2: `loadPreview` takes a
sequence number from a module-level `previewSeq`, and `success`, `error` and `complete` all return
early when they are not the newest request. A superseded plan can no longer repaint the pane, so
`#bulk-go-commit-btn` can no longer be re-enabled from a plan the operator is not looking at, and the
screen and `lastSelection` cannot describe different selections. Nothing server-side changed:
`BulkCreateFromAzurePlan` re-plans what it is posted and is right to, since it has no way to know what
was rendered._

_Both corrections the verifier made to the finder's fix were taken. Guarding `success` alone leaves a
stale `error` painting "Error connecting to server:" over a current valid plan, and a stale `complete`
hiding the spinner of a request still in flight. The `.abort()` interim was rejected for the reason
recorded under H2 — placement-sensitive against the pinned jQuery 4.0.0 — and the two wizards now
carry the identical guard, which is the point: they were diverging, and the reconcile wizard's
`renderPlan`-sets-`lastPlan` arrangement was already correct._

_Verified in real Chromium against the live app on 127.0.0.1:5813, SQL Server 2022, real ARM.
`rig-h3-gamma` (10.40.0.0/16, no subnets) and `rig-h3-beta` (10.31.0.0/16, two subnets) in Azure, and
a hand-made Bastet subnet `Prod Core` 10.31.0.0/16. Tick gamma, press Next: Preview, jump back through
the still-enabled step-2 pill while it loads, tick beta, preview again. Only arrival order is forced:
`page.route` fetches the live app's own bytes for preview #1, holds them 6 s, then fulfils; preview #2
passes straight through._

```
                       unfixed (ff285cf)                          fixed
after the stale plan   pane: 'New top-level create rig-h3-gamma   pane: 'VNet "rig-h3-beta" - prefix
lands                   (10.40.0.0/16) No child subnets            10.31.0.0/16 Exact match Bastet
                        selected.'                                 subnet "Prod Core"'
commit button          enabled, from GAMMA's canCommit            enabled, from BETA's canCommit
Confirm posts          vNetPrefixes=[rig-h3-beta ...]             vNetPrefixes=[rig-h3-beta ...]
screen vs payload      DIVERGENT                                  agree
row 1 after commit     Prod Core stamped with beta's ARM id       Prod Core stamped with beta's ARM id
                       while gamma was on screen; 10.40.0.0/16    - the plan that was reviewed
                       never created
page errors            []                                         []
```

_The write is identical in both columns, which is the whole point: unfixed, that write is the one the
operator did **not** approve. `Prod Core` carries an `AzureResourceId` permanently either way —
`DeletedSubnets` does not archive the column and there is no restore path — so the difference between
the two runs is whether the operator agreed to it._

_Test count unchanged at 683 — no JS test harness exists, per the watch list; the browser run above is
the verification. `dotnet build --no-incremental` 0 warnings, 0 errors._

---

# Low

## H4 [x1] — "Purge All" ignores the scope its own confirmation page states: the POST round-trips nothing and deletes whatever exists at execution time

_H4 is fixed and committed by bounding the purge to the set the confirmation page counted, which is
the finding's own promoted interim. Both view models carry a `MaxId`, both GETs read
`MaxAsync(d => (int?)d.Id) ?? 0` into it, both views post it back as a hidden `confirmedMaxId`, and
both POSTs delete `Where(d => d.Id <= confirmedMaxId)`. The parameter binds as `int?` and a missing or
non-positive value is **refused** with an error and a redirect to the confirmation page, rather than
binding 0 and reporting a cheerful "Permanently purged 0 record(s)" — the trap one verifier fell into
by accident. Both twins were changed together; they had already drifted once._

_**The finding's originally proposed fix was not applied, and must not be.** Wrapping the two POSTs in
`ExecuteWithSubnetLockAsync` was built, run and measured unsound by both verifiers: it prevents
nothing, because the loss needs no concurrency at all, and it converts the one ordering HEAD already
handles correctly into total loss by parking a purge behind an entire delete. Round 6's clean bill had
already noticed the same unguarded purge and left it deliberately. The watch list carries this;
nothing here should be read as re-opening it._

_The alternative interim — posting the rendered `Count` back and refusing on any change — was also not
taken. It refuses work the operator can legitimately do, where the bound simply does the right amount
of it, and the bounded `DELETE` is a clustered-index seek that never touches the uncommitted tail._

_Soundness of the bound, unchanged from the finding and re-checked: production is SQL Server only
(`Program.cs` uses `UseSqlServer` on both paths), `Id` is `IDENTITY`, and `DELETE` — unlike
`TRUNCATE` — never reseeds it, so any row archived after the GET necessarily has a higher `Id`._

_Verified live first, on the unfixed build, with the finding's own sequence — one admin, two tabs,
zero concurrency, five seconds apart — then again on the fixed build:_

```
                              unfixed (ff285cf)            fixed
page promised                 1                            1
hidden confirmedMaxId         absent - unbounded           1
tab 2 archives 11 more        DeletedSubnets=12            DeletedSubnets=12
tab 1 submits its open form   "Permanently purged 12"      "Permanently purged 1"
DeletedSubnets remaining      0                            11
```

_Seven tests were added (`PurgeAllScopeTests`), covering both archives: the bounded purge leaves
later-archived rows alone, a scope-less POST refuses and destroys nothing (`null`, `0`, `-1`), and the
ordinary unchanged case still purges everything. They are load-bearing, proved the way the skill
prescribes — in a scratch copy with **only** the `Where` bound reverted and the parameter kept, the two
scope tests fail and the rest pass. The suite runs on SQLite, where a plain `INTEGER PRIMARY KEY`
reuses the top rowid after a delete, so every assertion is about rows inserted while the archive is
non-empty and never about IDs surviving a purge — the caveat the finding raised._

_Not addressed, and deliberately: that the purge POST does not require the confirmation page at all,
since antiforgery tokens are per-session. That is on the watch list as by design, and it is a
different question from scoping._

_Test count 683 → 690 (+7). `dotnet build --no-incremental` 0 warnings, 0 errors._

---

## H5 [x1] — Migration bootstrap misreads SQL 4060: a database that exists but cannot be opened is treated as missing, so startup aborts with two successively wrong diagnostics

_H5 is fixed and committed. After the `master` bootstrap connection opens, `Program.cs` now probes
`SELECT HAS_DBACCESS(@catalog)` on it, and when the answer is **0** — the catalog exists but cannot be
opened — it disposes the connection and aborts startup with a message that names the database, both
possible causes, and what not to do. `NULL` (genuinely absent) and `1` (healthy) keep today's
behaviour, and a probe that cannot run at all is swallowed so the fix fails open rather than refusing
to start._

_It aborts rather than logging, which the finding is right to call a constraint: EF Core's
`SqlServerDatabaseCreator.Exists()` misreads 4060 identically, so anything that merely warns is
overruled by `Migrate()` a few lines later. The message names **both** causes because `HAS_DBACCESS`
is also 0 for an offline database, even for `sa` — wording that asserted "this login has no user
inside it" would be confidently wrong in exactly the case measured as run D below._

_**The finder's `SELECT DB_ID(@catalog)` probe was rejected, as the verifiers found.** It answers only
because `VIEW ANY DATABASE` is granted to `public` by default; deny that — ordinary hardening — and it
returns NULL for a database that plainly exists, so the fix silently no-ops and both wrong messages
come back. That is run C below, and it is why `HAS_DBACCESS` is used instead. `DB_ID` also returns
`smallint`, so the natural `is int` test never matches._

_Verified on the rig SQL Server 2022 against the shipped binary in `ASPNETCORE_ENVIRONMENT=Production`,
from six starting states, on both builds. The deployment was first migrated and given a row of real
data, then its database user was orphaned the way `sp_change_users_login` exists to repair — the login
dropped and recreated without `WITH SID`._

```
                                          unfixed (ff285cf)                fixed
A orphaned user, auto-migrate on          CREATE DATABASE [bastet_h5];     no CREATE DATABASE
                                          Error Number:262 "CREATE         Error Number:4060
                                          DATABASE permission denied       "The configured database
                                          in database 'master'."           'bastet_h5' exists on this
                                                                           server but could not be opened"
B same deployment, auto-migrate off       starts, accurate 4060 message    unchanged
C DENY VIEW ANY DATABASE TO public        Error Number:262, as above       same correct message
  (where the rejected DB_ID probe blinds)                                  (this is the case DB_ID fails)
D database OFFLINE, connecting as sa      Error Number:1801 "Database      same correct message
                                          'bastet_h5' already exists."
E genuine bootstrap, catalog absent, sa   starts                           starts; database created,
                                                                           6 tables - no over-fire
F orphaned user repaired, nothing else    starts                           starts
damage sweep                              Bastet tables in master 0, Subnets rows 1, migrations 6
```

_Runs B, E and F are identical on both builds, which is the point: the probe fires on exactly the
condition it names and on nothing else. (`GET /Subnet` answers 500 in every started run on both
builds — this rig runs Production with no OIDC authority configured. Pre-existing and environmental,
not a regression; the signal in this table is started-versus-aborted.)_

_Test count unchanged at 690. `Program.cs` is top-level startup code with no seam the suite can
reach — the six-state rig above is the verification, and the watch list already records that this
repo has no integration host. `dotnet build --no-incremental` 0 warnings, 0 errors._

_Not taken: the cheaper interim that wraps `Migrate()` and appends advice after the fact. It is
strictly weaker — it fires only after the pointless `CREATE DATABASE` — and the real fix costs one
round trip on a path that already opened a connection._

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
