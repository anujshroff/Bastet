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

_L2 is fixed and committed as proposed: `_ChildSubnets.cshtml` injects `IUserContextService` and the
anchor's condition becomes `Model.CanAddChildSubnet && ChildSubnetsUserContext.UserHasRole(ApplicationRoles.Edit)`.
The AND is load-bearing and was kept for the reason the finding gives - the `else if` branches print the
**Fully Allocated** and **Has Host IPs** badges, which are capacity statements a read-only user must keep
seeing, and replacing the condition rather than ANDing it would have suppressed them._

_Measured on a rig whose only edit to HEAD is `DevAuthHandler` reading an `X-Rig-Roles` header to choose
the role claim set (header absent => `Admin`, i.e. identical to HEAD); views, view models and every
`[Authorize]` policy stock. Three subnets were seeded to cover all three arms of the chain - empty and
not full, marked fully allocated, and carrying a host IP - and each was rendered as View, Edit, Delete
and Admin, before and after:_

```
                            BEFORE                       AFTER
subnet 1 empty/not full  View  anchor=True   ->   View  anchor=False
                         Edit/Delete/Admin  anchor=True  (unchanged)
subnet 2 fully allocated all roles  fullyAllocated badge=True   (unchanged both builds)
subnet 3 has host IPs    all roles  hasHostIps badge=True       (unchanged both builds)

GET /Subnet/Create?parentId=1 : none 403 | View 403 | Edit 200 | Delete 200 | Admin 200
GET /Subnet/Details/1         : none 403 | View 200   (200 for View after the fix too)
```

_So the only delta is the anchor the View principal could not follow; every badge and the page's own
status are unchanged for every role. The visible result for a View user on an empty, non-full subnet is
a bare "Child Subnets" card header with neither button nor badge, which matches how the other cards
already render for that principal._

_The cheaper interim - a `ViewBag.CanCreateSubnets` flag set in `SubnetController.Read.cs` beside the
existing `ViewBag.CanImportFromAzure` - was **not** taken. The finding marked it plausible but never
built, and the injected-partial form is the one that was measured; `_UnallocatedRanges.cshtml`,
`_HostIpAssignments.cshtml` and `Index.cshtml` all already inject `IUserContextService` the same way, so
this is the established idiom in these views rather than a second mechanism._

_Suite unchanged at **738** - no test in the repo renders a view (see `R6`), so there is nothing to pin
this with; the per-role render matrix above is the verification._

---

_L3 is fixed and committed with both gaps the verifiers found closed, in one commit. The delegated
per-row handler now calls a new `syncSelectAllSubnets()`, which recomputes the master from the rows
(`checked`, plus `indeterminate` for a partial selection); `loadSubnets`' `beforeSend` clears
`indeterminate` alongside the `checked` reset it already did; and the identical construction in the
reconcile wizard got the same two changes - `syncRecSelectAll()` on `.rec-item-checkbox`, and an
`indeterminate` reset beside the existing `#rec-select-all` reset on re-scan._

_The `checked > 0 &&` conjunct was kept in both, as the finding says: over an empty list `0 === 0`
would otherwise tick the master. `.prop()` was kept too - it fires no `change` event, so the sync
cannot re-enter the master's own handler, and this is not the synthetic-event shape round 4's `D1`
removed._

_Measured in real Chromium against real ARM on two publishes, unfixed `4a59ddf` and the fix, each on
its own port and catalog. Single-VNet import wizard, walking the motivated path - untick down to an
empty selection:_

```
                          BEFORE (HEAD)                      AFTER
E3 untick both rows   master=True  over 0 rows ticked   master=False  (honest)
E4 click 'Select All' selected 0 rows  (visible no-op)  selected 2 rows
P2 untick ONE of two  master=True, indeterminate=False  master=False, indeterminate=True
P3 re-enter the step  -                                 indeterminate cleared
```

_The reconcile sibling was driven with **genuinely stale rows**, not a stub: `rig-vnet-bulk` and its
three children were bulk-imported so Bastet held rows stamped with live `AzureResourceId`s, then that
VNet was deleted from ARM, so the scan found four real stale items._

```
                          BEFORE (HEAD)                      AFTER
R3 untick every row   master=True  over 0 of 4 ticked   master=False
R4 click 'Select All' selected 0 rows                   selected 4 rows
R5 untick 1 of 4      indeterminate=False               indeterminate=True
```

_Gap 1 is the reason the `beforeSend` line was needed: `indeterminate` is a second piece of master
state that survives a rebuild of the rows exactly as `checked` does, so the proposed fix on its own
would have left a dash rendering over a freshly emptied list - a new staleness of the same class the
comment at that reset exists to close. Gap 2 is the reconcile wizard, which the finding recorded as an
unfiled sibling; it is the only Azure-driven DELETE path, so it is fixed here rather than deferred, and
it is removed from the watch list below._

_The finding's rejection of its own cheaper interim was accepted without rebuilding it: clearing the
master only when a row is unticked leaves it unticked after the operator re-ticks the last row, which
is the same lie in the other direction. Zero pageerrors in every run. Suite unchanged at **738** -
no rendered-view test seam exists (see `R6`), so the browser legs are the verification._

---

_L4 is fixed and committed with the primary fix, verbatim as the finding specifies it: `committing` and
`committed` are declared beside `lastSelection`/`previewSeq`; `committing` is set in `commitImport`'s
`beforeSend` and cleared in its `complete`; `committed` is set on the `result.success` branch;
`invalidatePlan()` clears `committed` and deliberately **not** `committing`; and the re-entry line
becomes `$("#bulk-confirm-commit-btn").prop("disabled", committing || committed).toggle(!committed);`._

_Reproduced on the unfixed build with **no concurrency and no interception at all** - the simplest of
the three routes the finding records. Commit once, then inside the 2 s redirect window go back and
return to step 4:_

```
                                   BEFORE (HEAD)                    AFTER
via #bulk-back-to-preview-btn   confirm visible=True disabled=False   visible=False disabled=True
                                SECOND CLICK ACCEPTED                 refused
                                POSTs: 200 then 400                   POSTs: 200 only
via the #step3-tab pill         confirm visible=True disabled=False   visible=False disabled=True
                                SECOND CLICK ACCEPTED                 refused
                                POSTs: 200 then 400                   POSTs: 200 only
```

_The pill leg is the one that condemns the cheaper interim, and it was measured rather than assumed:
disabling `#bulk-back-to-preview-btn` cannot help, because `activateTab` deliberately leaves a visited
step's pill clickable and the pill reaches the same re-arm. **The interim was not taken.**_

_Both non-regression legs pass on the fix. With nothing in flight, an ordinary Back to Preview ->
Continue to Commit -> Confirm still commits (200, "Created 2 VNet target(s), 5 child subnet(s)") - no
operator is stranded. And a genuinely refused commit stays retryable: a colliding `10.150.1.0/24` was
created out of band through the ordinary Create form in a second cookie jar while the plan was on
screen, the commit returned 400 with `Azure subnet 'rig-bulk-a' ... already exists in Bastet`, and
Confirm came back `visible=True disabled=False`. `showCommitError`'s `prop("disabled", false)` is
therefore left ungated, with a comment saying why, since this fix is exactly what would tempt a later
change to gate it._

_No temporal dead zone: both flags are `let`s at the top of the same `$(document).ready` scope and every
handler closes over them, so this is not round 10's `J9` shape. Zero pageerrors across all six runs,
which is the only evidence that matters here - Razor compiles the view without parsing the embedded
JavaScript, so a broken script still builds at 0 warnings._

_Two residues are left deliberately, both recorded by the finding and neither unsound: re-previewing
*while* a commit is in flight leaves `committing` true when the new step 4 is entered, so Confirm needs
one more Back to Preview -> Continue to Commit once the response lands; and the success panel is still
hidden on re-entry to step 4. The simultaneous both-panels render the finding measured needs genuine
lock contention to open the window and was **not** re-measured here - the re-arm that causes it is
closed, which is what the fix is for._

_Suite unchanged at **738**; no rendered-view test seam exists (see `R6`)._

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
