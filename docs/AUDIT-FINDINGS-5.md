# Bastet — Round-5 Audit Findings

**Target:** branch `main`, HEAD `bf120d6` "Audit 4 Cleanup (#141)" (all 43 round-4 fixes squashed).
**Test baseline:** 576 passing, 0 failed. `dotnet build --no-incremental` clean, 0 warnings.
**Date:** 2026-07-26

## Verdict

No critical or high-severity defects, and nothing that loses or wrongly deletes data.

The headline is **E1**, and it is a regression introduced *by* round 4's D3 fix: the per-resource
confirmation D3 added is applied to every proposed row regardless of what the row asserts, so the two
statuses that mean "the resource still exists but its prefix drifted" are confirmed `Live` and then
dropped for still existing. Prefix drift is now permanently undetectable through the reconcile UI, and
the operator is shown a warning stating the rows "were missing from the subscription listing" when they
were not. I proved this end to end against a live Azure subscription, not by reading — see below.

The same live rig proved the D3 fix itself is sound and load-bearing: with a credential that had lost
access to a resource group, `BuildPlan` alone proposed **all 10** imported subnets for archival with
`CanCommit=True`, and D3's direct-read guard correctly withheld every one of them. Round 4's most
consequential fix does what its struck paragraph claims.

After that it is a long tail. **E2** and **E3** are the two worth reading next — both persist a state
the validated path would reject. The rest are messaging, diagnostics and client-side affordance defects.

Read in this order: **E1** (a shipped capability is silently dead), **E2**, **E3**, then the lows.

## How this audit ran

Eight parallel finder beats — security/web, logic & data integrity, Azure, locking & lifecycle,
UI/client-JS, regression-correctness, regression-tests, dead code — each finding then handed to an
independent verifier instructed to **refute** it and to default to "not real" when uncertain.

It ran **twice**, deliberately. 16 finders, 45 verifiers, 61 agents, 0 errors. Findings are tagged:

- **[×2]** — found independently by both passes. Strongest signal.
- **[×1]** — found by one pass only. *Not weaker in truth* — it means a full pass missed it, so it
  deserves **more** scrutiny during reconciliation, not less. **Absence is weak evidence.**

Pass 1: 13 survived, 8 refuted. Pass 2: 14 survived, 10 refuted. Merged and de-duplicated to 14 below.

Two things distinguish this round from round 4:

**The regression beats read atomic commits.** PR #141 squashed to one commit locally, but its 45
original commits were recovered via `git fetch origin pull/141/head`. 43 map one-to-one onto D1–D43;
the other two (`5770a3c`, `841e769`) are skill/docs only. Each fix was diffed against the struck
paragraph describing it.

**The Azure beat had a live rig.** Two service principals with *disjoint* scope — one seeing resource
group `bastet`, one seeing `bastet-hidden`, neither seeing the other — driving Bastet's own
`AzureService` and `AzureReconciler` against real ARM. Read-only throughout: enumeration and
`ConfirmResourcesAsync` only, never an archival path, never a write to Azure. Findings it settled are
marked **confirmed (live)**. Several verifiers built their own rigs too, including a real SQL Server
container for E4 and an EF Core tracking harness for E6.

I re-checked every citation in this file against the working tree by hand. Where a finder's line number
was off, it is corrected here and noted.

---

# Medium

## E1. Reconcile silently discards every prefix-drift row, and explains it with a false statement `[×2]`

_E1 is fixed and committed, and it is the finding this round turned on. The fix partitions on what a
row **claims** rather than applying one rule to every proposed row: a confirmation answers "is the
resource gone?", which is a question only `VNetDeleted` and `SubnetDeleted` ask. A drift row exists
precisely because the resource **was** found in the listing, so reading it back can only ever answer
`Live`._

_Both halves of the audit's suggested fix were applied — the guard in `ApplyConfirmations` and the
filter in `ConfirmProposedDeletionsAsync` — but **not as the audit worded them**. Writing the status
list out at both call sites would have created exactly the drift-prone duplication these rounds keep
finding, so the rule is extracted once as `AzureReconciler.IsAbsenceStatus` and both sites call it.
The controller now reads back only absence rows, so a scan whose drift is healthy makes **no ARM
calls at all** rather than one per row._

_Proven before it was fixed, in that order. Eight regression tests were written first and all eight
failed against the unfixed code with `Assert.Single() Failure: The collection was empty` — the drift
row being dropped, which is the defect itself and not an unrelated failure. They cover both drift
statuses against all three non-`Deleted` verdicts, a drift row absent from the map entirely (the
shape the new controller filter actually produces), and a mixed plan where an absence row and a drift
row must be judged differently in the same pass._

_Then re-measured against live ARM, both directions, because a unit test cannot prove ARM's answers.
With the drifted prefixes recorded against resources that genuinely exist, the plan now reports
`PROPOSED FOR DELETION: 2  CanCommit=True` carrying `VNetPrefixRemoved` and `SubnetPrefixChanged`,
where before the fix it reported `0  CanCommit=False`. The counter-test matters as much and was run
on the same build: a credential that cannot see the other resource group still withholds all 10 rows
with the warning naming each one. The reconciler discriminates rather than merely blocking — checking
only the first of those two would have let an over-blocking regression pass silently. Note the rig
feeds `ApplyConfirmations` **unfiltered** input, so it proves the guard holds even without the
controller's filter, which is a superset of the shipped path._

_The audit's second complaint — that the withheld warning states rows "were missing from the
subscription listing" when they were not — needed **no text change** and was deliberately not given
one. After the fix, `stillLive` can only hold absence rows, for which that sentence is exactly
accurate: they really were missing from the listing and a direct read really did find them. The
sentence was only ever wrong about the drift rows, which no longer reach it._

_Left alone deliberately, as the finding itself scoped: the `VNetDeleted` status is overloaded and
also fires for "VNet exists but has no IPv4 address space left", where the direct read answers `Live`
and the row is withheld. That withhold is defensible rather than clearly wrong — the VNet does still
exist — and changing it means re-labelling the row, not archiving it. Untouched._

_Tests 576 → 584 (+8). Build clean, 0 warnings._

---

## E2. Subnet Edit silently destroys names, descriptions and tags that Create rejects `[×2]`

_E2 is fixed and committed. `EditSubnetViewModel` now carries `[NoHtml]` on `Name` and `Description`
and `[Bastet.Services.Security.Tags(MaxTags = 10, MaxTagLength = 50)]` on `Tags`, matching the
`CreateSubnetViewModel` rules for the same three columns. The form now refuses the input instead of
accepting it and letting the sanitizer rewrite it afterwards, which restores `[Required]` on `Name`
as a real constraint._

_**`[SafeText]` was deliberately not added**, which is where this departs from simple parity with
Create. The reason was checked rather than assumed: `[SafeText]` appears on exactly three properties
in the tree — `CreateSubnetViewModel.Name` and the two HostIp models — and **no Azure import path
applies it**. Imported names go through `SanitizeName` only, which strips markup and trims but does
not enforce the `^[a-zA-Z0-9\s\-_.,!?@#$%&()+=]*$` character class. So a stored name outside that
class is reachable, and adding `[SafeText]` to the edit model would make those rows uneditable until
renamed — a migration hazard well beyond the four defects reported. The three attributes above close
all four._

_Six parity tests were written first, as a new `SubnetViewModelValidationParityTests`, asserting that
Create and Edit refuse the same input. **The first version of them was wrong and passed for the wrong
reason**, which is worth recording: `NoHtmlAttribute` and `TagsAttribute` both return a
`ValidationResult` carrying **no member names**, so the helper's `MemberNames.Contains(...)` match
never hit and the failures it produced were on the Create side, not the Edit side. Rewritten to
validate one property at a time with `Validator.TryValidateProperty` and an explicit `MemberName`,
which attributes the failure exactly. Both attributes also resolve `IInputSanitizationService` from
the validation context, so the tests supply one — without it every rule fails with "Input
sanitization service not available" and the whole suite would pass vacuously._

_Proven by reverting only `EditSubnetViewModel.cs` in a scratch copy and re-running: five failures,
each reading `EditSubnetViewModel.<property> was accepted but should have been rejected`. Against the
fix all six pass. The sixth is a guard against over-correcting — ordinary values, including a tag
sitting exactly on the 50-character limit and exactly ten of them, must still be accepted by both
models. It caught a bug in its own fixture first: ten 50-character tags exceed the 255-character
`[StringLength]` that governs the field as a whole, which both models correctly reject._

_Tests 584 → 590 (+6). Build clean, 0 warnings._

---

## E3. A CIDR decrease from /31 or /32 strands a host IP on the new network address `[×1]`

_E3 is fixed and committed, and being a `[×1]` it was re-derived from scratch rather than taken on
trust. It holds. Both halves of the finding's fix were needed and both were applied: the controller
gate now reads `viewModel.Cidr != subnet.Cidr` so a decrease reaches the validator at all, and
`ValidateSubnetCidrChangeWithHostIps` gained a decrease arm rejecting an assignment that equals the
network address when the new CIDR is below /31. Verified that the controller change alone is
insufficient, exactly as the finding warned — the service's own `newCidr > originalCidr` gate would
still have skipped it._

_One refinement the finding did not have. It compares the host IP against the recorded
`networkAddress` directly, which silently assumes the subnet is aligned to the new CIDR. It is not
always: widening `10.0.0.2/31` to `/30` moves the network address down to `10.0.0.0`, so `10.0.0.2`
becomes an ordinary host and there is no collision. The arm is therefore guarded with
`ipUtilityService.IsValidSubnet(networkAddress, newCidr)`, which establishes that the recorded
address really is the new network address before comparing. An unaligned decrease is rejected before
it reaches here, so this is belt-and-braces rather than a behaviour change — but it means the rule
is stated in terms of what is true rather than what happens to be true._

_The comment at the old call site — "For CIDR decreases (subnet expansion), no host IP validation is
needed since making a subnet larger cannot cause host IPs to fall outside its range" — was deleted
rather than amended. It is the reasoning that produced the bug: true about the *range* and silent
about *reservation*._

_Six tests written first. The two defect cases (`/31`→`/30` and `/32`→`/24`, host IP on the network
address) failed against the unfixed code; the three guards passed throughout, which is the point of
them — the other address of a `/31` must stay assignable once widened, an ordinary widening between
two CIDRs that both already reserve the network address cannot create a new collision, and
**widening a `/32` to a `/31` must reserve nothing**, since RFC 3021 applies to the destination too.
That last one is what a fix written without the `newCidr < 31` guard would break._

_The sixth test is a controller test, added because the service tests do not pin the gate: reverting
`!=` back to `>` leaves all five service tests green. Proven by doing exactly that in a scratch copy
— and the first attempt at that probe was **wrong and had to be redone**, because a careless `sed`
rewrote both `!=` comparisons in the file rather than the one under test, including a pre-existing
and correct one ten lines above. Re-run against line 124 alone, the controller test is the single
failure._

_Tests 590 → 596 (+6). Build clean, 0 warnings._

---

# Low

## E4. Every transaction catch block rolls back before logging, destroying the original exception `[×2]`

_E4 is fixed and committed at all five sites. Each catch block now logs first and then calls a shared
`TransactionCleanup.RollbackQuietlyAsync`, which rolls back and logs rather than throws if the
rollback itself fails._

_Reproduced independently against a real SQL Server 2022 container with the pinned
Microsoft.Data.SqlClient 6.1.1, rather than trusting the audit's recorded measurement. Killing the
session mid-transaction and then replaying the shipped catch-block order gives:_

```
A1 commit threw   -> SqlException: A transport-level error has occurred when receiving results from the server.
A2 rollback threw -> InvalidOperationException: This SqlTransaction has completed; it is no longer usable.
   => the logger.LogError(ex, ...) on the NEXT line never runs.
B  rollback after successful commit threw -> InvalidOperationException: This SqlTransaction has completed.
C1 write threw    -> SqlException: Conversion failed when converting the varchar value ...
C2 rollback: succeeded - so the common case is NOT affected by the fix
```

_Case C is the one that mattered to get right. An ordinary failure — a constraint violation, a bad
conversion — leaves the transaction usable and still rolls back exactly as before, so the fix changes
nothing on the path that actually runs most often. Case B shows the provider has no guard at all:
rollback after a **successful** commit throws identically._

_A shared helper was chosen over five copies of the same try/catch, following the precedent the
migration-lock release already set. It is a new file, `Controllers/TransactionCleanup.cs`, which is
more structure than a five-line finding usually earns — the alternative was the same six lines
written out five times, which is exactly the kind of residue these rounds keep finding. The class is
`public` rather than `internal` so the test project can reach it: the repo has no `InternalsVisibleTo`
and no convention of testing internals, and Bastet ships as an application rather than a library, so
widening it costs nothing real._

_Three tests pin the helper, and their value was checked by removing the swallow in a scratch copy —
two of the three fail. The remaining one is the guard: a rollback that succeeds must log nothing._

_The audit's severity reasoning was carried over unchanged and is worth keeping: the underlying fault
is **not** invisible without this fix. EF Core still logs `Database.Transaction[20205]` with the
exception attached, and the exception-handler middleware logs the escaping one. What was lost is the
controller's own contextual message and correlation, and the action's intended response shape — an
AJAX caller receiving an HTML error page where it parses JSON._

_Tests 596 → 599 (+3). Build clean, 0 warnings._

---

## E5. Subnet Edit POST returns 500 on an out-of-range CIDR instead of redisplaying the form `[×2]`

**Confidence: confirmed.** Reproduced against the real action for 33, 99, −1 and `int.MaxValue`.

**Where:** [SubnetController.Edit.cs:235-238](../src/Bastet/Controllers/SubnetController.Edit.cs#L235-L238),
which sits **after** the try/catch that closes at :215.
[IpUtilityService.cs:16-19](../src/Bastet/Services/IpUtilityService.cs#L16-L19) is the throw;
[EditSubnetViewModel.cs:26](../src/Bastet/Models/ViewModels/EditSubnetViewModel.cs#L26) is the `[Range(0,32)]`.

`[Range]` makes `ModelState` invalid, which skips the guarded block entirely — and then makes
`!ModelState.IsValid` true at :235, calling `CalculateSubnetMask(99)` outside any try.
`ArgumentOutOfRangeException` escapes to `UseExceptionHandler`, so the operator gets a 500 instead of
the form carrying the "CIDR must be between 0 and 32" message the model already produced.

Reachability is narrow and worth stating plainly: `asp-for` on an int emits `type="number"` with
`min`/`max` ([_EditForm.cshtml:20](../src/Bastet/Views/Subnet/Edit/_EditForm.cshtml#L20)), so native
HTML5 constraint validation blocks a normal browser even with JS off. The vector is curl or devtools
with a valid antiforgery token, by an already-authenticated Edit-role user — the same bar D8 was
accepted under. Values that fail binding outright leave `Cidr == 0`, which is handled fine.

**Fix.** Mirror D8's guard, which was scoped to the Create GET and never applied here:
`bool hasUsableCidr = viewModel.Cidr is >= 0 and <= 32;` and compute `SubnetMask` only when it holds.
Do not clamp — the posted `Cidr` is redisplayed and clamping would rewrite what the operator typed.
`SubnetMask` is display-only on the error re-render, so nothing else depends on it.

## E6. Concurrency-conflict handler displays a Last Modified time that was never saved `[×2]`

**Confidence: confirmed.** The EF behaviour was measured, not assumed.

**Where:** [SubnetController.Edit.cs:179-181](../src/Bastet/Controllers/SubnetController.Edit.cs#L179-L181)
(the re-query under the comment "reload current data"),
[:190](../src/Bastet/Controllers/SubnetController.Edit.cs#L190),
[:218](../src/Bastet/Controllers/SubnetController.Edit.cs#L218) and
[:247](../src/Bastet/Controllers/SubnetController.Edit.cs#L247) (the fall-through repopulation, which
runs last and is what reaches the view),
[_NetworkInformation.cshtml:24-27](../src/Bastet/Views/Subnet/Edit/_NetworkInformation.cshtml#L24-L27).

The re-query is a **tracking** query on a context where the entity is still tracked in `Modified` state,
so EF returns that same instance and discards the row it read. Measured on EF Core 10.0.10: the tracking
re-query returned `ReferenceEquals == True` with the in-memory values, while `AsNoTracking()` on the same
context returned the real row.

**Failure scenario.** Subnet 5 last modified 10:05 by user B. User A submits a stale edit at 10:10;
`subnet.LastModifiedAt` is set to 10:10 before the save, which then throws
`DbUpdateConcurrencyException`. The page renders "Last Modified 1/1/2026 10:10 AM" directly above the
banner "This subnet was modified by another user… review the current values before saving." The one
current value on the screen is user A's own rejected attempt.

**Bounded, deliberately:** `RowVersion` is *not* corrupted — line 72 loads the fresh row inside the lock,
so only `OriginalValues` was rewound. Concurrency control and the retry work correctly; this is
display-only. The view renders no username, so only the timestamp is wrong.

**Fix.** `await context.Entry(subnet).ReloadAsync()` in the handler, or read the display fields with
`.AsNoTracking()`. **The fall-through query at :218 needs the same treatment** — it runs last, so fixing
:179 alone changes nothing on screen. Cheapest correct option: read from
`ex.Entries[0].GetDatabaseValues()`, which EF already fetched for the failed UPDATE.

## E7. Every leaf subnet's toggle becomes an inert "+" expander after any tree interaction `[×2]`

**Confidence: confirmed.**

**Where:** [site.js:33-42](../src/Bastet/wwwroot/js/site.js#L33-L42) (`updateToggleIcons`, no leaf test)
against [site.js:43-54](../src/Bastet/wwwroot/js/site.js#L43-L54) (the startup loop, which has one).
[_SubnetTreeItem.cshtml:32](../src/Bastet/Views/Shared/_SubnetTreeItem.cshtml#L32) emits
`.subnet-children` only when `ChildSubnets.Any()`.

Two disagreeing definitions of "leaf". The startup loop gives a childless subnet a flat `bi-dash`,
`cursor: default`, and unbinds its click handler. `updateToggleIcons` then iterates **all** toggles; for
a leaf, `children('.subnet-children')` is an empty set and `.is(':visible')` is false on an empty
collection, so the else branch repaints it as `bi-plus-square`.

**Failure scenario.** Load /Subnet, click Expand All (or Collapse All, or any parent toggle). Every leaf
now advertises collapsed children it does not have, and clicking does nothing — the handler was already
removed. The startup loop never re-runs, so only a page reload restores the dash.

Softening detail: the inline `cursor: default` survives the `.html()` rewrite, so the misleading
affordance is the icon only, not the cursor.

**Fix.** Give `updateToggleIcons` the same emptiness test: inside the `.each`, early-return when
`$children.children().length === 0`. One rule for what a leaf looks like instead of two that disagree.

## E8. The Create form calls `/api/subnets/calculate-mask`, a route that has never existed `[×1]`

**Confidence: confirmed.** Found twice in pass 1 (UI and dead-code beats), by neither beat in pass 2 —
so treat the absence as weak, per the tagging rule.

**Where:** [_SubnetFormScripts.cshtml:46](../src/Bastet/Views/Subnet/Create/_SubnetFormScripts.cshtml#L46).

A repo-wide grep for `calculate-mask` returns exactly one line: this one. There is no `Api` controller
and the only `[Route]` attributes in the tree are `ErrorController.cs:16` and `:61`. So the request
404s, is re-executed through `UseStatusCodePagesWithReExecute("/Error/{0}")` — registered in both the
dev and non-dev branches — and renders a full server-side Razor error page that jQuery then discards,
because a 404 sends it to `.fail()`, which computes the mask locally anyway.

**Failure scenario.** Every keystroke in the CIDR field costs a wasted round-trip plus a discarded
multi-KB error render. And because `CreateSubnetViewModel.Cidr` is a non-nullable int, `asp-for` renders
`value="0"` and `initializeForm()` fires one on **every** page load before the user types anything.

The success callback at line 47 is unreachable code that has never executed.

**Fix.** Delete the `$.get`/`.fail` wrapper and call the local helper directly:
`$('#subnetMaskDisplay').text(calculateSubnetMask(cidrValue));`. It is already the fallback, already
covers /0 through /32, and being synchronous removes the ordering hazard between concurrent responses.

## E9. `BatchCreateChildSubnets` discards every selected child when an encompassing entry is present `[×2]`

**Confidence: confirmed.** An existing passing test asserts the buggy outcome.

**Where:** [SubnetController.Azure.cs:322](../src/Bastet/Controllers/SubnetController.Azure.cs#L322)
(`if (!hasFullyEncompassingSubnet)` — skips the creation loop wholesale),
[:181](../src/Bastet/Controllers/SubnetController.Azure.cs#L181) (D9's guard, which does not check the
count), [:366](../src/Bastet/Controllers/SubnetController.Azure.cs#L366) (the unconditional success
message).

**Failure scenario.** POST with `parentId=1`, `isAzureImport=true`, `vnetName="prod-vnet"` and three
entries: one `FullyEncompassesVNetPrefix=true` covering `10.0.0.0/24`, plus `web 10.0.0.0/25` and
`app 10.0.0.128/25`. D9's guard passes, both /25s validate, and then the creation loop is skipped
entirely. Result: parent renamed and marked `IsFullyAllocated=true`, two submitted subnets never
created, and "Successfully renamed parent subnet… and marked it as fully allocated." Because
`ValidateSubnetCreation` refuses children under a fully-allocated parent, they can never be added later
without an operator clearing the flag by hand.

`SubnetControllerFullyEncompassingTests.cs:311` asserts exactly this, including
`Assert.Equal(0, childSubnetCount)` — a test pinning the defect.

**Severity, corrected down from the finder's medium** to match where its twin D22 was filed: the
selection cannot arise from real Azure or the wizard. `GetCompatibleSubnets` sets the flag only when the
prefix equals a VNet prefix *and* matches the chosen parent, and Azure forbids overlapping subnets
within a VNet, so the two cannot coexist in one result list. The trigger is a crafted or corrupted POST
from an authenticated admin behind antiforgery — D22's reachability exactly.

**Fix.** Extend the D9 guard at :181 with a count clause and reject the combination outright, mirroring
the planner's wording. **Do not** instead hoist the creation loop above the fully-allocated write — that
would produce a parent that is both fully allocated and has children, a state
`ValidateSubnetCanBeFullyAllocated` and `SetAllocationStatus` both forbid.

## E10. The reconcile 409's `warnings` payload is never rendered `[×2]` — **verifier split**

**Confidence: confirmed as to mechanism; contested as to harm.** This is the most interesting
disagreement in the round and is recorded rather than resolved.

**Where:** [_ReconcileScripts.cshtml:407-419](../src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml#L407-L419)
(`showCommitError` reads only `payload.error` and `payload.globalErrors`),
[SubnetController.AzureReconcile.cs:78-87](../src/Bastet/Controllers/SubnetController.AzureReconcile.cs#L78-L87)
(the Conflict body, `warnings = plan.Warnings` at :86).

The mechanism is not in dispute and was verified by every agent that looked: the 409 carries no
`globalErrors` key, so the list renders empty and both `warnings` **and** `subnetIds` are parsed and
thrown away. D3's struck paragraph claims "plan.Warnings now rides along in that Conflict response" —
it does, and nothing reads it.

**Five finders reported it; three verifiers confirmed it and three refuted it.** The refutation:
`ReconcileScan` runs the same `ConfirmProposedDeletionsAsync`, so re-running the scan — which the 409's
own message explicitly instructs — regenerates the identical text into `#rec-scan-warnings`. The
explanation is deferred by one click along the route the error prescribes, not lost. The confirmation:
that argument holds for a persistent RBAC loss but not for a transient throttle, where the explanation
is lost outright, and the operator meanwhile sees a message pointing at a stale browser view.

I have kept it because the mechanism is real and the fix is two lines; reconciliation may reasonably
decide the deferred path is good enough and close it as won't-fix.

**Fix.** In `showCommitError`, after the `globalErrors` loop:
`(payload.warnings || []).forEach(function (w) { list.append($("<li></li>").text(w)); });`.
They share `#rec-commit-error-list`, so no markup change is needed. Note `subnetIds` is likewise unread,
so the operator is not told *which* rows were withheld either.

## E11. The green "nothing to clean up" banner shows even when rows were withheld `[×1]`

**Confidence: confirmed.**

**Where:** [_ReconcileScripts.cshtml:247-248](../src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml#L247-L248)
— both toggles key on `items.length` alone.
[_StepReview.cshtml:30-33](../src/Bastet/Views/Azure/Reconcile/_StepReview.cshtml#L30-L33) is the banner.

**Failure scenario.** The credential loses access to a resource group. Every affected row is withheld,
so `items` is empty and the success banner renders — directly beneath the yellow warning saying rows
were withheld because Azure would not confirm their state. The page asserts as fact
("Everything imported from this subscription still exists in Azure") the very thing Bastet just recorded
it could not establish. It also renders beneath E1's factually wrong warning.

**Fix.** Gate on there being nothing to report at all, not nothing deletable. `warnings` is already in
scope at :219; `reviewItems` is declared at :250 and must be hoisted above the toggle if included:
`items.length > 0 || warnings.length > 0 || reviewItems.length > 0`. Better: when `items` is empty but
something was reported, render a neutral "Nothing can be offered for deletion from this scan" so the
page never claims a clean bill it did not earn.

## E12. Collapsing a subtree leaves the toggle showing the expanded icon `[×1]`

**Confidence: confirmed.**

**Where:** [site.js:12](../src/Bastet/wwwroot/js/site.js#L12) (`slideToggle(200)`) and
[site.js:15](../src/Bastet/wwwroot/js/site.js#L15) (`updateToggleIcons()` on the next line);
same shape at [site.js:28-29](../src/Bastet/wwwroot/js/site.js#L28-L29) for Collapse All.

jQuery applies `display: none` for a *hide* in the animation's completion callback, but sets `display`
up front for a *show*. So the icon update on the next synchronous line reads the pre-animation state:
on collapse, `:visible` is still true and the glyph is rewritten to minus. 200 ms later the children are
gone and the icon still reads "expanded". Nothing revisits it, so after the first collapse the plus
glyph is unreachable for that node. Expanding hides the bug because `slideDown` sets display first.

**Fix.** Prefer computing the target state *before* animating and setting the glyph from that boolean.
Passing `updateToggleIcons` as the `slideToggle` completion callback works for the toggle handler, but
for `#collapse-all` it is not sufficient on its own: line 27's synchronous `.show()` can change state,
and when the tree has no second-level `.subnet-children` the animated selector matches nothing and the
callback never fires — so keep the bare call there in addition.

## E13. The Details page's Create-Subnet modal reports "0 IP addresses" for /31 and /32 `[×1]`

**Confidence: confirmed.**

**Where:** [_SubnetCalculationScripts.cshtml:150-154](../src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml#L150-L154)
— `const usableSize = size > 2 ? size - 2 : 0;`.
The correct sibling implementation is
[Create/_SubnetFormScripts.cshtml:28-38](../src/Bastet/Views/Subnet/Create/_SubnetFormScripts.cshtml#L28-L38),
and the server's is [IpUtilityService.cs:98-113](../src/Bastet/Services/IpUtilityService.cs#L98-L113);
both special-case RFC 3021.

**Failure scenario.** On a `/30` with no children, `findOptimalCidr` returns 31 and writes it into the
modal, which then displays "Resulting subnet size: 0 IP addresses" with the Create button enabled.
Typing 32 gives the same. The server says 2 and 1 respectively — round 4's D4 turns on exactly that —
and the very next page shows "Usable IPs: 2" for the same /31 the modal just called 0.

The wider trigger is not the /30 edge case but any parent with CIDR ≤ 30 where the operator types 31 or
32, which is reachable from every Details page with an unallocated range.

**Fix.** Add the same special case: `const usableSize = cidr >= 31 ? (cidr === 31 ? 2 : 1) : Math.max(0, size - 2);`.
This is the third copy of this calculation; lifting one shared implementation into `site.js` would stop
a fourth from drifting, but the load-bearing change is the special case alone.

## E14. The reconcile confirmation screen overstates the blast radius `[×1]`

**Confidence: confirmed.**

**Where:** [_ReconcileScripts.cshtml:306](../src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml#L306)
(`$("#rec-confirm-count").text(chosen.length);`) and the cascade block at :310-333.

**Failure scenario.** Target "prod-vnet" (id 10, a VNet resource ID, one descendant) with imported child
"prod-web" (id 11). When the VNet is deleted in Azure, `BuildPlan` emits **both** rows — id 10 as
`VNetDeleted`, id 11 as `SubnetDeleted` — and `renderPlan` gives each an independent checkbox. Select
all, and the confirmation reads "You are about to delete **2** subnet(s)…" followed by "This **also**
archives **1** child subnet(s)…". The word "also" is false: that 1 child *is* one of the 2, so the
screen announces 3 subnets where only 2 exist. The server then reports "deleted **1** stale subnet(s),
archiving 2 subnet(s)" — so the operator is told 2 targets before confirming and 1 afterwards. On a
real VNet import with a dozen children it is a dozen-way discrepancy, on a destructive confirmation.

The defect is one-sided: `covered` correctly suppresses the child as a cascade *contributor*, while
`chosen.length` and `#rec-confirm-list` still count it as a delete *target*.

**Fix.** Report what the server will actually do: compute top-level targets as
`chosen.filter(i => !covered[i.subnetId])` and use that length for the count and the list, still posting
the full `confirmedIds` (the server dedupes by subtree anyway). The existing cascade sum then genuinely
is "also".

---

# Refuted — reported by a finder, killed by the verifier

18 of 45 verified findings were killed (40%). Recorded so round 6 does not rediscover them.

| Finding | Why it was killed |
|---|---|
| D10's three tests mock away the only file the fix changed (×2, both passes) | Facts accurate, no runtime defect. HEAD behaves correctly; the scenario is hypothetical future regression — the "could drift while currently agreeing" category. All three callers already catch and return `success=false`, so the new `throw;` cannot escape as a 500. |
| D3's scan-path confirmation call is unpinned by any test (×2, both passes) | Citation and coverage gap both real, but no wrong output from real code. Traced end to end: the RBAC-filtered scan produces the *correct* result at HEAD. The scenario only becomes wrong after the auditor edits the source. Test-coverage gap, not a defect. |
| `AzureResourceIdentity.ToPortalPath` ships with no test | Names inputs and the **correct** output. The finder's own input makes `IsAzureSubnet` return false, so the ID is returned unchanged and the link is right. The broken URL existed only in the scratch tree the finder edited. |
| `AzureServiceTests` asserts on `MockAzureService`, whose compatibility rule contradicts the real service | Divergence is real but lives entirely inside test code. No test asserts an application outcome that depends on it, and nothing in `src/` references the mock. |
| `AzureBulkImportPlanner.TruncateForName` is orphaned by D19 (×2, both passes) | Every fact verified — genuinely dead, orphaned by `bf120d6`. Dies on reachability: with zero call sites no input can reach it and no wrong output can leave it. Unused-but-harmless member. |
| `IsFeatureEnabled` on two view models is write-only (×2, both passes) | Exhaustively verified as never read. Write-only, always-true, no runtime effect; the finder's own scenario concedes "the rendered pages are byte-identical". |
| `IInputSanitizationService.SanitizeString` and its private support chain have no caller | Genuinely dead — which is what kills it. No input a user of the shipped app can supply reaches it. |
| `SubnetTreeViewModel.ParentSubnetId` is set on every node and never read | Verified write-only. Unused-but-harmless member with no wrong output. |
| `BulkImportPlanItem.Warnings` has no writer, making a JS block unreachable | The guard simply evaluates false, skipping nothing that should have shown. Hard errors render correctly via the block above it. |
| `AzureReconcileItem.IsVNetLevel` is written and never read | Worst case its own scenario names is one extra `true` in a JSON row the browser discards. |
| The stale `/subnets/` doc comment on `AzureLinkedSubnetSnapshot` contradicts D2 | Comment really is stale, but no production code performs the substring test — the only two occurrences are doc comments. The claimed outcome is unreachable and describes pre-fix code. |
| Reconcile 409 drops `warnings` (×3 — see **E10**) | Killed by three verifiers on harm, not mechanism: re-running the scan, which the error message instructs, renders the identical text. Kept as E10 because three other verifiers confirmed it; the split is recorded there. |

The pattern is consistent with round 4: what dies is **test-coverage observations and unused-but-harmless
members**. Nine of the eighteen are one or the other. The dead-code beat produced almost nothing that
survived — a strong signal that round 4's deletion pass was thorough.

# Watch list — not findings, but worth knowing

Carried forward from round 4, all re-checked and still accepted:

- **ForwardedHeaders trust-all with `AllowedHosts: "*"`**; the Development-only `DevAuthHandler` bypass;
  `GlobalSanitizationFilter` skipping nested `System.*` collections; `CollectDescendants` lacking a
  cycle guard; the unreachable IP-change branch in `ValidateHostIpUpdate`; the blind `catch {}` around
  the DataProtectionKeys probe; **C20** (the Azure reconcile check/act window).
- **`GlobalSanitizationFilter` runs after model binding and validation.** Round 4 flagged this for
  sanitizers that *lengthen* a value (D7). **E2 is the same hazard in the removing direction** — this is
  now a demonstrated defect class, not a theoretical one, and any new `[Sanitize*]` attribute needs a
  matching validator.
- **`MockAzureService.DefaultConfirmation` is `Deleted`.** This single default is why no test caught E1.
  Any test touching the confirmation path must set the verdict explicitly.
- **Still no `WebApplicationFactory` or integration host, and still no JS test harness.** E7, E8, E11,
  E12, E13 and E14 are all client-side and none can be pinned by an automated test today.
- **The usable-IP calculation now exists in three places** (server, Create script, Details modal). E13 is
  the copy that drifted.
- **Migration `.Designer.cs` snapshots still contain old column widths.** Correct and frozen — do not
  "fix" them.

New this round:

- **A real Azure tenant ID is committed in the repository.**
  [launchSettings.json:41](../src/Bastet/Properties/launchSettings.json#L41) carries
  `BASTET_OIDC_AUTHORITY` pointing at a specific tenant GUID, landed in `aedd0bd` (#133). Not a
  credential — tenant IDs are discoverable from any domain's OIDC metadata — and it produces no wrong
  output, which is why it is here and not in the findings. But it ties a public repository to a
  specific tenant, and launch profiles are developer convenience rather than shipped configuration.
  Replacing it with a placeholder (or `common`) costs nothing.

# Clean bill

Swept across both passes and produced nothing:

- **Authorization coverage.** Every public action across all controllers enumerated against the fallback
  policy (`SetFallbackPolicy(RequireAuthenticatedUser)`), so a forgotten attribute fails closed. The four
  `[AllowAnonymous]` actions each have a reason that holds. No missing or wrong policy.
- **Antiforgery.** Every `[HttpPost]` carries `[ValidateAntiForgeryToken]`, verified by grepping the
  attribute against the verb attribute rather than assuming. All three AJAX wizards post the token via
  the configured header and render `@Html.AntiForgeryToken()`. No state-changing GET.
- **XSS — swept deeper than round 4.** Every `.html()` call site in the three wizard scripts and
  `site.js` was read: all server- or Azure-derived text goes through the local `escapeHtml`, and the
  unescaped interpolations are server ints or fixed literals from a `switch`. Specifically checked the
  two encoding-insufficient sinks — inline `on*=` handlers containing Razor (none anywhere) and Razor
  inside `<script>` (two sites, both safe).
- **SQL injection / command injection.** The only raw SQL remains the parameterised
  `sp_getapplock`/`sp_releaseapplock` calls. No `FromSqlRaw`, no `Process.Start`, no `HttpClient`.
- **SSRF.** The only outbound calls are ARM SDK calls; no host or scheme is caller-controlled.
- **Open redirect.** The one input-derived redirect target is guarded by `Url.IsLocalUrl`; the AJAX
  `redirectUrl` values are `Url.Action`-generated.
- **Security response headers.** D11's fix is intact, correctly placed below both error handlers and
  above `UseStaticFiles`. `BASTET_FRAME_ANCESTORS` is validated at startup through `HttpHeaderValue`,
  which correctly mirrors Kestrel's rule.
- **The Azure deletion path under real RBAC partial visibility — proven live, both directions.** With
  disjoint credentials, `BuildPlan` proposed all 10 (and symmetrically 3) imported rows for archival;
  D3's direct-read guard withheld every one. ARM returns **403** for cross-RG resources at both VNet and
  subnet level, while genuinely absent resources return **404** (`ResourceNotFound` for VNets,
  `NotFound` for subnets) — exactly as `ConfirmOneAsync`'s comment claims. Nothing live was ever offered
  for deletion.
- **`GetVNetInventory` fails closed — proven live.** An authentication failure returns `Success=false`
  with an error, not an empty list, so the reconciler cannot mistake "could not ask" for "nothing there".
- **IPv6 handling of the inventory — proven live.** A VNet whose only address space is
  `fd00:1234:5678::/48` is filtered out of the inventory entirely, honouring the documented IPv4-only
  contract.
- **The 43 round-4 fixes, reviewed as atomic commits.** Each diffed against its struck paragraph. The
  five that deviated from their finding's recommendation (D7, D22, D24, D29, D41) each do what their
  paragraph says they do. E1 is the only regression found; every other fix holds.
- **Test quality of the ~45 tests added by PR #141.** No assertion weakened to let a fix land, none
  asserting behaviour a fix was meant to change. The 305 lines removed from `SubnetOperationTests.cs`
  were traced: the coverage moved rather than vanished. (E9's test is a pre-existing test pinning a
  pre-existing defect, not a weakened one.)
- **Locking and lifecycle.** `sp_getapplock` session ownership under EF connection pooling,
  release-on-every-path, command-timeout arithmetic, transaction boundaries and DI lifetimes all hold.
  No captive dependencies. E4 is about diagnostics, not lock or transaction correctness.
- **Dead code.** Round 4's deletion pass was thorough: everything this round's beats surfaced was
  either already gone or an unused-but-harmless member that the verifiers correctly killed.

---

## Suggested order of attack

1. **E1** — a documented, badge-rendered reconcile capability is silently and permanently dead, and the
   operator is given a false explanation. Fix the status partition and add the `Live`-verdict test.
2. **E2, E3** — both persist a state the validated path rejects. E2 defeats a `[Required]`.
3. **E4, E5, E6** — diagnostics and error-path defects; E4 is five sites and one mechanical change.
4. **E7–E14** — client-side and messaging. E11 pairs naturally with E1, and E10 may reasonably be closed
   as won't-fix given the verifier split.
