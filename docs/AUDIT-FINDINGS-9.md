# Bastet — Round-9 Audit Findings

| | Audit | After reconciliation |
|---|---|---|
| Round | **9** (finding letter **I** — findings are `I1` … `I8`) | |
| Branch | `audit/round-9` | one commit per finding |
| HEAD | `a8f669b` — *"Audit 8 Cleanup (#152)"*, identical tree to `main` | |
| Build | **0 warnings, 0 errors** | **0 warnings, 0 errors** (clean rebuild, `bin`/`obj` deleted) |
| Tests | **690 passed**, 0 failed, 0 skipped | **716 passed**, 0 failed, 0 skipped (+26) |
| Working tree | clean at start and at finish | clean |
| Date | 2026-07-29 | 2026-07-29 |

Every line number in the original findings was re-derived against the working tree at `a8f669b`; the
struck entries below cite the **post-fix** lines.

**All eight findings were fixed — none was refuted on re-verification.** Each was reproduced against the
unfixed build before being fixed and re-measured after, one commit per finding, each carrying its own
struck entry. The reproductions were not taken on trust: I3's 87 s lock hold and refused rival write,
I4's three response orderings in real Chromium, I5's 153-of-340 under-stating renders, I6's live 500s and
I7's four Production callback failures were all re-run against HEAD first, and every regression test was
confirmed failing before its fix landed.

**Three of the audit's proposed fixes were rejected or corrected on evidence, and four suggestions were
declined** — each recorded in the relevant struck entry. Two are worth surfacing here because they look
reasonable and will be re-proposed: I6's diff does not compile as written (`AccountController.cs` had no
`using Bastet.Services.Security;`, caught as CS0103), and I3's fix needed all three of its verifier's
extensions before the outage actually cleared.

### Final verification sweep

- **Clean rebuild** with `bin`/`obj` deleted: 0 warnings, 0 errors. **Full suite: 716 passed**, 0 failed,
  0 skipped (690 → 716, +26; no test was deleted or disabled).
- **Real app against real SQL Server 2022**, every major area requested and asserted on **content**, not
  status codes: **21 of 21 passed** — subnet list/create/details/edit/delete, host-IP
  list/create/edit/delete, per-subnet and global deleted-host-IP listings, deleted subnets, both purge
  confirmation pages, all three Azure wizards, roles, access-denied and error pages. The
  delete → archive → purge flow was driven through the app's own forms, and both purge pages were
  confirmed to state exactly the scope their form posts.
- **Security headers** still ride on a normal 200 **and** on the error page:
  `X-Content-Type-Options`, `Referrer-Policy`, `Content-Security-Policy: frame-ancestors 'none'`,
  `X-Frame-Options: DENY`, `Cache-Control: no-store,no-cache`.
- **Log read and classified: 0 `fail:` lines, 0 unhandled exceptions.** Seven `warn:` lines, all
  expected and all pre-existing: three `Azure credential validation failed` (the sweep deliberately ran
  with no Azure credentials, so `DefaultAzureCredential` fell through to Managed Identity), three EF
  `QuerySplittingBehavior` advisories, and one DataProtection `No XML encryptor configured` — the last is
  on the watch list as accepted. Both non-Azure warnings were confirmed present on unfixed builds too,
  and the round added no `Include` anywhere.
- **Azure end to end against live ARM** through the application's own `DefaultAzureCredential` path:
  subscriptions → VNet discovery → bulk preview → commit (2 targets created) → reconcile scan → delete
  commit. Both counter-tests pass, which is what shows the reconciler **discriminates** rather than
  merely blocks: a genuinely deleted VNet was still offered and archived (`warnings:[]`,
  `subnetsArchived:1`), while a descendant the credential cannot read was **withheld and named** in the
  warning and its stale ancestor withheld by the cascade guard, with the delete refused and both rows
  surviving. All rig fixtures were torn down and both resource groups re-listed empty.

### Deliberately not done

- I1's optional service-level second cascade guard — the controller fix is measured sufficient for both
  `ReviewItems` statuses.
- I2's warning-only interim, I3's `AsNoTracking`-only interim, I4's button-as-mutex interim, I5's lock,
  I6's percent-encoding alternative, I7's `SkipUnrecognizedRequests`, I8's `long`-arithmetic interim —
  each superseded by the real fix, and I4's and I5's are unsound besides.
- `_StepConfirm.cshtml` still has no warnings block; nothing checks a cancellation token on the batch
  import; the `AccessDeniedPath` asymmetry between the cookie and OIDC handlers is unchanged. All three
  remain on the watch list.

---

## Verdict

**Eight findings: no critical, two high, two medium, four low, no info. Nothing was refuted at
verification.** All eight were reproduced on the live rig — real SQL Server 2022, real ARM, real
headless Chromium — and for all eight the proposed fix was *built and measured* in a copy outside the
repository rather than argued from source.

Read **I1** first. Azure reconcile's review-item cascade guard — the one round 8 shipped as H1 — lives
inside `ApplyConfirmations`, and `ApplyConfirmations` is only reached when the plan contains at least
one *absence*-status item. A plan made entirely of prefix **drift** (`VNetPrefixRemoved`,
`SubnetPrefixChanged`) takes an early return at `AzureController.cs:381` and the guard never runs at
all, so approving one drifted ancestor archives a descendant the same scan had just verified live in
Azure. The A/B is the part to look at: same two rows, same Azure state, and adding one *unrelated*
stale subnet elsewhere in the tree flips the server from `200 subnetsArchived:2` to `409 Conflict,
nothing deleted`. Safety is decided by whether some other subnet happens to be stale.

Then **I2**, the same harm through a different hole: a descendant whose `AzureResourceId` belongs to
another subscription is `continue`d past at `AzureReconciler.cs:77`, so it joins none of the protected
sets, is named nowhere in the plan, and is archived by its stale ancestor unexamined. Both I1 and I2
destroy rows unrecoverably — `DeletedSubnets` archives no `AzureResourceId` and there is no restore
path anywhere in the app.

**I3** is the write surface: one ordinary 40-subnet Azure import re-reads the whole `Subnets` table
twice per child while holding the single global write lock, so on a 200k-subnet deployment every other
write in the application is refused for 40 s with *"The operation timed out due to high concurrency"*
— and nothing was written. **I4** is the third instance of the round-8 H2/H3 shape, in the one wizard
round 8 did not touch: the reconcile wizard paints a superseded scan's stale-subnet table and its green
*"nothing to clean up"* banner on top of the current scan's *"Nothing was checked"* failure panel, and
the archive can be driven from that screen.

The four lows are ordered by consequence: **I5** purges more archive records than its own confirmation
page states; **I6** turns sign-out into an HTTP 500 that leaves the session alive; **I7** turns every
routine OIDC callback failure, including a declined consent prompt, into an unhandled exception and a
500; **I8** renders `Showing 51-40 of 40` over an empty table, and serves page 1's rows for a page
number large enough to overflow the `int` skip.

**Verifier corrections are the other thing worth reading.** Three severities were corrected, all
downward (I3 high → medium, I6 and I7 medium → low). One citation was re-anchored (I3). On five
findings a proposed fix or interim was built, measured, and found unsound, incomplete or backwards, and
replaced or dropped — including I1, where the finder's own proposed regression test **passes against
pristine HEAD** and its proposed interim is strictly *more* expensive than the real fix.

---

## How this audit ran

**Two passes per beat.** Every beat was worked by two independent finder agents that did not see each
other's output. Twenty finder agents ran; twenty returned. The beats represented in the surviving set
are `azure`, `security`, `ui`, `locking`, `regression`, `regtests` and `deadcode`.

**What the tags mean.**

- **`[x2]`** — both independent passes of the beat found it. Independent agreement is decent evidence
  that the code, not the reader, is the problem.
- **`[x1]`** — one pass found it and the other did not. Absence is weak evidence, so **every `[x1]`
  got a second verifier** on a reachability-and-consequence lens as well as a mechanism lens. That is
  why the `[x1]` findings below carry `2/2` verifier votes and the `[x2]` findings carry `1/1`.
  `[x1]` warrants **more** scrutiny during reconciliation, not less: this round's `[x1]` set contains
  the app-wide write outage (I3) and both purge/sign-out defects.

**Verification.** Every candidate went to a verifier whose brief was to *kill* it. A verifier ran its
own instance on its own port against its own SQL catalog, from an unmodified `a8f669b` tree exported
with `git archive` or `cp -a` — never from the repository working directory — and then built the
proposed fix in that copy and measured it (`dotnet build --no-incremental`, `dotnet test`, and the same
live request replayed against the patched build). All eight candidates survived unanimously.

**The funnel.**

| | |
|---|---|
| Finder agents launched / returned | 20 / 20 |
| Raw findings reported | 26 |
| Candidates after dedup, merge and brief screening | **8** — 4 `[x2]`, 4 `[x1]` |
| Survived verification | **8** |
| Refuted by a verifier | **0** |
| Reproduced live | **8** |
| Not runnable | **0** |
| Baseline | `a8f669b` on `main`, 690 tests |

The 18 raw findings that did not become candidates died at the **merge**, not at a verifier:
duplicates of each other, and re-files of things the round-9 brief lists as accepted, deliberately not
done, or refuted in rounds 5-8. That is why the refuted table below is empty — no candidate reached a
verifier and lost.

---

# Critical

None.

---

# High

_I1 is fixed and committed. `ConfirmProposedDeletionsAsync` no longer returns when the plan carries no
absence claim: it substitutes an empty confirmation map and calls `ApplyConfirmations` anyway
(`AzureController.cs:377-392`). The cascade guard over `plan.ReviewItems` therefore runs on the strength
of the target's own subtree instead of on whether some unrelated row happens to be absent, and the ARM
round trip is still skipped when there is nothing to ask — so a healthy scan costs no extra calls. An
empty map is safe because a non-absence item takes the `!IsAbsenceStatus` → keep path at
`AzureReconciler.cs:166`._

_Verified as the audit prescribed: the drift-only pair now answers **409 Conflict** with both rows
intact where HEAD answered **200 `subnetsArchived:2`**. The regression test drives the **controller**,
not the reconciler — `BulkDeleteStaleAzureSubnets_DriftOnlyPlanOverReviewItemDescendant_IsRefused` in
`test/Bastet.Tests/Azure/SubnetControllerAzureReconcileTests.cs`, which seeds a `VNetPrefixRemoved`
target over a `FullyAllocatingSubnetDeleted` descendant and asserts the archive is refused. Confirmed
failing against the unfixed tree first (`Expected ConflictObjectResult, Actual OkObjectResult` — the
archive had already happened), passing after. Build 0 warnings / 0 errors; suite **690 → 691**._

_Three of the audit's suggestions were **not** taken, all for the reasons its own verifier recorded.
Part 2 (widen `liveLinked` with `FullyAllocatingSubnetDeleted` rows) covers only one of the two
`ReviewItems` statuses and would put `UnrecognisedResourceId` rows in a set whose warning claims they
"still exist in Azure", which was never established for them. Part 3's reconciler-level test passes
against pristine HEAD, so it pins nothing. The proposed interim at `AzureReconciler.cs:124` breaks
`AzureReconcilerTests.ApplyConfirmations_TargetWhoseDescendantIsAReviewItem_IsAlsoWithheld`, whose
arrange asserts `Assert.Single(plan.Items)` before `ApplyConfirmations` runs. The optional service-level
second `WithholdTargetsWhoseCascadeIsBlocked` call was **deliberately not added**: the controller fix is
measured sufficient for both `ReviewItems` statuses, and a second guard with its own wording is new
surface for no measured gain. `_StepConfirm.cshtml` still has no warnings block — unchanged, and noted
on the watch list, where I1 sharpens why it is not a substitute for the guard._

---

_I2 is fixed and committed, as the audit proposed and with no correction needed. `BuildPlan` now
collects every row it skips for belonging to another subscription into a `notCovered` set
(`AzureReconciler.cs:55`, populated at `:85`) and passes it to a second
`WithholdTargetsWhoseCascadeIsBlocked` call at `:136-140`, so an ancestor whose subtree holds one is
withheld and the warning names it. The message is worded separately from the `liveLinked` one on
purpose: these rows were never read, so saying they "still exist in Azure" would assert something the
scan did not establish — the same distinction the code already draws between `NotVisible` and
`Unknown`._

_Two tests, both in `test/Bastet.Tests/Azure/AzureReconcilerTests.cs`.
`StaleAncestorOverOtherSubscriptionDescendant_IsWithheld` builds the multi-subscription tree the
existing `SubnetFromOtherSubscription_Ignored` never exercises — that one uses a standalone row with no
ancestor, so it passes with or without the guard — and was confirmed failing against the unfixed tree
(`Assert.Empty() Failure: Collection was not empty`; the ancestor was still on offer).
`StaleTargetWithNoOtherSubscriptionDescendant_IsStillOffered` is the over-withholding control and
passed **before** the fix as well as after, which is what makes the pair meaningful rather than
vacuous. Build 0 warnings / 0 errors; suite **691 → 693**._

_The audit's **interim** (a `plan.Warnings` entry when a plan item's `DescendantSubnetIds` intersects
the skipped set) was **not** taken: it removes the silence without changing the verdict, so the row is
still destroyed, and the real guard is the same shape as two calls already in the method. Live-ARM
confirmation of both I1 and I2 — including the counter-test that a genuinely deleted resource is still
offered and deletable — is recorded in the final sweep rather than repeated here._

---

# Medium

_I3 is fixed and committed, with all three of the verifier's extensions taken. `ValidateSubnetCreation`
gained an optional `List<Subnet>? treeCache` parameter used in place of its own unfiltered read
(`SubnetController.Helpers.cs:234`), and the snapshot is loaded once per batch by a new
`LoadSubnetTreeForBatchAsync` helper. The helper lives in `Helpers.cs` rather than at each call site —
which is how the missing `using Microsoft.EntityFrameworkCore` in `SubnetController.Azure.cs` is avoided
rather than papered over, and both batch paths need it anyway. Rows created inside a batch are appended
to the snapshot: **children and created targets both**, in `BatchCreateChildSubnetsCore` and in
`BulkCreateFromAzurePlanCore` (`BulkAzure.cs`), because `orderedItems` is sorted by CIDR ascending so a
containing item runs first and appending only children would stop a later item seeing an earlier item's
target. The duplicate lookup and the parent read inside the validator stay real queries — they must see
the current transaction — and `AsNoTracking` is safe because cached rows are read only for Id, Name,
NetworkAddress, Cidr and ParentSubnetId._

_The redundant **pre-flight** validation pass in `BatchCreateChildSubnetsCore` was dropped, which is the
part that removes the shape rather than moving the threshold. The loop remains — it still assigns the
parent and picks out the encompassing entry — but it no longer validates: the creation loop validates
every entry against a snapshot that includes rows created earlier in the same batch, so it catches
strictly more than a pass before any insert could, and any failure rolls the whole transaction back
either way. That halves the batch from 2N tree passes to N._

_Measured on real SQL Server 2022 (container, 200 001 rows), HEAD versus the fix, same request bodies,
app built Release from copies outside the repository:_

```
40  children:  29 994 ms -> 3 158 ms   rival POST /Subnet/Create 29 991 ms -> 2 983 ms (both 302)
120 children:  87 537 ms -> 4 957 ms   rival REFUSED at 30 587 ms (200 text/html, the form re-rendered
                                       with "The operation timed out due to high concurrency")
                                       -> succeeds in 4 887 ms with a 302
unfiltered whole-table Subnets reads during a 40-child import: ~84 -> ~5
                                       (the batch's own 2N loads collapse to 1; the rest are ordinary
                                        page reads outside the lock)
both builds created all 120 children, correctly parented
```

_The 145-child figure from the audit was not re-measured: the driver here emits 8 hidden inputs per
child where the wizard emits 7, so `FormOptions.ValueCountLimit` refused the post at 145 and 120 was
used instead. That is a property of the harness, not of the application, and 120 already crosses the
30 s budget on HEAD._

_One test added, `BatchCreateChildSubnets_EntryContainedInAnEarlierEntry_ReturnsValidationError` in
`test/Bastet.Tests/SubnetManagement/SubnetControllerBatchCreateTests.cs`. The existing
`BatchCreateChildSubnets_OverlappingSubnets_ReturnsValidationError` posts two **identical** entries, which
the unique `{NetworkAddress, Cidr}` lookup catches without the snapshot; the new one posts an entry
**contained in** an earlier entry, which only the appended snapshot can catch. It was proven to pin that
append by removing `treeCache.Add(newSubnet)` and re-running: the contained row was created and the
assertion failed. Build 0 warnings / 0 errors; suite **693 → 694**._

_The audit's **interim** (`AsNoTracking()` alone on the single query) was not taken — it was measured in
the audit to leave all three rival writes refused, and the real fix landed. The cosmetic note the
verifier flagged stands: on the `ExactMatch` branch the cached row keeps its pre-rename `Name`, so an
error message quoting `bestParent.Name` can print a stale name; it is commented at the cache load and
left alone. Also unchanged and still on the watch list: nothing checks a cancellation token on the batch
import, so an operator who gives up does not shorten the hold._

---

_I4 is fixed and committed, exactly as proposed. `runScan` now takes a sequence number
(`let scanSeq = 0;` beside `lastPlan`, `const seq = ++scanSeq;` as its first statement) and its
`complete`, `success` and `error` callbacks each open with `if (seq !== scanSeq) { return; }` — the same
shape `_ImportScripts.cshtml` and `_BulkScripts.cshtml` already carry, so the third wizard no longer
differs from its siblings. `renderPlan` additionally clears `#rec-scan-error` where it reveals
`#rec-scan-content`, which is safe because both `showScanError` branches return before that line._

_Verified in real headless Chromium against the shipped view, unfixed build then fixed, with
`requestAnimationFrame` stubbed. The two scan responses were fabricated by route interception rather
than driven through ARM: the defect is entirely in the client's ordering, so no Azure was needed, and
the reachable two-gesture path (Scan → step-1 pill → Scan) was used rather than a double-click. The
served page carried **0** occurrences of `scanSeq` before and **5** after._

```
                     unfixed                                    fixed
error-last     scanError=True  scanContent=True  rows=2   scanError=True  scanContent=False rows=0
clean-bill     scanError=True  nothingStale=True          scanError=True  nothingStale=False
success-last   scanError=True  over 2 live rows           scanError=False plan intact, no error painted
```

_So on HEAD the failure panel and a populated stale-subnet table were on screen together, and in the
zero-stale variant the green "there is nothing to clean up" banner rendered underneath "Nothing was
checked". Both are gone. The **reverse** ordering — a superseded failure landing after a valid plan,
which the audit noted was not part of the original candidate — is fixed by the same guard: the valid
plan survives untouched._

_The audit's **interim** was not taken, and its premise is worth recording because it looks reasonable:
disabling `#rec-scan-btn` for the scan's duration does not close the window, because
`$("#rec-subscription-select").on("change")` unconditionally re-enables it, so an operator who returns
to step 1 mid-scan and picks a different subscription starts a second overlapping scan — and that
variant is worse, since `selectedSubscriptionId` then names one subscription while the repainted table
describes another. No test ships with this fix: there is still no JS test harness, which the watch list
records, so the browser measurement above is the evidence. Suite unchanged at **694**; build 0 warnings._

---

# Low

_I5 is fixed and committed as the two-line reorder the audit prescribed, in both twins: the bound is read
first and the count is taken inside it — `SubnetController.Delete.cs:284-285` and
`HostIpController.cs:597-598`. The rendered count now equals the POST's scope by construction, with no
lock and no extra query, and the `count > 0` beside `confirmedMaxId = 0` variant closes for free because
`maxId == 0` now implies `count == 0`._

_Reproduced and re-measured on real SQL Server 2022, 6 threads rendering the purge page for 25 s against
a writer committing 200 transactions of 500 archive rows each (bursts, because
`ArchiveSubnetSubtreeAsync` commits a whole subtree at once), scoring every distinct render against
`COUNT(*) WHERE Id <= confirmedMaxId` — valid after the fact because nothing deletes from the table
during the run:_

```
HEAD   340 distinct renders   153 under-state the true scope   0 over-state   max excess exactly 500
fixed  188 distinct renders     0 under-state                  0 over-state   max excess 0
```

_Max excess of exactly 500 is one whole archive transaction landing inside the bound and outside the
count. On the fixed build both purge pages still redirect on an empty archive with the honest message
("There are no deleted subnet records to purge." / "...host IP records..."), checked by following the
302 on each twin._

_No test ships with this fix, and that is a deliberate call rather than an omission: on any static
dataset `COUNT(*)` and `COUNT(Id <= MAX(Id))` are equal by definition, so a unit test over a fixed
archive passes identically before and after and would pin nothing. The defect only exists between two
round trips, which needs a concurrent writer — hence the rig measurement above. Suite unchanged at
**694**; build 0 warnings._

_The verifier's correction to the harm framing is kept: the view comment ("anything archived while the
operator was reading it survives") was **not** violated — the bound is a render-time snapshot, which is
H4's deliberate design. What was violated is the XML doc at `PurgeAllViewModels.cs:4-6`, that the purge
destroys exactly the records the operator was shown a count of. **No lock and no transaction** was added:
round 8 measured `ExecuteWithSubnetLockAsync` on these POSTs to make a currently-correct ordering wrong,
and the brief lists it as never to be re-proposed. The ordering alone closes it._

---

_I6 is fixed and committed as the audit prescribed: `HttpHeaderValue.IsValid(returnUrl)` is now a third
conjunct on the same guard (`AccountController.cs:44-48`), so a local path that cannot legally be written
as a header value falls back to the anonymous `SignedOut` page and sign-out still completes. One line,
reusing `Services/Security/HttpHeaderValue.cs` — written for exactly this Kestrel rule — and it fixes the
Production and Development branches together because both read `target`. It cannot narrow anything that
works today: every URL the app itself generates is ASCII._

_The audit's diff needed one correction, of exactly the kind it flagged on I3: written as
`Bastet.Services.Security.HttpHeaderValue.IsValid(...)` it compiles, but as `HttpHeaderValue.IsValid(...)`
it does not — `AccountController.cs` had no `using Bastet.Services.Security;`. The using was added rather
than fully qualifying the call, to match how the rest of the file reads. The build caught it (CS0103)._

_Live on the rig, Development arm — which reproduces the mechanism with no IdP, since `Redirect(target)`
writes the same `Location` header:_

```
                                        unfixed          fixed
?returnUrl=/Subnet                      302 -> /Subnet   302 -> /Subnet          (control, preserved)
?returnUrl=/caf%C3%A9                   500              302 -> /Account/SignedOut
?returnUrl=/price%E2%82%AC              500              302 -> /Account/SignedOut
?returnUrl=%2F%0D%0AX-Injected:%20yes   302 -> SignedOut 302 -> SignedOut        (already refused)
?returnUrl=//evil.example               302 -> SignedOut 302 -> SignedOut        (already refused)
"Invalid non-ASCII" lines in the log    2                0
```

_Five tests added to `test/Bastet.Tests/Security/AccountControllerLogoutTests.cs`: a four-case theory over
e-acute, U-umlaut, the euro sign and U+2028 for the Production branch, plus one Development case. All five
were confirmed failing against the unfixed tree first. The non-ASCII characters are written as `\u00E9`
style escapes rather than literals, so they survive diffs and tool round-trips — the file is pure ASCII.
Build 0 warnings / 0 errors; suite **694 → 699**._

_Two of the audit's suggestions were not needed. `HttpHeaderValueTests` **already** covers non-ASCII
(`'self' https://café.example.com` and curly quotes are in its reject theory), so there was nothing to
extend. Dropping the `IUrlHelper.IsLocalUrl` mock in favour of the real helper for one case was skipped:
the audit established that the mock returns exactly what the real helper returns for non-ASCII input, so
it already pins the defect, and standing up a real `UrlHelper` needs an `ActionContext` with routing for
no additional pinning power. The percent-encoding alternative — honouring internationalized paths instead
of dropping them — was **not** taken; it is more code for a case the application never generates, and the
audit lists it as an option rather than a recommendation. Also unchanged: `GlobalSanitizationFilter` still
returns immediately for `typeof(string)`, so a top-level `string?` parameter is never sanitized. That is
on the watch list and is how this value arrives raw._

---

_I7 is fixed and committed, taking the verifier's correction rather than the finder's original.
`OnRemoteFailure` is assigned beside `OnTicketReceived` (`Program.cs:257`), logs the failure **message**
at Warning — not `context.Failure`, which would re-create the same ten-line stack trace per anonymous
request merely relabelled — then redirects and calls `HandleResponse()`. The destination is a new
`[AllowAnonymous] SignInFailed()` action (`AccountController.cs:98`) with
`Views/Account/SignInFailed.cshtml`, **not** `AccessDenied`: that page states the account lacks the
necessary roles, which is untrue for three of the four triggers. Like `SignedOut`, it redirects home if
the caller somehow arrives authenticated._

_Measured against a Production instance pointed at the **real** Entra metadata for the rig tenant, with a
genuine challenge captured first (390-char state, real correlation and nonce cookies) and the callback
then replayed:_

```
                                     unfixed   fixed
bare GET /signin-oidc                500       302 -> /Account/SignInFailed
declined consent (access_denied)     500       302 -> /Account/SignInFailed
missing correlation cookie           500       302 -> /Account/SignInFailed
garbage state                        500       302 -> /Account/SignInFailed
GET /Account/SignInFailed            302       200 "Sign-in Not Completed / You were not signed in"
unhandled-exception log entries      4         0
OIDC warning entries                 0         4
total log lines for the run          50        18
```

_The consumed-`code` trigger (`invalid_grant AADSTS9002313`) was not re-run — it needs a real
authorization code, which requires an interactive sign-in — but it reaches `OnRemoteFailure` by the same
path as the other three, all of which were reproduced._

_The allow-list dependency the audit flagged is real and was proven, not assumed:
`test/Bastet.Tests/Security/ControllerAuthorizationTests.cs` gates every `[AllowAnonymous]` action, and
with the `AccountController.SignInFailed` entry removed the suite fails two rows — "is marked
[AllowAnonymous] but is not in the allow-list" — then passes 134/134 with it restored. Build 0 warnings /
0 errors; suite **699 → 702** (+3: the new action is enumerated by that theory)._

_The audit's **interim** (`options.SkipUnrecognizedRequests = true`) was not taken: it closes only the
unauthenticated half, and the declined-consent callback still 500s under it because that failure happens
after state and correlation have both validated. Nothing else changed — authentication still fails closed,
and the framework's own escape hatch remains unavailable because
`OpenIdConnectHandler.HandleAccessDeniedErrorAsync` only redirects when `AccessDeniedPath` is set on the
**OpenIdConnect** options while the tree's only one belongs to the **cookie** handler. That asymmetry is
left as it is and stays on the watch list._

---

_I8 is fixed and committed with the audit's clamp, in both twins: once `totalCount` is known,
`int totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));` then
`page = Math.Clamp(page, 1, totalPages);`, placed before the `Skip`/`Take` and before `CurrentPage` is
set — `HostIpController.cs:481-482` (`AllHostIps`) and `:533-534` (`AllDeletedHostIps`). No view change
was needed: both views derive their banner and pager from `CurrentPage`, so clamping it makes the label,
the rows and the pager agree by construction. Post-clamp `(page-1)*pageSize <= totalCount`, so the `int`
overflow is structurally gone rather than merely bounded._

_Reproduced and re-measured live, unfixed then fixed, four live host IPs (one page):_

```
                 unfixed banner                          fixed banner        rows unfixed -> fixed
page=1           Showing 1-4 of 4                        Showing 1-4 of 4    4 -> 4
page=2           Showing 51-4 of 4                       Showing 1-4 of 4    0 -> 4
page=45000000    Showing -2044967345--2044967296 of 4     Showing 1-4 of 4    4 -> 4   (page 1's rows)
page=2147483647  Showing -99--50 of 4                    Showing 1-4 of 4    4 -> 4
```

_The intended behaviour change is real and worth stating: `?page=2` on a four-row set now renders page 1
rather than an empty page 2. That is the only way the label, the rows and the pager can agree, and it is
what the audit's fix chose._

_Fourteen tests added in a new
`test/Bastet.Tests/HostIpManagement/HostIpPaginationClampTests.cs`: over-range pages on both listings
(including `45000000`, `999999999` and `int.MaxValue`), the last real page of a 61-row archive served
intact at 11 rows, page 3 of that archive clamping back to page 2, an empty listing staying on page 1, the
existing lower bound still flooring `0`/`-5`/`int.MinValue`, and a guard that pins `PageSize` at 50 so the
arithmetic above cannot silently stop meaning what it says. Nine of the fourteen were confirmed failing
against the unfixed tree first. Build 0 warnings / 0 errors; suite **702 → 716**._

_The audit's **interim** was deliberately not taken, on its own advice: doing the arithmetic in `long`
and capping the skip closes only the wrong-rows half and chooses the opposite semantics from the clamp
(a correctly empty page rather than the last page), leaving the inverted banner in place. Shipping both
would have been contradictory. Nothing else was touched — no unhandled exception, no write and no
disclosure was ever involved, and the rows the overflow served were rows the same caller could already
see at `?page=1`._

---

# Info

None.

---

# Refuted — reported by a finder, killed by the verifier

| Candidate | Verdict | Reason |
|---|---|---|
| *(none)* | — | **No candidate was refuted this round.** All 8 that reached a verifier survived, unanimously: the four `[x2]` at 1/1 and the four `[x1]` at 2/2. |

The kill happened one stage earlier. Of **26** raw findings, **18** did not become candidates — they
died at the merge as duplicates of one another or as re-files of items the round-9 brief lists as
accepted-and-open, deliberately not done, or refuted in rounds 5-8. Nothing was dropped for being
unreproducible: `reproducedLive` is 8 of 8 and `notRunnable` is 0.

What the verifiers *did* kill was parts of what survived, and that is where the value is: three
severities (all downward), one citation, and on five findings a proposed fix or interim. Two are worth
singling out because a naive reader will re-propose them.

| Proposed by the finder | Measured outcome |
|---|---|
| **I1**, part 3: a reconciler-level regression test (drift-only `plan.Items`, review-item descendant, `ApplyConfirmations` with no confirmations) | **Built and run against pristine HEAD: it passes.** `ApplyConfirmations` is correct; the defect is at the controller seam. The test pins nothing. |
| **I1**, interim: `[.. liveLinked, .. plan.ReviewItems.Select(i => i.SubnetId)]` at `AzureReconciler.cs:124` | **More expensive than the real fix.** Fails `AzureReconcilerTests.ApplyConfirmations_TargetWhoseDescendantIsAReviewItem_IsAlsoWithheld` at `:866` (`Assert.Single() Failure: The collection was empty`) because that test's arrange runs before `ApplyConfirmations`. |
| **I7**, fix: redirect `OnRemoteFailure` to `/Account/AccessDenied` | **Wrong destination for 3 of 4 triggers.** That page tells a user whose correlation cookie expired that their account lacks roles. Replaced with a dedicated `SignInFailed` page. |
| **I4**, interim: disable `#rec-scan-btn` for the scan's duration | **Premise false.** `$("#rec-subscription-select").on("change")` (`:127-129`) re-enables it, and the resulting variant is worse — the repainted table then describes a different subscription. Dropped. |
| **I3**, fix: hoist the tree read | **Works but incomplete on three counts** — does not compile as described (missing `using`), under-specifies the bulk sibling's append points, and leaves 2N in-memory passes. Extended. |

---

# Watch list — not findings, but worth knowing

Round 8's list, trimmed, plus what this round's verifiers established on the way past. Several items are
the *reason* a nearby defect is worth more than it looks.

### Carried forward

- **`DeletedSubnets` archives neither `AzureResourceId` nor `IsFullyAllocated`**, the deleted-subnets
  table renders neither `Tags` nor `OriginalParentId`, and **there is no restore path anywhere in the
  app.** Re-confirmed from the live schema this round: 14 columns, `Id … ModifiedBy`, zero matching
  `%Azure%`. This is what makes I1, I2, I4 and I5 unrecoverable.
- **"There is no test for this" is not a finding.** Still no `WebApplicationFactory`, no integration
  host, no JS test harness. That shape has been refuted in five consecutive rounds.
- **Entry gates are not row invariants.** A `Blocked` bulk-planner row, a refused `GET
  /Azure/Import/{id}` (*"must not have any child subnets or host IP assignments"*, which also fires for
  a subnet carrying host IPs), and a hidden Import-from-Azure button are all **expected** on a
  correctly imported subnet. This killed round 8's only refuted candidate, and I1's verifier had to
  clear it explicitly.
- **The purge POST does not require its confirmation page at all** — antiforgery tokens are
  per-session. By design; a different question from scoping (I5 is the GET's query order).
- **Do not re-file the purge lock gap**, and do not propose `ExecuteWithSubnetLockAsync` on the two
  purge POSTs: built and measured in round 8 to make a currently-correct ordering wrong, and round 6
  had already left it deliberately.
- **`_StepConfirm.cshtml` has no warnings block** — `#rec-scan-warnings` exists only in
  `_StepReview.cshtml`, so a scan warning never reaches the screen that performs the archive. Still a
  real gap, still deferred — and I1 sharpens it: in the drift-only case `plan.Warnings` is empty, so
  the block would render nothing. It is not a substitute for the guard.
- **The same click-time-versus-response-time split exists in `loadVNets`** (`_BulkScripts.cshtml`).
  Not filed: only the subscription *label* can disagree.
- **After H6's fix, `_SubnetCalculationScripts.cshtml`'s overlap arm has no remaining visitor.**
  Defence-in-depth for a case `findOptimalCidr` makes impossible — **do not tidy it away.**
- **`/Azure/BulkImportPreview` latency scales with `existing x selected`** — ~0.06 ms per (selected
  prefix x 1000 existing subnets): 39 ms at 20k/1, **7 247 ms** at 200k/600. A lock-free read endpoint,
  so distinct from I3.
- **`GlobalSanitizationFilter` runs after model binding and validation**, and
  `SanitizeObject` returns immediately for `typeof(string)` (`Filters/GlobalSanitizationFilter.cs:44`),
  so a top-level `string?` action parameter is never sanitized. That is how I6's `returnUrl` arrives
  raw. Any new `[Sanitize*]` attribute needs a matching validator.
- **`MockAzureService.DefaultConfirmation` is `Deleted`** — any test touching the confirmation path
  must set the verdict explicitly.
- **EF Core's `SqlServerDatabaseCreator.Exists()` misreads SQL 4060** the same way the bootstrap did;
  any fix in that `catch` must abort startup, not log. **F15 / the migration lock:** the lock opens the
  configured catalog first and falls back to `master` only on 4060 — do not re-propose an unconditional
  `master` scope. `Program.cs`'s crafted exception for a failed `master` open is effectively
  unreachable on SQL Server.
- **Accepted and unchanged:** ForwardedHeaders trust-all with `AllowedHosts: "*"`; the Development
  `DevAuthHandler` bypass; `CollectDescendants` without a cycle guard; the blind `catch { }` around the
  DataProtectionKeys probe; the DataProtection key ring persisted unencrypted; **C20**, the reconcile
  check/act window; the unreachable IP-change branch in `ValidateHostIpUpdate` (the one place applying
  the network/broadcast reservations without the `cidr < 31` guard — a trap for whoever makes that field
  editable).
- **Deliberately left, small:** the equality-vs-membership prefix check on the VNet-resource-id stamp;
  the bulk import reading only a multi-prefix Azure subnet's first prefix; `findOptimalCidr`'s loop
  bound and the six CIDR→mask copies across four files; `AnnotatePrefix` cannot return
  `AlreadyImported` (4 046 brute-forced outcomes); the three cheap test gaps; the eleven controller
  sites that `RedirectToAction` to `/Error/{code}` instead of answering in place;
  `HostIpController.DeletedHostIps(int subnetId)` binding `subnetId = 0` → `NotFound()`; migration
  `.Designer.cs` snapshots holding old column widths on purpose; the committed rig tenant ID at
  `Properties/launchSettings.json:41`; three expected CodeQL log-forging alerts on `main`;
  `Max Pool Size` unset everywhere; `Logging__LogLevel__*` outranking `BASTET_LOG_LEVEL_*`;
  `SaveTokens = true` with no scope gate; `success` not being uniform across the Azure AJAX endpoints;
  `AZURE_TOKEN_CREDENTIALS=dev` excluding `EnvironmentCredential`; ARM ids being path-based and
  surviving delete-and-recreate.
- **Rig hazards.** `pkill -f "Bastet.dll"` kills every instance on the box — match on `ASPNETCORE_URLS`
  or a captured PID. Headless Chromium never ticks `requestAnimationFrame`, so delete
  `window.requestAnimationFrame` before any animation assertion. jQuery 4.0.0 dispatches an aborted
  request's `error`/`complete` handlers **synchronously inside `.abort()`**, so any `.abort()`-based
  staleness interim is placement-sensitive. Several `bastet-visible` VNets share `10.20.0.0/16`.
- **Round-7 and round-8 line-number citations have already moved and will move again. Re-derive every
  line before citing it.**

### New in round 9

- **`FormOptions.ValueCountLimit` (default 1024) caps one `BatchCreateChildSubnets` post at 145
  children** — measured 145 accepted, 146 rejected with `400 {"":["Failed to read the request form.
  Form value count limit 1024 exceeded."]}` before any lock is taken (the wizard emits 7 hidden inputs
  per child plus 4 form fields plus the token). It does **not** apply to `BulkCreateFromAzurePlan`,
  which binds `[FromBody]` JSON. This is what bounds I3.
- **Nothing checks a cancellation token on the batch import.** A 145-child import committed in full
  after the client aborted at 120 s (Kestrel 499, 150 261 ms) — an operator giving up does not shorten
  the lock hold.
- **On a single replica the 30 s lock timeout expires in the in-process `SemaphoreSlim` `_localGate`**
  (`Services/Locking/SqlServerSubnetLockingService.cs:57`), not in `sp_getapplock`. Both throw
  `TimeoutException` and render the identical message; the `sp_getapplock` attribution only applies
  across replicas. `DEFAULT_TIMEOUT_MS` (`:23`) is a private const no call site overrides.
- **`Helpers.cs:214` is the only whole-table load re-issued per item inside the lock.** `Edit`'s
  `allOtherSubnets` (`Edit.cs:113`) and `Delete`'s tree read are once per request;
  `HostIpController`'s two `ToListAsync` sites (`:448`, `:521`) are read-only pages outside the lock.
  Reads are unaffected by a held lock (`GET /Subnet` 200 in 2.87 s during a 40 s hold).
- **.NET's `Url.IsLocalUrl` rejects only category-Cc characters** (`char.IsControl`), so U+2028 and
  every non-ASCII character pass. All header-injection shapes are correctly refused — checked byte by
  byte over `0x00`-`0x1F` and `0x7F`.
- **`OpenIdConnectHandler.HandleAccessDeniedErrorAsync` only redirects when `AccessDeniedPath` is set
  on the OIDC options.** The tree's only `AccessDeniedPath` is the **cookie** handler's
  (`Program.cs:200`) — the one-handler-over trap round 4's D38 documented.
  `/signout-callback-oidc`, `/signout-oidc` and their garbage-parameter variants all answer 200
  anonymously and throw nothing.
- **`ControllerAuthorizationTests` gates every `[AllowAnonymous]` action** against an allow-list that
  requires a reason. Any new anonymous action (I7's `SignInFailed`) fails the suite until it is listed.
- **`AzureReconcilerTests.ApplyConfirmations_TargetWhoseDescendantIsAReviewItem_IsAlsoWithheld`
  (`:866`) asserts `Assert.Single(plan.Items)` *before* calling `ApplyConfirmations`,** so any guard
  moved earlier into `BuildPlan` fails that arrange; relax it to `Assert.Empty` if that is ever done.
  And `SubnetFromOtherSubscription_Ignored` (`:457-467`) uses a standalone row with no ancestor, so it
  passes with or without I2's fix — it never exercises a multi-subscription tree.
- **`BulkCreateFromAzurePlan` performs no ARM read.** It re-plans against the database and trusts the
  posted ids (`SubnetController.BulkAzure.cs:100-121`, `:265-288`), and `FindDeepestContainer`
  (`AzureBulkImportPlanner.cs:329-349`) parents purely on address containment with no subscription
  test. That is how a foreign-subscription VNet gets nested under a local reservation (I2).
  Relatedly, `IsFullyAllocated` is settable by a plain `RequireEditRole` UI POST
  (`HostIpController.cs:723`), so no Azure path is needed to produce the row shape I1 destroys.
- **The reconcile wizard offers no way out during a scan:** `#rec-back-to-subscription-btn` lives
  inside the `d-none`'d `#rec-scan-content` and the step-1 pill is never disabled, which is why I4's
  double scan is an ordinary two-gesture action. A plain double-click on `#rec-scan-btn` yields only
  **one** request (`activateTab` moves the button out from under the cursor) — the reachable overlap
  path is Scan → step-1 pill → Scan.
- **`ExecuteDeleteAsync` in the purge POSTs reports what it actually deleted**, which is how I5's two
  numbers were caught disagreeing. Only two such purge sites exist in the tree.
- **Rig hazard, this round:** two verifiers found their *assigned* port and catalog already held by a
  live process from a colliding label. Check listeners and catalogs before starting, do not clobber a
  sibling's instance, and kill only by captured PID.
