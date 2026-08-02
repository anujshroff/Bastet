# Bastet - Round-12 Audit Findings

| | Value |
|---|---|
| Round | **12** (finding letter **L** - findings are `L1` ... `L4`, numbered sequentially across the whole file) |
| Branch | `audit/round-12` |
| HEAD | `e03ae51` - *"Audit 11 Cleanup (#156)"* |
| Build | **0 warnings, 0 errors** |
| Tests | **738 passed**, 0 failed, 0 skipped |
| Date | 2026-08-02 |

## Verdict

**Nothing found this round is worse than cosmetic. There is no data-loss, no authorization bypass, no
wrong write.** Four findings survived: one **low** and three **info**. Every one of them is a
client-side or render-side inconsistency; in each case the server refused correctly, wrote the right
rows, or wrote nothing at all. The database was measured after every reproduction and was correct in
all four.

Read **`L1` first** - it is the only finding that ends in a state write. The bulk import wizard fails
to re-lock its step-2 pill when the operator changes subscription, so the previous subscription's VNet
tree stays reachable and the import commits against the subscription the operator navigated away from.
The rows it writes are internally coherent and are exactly the ones displayed and ticked, which is why
it is low and not medium - the delta is that the step-1 dropdown reads a subscription no later screen
honours, and no screen after step 1 names a subscription at all.

The other three are: an action link offered to a read-only user that is then correctly refused (`L2`),
a "Select All" master checkbox that goes stale and destroys the selection on the next click (`L3`), and
a commit button that re-arms mid-flight so one successful import briefly renders "Commit failed" beside
its own success banner (`L4`).

**The most valuable output of this round is the fix corrections, not the findings.** Three of the four
proposed fixes were measured wrong or incomplete by verifiers who built and ran them:

- **`L1`'s one-liner is overbroad and was rejected by two of three verifiers.** Widening
  `invalidatePlan()` re-locks the step-2 pill on three *correct-state* paths - including on every
  checkbox tick, which leaves the wizard's own current step marked disabled. Use the change-handler
  variant instead. **`L1`'s cheaper interim closes nothing worth closing and should be declined.**
- **`L4`'s cheaper interim is unsound and must not be taken** - it was measured to close nothing,
  because the step pill bypasses the button it disables.
- **`L3`'s fix is incomplete** - it plants a new stale-master state of the exact class it fixes, and
  misses an identical sibling in the reconcile wizard (the only Azure path that deletes).

Eight candidates were refuted, six of them on consequence measured at zero and two on the automatic
test-coverage refutation. The refuted table below is the more important half of this file for the next
round: five of the eight are shapes rounds 4-11 have already killed, and two of them are re-raises of
deliberate design decisions that are documented in the source.

## How this audit ran

Twenty finders over eight beats. Every beat was covered twice independently - **pass A code-first**,
**pass B behaviour-first** - with a deeper third sweep on **security**, **azure**, **regression** and
**regression-tests**.

15 raw findings merged to 12 candidates: **1 `[x2]`, 11 `[x1]`**.

- **`[x2]`** means two independent passes found it.
- **`[x1]`** means one did. Every `[x1]` got a second verifier on a reachability lens, and a third
  verifier where the first two disagreed.

Every candidate went to at least one verifier **prompted to refute it** and to reproduce it against a
live rig: real SQL Server, the real published application, a real browser, and two Azure service
principals with disjoint RBAC over two resource groups. **4 survived, 8 were refuted, 4 were reproduced
live.**

Verifier corrections to severity, scenario and proposed fix are carried inline in each finding below,
including where a verifier's correction contradicts the finder.

---

# Low

_L1 is fixed and committed with the change-handler variant, not the one-liner the finding proposed.
`$("#step2-tab").addClass("disabled")` now sits beside the existing `invalidatePlan()` call in the
`#bulk-subscription-select` change handler, so step 2 is re-locked exactly when the subscription
changes and at no other time. `#bulk-select-subscription-btn` re-opens it through `activateTab`._

_Reproduced first against an unfixed publish of `4a59ddf` driven by real Chromium against real ARM,
with only `GET /Azure/GetSubscriptions` stubbed - the rig principal still lists a single subscription,
so the second dropdown row has to be supplied, which is the same gap the finding recorded and it is
not closed. **Before:** after switching the dropdown with no Next, `step2: 'nav-link'`; the pill click
opened the pane; the tree still showed the previous subscription's `rig-vnet-bulk`; and
`POST /Azure/BulkImportPreview` carried `subscriptionId=f0e8d6db-...` (Main) while the dropdown read
`Second-Sub`. **After:** `step2: 'nav-link disabled'` and the pill click is refused with a pointer-events
timeout. Zero pageerrors either way._

_The finding's own one-liner - adding `#step2-tab` to the re-lock inside `invalidatePlan()` - was built
here as a third publish rather than taken on the verifiers' word, and it reproduces the regression they
reported. Standing on step 2 with the tree loaded the pill reads `nav-link active disabled`, so the
wizard marks its own current step disabled; that persists after every checkbox tick, because
`updateGoPreviewBtn` (`:332`) calls `invalidatePlan()` as well; and from step 3 the step-2 pill is
refused outright, the click landing on the intercepting `<li>`. The change-handler variant was put
through the same five legs and left every one of them normal, ending in an ordinary end-to-end import
that committed 1 VNet target on a clean catalog. The cheaper interim was declined for the reason the
finding gives - it leaves `selectedSubscriptionId` stale and the pill live._

_Suite unchanged at **738**. This repo has no rendered-view test seam - no `WebApplicationFactory`, no
`IRazorViewEngine`, no rendered-view assertion anywhere in `test/` - so a view-script fix cannot be
pinned by a unit test; the browser legs are the verification, as they were for `K1` and `K2`._

---

# Info

## L2 - "Add Child Subnet" on the subnet Details page is the one action control in the app with no role gate: a View-only user is offered it and every click is refused

`[x1]` &nbsp;|&nbsp; **Severity: info** (reported low; corrected to info by both verifiers) &nbsp;|&nbsp; **Confidence: confirmed**

**Citation:** `src/Bastet/Views/Subnet/Details/_ChildSubnets.cshtml:6` - `@if (Model.CanAddChildSubnet)`,
where `SubnetViewModels.cs:114` defines that as `HostIpAssignments.Count == 0 && !IsFullyAllocated`.
Capacity only; no user in the predicate.

### Failure scenario

A principal whose token carries only the `View` role opens `/Subnet/Details/1`. The Child Subnets card
header renders `<a class="btn btn-sm btn-primary" href="/Subnet/Create?parentId=1">Add Child Subnet</a>`.
Following it is refused: `GET /Subnet/Create` is `[Authorize(Policy = "RequireEditRole")]`
(`SubnetController.Create.cs:13`).

The page disagrees with itself on the same render: the Unallocated Ranges card lists its ranges and
renders **zero** "Create Subnet" buttons, because `_UnallocatedRanges.cshtml:34` gates that control on
`UserHasRole(ApplicationRoles.Edit)`; and `/Subnet` renders no Create link at all, because
`Index.cshtml:15` gates it the same way. One control offers the action while the two others offering
the identical action correctly hide it.

This is the only such gap. `grep -rn UserHasRole src/Bastet/Views/` returns 20 gates across 13 view
files; `_ChildSubnets.cshtml` is the only view emitting an action anchor with no `UserHasRole` in it.
Two independent per-role href enumerations over ten pages agreed: for `View` the only href pointing at
an unreachable action is `/Subnet/Create?parentId=N`; `Edit` and `Delete` have none at all.

### Reproduction

Two independent rigs, each a worktree at HEAD whose **only** edit is `Services/DevAuthHandler.cs`
reading an `X-Rig-Roles` header to choose the role claim set (header absent => `Admin`, i.e. identical
to HEAD). Views, view models and every `[Authorize]` policy stock.

```
GET /Subnet/Create?parentId=1 :  none 403 | View 403 | Edit 200 | Delete 200 | Admin 200
GET /Subnet/Details/1         :  none 403 | View 200 | Edit 200 | Admin 200

One page, /Subnet/Details/1, same DB row, three principals:
  View  | Create-Subnet buttons: 0 | unallocated rows: 2 | Add-Child anchors: ['Add Child Subnet']
  Edit  | Create-Subnet buttons: 2 | unallocated rows: 2 | Add-Child anchors: ['Add Child Subnet']
  Admin | Create-Subnet buttons: 2 | unallocated rows: 2 | Add-Child anchors: ['Add Child Subnet']
  (same render: Edit/Delete buttons 0/0 for View, 1/0 for Edit, 1/1 for Admin)

Following that href as View:
  HTTP/1.1 403 Forbidden
```

A full POST matrix (14 endpoints x 5 roles with per-role antiforgery tokens) found **no policy gap
anywhere**: `POST /Subnet/Create` = `403 403 200 200 200`, `POST /Subnet/Delete/1` = `403 403 403 302
302`, `PurgeAll*` and all four Azure POSTs = `403 403 403 403 <2xx/3xx/400>`. Server-side enforcement is
intact at every layer.

### Why info, not low

Both verifiers independently corrected this down. The finding narrates the Development consequence - a
generic "Status Code: 403 / An error occurred while processing your request" page - which is a rig
artifact twice over: `Program.cs:180-188` registers no `AccessDeniedPath` on the dev scheme (the code
comment there says so explicitly), and stock Development issues `Admin` to everyone, so a View-only
principal cannot exist there at all. In the only deployment where the precondition can hold
(Production/OIDC), `Program.cs:200` turns the same Forbid into a 302 to `/Account/AccessDenied`, whose
text is *"Your account doesn't have the necessary roles to view this page."*

So the entire harm is one misleading affordance whose click lands on a page that correctly explains the
refusal. Nothing is written, nothing is disclosed that `/Account/Roles` does not already show, no
privilege is gained.

**Not the twice-refuted claim on this line.** Round 7's `F10` and round 11's kill were the *capacity*
claim (hide the link on a `/32`; proposed gate `Cidr < 32`). Round 11 killed it because the "two
controls on one page disagree" premise is measurably **false** for capacity - a fully-covered subnet
renders no Unallocated Ranges card at all - and because the target answers 200 with an inline reason.
For the **role** predicate both inversions hold: the second control is on screen and measurably
disagrees, and the target genuinely refuses at the authorization layer.

### Fix - sound, built and run

```razor
@inject Bastet.Services.IUserContextService ChildSubnetsUserContext
...
@if (Model.CanAddChildSubnet && ChildSubnetsUserContext.UserHasRole(Bastet.Models.ApplicationRoles.Edit))
```

Both verifiers applied it, published (0 warnings / 0 errors) and re-rendered 3 subnet states x 5 role
sets. View loses the anchor; Edit/Delete/Admin keep it; `/Subnet/Details/1` still returns 200 for View.

**The AND is load-bearing, as the finding says.** The `else if` branches below print the "Fully
Allocated" and "Has Host IPs" badges, which are capacity statements that must keep rendering for a
read-only user - measured intact after the fix (subnet with host IPs still shows "Has Host IPs" to
View; fully-allocated subnet still shows "Fully Allocated" and its alert). Replacing rather than ANDing
would have broken exactly that.

Single render site (`Details.cshtml:41`), no sibling call site missed; the third `CanAddChildSubnet`
consumer (`_HostIpAssignments.cshtml:110`) is already inside an Edit gate at `:98`. No test touches the
view. Visible delta: a View user on an empty, non-full subnet now sees a bare "Child Subnets" card
header with neither button nor badge - matches the other cards, not a regression.

**Cheaper interim (`ViewBag.CanCreateSubnets` in `SubnetController.Read.cs` beside the existing
`ViewBag.CanImportFromAzure` at `:124-129`): plausible but NOT BUILT.** The mechanism is confirmed to
work - `Html.PartialAsync` inherits parent `ViewData`, which is how `_RoleBasedActions.cshtml:9` already
reads `ViewBag.CanImportFromAzure` set in that same block - but it was never measured. Take the injected
partial; it is the verified one.

---

## L3 - Single-VNet import wizard: unticking rows leaves the "Select All Subnets" master ticked, so the next click on it clears the whole selection instead of completing it

`[x1]` &nbsp;|&nbsp; **Severity: info** &nbsp;|&nbsp; **Confidence: confirmed**

**Citation:** `src/Bastet/Views/Azure/Import/_ImportScripts.cshtml:124` - the delegated `.subnet-checkbox`
change handler (`:124-126`) calls only `updateImportButton()`. The master at `:117-121` propagates to
every row with `.prop()` (which fires no `change`), and the only reset of `#select-all-subnets` anywhere
in the tree is `loadSubnets`' `beforeSend` at `:264`.

### Failure scenario

On `/Azure/Import/{id}` step 3 the operator ticks "Select All Subnets", then unticks rows. Nothing ever
recomputes the master, so it stays `checked` over a partial - or empty - selection. Their next click on
the control labelled "Select All Subnets" *unchecks* it, propagating `false` to every row: zero subnets
selected, "Import Selected Subnets" disabled.

The wizard already treats this exact staleness as a defect: the comment at `:256-263` records fixing the
reload variant (round 7's `G10`), in the same words - *"the operator's first click on Select All
untick[ed] everything instead of selecting it"*. It resets the master in `loadSubnets` only, not on a
per-row change.

**Scenario correction (both verifiers).** The finding's narrated motive is weak: after Select All then
unticking one row, every *remaining* row is already selected, so "clicks Select All expecting the
remaining rows to be selected" has no motive. The motivated and strictly worse path - measured on the
pristine HEAD reference instance - is the operator unticking rows down to an empty selection. The master
then asserts "all selected" over **zero** selected rows, and the click on Select All selects 0 rows,
i.e. the click is a visible no-op. That is the `:256-263` complaint verbatim, reached without a reload.

### Reproduction

Real Chromium against the unmodified HEAD reference instance (subnet `rig-probe-visible` 10.120.0.0/16
-> Azure VNet `rig-vnet-visible`, rows `rig-sub-web` / `rig-sub-app`). Reproduced independently twice.

```
E1 fresh                      {master: false, indeterminate: false, rows: [false, false], importDisabled: true}
E2 Select All ticked          {master: true,  indeterminate: false, rows: [true,  true ], importDisabled: false}
E3 operator unticks BOTH rows {master: true,  indeterminate: false, rows: [false, false], importDisabled: true}
     master still renders ticked over an EMPTY selection: True
E4 operator clicks 'Select All' {master: false, rows: [false, false], importDisabled: true}
     -> rows selected by that click: 0
pageerrors: []
```

The finding's own 1-of-2 sequence reproduces identically (`master_checked: true` over one ticked row,
`:checked` matches so it *visibly* renders ticked, next click leaves 0 selected).

**No wrong import.** The submit handler at `:399-408` re-derives the disabled flags from current
checkbox state and `:388` blocks a zero-selection submit; the POST carried only the ticked row. The harm
is confined to the selection being destroyed. Control leg: an operator who never touches the master gets
correct behaviour throughout, so the defect requires Select All to have been used first, and one further
click on the same control fully restores the selection. That bounded, self-correcting consequence is
why this sits at info rather than at `G10`'s low - info is already the floor, so no further correction
was available.

### Fix - INCOMPLETE as proposed; both verifiers built it and found two gaps

Proposed: sync the master from the rows inside the delegated per-row handler at `:124-126`:

```js
$(document).on("change", ".subnet-checkbox", function () {
    var boxes = $(".subnet-checkbox");
    var checked = boxes.filter(":checked").length;
    $("#select-all-subnets")
        .prop("checked", checked > 0 && checked === boxes.length)
        .prop("indeterminate", checked > 0 && checked < boxes.length);
    updateImportButton();
});
```

Built, published and driven by both verifiers: 0 warnings / 0 errors, zero pageerrors, and it closes the
reported defect (partial selection -> master unchecked + indeterminate; next click on Select All ticks
every row). The deliberate `.prop()` choice is correct and was verified not to re-enter the master's own
handler - this is **not** the synthetic-event shape round 4's `D1` removed. The `checked > 0 &&`
conjunct looks redundant but is load-bearing for the empty-list case; keep it.

**Gap 1 - it plants a new stale state of the exact class `:256-263` exists to close.** `indeterminate`
is now a second piece of master state, and `loadSubnets`' `beforeSend` at `:264` clears `checked` only.
Measured on the built fix, driving `G10`'s own path (partial selection -> "Back to VNets" -> "Next" with
the same VNet still chosen, so no `change` fires on `#vnet-select`): the rebuilt list comes back with
zero rows ticked and the master rendering an indeterminate **dash**.

```
FIXED build : after re-entry {master: false, indeterminate: TRUE,  rows: [false, false], importDisabled: true}
HEAD  build : after re-entry {master: false, indeterminate: false, rows: [false, false], importDisabled: true}
```

Required addition, beside the existing reset at `:264` (applied by content, not line number):

```js
$("#select-all-subnets").prop("checked", false);
$("#select-all-subnets").prop("indeterminate", false);
```

Re-run with that line: re-entry gives `{master: false, indeterminate: false, both rows false}` and one
click on Select All selects all. `G10`'s behaviour preserved, reported defect still closed, zero
pageerrors.

**Gap 2 - it misses a sibling call site.** `src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml`
has the identical construction: master `#rec-select-all` at `:316-320` propagating with `.prop()`, a
delegated `.rec-item-checkbox` handler at `:311-314` that never recomputes it, and a single reset at
`:292` on re-scan. Reproduced live with two genuinely stale Azure-linked rows: master stayed ticked over
an empty selection and the Select All click selected 0 rows. **That is the wizard whose commit is the
only Azure-driven DELETE path** - fix it in the same commit, with the matching `indeterminate` reset at
`:292`.

**The finding's rejection of its own cheaper interim is sound and was confirmed by measurement:**
clearing the master only when a row is unticked leaves it unticked after the operator re-ticks the last
row - the same lie in the other direction.

---

## L4 - Bulk import: the Commit step re-arms itself while a commit is in flight, so one successful import reports itself as "Commit failed" and names every subnet it just created

`[x1]` &nbsp;|&nbsp; **Severity: info** &nbsp;|&nbsp; **Confidence: confirmed**

**Citation:** `src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:652` - the `#bulk-go-commit-btn`
handler (`:647`) unconditionally runs `$("#bulk-confirm-commit-btn").prop("disabled", false).show();`
with no test for a commit in flight or already completed. `commitImport`'s `beforeSend` (`:677`)
disables the confirm button but nothing disables `#bulk-back-to-preview-btn` (`:655`) or
`#bulk-go-commit-btn`. The step pills are Bootstrap `data-bs-toggle="pill"` and bypass `:652` entirely,
so `:652` is the single re-arm site.

### Failure scenario

An admin on step 4 clicks **Confirm Import**. While the commit is in flight the operator clicks **Back
to Preview**, then **Continue to Commit** - which re-arms Confirm - and clicks it again. The server
serialises the two on the subnet lock and refuses the second, so **nothing is written twice**, but the
second response is what the operator is left looking at: a red **"Commit failed: The import failed."**
panel listing `Azure subnet 'rig-uiwzb-a' (10.150.1.0/24, VNet 'rig-vnet-uiwzb') already exists in
Bastet.` and three more lines naming exactly the rows the operator's own import had just created.

**Wrong-output correction (both verifiers, independently).** The finding says the wizard's final
rendered state is "the red panel". It is worse and simpler than that: `showCommitError` never hides
`#bulk-commit-success`, so the green *"Bulk import completed. Created 4 VNet target(s), 5 child
subnet(s)..."* alert and the red *"Commit failed"* alert are **on screen simultaneously for ~2 seconds**
(1.95 s and 2.04 s measured on two rigs; screenshot captured) before the `setTimeout(..., 2000)` at
`:692` redirects to `/Subnet`, whose banner says the import succeeded. The screen contradicts *itself*,
not only the page it redirects to.

**A second aggravator the finding misses:** `:650-651` also hide the success panel on re-entry, so the
re-entered step 4 shows no evidence the import already happened, next to a live Confirm button.

### Reproduction

Reproduced on three rigs by three methods, **including with no interception and no concurrency at all.**

*Genuine lock contention, ordinary UI clicks, no `page.route`* - the latency came from a second admin's
ordinary `POST /Subnet/Delete` of a 6001-row subtree holding the global subnet lock (timed alone at
7.58 s), which is the app's own designed serialisation:

```
 3.566 click Confirm #1
 3.577   >>> commit POST issued          (queues on the lock)
 4.278   progress visible: True  confirm disabled: True
 4.285 click Back to Preview  (commit still in flight)
 4.811 click Continue to Commit
 5.331   confirm now disabled: False  visible: True      <-- :652 re-armed it
 5.334 click Confirm #2  -> SECOND CLICK ACCEPTED
10.841   <<< 200 {"success":true,"createdTargets":4,"createdChildSubnets":5,...}
10.841   <<< 400 {"success":false,"globalErrors":["Azure subnet 'rig-uiwzb-a' (10.150.1.0/24,
                  VNet 'rig-vnet-uiwzb') already exists in Bastet.", ...]}
10.842 PANEL error  = True
10.947 PANEL success= True      >>> BOTH PANELS VISIBLE FOR 1.95 s
12.850 NAV -> /Subnet
commit POST bodies identical: True (both len=3327, sha256 1210e1b5...)   pageerrors: []
DB after: 9 live rows, no duplicates.
```

*Pill variant:* identical outcome without ever touching `#bulk-back-to-preview-btn` - clicking the
`#step3-tab` pill while the commit is in flight, then Continue to Commit, re-arms Confirm the same way.

*No concurrency, no throttling at all:* the commit returns in 157 ms; Back to Preview -> Continue to
Commit -> Confirm at 0.35 s intervals, all inside the 2 s redirect window, re-arms and fires a second
POST that returns the same 400 and renders the red panel for 0.83 s.

**Scenario correction:** the finding justifies the in-flight window by citing round 11's 7,247 ms figure
as ARM latency on this surface. That is wrong - `BulkCreateFromAzurePlanCore` makes **zero** ARM calls
(`GetExistingSubnetsAsync` is pure EF, the planner is in-memory) and measured 157-222 ms unthrottled.
The 7,247 ms is bulk *preview* latency and does not transfer. The finding's other named source - queuing
on the `Bastet:SubnetOperations` app lock, which the controller's own 503 names - is real and is what
both verifiers used.

### Fix - primary is sound (built and run); the cheaper interim is UNSOUND and must not be taken

Primary, applied verbatim and measured on two rigs:

- `let committing = false; let committed = false;` beside `lastSelection`/`previewSeq` at `:11-12`
- `committing = true` first statement of `beforeSend`; `committing = false` first statement of `complete`
- `committed = true` first statement of the `result.success` branch
- `:652` becomes
  `$("#bulk-confirm-commit-btn").prop("disabled", committing || committed).toggle(!committed);`
- `invalidatePlan()` clears `committed` (and must **not** clear `committing` - doing so reopens the hole)

Measured: confirm stays `disabled: True` across Back to Preview -> Continue to Commit, the second click
is refused, exactly **one** POST is issued, only the success panel renders, `pageerrors: []`, DB still
correct. No temporal dead zone - all handlers are closures over `let`s declared at the top of the same
`$(document).ready` scope and `commitImport` is a hoisted function declaration; this is not round 10's
`J9` shape. `.toggle(Boolean)` was probed in the live page against the shipped jQuery **4.0.0**
(`_Layout.cshtml:103`) and still works.

Non-regression legs both pass: an ordinary Back to Preview -> Continue to Commit -> Confirm with nothing
in flight still commits (no operator is stranded), and a genuinely failed commit (a real 409 from an
out-of-band row created through the ordinary Create form in a second cookie jar) stays retryable -
`showCommitError`'s `prop("disabled", false)` at `:737` must be **left ungated**, since `committed` is
still false there and that is the deliberate retry path.

**The cheaper interim closes nothing.** The finding calls disabling `#bulk-back-to-preview-btn` in
`beforeSend` "strictly weaker"; a verifier measured it as *ineffective*. `activateTab` deliberately
leaves a visited step's pill clickable (comment at `:25-28`), so clicking the `#step3-tab` pill instead
of the button re-arms Confirm identically - "SECOND CLICK ACCEPTED", 200 then 400, both panels visible
for 1.96 s. To work it would also have to disable `#bulk-go-commit-btn`, at which point it is no cheaper
than the flag.

Two residues the fix leaves, neither unsound: re-previewing *while* a commit is in flight leaves
`committing` true when the new step 4 is entered, so Confirm renders disabled and needs one more Back to
Preview -> Continue to Commit after the response lands; and `:650-651` still hides the success panel on
re-entry after a successful commit.

---

# Refuted

Reported by a finder, killed by a verifier that reproduced it and measured the consequence.

| # | Title (as reported) | Sev | Citation | Refuted because |
|---|---|---|---|---|
| R1 | Subnet rename skips the `[SafeText]` character rule the create form enforces, so `POST /Subnet/Edit` persists names `POST /Subnet/Create` refuses | info | `src/Bastet/Services/SubnetNaming.cs:27` (models at `SubnetViewModels.cs:8-14`, `EditSubnetViewModel.cs:42-47`) | Reachable and reproduced - and it is the codebase's documented design, not drift. `git log -S"SafeText"` on `EditSubnetViewModel.cs` is **empty**: the attribute was never there. `SubnetNaming.cs:26-30` and `SubnetController.Create.cs:72-75` both state the asymmetry is deliberate ("so a parent can legitimately be called `Prod/Web`"), and round 7 shipped `SubnetNaming.ToSafeText` plus tests whose `InlineData` is literally `Prod/Web`, `Bob's Lab`, `DC1:Core` - the exact names Create refuses - to filter at the generating site instead. The headline claim is also false: Create **accepted** `evil onmouseover=x` and **refused** `Bob's Lab`; `[SafeText]` is an ASCII-punctuation whitelist, not a danger rule. Downstream sinks measured clean (child-name prefill returned `ProdWeb-10.90.1.0-24`, legal on the next POST; parent dropdown HTML-encoded and inert). The cited comment at `EditSubnetViewModel.cs:37-41` is about `[NoHtml]`/`[Tags]`, which *are* in step. No wrong byte, no corrupted row, no misled user. |
| R2 | A `/31` or `/32` child subnet may be created on its parent's network or broadcast address, and a host IP can then be assigned to exactly the address the parent refuses | info | `src/Bastet/Controllers/SubnetController.Helpers.cs:216` (+ `HostIpValidationService.cs:70`) | Mechanism reproduces byte for byte; the asymmetry does not exist. Bastet enforces a container-vs-leaf invariant the finding never mentions: once the `/24` has the `/32` child it can hold **no** host IP (`HostIpValidationService.cs:218`, *"Cannot add host IP assignments to a subnet that has child subnets"*), and a `/24` holding host IPs is refused the child. The two legs are mutually exclusive states of the parent, so no rule is bypassed - there is no state in which the same subnet both reserves `.255` and contains a host at `.255`. Under the app's own model the row is not wrong: the `/31` leg **is** the RFC 3021 case `HostIpValidationService.cs:65-69` deliberately supports and round 11's `K5` re-affirmed with four tests. Parent Details page measured internally consistent (Broadcast `10.50.0.255`, Total 256, Usable 254, Unallocated 254). The finder's own evidence says "found NO wrong count". |
| R3 | Reconcile wizard drops the 409's `subnetIds`, so a refused deletion tells the operator a count but not which rows | info | `src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:453` | The headline is false as stated. Every withhold path writes `NameList(...)` into `plan.Warnings` (`AzureReconciler.cs:230, :238, :248, :295`), and `warnings` is exactly what `showCommitError` **does** render - a genuine withhold produced a rendered sentence naming `'ghost-net'` on screen. The residue is two triggers where `warnings` is empty (Bastet row deleted out of band; Azure resource live again with its recorded prefix), and in both the withheld row's identity is not needed and its absence never enables a wrong action: the message's own prescription ("Re-run the scan") is two clicks away and produced exactly the correct set. Nothing was written on either 409 (confirmed by SELECT); the confirmation list is a static pre-submission record and the pane says *"Nothing was deleted"*. The "identical shape to round 11's `K2`" framing is wrong - `K2` left the error list **empty** with no other channel; here the prose renders in full and only a machine-readable id array is unused. What survives is one sentence of UI copy - the `F10` shape rounds 4-11 killed every time. |
| R4 | `K3`'s divergence short-circuit is over-broad: a real "the plan changed since you previewed it" refusal is replaced by an unrelated 400 whenever the re-derived plan also carries any global error | info | `src/Bastet/Controllers/SubnetController.BulkAzure.cs:205` | Citation exact, premise correct (`AzureBulkImportPlanner.cs:121-125` is the only early return; the four detectors at `:130,:131,:145,:146` all run with `plan.Items` fully built, so the comment at `:199-201` is false for four of eight global-error sources), and one verifier's "needs two out-of-band writes" objection was itself falsified - a single ordinary `/Subnet/Edit` CIDR change produces both conditions at once. It still dies on consequence: `CanCommit => GlobalErrors.Count == 0 && ...` (`AzureBulkImportViewModels.cs:436`), so there is **no build in which the short-circuit lets a diverged plan commit**; the `J2`/`K1` guard is deferred by one click, not defeated (`_BulkScripts.cshtml:737` re-arms after any error, and the very next commit answers 409 with both divergence sentences once the blocker clears); and the message the operator gets is true, server-derived and names a real hard blocker. The proposed fix, built and measured, merely **reverses** which true sentence is dropped - its 409 carries no `globalErrors`. That is an ordering preference plus comment accuracy, both automatic refutations. |
| R5 | Both `K3` null guards are unreachable at HEAD and neither malformed-body test exercises them; `NullPrefixCollection` fails under no single-hunk revert at all | info | `test/Bastet.Tests/Azure/SubnetControllerBulkAzureImportTests.cs:305` | **`[x2]` - the round's only double report, and every factual claim reproduced.** The guards really are unreachable (both replaced with `throw`, published, three malformed bodies driven through a real antiforgery-tokened POST: zero guard hits, zero 500s, three 400s byte-identical to unmodified HEAD), and the test arithmetic is exactly as claimed. It dies on two automatic refutations: it is not a runtime defect (no input produces a wrong byte; a build with both guards deleted serves the identical response) and the entire deliverable is "add one test" plus a comment retitle - nothing in `src/` changes. It also re-raises round 11's `TruncateForName` kill ("unreachable in every execution - no wrong output to exhibit") and round 11's `J1` scaling-test kill ("manufactured in the reporter's private copy, consequence entirely future-tense"), and the brief's standing "**Dead but deliberate - do not tidy**" decision covers this precise code: these guards are the fail-safe half of a two-layer defence. Overstatement worth recording: `NullPrefixElement` is the only test in the suite that fails when the short-circuit hunk is reverted, so it is doing real work. |
| R6 | `K5`'s four new tests pin only the controller sentinel; reverting the user-visible half leaves the Details page with a blank Broadcast Address row and 738/738 still green | info | `test/Bastet.Tests/SubnetManagement/SubnetDetailsBroadcastAddressTests.cs:69` | Automatic refutation twice. The wrong output exists only in a build where the reporter has first collapsed the view half back - at HEAD all four CIDR classes render correctly through the ordinary GET, and nothing in `src/` changes under the finding's own remedy. Verbatim round 11's `J1` shape. The "split brain" (controller `Cidr < 31`, view `== 31` then `== 32`) has **no in-edge**: the two predicates diverge only on `Cidr >= 33`, which `[Range(0, 32)]` on `SubnetViewModels.cs:23` refuses at the POST; over `[0,32]` they are exactly complementary, with one writer and one reader. And the premise generalises away: the test project contains no `WebApplicationFactory`, no `IRazorViewEngine` and no rendered-view assertion of any kind, so *every* view in the app is unpinned for the same reason `K1` and `K2` shipped with no test at all. |
| R7 | The `K4` `ChildNames` comparison has no positive control: pointing it at a different property leaves 738/738 green while it would falsely refuse every previewed import that disambiguated a child name | info | `test/Bastet.Tests/Azure/SubnetControllerBulkAzureImportTests.cs:280` | Automatic refutation: the `fix` field is literally "Add one test", and the finder's own evidence concedes *"the shipped behaviour is correct so this is a test gap and not a defect at HEAD"*. The wrong output exists only in a build where `SubnetController.BulkAzure.cs:167` has first been edited. Third near-verbatim re-raise of a shape round 11 killed three times in this same file. The full path was driven on the shipped build with every precondition simultaneously true (ExactMatch target, colliding child name so `DisambiguateName` fires, `childNames` stamped as the wizard stamps it): **HTTP 200, child written as `vc11-clash (vc11-vnet)`** - correct. In-edge set for the claimed wrong output is empty at HEAD. Consequence also overstated: measured on the mutant build, only prefixes whose children were disambiguated are refused; a plain prefix committed 200 on the same binary. |
| R8 | Bulk import writes a child subnet under a name the preview never showed - the planner composes the name from the unsanitized `vNetName`, the commit re-sanitizes it | info | `src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:517` (+ `SubnetController.BulkAzure.cs:165-170, :429`) | Mechanism reproduces byte for byte and all citations are accurate. Dies on three grounds. **Not a real input:** all three limbs of `SanitizeName` are provably the identity on an ARM-derived composed child name - `az network vnet create -n 'rig-tb<b>x'` is rejected by ARM itself (*"may contain word characters or '.', '-', '_'"*), ARM caps names at 80 while `SubnetNaming.WithSuffix` leaves >= 73 chars of room so the 100-char cut is never reached, and the composed string always starts with a non-space and ends with `)`. The only trigger is an Admin hand-writing HTML into a field whose XML doc says "The Azure VNet's display name", on an Admin-only antiforgery-protected JSON endpoint - the caller authors the anomaly and is its only victim. **The `K4` equivalence is false, measured:** the same collision driven with an ARM-legal name planned `rig-vnet-visible (rig-vnet-visible)` and wrote it byte-identical; `K4`'s concurrent-rename protection is wholly intact. **Consequence ceiling:** the persisted name is *strictly more* sanitized than the planned one - no XSS (`renderPlan` escapes at `:623`), no empty name, no length bypass, and `Name` is one `/Subnet/Edit` from correction. Same shape as round 11's `BatchCreateChildSubnets` TempData kill. |

---

# Watch list

Not findings. Things measured this round that the next round should know before it spends a beat
re-deriving them.

**Code**

- **Reconcile wizard carries `L3` verbatim and is unfixed.** `_ReconcileScripts.cshtml`: master
  `#rec-select-all` (`:316-320`) propagates with `.prop()`, the delegated `.rec-item-checkbox` handler
  (`:311-314`) never recomputes it, and the only reset is `:292` on re-scan. Reproduced live with two
  genuinely stale Azure-linked rows: master ticked over an empty selection, Select All click selects 0
  rows. This is the only Azure-driven DELETE path. It was found as a sibling during verification, not
  filed separately - fix it alongside `L3`.
- **`SubnetController.BulkAzure.cs:199-201` states something false.** "because then it produced no
  items" is true only for the step-1 parse/validate early return (`AzureBulkImportPlanner.cs:121-125`);
  the four detectors at `:130, :131, :145, :146` leave `plan.Items` fully populated. Comment accuracy
  only - see `R4` - but if anyone ever does change the short-circuit, the 409 at `:211-218` must also
  carry `plan.GlobalErrors` or it drops the harder message.
- **`showCommitError`'s `prop("disabled", false)` at `_BulkScripts.cshtml:737` must stay ungated.** It
  is the deliberate retry-after-failure path; gating it strands an operator whose commit legitimately
  failed. Verified with a real 409.
- **`_BulkScripts.cshtml:650-651` hides the success panel on re-entry to step 4**, so a re-entered
  step 4 after a successful commit shows no evidence the import happened, next to a Confirm button.
  Cosmetic; folded into `L4`.
- **`_ChildSubnets.cshtml` is the only view in the tree emitting an action anchor with no
  `UserHasRole`.** `grep -rn UserHasRole src/Bastet/Views/` = 20 gates across 13 files. Two independent
  per-role href sweeps over ten pages found no other unreachable target for any role. The full GET
  (28 endpoints x 5 role sets) and POST (14 x 5) authorization matrices are clean.
- **`[SafeText]` on `EditSubnetViewModel.Name` is deliberately absent and must not be "restored".** See
  `R1`. `SubnetNaming.ToSafeText` and its tests exist precisely to live with it. `Description` carries
  `[NoHtml]` and no `[SafeText]` on **both** Create and Edit, so `[SafeText]` is not applied uniformly
  to text columns anywhere.
- **The `K3` null guards at `SubnetController.BulkAzure.cs:85` and `:91-97` are unreachable at HEAD and
  should stay.** Standing decision: dead but deliberate - removing the fail-safe arm of a two-layer
  defence converts fail-safe into fail-open. See `R5`.

**Measured facts that correct earlier rounds' numbers**

- **`BulkCreateFromAzurePlanCore` makes zero ARM calls** and commits in **157-222 ms**.
  `GetExistingSubnetsAsync` is pure EF and the planner is in-memory. Round 11's **7,247 ms** figure is
  bulk *preview* latency against live ARM and does not transfer to the commit.
- **The real multi-second window on the commit path is the `Bastet:SubnetOperations` app lock**, which
  the controller's own 503 names. Measured: an ordinary `POST /Subnet/Delete` of a 6001-row subtree holds
  it for 7.58 s; round 10 measured a 20,081-subnet root delete at 22.5 s. Any concurrency finding on
  this surface should use the lock, not interception, to open the window.
- **The shipped jQuery is 4.0.0**, pulled from a CDN at `_Layout.cshtml:103`. `.toggle(Boolean)` was
  probed in the live page and still works, but jQuery 4 removed a lot of deprecated surface - probe
  before relying on any jQuery idiom in a proposed fix.
- **Razor does not parse embedded JS.** Both patched builds this round compiled at 0 warnings with
  broken script logic. Any view-script fix must be driven in a browser, not read.

**Rig constraints the next round will hit**

- **SP_A lists exactly one subscription.** `az account list --all` returns only `Main`
  (`f0e8d6db-e9c4-4215-81a5-17762ea56be8`), and `GET /Azure/GetSubscriptions` returns that one entry.
  Any finding whose scenario needs two subscriptions in the dropdown - `L1` is one - can only be driven
  by fulfilling that single request in the browser, which is why `L1`'s confidence is *plausible*. A
  genuinely multi-subscription credential would close that gap.
- **There is no rendered-view test seam.** No `WebApplicationFactory`, no `IRazorViewEngine`, no
  rendered-view assertion anywhere in `test/`. Do not price a view-half fix as untested-therefore-broken
  (see `R6`); nothing in this app can pin one.
- **`/tmp` is a 16 GB tmpfs holding the entire scratchpad and it hit 100% twice during this round**,
  once badly enough that no tool could capture output. Put publish artefacts and logs under `/var/tmp`,
  and kill instances by PID rather than by port.
