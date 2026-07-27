# Bastet — Round-4 Audit Findings

**Target:** branch `task/audit-4`, HEAD `e774d4f` "Misc Cleanup (#138)" (all round-3 fixes squashed).
**Test baseline:** 531 passing.
**Date:** 2026-07-25

## Verdict

No critical or high-severity defects. The round-3 fixes hold up — I attacked `e774d4f` directly with a dedicated beat and found no regression in the migrations, the widened limits, the new helpers, or the deletions. What round 4 surfaces is **three medium correctness defects** (one client-side, two in the Azure reconciler), a long tail of **low-severity** correctness and messaging bugs, and — from the new dead-code beat — a substantial amount of **refactor residue**, including a second orphan left behind by the `SubnetDivisionService` deletion.

The two reconciler findings (D2, D3) are the ones worth reading first. Both can end with Bastet offering live Azure-linked subnets for archival.

## How this audit ran

Seven parallel auditors (security/web, logic & data integrity, Azure, locking & lifecycle, UI/client-JS, round-3 regression, dead code), each finding then handed to an independent verifier instructed to **refute** it.

It ran **twice**. The first pass lost the security beat to an agent error; my attempt to re-run just that beat re-executed the whole pipeline instead. The accident turned out to be useful: two independent passes over the same tree, 88 agents total. Findings are tagged accordingly:

- **[×2]** — found independently by both passes. Strongest signal.
- **[×1]** — found by one pass only. Still verified, but one pass missed it, so treat the *absence* of a finding as weak evidence.

Pass 1: 31 survived, 1 refuted. Pass 2: 37 survived, 4 refuted. Merged and de-duplicated below.

I personally re-checked every citation in D1–D10 and a sample of the rest against the working tree. No invented line numbers; the few corrections the verifiers made are folded in.

---

# Medium

_D1 is fixed and committed. Reproduced first, in a real browser: the shipped wizard markup and script
were loaded with the jQuery 4.0.0 `_Layout.cshtml` pins, driven through the audit's exact sequence
(tick `web` → Select All on → Select All off → tick `mgmt`), and the emitted form payload inspected.
With `mgmt` alone visibly checked, the browser POSTed **both** `subnets[0].Name=web` and
`subnets[1].Name=mgmt`, each with its own `subnets.Index` — confirming the note that `e774d4f`'s
explicit-index binding lets a non-contiguous stale row bind cleanly rather than being truncated. After
the fix the same sequence POSTs `mgmt` only. The scoping concern was settled the same way rather than
by reasoning: `vnetName` and `vnetResourceId` are still present in the fixed payload, so restricting
the reset to `#subnet-list` leaves them alone. `.trigger("change")` on Select-All was deliberately not
added as well — it fixes this one path but leaves the submit handler unable to correct future
divergence, and one authoritative mechanism beats two partial ones. No permanent regression test ships
with it: pinning this needs a browser harness, and the repo has no JS test infrastructure._

_D2 is fixed and committed. The collision was confirmed against the real ARM ID shapes read back from
a live subscription, not against assumed ones: both a genuine subnet ID and a VNet sitting in a
resource group named `subnets` return true for `Contains("/subnets/")`. The anchored test compares the
parsed `ResourceType` instead, which reports `Microsoft.Network/virtualNetworks` for the awkward VNet
and `Microsoft.Network/virtualNetworks/subnets` for a real subnet — verified against both shapes.

The test lives in a new shared helper, `Services/Azure/AzureResourceIdentity.cs`, rather than being
patched into each site: the finding's own concern is that the two copies drift, and the portal-link
logic in `_SubnetDetails.cshtml` had the identical defect — an RG named `subnets` would truncate a
VNet's own portal link back to the resource group. Both callers now share one implementation.

`ResourceIdentifier`'s constructor was found to throw on malformed input — `FormatException` for a
non-GUID subscription segment or a bad shape, `ArgumentException` for empty — and `AzureResourceId` is
free text on the entity, so the helper uses `TryParse` and treats anything unparseable as VNet-level,
the branch such a value already took. One hand-edited row cannot abort a whole reconcile scan.

Three tests added. The regression test was confirmed to fail against the pre-fix code and pass after,
so it genuinely pins the defect; the other two guard the new parse path and pass either way by design.
531 → 536 tests._

_D3 is fixed and committed, and it is the one finding in this round that was proven end to end against
a live tenant rather than argued. A service principal scoped to a single resource group was pointed at
a subscription holding two: ARM's subscription-wide VNet list returned **HTTP 200 with the other
resource group's VNets simply absent** — not a 403 — so `inventory.Success` stayed true, the
`VNets.Count == 0` guard never fired, and reconcile offered two healthy subnets for archival, one of
them carrying three descendants, under the reason "no longer exists". Nothing had been deleted in
Azure. After the fix the same run withholds both and explains why.

The audit recommended confirming each resource individually on the theory that 404 means deleted and
403 means invisible, but never tested whether ARM actually distinguishes them. It does, and that was
checked before the design was settled: a direct read of the invisible resource returns **403
AuthorizationFailed**, a genuinely absent one returns **404**, for VNet and subnet IDs alike. Two
details only showed up by probing: subnets are child resources, so the generic resource API rejects
their IDs outright and the subnet accessor is required (chosen using the helper D2 introduced); and a
missing subnet reports error code `NotFound` while a missing VNet reports `ResourceNotFound`, so the
check keys on HTTP status, never the error string.

`AzureReconciler` is deliberately pure — "no DB, no Azure calls", so the deletion rules stay
exhaustively testable — so the ARM round-trips were kept out of it. `BuildPlan` proposes; a new pure
`ApplyConfirmations` disposes; the controller owns the calls. Only rows already proposed for deletion
are read, so a healthy scan costs no extra calls at all, and they run with bounded concurrency rather
than serially. Anything that is not an explicit 404 — 403, an error, a throttle, an ID absent from the
map — is withheld, because an unanswered question is not a deletion.

The commit path was covered too, but not as first written: an initial blanket "any warning blocks the
commit" was wrong and broke a legitimate test. Round 3 made the empty-subscription warning
deliberately advisory, and `ApplyConfirmations` already drops withheld rows from `Items`, so the
existing staleness check refuses them for free. The real gap was that the operator was never told
*why*, so `plan.Warnings` now rides along in that Conflict response instead.

Eight tests added; 536 → 544._

_D4 is fixed and committed. The regression test was written first and confirmed to fail against the
unfixed code, so it genuinely pins the defect rather than describing the implementation.

Two claims were checked by execution before the fix was scoped. First, that a CIDR *increase* can never
move the network address — swept across every increase from /8 to /32 for four network addresses, and a
valid network address never stopped being one, so no new network-address collision is possible and only
the broadcast side needs a check. Second, that the `cidr < 31` guard is load-bearing rather than copied:
`CalculateBroadcastAddress` still returns an address for a /31 (10.0.0.1) and a /32 (10.0.0.0) even
though `CalculateUsableIpAddresses` reports 2 and 1 for them, so a broadcast check without that guard
would reject a legal point-to-point assignment and undo round 3's C7. A test covers that boundary.

Four tests added; 544 → 548._

_D5 is fixed and committed. The special case is deleted rather than adjusted: `lastIp` has already
been decremented past the broadcast address, so the range is simply inclusive of both ends and
`lastIp - currentPosition + 1` is correct for every gap size — including the gap of exactly one that
the old branch only got right through a hard-coded `: 1` fallback. That boundary now has its own test,
since collapsing the branch removed the thing that used to handle it.

The two existing expectations were corrected (126 → 127, 62 → 63) and their comments rewritten. They
had read "Implementation returns 126", describing the code rather than asserting correctness — and the
second test made the inconsistency visible on its own, expecting 64 for a middle gap and 62 for a
trailing gap measured the same way.

One test added; 548 → 549._

_D6 is fixed and committed. The conditional is dropped entirely rather than having its threshold
nudged to `> 1`: this branch only runs when the gap begins at the network address, so exactly one
address is unusable whatever the gap's size, and the enclosing `Start > currentPosition` guarantees at
least one address so the subtraction cannot go negative. Both new tests were confirmed to fail against
the pre-fix code — the gap of one reported the network address as usable, the gap of two reported both.

The finding's optional suggestion to suppress a zero-usable leading row was **not** taken, after
checking what consumes these ranges. `_UnallocatedRanges.cshtml` renders each row with a "Create
Subnet" button keyed on `StartIp`, and a gap of one is exactly the network address — a legitimate place
to create a child subnet even though no host IP can be assigned there. Suppressing the row would have
removed a working affordance to tidy up a display string.

Two tests added; 549 → 551._

_D7 is fixed and committed by joining with a bare comma, the finding's second option, because it is
the only one under which legitimate input survives intact. Every other step in `SanitizeTags` removes
characters — trim, strip HTML, drop empties, drop over-long tags, take ten — so a single-character
separator makes the method non-expanding by construction rather than by arithmetic that has to be
re-checked whenever the limits move. The first option, bounding the output, would have had to *drop a
whole tag* from the exact case the finding describes (ten 24-character tags, 249 characters in, 258
out), turning a wrong error into silent data loss.

Bounding was added anyway as a backstop, trimming on a tag boundary so the result can never end in a
half-written tag. It is unreachable while the input respected its own length limit, but this method's
output lands directly in a fixed-width column and should not depend on some other layer having
validated first.

The cost is cosmetic: tags now render `a,b,c` rather than `a, b, c`. Checked before choosing —
nothing anywhere parses the column back apart, it is stored and displayed as one string, so the only
effect is the rendered spacing. Four existing expectations were updated to match, and the fixture in
the Edit form's placeholder still accepts comma-space input; it is simply normalised.

Five tests added; 551 → 556._

_D8 is fixed and committed. Reproduced first: against the unfixed action every out-of-range value
threw `ArgumentOutOfRangeException: CIDR must be between 0 and 32`, which is the 500 the finding
describes. The guard treats the value the way `parentId` is already treated — advice, not instruction —
so the form renders with that field blank and the usable half of the query string still pre-filled.

The check is hoisted into a `hasUsableCidr` local rather than wrapped around the mask calculation
alone, because the generated name twelve lines below also interpolates the raw `cidr`. Guarding only
the calculation would have stopped the 500 while still offering the operator a pre-filled name reading
`Parent-10.0.0.0/33`, which the POST then rejects. Both now key off the same condition.

Covered for negatives and both `int` extremes as well as 33, since the parameter is `int?` and nothing
between the query string and the calculation narrows it. Nine tests added; 556 → 565._

_D9 is fixed and committed by rejecting the combination up front, the finding's first option. The
alternative — hoisting the fully-allocated write out of the import guard — would have invented a
behaviour nobody asked for: `FullyEncompassesVNetPrefix` is an Azure-import concept, and outside that
context there is no VNet name to rename the parent to, so the endpoint would have been left guessing
what a caller meant rather than telling them it cannot be done.

The guard sits after sanitization rather than with the other argument checks, because sanitization is
what decides whether `vnetName` is really empty — a name consisting entirely of markup arrives
non-empty and leaves empty, and that value is exactly what the downstream guard tests.

It keys on the encompassing flag rather than on `isAzureImport`, so the documented plain batch-create
convention keeps working for ordinary children; a test covers that so the fix cannot be mistaken for a
ban on non-import callers. The three rejection tests were confirmed to fail against the unguarded code,
where the call returned success having written nothing.

Four tests added; 565 → 569._

_D10 is fixed and committed. All three swallow sites now rethrow after logging — `GetSubscriptions`,
`GetCompatibleVNets` and `GetCompatibleSubnets`, the last of which also covers the VNet being deleted
between step 2 and step 3 of the wizard. No view or JS changed: the controllers already had catch
blocks returning `success = false`, and the wizard already renders `#vnet-error` / `#subnet-error`;
swallowing in the service was the only thing making them unreachable.

Verified against live Azure rather than only in tests. Pointed at a VNet the credential cannot see,
`GetCompatibleSubnets` previously returned an empty list and now throws `RequestFailedException`, so
the wizard reports an error instead of "no compatible subnets found".

**One thing the fix deliberately does not cover, worth recording.** In the same live run
`GetCompatibleVNets` still returned zero results for an invisible VNet, and correctly so: ARM's
*list* operation succeeded with the resource filtered out by RBAC, so there is no exception to
surface. That is D3's failure mode appearing on the import path, and it is not fixable by rethrowing.
It is far less severe here — the worst outcome is an import that cannot proceed, not data being
archived — so it was left alone rather than bolting per-resource confirmation onto a wizard that
deletes nothing. The `_armClient == null` early returns were likewise left: the Import page runs
`IsCredentialValid()` first and reports a connectivity failure before any of this is reached.

Three tests added, including one asserting a genuinely empty result is still a success, so the fix
distinguishes "Azure says none" from "Azure could not be asked" rather than collapsing both into an
error. 569 → 572._

_D11 is fixed and committed, and it is no longer plausible — it is confirmed, twice over.

The load-bearing step neither auditor could check was whether `Response.Clear()` clears
`Response.Headers`. It does: `ExceptionHandlerMiddlewareImpl:163` calls `ClearHttpContext`, which
calls `context.Response.Clear()`, which is `ResponseExtensions.Clear()` — and that calls
`response.Headers.Clear()` outright. Read from `dotnet/aspnetcore` release/10.0.

Then measured rather than inferred. A minimal app replicating this exact middleware ordering was run
in Production against both arrangements: with the header middleware registered above
`UseExceptionHandler`, a 200 carried all four security headers and a 500 carried **none**; moved
below, a 500 carries **all four** and 200s are unchanged.

Both handler registrations are now above the header middleware, not just the exception one.
Status-code pages do not clear the response so they were never affected, but keeping the two together
leaves one rule to remember rather than an exception to it. `OnStarting` would also have worked and
was not used — it is more machinery than an ordering change needs.

No permanent test ships with this: pinning it needs a `WebApplicationFactory`, and the suite has no
integration host. The measurement above is the record. The finding's own severity note still stands —
`_ErrorLayout.cshtml` is a static panel with no forms or state-changing controls, so framing it
accomplishes nothing and MIME-sniffing is moot on a Razor `text/html` body. What is fixed is that the
code now does what the comment above it claims: security headers on every response._

_D12 is fixed and committed, and it is no longer plausible: the failure was reproduced against a real
SQL Server 2022, which the suite has never been able to reach. Both migrations succeed, the idle lock
connection is killed to stand in for an Azure SQL gateway timeout, and the unguarded release throws
`SqlException: The connection is broken and recovery is not possible` — turning a completed migration
into a hard startup crash. With the release wrapped, the same run logs the failure and startup
completes.

**A correction to the finding's framing.** Its first variant says that if `Migrate()` failed because
the connection died, "the release on that same dead connection throws too". They are not the same
connection: the release runs on the dedicated `migrationLockConnection` while `Migrate()` runs on EF's
own. Masking the real error therefore needs a partition that kills both, not one dead connection — and
that variant did not reproduce here, because the stand-in migration command survived having its SPID
killed. So the exception-masking half remains reasoned rather than demonstrated. It changes nothing
about the verdict: variant two alone justifies the fix, and the same wrapping covers both.

The finding's advice not to add a `State != Open` guard was followed, and the reason recorded at the
site: SqlClient does not poll the socket, so `State` still reports `Open` after a silent failover.

Matches `SqlServerSubnetLockingService.cs:49-63`, which round 3 fixed for exactly this reason one file
over. No test ships with it — exercising this needs a real SQL Server plus a killed connection mid-
startup, which is not something the suite can host; the reproduction above is the record._

_D13 is fixed and committed, and both the defect and the fix were demonstrated in a real browser
driving the shipped wizard. Pre-fix, after the snapshot was invalidated the Delete button came back to
life on the typed word alone and clicking it posted nothing; post-fix it stays disabled and still
posts nothing. Same sequence, same jQuery 4.0.0 the app pins.

The same run settles round-3's **C5**, which was closed on reading. The confirmation reset is real —
the button is disabled immediately after invalidation both before and after this change — so C5's fix
works and D13 was a separate gap that let typing defeat it, not an incomplete round-3 fix. Worth
recording, since the two live in the same function.

Fixed as a shared `refreshDeleteButton()` rather than a one-line patch, as the finding asked: the
snapshot-and-text rule now has one home, used by the `input` handler, by `showCommitError`, and by
`invalidateConfirmation`, so the two re-enable sites cannot drift apart again. `invalidateConfirmation`
also navigates back to review when step 3 is the visible pane, and `#rec-scan-btn` now calls
`invalidateScan()` before `runScan()` so the later steps are not reachable through their pills for the
whole duration of a re-scan.

One honest limit on the evidence: the probe cannot isolate the pane-navigation half, because the scan
button activates step 2 on its own path regardless. That change matters for the other invalidation
routes — a failing re-scan reaching `showScanError` → `invalidateScan()` — which the probe does not
drive. The button-state half is what was measured.

No permanent test ships: pinning this needs a browser harness the repo does not have._

_D14 is fixed and committed. Every failure path in the batch-create action now goes through one
`BatchCreateFailure` helper: an import redirects to the parent's Details page with the reason in
TempData — a proper PRG matching the success path, and Details renders TempData as of `e774d4f` — while
a direct JSON caller keeps the status codes it already relies on. Both halves have a test, so the
redirect cannot later be applied to every caller by accident.

The one path that cannot redirect to Details is the parent not being found, since that page would 404
in its own right; it redirects to the subnet list instead.

Updating the six existing tests that asserted `BadRequest` was part of the fix, not a workaround: they
were pinning the old contract on the wizard path. They now assert the redirect **and** that TempData
actually carries a non-empty message, so they check more than before rather than less. The one test
posting with `isAzureImport: false` still asserts `BadRequest`, which is what keeps the two paths
honest.

The bfcache half is also addressed: a `pageshow` handler gated on `event.persisted` clears
`importSubmitting` and restores the button's label, so coming Back leaves a usable wizard instead of
one stuck on "Importing…" that needs a manual reload. That half is **not** demonstrated — bfcache
behaviour is browser-specific and awkward to drive, and I did not manage to reproduce a restore in the
harness. The primary claim was straightforward to reason about from the response types; the restore
behaviour is written to the spec's `persisted` flag rather than to an observation.

Two tests added, six updated; 572 → 574._

_D15 is fixed and committed. Both hashes were computed from the live CDN bytes rather than copied from
anywhere, fetched twice to confirm they were stable, and then verified in a real browser: the shipped
partial was loaded with the pinned jQuery, and both `jQuery.fn.validate` and
`jQuery.validator.unobtrusive` still executed with zero console errors. That check matters more than
usual here — a wrong integrity hash makes the browser refuse the script *silently*, which would have
disabled client-side validation on all five forms that render this partial, including two Edit-role
destructive flows, with nothing on screen to say so.

Self-hosting under `wwwroot/lib` was not done. It would also fix air-gapped deployments, and the
project has no `wwwroot/lib` at all, so every CDN asset shares that limitation — pulling only these
two local would be a larger and inconsistent change than this finding calls for. The finding's own
framing is the right scope: an inconsistency with the file's own siblings, now removed._

_D16 is fixed and committed, and it is no longer plausible — the code-side chain was confirmed end to
end. `SimpleConsoleFormatter` (dotnet/runtime release/10.0) performs no control-character escaping at
all: its only transformation is `WriteReplacing`, substituting `Environment.NewLine` with padding. So
an ESC byte was captured reaching the console sink verbatim, and after the fix the same probe shows it
gone while the surrounding text and the tab both survive.

That newline-padding behaviour is also why ESC is the *better* forgery vector, which the finding did
not note: a raw newline gets indented under the log line rather than starting a new one, whereas
`ESC[1A` `ESC[2K` rewrites the line above without any line break at all.

Widened to control characters generally with tab exempted, exactly as the finding advised — a naive
`!char.IsControl(c)` would have broken `LogSanitizerTests`' deliberate assertion that a tab survives,
and there is now a dedicated test guarding that so a future tidy-up cannot quietly drop it. The
characters are dropped rather than escaped: these are identifiers and names logged for diagnosis, not
content that needs to round-trip.

ESC is written as a named `(char)0x1B` constant in both the source and the tests, never as a literal
control character — literals are invisible in diffs and get mangled through tool round-trips.

Six tests added, all confirmed to fail against the pre-fix sanitizer. The final rendering step remains
the operator's terminal emulator rather than anything in this codebase, but the code-side half — that
attacker bytes reached the sink intact, and no longer do — is now measured rather than argued.
574 → 581._

_D17 is fixed and committed as a shared `Views/Shared/_TempDataAlerts.cshtml`, the structural fix the
finding asked for rather than pasting the block into two more files. Eight views now render it:
`Subnet/Index` and `Subnet/Delete` were the two genuinely missing sinks — the delete flow's success
message with its cascade counts, and every rejected confirmation redirecting back to the same page
unchanged — and the six that already had their own copy were switched over so there is one place to
change and one thing to forget.

The markup was byte-identical across all six, which is what made consolidating safe rather than a
redesign. `HostIp/Delete/_Header.cshtml` keeps its own heavier treatment deliberately and was left
alone.

Verified by reading the shipped views rather than assuming: Razor resolves partial names at runtime,
so a typo or wrong folder compiles cleanly and shows up only as a silently missing alert. All eight
redirect targets pull the partial in, none has a stale copy left behind.

Two of the D41 Create-page `_ErrorAlert` partials read the same key on a path that never sets it —
that is a separate finding and is left for it._

_D18 is fixed and committed, matching `HostIpController.cs:47` — the structurally identical guard ten
lines above that `e774d4f` fixed and this one was left out of. The errors now go into
`TempData["ErrorMessage"]` joined into a sentence, because a redirect starts a new request and
ModelState does not survive one: they were being collected and immediately discarded, so Details
reloaded with nothing to say while `/HostIp/Index/{id}` for the same subnet explained itself properly.

Three tests added; the two covering the guard were confirmed to fail against the pre-fix controller,
and the third asserts a leaf subnet still reaches the form with no message left behind. The finding's
route correction is recorded in the test: the reachable form is `GET /HostIp/Create?subnetId=5`, what
the tag helper emits — a hand-typed `/HostIp/Create/5` binds `id`, leaves `subnetId` at 0 and returns
NotFound before the guard is reached at all.

578 → 584._

_D19 is fixed and committed. Reproduced first: a 95-character parent name composed a 107-character
value against a 100-character limit. The parent name is what gives way, never the address and CIDR —
those are the part that makes a generated name mean anything, and truncating the combined string
would cut them off instead.

Done by extracting the planner's `WithSuffix` into `Services/SubnetNaming.cs` and having both callers
use it, which is the shared helper the finding asked for. The planner's copy already encoded the right
rule and its own doc comment explained why; duplicating that reasoning into the controller would have
been the drift the finding warns about. The planner now delegates, so its behaviour is unchanged and
its existing tests still cover the logic.

One test added, confirmed to fail against the unclamped code. 584 → 585._

_D20 is fixed and committed by dropping both references rather than wiring either up. Runtime Razor
compilation was the one with a choice attached — the finding offered scoping it to Debug instead — but
adding a development convenience nobody has asked for is not what a dead-reference finding calls for,
and it can be added back deliberately if hot-reloading views is ever wanted.

Verified beyond a green build, because removing the Razor package is the kind of change that compiles
fine and fails at first render. After a clean rebuild the runtime-compilation assemblies are gone from
the output, and the app was then started against a real SQL Server and asked for three pages:
`/Subnet`, `/Subnet/Create` and `/Subnet/DeletedSubnets` all returned 200 with their proper titles and
real content, two of them exercising the shared TempData partial added in D17, with no render errors
logged. Views are compiled into the assembly at build time, so nothing depended on the package.

The EF InMemory reference was unambiguous — `UseInMemoryDatabase` appears nowhere and
`TestDbContextFactory` uses SQLite in-memory._

---

# Info — correctness & consistency

_D21 is fixed and committed in the planner, where the sibling loop twelve lines below already does the
right thing — not in `GlobalSanitizationFilter`, whose skipping of nested `System.*` collections is
why the value arrives raw and was accepted in round 3.

Not an XSS, as the finding says: no `Html.Raw`, no `innerHTML`, no export endpoint, so there is no
sink. What was actually wrong is that "descriptions contain no markup" was silently false for one
field, and an invariant that holds everywhere except one place is worse than one that does not hold at
all — it is the kind of thing a future feature relies on without checking.

One test added, confirmed to fail against the unsanitized code. 585 → 586._

_D22 is fixed and committed by rejecting the selection, the finding's second option, not by silently
leaving `ChildSubnets` empty.

The deciding fact is one the finding does not mention: **Azure does not permit overlapping subnets
within a VNet**, so a subnet covering the entire VNet prefix leaves no room for siblings. This
selection cannot come from a real Azure inventory — reaching the planner with it means the post was
crafted or corrupted. Quietly dropping the children would have made the preview honest while still
applying half of an impossible request; the error says what is wrong and commits nothing.

The item now returns before `WillMarkFullyAllocated` is set, so nothing is left half-planned — the
preview shows an error rather than a target that will be flagged fully allocated. That matters
because `IsFullyAllocated` is exactly the state in which the missing children could never be added
later without clearing the flag.

Two tests added — one confirmed to fail against the pre-fix planner, one asserting the ordinary
single-encompassing-subnet case still commits, so the guard cannot be mistaken for a ban on the
supported flow. 586 → 588._

_D23 is fixed and committed by referencing `MaxSubnetNameLength` instead of restating the number, so
it cannot drift again the next time a limit moves. Re-swept afterwards: no `50-character` or
`500-character` reference survives anywhere in production code or views, which matches both audit
passes independently concluding this was the last one.

The migration `.Designer.cs` snapshots still contain `HasMaxLength(50)` and `HasMaxLength(500)` and
were deliberately not touched — they are frozen history describing the schema as it was at each
migration, and rewriting them would make the snapshots disagree with the migrations they belong to._

_D24 is fixed and committed, applying the finding's own correction rather than its suggested wording:
the Edit path's real message is "Description cannot **be longer than** 1000 characters"
(`EditSubnetViewModel.cs`), not "cannot exceed", so copying the suggestion verbatim would have swapped
one mismatch for another.

The value was raised to 1100 rather than left at 600 with the comment deleted. All three errors in
this test are hand-injected and the value is inert either way, but a fixture holding a perfectly legal
length under a comment reading "too long" is a trap for whoever reads it next — which is exactly how
this one survived the round-3 widening.

Swept for siblings afterwards: the only other `new string('x', 600)` in the suite is a deliberately
over-long `AzureResourceId` against a 500-character column, which is correct and was left alone._

---

# Info — dead code & refactor residue

The new beat. Grouped for convenience; several could reasonably be one commit.

_D25 is fixed and committed: the interface member, the 39-line implementation and the
`SubnetCalculation` model are deleted, 86 lines in total. `IPRange` in the same file is live in
thirteen places and stays.

Corroborated by coverage before deleting, not by grep alone: `CalculatePossibleSubnets` reports 0 of
39 lines executed across the whole suite, and has no reference anywhere outside its own declaration.
Zero coverage and zero references is the standard the handoff set for a safe delete, and this meets
both. Git history keeps the division arithmetic recoverable if the feature is ever built for real.

This was the second orphan left by the `SubnetDivisionService` deletion — the helper survived its only
consumer — which is the residue that commissioned this beat in the first place._

_D26 is fixed and committed. All eight methods are deleted — roughly 130 lines of hand-rolled bit
arithmetic reimplementing what `IpUtilityService` and the validation services own.

Coverage confirmed every one at 0 executed lines, and the reference sweep found the eight form a
**closed group**: the only call between any of them is `OverlapsWith` invoking `CanContainSubnet`,
both inside the set. Nothing outside points in, so removing them together cannot strand a caller.
That mattered, because a first read of the grep showed `CanContainSubnet` with one call site and it
would have been easy to keep the pair on that basis.

The comment at `SubnetController.Helpers.cs` that called this surface authoritative — "mirrors
Subnet.CanAddChildSubnet" — is reworded rather than repointed. There is no surviving authority to
name: the rule is enforced right there in the create path, and pointing at a second implementation is
what let a duplicate sit unused long enough to drift.

`using System.Net;` became dead in both entity files with the methods gone and was removed too; the
build carries no unused-using warning, so it would have sat there indefinitely._

_D27 is fixed and committed: the method, its interface member and its two tests are deleted.

This is the one dead-code item where coverage argued *against* deleting and was right to be
overruled. `ValidateSubnetDeletion` reports 9 of 9 lines executed — but every one of those hits comes
from the two tests that are its only callers anywhere. Coverage measures what tests touch, not what
ships; the reference sweep showed zero production call sites, and the two facts together are what
make this dead rather than merely untested.

Deleting rather than rewiring is right because the rule it encodes is **false**. It returns "Cannot
delete a subnet that has child subnets. Delete the children first," while the shipped path
(`DeleteConfirmedCore` → `ArchiveSubnetSubtreeAsync`) deliberately cascades and reports "and N child
subnet(s) were deleted successfully." A validator asserting the opposite of shipped behaviour is worse
than no validator, and its two green tests made the suite look like it covered subnet-deletion rules
while covering nothing that runs.

`SUBNET_HAS_CHILDREN` in `SubnetValidationService` became orphaned with it and was removed too. The
identically-named constant in `HostIpValidationService` is live in three places and stays — worth
noting, because a careless sweep on the name alone would have taken a working guard with it.

588 → 586 tests. A future deletion hook should be written against the cascade semantics that ship._

_D28 is fixed and committed by deleting both methods, their interface members and their ten tests —
not by having `ValidateSubnetCreation` delegate, which was the finding's other option.

Coverage decided it. Delegating is only worth the risk if those ten tests are the sole coverage of
these rules, and they are not: the live `ValidateSubnetCreation` already runs at **74% (85/114 lines)**
under the existing create-path tests. Rewiring the application's core no-overlap invariant to reuse a
parallel implementation — one that is missing the canonical dotted-quad rule and the duplicate lookup
the live path adds — would have meant refactoring the most safety-critical path in the app while
simultaneously reorganising the tests that would catch a mistake. Deleting removes false comfort and
changes no runtime behaviour at all.

Two things the sweep caught that a delete-by-name would have broken:

- **`ValidateSiblingOverlap` is live** — called from `ValidateSubnetCidrChange` as well as from the
  deleted `ValidateNewSubnet` — so it and its test stay. It sits between the two blocks of deleted
  tests in the same file, which is exactly where a whole-file deletion would have taken it.
- **`CreateSubnetDto` and `UpdateSubnetDto` are now dead**, having had no consumer but these two
  methods. D29 lists them as live, which was true when the audit was written; that finding's scope
  grows as a result and it is handled there.

586 → 576 tests._

_D29 is fixed and committed, and its scope is **larger than the finding recorded** — five files rather
than three. `CreateSubnetDto` and `UpdateSubnetDto` were live when the audit was written, and they
were: their only consumers were `ValidateNewSubnet` and `ValidateSubnetUpdate`, which D28 deleted one
commit earlier. Re-running the liveness check after that deletion rather than trusting the finding's
list is what caught it.

Deleted: `SubnetDtos.cs` (`SubnetDto` and `SubnetDetailDto`, which inherits from it — dead together,
so neither is stranded), `HostIpDto.cs`, `CreateHostIpDto.cs`, `CreateSubnetDto.cs`,
`UpdateSubnetDto.cs`.

Kept: `SubnetAllocationDto` (4 references) and `UpdateHostIpDto` (3). The folder was never dead
wholesale, and a sweep by directory rather than by type would have taken two working types with it.

Three `using Bastet.Models.DTOs;` directives became dead with them and were removed. The build emits
no unused-using warning, so nothing would ever have flagged them._

_D30 is fixed and committed. Both attributes are deleted; the six live ones in the same two files stay.

The sweep needed care, and a first pass got it wrong. Grepping for `[Tags` reported **zero**
applications, which would have made `TagsAttribute` look just as dead — it is applied
fully-qualified, as `[Bastet.Services.Security.Tags(MaxTags = 10, MaxTagLength = 50, ...)]`, on
`SubnetViewModels.cs`. Re-running the check against both the bare and qualified forms is what kept a
live validator out of this commit. `SanitizedString` and `SanitizeGeneral` are the only two with no
application in either form.

`IsSafeText`, the service method the deleted `SanitizedStringAttribute` called, is still used by the
surviving `SafeText` and `Tags` attributes and was left alone._

_D31 is fixed and committed: all six properties deleted, leaving the four the model actually carries.

Verified by matching writers to readers rather than by name, because a bare-name grep is useless here
— `Subscriptions`, `VNets` and `Cidr` all exist on other types and reported dozens of hits. The
controller's object initialiser sets exactly `SubnetId`, `SubnetName`, `NetworkAddress` and `Cidr`,
and `Model.` references across the view and its seven partials resolve to exactly the same four. No
writer, no reader, on either side.

`AzureSubscriptionViewModel`, `AzureVNetViewModel` and `AzureSubnetViewModel` — the types those
properties referenced — are live elsewhere and stay; the AJAX flow that replaced this server-rendered
wizard still returns all three._

_D32 is fixed and committed: the property and its single assignment of the literal `false` are gone,
and no reference to the name survives anywhere.

It could never have been true. The loop that constructs these items opens with
`if (sub.FullyEncompasses) { continue; }`, so an encompassing subnet never reaches the assignment —
and its own XML doc described behaviour that `BulkImportPlanItem.WillMarkFullyAllocated` actually
carries, which is the more misleading half: a reader could reasonably have written a condition
against it._

_D33 is fixed and committed. Every real construction site uses `new()` followed by `AddError`, so both
factories were unreachable.

The finding's own warning was the thing to be careful about: `ValidationAttributes.cs` is full of
`ValidationResult.Success` references, and they are **not** these. That file imports
`System.ComponentModel.DataAnnotations`, whose unrelated `ValidationResult` exposes `Success` as a
static *field*. Checked before deleting — a search on the bare name matches ten lines that have
nothing to do with Bastet's type._

_D34 is fixed and committed. `AdditionalData` had no reference beyond its declaration; `OriginalPath`
was set in both error actions and rendered by no view — checked against the `.cshtml` files
specifically, since "assigned twice" is exactly what makes a property look live to a grep.

Removing them stranded three more things the compiler would not have complained about, all removed
too: the `IStatusCodeReExecuteFeature` and `IExceptionHandlerPathFeature` locals, which existed only
to populate `OriginalPath`, and the `using Microsoft.AspNetCore.Diagnostics;` that existed only for
those two interfaces. `using System.Diagnostics;` stays — `Activity.Current` still needs it, and the
two namespaces are easy to confuse at a glance.

Same family as the `ErrorCode` plumbing round 3 removed._

_D35 is fixed and committed: the permanently-false branch and its comment are gone, and no reference
to the key survives.

The sibling `ViewData["RenderErrorGuidance"]` immediately above it is live — set by
`NotFound.cshtml:5` and `ServerError.cshtml:5` — and stays. That similarity is what made the dead one
read as working infrastructure, and it is why the two were checked separately rather than treated as
one mechanism._

_D36 is fixed and committed: the `returnUrl` parameter, the `ViewData` assignment and the comment
about "potentially" using it are gone, leaving `public IActionResult AccessDenied() => View();`.

One correction to the finding's framing, checked against the framework source rather than assumed. The
parameter was **not** unpopulated: `CookieAuthenticationHandler.cs:462` builds the redirect as
`Options.AccessDeniedPath + QueryString.Create(Options.ReturnUrlParameter, returnUrl)`, so a real
forbidden response arrives here carrying `?ReturnUrl=...`. It was bound and then discarded, which is
why removing it changes nothing — the framework still sends the query string, MVC simply no longer
binds it, and the view never read it either way.

The `returnUrl` on the sibling `Logout` action is genuinely used and covered by tests; it was left
alone._

_D37 is fixed and committed. `NetworkInputPattern()` had exactly one occurrence — its own declaration
— while each of the three surviving generated regexes has two, the declaration and a call. The source
generator was emitting and compiling a regex nothing ever invoked.

`SanitizeNetworkInput` keeps its manual character loop. Rewiring it onto the regex would have been a
behaviour change dressed up as a cleanup: the loop *filters* invalid characters out, the pattern
`^[a-zA-Z0-9.\-_:]*$` only *tests* a whole string, so they are not interchangeable and
`IsValidIpAddress` depends on the filtering behaviour to detect that its input changed._

_D38 is fixed and committed: the write-only property is gone and the lambda that set it is now empty.
`DevAuthOptions` itself stays — `AuthenticationHandler<TOptions>` requires an options type — and its
emptiness is now documented rather than implied.

The reason this survived is the reason the finding is worth acting on: `Program.cs:158` and
`Program.cs:170` set an identically-named `AccessDeniedPath` two lines apart, and only the second does
anything. The first was on `DevAuthOptions`, a property this codebase declares and the handler never
reads; the second is the cookie handler's genuine framework option, which is load-bearing and
untouched. A comment now says so at the site, because the next reader will have the same question.

Both files verified after the change: the only surviving `AccessDeniedPath` assignment is the cookie
handler's._

_D39 is fixed and committed. The finding warned that the two passes disagreed on scope and to verify
against the union before deleting anything — worth doing, because checking each property against its
own model's views turned up **three claims that were wrong**, and acting on the list as written would
have broken the build or the app.

Deleted, all six verified individually as neither written nor read (or written and never read):
`EditSubnetViewModel.IsFullyAllocated` · `DeleteSubnetViewModel.Confirmation` ·
`DeleteSubnetViewModel.IsFullyAllocated` · `SubnetDetailsViewModel.HostIpCount` ·
`SubnetTreeViewModel.IsFullyAllocated` · `SubnetTreeViewModel.TotalIpAddresses`. The two orphaned
write sites in `SubnetController.Delete.cs` and `.Helpers.cs` went with them.

**Kept, against the finding:**

- **The `allSubnets` parameter on `BuildSubnetTreeViewModel` is not unused.** It is passed to the
  method's own recursive call when descending into child subnets. Removing it would have broken the
  tree build outright.
- **`SubnetDetailsViewModel.TotalIpAddresses` and `IsFullyAllocated` are live** — both are read by the
  Details partials, `IsFullyAllocated` notably by `_UnallocatedRanges.cshtml`. Only the
  `SubnetTreeViewModel` properties of the same names are dead, and the two models are easy to conflate.
- **`DeleteSubnetViewModel.HostIpCount` is live** — read by the Delete partials. Its neighbour
  `IsFullyAllocated`, set on the same object two lines apart, is not.

The generic sweep that produced those first two answers was a bare-name grep, which is useless here:
`IsFullyAllocated` exists on four types and reported seven writes and six reads pooled together. Only
matching each property against the views bound to *its* model separated them._

_D40 is fixed and committed: all seven rules deleted, none referenced by any `.cshtml` or `.js`, and
the `Subnet` entity has no status field for them to have described. Braces re-checked as balanced
afterwards, since a mis-sliced CSS file fails silently rather than at build time.

Adjacent rules in the same file — `.subnet-children` immediately above — are live and untouched._

_D41 is fixed and committed, but **not** by deleting both partials — that would have broken the HostIp
create page.

The two are not equivalent. `Subnet/Create/_ErrorAlert.cshtml` contained nothing but the dead TempData
block, so the partial and its include are gone; the Subnet create form surfaces its
`ModelState.AddModelError("", ...)` failures through `asp-validation-summary="ModelOnly"` in
`_SubnetForm.cshtml`, which was checked before removing anything.
`HostIp/Create/_ErrorAlert.cshtml` has a **second block** rendering `ModelState[""]` errors, and that
one is live — `HostIpController.Create` reports lock timeouts, validation failures and unexpected
errors exactly that way. Only its TempData half was removed.

The finding's own observation — "Both Create actions surface failures through ModelState instead" — is
the reason that block exists, which is what makes deleting the whole file on the strength of the
finding's headline the wrong move.

Verified that nothing redirects to either Create action, so the TempData block could only ever have
displayed a message that leaked from an unrelated request — TempData survives until read, which D17's
shared partial now makes far less likely._

_D42 is fixed and committed. `PARENT_NOT_FOUND` had one occurrence, its own declaration; C# emits no
warning for an unused private const, which is how it survived invisibly.

`REQUIRED_FIELD_MISSING` went with it. It was live when the audit was written and became dead two
commits earlier when D28 removed `ValidateNewSubnet` and `ValidateSubnetUpdate` — the only emitters.
Re-checking every constant in the block rather than only the one the finding named is what caught it;
the other ten are all still used at least twice._

_D43 is fixed and committed. The file was byte-identical to the root `_ViewImports.cshtml` — confirmed
with `diff`, not by eye — and Razor applies those hierarchically and cumulatively, so it contributed
nothing. No other view folder had a local copy, so it was a one-off leftover rather than a convention
being broken.

Verified by running the app rather than by building it: Razor resolves imports at render time, so a
mistake here compiles cleanly and fails only when a page is requested. `/HostIp/AllHostIps` and
`/HostIp/AllDeletedHostIps` both return 200 with their proper titles and no Razor resolution errors
logged._

---

# Refuted — reported by a finder, killed by the verifier

Recorded so they don't get re-raised next round.

| Finding | Why it was killed |
|---|---|
| `ISubnetLockingService`'s optional `timeout` parameter is never supplied | All facts verified, but no failure scenario — the finding's own scenario field opens "Not a runtime defect." Also misunderstands what the parameter controls: it governs lock *acquisition wait*, not operation duration. API-surface preference. |
| Acquire command-timeout path throws without releasing a granted lock | Self-refuting on timing — the command timeout is `(timeoutMs / 1000) + 30`, so it cannot fire before `@LockTimeout`. |
| `Subnet.NetworkAddress` `MaxLength(15)` is dead; fluent `HasMaxLength(45)` wins | Static facts correct, failure scenario unreachable. Collapses to a maintainability note. |
| `MaxSubnetNameLength = 100` declared in three places that can drift | Central premise backwards — the three constants currently **agree**, so no input produces a wrong outcome. DRY preference wearing a severity label. |
| `IHostIpValidationService.ValidateIpIsWithinSubnet` has no external caller | Facts accurate, but the stated cost is false. Interface-surface taste. |

---

# Watch list — not findings, but worth knowing

- **`#select-all-subnets` is never reset when a new VNet's subnet list loads**, so its checked state persists across VNet changes. Latent, related to D1; fixing D1 the recommended way makes it harmless.
- **No `WebApplicationFactory` or integration host exists in the test suite at all.** Several settling tests (notably D11) require building that infrastructure, not just adding a test method.
- **No JS test harness**, so D1 and D13 cannot be pinned by an automated test — the definitive check is a browser run with devtools on the POST body.
- **`GlobalSanitizationFilter` runs after model binding and validation.** D7 is one consequence; any future sanitizer that can *lengthen* a value has the same hazard.
- **Migration `.Designer.cs` snapshots still contain `HasMaxLength(50)`/`(500)`.** Correct and frozen — do not "fix" them.

# Clean bill

Areas swept that produced nothing, across both passes:

- **Authorization coverage** — no missing `[Authorize]`, no missing `[ValidateAntiForgeryToken]` on state-changing actions.
- **XSS** — no `Html.Raw` and no `innerHTML` anywhere in `src/Bastet/Views` or `wwwroot`. Razor encoding is relied on consistently.
- **SQL injection** — the only raw SQL is the parameterised `sp_getapplock`/`sp_releaseapplock` calls.
- **Log sanitization consistency** — every log statement carrying a user- or Azure-controlled string routes through `LogSanitizer` (D16 is about the helper's strength, not its coverage).
- **The two round-3 migrations and `3.3.sql`** — correct, correctly ordered, consistent with the model snapshots, `Down()` reverses `Up()`, no data-loss risk, both DbContexts present.
- **The widened 100/1000 limits** — swept independently by both passes; only the stale doc comment (D23) and the unclamped generated name (D19) were missed by the original sweep.
- **`HttpHeaderValue`** — correct and consistently applied.
- **Locking semantics** — command-timeout arithmetic, restore-on-every-path, and `sp_getapplock` session ownership under EF connection pooling all hold. No captive dependencies in DI.
- **Tests added by `e774d4f`** — no assertions weakened to pass, none asserting the buggy behaviour. (D24 is a stale fixture, not a weak test.)

---

## Suggested order of attack

1. **D2, D3** — the reconciler can offer live subnets for deletion. Highest real-world consequence.
2. **D1** — imports data the user explicitly deselected.
3. **D4, D5, D6, D7, D8** — correctness bugs with concrete wrong outputs.
4. **D9–D20** — messaging, fail-open and consistency defects.
5. **D21–D24** — small consistency fixes.
6. **D25–D43** — dead code. Mostly deletions; could be batched into a handful of commits.
