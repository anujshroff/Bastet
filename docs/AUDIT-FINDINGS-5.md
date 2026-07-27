# Bastet — Round-5 Audit Findings

**Target:** branch `main`, HEAD `bf120d6` "Audit 4 Cleanup (#141)" (all 43 round-4 fixes squashed).
**Test baseline:** 576 passing, 0 failed. `dotnet build --no-incremental` clean, 0 warnings.
**Date:** 2026-07-26

## Reconciliation — complete

**All 14 findings fixed, none refuted on re-verification.** One commit each, on `task/audit-5`.

**Final state:** 603 passing (576 → 603, +27), 0 failed. Clean rebuild from deleted `bin`/`obj`,
0 warnings. Working tree clean, no scaffolding committed.

**Closing sweep.** Every major area was requested from the real application running against a real
SQL Server, asserting titles and content rather than status codes: home, subnet hierarchy, create,
details, edit, delete, deleted subnets, purge, host IP create, all-deleted-host-IPs, both Azure
wizards, the reconcile wizard, and the 404/500/access-denied pages. Two redirects were classified
rather than glossed: `/Azure/Import` 404s without a subnet id, and the purge page redirects when
there is nothing to purge — both by design. Security headers ride on both a normal 200 and the error
page. Three fixes were confirmed live through HTTP: E2 (markup name rejected, stored value
untouched), E5 (out-of-range CIDR renders the form, not a 500), and E10 (the 409 carries its
`warnings`).

**The Azure surface was driven end to end against live ARM**, with the discrimination check that
matters — a reconciler that blocks everything is as broken as one that deletes everything:

| Linked row | Azure reality | Result |
|---|---|---|
| `invisible-link` | 403, resource group not visible | **withheld**, warning names it; delete refused 409 |
| `really-gone` | 404, genuinely absent | **offered**, deleted and archived |
| `drifted-prefix` | 200, exists with a changed prefix | **offered** — this is E1's fix, dead before it |

**Log:** 1467 lines, **zero `fail:`**, five `warn:`. Three are `Azure denied access to … (403), so it
cannot be reported as deleted` — the deliberate permission probe working, logged by design, not a
fault. The other two are environmental and pre-existing: DataProtection has no XML encryptor
configured for a local run, and EF advises on `QuerySplittingBehavior` for a multi-`Include` query.

**Coverage was not re-run.** This round deleted no code — E8 removed a call, not a declaration — so
there is no dead-code delta to compare against a reference sweep.

**Deliberately not done**, each argued in the struck entry that owns it: `[SafeText]` was **not**
added to the subnet edit model (E2), because no Azure import path applies it and existing rows could
become uneditable; the overloaded `VNetDeleted` "exists but has no IPv4 space" sub-case was left
withholding (E1); the three copies of the usable-IP calculation were **not** consolidated (E13); and
`subnetIds` in the reconcile 409 remains unrendered (E10).

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

_E5 is fixed and committed, mirroring D8's guard on the Create action — the one entry point D8's
struck paragraph explicitly scoped itself to. A `hasUsableCidr` local now gates the mask calculation
at the tail of the Edit POST, leaving `SubnetMask` empty when the posted CIDR is outside 0–32._

_The posted `Cidr` is **not** clamped, following the finding's own warning. It is redisplayed in the
form, so rewriting it would silently change what the operator typed and hide the mistake the range
message is about to explain. `SubnetMask` is display-only on an error re-render — nothing in the Edit
views or the POST path reads it back — so leaving it empty costs nothing._

_Four tests written first, one per boundary (33, 99, −1 and `int.MaxValue`), all four failing against
the unfixed action with `System.ArgumentOutOfRangeException : CIDR must be between 0 and 32
(Parameter 'cidr')` — the defect itself. They assert the action returns a view with the `Cidr` error
intact **and** that the posted value survives unclamped, so a future "fix" that clamps would fail
them. Values that fail model binding outright leave `Cidr` at 0, which was always handled._

_Reachability is narrower than a casual reading suggests and is worth recording so it is not
re-raised as more serious than it is: `asp-for` on an int emits `type="number"` with `min`/`max`, so
a normal browser blocks the submit even with JavaScript disabled. The vector is a crafted POST by an
already-authenticated Edit-role user holding a valid antiforgery token — the same bar D8 was accepted
under — and the blast radius is a 500 on that caller's own request. The guarded block is skipped
entirely, so no CIDR-change validation is bypassed and nothing is written._

_Tests 599 → 603 (+4). Build clean, 0 warnings._

---

## E6. Concurrency-conflict handler displays a Last Modified time that was never saved `[×2]`

_E6 is fixed and committed. Both re-queries — the one in the `DbUpdateConcurrencyException` handler
and the fall-through repopulation that runs last — now use `AsNoTracking()`. Both were needed: the
fall-through query is what actually reaches the view, so fixing only the handler would have changed
nothing on screen._

_Reproduced against a real SQL Server 2022 container, because the defect is unreachable on the SQLite
the suite runs on: `[Timestamp] byte[] RowVersion` is only DB-generated on SQL Server, so
`DbUpdateConcurrencyException` never fires under the test provider. Driving Bastet's own
`BastetDbContext` through the exact handler sequence — load tracked, mutate, lose the optimistic
concurrency race — gives:_

```
loaded            LastModifiedAt=10:05  (this is user B's saved value)
SaveChanges threw DbUpdateConcurrencyException - handler entered
tracking re-query LastModifiedAt=01:35  Name=webA
                  same object as the dirty entity? True
AsNoTracking      LastModifiedAt=10:05  Name=web
```

_`ReferenceEquals` being true is the mechanism: identity resolution hands back the tracked instance
and discards the row it just read. **The measurement corrected the finding on one point.** The audit
expected the screen to show the submitting user's own timestamp; it actually shows `01:35`, the wall
clock at the moment of the failed save, because `BastetDbContext.UpdateAuditFields` re-stamps
`LastModifiedAt = UtcNow` on every `SaveChangesAsync` attempt including the one that fails. So the
value displayed as "current" was never anyone's edit — it is simply *now_.

_No permanent test ships with this one, deliberately. Reaching the defect requires a real
`rowversion`, and the suite has no SQL Server; a SQLite test would either not compile the scenario or
pass vacuously. The rig was ephemeral and is deleted. The audit's cheaper interim —
`ex.Entries[0].GetDatabaseValues()` — was not taken: it would fix only the handler and leave the
fall-through query, which is the one that wins._

_Confirmed display-only, as the finding said: `RowVersion` was never corrupted, because the entity is
loaded fresh inside the lock and only `OriginalValues` is rewound, so the retry keeps working._

_Tests 603 → 603 (unchanged). Build clean, 0 warnings._

---

## E7. Every leaf subnet's toggle becomes an inert "+" expander after any tree interaction `[×2]`

_E7 is fixed and committed. `updateToggleIcons` now early-returns for a subnet with no children,
using `$children.children().length === 0` — the same test the startup loop already applies, so there
is one definition of "leaf" instead of two that disagree._

_Reproduced in a real browser before fixing, not reasoned about. The rig loads the **actual
`site.js`** read from the repo at run time, against **jQuery 4.0.0 from the same CDN URL
`_Layout.cshtml` pins**, into a tree built from `_SubnetTreeItem.cshtml`'s shipped markup with only
the Razor stripped. The version matters: the defect turns on `:visible` returning false for an empty
jQuery set, which is a library behaviour, not something to take on trust from a different build._

```
--- after ready ---                --- after clicking Expand All ---
  parent 'corp' : bi-dash-square     leaf 'web'  : bi-plus-square
  leaf   'web'  : bi-dash            leaf 'solo' : bi-plus-square
  leaf   'solo' : bi-dash
```

_Both a nested leaf and a top-level childless subnet flip, confirming it is not specific to depth.
Against the fix both stay `bi-dash` through Expand All, Collapse All and a parent toggle click._

_The finding's "cheaper interim" — marking leaves at startup with a class and excluding them by
selector — was not taken. It keeps the two definitions of "leaf" and just adds a third mechanism to
hold them together; the early return removes the disagreement outright._

_No test ships. There is no JS harness in the repo, and adding Playwright to the suite for one
cosmetic defect is far beyond this finding's weight — the rig stayed in the scratchpad. Recorded here
instead, which is what the watch list already anticipated for client-side findings._

_Tests 603 → 603 (unchanged). Build clean, 0 warnings._

---

## E9. `BatchCreateChildSubnets` discards every selected child when an encompassing entry is present `[×2]`

_E9 is fixed and committed. A second guard now sits beside D9's: an encompassing entry submitted with
any other subnet is refused outright, naming how many others were sent. The message tells the caller
how to proceed — submit the encompassing entry alone, or the others without it — rather than leaving
them to work out why a "successful" import created nothing._

_Rejecting was chosen over the alternative of hoisting the creation loop above the fully-allocated
write, and the finding is right that the alternative is worse: it would produce a parent that is both
marked fully allocated **and** has children, a state `ValidateSubnetCanBeFullyAllocated` and
`SetAllocationStatus` each forbid. Trading a silent drop for a corrupt one is no trade._

_**An existing test asserted the defect** and had to be rewritten, which is the part worth flagging
for future rounds. `BatchCreate_MixedSubnets_HandlesFullyEncompassingCorrectly` asserted the parent
was renamed, marked fully allocated, and that `childSubnetCount == 0` — i.e. it pinned "the two
submitted /25s vanish and we call that success" as the contract. It is now
`BatchCreate_EncompassingEntryWithSiblings_IsRefusedAndWritesNothing`, asserting the post is refused
and that the parent is neither renamed nor flagged. Rewritten first, and it failed against the
unfixed action at `AssertImportFailureRedirect` — no error was reported because the action had
happily returned success._

_The guard is `subnets.Count > 1`, so the legitimate case D9 protects — a single encompassing entry
inside a real Azure import — is untouched, and its test still passes._

_Severity was recorded as low rather than the finder's medium, and that holds: the combination cannot
come from Azure or the wizard, since subnets within a VNet may not overlap and the flag is only set
when the prefix matches the chosen parent exactly. The vector is a crafted post from an authenticated
admin behind antiforgery — the same reachability D22 was accepted under._

_Tests 603 → 603 (unchanged; one test rewritten, none added or removed). Build clean, 0 warnings._

---

## E10. The reconcile 409's `warnings` payload is never rendered `[×2]` — **verifier split**

_E10 is fixed and committed. `showCommitError` now renders `payload.warnings` into the same
`#rec-commit-error-list` the `globalErrors` loop already fills, mirroring the line directly above it._

_**The split was resolved in favour of fixing**, and the reasoning is recorded because the file left
it open. Neither side disputed the mechanism: a grep of the 424-line script confirms `warnings` is
read at lines 219–222 only, inside `renderPlan`, which runs for the scan response and never for a
commit — so the 409's explanation was parsed and dropped. The refuters argued the harm is nil because
re-running the scan, which the error message itself instructs, regenerates the identical text. That
holds for a **persistent** cause such as a lost RBAC assignment. It does not hold for a **transient**
one: an ARM throttle that has cleared by the time the operator re-scans produces a clean scan and no
explanation at all, leaving them with a message that reads like a stale browser view. Against a
two-line fix mirroring adjacent code, that residual case is worth closing._

_No rig was used and none was warranted: the defect is the absence of a reader, which grep settles
outright, and the fix is a copy of the loop above it against an element that already exists in
`_StepConfirm.cshtml`. Recording that plainly rather than implying a browser run that did not happen._

_Left undone deliberately: `subnetIds` in the same 409 body is likewise never read, so the operator
still is not told *which* rows were withheld by name in the error panel — though the warning text the
fix now renders does name them. Adding a second unrendered field to the panel is a UI decision beyond
this finding, and the warning covers the practical need._

_Tests 603 → 603 (unchanged). Build clean, 0 warnings._

---

## E11. The green "nothing to clean up" banner shows even when rows were withheld `[×1]`

_E11 is fixed and committed. The success banner is now gated on a `nothingToReport` local —
`items.length === 0 && warnings.length === 0 && reviewItems.length === 0` — so it only appears when
the scan genuinely established that there is nothing to say._

_The finding's own correction was applied: `reviewItems` was declared **after** the toggle it now
participates in, so the declaration is hoisted to sit beside `items`. Dropping the suggested
one-liner in as written would not have compiled meaningfully — it would have referenced a `const` in
its temporal dead zone. The finding caught this itself; it is recorded because it is the kind of
detail that turns a two-line fix into a broken page._

_The alternative of adding a neutral "nothing can be offered for deletion from this scan" message was
not taken. It needs new markup, and the warnings block sitting directly above already says precisely
why nothing is offered — a second message would restate it less specifically. Hiding the false claim
is the whole of the defect._

_Verified the edited script still parses, by extracting the `<script>` body with the four
`@Url.Action` expressions substituted (not retyped) and running it through `new Function(src)` in
chromium: `OK`. Worth doing because a syntax error in a `.cshtml` is invisible to the C# compiler and
to the test suite — it would surface only when the page is requested. This also covers E10's edit to
the same file. The banner's rendered behaviour is exercised in the closing sweep._

_Being a `[×1]` the premise was re-checked rather than assumed: both toggles at that point keyed on
`items` alone, so a scan with zero deletable rows and a non-empty warnings array did assert
"Everything imported from this subscription still exists in Azure" directly beneath a warning saying
otherwise._

_Tests 603 → 603 (unchanged). Build clean, 0 warnings._

---

## E12. Collapsing a subtree leaves the toggle showing the expanded icon `[×1]`

_E12 is fixed and committed. The toggle handler now passes `updateToggleIcons` as `slideToggle`'s
completion callback, so the icon is chosen from the settled state instead of the one being animated
away._

_The verifier's correction was applied rather than the finder's original: for `#collapse-all` the
bare `updateToggleIcons()` call is **kept in addition to** the callback. Replacing it would break a
one-level tree, where the deeper-levels selector matches nothing, the callback never fires, and the
first level would never be refreshed. `#expand-all` was left alone — `slideDown` sets `display` up
front, so its existing bare call already reads the correct state._

_Reproduced and then re-verified in a real browser with the pinned jQuery 4.0.0, using the same rig as
E7:_

```
before:  children actually visible? False   parent icon: bi-dash-square   <- claims expanded
after:   children actually visible? False   parent icon: bi-plus-square
```

_The round trip was checked too, since changing **when** the icon updates could easily break the
other direction: re-expanding restores visible children and the minus icon, and Collapse All followed
by Expand All leaves every parent on minus and every leaf on its flat dash. That last check also
confirms E7's fix still holds through the animated paths._

_As a `[×1]` the mechanism was confirmed directly rather than accepted: jQuery defers `display:none`
for a hide into the animation's completion, which is why only the collapse direction was ever wrong
and why the bug survived unnoticed — expanding happened to produce the right icon by accident._

_Tests 603 → 603 (unchanged). Build clean, 0 warnings._

---

## E13. The Details page's Create-Subnet modal reports "0 IP addresses" for /31 and /32 `[×1]`

_E13 is fixed and committed. `updateSubnetSize` now carries the RFC 3021 special case the server and
the Create page's copy both already had, and clamps the ordinary branch with `Math.max(0, size - 2)`._

_Verified by extracting the shipped function **verbatim** — no retyping, only the jQuery line swapped
for a `return` — running it in chromium, and comparing every value against
`IpUtilityService.CalculateUsableIpAddresses` executed directly from the real assembly:_

```
cidr | modal JS | server | match          cidr | modal JS | server | match
/0   | 4294967294 | 4294967294 | yes      /30  |        2 |      2 | yes
/8   |   16777214 |   16777214 | yes      /31  |        2 |      2 | yes
/24  |        254 |        254 | yes      /32  |        1 |      1 | yes
/29  |          6 |          6 | yes
```

_Checking the whole range rather than only the two reported CIDRs was the point: `/0` confirms the
clamp did not introduce a precision or sign problem at the other extreme, where the count exceeds
what a 32-bit int holds. Before the fix, `size > 2 ? size - 2 : 0` returned 0 for `/31` (size 2) and
`/32` (size 1) — the two cases where the subtraction should not happen at all._

_The finding's optional suggestion — hoisting this into `site.js` so all three copies share one
implementation — was **not** taken. It is the right instinct and is recorded on the watch list, but
it touches two pages plus the shared script to fix a defect that lives in one function, and an
unrequested refactor riding along in a fix commit is exactly the residue these rounds keep finding._
_Tests 603 → 603 (unchanged). Build clean, 0 warnings._

---

## E14. The reconcile confirmation screen overstates the blast radius `[×1]`

_E14 is fixed and committed. A `targets` local — `chosen.filter(i => !covered[i.subnetId])` — now
drives both `#rec-confirm-count` and `#rec-confirm-list`, so the screen counts and names what the
server will actually report as deleted. The cascade sum iterates `targets` directly rather than
`chosen` with a skip, which is the same set by construction and one fewer thing to keep in step.
`confirmedIds` still carries every selected id: the server dedupes by subtree, and narrowing what is
posted would change behaviour rather than reporting._

_Verified by lifting the `covered`/`targets`/sum block **verbatim** out of the shipped file — pulled
by offset, not retyped — wrapping it in a function and running it in chromium against the finding's
own scenario, a deleted VNet target plus its imported child, both ticked:_

```
{"targets":1,"descendants":1,"hostIps":2,"names":["prod-vnet"]}
```

_So the screen now reads "1 subnet(s)… this also archives 1 child subnet(s) and 2 host IP
assignment(s)" — three numbers describing two subnets, and agreeing with the server's "deleted 1
stale subnet(s), archiving 2 subnet(s)". Previously it read 2 targets **plus** 1 cascaded child,
announcing three subnets where two exist, and then contradicted itself after the delete._

_The first run of this check reported `hostIps: 0`, which was a **fault in my fixture, not the
code**: the plan's counts are subtree-inclusive, so a parent's `hostIpCount` already contains its
children's. Corrected to 2 and re-run, rather than recording a number that would have misled the next
reader._

_The finding's cheaper interim — leaving the count alone and rewording the cascade sentence to a
total — was not taken. It fixes the arithmetic by changing the prose around it while
`#rec-confirm-list` still enumerates rows that are not separate deletions._

_Whole script re-parsed in chromium after the edit: `OK`._

_Tests 603 → 603 (unchanged). Build clean, 0 warnings._

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
