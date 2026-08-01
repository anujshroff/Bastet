# Bastet - Round-11 Audit Findings

| | Value |
|---|---|
| Round | **11** (finding letter **K** - findings are `K1` ... `K5`) |
| Branch | `audit/round-11` |
| HEAD | `09cee3d` - *"Audit 10 Cleanup (#155)"* |
| Build | **0 warnings, 0 errors** |
| Tests | **730 passed**, 0 failed, 0 skipped |
| Date | 2026-08-01 |

Every line number below was re-derived against the working tree at `09cee3d` by at least one verifier,
and every surviving finding was reproduced against a live rig - real SQL Server, the real application,
a real browser, and two Azure service principals with disjoint RBAC over two resource groups.

---

## Verdict

**Read `K1` first.** It is the only finding above `low`, and it is a hole in round 10's own J2 guard:
the bulk-import wizard leaves the **Commit** step clickable across a re-preview, so an operator can
confirm an import whose plan was never rendered. The commit then adopts a subnet the operator never
selected - stamping it with an `AzureResourceId` that **no screen in the application can clear** - and
J2's approved-plan check does not fire, because the wizard posted either no expectation at all or one
stamped from a plan that landed behind the operator's back. Verified against a control on the same
build: without the re-preview the identical race is correctly refused with a 409 and nothing is
written. The fix is one line - `invalidatePlan()` in the `#bulk-go-preview-btn` handler - and it was
built, published and driven in a real browser, closing all three legs with no regression to the happy
path.

Everything else is small. Three `low` findings all sit on the same J2/bulk-import surface: the 409's
`differences` list is computed and then discarded by the client (`K2`), two malformed JSON bodies turn
a modelled 400 into an unhandled 500 (`K3`), and the approved-plan expectation carries no child-subnet
information so children can be written under names the preview never showed (`K4`). One `info`
finding is a display contradiction on `/Subnet/Details` for `/31` and `/32` (`K5`).

**This was a quiet round.** 40 raw findings collapsed to 19 candidates and only 5 survived; 14 were
killed, most of them for the same reason - a true mechanical observation with no reachable wrong
output behind it (dead code, unpinned tests, comment inaccuracies, hypothetical future maintainers).
Nothing critical or high was found. No data-loss, privilege-escalation or cross-tenant defect
survived verification, and the two Azure service principals with disjoint RBAC produced no new
isolation failure.

Two corrections to proposed fixes are worth as much as the findings themselves, and both are recorded
in place: **`K4`'s cheaper interim should not be taken** (it deletes deliberate naming behaviour and
closes nothing structurally), and **`K4`'s new divergence message is dead text until `K2` is fixed** -
`showCommitError` never renders `differences`, so the operator is told a plan changed without being
told how. Fix `K2` in the same change as `K4`.

## How this audit ran

**20 finders over 8 beats.** Each beat was covered twice independently - **pass A** working code-first
(read the diff and the call graph, then go looking for the behaviour) and **pass B** working
behaviour-first (drive the running application, then go looking for the code) - plus a **deeper third
sweep** on the four highest-yield beats: `security`, `azure`, `regression` and `regression-tests`.

**40 raw findings merged to 19 candidates** (5 tagged `[x2]`, 14 tagged `[x1]`).

- **`[x2]`** means two independent passes found the same defect. Merging was on the defect, not the
  wording: same root cause, same citation, same one-line fix.
- **`[x1]`** means only one pass found it. Those got a **second verifier on a reachability lens** -
  "can an actual request, job, or user action reach this in a real deployment?" - and where the two
  verifiers disagreed, a **third broke the tie**. Three candidates went to a tie-break; all three
  died there.

**Every candidate went to at least one verifier prompted to refute it**, not to confirm it, and to
reproduce it against the live rig rather than reason about it. **5 survived, 14 were refuted, and all
5 survivors were reproduced live.** Four of the five had their proposed fix built, published on a
separate port and catalog, and driven end to end; three of those fix builds produced a correction that
is recorded in the finding.

---

# Medium

_K1 is fixed and committed, exactly as proposed: `invalidatePlan()` is now the first statement in
`#bulk-go-preview-btn`'s click handler. Ordering is load-bearing twice and is preserved — it nulls
`lastSelection`, so it precedes the reassignment, and it re-disables `#step3-tab`, so it precedes
`activateTab("step3")`. Step 4 is now reachable only through `#bulk-go-commit-btn`, which `renderPlan`
enables only when `plan.canCommit`, i.e. only from a plan that was actually rendered._

_Reproduced first on an unfixed publish of `09cee3d` on its own port and catalog, driven headless
against real ARM. **Before:** one click of *Continue to Commit* left `step4-tab disabled: False` for the
page's life; after an aborted re-preview both the pill click and the confirm click were **accepted**,
the commit body carried **`expected: [null, null]`** — no expectation at all, so J2's check could not
fire — and the import succeeded with *"linked 1 existing target(s) to Azure"*. The out-of-band row
`created-by-another-admin` came back stamped
`AzureResourceId=/subscriptions/.../virtualNetworks/rig-vnet-visible-2`, which no screen in the
application can clear. **After:** `step4-tab disabled: True`, Playwright **refuses** both clicks, **no
POST is issued at all**, and the row keeps an empty `AzureResourceId`. Leg C (the click landing while
the second preview is still in flight — the silent leg, with no log line) closes identically._

_Two non-regression legs measured on the same build. The **J2 control** — same race, no re-preview —
still stamps `expected` and still answers **409 "The plan changed since it was previewed"**, so the
guard this finding is about is untouched. A **benign re-preview that succeeds** still re-opens step 4
through *Continue to Commit* and commits normally (2 targets, 3 child subnets), so the fix does not
strand an operator who simply previewed twice. Zero `pageerror`s across every run; `invalidatePlan` is
a hoisted `function` declaration, so this is not the round-10 J9 temporal-dead-zone shape._

_No test ships with this. It is view-embedded JavaScript with no unit-testable seam — the position
round 10 took for J5 and J9 — and the browser runs above are the verification. Suite unchanged at
**730**._

_Rejections, both on the verifier's measurement. The **cheaper interim was not taken**: calling
`invalidatePlan()` from `loadPreview`'s error handler and `!result.success` branch closes legs A and B
but leaves **leg C** open, and leg C is the one that stamps an expectation from a plan the operator
never saw with no warning line at all — it closes the loud legs and leaves the silent one. And this was
**not** "fixed" server-side by refusing a null `Expected`: `SubnetController.BulkAzure.cs:85-90` records
that as a deliberate round-10 decision so the documented direct JSON API keeps working._

_Residue, deliberately not changed: `#bulk-confirm-commit-btn` still reads `disabled: False` after a
failed re-preview. Nothing follows from it because the step-4 pane is never activated — measured, the
click is refused — and adding `prop("disabled", true)` for it inside `invalidatePlan()` is
belt-and-braces beyond the defect. Noted here so the next round does not re-derive it._

---

# Low

_K2 is fixed and committed with the client-side version: `showCommitError` now renders
`payload.differences` as `<li>`s after the `itemErrors` block, using `.text()` to match the two blocks
either side. This is the same treatment the reconcile wizard's `showCommitError` already gives its
409's `warnings`._

_Measured before and after against two publishes on separate ports and catalogs, with the colliding
subnet created through `/Subnet/Create` from a second cookie jar while the plan was on screen.
**Before:** the server answered 409 with
`differences: ["10.110.0.0/16: the preview showed a different action; it now resolves to ExactMatch.",
"10.110.0.0/16: it now targets existing Bastet subnet N."]` and the rendered
`#bulk-commit-error-list` was **empty** — every sentence the server built was dropped. **After:** the
identical 409 renders both sentences as list items beneath the generic message. Zero `pageerror`s in
both runs, and the refusal itself is unchanged, so nothing was written either way._

_The cheaper interim was **not** taken. Emitting `globalErrors = divergences` server-side alongside
`differences` does render, and it avoids touching a `.cshtml` — a real consideration given round 10
lost two fixes to view-embedded JS — but it ships the same array twice on the wire and fixes only this
one 409 rather than the client's inability to render a `differences` body at all. The client fix was
verified in a browser, which is the mitigation the interim was trying to avoid needing._

_Not closed by this change, and deliberately left: `#bulk-back-to-preview-btn` still repaints the
stale plan after a 409, so the button beside the now-truthful error panel still shows a plan that
contradicts it. That is a separate defect on the same screen and is recorded in the watch list rather
than fixed here. Suite unchanged at **730** — no test asserts on this view's DOM, and the block is
purely additive._

_One process note worth carrying forward: Razor views are compiled into the assembly, so a
`dotnet run --no-build` restart serves the previous view. The first "after" run reported an empty list
because it was still serving the pre-fix build; rebuilding and re-running produced the result above.
Always rebuild before believing a browser measurement of a `.cshtml` change._

---

_K3 is fixed and committed. `DescribeApprovedPlanDivergences` now copies `selection.VNetPrefixes ?? []`
into a local, skips a null element with a comment rather than counting it, and the third dereference
inside the `LogWarning` reads the local. The guard is in place rather than reordered, exactly as the
finding argued: the 409 is deliberately raised before the `CanCommit` 400, and moving it after would
report "this plan cannot be committed" in place of "the plan changed since you approved it" whenever
both apply. A null entry is **not** counted as `unverified` — it is a malformed request, not an
unchecked one, and the planner already names it._

_**One thing the finding did not anticipate, found by the test rather than by reading.** With the null
dereference guarded, body (B) still did not produce the promised 400 — it produced a **409**. When the
planner records a global error it returns no items, so the *valid* prefix alongside the null one is
described as "no longer produces a target to import": a divergence manufactured by the malformed body
rather than by the tree moving. The divergence check is therefore skipped when
`plan.GlobalErrors.Count > 0`, so the planner's own message reaches the caller. Round 10's
409-before-400 ordering is preserved wherever both are meaningful — a plan failing only on per-item
errors is still reported as diverged first._

_Measured live on two publishes. **Before:** `{"vNetPrefixes":null}`, `{"vNetPrefixes":[null]}` and
`{"vNetPrefixes":[<valid>,null]}` all returned **HTTP 500** with
`System.NullReferenceException ... at DescribeApprovedPlanDivergences`. **After:** all three return
**400** with the planner's own wording — *"No VNet address prefixes were selected."* for (A) and
*"A selected VNet prefix was empty."* for (B). Both controls are byte-identical either side: an empty
list still 400s, and a valid selection with no `Expected` still returns `200 createdTargets:1`._

_Because the ordering changed, J2's own path was re-verified rather than assumed: a valid selection
whose plan genuinely diverged still answers **409** with identical `differences` wording, and K2's
rendering still shows both sentences._

_Two regression tests added (`SubnetControllerBulkAzureImportTests`), 730 → **732**. Both confirmed
failing against the unfixed code with the defect's own signature — `System.NullReferenceException`,
not an unrelated assertion._

_The cheaper interim was **not** taken. Adding the sibling endpoint's
`selection.VNetPrefixes is null or { Count: 0 }` guard to the public action closes body (A) only; a
null element inside a non-empty list still threw. Applied by content rather than by line number, as
the finding advised, since the inserted comment block shifts the cited lines._

---

_K4 is fixed and committed with the `ChildNames` version. `BulkImportExpectedTargetDto` gained
`List<string>? ChildNames`, `attachApprovedOutcomes` stamps
`(item.childSubnets || []).map(function (c) { return c.name; })` — the defensive `|| []` and the
`function` form both taken as advised, since this runs in a preview success handler where a TypeError
would leave step 3 blank with the commit button live — and `DescribeApprovedPlanDivergences` compares
the list after the `WillMarkFullyAllocated` check, guarded on null so a caller that never previewed
keeps the optional contract. Neither list is repeated in the message, following the same rule the
`NewName` check uses, because these names are caller-influenced and nothing here is sanitized._

_Measured in a real browser against real ARM, before and after, with the target renamed through the
ordinary Edit page while the plan sat on screen. The preview displayed
*"Create rig-sn-vis-app (rig-vnet-visible) 10.110.1.0/24 (was rig-sn-vis-app)"* in both runs.
**Before:** the commit returned **200** with `differences: null` and the child was written — under the
bare `rig-sn-vis-app`, a string that appeared nowhere on the preview screen. **After:** the identical
race returns **409** with
`differences: ["10.110.0.0/16: the child subnet names have changed."]`, and nothing is written._

_**The finding's warning that this message would be dead text was acted on rather than noted.** K2 was
fixed first, in numeric order, so the same run shows the sentence actually rendered in
`#bulk-commit-error-list` rather than an empty list beneath the generic refusal. Zero `pageerror`s in
both runs._

_Two regression tests added, 732 → **734**: the rename race, and a pin that a caller supplying no
child names is still not refused. The rename test was confirmed failing with **only** the comparison
hunk reverted — `OkObjectResult` where `ConflictObjectResult` was expected, i.e. the commit going
through and writing the moved name — so it fails for the defect's own reason, not a compile error._

_The cheaper interim was **not** taken, and its rejection is confirmed. Dropping
`usedNames.Add(targetExistingName)` from `BuildPlanItem` does work mechanically, but it pays for a
verification gap by deleting deliberate product behaviour the comment beside it exists to explain, it
lets a child be created with the exact name of its own parent, and it fixes nothing structurally — the
expectation would still carry no child information, so the next tree-dependent input to child planning
reopens the same hole silently. `OriginalAzureName` and the child address prefixes are deliberately
not compared: both are selection-derived and cannot move, so comparing them would add no coverage._

---

# Info

## K5 - Subnet Details prints a broadcast address for `/31` and `/32`, contradicting the app's own RFC 3021 rule and a host IP listed on the same page

`[x1]` &nbsp;|&nbsp; **Severity: info** (filed low; corrected down on measurement - see below)
&nbsp;|&nbsp; **Confidence: confirmed** &nbsp;|&nbsp;
`src/Bastet/Controllers/SubnetController.Read.cs:63`

### The defect

`SubnetController.Read.cs:63` sets `BroadcastAddress = ipUtilityService.CalculateBroadcastAddress(...)`
with no CIDR test. `HostIpValidationService.ValidateNewHostIp:70` applies the network/broadcast
reservation only when `subnet.Cidr < 31`, so both `/31` addresses and the single `/32` address are
legitimately assignable, and `IpUtilityService.CalculateUsableIpAddresses:105-110` documents `/31` as
2 usable and `/32` as 1.

### Failure scenario

Create `10.211.0.0/31` and assign both addresses as host IPs (the app accepts them). `GET
/Subnet/Details/{id}` then renders, **in one document**: *"Broadcast Address 10.211.0.1"*, *"Usable IP
Addresses 2"*, and a Host IP Assignments row *"10.211.0.1 p2p-b"*. The page labels an assigned, legally
assignable host address as the subnet's broadcast address. For a `/32` (`10.212.0.0/32`) it prints
*"Broadcast Address 10.212.0.0"* - the subnet's only address, also assignable and assigned, so the
Network Address and Broadcast Address rows print the identical string.

Control: on a `/30`, `POST /HostIp/Create` for the broadcast address is refused (*"Cannot assign the
broadcast address as a host IP"*, no row written) and the page correctly prints it as the broadcast.

### Reproduction

Driven through the real Create and HostIp forms (no direct SQL writes):

```
HOSTIP subnet=1 ip=10.211.0.0 -> 302 ; ip=10.211.0.1 -> 302 ; subnet=2 ip=10.212.0.0 -> 302
HOSTIP subnet=3 ip=10.213.0.3 -> 200 (refused: "Cannot assign the broadcast address as a host IP")

/Subnet/Details/1  /31  Subnet Mask 255.255.255.254  Broadcast Address 10.211.0.1  Total 2  Usable 2
                   ...Host IP Assignments: 10.211.0.0 p2p-a | 10.211.0.1 p2p-b
/Subnet/Details/2  /32  Subnet Mask 255.255.255.255  Broadcast Address 10.212.0.0  Total 1  Usable 1
/Subnet/Details/3  /30  Broadcast Address 10.213.0.3  Total 4  Usable 2          (correct, control)
```

Nothing is written wrong and nothing is blocked - the harm is entirely a misread, which is why the
severity was corrected from low to info. The two verifiers disagreed here: one held low on the
precedent of round 5's E13 (an RFC-3021 display contradiction on the same page, filed low), the other
corrected to info on the precedent of round 10's J8 (a page stating a wrong network fact about a row
on the same screen, filed info) after measuring that no operator action is refused or misdirected.
**Info stands.** What is genuinely defensible, and was verified, is the self-contradiction: one card
says Total 2 / Usable 2 and then names one of those two the broadcast, and the card below names it as
an assignment.

### Fix

At `SubnetController.Read.cs:63`, compute it only below `/31`:

```csharp
BroadcastAddress = subnet.Cidr < 31
    ? ipUtilityService.CalculateBroadcastAddress(subnet.NetworkAddress, subnet.Cidr)
    : string.Empty,
```

and render the empty case in `src/Bastet/Views/Subnet/Details/_NetworkInformation.cshtml:19`.
`SubnetDetailsViewModel.BroadcastAddress` (`SubnetViewModels.cs:101`) has exactly one consumer - that
one `<dd>` - and no test in `test/` mentions the view model, so nothing else moves.

**Built and driven side by side** against the same catalog: 0 warnings / 0 errors, **730 passed**, the
`/31` and `/32` flip while the `/30` is byte-identical. The Razor `@(...)` expression is compiled at
build, so this is not the J9 shape.

**Cheaper interim, no controller change:** gate the two lines in `_NetworkInformation.cshtml:18-19` on
`@if (Model.Cidr < 31)`, which the view can do because `Model.Cidr` is already on the view model. It
leaves the value computed and unused **and silently drops the row from the definition list**, so the
card loses a label rather than answering the question - the controller-side version is preferable.

### Fix corrections

- **The proposed label is wrong for a `/32`.** RFC 3021 is *"Using 31-Bit Prefixes on IPv4
  Point-to-Point Links"* and says nothing about `/32`; a `/32` has no broadcast because it is a single
  host - exactly the distinction the app's own comment at `HostIpValidationService.cs:65-69` already
  draws. Rendering `"None (RFC 3021)"` for a `/32` replaces one false statement with a differently
  false one. Discriminate on `Model.Cidr`, e.g.
  `@(Model.Cidr == 31 ? "None (RFC 3021 point-to-point)" : Model.Cidr == 32 ? "None (single host)" : Model.BroadcastAddress)`,
  which also removes the reliance on `string.Empty` as an out-of-band sentinel.
- **The finding's claim that `Read.cs:63` is "the one call site with no guard" is false**, and this
  matters because it is the reason the fix must not be generalised. Four other call sites are also
  unguarded: `HostIpController.cs:104`, `:140`, `:182` - all benign, because they build the
  `SubnetRange` string and `"10.211.0.0 - 10.211.0.1"` is the correct range for a `/31` (verified in
  the rendered Create page) - and `HostIpValidationService.cs:161`, which is **permanently-accepted
  item 5** and out of scope.
- **Do NOT "fix" this inside `IpUtilityService.CalculateBroadcastAddress`.**
  `HostIpValidationService.cs:327` deliberately calls it for `newCidr < 31` only,
  `SubnetPropertyCalculationTests` pins its current `/31` and `/32` return values, and it would break
  the three `HostIpController` range strings.

---

# Refuted

Reported by a finder, killed by the verifier.

| Candidate | Severity as reported | Citation | Why it was killed |
|---|---|---|---|
| The J4 pending-message queue is a read-modify-write over a cookie, so two concurrent error redirects destroy each other's entry `[x1]` | info | `src/Bastet/Controllers/ErrorPageMessages.cs:55-64` | **Tie-break; the mechanism claim is false by measurement.** The loss reproduces easily (10/10 with two tabs in one context), but the cited line is not the cause: rebuilding with the queue removed entirely - one `TempData["EPM_"+token]` key, no list, no cap - loses messages at the same rate (9/10), because `CookieTempDataProvider` serialises the **entire** TempData dictionary into one cookie, so the second `Set-Cookie` replaces everything regardless of read-modify-write. The title states a causal claim that measurement falsifies, and no change confined to `ErrorPageMessages.cs` removes it. It is a framework-wide property of the app's TempData use, and J4's site is the one place it has been made *harmless*: across 20 tab-loads there was zero cross-talk, only the designed generic fallback, correct 404 status, nothing written. The untreated pre-J4 path (`AzureController.cs:41` + `_TempDataAlerts.cshtml`) renders the **wrong** message 9/10 under the identical race. The residual strand (the `MaxPending` doc comment) is comment accuracy, and is literally true sequentially. |
| The J2 test's "caller's strings are never echoed" assertion tests a string that was never in the request, so it cannot fail `[x2]` | info | `test/Bastet.Tests/Azure/SubnetControllerBulkAzureImportTests.cs:177` | **Automatic refutation: test coverage.** The finding's own evidence concedes "production is correct today; it is only unpinned" - the entire claimed consequence is a hypothetical future edit by a hypothetical author. Two further kills on measurement: every consumer of this endpoint's error payload renders with jQuery `.text()` (`_BulkScripts.cshtml:541`, `:695`, `:698`, `:704`) and `differences` is read by no view at all, so even the *existing* `VNetName` echo reaches the DOM as a text node - the boundary being guarded has no measured consequence behind it. And the invariant it asks to pin is round 10's chosen mitigation for **permanently-accepted item 3** (`GlobalSanitizationFilter` skipping nested collections); asking for a test that pins that choice is not a new defect. What survives is one sentence of comment at `:175-176` that describes something the line below does not test. |
| J1's scaling regression test does not fail when the per-target `SaveChangesAsync` - the dominant half of the fix - is put back `[x2]` | info | `test/Bastet.Tests/Azure/SubnetControllerAzureReconcileScalingTests.cs:219` | **Every factual claim is true and none of them is a defect in Bastet.** Reproduced: reverting only that half leaves all 730 green, and the test really is blind to it. But at HEAD the production code is **correct** - the batch save is present, and no operator can observe anything wrong. The finding's scenario opens "In a copy of the tree I reverted exactly one production hunk"; the defect is manufactured in the reporter's private copy and its consequence is entirely future-tense. That is a test-coverage gap and hits two automatic refutations at once. The secondary complaint (the test's `<remarks>` overstate what it bounds) is a doc-comment overstatement on a test. |
| `AzureBulkImportPlanner.TruncateForName` has had no caller since round 4 extracted `SubnetNaming.WithSuffix` `[x2]` | info | `src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:774` | **Automatic refutation: not a runtime defect, and what remains is naming.** A private static method with zero call sites is unreachable in every execution - no input, request, or click causes it to run, so there is no wrong output to exhibit. The finding's stated harm is a hypothetical maintainer confusing it with `TruncateAndSanitizeName`. The one load-bearing embellishment is also factually wrong: they are not "eight lines" apart - `TruncateAndSanitizeName` ends at `:724` and two full methods stand between them, putting them 57 lines apart, so the adjacency that made the trap plausible is not there. The codebase has already ruled on this class: the live watch-list entry on round 8's H6 residue says *"Defence in depth - do not tidy it away."* |
| A plain-HTTP `BASTET_OIDC_AUTHORITY` passes startup and then answers every routed request with a bodiless HTTP 500 carrying none of the four security headers `[x1]` | info | `src/Bastet/Program.cs:210` | **Mechanism reproduces exactly; all three consequence limbs collapse.** (1) The headline limb is nil-consequence and is the shape round 10 already refuted verbatim: the response is `Content-Length: 0` with no body - nothing to MIME-sniff, no document to frame, no navigation to leak a referrer from, and nothing disclosed. (2) "Kills the error page and every anonymous page" is real but not *incremental*: the control shows the deployment is totally dead either way - with an ordinary unreachable HTTPS authority nobody can sign in and every routed page is 500 too - so the entire measured delta is a rendered ServerError page plus two account pages on an installation where no user can reach a single page of data. The plain-HTTP case is in fact the **louder** failure, 100% from the first request, with the framework naming the exact remediation. (3) "A liveness probe reports the deployment healthy" has no basis here: there is no `MapHealthChecks`, no `/health`, and no `HEALTHCHECK` in the Dockerfile. What is left is an operator misconfiguration that ASP.NET Core's own guardrail refuses, plus a configuration-surface feature request (expose `RequireHttpsMetadata`, fail at startup). |
| A `/32` subnet offers "Add Child Subnet", an action every CIDR value is refused for `[x1]` | info | `src/Bastet/Views/Subnet/Details/_ChildSubnets.cshtml:6` | **Tie-break; verbatim re-raise plus a false premise.** `AUDIT-FINDINGS-7.md:876` is row 1 of round 7's refuted table: same file, same line, same claim, same severity band, killed for F10. The kill reason then reproduces now, and the load-bearing premise ("the two buttons on one page disagree about whether the action exists") is **false**: a fully-covered `/30` renders the identical "Add Child Subnet" link, has all 8 possible child POSTs refused (the complete space), and renders no Unallocated Ranges card at all - so there is no second button to disagree with, and the proposed `Cidr < 32` gate would not fire. The two controls are not peers: the per-range Create button is capacity-derived by construction, the header link is navigation that is capacity-gated nowhere. The proposed fix also does not close the dead end - `SubnetController.Create.cs:17-25` still offers the `/32` in the parent dropdown of a plain `GET /Subnet/Create`. Consequence measured at nil: ordinary 200 re-renders with an inline reason, `SELECT COUNT(*) FROM Subnets` unchanged after 9 attempted creations. |
| Error page prints "Status Code: 200" on a response it sends as 500 when the route segment is not a number `[x1]` | info | `src/Bastet/Controllers/ErrorController.cs:62` | **Tie-break; both load-bearing claims fail.** (1) The "J3 leg" is not what produces the output: `/Error/200` is byte-identical to `/Error/abc` (wire 500, body "Status Code: 200"), and "200" parses, so that request never touches the fallback J3 added. `/Error/0` and `/Error/-1` disagree the same way. The finding requested `/Error/200` in its own loop and omitted the row. `git show ff285cf` has the clamp and the unclamped assignment byte-identical, so J3 changed only *which* wrong number prints for a non-numeric segment. The behaviour is twice-recorded as intended (`AUDIT-FINDINGS-7.md:76-77`, `AUDIT-FINDINGS-10.md:272-273`) and pinned by `HttpStatusCodeHandler_OutOfRangeStatus_FallsBackTo500` with `InlineData(200)`, `(302)`, `(0)`, `(99999)`. (2) The stated harm never happens: all eleven `RedirectToErrorPage` sites pass literal 403/404, `UseStatusCodePagesWithReExecute` always writes a framework int, `UseExceptionHandler("/Error")` hits the hardcoded-500 action, and no view links to `/Error/`. Six genuine failure paths driven live all have page-number == wire-status. The only way in is a human hand-typing an out-of-range `/Error/N`, on which request nothing failed - and the page's own `h1` says "Error" anyway. |
| J1's `archivedSubnetIds` hunk is unpinned: breaking it writes a duplicate archive row for every nested selected target `[x1]` | low | `src/Bastet/Controllers/SubnetController.Delete.cs:186` | **Automatic refutation, twice.** (1) No runtime defect: at HEAD the shipped code is correct, independently reproduced with the exact shape the finding says is unexercised - operator ticks a target *and* one of its own descendants, both Azure-linked and stale; the plan offers both and the result is 2 archive rows, no duplicate. The "specific wrong output" exists only in a build where someone has first replaced `AddRange(toDelete.Select(...))` with `Add(subnet.Id)`. (2) Missing test coverage: strip the mutation and what remains is "all 730 tests still pass" with a proposed fix that is literally "add one case". Nothing in `src/` changes. The mechanism narrative is true - `FindAsync` returns the tracked Deleted-state instance so the `subnet is null` skip at `AzureReconcile.cs:145` is now dead - but that makes `alreadyArchived` correct **and** load-bearing, not defective. |
| Reconcile plan router still tests for a status its two evaluators can no longer produce - dead arm left by round 10's J7 `[x1]` | info | `src/Bastet/Services/Azure/AzureReconciler.cs:133` | **No runtime defect, a settled watch-list item, and the proposed fix inverts the safety it claims to protect.** The arm really is unreachable (a `throw` wired into `:132` never fires across 730 tests nor a live ARM scan carrying two unrecognisable resource IDs), but the harm clause is verbatim counterfactual. It re-raises the recorded H6-residue watch-list entry - *"Defence in depth - do not tidy it away."* Most importantly: `or UnrecognisedResourceId` is the **fail-safe** half of a two-way router. `plan.ReviewItems` is never offered for archival; `plan.Items` is. Under the proposed fix the `else` at `:137` sends any such row straight into `plan.Items` - a row offered for archival with nothing established about it, which is the literal harm the finding says it is guarding against - and silently degrades the withholding set at `:256-264`. The inline comment at `:114-117` already tells the next reader the thing the finding says they will not be told. |
| `BatchCreateChildSubnets`' JSON-API branch writes a TempData success banner nobody consumes, overwriting a message a real redirect had queued `[x1]` | info | `src/Bastet/Controllers/SubnetController.Azure.cs:429` | **Tie-break; reachable code path, unreachable wrong output.** The clobber reproduces byte for byte, but the shared cookie jar is the harness, not a client: with the operator's browser as one jar and the API script as another - which is what "a script calls the documented JSON API" means - the operator's banner comes back intact and the stray message sits unread in the script's jar, in a client that renders no HTML. No browser reaches the JSON branch through the app: the only `isAzureImport` in `Views/` is `_SubnetList.cshtml:26`, hardcoded `true`, and `wwwroot` never references the endpoint. Even forced, a real browser follows the 302 synchronously so the operator's message is already rendered; the pending-message window needs a client that posts an HTML form, declines its redirect, posts JSON, then requests HTML - reachable only via devtools JS an admin writes against their own session, cosmetically damaging only themselves, and not cross-site triggerable. Same asymmetry-as-evidence shape round 8 killed on this very method. |
| Both Azure wizard landing view models carry an `IsFeatureEnabled` flag that is written true and read by nothing `[x1]` | info | `src/Bastet/Models/ViewModels/AzureBulkImportViewModels.cs:139` | **Consequence measured at exactly zero.** A/B on one port and one catalog: with the property present the two landing pages are 54674 and 38799 bytes; with both properties and both initialisers deleted they are 54674 and 38799 bytes, byte-identical after normalising the antiforgery token. A property whose complete removal changes not one byte of any response it can participate in has no observable behaviour to be wrong about. The stated harm is explicitly counterfactual ("a maintainer adding a 'feature disabled' panel would naturally branch on it") - the branch is not unreachable, it does not exist. The one reading with teeth also fails: neither type is bound by any POST and no view emits the flag, so it is not operator-tamperable; and the real gate is confirmed live (with the feature off both actions 302 to `/Error/403` and the view is never reached). Round 5's refuted table already killed `IsFeatureEnabled` on this reasoning. Corrections: it is one property per class, not several, and the wizards have sixteen view files, not eleven. |
| Reconcile and bulk-import DTOs ship two dead fields to every client: `isVNetLevel`, and the raw enum ordinal `status` alongside `statusName` `[x1]` | info | `src/Bastet/Models/ViewModels/AzureBulkImportViewModels.cs:85` | **Dies on consequence, and half of it is a recorded kill.** Driving the whole path from a real entry point against a build with **both** fields stripped from the wire produced a byte-identical rendered row, zero JS errors, 730/730 either way. The harm needs two stacked hypotheticals - a future enum value inserted mid-list *and* a future client reading `status` - and neither exists: the only clients switch on `statusName`, the only `.status` hits in `Views/` are the two `xhr.status` lines the finding itself names, and there is no documented external HTTP API to postulate a third-party consumer for. `AUDIT-FINDINGS-5.md:633` already killed `IsVNetLevel` by name ("Worst case its own scenario names is one extra `true` in a JSON row the browser discards"), along with four sibling members. One overstatement corrected: `Status` is load-bearing server-side (`AzureReconciler.cs:132`, `:192`, `AzureController.cs:374`); only its JSON projection is redundant, and a redundant superset is not a defect. |
| `IInputSanitizationService.SanitizeString` is registered, implemented, unit-tested and never called, and its `allowHtml` branch does not do what its comment claims `[x1]` | info | `src/Bastet/Services/Security/IInputSanitizationService.cs:14` | **Every claim is true - confirmed at IL level - and it still fails reachability.** An exhaustive scan of the shipped assembly (4395 method bodies, 129332 instructions, every call token resolved) finds **zero** call sites, stronger than grep because it also rules out async state machines, lambdas and local functions; dispatch cannot reach it, since `GlobalSanitizationFilter` goes through `SanitizationAttributeBase.Sanitize` and all four overrides call the other members. The comment really is false (`SanitizeString("<p>Paragraph</p>", true)` returns `&lt;p&gt;Paragraph&lt;/p&gt;`) and the `allowHtml:true` arm really is the weaker filter. But the in-edge set is provably empty - not a hard-to-hit branch, no edge at all - so no request produces a wrong byte, no row is corrupted and no XSS is admitted. The consequence chain terminates in a comment that misleads a reader, and the latent-XSS reading needs two future decisions (route untrusted markup through that arm, then render with `Html.Raw`), neither present. |
| The archived-host-IP page's role check is tautological: its `else` branch cannot be reached `[x1]` | info | `src/Bastet/Services/UserContextService.cs:36` | **Automatic refutation: not a runtime defect.** The citations are accurate and the reachability conclusion is right - the full live matrix produced 403 with the view never executing, and only a rewritten `DevAuthHandler` reached the `else`. But the scenario names no wrong output and no wrong state, and its stated "why it matters" is verbatim *"the pattern a maintainer would copy"*. Consequence measured, not assumed: the surviving arm is the correct link for 100% of reachable principals and the dead arm's target is gated by the same policy, so it is a no-op alternative, not a mis-designed fallback. Two factual corrections, neither rescuing it: **"the two predicates are the same set" is false** - they differ on the authenticated-identity precondition, and the `else` was rendered live by exploiting exactly that gap, so the finding is right for the wrong reason; and `DevAuthHandler.cs:26-32` issues **one** role claim (`Admin`), not all four - the other three come from the inheritance switch at `UserContextService.cs:53-60`. |

---

# Watch list

Not findings. Things a later round should know before it re-derives them.

**Bulk-import / J2 surface**

- **`AzureResourceId` can never be cleared through the UI.** It is written by the two import paths and
  by nothing else, no unlink view exists, `EditSubnetViewModel` does not carry it, and a CIDR edit on a
  stamped row then throws. This is what makes `K1`'s end state unrecoverable and is the reason both J2
  and `K1` are priced above cosmetic. A "clear Azure link" action would retire a whole class of finding.
- **`#bulk-back-to-preview-btn` repaints a stale plan after a 409.** Measured: after the refusal, the
  only other button on the commit step returns the operator to step 3 still showing the superseded
  plan, which contradicts the refusal that just fired. `K2`'s fix makes the error panel truthful and
  does not touch this.
- **The in-flight window is real.** Bulk preview latency up to **7,247 ms** was observed against live
  ARM, which is why `K1`'s leg C - an impatient click on an already-lit pill - is unremarkable rather
  than exotic.
- **`#bulk-confirm-commit-btn` is enabled in markup** (`_StepCommit.cshtml:30`) and is disabled only
  while a commit is in flight. It relies entirely on its pane being unreachable.
- **Nested DTO fields arrive unsanitized.** Permanently-accepted item 3 (`GlobalSanitizationFilter`
  skipping nested `System.*` collections) covers J2's `Expected` and will equally cover `K4`'s new
  `ChildNames`. Round 10's chosen mitigation is "echo nothing back"; anyone making a divergence message
  concrete must keep that rule.

**TempData / flash messages**

- **Ordinary `TempData` flash messages cross-talk between browser tabs.** Measured while refuting the
  J4 candidate: the untreated pre-J4 path (`AzureController.cs:41` rendered by
  `_TempDataAlerts.cshtml`) shows one tab the **wrong** message 9 times in 10 under a two-tab race,
  across roughly 30 call sites. `CookieTempDataProvider` serialises the whole dictionary into one
  cookie, so this is structural and no per-site fix removes it. J4's tokenised site is the one place it
  has been made harmless. This is the honest version of a claim two candidates aimed at the wrong file.

**Broadcast address / RFC 3021**

- `CalculateBroadcastAddress` has four other unguarded callers besides `K5`'s.
  `HostIpController.cs:104`, `:140`, `:182` are **benign** (range endpoints, correct at `/31` and
  `/32`). `HostIpValidationService.cs:161` is **permanently-accepted item 5**. Do not "unify" the
  guard into `IpUtilityService` - `SubnetPropertyCalculationTests` pins the current return values and
  the three range strings would break.

**Dead but deliberate - do not tidy**

- `AzureReconciler.cs:132-133`'s `or UnrecognisedResourceId` arm is unreachable **and fail-safe**.
  Removing it converts a fail-safe default into a fail-open one, sending unrecognised rows into the
  archivable `plan.Items`. Leave it; the comment at `:114-117` already explains why.
- `AzureBulkImportPlanner.TruncateForName` (`:774`) has had no caller since round 4, and
  `IInputSanitizationService.SanitizeString` has no call site in the shipped assembly at all - proven
  at IL level, not by grep. Both are inert. `SanitizeString`'s `allowHtml: true` arm is the **weaker**
  filter despite its permissive name, and its comment ("keep basic HTML") is false - both arms end in
  `EncodeHtml`. If anyone ever wires a rich-text field, fix the comment first.

**Deliberate behaviours that keep getting re-filed**

- `/Error/N` for any `N` outside 400-599 answers **500 while printing `N`**. Deliberate, documented in
  rounds 7 and 10, and pinned by `HttpStatusCodeHandler_OutOfRangeStatus_FallsBackTo500`.
- "Add Child Subnet" is capacity-gated **nowhere** - it renders identically on a `/32` and on a fully
  covered `/30`, and both refuse every possible child. Killed for F10 in round 7 and again this round.

**Rig hygiene**

- Two agents this round bound ports that were free at probe time and taken by bind time, and in both
  cases the probe then silently hit **another agent's application**. One such collision wrote three
  rows (`renamed-by-someone-else` 10.110.0.0/16 plus two children) into catalog `bastet_vc3r` that
  were never cleaned up. Check `ss -ltn` immediately before bind, and re-check which PID owns the port
  after start, before believing any measurement.
- The repository working tree was observed **dirty mid-round** by one verifier - another agent's
  untracked `ZzC13ProbeTests.cs`, an untracked `Bastet/` directory, and a modified tracked
  `SubnetControllerAzureReconcileScalingTests.cs`. It was clean again afterwards, but round 10's note
  stands: verify `git status --porcelain` is empty immediately before any commit.
