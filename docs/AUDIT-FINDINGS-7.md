# Bastet — Round-7 Audit Findings

| Baseline | |
|---|---|
| Branch | `main` |
| HEAD | `6a1fe75` |
| Build | 0 warnings, 0 errors (`dotnet build --no-incremental`) |
| Tests | 643 passed, 0 failed, 0 skipped |
| Date | 2026-07-27 |

Finding ids use the letter **G** and are numbered sequentially across the whole file, grouped by
severity, ordered within each severity by consequence.

---

## Verdict

Thirteen findings survived verification: **1 high, 3 medium, 8 low, 1 info**. Seven candidates were
refuted; the refuted table below is the round's second-most-useful output and should be read before
anyone reports one of them again.

**Read these three first, in this order:**

1. **G1 — bulk import silently repoints an existing subnet's Azure link to a different VNet, and
   reconcile then archives the subtree on the strength of the wrong resource.** It is the only
   finding in this round that loses persisted data with no in-app recovery. It was reproduced end to
   end twice, independently, against live ARM. The two finders proposed *opposed* fixes; both
   verifiers picked the refusal, and one of them empirically demolished the objection to it. Read
   the corrected fix, not the finders' summary.
2. **G2 — F11's own fix throws a `ReferenceError` on the exact screen it exists for.** Round 6
   verified that fix by lifting the function out of the file, which is why a block-scope error
   survived. It renders a blank panel and, on re-entry, a permanently stuck wizard.
3. **G3 — one VNet deleted mid-scan makes the whole subscription report as unreadable**, disabling
   bulk import and reconcile for that request. Fail-closed, self-healing, but it fires on precisely
   the event reconcile exists to detect.

The shape of the round: **the round-6 fixes themselves are where the defects are.** Three of the
thirteen (G1's neighbourhood aside) are residuals of F9, F11 and F13-era work — the half of a fix
that was not verified in the shipped file. Two more (G4, G13) are configuration and packaging that
never worked as documented. The low band is dominated by client-side state that is wrong on screen
but harmless to the database; none of it corrupts data, and G5/G6/G7 are the only low findings with
a security or availability edge.

Nothing critical was found. The reconcile commit path's fail-closed behaviour, exercised again by
G1 and G3, held every time it was attacked — in G1 the loss comes from the link being wrong *before*
reconcile runs, not from reconcile being permissive.

---

## How this audit ran

**Eight beats**, each run as **two independent passes** by separate finders with no shared context:
security / web surface, logic and data integrity, Azure integration, locking and lifecycle, UI and
client-side JavaScript, regression correctness over the round-6 delta, regression *tests*, and dead
code / refactor residue. Four beats additionally got a **deep sweep** against the live rig:
security, logic, azure, regression.

**20 finders launched, 20 returned, 31 raw findings, merged to 20 candidates.** 13 survived, 7 were
refuted.

**Verification.** One adversarial verifier per candidate, prompted to refute and to **default to
not-real**. Every `[x1]` candidate additionally got a **second verifier on a reachability-and-
consequence lens** — walking the defect from the HTTP entry point inward rather than outward from
the cited line. **Either verifier refuting kills the candidate.**

**The Azure rig was live**, not mocked: two service principals with disjoint RBAC over two resource
groups in a real subscription, with discrimination between them verified before any finding was
accepted. Findings G1, G3 and parts of G4/G7/G13 were driven against real ARM with real VNets
created and deleted by the verifier.

### What `[x2]` and `[x1]` mean

- **`[x2]`** — both independent passes of the beat found it.
- **`[x1]`** — only one pass found it.

**`[x1]` warrants MORE scrutiny during reconciliation, not less.** It means one of two competent
passes walked the same code and missed it. Absence is weak evidence: in this round every `[x1]`
survivor was reproduced against a running application, and two of them (G1, G3) are among the three
most consequential findings in the file. Nine of the thirteen survivors are `[x1]`. Treat the tag
as "one pass missed this", never as "this is probably marginal".

### Reproduction discipline

**Every surviving finding was reproduced against a live rig.** Each finding below carries an
**Evidence** line naming what was run and what came back. Where a verifier corrected the finder —
severity, citation, scenario, or above all the **fix** — the correction wins and is what is
published here. Where a verifier judged the proposed fix unsound or incomplete, the corrected fix
is published together with one sentence saying why the obvious fix does not work; that sentence is
there to stop the reconciler reimplementing the broken fix from the finding's own text.

Nothing in this file was reported on inspection alone. No finding is marked not-runnable.

---

# High

## G1 — Bulk import silently repoints an existing subnet's Azure link to a different VNet `[x1]` — **FIXED**

*Both write sites now refuse to move a non-empty `AzureResourceId` to a different VNet, and the
planner refuses the selection before the operator ever reaches commit. `AnnotatePrefix` gained the
`BulkAzureVNetViewModel` parameter the finding said it lacked (threaded from `AnnotateAvailability`,
which already had it in scope) and returns `Blocked` naming **both** ARM ids; `BuildPlanItem` adds
the matching hard error, so preview reports `canCommit=false` instead of `errors=[]`. The commit
response and success banner gained a `linkedTargets` counter, closing the all-zeros report: linking
a previously unlinked subnet is a persisted change no other counter recorded.*

*Verified by regression tests written first and confirmed failing against the unfixed tree:
`ExactMatch_TargetLinkedToADifferentVNet_HardFails` failed on `Assert.False(plan.CanCommit)`,
`Availability_PrefixTargetLinkedToADifferentVNet_IsNotSelectable` failed on the status still being
`WillUpdateExisting`, and `BatchCreateChildSubnets_ParentLinkedToADifferentVNet_RefusesAndKeepsTheLink`
failed with `TempData["ErrorMessage"]` null — the silent success the finding describes. Three guard
tests (same VNet re-imported, unlinked target stamped, plain batch create) passed before and after,
so the refusal is not blanket. The wizard really does render the reason:
`_BulkScripts.cshtml:204` appends `reasonHtml(prefixInfo.reason)` HTML-escaped.*

*Three places the finding's fix was changed, all load-bearing:*

*1. **Identity compares `OrdinalIgnoreCase`, not `Ordinal`.** The finding says only "differs from the
recorded id". Taking `Ordinal` — which the shipped stamp test at `:150` uses — would refuse an import
whose id differs only in casing, and ARM path segments are case-insensitive. `AnnotateSubnet`
already used `OrdinalIgnoreCase` for exactly this comparison, so this aligns with the file's own
precedent. `Ordinal` is retained for the separate question of whether the stored string needs
rewriting.*

*2. **Both new checks are guarded on the selected id being non-empty.** The finding's wording would
have errored whenever a recorded id was present and the selection carried none — but a selection
with no resource id never stamps anything, so that would be a refusal with no defect behind it.*

*3. **The bulk write-site refusal is defence-in-depth, not the operator-facing one, and the record
should not pretend otherwise.** `BulkCreateFromAzurePlanCore` re-runs the planner and returns
`BadRequest` on `!plan.CanCommit` (`:64-75`) before reaching the write at `:152`, so once the planner
errors the write-site branch is unreachable through HTTP. It was still applied — that line is the
actual integrity boundary and the consequence of letting a relink through is unrecoverable — but the
message an operator sees comes from the planner. The **second** write site
(`SubnetController.Azure.cs`, single-VNet import) has no planner in front of it and its guard is
genuinely reachable; that is the one the controller test drives.*

***Deliberately not done: fix part 3, the per-item "replace the existing Azure link" opt-in.*** *The
finding offers "or, at minimum, make the refusal text state that recourse", and that is what was
taken — both refusals end with "If the VNet was renamed or moved, delete the Bastet subnet and import
it again." A real opt-in spans the selection DTO, the planner, the commit loop and two wizard views;
that is a feature, not the defect. The rename/move case therefore still has no in-app relink, which
remains true of the tree and is carried on the watch list.*

*Not re-driven against live ARM: the defect and fix are both above the SDK boundary — the repoint is
a string comparison on a stored column — and the finding already carries two independent live-ARM
reproductions. The Azure surfaces were exercised end to end in the closing sweep.*

*Tests 643 → 651 (+8: five planner, three controller).*

---

# Medium

## G2 — F11's fix throws a `ReferenceError` on the exact screen it exists for `[x2]` — **FIXED**

*`let suppressedPrefixes = 0;` moved out of the `$.each(vnets, …)` callback and into `renderVNetTree`'s
own scope, immediately after `const hideImported = …`. That is also the semantically correct scope:
the message says "in this subscription", so it must be a subscription-wide total rather than the last
VNet's. `visiblePrefixes` stays in the callback — it is genuinely per-VNet and drives the
card-skip at `:249`. The interim `try`/`catch` was **not** taken, as the finding directs: it is the
same size as the correct change and leaves the panel blank in exactly the case the block exists for.*

*Reproduced first, in a real browser, against the running application — not a lifted copy of the
function, which is how round 6 missed it. Chromium via Playwright loaded `/Azure/BulkImport` from a
live instance on port 5401 (catalog `bastet_rig7`, real SQL Server 2022 in Docker, SP_A against real
ARM), so the page carried the exact jQuery 4.0.0 and Bootstrap 5.3.8 that `_Layout.cshtml` pins.
Precondition built through the application's own `POST /Subnet/Create`: four blocker subnets
(`10.30.1.0/24`, `10.10.1.0/24`, `10.140.1.0/24`, `10.120.1.0/24`), after which `BulkGetVNets`
reported all four VNet prefixes `Blocked / isSelectable=False`.*

*Before the fix — step 2 rendered 4 cards and 9 "Cannot import" badges; one real click on
`#bulk-hide-imported` produced `pageerror: suppressedPrefixes is not defined`, with
`#bulk-vnet-tree` children = 0 and `innerHTML === ''` — a completely blank panel. The second-order
consequence reproduced too: after **Back to Subscription** → re-select, `#bulk-vnet-loading` visible =
True, `#bulk-vnet-selection` = False and `#bulk-hide-imported` itself not visible — the permanently
stuck "Loading VNets…" spinner, with a second `ReferenceError` logged.*

*After the fix — same sequence, same instance: tree children = 1, text = "**4** VNet prefix(es) in
this subscription cannot be selected, and are hidden. Untick "Hide unavailable" to see why.",
`#bulk-vnet-selection` still visible, page errors **0**, and re-entry renders normally instead of
hanging. The count being 4 (all VNets) rather than the last VNet's 1 is the scope correction landing.*

*The unreachable `else` arm was dropped, and its unreachability was re-derived rather than taken on
the finding's word: `loadVNets` returns early to `#bulk-no-vnets` when `vnets.length === 0`
(`:137-141`), `GetVNetInventory` skips any VNet whose `Ipv4AddressPrefixes.Count == 0`, and
`AzureController.BulkGetVNets` always calls `AnnotateAvailability` on its success path — so every
VNet reaching the loop has at least one prefix, and an empty tree can only mean suppression. A
comment now records that chain in place of the dead branch.*

*No permanent test ships: there is still no JS test harness, which the watch list already carries.
Tests 651 → 651, build 0 warnings.*

---

## G3 — One VNet deleted during a scan makes `GetVNetInventory` report the whole subscription unreadable `[x1]` — **FIXED**

*Took the primary fix, not the interim. `GetVNetInventory` now iterates `vnet.Data.Subnets`
(`SubnetData`) from the listing response instead of issuing a separate `vnet.GetSubnets()` call per
VNet, and `ExtractIpv4Prefix`/`ExtractIpv4Prefixes` were retyped from `SubnetResource` to
`SubnetData` — both are private and, as the finding said, called only from this method, so no
overload was needed and no caller broke. This removes the 1+N round-trips outright, and with them
the 404 window, the throttling surface and the failure mode. The interim narrowed catch was **not**
applied and is not needed: with no inner per-VNet call there is nothing left to catch, and the outer
`catch` now only sees a genuine failure of the subscription listing itself, which is correctly
reported as one.*

*The fix's load-bearing claim was re-measured against live ARM rather than taken from the finding.
A standalone console app on the exact pinned SDK (Azure.ResourceManager.Network 1.16.1,
Azure.ResourceManager 1.14.0) walked every VNet in the subscription and compared the list-response
`SubnetData` against the separately-fetched `SubnetResource` field by field:
**8 subnets compared, 0 mismatches** — name, `Id` (compared `Ordinal`, so byte-identical, same
casing), `AddressPrefix` and `AddressPrefixes` all agreed. That included both multi-prefix fixtures,
`multi` (2 prefixes) and `g2multi` (3), where the singular `AddressPrefix` is empty and only
`AddressPrefixes` carries data — the case that would have lost information had the list response been
a summary. No information is lost.*

*Defect reproduced first, against live ARM, on the first attempt. Fixture `rig-g3-race`
(10.150.0.0/16, uksouth) created in RG `bastet`; `GET /Azure/BulkGetVNets` fired, the VNet deleted at
t+0.4 s. Baseline immediately before returned `success=True` with 5 VNets including the fixture; the
in-flight request returned
`{"success":false,"error":"Azure could not be read for this subscription. Details have been logged."}`
with `Azure.RequestFailedException: Resource …/RIG-G3-RACE not found. Status: 404` in the log, and the
very next request self-healed.*

*After the fix, the identical race on a second fixture `rig-g3-race2` (10.151.0.0/16, uksouth)
returned `success=True` with the full inventory — three consecutive scans around the delete, all
successful. The scan taken mid-delete still lists the VNet, which is correct: the listing is a
point-in-time snapshot, and reconcile's `ConfirmProposedDeletionsAsync` direct-read guard is what
decides deletion. Both consumers verified healthy afterwards on the same instance:
`BulkGetVNets` `success=True`, and `POST /Azure/ReconcileScan` (form-bound, with a real antiforgery
token) `success=True, scanSucceeded=True, globalErrors=[]`. Zero `fail:`/`warn:` lines in the log.*

*One scope correction the finding does not make: there is a **second** `GetSubnets()` call at
`AzureService.cs:170`, in `GetCompatibleSubnets`. It was deliberately left alone. That one is a
targeted read of a single VNet the caller named, so a 404 there is a genuine failure of the thing
asked for, not a 1+N amplification across a subscription — and it does not use the retyped
extractors, so the change cannot reach it.*

*No permanent test ships: `AzureServiceTests` drives `MockAzureService` only, and the real
`GetVNetInventory` needs an `ArmClient`, so the SQLite suite cannot reach this path. Verification is
the live-ARM measurement above. Tests 651 → 651, build 0 warnings.*

---

## G4 — `BASTET_LOG_LEVEL_DEFAULT` does nothing, and `BASTET_LOG_LEVEL_ENTITYFRAMEWORK` cannot silence the SQL `[x1]` — **FIXED**

*The whole `Logging` section moved out of `src/Bastet/appsettings.json` into
`src/Bastet/appsettings.Development.json`, where the only key genuinely needing to be added was
`"Microsoft.EntityFrameworkCore.Database.Command": "Information"` — Development previously inherited
it from the base file. With no configuration-derived rules in Production, `SetMinimumLevel` becomes
the matching-rule fallback again and the code's `Microsoft.EntityFrameworkCore` filter becomes the
longest match for `…Database.Command`. The interim `AddFilter((string?)null, level)` was **not**
taken, as the finding directs: it would win the null-category tie against `Logging__LogLevel__Default`
and convert the operator's one working escape hatch into a second inert knob.*

*Reproduced against the shipped DLL in Production before touching anything, on a warm boot,
categories counted with `grep -oE '^[a-z]+: [A-Za-z.]+' | sort | uniq -c`:*

| Configuration | Before | After |
|---|---|---|
| all three knobs = `None` | 15 `info: …Database.Command` + 5 `info: Microsoft.Hosting.Lifetime` | **0 lines total** |
| all unset | same 15 + 5 | 3 `fail:` + 1 `warn:` only |
| `ENTITYFRAMEWORK=Information` | (SQL printed regardless) | 15 `Database.Command` **+ 4 `Migrations`** |
| `DEFAULT=Debug` | 0 `dbug:` outside `Microsoft.AspNetCore.*` | **3** `dbug: Microsoft.Extensions.Hosting.Internal.Host` |

*So the knob is now live in both directions, which is the defect: it was inert going down (`None`
still printed every SQL statement) and inert going up (`Debug` produced nothing new). A first
migrating boot showed 42 `Database.Command` lines under `None`, confirming the finding's note that the
count is boot-dependent and should be stated qualitatively rather than as a fixed number.*

*Development verified behaviour-neutral by running it: 43 `info: …Database.Command` and 8
`…Migrations` lines still appear, i.e. the explicit key reproduces exactly what the inherited one
did. The three `fail:` lines present in every Production run are **expected and not a defect** —
they are OIDC discovery failing against the default `https://localhost` authority, because the rig
configures no IdP; the single `warn:` is the HTTPS redirection notice on a plain-HTTP rig.*

*One consequence accepted deliberately, and the finding's optional mitigation declined.* ***The
`Microsoft.Hosting.Lifetime` "Now listening on…" / "Application started" lines no longer appear at
the default level in Production*** *— visible above as `post_unset` having no lifetime lines. The
finding offers `AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information)` to keep them
unconditionally; that was **rejected**, because an unconditional filter would print those five lines
even when the operator sets `BASTET_LOG_LEVEL_DEFAULT=None` — reintroducing in miniature the exact
"the knob is a lie" defect being fixed. `Warning` is what `README.md` has always promised as the
default, and an operator who wants startup lines sets `BASTET_LOG_LEVEL_DEFAULT=Information`, which
now actually works. This is a visible behaviour change on upgrade and belongs in the release notes.*

*`README.md`'s three logging rows rewrote the note that was false: they no longer claim development
"falls back to appsettings.json" for a section that is no longer there, they name
`appsettings.Development.json` instead, they record that the standard `Logging__LogLevel__*`
variables outrank every `BASTET_LOG_LEVEL_*` knob, and the EntityFramework row now says it covers the
whole `Microsoft.EntityFrameworkCore.*` namespace including the `Database.Command` category that
prints SQL.*

*The finding's two other corrections are carried and were not re-litigated: there is no
`EnableSensitiveDataLogging` anywhere in the tree, so this is a log-volume and troubleshooting
defect, not information disclosure; and `Logging__LogLevel__*` remains the higher-precedence
override after this change, which is why the README now says so.*

*No permanent test ships: log-provider rule selection is decided by host configuration at boot and
the SQLite suite has no host. Verification is the measured table above. Tests 651 → 651, build
0 warnings.*

---

# Low

## G5 — A request waiting for the subnet lock already holds a pooled SQL connection `[x2]` — **FIXED**

*Took the primary fix: a process-local `SemaphoreSlim(1, 1)` gate in front of the database lock, so
at most one request per replica is ever parked inside `sp_getapplock`. The gate is waited on
**before** `OpenConnectionAsync()`, released in an outer `finally`, and — this is the part that keeps
the existing contention contract honest — the time spent waiting on it is subtracted from the
caller's budget and the remainder passed as `sp_getapplock`'s `@LockTimeout`, so a contended caller
still fails in ~30 s rather than 60. The no-static-state alternative (`@LockTimeout=0` plus a backoff
loop with `CloseConnectionAsync()` between attempts) was not taken: it re-acquires a connection per
attempt and turns one wait into a poll, for the same effect.*

*The interim fix was **not** taken. Setting `Max Pool Size` higher or lowering `DEFAULT_TIMEOUT_MS`
moves the threshold rather than removing the amplification, as the finding says. The watch-list note
advising operators to set `Max Pool Size` explicitly still stands on its own merits.*

*Reproduced against real SQL Server 2022 (Docker, port 11433) before any change, with the finding's
own method: an outside `sqlcmd` session held `Bastet:SubnetOperations`
(`sp_getapplock` Exclusive/Session + `WAITFOR`), then 110 concurrent authenticated
`POST /Subnet/Create` — each with its **own** session and antiforgery token, which matters: pairing
one session's token with another's cookie jar is rejected at 400 before any lock is taken, and the
first attempt at this rig measured nothing because of exactly that.*

| Measurement | Before | After |
|---|---|---|
| `GET /Subnet` (read-only, takes no lock) | **HTTP 500 after 15.01 s** | **HTTP 200 in 0.068 s** |
| `max pool size was reached` entries in the log | **13** | **0** |
| stacks naming `ExecuteWithSubnetLockAsync` | 10 | — |
| stacks naming `SubnetController.Index` | 7 | — |
| writer completion | 30.02–30.65 s | 30.02–30.08 s (**not** 60 s) |

*Control, to prove it is queued waiters and not write load or HTTP concurrency: the same 110
concurrent `POST /Subnet/Create` with **no** external lock holder completed in ≤1.26 s each and the
reader returned **200 in 0.051 s**.*

*Two behaviours re-checked after the fix on a clean lock state: an uncontended write still returns
**302**, and a contended write returns after 30 s rendering the modelled
*"The operation timed out due to high concurrency. Please try again."* rather than the generic
*"Error creating subnet. Details have been logged."* That is the finding's own correction landing —
before the fix, writers past the hundredth threw `InvalidOperationException` (pool exhaustion), which
is not a `TimeoutException` and so bypassed the `catch (TimeoutException)` the contention message
lives behind.*

*The no-self-deadlock precondition was verified rather than assumed, because the gate makes nesting
fatal where `sp_getapplock` with `Session` owner would previously have been re-entrant: all ten
`ExecuteWithSubnetLockAsync` call sites are top-level controller actions, and `DeleteConfirmed`,
`BatchCreateChildSubnets`, `BulkCreateFromAzurePlan` and `BulkDeleteStaleAzureSubnets` have **no**
internal callers anywhere in `src/` — every guarded action is reachable only from HTTP routing.*

*One rig artefact worth recording so it is not mistaken for a defect next round: a leftover
`docker exec sqlcmd` holder keeps its session — and therefore its APPLICATION lock — alive after the
driving process is killed, which made a follow-up write time out and read as a regression.
`sys.dm_tran_locks` returned to 0 `APPLICATION` rows as soon as the container's stray `sqlcmd`
processes were killed. Kill holders inside the container, not just the client.*

*No permanent test ships: the suite runs SQLite, `SqlServerSubnetLockingService` has no test
reference anywhere in `test/`, and `SubnetLockTimeoutTests` fakes `ISubnetLockingService` outright —
so the pool behaviour is unreachable from it. The controller-side contract that a `TimeoutException`
renders the friendly message is already pinned there. Tests 651 → 651, build 0 warnings.*

---

## G6 — Authenticated listing pages ship with no cache directives at all `[x1]` — **FIXED**

*Took the global filter, not the four-attribute interim. `Program.cs`'s
`AddControllersWithViews` lambda now registers
`new ResponseCacheAttribute { NoStore = true, Location = ResponseCacheLocation.None }` beside the
existing `GlobalSanitizationFilter`. `ResponseCacheAttribute` is an `IFilterFactory`, so
`FilterCollection` takes the instance directly. The interim was rejected on the finding's own
grounds: it leaves `/`, `/Account/Roles`, `/HostIp/Index` and `/HostIp/DeletedHostIps` uncovered, and
the defect is precisely that coverage was per-page by accident.*

*Reproduced before the change. Headers on the running instance — the six authenticated listing pages
`/`, `/Subnet`, `/HostIp/AllHostIps`, `/Account/Roles`, `/Subnet/DeletedSubnets` and
`/HostIp/AllDeletedHostIps` returned 200 with **no** `Cache-Control` and **no** `Pragma`, while
`/Subnet/Create`, `/Subnet/Details/1` and `/Subnet/Edit/1` returned `no-cache, no-store` +
`Pragma: no-cache`. That is the finding's central point measured directly: the protection tracked
"does this view render an antiforgery token", not "is this page authenticated".*

*Consequence proven in a real browser with a staleness differential rather than a shutdown, so the
result cannot be confused with a connection error. A marker subnet was created through the
application's own `POST /Subnet/Create`, `/Subnet` loaded in Chromium, the subnet renamed
server-side (confirmed over HTTP), then Back:*

| Page | Before — Back shows | After — Back shows |
|---|---|---|
| `/Subnet` (listing) | **stale** name; fresh name absent | **fresh** name; stale absent |
| `/Subnet/Details/<id>` (no-store control) | fresh name; stale absent | fresh name; stale absent |

*So before the fix the listing served a cached document while the no-store page refetched; after it,
the listing behaves like the control. The eight network requests counted in every run are the pinned
CDN stylesheet and scripts, not the document.*

*Headers re-measured after the fix: `/`, `/Subnet`, `/HostIp/AllHostIps`, `/Account/Roles`,
`/Subnet/DeletedSubnets`, `/HostIp/AllDeletedHostIps`, `/Subnet/Create`, `/Subnet/Details/1` and
`/Error/404` all return `Cache-Control: no-store,no-cache`, while `/css/site.css` and `/js/site.js`
return **none** and stay cacheable — confirming the filter reaches controller responses only.
`grep` for `FileResult`/`return File(` over `src/` finds nothing, so there is no binary response the
filter would wrongly mark. The directives name no scheme, so plain-HTTP and air-gapped hosting are
unaffected.*

*The finding's two-mechanism correction is carried and matters for why this fix is the right one:
the bfcache path would not be closed by a header, but the HTTP disk-cache path is, and that is the
one that survives the browser process and leaks across a sign-out. This change closes the disk-cache
path; it does not claim to close bfcache.*

*No permanent test ships: response headers are set by the MVC filter pipeline in a running host, and
the suite has no `WebApplicationFactory` — already on the watch list. Verification is the header
table and the browser differential above. Tests 651 → 651, build 0 warnings.*

---

## G7 — The OIDC handler requests and stores a refresh token that no code ever reads `[x1]` — **FIXED**

*All three parts applied. `options.Scope.Add("offline_access")` deleted; `SaveTokens = true`
**kept**, because the saved id_token is what supplies `id_token_hint` on the end-session request in
`AccountController.Logout`; the `offline_access` bullet removed from `README.md`'s list of scopes the
operator's IdP must grant; and the `Events.OnTicketReceived` handler added, re-storing only the
id_token.*

*The handler was included rather than treated as optional, because the finding measured that
`SaveTokens` has no scope gate: on a provider that returns a refresh token without being asked
(Keycloak's standard flow), dropping the scope does not stop the token reaching the cookie. With the
handler the scope deletion stops being a best effort.*

*Reproduced end to end against a purpose-built spec-conformant IdP — discovery, JWKS, authorize,
token, userinfo and end-session, RS256-signed id_tokens, served over HTTPS behind a locally
generated CA — with the shipped `Bastet.dll` running in **Production** as a **public client**
(`BASTET_OIDC_CLIENT_ID` set, **no** `BASTET_OIDC_CLIENT_SECRET`), the configuration `README.md`
explicitly supports.*

| Measurement | Before | After |
|---|---|---|
| authorize `scope` | `openid profile email roles offline_access` | `openid profile email roles` |
| auth cookie bytes (IdP returns refresh token) | **3266** | **2498** |
| auth cookie bytes (control: IdP omits it) | 2818 | — |
| `id_token_hint` on end-session | present, 833 chars | **present, 833 chars** |
| signed-in `GET /` | 200 | 200 |

*The control isolates the refresh token's own cost: 3266 − 2818 = **448 bytes** on every request
from every signed-in user, for a value nothing reads. The after figure is lower than the control
too, because the handler also drops the equally unused access_token.*

***The strongest measurement is the last row of the before/after: the IdP was still returning an
unsolicited refresh token when the "after" run was taken, and the cookie did not grow.*** *That is
the unsolicited-token case the finding could only describe, exercised directly — the handler discards
it. And `id_token_hint` is byte-for-byte the same length after the change, so keeping `SaveTokens`
was the right call and logout is intact.*

*Two corrections to the finding's own fix text, both found by applying it:*

*1. **The suggested snippet does not compile in this tree.* `StoreTokens`/`GetTokens` are extension
methods on `Microsoft.AspNetCore.Authentication`, which `Program.cs` did not import — it had only the
`.Cookies` and `.OpenIdConnect` child namespaces. A `using Microsoft.AspNetCore.Authentication;` was
added.*

*2. **The suggested snippet also breaks the 0-warning baseline.**
`ctx.Properties!.StoreTokens(ctx.Properties.GetTokens()…)` null-forgives only the *first*
`ctx.Properties`; the second is still nullable and raises `CS8604`. Rewritten to bind
`AuthenticationProperties? properties` once and use `properties?.StoreTokens(…)`.*

*Two rig notes, neither a product defect. The OIDC correlation and auth cookies are issued `Secure`
(`CookieSecurePolicy.Always`), so a conformant client refuses to return them over the rig's plain
HTTP and the callback fails "Correlation failed" — the client clears the flag locally, since the
transport is not what is under test. And the CA must carry `basicConstraints`/`keyUsage` or .NET
rejects the chain with "CA cert does not include key usage extension".*

*No permanent test ships: there is no `WebApplicationFactory` and no way to drive an OIDC handshake
from the SQLite suite — already on the watch list. Verification is the table above.
Tests 651 → 651, build 0 warnings.*

---

## G8 — `/Account/Logout` returns HTTP 500 in Development: `SignOutAsync` is called for a scheme that is not registered there `[x2]`

**File:** `src/Bastet/Controllers/AccountController.cs:45`
**Also:** `src/Bastet/Program.cs:163`, `:171`, `AccountController.cs:58`,
`test/Bastet.Tests/Security/AccountControllerLogoutTests.cs:28`
**Confidence:** confirmed

### Scenario

Development is the only environment where `Program.cs:160-172` registers a single scheme,
`DevAuthScheme`, and `DevAuthHandler` is **not** an `IAuthenticationSignOutHandler`. `Logout` deletes
every request cookie at `:39-42`, then calls
`HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)` at `:45`
**unconditionally**. Scheme `"Cookies"` has no sign-out handler at all, so it throws. The
Development branch the author wrote at `:48-58` (*"In development, just redirect to the specified URL
or the signed-out page"*) is unreachable.

**The wrong output is not merely a log entry.** Development registers `UseDeveloperExceptionPage`
(`Program.cs:408-416`), so the full framework stack trace **including the absolute source path** is
the HTTP response body. The operator reaches this by clicking Logout in the nav dropdown
(`_Layout.cshtml:77`; also `Views/Account/AccessDenied.cshtml:14`) — not by crafting a URL.

The suite is blind to it: `AccountControllerLogoutTests.CreateController` injects a
`Mock<IAuthenticationService>` (`:29-32`), so `SignOutAsync` is a no-op there and two tests —
`Logout_Development_LocalReturnUrl_IsPreserved` and
`Logout_Development_NonLocalReturnUrl_RedirectsToSignedOutPage` — assert a `RedirectResult` the
running application **cannot produce**.

This is a distinct defect from the accepted Development-only `DevAuthHandler` bypass: that item is
about the handler granting all roles; this is a sign-out call for a scheme that was never registered.

**Corrected by the verifier:** the finder's claim that *"the whole SignedOut view is unreachable in
Development"* is true but over-attributed. Even with `:45` guarded, Development's
`Redirect("/Account/SignedOut")` lands on `SignedOut()`, which sees
`User.Identity.IsAuthenticated == true` — `DevAuthHandler` re-authenticates every request — and 302s
to Home (measured: `GET /Account/SignedOut` → 302 → `/`). **Fixing this turns a 500 into a redirect
to the home page, not into the signed-out page.** The view stays unreachable in Development because
of the accepted `DevAuthHandler` bypass; do not chase that here.

### Evidence — reproduced: yes, response body captured, and the suite's blindness confirmed

Verifier ran `rig-app.sh start verify:logout-500-development A` → pid 423486 on
`http://127.0.0.1:5239` (`GET /` → 200). Three requests:

```
/Account/Logout                                          -> HTTP 500 redirect=[]
/Account/Logout?returnUrl=%2FSubnet                      -> HTTP 500 redirect=[]
/Account/Logout?returnUrl=https%3A%2F%2Fevil.example.com -> HTTP 500 redirect=[]
```

The **body**, not just the log, is the raw developer exception page; its first bytes:

```
System.InvalidOperationException: No sign-out authentication handlers are registered. Did you forget
to call AddAuthentication().AddCookie("Cookies",...)?
   at Microsoft.AspNetCore.Authentication.AuthenticationService.SignOutAsync(...)
   at Bastet.Controllers.AccountController.Logout(String returnUrl) in
      /home/anuj/code/Bastet/src/Bastet/Controllers/AccountController.cs:line 45
```

`app.log` carries the same frame three times. No `RedirectResult`, no `Location` header, on any
input. In the same session:
`dotnet test … --filter-query "/*/*/AccountControllerLogoutTests/*"` → **9 total, 9 succeeded**,
including the two tests that assert the redirect the live app cannot produce.

### Fix

Call `HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)` only when
`!environment.IsDevelopment()`, leaving the cookie-clearing loop at `:39-42` where it is. That makes
the existing Development branch reachable and matches the intent already written in the comments.
Production/Staging are byte-for-byte unchanged because `Program.cs:160` keys the auth registration on
the same `IsDevelopment()` test — there is no third environment shape where the guard and the
registration disagree. Nothing touches HTTPS, OIDC or the host model.

**The verifier notes a strictly simpler variant:** delete line 45 outright. The `SignOutResult` at
`:51-54` already lists `CookieAuthenticationDefaults.AuthenticationScheme`, so the guarded version
leaves the cookie sign-out happening twice in Production (idempotent, harmless, redundant). Prefer
the deletion unless the explicit pre-sign-out is wanted for readability.

**The test change must be asymmetric.** Supply a throwing (or real) `IAuthenticationService` only to
the two Development tests; keep the permissive `Mock<IAuthenticationService>` for the five Production
cases (the four-row theory at `:50-55` plus `Logout_Production_LocalReturnUrl_IsPreserved` at `:66`),
which legitimately need `SignOutAsync` to be callable. Replacing the mock in `CreateController`
wholesale would fail the Production cases for the wrong reason.

### Interim fix

Wrap the `:45` call in `try/catch (InvalidOperationException)` and continue — the cookies are already
deleted, so the redirect at `:58` still ends the visible session.

---

## G9 — F9's own contract is still violable: the parent name is copied into the Create prefill unchecked, so any parent whose name carries a `[SafeText]`-forbidden character reproduces F9 exactly `[x1]`

**File:** `src/Bastet/Controllers/SubnetController.Create.cs:72`
**Also:** `src/Bastet/Services/Security/InputSanitizationService.cs:14`,
`src/Bastet/Models/ViewModels/EditSubnetViewModel.cs`
**Confidence:** confirmed

### Scenario

F9 changed the generated suffix from `-{networkAddress}/{cidr}` to `-{networkAddress}-{cidr}` because
`[SafeText]` on `CreateSubnetViewModel.Name` forbids `/`. The **other half** of the same string —
`parentSubnet.Name`, passed straight into `SubnetNaming.WithSuffix` at `:72-73` — is never checked
against that rule. `SafeTextPattern` is `^[a-zA-Z0-9\s\-_.,!?@#$%&()+=]*$`, so `/`, `'`, `:`, `;`,
`*`, `"`, `[`, `]`, `|`, `~`, `^` and backtick all fail it. Round 5's E2 deliberately did **not** put
`[SafeText]` on `EditSubnetViewModel.Name` (only `[NoHtml]` + `[SanitizeName]`, and `SanitizeName`
only trims/truncates/strips markup), so those characters are reachable in a stored subnet name
through the ordinary Edit form.

Rename `10.0.0.0/16` to `Prod/Web` (accepted, 302). Click Create Subnet on an unallocated range —
`GET /Subnet/Create?networkAddress=10.0.0.0&cidr=17&parentId=1` prefills
`value="Prod/Web-10.0.0.0-17"`. POST that exact prefilled value back → HTTP 200 with
`data-valmsg-for="Name"` reading **"Subnet name contains invalid characters"** — F9's own failure
text, on F9's own flow, with F9's fix in place. `Bob's Lab` and `DC1:Core` do the same.

**Corrected scope, from the reachability verifier:** the precondition is narrower than "any parent
with a forbidden character" implies. E2's paragraph justifies omitting `[SafeText]` from Edit by
pointing at Azure import, but Azure VNet/subnet resource names are themselves restricted to
alphanumerics, `.`, `-` and `_` — all inside `SafeTextPattern` — so the **import path cannot in
practice produce an out-of-class stored name**. The operator must first rename through
`/Subnet/Edit`. That is an ordinary action with ordinary names, so it is reachable, but it is a
two-step precondition rather than a passive state.

**Consequence is UX friction only**, and the finding should not be read as more: nothing is persisted
(`/Subnet/Index` still lists exactly one subnet after the rejected POST), nothing is corrupted, and
the operator can fix it by editing the one field. This is precisely F9's consequence — an error
message on the one field the operator did not type — which is why low is right and why it should not
be argued up.

### Evidence — reproduced: yes, twice, on the flow the Details page's own button drives

Verifier 1 (port 5297, catalog `bastet_vf9parent`, unmodified binaries, curl with a cookie jar and
real antiforgery tokens): `POST /Subnet/Create` `Name=Prod, 10.0.0.0/16` → 302 `/Subnet/Details/1`;
`POST /Subnet/Edit/1` `Name=Prod/Web` → **302**, and `GET /Subnet/Details/1` renders `<h1>Prod/Web</h1>`
so the slash really is persisted; `GET /Subnet/Create?networkAddress=10.0.0.0&cidr=17&parentId=1` →
`id="Name" maxlength="100" name="Name" value="Prod/Web-10.0.0.0-17"`; `POST /Subnet/Create` with
exactly that value → **HTTP 200**, no redirect, `field-validation-error … data-valmsg-for="Name" …>
Subnet name contains invalid characters`. Every other field's validation span was empty.

Verifier 2 (port 5262, catalog `bastet_f9reach2`) reproduced the same chain **and** the finding's
second claimed input: renamed to `Bob's Lab` → 302; prefill came back
`value="Bob&#x27;s Lab-10.0.0.0-17"`; POSTing it back → 200 with the same message. It confirmed
`GET /Subnet/Index` afterwards still shows exactly one subnet — nothing created, nothing corrupted.

Both verifiers confirmed the flow is the Details page's own button
(`_SubnetCalculationScripts.cshtml:151` navigates to
`/Subnet/Create?networkAddress=…&cidr=…&parentId=…`), that `SubnetNaming.WithSuffix` has only one
other caller (`AzureBulkImportPlanner.cs:744`, untouched by the fix), and that
`SubnetCreateGetPrefillTests.cs:119-133` pins only the suffix — its theory rows use a fixture parent
literally named `Parent`, so both current rows pass vacuously with respect to this defect.

### Fix

In the `parentSubnet != null` block, **filter the parent name to the `SafeTextPattern` character
class** before calling `SubnetNaming.WithSuffix`, falling back to a bare `{networkAddress}-{cidr}`
when nothing usable survives.

Three refinements from the verifiers:

- **Prefer the filter over the alternative** the finder also offered (resolve
  `IInputSanitizationService` and test `IsSafeText` on the composed value, else fall back). The
  `IsSafeText` variant discards the **whole** parent name for one bad character — a worse default
  than `ProdWeb-10.0.0.0-17` — and costs an 8th parameter on the primary constructor in
  `SubnetController.cs` shared by eight partials.
- **Trim the filtered parent name** before composing: `SafeTextPattern` admits `\s`, so a parent
  named `"/ / /"` filters to `" "` and composes `"  -10.0.0.0-17"` — valid but ugly, and the
  empty-fallback branch would not catch it.
- The filter is complete for the whole POST rule set: the class excludes `<` and `>` so `[NoHtml]` is
  satisfied; filtering only shortens, so D19/F9's length arithmetic in `WithSuffix` is untouched; and
  the fallback satisfies `[Required]` while avoiding a leading `-`.

Extend `SubnetCreateGetPrefillTests`' theory with a parent-name row (`Prod/Web`, `Bob's Lab`) that
exercises the `parentId` path, so the contract it claims to pin actually covers both components of
the string.

### Interim fix

Strip characters outside the SafeText class from the parent-name component only, leaving the suffix
and the length logic untouched — a one-expression change at `:72` with no effect on any name that is
already clean.

---

## G10 — Re-entering the Azure import wizard's subnet step leaves "Select All Subnets" ticked over an empty selection and the Import button live `[x1]`

**File:** `src/Bastet/Views/Azure/Import/_ImportScripts.cshtml:244`
**Also:** `:110`, `:319`, `:83`, `:100`, `:297`;
`src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:271`
**Confidence:** confirmed

### Scenario

An admin on `/Azure/Import/{id}` reaches step 3, clicks **Select All Subnets** (all rows tick, Import
goes green), clicks **Back to VNets**, then immediately **Next: Select Subnets** with the same VNet
still chosen. Because the dropdown value did not change, no `change` fires on `#vnet-select`, so
`invalidateSubnetStep()` (`:83`, wired only at `:100`) never runs. `loadSubnets` succeeds and
`$list.empty()` (`:244`) replaces every row, all unticked — but **nothing** resets
`#select-all-subnets` (which lives *outside* `#subnet-list`, so `$list.empty()` cannot reach it) and
nothing calls `updateImportButton()` (`:319`).

The screen shows "Select All Subnets" **ticked** with **zero** rows selected and the green "Import
Selected Subnets" button **enabled**. The operator's natural repair — clicking Select All — reads
`$(this).is(":checked")` as `false` (`:111`), so `.prop("checked", false)` runs across the rows and
**still nothing is selected**; only the *second* click selects the list.

**No bad data reaches the server** — measured, not assumed: clicking Import in the stale state raises
`alert("Please select at least one subnet to import")`, `location` does not change, and the server
log contains **0** occurrences of `BatchCreateChildSubnets`. The wrong output is entirely on screen:
two controls reporting a selection state the list does not have, and a master checkbox that does the
opposite of its label on first use.

Also reproduced: switching to a *different* matching VNet does fire `invalidateSubnetStep()`, so the
button is correctly disabled — but the master checkbox is **still** left ticked over the new, empty
list.

### Evidence — reproduced: yes, in a real browser, twice, with the fix measured

Verifier 1 (own instance at `6a1fe75`, catalog
`bastet_verify-import-wizard-select-all-stale-on-reentry`, SP_A, headless Chromium 124 with jQuery
4.0.0 confirmed loaded, native `element.click()` over CDP). Fixture used **only read-only rig
resources**: a Bastet subnet `10.10.0.0/16` created through the app's own POST, making the existing
`vnet-visible` (10.10.0.0/16, subnets web/app/multi) the sole compatible VNet. **No Azure resource
was created or deleted.**

```
(0) fresh step 3        {rows:3, checked:0, selectAllTicked:false, importBtnDisabled:true}
(1) after Select All    {checked:3, selectAllTicked:true,  importBtnDisabled:false}
(2) after re-entry      {rows:3, checked:0, selectAllTicked:TRUE, importBtnDisabled:FALSE,
                         step3TabDisabled:false}   <-- the defect
(3) after ONE click     {checked:0, selectAllTicked:false, importBtnDisabled:true}
(4) after a SECOND click{checked:3, selectAllTicked:true,  importBtnDisabled:false}
```

`step3TabDisabled` staying `false` at (2) proves `invalidateSubnetStep()` never ran.

Verifier 2 reproduced independently (port 5291, catalog `bastet_selall_reach`, own Chrome container),
with the same five states, and additionally captured the harm test with `window.alert` hooked and a
submit listener installed: exactly one alert, `location.href` unchanged, 0 server-side
`BatchCreateChildSubnets`.

**Fix measured** by both: executing the two proposed statements at the same point in the sequence
(verifier 2 used a jQuery `ajaxComplete` hook on the `GetSubnets` call, i.e. after rows are appended)
gave `{checked:0, selectAllTicked:false, importBtnDisabled:true}`, and a **single** subsequent click
on Select All ticked all rows and enabled Import. The interim variant (uncheck only) left
`importBtnDisabled:FALSE` until the operator's first click, exactly as the finding concedes.

### Fix

In `loadSubnets`' success handler, reset the two things the row rebuild leaves behind:

- add `$("#select-all-subnets").prop("checked", false);` beside `$list.empty()` at `:244`
- call `updateImportButton();` after the rows are appended, just before
  `$("#subnet-selection").removeClass("d-none")` at `:297`

This is exactly the pair the reconcile wizard already runs after every rebuild —
`_ReconcileScripts.cshtml:271-272` does
`$("#rec-select-all").prop("checked", false); updateGoConfirmBtn();` — so it aligns the oldest wizard
with the pattern the two later ones set rather than inventing one.

Note for whoever applies it: this does **not** reintroduce the `.trigger("change")` that round 4's D1
deliberately rejected — that was a payload concern; this is display state.

Optional refinement: putting the uncheck in `loadSubnets`' `beforeSend` (beside the existing
`$("#subnet-selection").addClass("d-none")` at `:235`) additionally clears the tick on the
empty-result and error branches, which the `:244` placement leaves ticked (harmlessly, since the
checkbox is inside the hidden `#subnet-selection` on those branches). The `updateImportButton()` call
still belongs at `:297`.

### Interim fix

The single line `$("#select-all-subnets").prop("checked", false);` at `:244`. It fixes the lying
master checkbox, and because the operator's first click then genuinely selects everything, the
stale-enabled Import button becomes correct on that same click.

---

## G11 — The Details page's Create-Subnet modal reopens carrying the previous range's validation failure: a red field and "would overlap with existing subnets" under a CIDR that is valid and committable `[x1]`

**File:** `src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml:49`
**Also:** `src/Bastet/Views/Subnet/Details/_CidrInputModal.cshtml:25`,
`_SubnetCalculationScripts.cshtml:112`, `:135`
**Confidence:** confirmed

### Scenario

An Edit-role user on `/Subnet/Details/1` clicks **Create Subnet** on an unallocated range, mistypes
the CIDR (any value the handler rejects), and sees the field turn red with a specific reason. They
cancel, then click Create Subnet on a **different** unallocated range. The modal opens with a freshly
computed, entirely valid `/23` (510 usable addresses) and an **enabled** Create Subnet button, but
the CIDR field is **still red** and still carries *"This CIDR would create a subnet that overlaps
with existing subnets."* — a statement about a value that is no longer in the field. The screen
contradicts itself: the error says the subnet cannot be created, the button says it can.

The `.create-subnet-btn` click handler (`:23-60`) resets `#networkAddressDisplay`,
`#originalNetworkAddress`, `#parentId`, `#parentCidr`, `#recommendedCidr`, `#validCidrRange`,
`#cidrInput`'s min/max/value, the subnet size (`:52`) and the button's `disabled` state (`:59`), and
calls `makeNetworkAddressReadOnly()` at `:49` — but nothing clears `#cidrInput`'s
`is-invalid`/`is-valid` classes or the text the input handler wrote into `#cidrValidationFeedback`,
which is declared as an `.invalid-feedback` sibling at `_CidrInputModal.cshtml:25-27` and therefore
renders whenever the input carries `is-invalid`.

Same class as round 6's F14 (a stale explanation left standing under a value the script did not
produce), at a second site. F14's struck paragraph explicitly records that the `is-valid` class on
`#cidrInput` *"was accurate"* and was **not** its anchor, so this is a genuine residual, not a
re-raise.

**Corrections from the verifiers, both worth carrying:**

- **The stale state also survives reopening the *same* range**, so the two-ranges framing understates
  reachability — the handler simply never clears validation state on any open. The modal is not
  destroyed on close (Bootstrap's `data-bs-dismiss` only removes `.show`), which is why the classes
  and text persist; the cancel step is not what leaves them behind, the missing reset in the open
  path is.
- **The error is provably false, not merely stale**: the flagged value is genuinely committable.

### Evidence — reproduced: yes, in a real browser, twice, with a screenshot and a server-side proof

Both verifiers seeded parent `10.10.0.0/16` (id 1) and child `10.10.1.0/24` through the real
`POST /Subnet/Create`, giving two unallocated ranges, and drove `/Subnet/Details/1` over CDP with
native clicks and `Input.insertText`.

Verifier 1 (port 5287, catalog `bastet_verify_csmodal_stale`, own Chromium on CDP 9287):

```
1 open on 10.10.0.0 -> addr 10.10.0.0, cidr 24, size 254, isInvalid false, btnDisabled false
2 type "5"          -> isInvalid TRUE, "This CIDR would create a subnet that overlaps with
                       existing subnets.", size "Invalid - Would overlap", btnDisabled true
3 click Cancel      -> modal hidden, isInvalid still TRUE
4 open on 10.10.2.0 -> addr 10.10.2.0, cidr 23, size 510, btnDisabled FALSE,
                       isInvalid TRUE, same overlap text, computed display block, visible TRUE
```

A `Page.captureScreenshot` of state 4 shows the contradiction on screen: red-bordered CIDR field
containing `23`, red overlap text, *"Valid range: 17 - 32 (recommended: 23)"*, *"Resulting subnet
size: 510 IP addresses"*, and an enabled blue **Create Subnet** button.

**The flagged value is committable:** `POST /Subnet/Create` with `NetworkAddress=10.10.2.0, Cidr=23,
ParentSubnetId=1` → **302 → `/Subnet/Details/3`**. Verifier 2 (port 5249, catalog
`bastet_verify2_stalecidr`) reproduced the identical five states and the identical 302.

**Fix measured** by both, by binding the two proposed lines as an additional handler after the
shipped one: reopening after the invalid entry gave `class="form-control"`, default feedback text,
`feedbackVisible false`, `btnDisabled false`, and one click on Select-equivalent behaviour restored.
Both also checked the **green** direction (type a valid 24, cancel, reopen): the same two lines clear
the leftover `is-valid` too. No other stale element remains — `#subnetSizeDisplay` is already
overwritten by `updateSubnetSize()` at `:52` and `#networkAddressHelp` by `makeNetworkAddressReadOnly()`
at `:49`.

### Fix

In the `.create-subnet-btn` click handler, beside the existing `makeNetworkAddressReadOnly()` call at
line 49 — and **before** the modal is shown at `:55`, so there is no flash of the stale red state —
reset the CIDR field's validation state:

```javascript
$('#cidrInput').removeClass('is-invalid is-valid');
$('#cidrValidationFeedback').text('Please enter a valid CIDR value within the allowed range.');
```

Use the exact default string already at `_CidrInputModal.cshtml:26` and line 130 so the three copies
stay in agreement. Pure client-side DOM work, no network dependency; plain-HTTP and air-gapped
hosting unaffected.

**Worth folding in at the same time, but independent — do not let it gate the primary fix.** Lines
127-142 test `else if (wouldOverlap)` *before* the range test, so a CIDR that is out of range and
also overlaps is reported as an overlap: entering `5` under a `/16` parent (minimum `/17`) produced
the overlap message when the real objection is the range. Concretely: keep the
`if (cidrValue >= minCidr && cidrValue <= maxCidr && !wouldOverlap)` success branch, then
`else if (cidrValue < minCidr || cidrValue > maxCidr)` → *"Please enter a valid CIDR value within the
allowed range."* / `"Invalid"`, then `else` → the overlap message / `"Invalid - Would overlap"`. This
preserves today's handling of a cleared field (`parseInt` gives `NaN`, every comparison is false, and
it still lands on the generic `"Invalid"` branch).

### Interim fix

None cheaper; the fix is two lines.

---

## G12 — Three form fields carry a hand-written `id`, so their generated `<label for=…>` points at an element that does not exist `[x1]`

**File:** `src/Bastet/Views/Subnet/Create/_SubnetForm.cshtml:23`
**Also:** `_SubnetForm.cshtml:16`, `src/Bastet/Views/HostIp/Create/_HostIpForm.cshtml:19`
**Confidence:** confirmed

### Scenario

A user opens `/Subnet/Create` and clicks the text **"CIDR Notation"** or **"Network Address"** — the
standard way to put focus in a field. Nothing happens: the label's `for` names `Cidr`/`NetworkAddress`
and the only element on the page is `id="cidrInput"`/`id="networkAddressInput"`. Measured: focus
stays on `<BODY>`.

A screen-reader user reaches the same three inputs (Subnet Create's network address and CIDR, Host IP
Create's IP address) with no `for`/`id` pair, no wrapping label and no `aria-label`.

Client-side validation is unaffected — jquery-validation-unobtrusive keys on `name`, and
`asp-validation-for` emits `data-valmsg-for` by name — which is why this has survived: nothing
functional breaks, so nothing complains.

**Corrected by both verifiers — the accessible-name claim is right for two of the three, not all
three:**

- `#cidrInput` (role `spinbutton`) and `#ipAddressInput` (role `textbox`) compute an accessible name
  of **`''`** — genuinely nameless.
- `#networkAddressInput` computes **`'192.168.1.0'`** — the placeholder that
  `_SubnetFormScripts.cshtml:74` installs on load. A screen-reader user hears a value-shaped string
  instead of "Network Address": a *wrong* name, not an absent one.
- On `/HostIp/Create` the dead-click symptom is largely masked, because
  `HostIp/Create/_FormScripts.cshtml:19-21` focuses the IP field at `DOMContentLoaded`. The missing
  accessible name is the live consequence there.

### Evidence — reproduced: yes, in a real browser with the accessibility tree read, twice

Verifier 1 (port 5341, catalog `bastet_verify_labelfor`, headless Chrome over CDP with trusted
`Input.dispatchMouseEvent`): `/Subnet/Create` renders
`<label class="form-label" for="NetworkAddress">` + `<input … id="networkAddressInput" …
name="NetworkAddress">` and `<label … for="Cidr">` + `<input … id="cidrInput" … name="Cidr">`;
`grep -c 'id="Cidr"'` = **0**, `grep -c 'id="NetworkAddress"'` = **0**. `/HostIp/Create?subnetId=1`
renders `<label … for="IP">` + `<input … id="ipAddressInput" … name="IP">`; `grep -c 'id="IP"'` = 0.

DOM audit: `for="NetworkAddress"` / `for="Cidr"` / `for="IP"` all `idExists=false, control=null`,
while `for="Name"` / `"Tags"` / `"Description"` / `"ParentSubnetId"` are all `true`.

Trusted clicks on the label text: *"Subnet Name"* → `INPUT#Name`; *"Tags"* → `INPUT#Tags`;
*"Network Address"* → `BODY`; *"CIDR Notation"* → `BODY`; on Host IP Create, *"Host Name (Optional)"*
→ `INPUT#Name`, *"IP Address"* → `BODY`.

`Accessibility.getPartialAXTree`: `#cidrInput` role=spinbutton name=`''`; `#ipAddressInput`
role=textbox name=`''`; `#networkAddressInput` role=textbox name=`'192.168.1.0'` (namefrom:
placeholder); `#Name` name=`'Subnet Name'`; `#Tags` name=`'Tags'`.

Verifier 2 reproduced independently (port 5273, catalog `bastet_lblreach`, own Chrome container) with
identical results, and additionally enumerated the full id inventory of `/Subnet/Create`
(`cidrInput, cidrRangeText, Description, maxIpsDisplay, Name, networkAddressHelp,
networkAddressInput, ParentSubnetId, subnetMaskDisplay, Tags, usableIpsDisplay` — no `Cidr`, no
`NetworkAddress`).

**Both fixes measured in the live DOM.** Renaming the ids: `label[for=Cidr].control.name === "Cidr"`,
click focuses `INPUT#Cidr`, AX names become `'CIDR Notation'` / `'Network Address'`,
`querySelectorAll('[id="Cidr"]').length === 1` (no duplicate id introduced), and typing `30` into the
renamed field still produced `255.255.255.252 | 4 | 2` in the mask/max/usable displays. The interim
variant (leave the ids, repoint the labels): clicks land on `INPUT#cidrInput` /
`INPUT#networkAddressInput` with the correct AX names.

### Fix

Drop the hand-written `id` attributes and let the tag helper generate `id="NetworkAddress"`,
`id="Cidr"`, `id="IP"`, then update the three selectors that used them:

- `src/Bastet/Views/Subnet/Create/_SubnetFormScripts.cshtml`: `#cidrInput` → `#Cidr`,
  `#networkAddressInput` → `#NetworkAddress`
- `src/Bastet/Views/HostIp/Create/_FormScripts.cshtml`: `getElementById('ipAddressInput')` → `'IP'`

Safe to scope to those two script files: the other `#cidrInput` user,
`src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml`, drives a **different page** whose
input is the plain `<input id="cidrInput">` in `Subnet/Details/_CidrInputModal.cshtml`, and that
partial is not loaded by `Subnet/Create.cshtml`.

### Interim fix

Touch only the three view files: replace `<label asp-for="Cidr" class="form-label"></label>` with
`<label for="cidrInput" class="form-label">@Html.DisplayNameFor(m => m.Cidr)</label>`, and the same
for NetworkAddress/`networkAddressInput` and IP/`ipAddressInput`. No JavaScript changes, so nothing
can regress in the inline scripts.

---

# Info

## G13 — OpenAPI is registered, mapped and carried as two NuGet packages, and serves a document with zero paths `[x2]`

**File:** `src/Bastet/Program.cs:35`
**Also:** `Program.cs:411`, `src/Bastet/Bastet.csproj:14`, `:15`
**Confidence:** confirmed

### Scenario

Bastet is an MVC app with no API surface: `grep -rn 'ApiController|ApiExplorerSettings'` over `src/`
and `test/` returns nothing, so the generated document describes nothing. An operator or auditor who
opens `/openapi/v1.json` on a Development instance (the only environment where `Program.cs:411` maps
it) gets a **200 and an empty `paths` object** — a live endpoint that documents zero endpoints.
Meanwhile `AddOpenApi()` runs on every startup in **every** environment including production, and
`Microsoft.AspNetCore.OpenApi` 10.0.10 plus `Microsoft.OpenApi` 2.11.0 ship in every published output
and every container image and appear in every dependency/CVE scan, purely as `dotnet new webapp`
template residue. The cost is deployment and supply-chain surface, not a wrong answer to a user.

**The verifier's finding strengthens this one:** the residue is not inert. See the fix.

### Evidence — reproduced: yes, endpoint fetched, and both fixes built and run

Verifier (label `verify:deadcode`, port 5238, catalog `bastet_verify_deadcode`, SP_A, from the built
DLL):

```
curl -s -w 'status=%{http_code} size=%{size_download}' http://127.0.0.1:5238/openapi/v1.json
status=200 size=178
{"openapi":"3.1.1","info":{"title":"Bastet | v1","version":"1.0.0"},
 "servers":[{"url":"http://127.0.0.1:5238/"}],"paths": { }}
```

`/openapi/v1.yaml` → 404. `ls src/Bastet/bin/Debug/net10.0/ | grep -i openapi` →
`Microsoft.AspNetCore.OpenApi.dll` and `Microsoft.OpenApi.dll`, both in every published build.
`grep -rn -i 'openapi|swagger'` over the repo excluding `bin`/`obj`/`docs` returns exactly the five
cited lines and nothing else — no `using Microsoft.OpenApi`, no type reference anywhere.

**Full fix measured in a scratch copy of the tree** (nothing written into the repository): with
`Program.cs:33-35`, `:411` and both `PackageReference`s deleted — `dotnet build` → **0 warnings, 0
errors**; `dotnet test --no-build` → **643 passed / 0 failed** (the exact rig baseline); the patched
DLL served `GET /` → 200, `/Subnet/Index` → 200, `/Azure/Reconcile` → 200, `/openapi/v1.json` → 404.

### Fix

Delete `Program.cs:33-35` (the comment and `builder.Services.AddOpenApi();`), delete
`Program.cs:411` (`app.MapOpenApi();`), and delete **both** `PackageReference` lines at
`Bastet.csproj:14-15`. Nothing else in the tree references either package, so this cannot strand a
caller. If an OpenAPI document is wanted later, it needs `[ApiController]`/ApiExplorer metadata on
the JSON endpoints first — the registration on its own produces nothing.

### Interim fix — DELETED BY THE VERIFIER; do not apply it

The finder proposed dropping only `Microsoft.OpenApi` (`Bastet.csproj:15`), on the stated grounds
that it is transitive and *"removing the direct reference changes no restored assembly"*. **That is
factually false and the change is actively harmful.** `src/Bastet/obj/project.assets.json` shows
`Microsoft.AspNetCore.OpenApi/10.0.10` declares its dependency as `Microsoft.OpenApi >= 2.0.0`, not
2.11.0 — the direct `PackageReference` is what lifts the resolved version. The verifier built a
scratch copy with only line 15 removed: NuGet resolved `Microsoft.OpenApi` **down to 2.0.0**
(`deps.json` then reads `"Microsoft.OpenApi": "2.0.0"`, shipping `lib/net8.0/Microsoft.OpenApi.dll`
v2.0.0.0) and the build emitted four
`warning NU1903: Package 'Microsoft.OpenApi' 2.0.0 has a known high severity vulnerability,
GHSA-v5pm-xwqc-g5wc` — turning a 0-warning baseline into a 4-warning one and putting a vulnerable
assembly into every image.

**Either apply the whole fix or change nothing.** The hand-pinned 2.11.0 is not drift risk, it is the
CVE floor — which is itself the strongest argument for deleting the feature: the project carries a
hand-maintained security pin for a package no source file imports.

*Citation note:* the finder's evidence cites `bin/Debug/net10.0/Bastet.deps.json:105-107` as showing
the package is "satisfied only as a transitive dependency". `deps.json` records the **resolved**
version (2.11.0, which the direct reference forced); the real constraint (2.0.0) lives in
`src/Bastet/obj/project.assets.json`. The inference is right in substance; the cited file is the
wrong one for that specific claim.

---

# Refuted — reported by a finder, killed by the verifier

7 of 20 candidates were killed (35%). Recorded so round 8 does not spend agents rediscovering them.
Reasons are the verifiers' own, verbatim.

| Candidate | Severity claimed | Why it was killed |
|---|---|---|
| F10 gated the unallocated-range Create button on `/32` but left the **Add Child Subnet link** on the same page ungated (`Views/Subnet/Details/_ChildSubnets.cshtml:6`) `[x1]` | low | Reproduces exactly, but "Add Child Subnet" is a generic navigation link that is capacity-gated nowhere — it renders identically on a fully-covered `/30` with no unallocated space, where every child POST is likewise refused — so F10 left no impossible state behind, and the `/32`-only gate would not close the dead end anyway because the Create form's parent dropdown still offers the `/32` as a selectable parent. |
| The F15 hotfix's regression tests cannot catch the regression: **`Program.cs`'s catalog ordering is unpinned** (`Program.cs:266`) `[x1]` | low | HEAD is correct — `Program.cs:266` opens the configured catalog and `master` is reachable only inside the `SqlException 4060` catch — so the wrong output exists only in source the auditor mutated (I ran the mutation: 643 green), which is the test-coverage-observation shape rounds 4, 5 and 6 all refuted, most exactly round 6's "E4's five call-site orderings unpinned"; worse, the gap is not a discovery but a deliberate, documented one, stated verbatim in the shipped test file's own class comment (`MigrationLockConnectionStringTests.cs:6-13`: "these cover the catalog choice only … the ordering … lives in Program.cs and needs a real SQL Server to exercise, which the SQLite suite cannot reach"), and the finding's own interim fix is already in the tree at `Program.cs:243-247`, nineteen lines above the cited call site. |
| **Five fragments of the round-6 fix set are unpinned**: reverted together, the suite still reports 643/643 green (`SubnetController.BulkAzure.cs:189`) `[x1]` | info | Every fragment is correct at HEAD and the finding's own scenario only produces wrong output after the auditor reverts the source — the exact test-coverage-observation shape refuted seven times across rounds 4, 5 and 6 (three in round 6 alone, all watch-listed), and the surviving distinguishing claims are consequence-free: the F8 server half really is cheaply testable and `Edit.cs:202`'s `.AsNoTracking()` really is not load-bearing, but neither correction removes any defect from the shipping product. |
| **F2's producer is unpinned and the consumer's fallback makes producer loss silent** (`AzureService.cs:360`) `[x1]` | medium | Every mechanical claim reproduces — the producer at `AzureService.cs:360` is unpinned (deleting it leaves 643 green) and the reconciler's scalar fallback is dead in production — but HEAD is correct, proven live against real ARM: a Bastet row linked at the second prefix of the multi-prefix `multi` subnet yields `items=[]`, `warnings=[]`, `canCommit=false`, so the wrong output appears only after the auditor mutates the source, which is the exact test-coverage shape round 6 refuted twice (`IsAbsenceStatus`, E4's five orderings) and which the brief's watch list already carries as "watch-list items, not findings". |
| **F6's regression tests pin the `SanitizingConsoleFormatter` class but not that it is the installed console formatter** (`Program.cs:30`) `[x1]` | low | HEAD is correct and proven so live — the finding's own crafted request against an unmodified `6a1fe75` produced zero `(char)0x1B` bytes in the log, and the escape-on-terminal harm appears only after the auditor deletes `Program.cs:30-31` — making this the test-coverage-observation shape refuted three times in rounds 5 and 6, and one the F6 struck paragraph already records as a deliberately deferred wiring check; its proposed fix would not close the gap either, since a test that calls the extracted extension itself still passes when `Program.cs` stops calling it. |
| **`ExtractIpv4Prefix`'s doc comment was orphaned** onto `ExtractIpv4Prefixes` by `0de1293`, leaving two stacked `<summary>` elements on one method and none on the other (`AzureService.cs:380`) `[x1]` | info | Every mechanical fact verified — the orphaned summary really is first, both summaries really do land on `ExtractIpv4Prefixes`, and no compiler warning fires — but it dies on consequence: the two methods are private to one file and return `IEnumerable<string>` versus `string?`, so a reader misled by the tooltip gets a compile error rather than F2's defect, and round 5's refuted table already killed the identical stale-doc-comment shape in this same subsystem ("no production code performs the substring test — the claimed outcome is unreachable") while round 6 explicitly classified false comments in this very file as tidy-up rather than a finding. |
| **Three Bootstrap-4 class names survive the v5 upgrade in shipped markup and style nothing** (`Views/Subnet/DeletedSubnets/_SubnetTable.cshtml:12`) `[x1]` | low | The three names really are inert (0 rules in the pinned bootstrap 5.3.8 bundle, none in `site.css`), but there is no wrong output — the rendered Deleted Subnets page and navbar look entirely normal — and the finding's premise is false: `_Layout.cshtml` has pinned Bootstrap **5** since the very commit that introduced these classes (`e05c3b1`, 5.3.2) and `.box-shadow` was never defined in `site.css`, so nothing "survived a v5 upgrade" and no shading or shadow was ever lost; cosmetic template residue, the unused-but-harmless category rounds 4–6 have killed each time, in an area round 6's clean bill already swept. |

**Dropped by the merge as already-settled: none this round.** No candidate was killed before
verification for duplicating a prior round's refuted table, struck entry or watch-list item — a
change from rounds 5 and 6, and a sign the finders read the brief.

**The pattern, for the fourth round running: what dies is test-coverage observations.** Four of the
seven above are the same shape — HEAD is correct, and the wrong output exists only in source the
auditor mutated. Two more are unused-but-harmless residue. If a candidate's failure scenario
requires reverting a shipped fix before it produces a wrong output, it is refuted before it is filed.

---

# Watch list — not findings, but worth knowing

Carried forward from rounds 4/5/6, plus what this round accepted as a known risk.

## Carried forward unchanged

- **ForwardedHeaders trust-all with `AllowedHosts: "*"`** (`Program.cs:223-228`); the
  **Development-only `DevAuthHandler` bypass** (`Program.cs:160-172`); **`GlobalSanitizationFilter`
  skipping nested `System.*` collections**; **`CollectDescendants` lacking a cycle guard**
  (`SubnetController.Helpers.cs:92`); the **blind `catch {}` around the DataProtectionKeys probe**
  (`Program.cs:88-92`).
- **C20 — the Azure reconcile check/act window**, documented at
  `SubnetController.AzureReconcile.cs:99-106`. The losing request of a duplicate concurrent commit
  returns 200 `success:true` with zero counts, rendered as "Deleted 0 stale subnet(s)". No
  corruption; same window.
- **The unreachable IP-change branch in `ValidateHostIpUpdate`** — and why it matters: it is the one
  place applying the network/broadcast reservations **without** the `cidr < 31` guard the other two
  sites carry. A trap for whoever makes that field editable.
- **`GlobalSanitizationFilter` runs after model binding and validation.** Demonstrated three times
  (D7 lengthening, E2 removing, F18 emptying). Any new `[Sanitize*]` attribute needs a matching
  validator.
- **`MockAzureService.DefaultConfirmation` is `Deleted`.** Any test touching the confirmation path
  must set the verdict explicitly.
- **Still no `WebApplicationFactory`, no integration host, no JS test harness.** This round adds
  G2, G6, G7, G10, G11 and G12 to the list of findings no automated test can pin today.
- **Migration `.Designer.cs` snapshots contain old column widths on purpose.** Correct and frozen.
- **A real Azure tenant ID is committed** at `src/Bastet/Properties/launchSettings.json:41`. Not a
  credential; not a finding. Re-checked at `6a1fe75`: still present.
- **The equality-vs-membership prefix check at `SubnetController.Azure.cs:341`** (the VNet-resource-id
  stamp; it was `:320` before `0de1293` shifted the file) — deliberately not
  implemented in round 6 (it needs an ARM read inside a transactional write).
- **The bulk import still reads only a multi-prefix Azure subnet's first prefix** when offering it to
  the wizard. Closing it means creating several Bastet subnets from one Azure subnet — a feature
  change. The prefix list is now carried on the inventory view model, so the plumbing exists.
- **`findOptimalCidr`'s loop bound**, the `site.js` consolidation of the CIDR→mask copies, and the
  per-prefix "already imported" sentence — all deliberately left in round 6.
- **`AnnotatePrefix` cannot return `AlreadyImported`** — established over 4,046 brute-forced planner
  outcomes. Behaviour of the planner, unpinned.
- **The usable-IP calculation's three copies agree at every CIDR 0–32.** The drift is in the
  **CIDR→mask** copies: six across four files, two of which F16 fixed.
- **Three cheap test gaps, each with a free fix**, from round 6's refuted table: a `SubnetDeleted`
  case for `IsAbsenceStatus`; E4's five call-site orderings; E9's `Count > 1` boundary. **Watch-list
  items, not findings.** Round 6 refused them; round 7 refused four more of the same shape.
- **`DeletedSubnets` does not archive `AzureResourceId` or `IsFullyAllocated`**, and the
  deleted-subnets table renders neither `Tags` nor `OriginalParentId`. There is no restore path
  anywhere in the app. **G1 depends on this** — it is what makes the archival unrecoverable and what
  constrains G1's fix.
- **`AZURE_TOKEN_CREDENTIALS=dev`, which the launch profiles set, excludes `EnvironmentCredential`.**
  A trap for anyone building a live rig.
- **`success` is not uniform across the Azure AJAX endpoints.** `/Azure/BulkGetVNets` reports an
  Azure read failure as `success:false`; `/Azure/ReconcileScan` reports the same failure as
  `success:true` with the reason inside the plan. Both conventions coexist deliberately. **G3 shows
  both consumers of `GetVNetInventory` failing through these two different shapes on one root cause.**
- **`pkill -f "Bastet.dll"` kills every instance on the box.** Match on `ASPNETCORE_URLS` or a PID
  file.
- **Headless Chromium never ticks `requestAnimationFrame`**, so jQuery's fx queue never drains and
  every animation assertion is a false pass unless `window.requestAnimationFrame` is deleted first.
- **Three CodeQL log-forging alerts are open on `main` and are expected to stay open.** True
  positives resolved by a mechanism CodeQL cannot see (the sanitizing console formatter). Do not
  re-raise the alerts themselves.
- **F15 / the migration lock.** `73f68ac` amended round 6's F15: the lock now opens the configured
  catalog first and falls back to `master` only on SQL 4060. **Do not propose re-applying an
  unconditional `master` scope.** The documented mid-bootstrap window at `Program.cs:256-259` (one
  replica locking on `master` while a peer locks on the catalog, with EF Core's own
  `__EFMigrationsLock` still serialising the half that applies migrations) is accepted.

## Added this round

- **ARM ids are path-based and survive delete-and-recreate.** Measured twice: deleting a VNet and
  re-creating it with the same name in the same resource group returns a **byte-identical**
  `AzureResourceId` (only `resourceGuid` changes, which Bastet never stores). Any future guard that
  compares stored ARM ids can rely on this; the "but recreate breaks it" objection to G1's fix is
  empirically false and should not be re-litigated.
- **`GetVNetInventory` is 1+N by construction** — one subscription listing plus one serial ARM call
  per VNet, ~1.3–2 s over 5–6 VNets on the rig. G3 fixes the 404 case, but the shape itself is what
  makes ARM **429 throttling** collapse the whole subscription too. If G3's primary fix (read
  `vnet.Data.Subnets`) is not taken, the throttling exposure remains.
- **The queued-writer connection-pool amplification (G5) is accepted as low, not closed by design.**
  A replica can park up to `Max Pool Size` connections inside `sp_getapplock` doing no work. The
  threshold is the SqlClient default of 100 per replica and nothing in the repo, README or
  appsettings sets it. Anyone deploying Bastet behind a fan-out client, a bulk script, or more
  replicas than expected should set `Max Pool Size` explicitly.
- **`Logging__LogLevel__*` is a higher-precedence override than every `BASTET_LOG_LEVEL_*` knob.**
  Measured. Until G4 lands it is the only configuration that can actually silence the SQL log; after
  G4 lands it still outranks the documented knobs, and the README should say so.
- **`SaveTokens = true` has no scope gate.** Measured: the OIDC handler persists whatever the token
  response contains, whether or not the corresponding scope was granted. G7's scope deletion is a
  best effort, not a guarantee, on IdPs that volunteer refresh tokens (Keycloak's standard flow).
- **The DataProtection key ring is persisted unencrypted in the application database** (accepted;
  `Program.cs:100-102`, confirmed by the app's own startup warning *"No XML encryptor configured"*).
  This is the precondition that gives G7 its consequence, and it also means anyone with database read
  access can forge any Bastet session. Known, deliberate for the air-gapped/plain-HTTP hosting model,
  and recorded here so the next round does not re-derive it as a finding.
- **The rig's label→port table collapses every colon-less label onto port 5259.** Three verifiers
  this round found a sibling agent already bound there and had to take an out-of-band port
  (5297/5307/5312/18260). Not a product defect; a rig hazard worth fixing before round 8.
