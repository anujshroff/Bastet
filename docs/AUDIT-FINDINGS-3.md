# BASTET — Bug & Security Audit Findings (Round 3)

Third fresh pass, run as a six-member audit team with delegated beats — security/web, logic & data
integrity, Azure integration, locking & lifecycle, UI/client-JS, and a diff review of the last three
commits (`761f1b9`, `6edef5c`, `aedd0bd`). Every finding was then handed to an independent
adversarial verifier instructed to refute it against the real code; several verifiers ran live
repros (a .NET 10 model-binding harness, a console-logger harness, BCL `IPAddress.Parse` probes).
**24 findings were raised, 0 were refuted**; two were upgraded from plausible to confirmed by
runtime repro. One finding remains "plausible" (depends on Azure allowing IPv6-only VNets).

**Overall verdict: no critical issues.** Two highs (an input-canonicalization gap that can break the
core no-overlap invariant, and a silent-data-loss bug in the single-import wizard), four mediums,
and a tail of lows/infos concentrated in the newer Azure import surface. Round-1/2 fixes all held
under re-verification.

_Both highs (C1, C2) are fixed and committed — each reproduced against the running framework before
the fix: C1's alias duplicate via a controller test, C2's dropped selections via a live MVC
model-binder harness._

---

## Medium

_All four mediums (C3–C6) are fixed and committed. C3, C4 and C6 were reproduced against the real
code first: `BuildPlan` never returned within 20s for two names sharing their first 50 characters,
the CIDR sweep rejected a legal /24 → /23 expansion with two bogus overlap errors (grandparent and
grandchild), and a 54-character VNet name was written whole into a column declared for 50. C5 is
client-side only, so it was traced through the code paths rather than executed._

---

## Low

_C7 through C10 are fixed and committed. C7–C9 were reproduced first: every address of a /31 and the
single address of a /32 were rejected as reserved (against `CalculateUsableIpAddresses` reporting 2
and 1 usable); a /24 whose top half was allocated reported a phantom "0.0.0.0 – 255.255.255.254"
range of 4,294,967,294 addresses; and an encompassing import entry marked a parent fully allocated
both when that parent already had children and when the entry's prefix was unrelated to it._

_C10 and C11 are fixed and committed, both by widening the column rather than trimming the data: the
sanitizer's limits (100 for names, 1000 for descriptions) were always the intended sizes, and the
columns at 50/500 were the anomaly — `HostIpAssignment.Name` had used 100 all along, and
`CreateSubnetViewModel.Description` had promised 1000 into a 500-wide column, so an ordinary 600-char
description failed the insert with a generic 500 (found while fixing C11, not in this audit). C11's
append is additionally guarded: the note is only added when it fits, since it repeats what
`IsFullyAllocated` already records and the operator's own text should not be sacrificed for it._

_**The two schema changes in the series**: migrations `20260726000148_WidenSubnetNameTo100` and
`20260726003042_WidenSubnetDescriptionTo1000`, both in manual script `src/Bastet/Database/3.3.sql`._

_C12 and C13 are fixed and committed. C12: the Bulk Import wizard now invalidates the cached plan when
the subscription selection changes and in `loadVNets`' `beforeSend`, so every load — success, failure
or empty — re-locks steps 3/4. C13: the alignment in `findCompatibleNetworkAddress` is coerced back to
unsigned with `>>> 0`; the arithmetic was confirmed by transliterating it (192.168.1.0/23 aligned to
-1062731776, so the overlap test could never fire, while 10.0.1.0/23 stayed positive and worked).
`normalizeIpToSubnetBoundary`'s identical intermediate was deliberately left alone and commented —
it only feeds `numberToIpAddress`'s unsigned shifts and is never compared numerically. Both are
client-side, so traced through the code paths rather than executed._

_C14 and C15 are fixed and committed, both in `SqlServerSubnetLockingService`. C14: the acquire sets a
command timeout derived from the requested lock timeout (+30s) and restores the context's previous
value, mirroring the migration lock in Program.cs, and maps a client-side timeout (SqlException -2) to
the `TimeoutException` the call sites catch. C15: the release is wrapped in its own try/catch and
logged instead of thrown, so a release failure can neither report a committed operation as failed nor
replace the original exception when raised from a finally. Exercising either needs a real SQL Server —
the suite's locking double is the SQLite one — so both were verified by reading, not by runtime repro._

_C16 is fixed and committed. Reproduced against a real Kestrel app first: a trailing CR, a trailing
LF, or any non-ASCII character in the header value returns HTTP 500 ("Invalid non-ASCII or control
character in header"), while a tab is accepted — so the check matches exactly what the framework
enforces. The value is now trimmed (rescuing the common env-file CRLF outright) and validated once at
startup via `HttpHeaderValue.IsValid`, failing fast with a message naming the variable rather than
silently applying a framing policy the operator did not write._

### C17. `GlobalSanitizationFilter` logs raw pre-sanitization user input (log forging at Debug level)
`src/Bastet/Filters/GlobalSanitizationFilter.cs:77-83` — when sanitization changes a value, the
filter LogDebugs the ORIGINAL value truncated by length only; CR/LF survive. Verifier reproduced the
forged log line with a console-logger harness at Debug level. Only fires when an operator sets
`BASTET_LOG_LEVEL_DEFAULT=Debug` (e.g. troubleshooting in production) and requires an authenticated
Edit-role user. The project already strips CR/LF elsewhere (`AzureService.SanitizeForLog`, added for
the CodeQL log-injection rule) — the filter just never got the same treatment. **Fix:** route the
logged values through the same CR/LF stripping, or log property names/lengths instead of values.

---

## Info

_C18 is fixed and committed, with a second half the finding did not cover: the guard redirects to
Subnet Details carrying `TempData["ErrorMessage"]`, and that view rendered no TempData at all — so
making the guard fire would only have produced an unexplained redirect. Details now renders both
message blocks (the markup `HostIp/Index` already used), which also restores five messages that were
being dropped silently: subnet create, subnet edit, the Azure import summary, this guard, and
`SetAllocationStatus` validation errors._

_C19 is fixed and committed: `SubnetDivisionService`, `ISubnetDivisionService`, `SubnetDivisionDto`
and the DI registration are deleted (~350 lines). Nothing referenced them, and what they offered was a
DI-resolvable write path that took no global lock and skipped the fully-allocated/host-IP checks — the
hazard being that it looked like supported infrastructure. `SubnetCalculation` was left alone; it
lives in `IPCalculationModels.cs` and belongs to `IpUtilityService`. Git history keeps the division
math recoverable if the feature is ever built for real._

_C20 is closed as accepted, not changed. The residual race is real but is a bounded cross-admin one:
it needs a concurrent write to a subtree that Azure has just been re-confirmed to report as deleted,
inside the window where the lock is contended. Azure cannot be re-checked while holding the lock, the
single-subnet delete has the same confirm-then-cascade semantics, and everything is archived rather
than destroyed — so adding a check here would make bulk stricter than single delete and risk spurious
409s on a flow already gated behind a typed confirmation. The window, and what closing it would take
(comparing each subtree against `stillStale[id].DescendantSubnetIds` inside the lock), are now
documented at the lock in `SubnetController.AzureReconcile.cs`. Note the hardening this audit
originally suggested — re-verifying NetworkAddress/Cidr/ResourceId — would not have caught the
scenario described, since the target itself is unchanged; it is the subtree that grows._

_C21 is fixed and committed — by rejecting, deliberately not by truncating or widening as the other
members of this family (C6, C10, C11) were. The column already fits every real ARM ID (~330 chars),
so nothing legitimate is constrained, and `AzureResourceId` is an identifier rather than prose:
reconcile matches Bastet subnets to live Azure by it, so a truncated ID would match nothing and leave
the subnet permanently reported as deleted in Azure — which is exactly the set reconcile offers for
deletion. Over-long IDs now return a 400 naming the field, on both the single and bulk import paths._

_C22 is fixed and committed: the `ErrorCode` property, the `SUBNET_OVERLAP` branch in
`ConflictError.cshtml`, the "Error Type" alert in `_ErrorLayout.cshtml`, the `_SubnetOverlapGuidance`
partial and the test asserting the property was always null are all deleted. The
`ViewData["RenderErrorGuidance"]` mechanism itself stays — it is live for `NotFound` and `ServerError`.
Not re-wired through TempData as the finding offered: nothing in the app produces a 409 error *page*
(the reconcile 409s are JSON API responses), so wiring it would have been inventing a feature under
cover of a cleanup._

_C23 is fixed and committed, but not as proposed. Including zero-IPv4 VNets in `GetVNetInventory`
would have leaked them into the Bulk Import wizard's VNet tree as rows with nothing selectable —
`GetVNetInventory` serves `BulkGetVNets` as well as the two reconcile paths, and the skip is correct
for that consumer. Instead the reconciler's reason now states what it can actually distinguish: "no
longer exists in Azure, **or** no longer has any IPv4 address space." That removes the misleading
claim shown above a Delete button without touching a shared code path, and is accurate regardless of
whether Azure permits fully IPv6-only VNets — the premise this finding was only ever "plausible" on._

---

## Clean bill (checked this round, no findings)

- **Authorization & CSRF:** fallback policy fails closed; every controller/action carries the correct
  role policy; all 14 state-changing POSTs have antiforgery; AJAX endpoints send the token header;
  `[AllowAnonymous]` surface is exactly Error + AccessDenied/Logout/SignedOut.
- **XSS / injection:** no `Html.Raw` anywhere; all three wizards escape consistently (checked
  character-by-character, including attribute contexts); no concatenated SQL; SSRF surface pinned to
  the fixed ARM endpoint; Azure portal link prefix-pinned.
- **Locking (the #134 refactor):** session-owned `sp_getapplock` on a pinned connection, released in
  finally; **every** subnet/host-IP mutation runs its full read-validate-write span inside the lock —
  no TOCTOU gap found at any call site; Sqlite test double is semantically faithful; migration lock
  correct (including the command-timeout rule the service itself misses, C14).
- **Round-1/2 fixes re-verified:** TempData refactor complete (no `errorMessage` route values
  anywhere); `/0` fix has no unfixed `1u << 32` siblings; all four SRI hashes re-verified
  byte-for-byte against the live CDN assets; logout returnUrl validation holds (incl. `//` forms);
  mass-assignment split (`AzureImportSubnetViewModel`) fully migrated.
- **Bulk import/reconcile core:** plan rebuilt from fresh DB snapshots inside the lock before any
  write; reconcile delete fails closed at every traced link (scan failure → zero deletables →
  Conflict on mismatch → typed confirmation → single transaction); planner overlap/containment math
  complete for canonical inputs; error JSON never leaks exception details.
- **EF model & lifecycle:** unique indexes, Restrict self-FK, RowVersion enforced via OriginalValues,
  original CIDR re-read from DB (resubmission bypass stays closed), archive tables preserve original
  audit fields; deepest-first archive ordering intact after the #133/#134 refactors.
- **Diff review:** the locking rework, `ArchiveSubnetSubtreeAsync` extraction, Referer→parameter
  refactor, and view-model split were compared line-by-line against the deleted code — no behavior
  lost; test changes across all three commits assert the new behavior rather than weakening
  assertions; 475/475 tests pass.

---

## Watch-list carried forward (unchanged from round 2)

GlobalSanitizationFilter nested-collection skip · CollectDescendants cycle guard ·
dead `ValidateHostIpUpdate` IP-change branch · blind DataProtectionKeys probe ·
Development-environment auth bypass · ForwardedHeaders trust-all + `AllowedHosts:"*"` (accepted).
