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

_J2 is fixed and committed. `BulkImportSelectedVNetPrefixDto` gained an optional `Expected`
(`BulkImportExpectedTargetDto`: target type by name, the two target subnet ids, the rename pair and
the fully-allocated flag), the bulk wizard stashes each previewed outcome onto the very object the
commit posts (`attachApprovedOutcomes`, new client state — the finding's claim that the wizard already
holds this in `lastPlan` is false, that exists only in `_ReconcileScripts.cshtml`), and
`BulkCreateFromAzurePlanCore` compares the re-derived plan against it immediately after `BuildPlan`,
answering **409** with the differences and writing nothing — the same discipline
`BulkDeleteStaleAzureSubnets` already applies._

_**Two decisions the finding left open.** The expectation stays optional, so the documented direct
JSON API keeps working; a prefix that carries none is counted and written to the log as an unverified
commit rather than passing silently, which is the verifier's "log it" option. And because
`GlobalSanitizationFilter` does not descend into the nested selection list, **nothing from the caller
is echoed into the 409 body** — every divergence is described from the server's own re-derived plan,
targets are labelled with the plan's parsed `PrefixNetworkAddress/PrefixCidr`, a prefix with no plan
item is identified by its position in the submitted list, and a changed rename target is reported as
"the name has changed" without repeating it. A test pins that the caller's description string does not
come back._

_Measured on the live rig — real ARM (`rec-vnet-div` 10.151.0.0/16 with two subnets), real headless
Chromium, real SQL Server. Control: preview then commit with no interference imports normally
(1 target, 2 children), 0 console errors, 0 page errors. Interleaved: with the plan on screen showing
"New top-level — create rec-vnet-div", a second admin creates `10.151.0.0/16` by hand through
`/Subnet/Create`; the commit now answers **409** "The plan changed since it was previewed, so nothing
was imported", and the hand-made row survives with `AzureResourceId` still null — unadopted, unstamped
and unrenamed, where before it was adopted. The audit's own diagnostic that "the preview and commit
request bodies are byte-identical on the wire" is no longer true: the commit now carries the approved
outcome._

_Five regression tests added (`SubnetControllerBulkAzureImportTests`), 718 → **723**. Two pin the
refusal and the divergence message; three pin what must NOT break — an unchanged plan still commits,
an adoption that was itself previewed and approved still commits (the advertised adopt path, which the
finding correctly says a blanket server-side rule would have broken), and a caller with no expectation
is not refused. Confirmed failing against unfixed code by reverting only the guard hunk while keeping
the DTO, so the failure is behavioural rather than a compile error: both refusal tests returned
`OkObjectResult` — the commit adopting the subnet — instead of `ConflictObjectResult`._

_Not done: the finding's two rejected alternatives were left rejected, and its reasoning confirmed.
Re-running the preview at commit time is the same defect one step later, and a server-only rule
refusing `ExactMatch` onto an unlinked row would break the adopt path that
`AzureBulkImportPlanner.cs:225-226` advertises as selectable._

---

# Low

_J3 is fixed and committed. `HttpStatusCodeHandler` is now parameterless and reads the status off
`RouteData`, falling back to the status already on the response. The verifier's recommendation was
taken over the finding's own `[FromRoute]` suggestion, because `[FromRoute]` closes only three of the
four legs: when the form reader throws, MVC abandons binding for **every** source, not just the form,
so the NUL-byte and malformed-multipart posts would still have arrived as `statusCode = 0` and been
turned into 500 by the out-of-range guard._

_Measured against the live app on real SQL Server, Development, before and after. Laundering:
`POST /Subnet/Create Name=x&statusCode=404` **404 → 400**, `statusCode=451` **451 → 400**,
`POST /No/Such/Path statusCode=451` **451 → 404**; the control with no field was 400 throughout.
Unreadable body: `Name=x%00y` **500 → 400**, with the page text going from *"Status Code: 0"* to
*"Status Code: 400"*. Nothing else moved: `GET /Subnet/Details/99999` followed still lands on 404,
`/Error/409` still answers 409, and `/Error/200` still answers 500, so the deliberate guard against a
caller-supplied route segment reaching a 2xx is intact._

_The four `ErrorControllerTests` call sites were updated through a single `Invoke` helper that sets
the route value, rather than four separate edits — the action is no longer callable with an argument,
which is the point. One regression test added for the leg that motivated the parameterless variant
(`HttpStatusCodeHandler_NoRouteValue_UsesTheStatusAlreadyOnTheResponse`), 723 → **724**._

_Scope, unchanged from the finding: the 403-laundering leg was **not** treated as live. The second
verifier established the shipped build emits no launderable bodiless 401/403 — Development's
`DevAuthHandler` issues Admin, Production's cookie handler has `AccessDeniedPath` so a Forbid is a
302, and the three literal `StatusCode(403, <json>)` sites write a body, which
`StatusCodePagesMiddleware` skips. The fix closes it as mechanism regardless; no deployment emitting
such a response was demonstrated, and none was invented to justify a wider change._

---

_J4 is fixed and committed, but **not with the fix as proposed**. The suggested
`IStatusCodeReExecuteFeature` guard was rejected on the verifier's own measurement: it closes the
re-execute leg while leaving redirect-vs-redirect untouched, and that is the 10-of-12 real-browser
path — neither request in that pair is a re-execute, so neither carries the feature. Shipping it would
have fixed the variant an operator is least likely to hit._

_Instead each redirect now gets its own slot. A new `ErrorPageMessages` helper mints an opaque token,
stores the diagnostic against it, and puts the token in the redirect's route; `ErrorController` reads
only the entry its own token names. All eleven stashing sites (`Read.cs`, `Delete.cs` x2, `Edit.cs`
x4, `AzureController.cs` x4) became one-line `this.RedirectToErrorPage(status, message)` calls, which
also removes eleven copies of the stash-then-redirect pair. Round 3's B6 is not reintroduced: the
token is a lookup key that is never rendered, and the message text still comes from TempData set
server-side, never from the URL. Pending messages are capped at five, oldest dropped first, because
TempData keeps anything unread and an abandoned redirect would otherwise accumulate in the cookie for
the whole session — the finding notes that abandonment already leaves a message pending indefinitely._

_All three variants measured against the live app, one cookie jar per browser. **(1)** With
`/Subnet/Edit/999` pending, an unrelated `GET /css/nope.css` now renders the generic "The resource you
requested could not be found." and the intended page still renders "ID 999 could not be found or may
have been deleted" — before, those were the other way round. **(2)** Redirect-vs-redirect: tab A
(`Edit/999`) and tab B (`Details/888`) each now receive their own message; before, tab A received tab
B's and tab B got the generic. **(3)** The silent-delete variant: after an unrelated 404, a pending
message still arrives, because a request with no token neither reads nor clears anything._

_Four existing tests asserted the single shared slot and were rewritten against the new mechanism —
they now read the message back through the redirect's own token, which is what the page does. Two
regression tests added, including one pinning that another redirect's message is **neither shown nor
consumed**, 724 → **725**. `ErrorPageMessages` is public rather than internal so the tests exercise the
real path instead of reimplementing its storage format; the assembly has no `InternalsVisibleTo`._

_Not done, on the finding's own evidence: no `wwwroot/favicon.ico` — real headless Chromium issued
zero favicon requests across ~40 navigations, and a favicon fetch lands after the document has already
consumed the message. The round-9 watch-list item about answering in place from the eleven sites
rather than round-tripping through a cookie remains the larger structural change; this closes all
three observed variants without restructuring eleven actions' return types for a low-severity finding._

---

_J5 is fixed and committed, exactly as proposed — the only fix this round that needed no correction.
One line in `loadVNets`' `beforeSend`, beside the four panel resets:
`$("#select-vnet-btn").prop("disabled", true);`. Same placement and pattern `loadSubnets`' `beforeSend`
already uses, so the wizard now has one rule rather than two that disagree, and it covers the error and
no-vnets branches for free._

_Measured in real headless Chromium against the live rig, before and after, walking subscription →
Next: Select VNet → pick a VNet → Back to Subscriptions → Next: Select VNet. **Before:** state E read
`value="" disabled=false opacity=1 pointer-events=auto` — a fully saturated primary button over the
"-- Select a Virtual Network --" placeholder — and the click was **accepted** while issuing zero Azure
requests and never opening step 3. **After:** state E reads
`value="" disabled=true opacity=0.65 pointer-events=none` and Playwright **refuses** the click, which
is itself the proof, since it will not click a disabled control. The happy path is untouched: picking a
VNet still enables the button, still issues `GetSubnets`, and still advances. 0 console errors and 0
page errors in both runs._

_No test ships with this. It is view-embedded JavaScript with no unit-testable seam — the repo has no
harness for driving a Razor script block, and the verification above is recorded as prose for that
reason. Suite unchanged at **725**._

_Two alternatives left unused, both on the finding's own evidence. The "cheaper interim"
(`prop("disabled", !$("#vnet-select").val())` after the `$.each`) closes the reproduced path but leaves
the button stale on the error and no-vnets branches, so `beforeSend` is the better home. And
`$("#vnet-select").trigger("change")` was **not** used: it would re-run `invalidateSubnetStep()` as a
side effect and reintroduce the synthetic-event pattern round 4's D1 removed._

---

_J6 is fixed and committed, with all three of the verifier's corrections the fix itself omitted. The
local session is now ended first and separately — `await HttpContext.SignOutAsync(cookie, properties)`
— then the OIDC leg runs inside a `try`, returning `EmptyResult` on success and `Redirect(target)` if
it throws. Doing it in the action rather than returning `SignOut(...)` is the whole point: as a
`SignOutResult` both legs ran during **result execution**, after the action had returned, where
nothing could intercept the throw and `UseExceptionHandler`'s `Response.Clear()` discarded every
queued `Set-Cookie` with it._

_The corrections: **(1)** `AuthenticationProperties` is passed to the **cookie** leg too —
`CookieAuthenticationDefaults.LogoutPath` is `/Account/Logout`, this very path, so without it
`HandleSignOutAsync` takes its redirect branch with a null `RedirectUri` and writes a self-redirect,
correct today only by accident because both exits overwrite `Location`. **(2)**
`ILogger<AccountController>` is injected and the swallowed exception is logged at warning, matching
`Program.cs`'s `Bastet.Authentication` warning for the sign-in half; the controller previously had no
logger at all. **(3)** The nine round-9 I6 pins were **rewritten in this commit**, not deleted._

_Measured with two Production instances of the same build differing only in the fix, both pointed at
an authority with nothing listening, so discovery can never succeed — a genuine same-branch control
rather than a Development comparison, which returns through a different branch. **Unfixed:**
`GET /Account/Logout` → **HTTP 500 with zero `Set-Cookie` headers**; the browser keeps its ticket.
**Fixed:** → **HTTP 302 `Location: /Account/SignedOut`** carrying
`.AspNetCore.Cookies=; expires=Thu, 01 Jan 1970 ...; secure; samesite=lax; httponly`, and
`/Account/SignedOut` renders 200 anonymously during the outage. Fail-open became fail-closed._

_The nine pins now assert on the mocked `IAuthenticationService` rather than on
`SignOutResult.Properties.RedirectUri`, which no longer exists — same guarantee, one layer down,
through a small `LogoutHarness` that captures the properties handed to each scheme. All four
`NonLocalOrMissingReturnUrl` cases, `LocalReturnUrl_IsPreserved` and all four
`ReturnUrlKestrelCannotWrite` cases survive, so round 9's I6 protection is intact. Two tests added: the
unreachable-IdP case (the regression test this finding needed and did not propose) and one pinning
that the cookie leg carries the redirect target. 725 → **727**._

_The finding's own judgements were confirmed rather than taken on trust: a `try` around `SignOut(...)`
as written really would be unreachable code, and all three cheaper interims really do fail —
queue-order changes are all erased by `Response.Clear()`, and pre-loading discovery at startup cannot
help when the trigger is the IdP being unavailable at process start._

---

_J7 is fixed and committed, as proposed. The three-way recognition now runs **before** the
subscription-scope test in `BuildPlan`: a link that is neither a VNet nor a subnet id goes straight to
`ReviewItems` and `continue`s, and the remaining branch is a two-way conditional. Written that way on
the verifier's warning — simply deleting the `else` arm leaves `AzureReconcileItem? item;` unassigned
on one path and does not compile._

_Why it mattered: `BelongsToSubscription` is a `StartsWith` over `"/subscriptions/{id}/"`, so a value
that names no subscription failed it for **every** subscription, landed in `notCovered`, and never
reached the `UnrecognisedResourceId` arm that exists precisely for it. The row was reported in no list
on any scan, and rescanning a different subscription could never surface it either._

_Measured live against real ARM through the app's own endpoints. The corrupt row was created by the
application itself, not hand-edited: `POST /Subnet/BatchCreateChildSubnets` with
`subnets[0].AzureResourceId=this-is-not-an-arm-id` returned `{"success":true,"subnetIds":[7]}` and
persisted it verbatim, confirming the finding's claim that the only write-side check is length. A real
`POST /Azure/ReconcileScan` then returned `reviewItems: [(7, UnrecognisedResourceId, "The recorded
Azure resource ID names neither a VNet nor a subnet...")]` with `items` empty and no warnings — where
before the fix that row appeared in **neither** list._

_Three regression cases added (`UnparseableResourceId_IsReviewed_NotSilentlySkipped` for both
`not-an-arm-id` and the truncated `/subscriptions/<scanned-sub>` shape, plus
`StaleAncestorOverUnparseableDescendant_IsWithheldWithoutBlamingASubscription`), 727 → **730**. All
three confirmed failing against the unfixed reconciler with "collection was empty" — the ReviewItems
list the row never reached. The existing
`UnrecognisedResourceId_IsReviewedNotOfferedForDeletion` theory uses only ids prefixed with the
scanned subscription, so it pinned exactly the half that already worked._

_One correction to the finding, found while writing the test rather than by reading. It says the
ancestor "stays withheld after the fix"; that is true, but **not at `BuildPlan` time**. The
review-item cascade guard runs inside `ApplyConfirmations` (round 9's I1 design), so the ancestor is
still in `plan.Items` after the scan and is withheld only once Azure confirms its VNet is gone — which
is the point at which the delete path asks. The test asserts it through `ApplyConfirmations` for that
reason, and confirms the honest wording replaces the false "belongs to a different subscription"._

_The three tests the brief flagged as arrange-sensitive
(`ApplyConfirmations_TargetWhoseDescendantIsAReviewItem_IsAlsoWithheld`,
`SubnetFromOtherSubscription_Ignored`, `StaleAncestorOverOtherSubscriptionDescendant_IsWithheld`) all
survive unchanged, and a genuine other-subscription descendant still produces the "different
subscription" warning, so I2's regression path is intact._

_The proposed cheaper interim was **not** taken, and its rejection is confirmed: a second
`WithholdTargetsWhoseCascadeIsBlocked` call returns early when `plan.Items` is empty and empties
`plan.Items` on firing, so whichever guard runs first swallows the other's reason. It also re-proposes
the shape round 9 struck under I1._

---

# Info

_J8 is fixed and committed, but **the proposed fix was declined**. Adding `OriginalNetworkAddress` and
`OriginalCidr` to `DeletedHostIpAssignment` plus a migration is disproportionate to an info-severity
display defect, and the verifier measured three unresolved problems with it: declared non-nullable the
migration backfills `('', 0)` and every pre-upgrade row renders as `NAME (/0)`; the page-level header
on the per-subnet view has no single truthful value once rows in one subnet were archived at different
prefixes; and "populate them in both writers" is not a field copy, because the prefix is not on
`HostIpAssignment` — `DeleteConfirmed` would need an extra read inside the open transaction and
`ArchiveSubnetSubtreeAsync` a lookup keyed on `hostIp.SubnetId`._

_Taken instead: the verifier's own "only cheap and fully correct variant" — **drop the parenthetical
range** from `AllDeletedHostIps.cshtml`. The subnet name and the `OriginalSubnetId` link are enough to
navigate, and the column stops making a containment claim it has no data to support. A comment records
why the range is absent, so it is not helpfully restored later._

_Reproduced first, through ordinary UI posts only. `ARCHTEST` created as `10.150.0.0/24`,
`10.150.0.200` assigned and deleted, then the subnet narrowed to `/25` — accepted with no validation
error, because `ValidateSubnetCidrChangeWithHostIps` returns early when a subnet has no **live** host
IPs, and deleting the address is precisely what empties that collection. `/HostIp/AllDeletedHostIps`
then rendered `10.150.0.200 | oldhost | ARCHTEST (10.150.0.0/25)`, and a containment test on the
printed range returned **False**. After the fix the same row reads `ARCHTEST`, with no range printed
and the "Original Subnet" column header intact._

_**Orphan sweep.** Removing the only consumer left `AllDeletedHostIpItemViewModel.NetworkAddress` and
`.Cidr` dead — nothing else reads them, no test asserts them — so both properties and their three
population sites in `HostIpController` were removed in the same commit. The compiler reports none of
this. `DeletedHostIpListViewModel.NetworkAddress/Cidr` are a **different** view model and are still
live in `DeletedHostIps/_Header.cshtml`; they were left alone._

_The per-subnet page header was deliberately not changed. It reads as a present-tense description of
the subnet being viewed rather than a per-row historical claim, and it is the one place a single
page-level prefix is defensible; verified still rendering
"...for subnet ARCHTEST (10.150.0.0/25)". The load-bearing wrongness was the per-row pairing under an
explicitly historical column, and that is what was removed._

_The interim the finding proposed was **not** taken and is confirmed unsound: dropping only the
live-subnet assignment renders `ARCHTEST (/0)`, because the view has no discriminator with which to
suppress the parenthetical, and the archived branch it preserves is false in exactly the same way
(`SUBB (deleted) (10.151.0.0/25)` over `10.151.0.200`). Suite unchanged at **730** — no test asserted
these fields._

---

_J9 is fixed and committed, but **not with the fix as written, which does not run**. It declares
`const subnetBlockedByPrefix` at the reason site and reads it in the label built above — a temporal
dead zone. Razor never parses the embedded script, so the project builds at 0 warnings and then throws
`ReferenceError: Cannot access 'subnetBlockedByPrefix' before initialization` inside `renderVNetTree`,
before `#bulk-vnet-selection` is un-hidden: step 2 spins forever with zero checkboxes, strictly worse
than the defect it repairs. Both verifiers built it independently and hit the identical failure._

_Taken instead, the verifier's smaller and more complete repair: the declaration is hoisted to just
after `$subnetDiv` is created, and `reasonHtml(subnet.reason)` is appended **unconditionally** with the
prefix-blocked sentence added only when the row has no reason of its own. That removes the
`if (!subnet.isSelectable)` branch rather than adding one, and it fixes the sibling case the proposed
fix misses — the guard also swallowed the reason of a *selectable* row, and `AnnotateSubnet` produces
exactly one: the encompassing subnet's "Covers the whole VNet prefix, so it marks the target fully
allocated instead of being created."_

_The badge is deliberately **not** the red "Cannot import". The legend defines that as conflicting with
something already in BASTET, which is untrue of a row whose own status is Available and which is
unselectable only because its prefix is; reusing it would trade one legend inaccuracy for another. A
distinct neutral "Blocked by VNet prefix" badge is used instead._

_Measured in real headless Chromium against real ARM, before and after, with a DOM dump of every row.
Fixtures: Azure VNet `rec-vnet-blk` 10.60.0.0/16 with `rec-b1` 10.60.1.0/24 and `rec-b2` 10.60.2.0/24,
against a Bastet `10.60.0.0/16` that already has a child — so the prefix annotates Blocked while
`rec-b2` stays Available, which is J9's exact shape. **Before:** `bulk-subnet-0-0-1`
`disabled=true badges=[] reason=(NONE)`, beside a sibling disabled by the same line of code carrying
both a badge and a full sentence. **After:** `badges=['Blocked by VNet prefix']` and
*"Its VNet prefix cannot be imported, so this subnet cannot be either."*, while `bulk-subnet-0-0-0`
keeps its own specific reason rather than being overwritten by the generic one, and the selectable
rows are unchanged. **0 console errors and 0 page errors**, and the wizard still previews and commits
end to end (1 VNet target, 2 child subnets). The tree renders — which is the whole point, given how
the proposed fix failed._

_No test ships with this: it is view-embedded JavaScript with no unit-testable seam, and the browser
run is the verification. Suite unchanged at **730**._

_The finding's two self-corrections were confirmed rather than taken on trust: it is five rows, not
six, and the legend framing was the weakest part of it — `_StepSelection.cshtml:8` is a badge glossary,
not an invariant, and it is already loose in the other direction at HEAD. The cheaper interim
(softening the legend) was **not** taken; it is incomplete on its own terms, since any legend rewrite
would have to fix both directions._

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
