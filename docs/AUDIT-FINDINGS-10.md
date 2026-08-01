# Bastet - Round-10 Audit Findings

| | |
|---|---|
| Round | **10** (finding letter **J** - findings are `J1` ... `J9`) |
| Target branch | `audit/round-10` |
| HEAD | `dcc15ab` - *"Audit 9 Cleanup (#154)"*, identical tree to `main` |
| Build | **0 warnings, 0 errors** |
| Tests | **716 passed**, 0 failed, 0 skipped |
| Date | 2026-07-31 |

Every line number below was re-derived against the working tree at `dcc15ab` by at least one verifier.
Round-9 citations have already moved; re-derive these before acting on them.

---

## Verdict

**Nine findings: no critical, no high, two medium, five low, two info. Two candidates were refuted.**
Nothing in this round is reachable by an unauthenticated caller, nothing corrupts a row silently, and
nothing destroys data that an operator did not explicitly approve destroying. This is a quiet round.

Read **J1** first. It is the only finding with deployment-wide blast radius. One Admin clicking *delete*
in the Azure reconcile wizard, with the checkbox that ticks every row, holds the single global write
lock for O(selected targets x total subnets): 200 targets against a 66,000-subnet catalog held
`Bastet:SubnetOperations` for ~57 s, and during that window an ordinary `POST /Subnet/Create` issued
**from a second replica** was refused after 30.3 s with *"The operation timed out due to high
concurrency. Please try again."* - the message the app reserves for genuine contention. This is round
9's I3 shape surviving in the one path that deletes, and round 9's watch list actively deters finding
it: it records *"Delete's tree read [is] once per request"*, which is false here. It is twice per
**selected target**, and one request carries as many targets as the operator ticked.

Then **J2**: the bulk Azure import commits a plan it re-derives at commit time and never compares
against the plan the operator approved. A subnet created by a second admin while step 3 is on screen is
silently adopted, permanently stamped with the VNet's ARM id, renamed if the rename box is ticked, and
thereby pulled into the reconcile wizard's deletion scope - where, once that VNet is deleted in Azure,
it is archived into a table that stores no `AzureResourceId` and has no restore path. The sibling
destructive endpoint does exactly the opposite on the same class of divergence: it re-derives, sees the
mismatch and answers 409. Both mediums are Azure-path defects and both were driven end to end in real
Chromium against real ARM.

The five lows and two infos are ordered by consequence and none of them is load-bearing: a caller can
relabel their own failed request's HTTP status (**J3**); a concurrent 4xx steals the error page's
pending diagnostic (**J4**); a wizard's primary Next button stays live but inert after Back-then-Next
(**J5**); Production sign-out 500s and discards the session-cookie deletion during a cold start against
an unreachable IdP (**J6**); a corrupt `AzureResourceId` is skipped before it is classified, so the
reconcile warning blames a subscription that does not exist (**J7**); the deleted-host-IP archive prints
a subnet range that never contained the archived address (**J8**); and the bulk wizard greys out rows it
does not explain (**J9**).

**The fix corrections are the most valuable output of this round.** Every proposed fix was built and
measured in a copy outside the repository, not argued from source, and **seven of the nine were found
unsound or incomplete**:

- **J9's fix does not run.** It declares a `const` at `:238` and reads it at `:236` - a temporal dead
  zone. Razor does not parse embedded JS, so it builds at 0 warnings and then throws
  `ReferenceError: Cannot access 'subnetBlockedByPrefix' before initialization` on every load, leaving
  step 2 of the bulk wizard permanently spinning with zero checkboxes. Both verifiers built it and hit
  the identical failure. **A fix that compiles clean and breaks at runtime is this round's recurring
  trap** - J1's fix has the same shape in C#.
- **J1's fix passes a 200-target benchmark and then 500s on the first nested target**, because the cache
  it threads must be a *tracking* read and the finding says to copy `LoadSubnetTreeForBatchAsync`, which
  is `AsNoTracking`. It also does not compile as written (`SubnetController.AzureReconcile.cs` has no
  `using Microsoft.EntityFrameworkCore;` - the same trap round 9 recorded against I3).
- **J6's fix breaks 9 shipped tests** (716 -> 707), all of them round 9's own I6 regression pins.
- **J2's fix rests on client state that does not exist** (`lastPlan` is in the *reconcile* wizard, not
  the bulk one) and is weaker than the precedent it invokes, because it makes the check opt-in.
- **J8's and J4's interims were built and produce worse output than the defect** (`ARCHTEST (/0)`; a
  favicon that headless Chromium never requests).

Only **J5** and **J7** shipped fixes that survived measurement unchanged.

---

## How this audit ran

**Twenty finders over eight beats.** The beats were `azure`, `security`, `ui`, `locking`, `logic`,
`regression`, `regression-tests` and `deadcode`. Each was covered **twice, independently** - pass **A**
code-first, pass **B** behaviour-first, neither seeing the other's output - plus a **deeper third
sweep** on `security`, `azure`, `regression` and `regression-tests`. 20 launched, 20 returned.

**What the tags mean.**

- **`[x2]`** - both independent passes of the beat found it. Independent agreement is decent evidence
  that the code, not the reader, is the problem.
- **`[x1]`** - one pass found it and the other did not. Absence is weak evidence, so **every `[x1]` got
  a second verifier** on a reachability-and-consequence lens as well as the mechanism lens, and a third
  where the two disagreed. `[x1]` warrants **more** scrutiny at reconciliation, not less - seven of this
  round's nine survivors are `[x1]`, including everything below medium.

**Verification.** Every candidate went to at least one verifier whose brief was to *kill* it, and to
reproduce it against a live rig rather than reason about it: real SQL Server 2022 in a container, the
real application built from an unmodified `dcc15ab` tree, real headless Chromium where the defect is on
screen, and **two Azure service principals with disjoint RBAC over two resource groups** so that
"Azure denied access" and "Azure says it is gone" are distinguishable facts rather than assumptions.
Verifiers ran their own instances on their own ports against their own catalogs, from `git archive` or
`cp -a` copies - never from the repository working directory - and then built each proposed fix in that
copy and measured it (`dotnet build`, `dotnet test`, and the same live request replayed).

**The funnel.**

| | |
|---|---|
| Finder agents launched / returned | 20 / 20 |
| Beats | 8, each covered twice (A/B) + 4 deeper third sweeps |
| Raw findings reported | 18 |
| Candidates after dedup, merge and brief screening | **11** - 2 `[x2]`, 9 `[x1]` |
| Survived verification | **9** |
| Refuted by a verifier | **2** |
| Reproduced live | **9** |
| Proposed fixes built and measured | 9 - **2 sound, 6 incomplete, 1 unsound** |
| Baseline | `dcc15ab` on `main`, 716 tests, 0 warnings |

The seven raw findings that did not become candidates died at the **merge**, not at a verifier:
duplicates of each other, and re-files of items rounds 5-9 list as accepted, deliberately not done, or
already refuted.

---

# Medium

_J1 is fixed and committed. All three parts were taken: `GetAllDescendantsOrdered`
(`SubnetController.Helpers.cs`) and `ArchiveSubnetSubtreeAsync` (`SubnetController.Delete.cs`) each
gained an optional `treeCache`, the archive path also fills an optional `archivedSubnetIds` so the
reconcile loop no longer needs its own descendant walk, the redundant `GetAllDescendantsOrdered` call
in the loop is gone, and the per-target `SaveChangesAsync` moved to a single save after the loop. The
missing `using Microsoft.EntityFrameworkCore;` was added, as the verifier warned. **The cache is a
tracking read**, and the two XML doc blocks say why rather than leaving it to be rediscovered: the
verifier's `AsNoTracking` trap was reproduced here before the fix was written — built that way the
request returns the 500 path (`ObjectResult`, "another instance with the same key value is already
being tracked") on the first target with descendants._

_Measured on a nested rig rather than the finding's flat 200-leaf benchmark, which by construction
cannot see the tracking fault: six targets, each a root/child/grandchild subtree with a host IP,
alongside 200 unrelated subnets. Unfiltered whole-table `Subnets` reads went **13 → 2** (13 = 1 + 2
per target, exactly the formula the finding gives), with the outcome byte-identical before and
after — 218 subnets before, 200 remaining, 18 archived, 6 host IPs archived. The lock-hold and
rival-write timings in the finding were not re-measured at 66,000-subnet scale; the read count is the
mechanism, and it is now flat in target count._

_Two regression tests added (`SubnetControllerAzureReconcileScalingTests`), 716 → **718**. The scaling
test deliberately asserts that the read count for 2 targets **equals** the count for 8 rather than
pinning a magic number — an exact count would break on unrelated query changes while still admitting
the defect back. Confirmed failing against unfixed code with the defect's own signature (expected 5,
actual 17) before the fix landed. The second test pins nested-subtree-with-host-IP archiving, which is
what the tracking requirement protects._

_Not done, on the finding's own evidence: the redundant `:143` read was not removed on its own (59.4 s
→ 57.3 s alone — it is only worth taking as part of this change), and the per-subnet host-IP `Include`
N+1 at `Delete.cs:182-192` was left alone (measured 22.50 s → 22.74 s, i.e. nothing, despite removing
20,081 round trips). Both remain on the watch list as measured dead ends._

---

## J2 [x2] - Bulk Azure import commits a re-derived plan without checking it still matches the plan the operator approved

**Severity:** medium | **Confidence:** confirmed | **Cite:**
`src/Bastet/Controllers/SubnetController.BulkAzure.cs:62` (the re-plan), `:156` (rename write),
`:190` (the ARM-id stamp)

**What goes wrong.** Admin opens `/Azure/BulkImport`, selects VNet `rig-jb-div` (10.151.0.0/16) and
previews. Step 3 renders *"New top-level - create rig-jb-div (10.151.0.0/16)"*. While that screen is up,
a second admin creates an unrelated Bastet subnet `10.151.0.0/16` named `Finance-Prod-Reserved`
(description *"Reserved by the network team. Not an Azure VNet."*, no Azure link) through the ordinary
`/Subnet/Create` form. The first admin clicks **Confirm Import** on a screen that says *"apply the plan
to Bastet"*.

`BulkCreateFromAzurePlanCore` re-runs the planner against the now-changed tree (`:61-62`), gets a
different plan - `TargetType` flips from `AutoCreateTopLevel` to `ExactMatch` - and executes **that**
plan with no comparison against what was approved. The commit posts the *selection*, never the plan: the
preview and commit request bodies are byte-identical on the wire. It stamps
`AzureResourceId=/subscriptions/<sub>/.../virtualNetworks/rig-jb-div` onto `Finance-Prod-Reserved`
(`:190`) and parents the Azure children under it; with the rename box ticked it also overwrites the
operator's name (`:156`).

**Wrong output, and it is irreversible in-app:** `AzureResourceId` is written only by the two import
paths and **never cleared**; `EditSubnetViewModel` does not carry the field and `Views/Subnet/Edit.cshtml`
never mentions it, so there is no unlink. The row's CIDR also becomes uneditable
(`SubnetController.Edit.cs:92` throws when the field is non-empty). The row is now inside the reconcile
wizard's deletion scope, and when the VNet is later deleted in Azure it is archived - into a
`DeletedSubnets` table with no `AzureResourceId` column and no restore path anywhere in the app.

**The sibling destructive endpoint does the opposite on this exact class of divergence:**
`SubnetController.AzureReconcile.cs:77-92` rebuilds the plan and returns **409 Conflict**, deleting
nothing. The single-VNet wizard is immune because `BatchCreateChildSubnetsCore(int parentId, ...)` pins
its target with `FindAsync(parentId)`.

**Reproduction.** Real headless Chromium driving the real wizard against real ARM, verifier's own
catalog and instances; `interleave.sh` is the second admin.

```
PLAN_SHOWN:   VNet "rig-v10c2-div" - prefix 10.152.0.0/16  New top-level  create rig-v10c2-div ...
INTERLEAVE:   hand-create HTTP 302 | 1 Finance-Prod-Reserved 10.152.0.0/16 arm=<null>
PLAN_STILL_SHOWN: (unchanged - the screen never repaints)
COMMIT_SCREEN: "Click the button below to apply the plan to Bastet. This is performed in a single
                transaction; if anything fails, no changes are saved."  Confirm Import
POSTED_TO BulkCreateFromAzurePlan: <byte-identical to the preview post>
COMMIT_RESULT: SUCCESS[Bulk import completed. Created 0 VNet target(s), 2 child subnet(s), renamed 0
                target(s), linked 1 existing target(s) to Azure, ...]
CONSOLE_ERRORS: 0

DB after:  1 | Finance-Prod-Reserved | 10.152.0.0/16 | desc='Reserved by the network team...'
               arm=/subscriptions/.../virtualNetworks/rig-v10c2-div
           2,3 | rig-v10c2-s1, rig-v10c2-s2 | parent=1

Approved plan, in machine form:
  ITEM prefix=10.152.0.0/16 targetType=2 (AutoCreateTopLevel) existingTargetSubnetId=None
Executed plan: ExactMatch onto subnet 1.
```

Consequence chain, run to the end: `az network vnet delete` -> `ResourceNotFound`;
`POST /Azure/ReconcileScan` -> `id=1 Finance-Prod-Reserved 10.152.0.0/16 status=VNetDeleted
descendants=2`; `POST /Subnet/BulkDeleteStaleAzureSubnets {subnetIds:[1]}` -> 200
`{"targetsDeleted":1,"subnetsArchived":3}`; `SELECT` over `10.152.*` -> 0 rows; `DeletedSubnets` holds
`Finance-Prod-Reserved` with its original description and **no** `AzureResourceId` column. Rename
variant: same commit returned `renamedTargets:1, linkedTargets:1` and the row became `rig-v10c2-div`
with the operator's description preserved - proving it is the same row. Sibling asymmetry, same session:
`POST /Subnet/BulkDeleteStaleAzureSubnets {subnetIds:[99]}` -> **409**, nothing deleted.

Control: three concurrent identical commits gave 1x200 and 2x400 (*"already exists in Bastet"*), so the
lock and the re-plan do work - the gap is specifically that nothing compares the re-derived plan with
the approved one.

**Fix.** Make the commit refuse a plan that is not the plan that was approved, exactly as
`BulkDeleteStaleAzureSubnets` already does. Add an optional per-prefix expectation to
`BulkImportSelectionDto` (`{vNetResourceId, addressPrefix, targetType, existingTargetSubnetId,
autoCreateParentSubnetId, willRename, newName, willMarkFullyAllocated}`), populated by the wizard from
the preview response; compare each re-derived item against its expectation immediately after `BuildPlan`
at `:62` and return **409 Conflict** listing the differences. **Cheaper interim:** send and compare only
`targetType` and `existingTargetSubnetId` - about fifteen lines each side, and it does close the
reproduced case (approved `AutoCreateTopLevel`/null vs re-derived `ExactMatch`/1 diverges on both
fields).

**Verifier correction - INCOMPLETE.** The shape is right and the DTO is a plain POCO
(`AzureBulkImportViewModels.cs:192-208`), so an added nullable property breaks no caller. Two problems:

1. **A stated premise is false.** The fix says the wizard already holds the preview response *"in
   `lastPlan` (`_BulkScripts.cshtml` renderPlan/commitImport)"*. `grep -n lastPlan` over that file
   returns **nothing** - `lastPlan` exists only in `_ReconcileScripts.cshtml`. The bulk wizard's state is
   `lastSelection` and `previewSeq`. `renderPlan(result.plan)` does receive the plan, so stashing it is a
   two-line addition - but the fix must say it is *adding* client state, not reusing it.
2. **It is weaker than the precedent it invokes, in the way that matters.**
   `BulkDeleteStaleAzureSubnets` takes ids only and re-derives server-side; the client cannot opt out.
   Making the expectation optional so *"leaving the field null keeps the documented direct-JSON-API
   behaviour unchanged"* means the server is safe only when the client volunteers to be checked - a
   browser holding a cached older script, or any direct caller, keeps today's behaviour on the Admin-only
   endpoint that is dangerous precisely because it writes. If the field must stay optional, the null case
   needs its own recorded decision (log it, or refuse server-side the narrow `ExactMatch`-onto-a-row-that-
   was-not-in-the-approved-plan case), not silence.
3. Not mentioned by the fix: the nested expectation object arrives **unsanitized**, because
   `GlobalSanitizationFilter` does not descend into the nested selection list. Harmless while it is only
   compared, but it must not be echoed into the 409 body unescaped.

**Both of the fix's rejections are correct and were checked.** *"Re-run the preview at commit time"* is
the same defect one step later - the operator has already left the preview screen. And a server-only
rule *"refuse ExactMatch onto a subnet not already linked to this VNet"* really would break the
advertised adopt path: `AzureBulkImportPlanner.cs:225-226` sets `WillUpdateExisting` with reason *"Will
import into existing Bastet subnet"* for exactly the unlinked case, and that is what the wizard renders
as selectable.

---

# Low

## J3 [x1] - Error page binds its status code from the POST body, so a caller picks the status of any re-executed bodiless 4xx

**Severity:** low (filed medium; **corrected down by both verifiers**) | **Confidence:** confirmed |
**Cite:** `src/Bastet/Controllers/ErrorController.cs:17`

**What goes wrong.** `UseStatusCodePagesWithReExecute("/Error/{0}")` (`Program.cs:524` Development,
`:529` Production) re-executes the pipeline for the **same** request - same method, same body - with the
path rewritten to `/Error/<real status>`. `HttpStatusCodeHandler(int statusCode)` binds through MVC's
default composite value provider, where `FormValueProviderFactory` precedes `RouteValueProviderFactory`.
A form field named `statusCode` in the original POST body therefore **beats the route segment the
middleware just set**. The guard at `:32` only clamps outside 400-599, so 400 -> 404 passes untouched.

Two legs survive verification:

- **Status laundering of empty-bodied framework statuses.** An authenticated user posting
  `Name=x&statusCode=404` to `/Subnet/Create` with a stale antiforgery token gets **404 Not Found** on
  the wire and in the request log, though the framework produced 400. `POST /No/Such/Path` with
  `statusCode=451` answers **451**. Anonymous in Production: `POST /Error/404` with `statusCode=451`
  answers **451**.
- **A malformed form becomes a server fault, with no attacker at all.** `Name=x%00y` (a NUL in any form
  value) makes `FormPipeReader` throw, `CompositeValueProvider.TryCreateAsync` abandons binding for every
  parameter, `statusCode = 0`, and the `:32` guard turns that into **500 Internal Server Error** with the
  page printing *"Status Code: 0"*. The same happens for a malformed multipart POST (bad boundary,
  truncated upload) - an ordinary client bug reported as a server fault.

**The two verifiers disagree on the 403 leg, and the disagreement matters.** The finding's headline is
that a View-role user's 403 launders into 404, hiding an authorization denial from log analysis. That
leg was only ever reproduced on a rig copy whose `DevAuthHandler` reads roles from an `X-Rig-Roles`
header. On the **shipped** build the second verifier established no launderable bodiless 401/403 exists:
Development's `DevAuthHandler` issues Admin (`DevAuthHandler.cs:31`), satisfying all four policies;
Production's cookie handler sets `AccessDeniedPath` (`Program.cs:200`), so a Forbid is a 302; and the
three literal `StatusCode(403, <json>)` sites write a body, which `StatusCodePagesMiddleware` skips.
Measured both ways on one endpoint: `POST /Subnet/BatchCreateChildSubnets` **with** a valid token
returns 400 with `{"subnets":[...]}` unlaundered; **without** a token (empty-bodied 400) returns 451.
Treat the 403 leg as mechanism-only until a deployment that emits a bodiless 403 is demonstrated.

Two further legs were **struck** as pre-existing and already documented: `POST /Error/451` answering 451
and `GET /Error/200` answering 500 with *"Status Code: 200"* both happen through the **route** alone, and
round 7 recorded that the route segment is caller-supplied (`ErrorControllerTests.cs:99-101` pins it).
Only the framework-generated-4xx half is new ground.

**Reproduction.** Pristine shipped DLL, Development, no source modification:

```
POST /Subnet/Create  Name=x                -> HTTP/1.1 400 Bad Request        (control, "Status Code: 400")
POST /Subnet/Create  Name=x&statusCode=404 -> HTTP/1.1 404 Not Found          ("Resource Not Found")
POST /Subnet/Create  Name=x&statusCode=451 -> HTTP/1.1 451 Unavailable For Legal Reasons
POST /Subnet/Create  Name=x%00y            -> HTTP/1.1 500                    ("Status Code: 0")
POST /No/Such/Path   statusCode=451        -> 451     (404 without the field)
GET  /Error/404?statusCode=451             -> 404     (query loses to route; only the form wins)
malformed multipart on a 400 path          -> 500     ("Status Code: 0")

app log, antiforgery leg:  "Executing StatusCodeResult, setting HTTP status code 400"
                        -> "Executing endpoint '...ErrorController.HttpStatusCodeHandler'"
                        -> "Request finished HTTP/1.1 POST .../Subnet/Create - 404"
Production (anonymous):  POST /Error/404 statusCode=451 -> 451 ; a=x%00b -> 500
```

Note the log line is Development-only evidence: `Program.cs:22` pins `Microsoft.AspNetCore` to `Warning`
outside Development, and a Production instance logged **0** `Request finished` / `Authorization failed`
lines across the same requests. The wire-level relabelling is what stands.

**Why low.** No data change, no disclosure, no privilege change, no XSS (`@Model.StatusCode` is an
`int`), no cross-user effect (`Cache-Control: no-store,no-cache`), and the caller cannot reach 2xx or
3xx. A caller relabels *their own* failed request.

**Fix.** `public IActionResult HttpStatusCodeHandler([FromRoute] int statusCode)`. Built and driven:
closes the laundering completely (400 stays 400, `POST /No/Such` stays 404), leaves the redirect path
intact (`GET /Subnet/Details/99999` followed -> 404, `GET /Error/409` -> 409), builds 0/0, **716 passed,
0 failed** - the four `ErrorControllerTests` call sites that invoke `HttpStatusCodeHandler(int)` directly
still compile.

**Verifier correction - INCOMPLETE against the finding's own scenario 4.** `[FromRoute]` does **not** fix
the unreadable-form leg: when the form reader throws, binding is abandoned for every source, so the NUL
and malformed-multipart POSTs are still 500 *"Status Code: 0"*. Closing that too needs the value read
outside model binding - a parameterless signature with
`int statusCode = int.TryParse(RouteData.Values["statusCode"] as string, out int r) ? r : Response.StatusCode;`
- which was built and verified to fix all four legs (NUL POST -> 400, page says *"Status Code: 400"*) but
breaks the four test call sites with `error CS1501: No overload for method 'HttpStatusCodeHandler' takes
1 arguments` (`ErrorControllerTests.cs:32, 46, 94, 112`). Recommendation: take the parameterless variant
plus the four one-line test updates; take `[FromRoute]` alone if only the laundering is in scope. No
sibling call site is missed either way - the eleven `RedirectToAction(..., new { statusCode = N })` sites
generate `/Error/N` and are followed by a fresh GET with no form body.

---

## J4 [x1] - A concurrent 4xx anywhere in the session steals the pending error-page message: the unrelated page prints it, the intended page loses it

**Severity:** low | **Confidence:** confirmed | **Cite:**
`src/Bastet/Controllers/ErrorController.cs:22`

**What goes wrong.** `ErrorController.HttpStatusCodeHandler` is two things at once: the explicit
redirect target of eleven controller sites that first stash a diagnostic in
`TempData["ErrorPageMessage"]` (`Read.cs:43`, `Delete.cs:22`, `:127`, `Edit.cs:22/:59/:190/:251`,
`AzureController.cs:24/:36/:184/:292`), and the automatic target of `UseStatusCodePagesWithReExecute`
for every bodiless 4xx/5xx. Line 22 reads that key **unconditionally**, and a TempData read *consumes*
it. Any unrelated 4xx that lands between a controller's 302 and the browser following it takes the
message.

Tab A requests `GET /Subnet/Edit/999` (row archived): the controller stashes *"The subnet with ID 999
could not be found or may have been deleted."* and 302s to `/Error/404`. Tab B, on a form with a stale
antiforgery token, posts `/Subnet/Create`. **Wrong output:** tab B is served an HTTP 400 page titled
*"Bad Request"*, *"Status Code: 400"*, whose body reads *"The subnet with ID 999 could not be found or
may have been deleted."* - a statement about a request that browser never made - and tab A then gets the
generic *"The resource you requested could not be found."*

**Reachability is wider than filed, in two directions found by the verifiers.** (a) **No concurrency is
required at all:** an abandoned redirect (user hits Stop, closes the tab, the follow-up fails) leaves the
message pending indefinitely - 30 unrelated 200s later, a mistyped URL rendered the stale subnet message.
(b) **Two stale rows in two tabs is the most reachable path and it is not the filed one:** 10 of 12
real-Chromium trials had one tab printing the *other* tab's diagnostic. (c) A 4xx re-execute with
*nothing* pending still emits a TempData-clearing `Set-Cookie`, so an overlapping 4xx that started before
the 302 silently **deletes** the message rather than stealing it (3 of 36 runs).

**Reproduction.** One cookie jar = one browser, pristine `dcc15ab`:

```
GET /Subnet/Edit/999  -> 302 Location: /Error/404 ; Set-Cookie: .AspNetCore.Mvc.CookieTempDataProvider=...
GET /css/nope.css     -> 404, Content-Type: text/html, 6369 bytes
                         Set-Cookie: .AspNetCore.Mvc.CookieTempDataProvider=; expires=Thu, 01 Jan 1970
                         body: "Status Code: 404" + "The subnet with ID 999 could not be found..."
GET /Error/404        -> 404, SPECIFIC MESSAGE ABSENT: "The resource you requested could not be found."

POST /Subnet/Create (stale token) -> 400, <title>Bad Request - BASTET</title>,
                                     "The subnet with ID 999 could not be found or may have been deleted."
GET /Error/403 with the message pending -> 403, "Status Code: 403" + the same sentence.

Control: GET /Subnet (200) does NOT consume it - only a request reaching /Error does.
Real Chromium, 36 runs: clean=26, wrong-page-printed-it=10, tab-A-lost-it=3.
Real Chromium, two stale rows in two tabs, 12 trials: 10 crossed.
```

**Why low.** The text is server-composed from an int id (round 3's B6 removed the query-string source),
`_ErrorLayout.cshtml:7` Razor-encodes it, and the cookie is `SameSite=lax; httponly` and same-origin - so
no injection, no cross-user leak. What is left is a misleading diagnostic on the wrong page and a lost one
on the right page.

**Fix.** Only read the stashed message when the request really is the explicit redirect, not the
automatic re-execute. With `using Microsoft.AspNetCore.Diagnostics;`:

```csharp
string? errorMessage = HttpContext.Features.Get<IStatusCodeReExecuteFeature>() is null
    ? TempData["ErrorPageMessage"] as string
    : null;
```

Built and measured: 0 warnings, **716/716**, the interleaved `/css/nope.css` and antiforgery-400 pages
render their own generic text and the message survives to `/Error/404`, the intended redirect path is
unchanged, and as a bonus the patched re-execute emits no `Set-Cookie` at all, closing the silent-delete
variant. Unit tests are unaffected because `DefaultHttpContext` carries no such feature.

**Verifier correction - INCOMPLETE, and the interim is unsupported.**

1. **Redirect-vs-redirect collisions are not covered**, because neither request carries the feature.
   Measured on the *patched* build: `GET /Subnet/Edit/999` (stashes) then `GET /Azure/BulkImport` ->
   `/Error/403` (overwrites with *"Azure Import feature is not enabled"*); `/Error/404` then renders the
   Azure sentence and `/Error/403` renders the generic default. Same wrong output, after the fix. This is
   also the 10-of-12 real-browser path, i.e. the fix misses the variant an operator is most likely to hit.
   Direct navigation to `/Error/{code}` likewise still consumes a pending message.
2. **The "cheaper interim" (ship a `wwwroot/favicon.ico`) is not supported by measurement.** `/favicon.ico`
   does 404 with a 6348-byte HTML page, but real headless Chromium issued **zero** favicon requests across
   ~40 navigations, and a favicon fetch is triggered by a document that has already loaded - i.e. after the
   ~1 ms window, never inside it. Keep it only as *"removes one stray 404"*, never as *"removes the most
   common thief"*.
3. Side effect worth stating rather than discovering: because re-executes no longer flush the key, an
   abandoned message now survives strictly longer. It is never rendered wrongly, so this is not a
   regression.

The change that closes the whole class is making the message request-scoped instead of session-scoped -
answering in place from the eleven sites rather than round-tripping a diagnostic through a single-slot
browser cookie. That is the round-9 watch-list item, and it subsumes all three variants.

---

## J5 [x1] - Single-VNet import wizard: "Next: Select Subnets" stays enabled when `loadVNets` repopulates the dropdown, leaving a live-but-inert primary button

**Severity:** low | **Confidence:** confirmed | **Cite:**
`src/Bastet/Views/Azure/Import/_ImportScripts.cshtml:188` (`loadVNets`' `beforeSend`)

**What goes wrong.** On `/Azure/Import/{id}`: pick the subscription, click **Next: Select VNet**, pick a
VNet (the change handler at `:107-114` enables `#select-vnet-btn`), click **Back to Subscriptions**, then
**Next: Select VNet** again. No change event fires anywhere, `loadVNets` re-runs, and its success branch
does `$dropdown.empty()` (`:199`) and re-appends the disabled/selected placeholder (`:200`), which resets
the select's value to `""` **without** firing `change`. `beforeSend` (`:188-193`) resets `#vnet-loading`,
`#vnet-selection`, `#vnet-error` and `#no-vnets` but not the button, and `:110`/`:112` inside the change
handler are the **only** code in the tree that touches its disabled state.

**Wrong output:** the dropdown reads *"-- Select a Virtual Network --"* while **Next: Select Subnets**
renders as a fully saturated, hit-testable `btn-primary`. Clicking it hits
`selectedVNetId = $("#vnet-select").val()` -> `""` -> the `if (selectedVNetId)` guard at `:59` falls
through: no request, no step change, no spinner, no message. The wizard's primary Next button silently
does nothing.

This is the step-2 twin of round 7's G10, which fixed exactly this class for step 3's
`#select-all-subnets` / `#import-subnets-btn` by resetting them in `loadSubnets`' `beforeSend`
(`:255-256`); `loadVNets` never got the same treatment. The in-repo name for this shape is
`_ReconcileScripts.cshtml:68-72`: *"A permanently live, inert button"*.

**Reproduction.** Real headless Chromium (jQuery 4.0.0, Bootstrap 5.3.8), two verifiers with independently
written harnesses, four runs, identical result. One measured with a raw `page.Mouse.ClickAsync` so that
"the click landed" is not doing inferential work:

```
B first entry to step 2 : value=""  disabledProp=true  :disabled=true  opacity=0.65  pointer-events=none
C after picking a VNet  : value=".../virtualNetworks/rig-vnet-visible"  disabledProp=false  opacity=1
  [REQ] GET /Azure/GetVNets?subscriptionId=...&subnetId=1        (the second load)
E re-entry to step 2    : value=""  text="-- Select a Virtual Network --"  optionCount=3
                          disabledProp=false  hasDisabledAttr=false  opacity=1  pointer-events=auto
F CLICK ACCEPTED -> Azure requests after the click: []   count=0
                    step3 pill still DISABLED; #subnet-selection/#subnet-error/#no-subnets all d-none
                    #vnet-resource-id = ''      CONSOLE_ERRORS: 0
G control: pick a VNet -> GET /Azure/GetSubnets?vnetResourceId=...  -> step 3 ACTIVE, 2 rows
```

Screenshot of state F shows a solid blue **Next: Select Subnets** directly under the placeholder text.
Leg G proves the button is not broken, only mis-enabled.

**Reachability is slightly wider than filed:** `invalidateVNetStep()` (`:84-88`), which runs when the
subscription dropdown changes, disables `#step2-tab`, `#step3-tab` and `#import-subnets-btn` but likewise
never touches `#select-vnet-btn`, so switching subscriptions lands in the same state. Not claimed as
reproduced - the rig tenant exposes only one subscription.

**Fix - SOUND, and the only fix this round that needed no correction.** One line in `loadVNets`'
`beforeSend`, beside the four panel resets:

```javascript
$("#select-vnet-btn").prop("disabled", true);
```

Same placement and pattern `loadSubnets`' `beforeSend` already uses, so the wizard ends up with one rule
rather than two that disagree. Built and driven on the patched build: state E flips to
`disabled / :disabled / opacity 0.65 / pointer-events none`, Playwright then **refuses** the click, the
happy path is untouched (leg G still issues `GetSubnets` and activates step 3), 0 console errors, build
0/0, suite **716/716**. `loadVNets` has exactly one caller (`:43`), so `beforeSend` covers every entry;
`loadAzureSubscriptions()` is called once at document-ready and needs no equivalent.

**Two notes on the alternatives.** The "cheaper interim"
(`prop("disabled", !$("#vnet-select").val())` after the `$.each`) does close the reproduced path but is
strictly worse - it leaves the button stale on the error and no-vnets branches. Those branches are
currently unreachable to a user (`#vnet-selection` keeps its `d-none`, and the button's box is
zero-sized), so `beforeSend` placement is defence-in-depth there rather than a second live defect; that
is still the right home. **Do NOT fix this with `$("#vnet-select").trigger("change")`** - it would re-run
`invalidateSubnetStep()` as a side effect and reintroduce the synthetic-event pattern round 4's D1
removed.

---

## J6 [x1] - Production sign-out answers 500 and silently discards the session-cookie deletion when the OIDC discovery document is unavailable

**Severity:** low (filed medium; **corrected down by both verifiers** to match round 9's I6, which shipped
the byte-identical consequence at low) | **Confidence:** confirmed | **Cite:**
`src/Bastet/Controllers/AccountController.cs:67`

**What goes wrong.** Round 9's I6 fixed one way for `GET /Account/Logout` to answer 500 with the session
still alive (a non-ASCII `returnUrl` reaching the Location header). The same failure mode survives at the
sibling statement the fix did not touch, and needs no crafted input.

Preconditions: Production, `BASTET_OIDC_AUTHORITY` configured, and the process has not yet successfully
fetched `{authority}/.well-known/openid-configuration` - i.e. it started or restarted during an IdP
outage, a DNS/firewall change, or a misconfigured authority. `Program.cs` never touches the discovery
document at startup, so a Production process starts happily against an unreachable authority, and the
user's ticket survives the restart because the DataProtection key ring is DB-persisted
(`Program.cs:115-119`).

A user with a valid `.AspNetCore.Cookies` ticket clicks Logout. `:51-54` queues a `Set-Cookie` deletion
for every request cookie; `:67-70` returns
`SignOut(props, CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme)`.
The cookie handler queues its own deletion, then `OpenIdConnectHandler.SignOutAsync` throws
`IDX20803`. Because `SignOut(...)` is an `IActionResult`, the throw happens during **result execution**,
after the action returned, so nothing in the action can intercept it - and `UseExceptionHandler("/Error")`
calls `Response.Clear()`, taking every queued `Set-Cookie` with it (the source says so at
`Program.cs:539-546`).

**Wrong output:** HTTP 500 with **zero** `Set-Cookie` headers. The browser keeps its ticket, the cookie
handler validates it locally without contacting the IdP, and a privileged session the operator explicitly
asked to end keeps running for up to the 1 h sliding expiry.

**Reproduction.** Two Production instances of the **same** pristine DLL, differing only in whether
discovery resolves - a genuine same-branch control, unlike a Development comparison which returns through
`Redirect(target)` at `:74`:

```
A. discovery never fetched:
   HTTP/1.1 500 Internal Server Error ; set-cookie count: 0
   System.InvalidOperationException: IDX20803: Unable to obtain configuration from ... Connection refused
     at ConfigurationManager`1.GetConfigurationAsync
     at OpenIdConnectHandler.SignOutAsync
     at AuthenticationService.SignOutAsync
     at Microsoft.AspNetCore.Mvc.SignOutResult.ExecuteAsync(HttpContext httpContext)
     at ExceptionHandlerMiddlewareImpl

B. same build, discovery reachable:
   HTTP/1.1 302 Found ; Location: https://.../endsession?post_logout_redirect_uri=...
   Set-Cookie: .AspNetCore.Cookies=; expires=Thu, 01 Jan 1970 ...; secure; samesite=lax; httponly

C. scoping: kill the IdP AFTER B has cached the document, repeat -> still 302 + Set-Cookie.
```

One verifier went further and minted a genuine `.AspNetCore.Cookies` ticket against the same DB-persisted
key ring: `GET /Account/Roles` 200 rendering the user before Logout, 500 with zero `Set-Cookie` on Logout,
**200 again after** on the same jar, and the surviving session still reached the Admin-gated
`/HostIp/PurgeAllDeletedHostIps` (302 to the "nothing archived" redirect *inside* the action, where an
unauthorized request 500s).

**Why low, honestly stated.** The window is narrow and self-sealing - one successful discovery fetch
anywhere in the process's life closes it permanently. Inside that window the deployment is already
comprehensively broken: an unauthenticated `GET /` also returns 500 through the OIDC *challenge*, so
nobody can sign in at all, and the user sees a 500 page rather than a signed-out page (the finding's
*"while believing they signed out"* is overstated). What keeps it a real finding: every other 500 in that
window fails **closed**; this one fails **open**.

**Fix.** Do not let the local session teardown depend on the remote round trip, and do not perform the
remote leg as an `IActionResult`:

```csharp
if (!environment.IsDevelopment())
{
    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    try
    {
        await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
            new AuthenticationProperties { RedirectUri = target });
        return new EmptyResult();
    }
    catch (Exception)
    {
        return Redirect(target);
    }
}
```

Built: 0 warnings / 0 errors, no missing using. Patched build against an unreachable authority: **302
Location: /Account/SignedOut** with the deletion header present, and `/Account/SignedOut` renders 200
anonymously during the outage. Healthy path byte-equivalent to HEAD (same 302 to the real
`end_session_endpoint`, same two `Set-Cookie` headers). The finding is right that a `try` around
`SignOut(...)` as currently written would be unreachable code, and right that all three cheaper interims
fail (queue-order changes are all erased by `Response.Clear()`; pre-loading discovery at startup does not
help, because the trigger *is* the IdP being unavailable at process start).

**Verifier correction - INCOMPLETE.**

1. **It breaks 9 of the 716 shipped tests, and the fix does not mention them.** `dotnet test` on the
   patched copy: `total: 716, failed: 9, succeeded: 707`. All nine are in
   `test/Bastet.Tests/Security/AccountControllerLogoutTests.cs` - the four
   `Logout_Production_NonLocalOrMissingReturnUrl_RedirectsToSignedOutPage` cases (`:79`),
   `Logout_Production_LocalReturnUrl_IsPreserved` (`:90`) and the four
   `Logout_Production_ReturnUrlKestrelCannotWrite_RedirectsToSignedOutPage` cases (`:117`), each
   `Assert.IsType() Failure: Expected typeof(SignOutResult), Actual typeof(EmptyResult)`. **These are
   round 9's own I6 regression pins**, and they read `signOut.Properties.RedirectUri` - the only place
   `target` is pinned. They must be rewritten in the same commit to assert on the mocked
   `IAuthenticationService.SignOutAsync(context, OpenIdConnectDefaults.AuthenticationScheme, props)` (the
   mock already exists in that file's helper and captures the properties), plus a new case that makes the
   mock throw and asserts a `RedirectResult` to `SignedOutPath` - the regression test this finding
   actually needs and does not propose.
2. **The cookie leg should carry `AuthenticationProperties`.** `CookieAuthenticationDefaults.LogoutPath`
   is `/Account/Logout`, which equals the request path, so `HandleSignOutAsync` takes its redirect branch
   with a null `RedirectUri` and writes `Location: /Account/Logout` - a self-redirect. Harmless today only
   because both exits overwrite `Location`, i.e. the fix is correct by accident. Pass
   `new AuthenticationProperties { RedirectUri = target }` to the cookie leg too.
3. `catch (Exception)` also swallows `OperationCanceledException` on a client abort and is silent.
   `AccountController` has no logger; add `ILogger<AccountController>` to the primary constructor to
   match `Program.cs:257-268`'s `Bastet.Authentication` warning for the sign-in half.

No sibling call site: `SignOut(` appears exactly once in `src/`.

---

## J7 [x1] - Reconcile scopes a subnet by subscription before it recognises its resource ID, so a corrupt Azure link is never classified and the cascade guard blames a subscription that does not exist

**Severity:** low (filed medium; **corrected down by both verifiers** - both consequences are diagnostic,
nothing is destroyed and nothing becomes deletable) | **Confidence:** confirmed | **Cite:**
`src/Bastet/Services/Azure/AzureReconciler.cs:83`

**What goes wrong.** A Bastet subnet carries an `AzureResourceId` that is not a parseable ARM id - a
typo, a truncated value, a migrated string. `AzureResourceId` is free text on the entity
(`AzureResourceIdentity.cs:26` says so verbatim) and the only write-side check on the import paths is a
length check, so the app's **own** Admin API will write one: `POST /Subnet/BatchCreateChildSubnets` with
`subnets[0].AzureResourceId=this-is-not-an-arm-id` returned `{"success":true,"subnetIds":[2]}` and
persisted it verbatim. No hand-edited row is required.

In `BuildPlan` the subscription-scope test at `:83` runs **before** the three-way recognition at
`:94-108`. `BelongsToSubscription` is `resourceId.StartsWith($"/subscriptions/{subscriptionId}/")`, so
such a row fails it for **every** subscription id, is added to `notCovered` and `continue`d. It never
reaches the `UnrecognisedResourceId` arm at `:105-107` that exists precisely for it - whose own comment
says *"It is reported for review instead, so the operator can correct the row rather than have it
silently offered for archival."*

Two wrong outputs:

- **(A)** With no Azure-linked ancestor the scan returns empty `items`, `reviewItems`, `warnings` and
  `globalErrors`, so the wizard renders its nothing-to-clean banner over a subnet the scan never
  classified.
- **(B)** With such a row beneath a stale ancestor, `dcc15ab`'s new `notCovered` guard (`:137-140`)
  withholds the ancestor with *"...would also archive Azure-linked subnet(s) beneath them that **belong
  to a different subscription** and were not checked by this scan: 'stale-parent'."* That is **false** -
  the child names no subscription at all - and unactionable: the offending child is named nowhere in the
  response, and rescanning any other subscription will never surface it. The reconcile delete then answers
  409 with the same false reason.

**Reproduction.** All state created through real HTTP endpoints, then one scan over one tree with two
rows that are both "neither a VNet nor a subnet":

```
items       : []
reviewItems : [(3, 'control-storage-acct', 'UnrecognisedResourceId',
                'The recorded Azure resource ID names neither a VNet nor a subnet, so nothing can be
                 established about it. Correct or clear the link on this subnet.')]
Row 2 (AzureResourceId='this-is-not-an-arm-id') appears in NEITHER list. Only the prefix differs.

Under a stale ancestor:
  warnings: ["1 subnet(s) were withheld from deletion because archiving them would also archive
             Azure-linked subnet(s) beneath them that belong to a different subscription and were not
             checked by this scan: 'rig-vnet-gone-vc11'."]
  POST /Subnet/BulkDeleteStaleAzureSubnets {subnetIds:[4]} -> HTTP 409, same warning, DeletedSubnets = 0

Real Chromium, standalone broken row:
  SCAN-ERROR panel visible=False ; STALE/REVIEW sections visible=False
  NOTHING-TO-CLEAN BANNER visible=True
    "Everything imported from this subscription still exists in Azure. There is nothing to clean up."
  CONSOLE_ERRORS: 0
```

**Widened by one verifier:** a **truncated** id that names the scanned subscription itself
(`/subscriptions/<scanned-guid>`, no trailing slash) is skipped identically and produces the same
"different subscription" warning - which is then not merely unestablished but directly contradicted by
the row's own content.

**Two of the finding's claims are corrected, both narrowing it.** (1) The banner text quoted in the
finding (*"Everything still exists in Azure..."*) exists only in a code comment; the rendered banner is
subscription-scoped, and the defect case is byte-identical to a genuine other-subscription row, which is
the shipped, deliberate design. So (A) is better stated as *"a row that is in no subscription is reported
in no scan"* than as a false clean bill. (2) *"The stale ancestor can never be archived through the app"*
is **false**: `POST /Subnet/Delete/{id}` with `confirmation=approved` archived it immediately - the
ordinary delete page carries no Azure gate. The 409 is confined to the reconcile path, and it fires
identically on the *correct* control case, i.e. that refusal is the intended protection.

**Fix - SOUND.** Recognise the resource ID before scoping it: hoist the type test above `:83`, report an
unrecognised id as a `ReviewItems` entry, and `continue`; the `else` arm at `:103-108` then collapses.

```csharp
bool recognised = AzureResourceIdentity.IsAzureSubnet(snapshot.AzureResourceId)
                  || AzureResourceIdentity.IsAzureVNet(snapshot.AzureResourceId);
if (!recognised) { plan.ReviewItems.Add(Item(snapshot, AzureReconcileStatus.UnrecognisedResourceId, true,
        "The recorded Azure resource ID names neither a VNet nor a subnet, so nothing can be established "
        + "about it. Correct or clear the link on this subnet.")); continue; }
if (!BelongsToSubscription(snapshot.AzureResourceId, subscriptionId)) { notCovered.Add(snapshot.Id); continue; }
```

Built by both verifiers: 0 warnings / 0 errors, **716/716** - including the three tests the brief flags
as arrange-sensitive (`ApplyConfirmations_TargetWhoseDescendantIsAReviewItem_IsAlsoWithheld`,
`SubnetFromOtherSubscription_Ignored`, `StaleAncestorOverOtherSubscriptionDescendant_IsWithheld`), which
survive because this moves *classification*, not a guard, and their helpers emit parseable ARM ids. Live:
the broken child lands in `reviewItems`, the ancestor is still withheld with the honest wording
(*"...subnet(s) beneath them that were withheld from deletion"*), and a genuine other-subscription
descendant still produces the "different subscription" warning, so I2's regression path is intact.

Two caveats on implementation, from measurement rather than reading:

- **Write it as a two-way `if/else`.** Simply deleting the `else` arm leaves `AzureReconcileItem? item;`
  unassigned on one path and will not compile.
- **Do not oversell it.** The ancestor stays withheld after the fix and the 409 would still be returned;
  what the fix buys is that the operator is finally told *which row to correct*, so the dead end becomes
  escapable. A broken link now appears on every subscription's scan instead of none - which is correct,
  because it is in no subscription - and it lands in `ReviewItems`, which is never offered for deletion.

**The proposed cheaper interim is UNSOUND - do not ship it.** It adds a second
`WithholdTargetsWhoseCascadeIsBlocked` call with its own wording. That method returns early on
`if (protectedSubnetIds.Count == 0 || plan.Items.Count == 0)` (`:269`) and its first act on firing is
`plan.Items.RemoveAll(blocked.Contains)` (`:282`), so on an ancestor whose subtree holds **both** an
unrecognised row and a genuine foreign-subscription row, whichever guard runs first empties `plan.Items`
and the second returns silently - the new reason is swallowed and the operator still reads only "a
different subscription". Observed live. It also re-proposes the shape round 9 struck under I1 (*"a second
guard with its own wording is new surface for no measured gain"*) for strictly less benefit than the
two-line reorder.

**Regression test** (proposed and confirmed to fail on HEAD, pass on the patch): mirror
`StaleAncestorOverOtherSubscriptionDescendant_IsWithheld` with a `Linked(...)` whose resource id is
`"not-an-arm-id"`, asserting `Assert.Single(plan.ReviewItems)` and
`Assert.DoesNotContain(plan.Warnings, w => w.Contains("different subscription"))`. Add a second case for
the truncated `/subscriptions/<scanned-sub>` shape - that is the one where the shipped warning
contradicts the row's own text. Note the existing
`UnrecognisedResourceId_IsReviewedNotOfferedForDeletion` theory uses only ids prefixed with the scanned
subscription, so it pins exactly the half that works.

---

# Info

## J8 [x1] - Deleted-host-IP archive pages print the subnet's CURRENT prefix, so an archived address is shown inside a range that never contained it

**Severity:** info (filed low; one verifier left it as filed, the second corrected it down after finding
no consumer of the field beyond two Razor templates) | **Confidence:** confirmed | **Cite:**
`src/Bastet/Controllers/HostIpController.cs:570` (live-subnet branch), `:580-581` (archived branch),
`Models/DeletedHostIpAssignment.cs:21`

**What goes wrong.** Create `ARCHTEST = 10.150.0.0/24`, assign `10.150.0.200`, delete that host IP (it is
archived), then narrow `ARCHTEST` to `/25` - which `Subnet/Edit` allows, because
`ValidateSubnetCidrChangeWithHostIps` returns early when the subnet has **no live host IPs**, and
deleting the address is precisely what empties that collection. `/HostIp/AllDeletedHostIps` then renders
`10.150.0.200 | oldhost | ARCHTEST (10.150.0.0/25)` under a column headed **"Original Subnet"**.
`10.150.0.200` is not in `10.150.0.0/25`. The archive row has no prefix column - only `OriginalSubnetId` -
so the view has nothing truthful to print and re-derives it at render time.

That the pairing is read as a containment claim is not interpretation: `Views/HostIp/AllHostIps.cshtml:53`
renders **live** assignments with the byte-identical string `@hostIp.SubnetName
(@hostIp.NetworkAddress/@hostIp.Cidr)`, where containment is guaranteed by validation. And the displayed
pairing is unconstructible - re-creating it is refused with *"IP address 10.150.0.200 is outside the
subnet range 10.150.0.0/25"*.

**The finding's own diagnosis of the mechanism is wrong, and the defect is broader than filed.** It claims
the column is internally inconsistent - live rows showing current values, archived rows showing historical
ones. Both verifiers built the archived branch and it is wrong in exactly the same way: `SUBB` archived at
`/24`, narrowed to `/25`, then deleted, prints `SUBB (deleted) (10.151.0.0/25)` over `10.151.0.200`. Both
branches print the subnet's **last-known** prefix; neither branch has the datum. The prefix at
host-IP-deletion time is never stored anywhere.

**Reproduction.** Every step an ordinary UI POST; four archived rows on one page, each printed range
containment-tested:

```
step 4  POST /Subnet/Edit/1 Cidr=25  -> HTTP 302 -> /Subnet/Details/1   (accepted, no validation error)
        SQL: ARCHTEST 10.150.0.0/25 ; LiveHostIps = 0

GET /HostIp/AllDeletedHostIps:
  IP            | "Original Subnet"                  | IP in printed range?
  10.170.0.200  | ARCH3 (deleted) (10.170.0.0/24)    | True
  10.160.0.10   | ARCH2 (deleted) (10.160.0.0/25)    | True
  10.160.0.200  | ARCH2 (deleted) (10.160.0.0/25)    | False   <- archived branch
  10.150.0.200  | ARCHTEST (10.150.0.0/25)           | False   <- live branch, as filed

GET /HostIp/DeletedHostIps?subnetId=2 header:
  "View previously deleted host IP assignments for subnet ARCH2 (10.160.0.0/25)"
  over rows 10.160.0.10 and 10.160.0.200 - true original prefixes /25 and /24 respectively.

archive row: Id 1 | OriginalIP 10.150.0.200 | Name oldhost | OriginalSubnetId 1   (no prefix column)
POST /HostIp/Create SubnetId=1 IP=10.150.0.200 -> 200 with
  "IP address 10.150.0.200 is outside the subnet range 10.150.0.0/25"
```

**Why info.** Nothing computes with these fields - `grep` shows the only consumers of
`AllDeletedHostIpItemViewModel.NetworkAddress/Cidr` and `DeletedHostIpListViewModel.NetworkAddress/Cidr`
are the two Razor templates; no test asserts them, nothing writes from them. Every load-bearing audit fact
on the row is correct and the parenthetical is decoration. The one action a misled operator might take is
refused with an accurate message. The per-subnet page's header is also soft - it reads as a present-tense
description of the subnet you are viewing, and `_HostIpTable.cshtml` carries no per-row prefix at all; the
load-bearing wrongness is `AllDeletedHostIps.cshtml:65` under an explicitly historical column header.

**Fix.** Stamp the prefix onto the archive row instead of re-deriving it: add `OriginalNetworkAddress`
(nvarchar(15)) and `OriginalCidr` (int) to `DeletedHostIpAssignment` plus a migration, populate them in
both archive writers, and read them in the views. The two writers named are the only two -
`HostIpController.cs:397-408` (`OriginalSubnetId` at `:401`) and
`SubnetController.Delete.cs:196-207`; `grep` finds no third.

**Verifier correction - INCOMPLETE on three counts, and the interim is UNSOUND.**

1. **No backfill and no nullability decision.** Declared non-nullable, the migration fills existing rows
   with `('', 0)` and `AllDeletedHostIps.cshtml:63` - which guards on `SubnetName != "Unknown"`, not on the
   prefix - renders every pre-upgrade row as `NAME (/0)`. That exact output was measured. Make both columns
   nullable and suppress the parenthetical when null, or backfill in the migration.
2. **The `:688-692` call site is not well-defined.** `DeletedHostIpListViewModel`
   (`DeletedHostIpViewModels.cs:6-14`) carries **one** page-level prefix used in
   `DeletedHostIps/_Header.cshtml:5`, above N rows that can each now carry a different original prefix -
   measured: `ARCH2`'s two rows, archived at `/24` and `/25`. There is no single value to put there.
   Either move the prefix onto the per-row view model with a new column in `_HostIpTable.cshtml`, or leave
   that header describing the live subnet and relabel it.
3. **"Populate them in both writers" is not a field copy** - the prefix is not on `HostIpAssignment`. In
   `DeleteConfirmed` the row comes from `FindAsync(ip)` with no `Include` and no subnet loaded, so it needs
   an extra read inside the open transaction; in `ArchiveSubnetSubtreeAsync` `allHostIps` is a **flat** list
   across the whole subtree, so it needs a lookup keyed on `hostIp.SubnetId` (`toDelete` already holds
   every subnet).

**The interim was built and produces worse output than the defect.** Dropping the assignment at
`:570-571` renders `ARCHTEST (/0)` - the view has no discriminator to suppress the parenthetical. And the
branch the interim deliberately keeps is equally false (`SUBB (deleted) (10.151.0.0/25)` over
`10.151.0.200`), so "render the prefix only when it came from the archive" preserves the defect one row
up rather than removing it. **The only cheap and fully correct variant is the fix's own last alternative:
drop the parenthetical range from `AllDeletedHostIps.cshtml:65` altogether, or relabel it as the subnet's
*current* range.** The subnet name plus the `OriginalSubnetId` link is enough to navigate.

---

## J9 [x1] - Bulk import greys out Azure-subnet rows whose parent prefix is blocked, but prints no badge and no reason for them

**Severity:** info (filed low; one verifier corrected it down, the other left it as filed) |
**Confidence:** confirmed | **Cite:**
`src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:238` (the reason condition), `:232` (the disabled
condition), `:161` (`availabilityBadge` returning `""` for `"Available"`)

**What goes wrong.** `:232` disables a subnet checkbox when `!subnet.isSelectable ||
!prefixInfo.isSelectable`, but `:238-240` appends the explanatory reason only when
`!subnet.isSelectable`, and the badge helper returns the empty string for `Available`. So the rows
disabled **because their VNet prefix is blocked** render as a greyed-out checkbox with no badge and no
text - while the sibling row directly above them, disabled by the same line of code, carries both. That
asymmetry is the signature of `:238` not being updated when the `|| !prefixInfo.isSelectable` cascade was
added at `:232`; the in-file comment at `:229-231` documents the cascade and says nothing about
suppressing the reason.

This is also the steady state after any successful bulk import: the imported parent then has children, so
`AnnotatePrefix` returns `Blocked` on every later run while any Azure subnet added since is `Available`.

**Reproduction.** Real ARM, real headless Chromium, DOM dump of `#bulk-vnet-tree .form-check`:

```
bulk-prefix-3-0   disabled=true  badges=[10.60.0.0/16, Cannot import]
                  reason="Bastet subnet 'Rig Parent 10.60' already has child subnets. Already imported?"
bulk-subnet-3-0-0 disabled=true  badges=[Cannot import]
                  reason="Bastet subnet 'child-of-1060' already uses 10.60.1.0/24."
bulk-subnet-3-0-1 disabled=true  badges=[]  reason=(NONE)     <- rig-subnet-beta 10.60.2.0/24
bulk-subnet-4-0-0..3 disabled=true badges=[] reason=(NONE)    <- gamma2/delta2/eps2/zeta2
CONSOLE_ERRORS: 0

server payload for those rows: "statusName":"Available","reason":null,"isSelectable":true
"Hide unavailable" ON: both 10.60 cards vanish entirely; EMPTY_STATE "(none)" - no explanation either way.
select-all then preview: zero blocked rows submitted -> display-only defect.
```

**Two of the finding's claims are corrected, both narrowing it.** (1) It says six rows; it is **five** -
the sixth (`rig-full-span`) needs the 10.92 VNet imported first and renders **enabled** from the stated
inputs. (2) *"Zero explanation ... cannot tell them apart from rows unclickable by accident"* is too
strong: each such row sits indented inside its prefix's own card, directly beneath a red **Cannot import**
badge and a full sentence of reason, and the two can never be separated. The legend framing is the weakest
part of the finding: `_StepSelection.cshtml:8` is a badge glossary, not an invariant, and it is **already**
loose in the other direction at HEAD - a `Will update existing` prefix is neither greyed nor unselectable
(measured: `bulk-prefix-1-0 | disabled=false`).

**Fix - UNSOUND AS WRITTEN. It does not run.** The fix declares
`const subnetBlockedByPrefix` at the reason site (`:238`) and references it in the label built at
`:233-236` - a temporal dead zone. Razor never parses the embedded JS, so the project **builds clean**,
and then:

```
SELECTION_VISIBLE: False   LOADING_VISIBLE: True   TREE_ROWS: 0   ENABLED PREFIXES: 0
PAGEERROR: ReferenceError: Cannot access 'subnetBlockedByPrefix' before initialization
    at Object.<anonymous> (/Azure/BulkImport:574:40) at renderVNetTree (:520:15) at Object.success (:481:21)
```

`renderVNetTree` throws before `#bulk-vnet-selection` is un-`d-none`d, so step 2 of the bulk wizard spins
forever with zero checkboxes - strictly worse than the defect it repairs. Both verifiers built it
independently and hit the identical failure.

**Repair, built and measured:** hoist the declaration to just after `const $subnetDiv = ...`, before the
checkbox and label. All five rows then render `badges=[Cannot import]` plus *"Its VNet prefix cannot be
imported, so this subnet cannot be either."*, `rig-subnet-alpha` keeps its own specific reason, the
preview still builds the correct plan for the selectable VNets, and console errors are 0.

**Three further notes.**

- **The fix misses its own sibling case.** The `if (!subnet.isSelectable)` guard also swallows the reason
  of a *selectable* subnet, and `AnnotateSubnet` produces exactly one: the encompassing subnet, whose
  reason is *"Covers the whole VNet prefix, so it marks the target fully allocated instead of being
  created."* Measured `reason=(NONE)` on screen **before and after** the proposed fix. The smaller and more
  complete change is to append `reasonHtml(subnet.reason)` unconditionally and add the prefix-blocked
  sentence only when the row has no reason of its own - which also removes the extra branch.
- The `else if (subnetBlockedByPrefix)` is redundant inside the else (`subnet.isSelectable` is necessarily
  true there), and reusing the `bg-danger` **Cannot import** badge trades one legend inaccuracy for
  another - the legend defines that badge as *"conflicts with something already in BASTET"*, which is false
  for `rig-subnet-beta`. A distinct badge would keep the legend honest.
- The cheaper interim (soften the legend at `_StepSelection.cshtml:8`) is fine but incomplete: any legend
  rewrite must fix **both** directions, not only add the "sits under a greyed prefix" clause.

No sibling call site: `isSelectable` appears in exactly one view, and the only other `prop('disabled'`
view is the unrelated CIDR calculator.

---

# Refuted

| Candidate | What it claimed | Why it was killed |
|---|---|---|
| **Security-header middleware is bypassed by two responses written above it** - `src/Bastet/Program.cs:547`, filed **info**, `[x1]` | That the header middleware at `:547` sits *below* both error handlers, so two responses ship without `X-Content-Type-Options`, `Referrer-Policy`, `CSP: frame-ancestors` or `X-Frame-Options`: **(a)** the Development developer-exception page, which calls `Response.Clear()` and writes its own body carrying the `SqlException` text, catalog name, source paths, query string and cookie values; and **(b)** the Production plain-HTTP `307` from `UseHttpsRedirection` at `:536`, which short-circuits before `:547`. | **Both mechanisms reproduce byte for byte on the pristine build; neither is a defect.** **(b)** is an automatic refutation on this round's own list - it exists only where the deployment is configured for HTTPS. Controls run: with no `ASPNETCORE_HTTPS_PORT` there is no 307 at all and the response carries 4/4; with `X-Forwarded-Proto: https` (i.e. behind the TLS-terminating proxy) the redirect is suppressed and the response carries 4/4. The 307 is a bodyless `Content-Length: 0` hop with nothing to sniff, no document to frame and no navigation to leak a referrer from. **(a)** is Development-only and its consequence was measured to be nil: in real Chromium the page frames (so the header is genuinely absent and would be effective) but the attacker origin cannot read it - `BLOCKED:TypeError`; the `<script src>` include ERRORs; direct navigation gets an explicitly-typed `text/html` with the injected `<script>alert(1)</script>` HTML-encoded; the page has 0 anchors, 0 forms, 0 external subresources. Round 4 already priced these four headers on exactly this class of page (`AUDIT-FINDINGS-4.md:227-228`: *"framing it accomplishes nothing and MIME-sniffing is moot on a Razor `text/html` body"*). **The load-bearing overstatement was proved, not asserted:** the finding's stated wrong output is the *body* - which is `UseDeveloperExceptionPage` doing exactly what it is built to do - and running the finding's own proposed fix produced 4/4 headers **and** the identical body, `SECRETVALUE123` still twice, `QUERYSECRET456` still once, the catalog still four times. A fix that closes the cited gap and changes none of the narrated harm is a fix for a different problem. And the regime itself is permanently-accepted item 2: Development has no authentication at all (an anonymous curl renders "Admin" on `/Account/Roles` and passes the Admin-only purge gate), so a deployment reaching (a) has already handed full Admin to every anonymous visitor. What is left - *"the comment at `:497` says every response and it does not"* - is comment accuracy with no measurable wrong behaviour behind it. |
| **Single-VNet import wizard offers a VNet-spanning Azure subnet as an ordinary importable child; importing it creates nothing and silently flips the parent to fully-allocated** - `src/Bastet/Views/Azure/Import/_ImportScripts.cshtml:311`, filed **info**, `[x1]` | That `fullyEncompassesVNetPrefix` is carried in a hidden input at `:310` but never surfaced in the label, so the row renders like any other; ticking it imports **zero** subnets, sets `IsFullyAllocated = true`, overwrites the parent description, permanently bars child subnets and host IPs, and locks the operator out of the import wizard - with the only way back a button buried in the *Host IP Assignments* card footer. The bulk wizard states the same outcome up front (`AzureBulkImportPlanner.cs:273`, `_BulkScripts.cshtml:519-521`), so the codebase already treats it as something the operator must be told. | **Every mechanical claim reproduces at the cited lines; three of the consequence claims are false, measured.** *"Silently flips"* - the redirect target states it in the success banner, in the description, and in two "Fully Allocated" alerts. *"Permanently barred ... buried in the card footer"* - the **Mark as Not Fully Allocated** button is on that same landing page at docTop 958 of a 1098px document, no confirm and no navigation; one click restores *Add Child Subnet* and makes `/Azure/Import/1` answer 200 as the wizard. *"Overwrites the parent description"* - `AppendFullyAllocatedNote` (`SubnetController.Azure.cs:85-101`) **appends** and preserves; over 1000 chars it keeps the existing description and drops the note. *"Importing it creates nothing"* is vacuous and the aggravated case is unreachable: the encompassing branch fires only when the Azure prefix equals the parent's network **and** cidr exactly, and ARM refuses a second same-VNet subnet nested inside it (`NetcfgSubnetRangesOverlap`, run live) - `ROW_COUNT` was 1 and is 1 by construction, so the `:380` skip never discards anything the operator ticked. With the row unticked the Import button stays disabled, so the only two reachable outcomes are the correct row or nothing. Nothing wrong is written either: when the flag is true the Azure subnet genuinely **is** the whole VNet, and the write is server-validated first (prefix equality `:302-311`, `ValidateSubnetCanBeFullyAllocated` `:313`), so the wizard cannot reach a state the manual path forbids. What survives is one sentence of UI copy - a badge the bulk wizard's row carries and this one does not - which is the shape round 7 killed for F10 and which this round's rules put on the wrong side of *"not a runtime defect is not a finding"*. Residue, too small to file: after un-flagging, the appended note *"Fully allocated by Azure subnet 'rig-full-span'..."* stays in the description (`DescLen 91` with `IsFullyAllocated=0`). |

---

# Watch list

Not findings. Known, accepted, deferred, or measured dead ends. Knowing these stops a later round filing
something already understood - and several are the *reason* a nearby defect is worth more or less than it
looks.

## Corrections to earlier rounds' watch lists

- **Round 9's entry "Delete's tree read is once per request" is FALSE for the reconcile delete path.**
  It is **twice per selected target** (`AzureReconcile.cs:143` plus the one inside
  `ArchiveSubnetSubtreeAsync`), and one request carries as many targets as the operator ticked. That
  entry is exactly what would have deterred filing J1. Re-derive scaling claims per *path*, not per
  method.
- **Round 7's `/Error/*` note recorded that the route segment is caller-supplied; it did not record that
  the FORM outranks the route.** `FormValueProviderFactory` precedes `RouteValueProviderFactory` in the
  default composite value provider. That is J3.

## Measured dead ends - do not re-propose

- **The per-subnet host-IP `Include` N+1 at `SubnetController.Delete.cs:182-192` is not worth fixing.**
  Replacing it with a single `WHERE SubnetId IN (...)` moved a 20,081-subnet root delete from 22.50 s to
  **22.74 s**, despite removing 20,081 round trips.
- **Removing only the redundant `GetAllDescendantsOrdered` at `AzureReconcile.cs:143` is not worth taking
  on its own** - 59.4 s -> 57.3 s. The per-target `SaveChangesAsync` is the dominant term.
- **Do not add a second `WithholdTargetsWhoseCascadeIsBlocked` call with its own wording.** It returns
  early when `plan.Items` is empty and empties `plan.Items` on firing, so whichever guard runs first
  swallows the other's warning. Round 9 reached the same conclusion under I1 by a different route.
- **Do not fix a stale control with `$(...).trigger("change")`** - it re-runs the invalidate cascade as a
  side effect and reintroduces the synthetic-event pattern round 4's D1 removed.
- **A `wwwroot/favicon.ico` fixes nothing.** Real headless Chromium issued **zero** favicon requests
  across ~40 navigations, and a favicon fetch always lands after the document that already consumed the
  message.

## Traps for anyone writing these fixes

- **Razor does not parse embedded `<script>` JS.** A view-embedded JS error - TDZ, typo, wrong scope -
  **builds at 0 warnings / 0 errors** and fails only at runtime, and a wizard step that throws in a
  render loop is left permanently spinning. Two of this round's proposed fixes were exactly this shape.
  Anything touching a `.cshtml` script block must be driven in a browser before it is called done.
- **`SubnetController.AzureReconcile.cs` has no `using Microsoft.EntityFrameworkCore;`.** Any async LINQ
  call added there is `error CS0411`. Round 9 recorded the same trap in `AccountController.cs`.
- **Any cached subnet tree passed into `ArchiveSubnetSubtreeAsync` must be a TRACKING read.** The
  per-subnet host-IP `Include` at `Delete.cs:184-186` tracks fresh instances of every descendant, and
  `Remove()` on a detached cached instance throws *"another instance with the same key value is already
  being tracked"*. A flat leaf-only benchmark cannot see this - test nesting.
- **`ArchiveSubnetSubtreeAsync` has exactly two callers** (`Delete.cs:136`, `AzureReconcile.cs:144`) and
  **`GetAllDescendantsOrdered` exactly two** (`Delete.cs:174`, `AzureReconcile.cs:143`). Optional
  parameters leave the single-subnet delete path unchanged.
- **The nine `AccountControllerLogoutTests` assert on the returned `SignOutResult` and read
  `Properties.RedirectUri`.** Any change to `Logout`'s return type breaks all nine (716 -> 707). They are
  round 9's own I6 pins - rewrite them in the same commit, against the mocked `IAuthenticationService`.
- **`CookieAuthenticationDefaults.LogoutPath` is `/Account/Logout`**, so a cookie-scheme sign-out on that
  path self-redirects unless given a `RedirectUri`. `AccountController` has no `ILogger` injected.
- **`lastPlan` exists only in `_ReconcileScripts.cshtml`.** The bulk wizard's client state is
  `lastSelection` and `previewSeq`. Do not write a fix that "reuses" it.
- **`ErrorControllerTests.cs:32, 46, 94, 112` call `HttpStatusCodeHandler(int)` directly.** Any signature
  change is `CS1501` in four places.

## Behaviour of the framework and the app that shaped this round's verdicts

- **`StatusCodePagesMiddleware` skips any response that already has a `Content-Type`.** The three literal
  `StatusCode(403, <json>)` sites are therefore never re-executed; only bodiless framework statuses reach
  `/Error/{code}`. Measured both ways on one endpoint.
- **Development's `DevAuthHandler` issues Admin** (satisfying all four policies) and Production's cookie
  handler has `AccessDeniedPath`, so a Forbid is a 302. The shipped build emits **no launderable bodiless
  401/403**. Any finding about denial-status handling must say which of those it assumes away.
- **`Program.cs:22` pins `Microsoft.AspNetCore` to `Warning` outside Development**, so Production logs
  neither `Request finished` nor `Authorization failed` at default levels - measured 0 lines. Do not build
  a consequence on request-log content without stating the log level.
- **The OIDC `ConfigurationManager` is lazy and caches.** One successful discovery fetch closes J6's window
  permanently for that process; and inside the window an unauthenticated `GET /` is already 500 through the
  challenge, so the deployment is loudly broken, not silently.
- **`ValidateSubnetCidrChangeWithHostIps` returns early when a subnet has no LIVE host IPs.** Deleting a
  host IP is the prerequisite for narrowing, so **the archive is the one place an address can survive the
  very edit that would have blocked it.**
- **`POST /Subnet/BatchCreateChildSubnets` writes `AzureResourceId` verbatim.** The only write-side check
  is length (500 chars); `AzureResourceIdentity.cs:26` states the column is free text. The app will not
  self-inflict a corrupt id, but its own Admin API will write one.
- **The ordinary delete page has no Azure gate.** `POST /Subnet/Delete/{id}` archives a stale Azure-linked
  ancestor that `BulkDeleteStaleAzureSubnets` refuses with 409. "The reconcile wizard refuses it" never
  means "the app refuses it".
- **`AzureResourceId` is written only by the two import paths and never cleared.** `EditSubnetViewModel`
  does not carry it, no view offers an unlink, and `SubnetController.Edit.cs:92` makes a CIDR change throw
  when it is non-empty. `DeletedSubnets` archives no `AzureResourceId` and **there is no restore path
  anywhere in the app** - re-confirmed from the live schema. This is what makes J2 unrecoverable.
- **`GlobalSanitizationFilter` does not descend into the nested bulk selection list** (which is why
  `AzureBulkImportPlanner` sanitizes names itself). Any new nested DTO field arrives unsanitized.
- **The reconcile delete re-derives its verdict server-side from ids alone and answers 409 on divergence;
  the bulk import commit trusts the posted selection.** That asymmetry is J2.
- **`appsettings.Development.json` already sets `Microsoft.EntityFrameworkCore.Database.Command` to
  `Information`** - no instrumentation is needed to count queries in Development.

## Rig and method notes

- **Playwright's `ClickAsync` refuses a disabled control**, so "the click was accepted" is itself the
  enabled-ness proof - and "the click was refused" is the proof a fix landed. To measure a click that must
  land regardless of state, use raw `page.Mouse.ClickAsync` at the element centre.
- **Two service principals with disjoint RBAC is what makes an Azure verdict falsifiable.** SP_B scanning a
  resource it cannot read produces *"Azure denied access when asked about them directly"* and correctly
  **suppresses** the nothing-to-clean banner - the design working, and the control that shows a missing
  banner is a real signal rather than an artefact.
- **An untracked `idp.pid` appeared in the repository root mid-round** (from another agent's stub IdP) and
  was reported by three verifiers as not theirs. The working tree must be verified clean immediately before
  any commit; `git status --porcelain` was empty at HEAD `dcc15ab` at the start and end of every verified
  session otherwise.
- **Round-9 line numbers had already moved; round-10's will move again.** Re-derive every citation before
  acting on it.
