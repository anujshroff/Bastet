# Bastet — Round-6 Audit Findings

**Target:** branch `main`, HEAD `8cefc64` "Audit 5 Cleanup (#142)" (all 14 round-5 fixes squashed).
**Test baseline:** 603 passing, 0 failed. `dotnet build --no-incremental` clean, 0 warnings.
**Date:** 2026-07-26

## Reconciliation — complete

**All 18 findings fixed, none refuted on re-verification**, plus the E8 restoration as step 0. One
commit each, on `task/audit-6`.

**Final state:** 635 passing (603 → 635, +32), 0 failed. Clean rebuild from deleted `bin`/`obj`,
0 warnings. Working tree clean, no scaffolding committed, no credential in any commit.

**Closing sweep.** Every major area was requested from the real application running against a real SQL
Server 2022 container, asserting titles and content rather than status codes: home, subnet hierarchy,
create, details, edit, deleted subnets, all-deleted-host-IPs, both Azure wizards, the reconcile wizard,
and the 404 page. Security headers ride on both a normal 200 and the 404 (`X-Content-Type-Options`,
`Referrer-Policy`, `Content-Security-Policy: frame-ancestors 'none'`, `X-Frame-Options`). One classified
rather than glossed: `/HostIp` 404s because there is no index action — host IPs are reached through a
subnet.

**Six fixes were confirmed live through HTTP, not just by test:**

| | |
|---|---|
| F1 | a crafted CIDR change on an Azure-linked row is refused with the new message; the stored CIDR stays 16 |
| F1 | the Edit form renders `Cidr` as a hidden input plus the "Imported from Azure" note |
| F5 | the `parseJSON` shim is served between jQuery 4.0.0 and the two validation scripts |
| F6 | **the sanitizing formatter is actually selected at runtime** — the thing the unit tests could not prove |
| F9 | the prefilled name `prod-10.0.5.0-24` posts verbatim and succeeds (302) |
| F10 | a `/32` Details page renders **0** Create Subnet buttons; the `/16` renders 2 |

**F2's plumbing confirmed against real ARM:** every subnet in the inventory carries a populated
`ipv4AddressPrefixes`, deduplicated, and the IPv6-only VNet is still filtered out.

**The Azure surface was driven end to end against live ARM**, with the discrimination check that
matters — a reconciler that blocks everything is as broken as one that deletes everything. Two service
principals with disjoint scope, probed rather than assumed: SP_A sees `bastet`, SP_B sees
`bastet-hidden`.

| Linked row | Azure reality | Result |
|---|---|---|
| `invisible-link` | 403, resource group not visible | **withheld**, warning names it; force-through refused 409, nothing deleted |
| `really-gone` | 404, genuinely absent | **offered** `VNetDeleted`, committed and archived (1 archived) |

Afterwards `invisible-link` is still live and only `really-gone` is in `DeletedSubnets`. The 409's
warning carries **F12's new wording** — "Azure denied access when asked about them directly" — so that
fix is confirmed live too.

**Log:** 2,255 lines, **zero `crit:`, zero ESC bytes**, one `fail:` and six `warn:`, every one
classified. The `fail:` is my own deliberate log-forging probe, and its rendering *is* F6's evidence:
the crafted escape sequence appears as literal text in both the message and the exception line. Three
`warn:` are `Azure denied access to …vnet-hidden (403), so it cannot be reported as deleted` — the
deliberate permission probe working, logged by design. The other three are environmental and
pre-existing, both recorded by round 5: DataProtection has no XML encryptor for a local run, and EF
advises on `QuerySplittingBehavior` for a multi-`Include` query.

**Coverage was not re-run.** This round deleted no code — F14 renamed a function and F18 added a
helper — so there is no dead-code delta to compare against a reference sweep.

**Deliberately not done**, each argued in the struck entry that owns it:

- the ARM-based prefix-equality check at `SubnetController.Azure.cs:320` (F1) — it needs a network
  round-trip inside a transactional write, far more invasive than a crafted-post-only defect warrants;
- `Guid.TryParse` before the ARM calls (F6) — the parsing is cheap but the failure semantics differ per
  method, and conflating "bad identifier" with "no VNets" is the distinction round 5's E-series was
  about;
- the bulk import still reads only a multi-prefix subnet's **first** prefix (F2) — closing that means
  creating several Bastet subnets from one Azure subnet, a feature change;
- `findOptimalCidr`'s loop bound (F10), the `site.js` consolidation of six mask copies (F16), and the
  per-prefix "already imported" sentence (F11), which is correct for its one reachable case;
- the reconcile badge colour for `VNetPrefixRemoved` (F4) — a UI preference once the reason is rendered.

**Two round-5 entries were corrected** where this round disproved them: E5's reachability claim (a
normal browser does *not* block the submit — the library sets `novalidate`) and E6's claim that a SQLite
test was impossible. Both are appended as corrections rather than rewrites, so the original reasoning
stays readable.

## Verdict

**No critical findings. Two high, three medium, twelve low, one info.** 18 survived verification, 7
were refuted (28%).

The headline is **F1**, and it is the most serious thing five rounds have found: an ordinary,
validated CIDR edit on an Azure-linked subnet turns that subnet into a reconcile deletion candidate,
and committing archives it, its descendants and their host IP assignments while Azure is entirely
healthy — with **zero ARM reads**. Three things make it worse than its predecessors:

- **It is a privilege escalation.** An `Edit`-role account is refused `/Subnet/Delete/{id}`,
  `/Azure/ReconcileScan` and `/Subnet/BulkDeleteStaleAzureSubnets` with 403 — and can still set the
  trap that makes an Admin archive a subtree. Measured, with a role matrix.
- **The collateral is not Azure data.** A measured run archived 5 subnets and 3 host IPs, of which
  **2 subnets and 2 host IPs had no `AzureResourceId` at all** — hand-created, never imported.
- **The archive is lossy in exactly the wrong column.** `DeletedSubnets` has no `AzureResourceId`
  and no `IsFullyAllocated`. The column whose disagreement with Azure caused the deletion is the one
  the archive discards, and there is no restore path anywhere in the application.

**F1 is not a regression from round 5.** Two finders framed it that way and both were wrong. A build
from `6d0e8c4~1` — before round 4's D3, where `ApplyConfirmations` does not exist — reproduces the
archival. The defect is original to the reconcile feature (`aedd0bd`, #133). D3 masked it as a side
effect for one release-range and **E1 correctly un-masked it. E1 must stay.**

**F2** is the same class from the Azure side and independent of F1: the subnet-level prefix check
tests equality where the VNet-level check ten lines above tests membership. Multiple IPv4 prefixes
per subnet went **GA on 2025-09-04**, so this is reachable on any current subscription without a
preview flag.

**F3** is the other way a healthy resource gets archived: a stored `AzureResourceId` that names
something other than an Azure subnet is answered by a read of a *different* resource, and a 404 there
reads as "Azure confirms it is gone".

After that it is a long tail of messaging, diagnostics and affordance defects, plus **F5** — client-side
validation is dead across the whole application, which also falsifies a reachability claim in round 5's
own record.

Read in this order: **F1**, **F2**, **F3**, then **F5**.

## Reconcile step 0 — restore the E8 entry to `docs/AUDIT-FINDINGS-5.md`

_Done and committed. The 28-line `E8` section was recovered from `8a2223a` and reinserted into
`docs/AUDIT-FINDINGS-5.md`, which now carries 14 `E` entries again and no longer cites a section it
does not contain. The restored range is byte-identical to `8a2223a`'s copy, verified by `diff`, and
the change is `28 insertions, 0 deletions` — purely additive, nothing else in the file touched._

_**This entry's own instruction was wrong and was not followed.** It said to reinsert the section
"immediately ahead of `## E7`, its original position, not in numeric order", reasoning from the
entry's own sentence "Taken out of numeric order, ahead of E7… E7 follows immediately". That sentence
describes the order the **fixes** were done, not where the text sat: at `8a2223a` the E8 heading is
at line 330 and E7's at 306, so E8 was always **after** E7, in ordinary numeric order. E7 was still
an unstruck finding at that point and E8 a struck one, which is what "E7 follows immediately" refers
to — the next thing to be worked, not the next section. Restoring it ahead of E7 would have moved it
somewhere it never was. It is back between E7 and E9, where `3c805c1` deleted it from._

_The deletion mechanism is worth keeping: `3c805c1` replaced E7's finding text with its struck
paragraph, and the replacement hunk ran on past E7's own text and swallowed the whole neighbouring
entry — `-62/+32` on a file that should only ever grow. **A fix commit that also edits the findings
file must diff its own hunk before committing.**_

_No `F` number, no code change, no test delta: 603 → 603._

## How this audit ran

Eight finder beats — security/web, logic & data integrity, Azure, locking & lifecycle, UI/client-JS,
regression-correctness, regression-tests, dead code — **run twice** with fresh agents and no knowledge
of each other, then every surviving finding handed to an independent verifier instructed to **refute**
it and to default to "not real" when uncertain.

**32 agents, 0 errors:** 1 rig builder, 16 finders, 15 verifiers producing 28 verdicts over 25
findings. Findings are tagged:

- **[×2]** — found independently by both passes. Strongest signal.
- **[×1]** — found by one pass only. *Not weaker in truth* — it means a full pass missed it, so it
  deserves **more** scrutiny during reconciliation, not less. **Absence is weak evidence.**

Pass 1: 16 reports. Pass 2: 18 reports. Merged and de-duplicated to 25, of which 18 survived.

Two calibration notes the tags alone would hide:

- **F10 was found by three pass-2 beats and by none in pass 1.** It is tagged `[×1]` by the rule.
  Three independent agents in one pass outweigh the other pass's silence.
- **F5 was found by two beats and missed by a third working the same area.** Pass 2's UI beat swept
  jQuery 4 for removed APIs and scoped itself to the app's own scripts, none of which render
  `_ValidationScriptsPartial.cshtml`. Both were right within their scope. That is the honest measure
  of what one beat's silence is worth.

**Three things distinguish this round.**

**The live rig entered the commit path.** Round 5 drove Azure read-only and deliberately never
archived anything. This round used two service principals with disjoint RBAC scope over two resource
groups, ARM strictly read-only, plus a throwaway SQL Server 2022 container and the real application —
and drove reconcile scan *and commit* to completion against a local database. F1, F3 and F12 are
`confirmed (live)` because of it, and so are the strongest clean-bill entries: **a withheld row cannot
be forced through the commit — 9 of 9 attempts refused, database byte-identical before and after each
one**, including the case that matters most, a deletable row mixed with a withheld one (409, and the
deletable row was not archived either).

**Verification changed the answer, not just the confidence.** It killed **four proposed fixes** by
measurement — the containment fix for F1 (misses the widening half, and re-creates E1 where it does
bite), clearing `AzureResourceId` on edit (irreversible; reconcile goes permanently blind),
`ResourceIdentifier.TryParse` for F6 (does not filter control characters), and deleting the migration
lock for F15 (trades a deterministic crash for a racy one and silently drops a 300s wait to 30s). It
also corrected the provenance of F1 and F4, downgraded four severities, upgraded one, and found the
privilege escalation in F1 that no finder saw.

> **Correction (v3.3.1).** One of those four killed fixes should not be counted as a win: the
> replacement verification chose for F15 broke production. Verification measured the bootstrap case
> it cared about and never measured the steady-state one, while explicitly overruling the finder's
> warning about it. See the correction block on F15. Measurement beats reading only where the
> measurement covers the path that matters.

**Every finding was measured, not read.** Rigs included a recording `HttpMessageHandler` capturing the
Azure SDK's real wire requests, `pyte` as an independent VT100 emulator for F6, a role matrix built
with the dev-auth stub as the only changed file (verified by per-file `cmp`), instrumentation of
`$.fn.on` and `addEventListener` before the shipped script ran for F14, 4,046 brute-forced planner
outcomes for F11, ~6,100 allocation layouts and 6,950 random write operations against nine tree
invariants for the arithmetic clean bill, and real trusted `Input.dispatchMouseEvent` clicks on real
rendered pages for F5.

I re-checked every citation in this file against the working tree by hand. Where a finder's line
number was wrong it is corrected here and noted.

---

# High

## F1. A legitimate CIDR edit on an Azure-linked subnet makes reconcile archive it and its subtree `[×2]`

_F1 is fixed and committed. `SubnetController.Edit`'s POST now refuses a CIDR change on any subnet
carrying an `AzureResourceId`, inside the same lock and beside the other CIDR guards, using the
database value rather than the posted one. The Edit form renders the field read-only for those rows
with a sentence saying why, and `EditSubnetViewModel.IsAzureLinked` is **re-derived from the database
on every render, including the error re-render**, so a post cannot claim a row is unlinked to get the
editable field back. The server is authoritative; the view only stops the operator typing._

_Refusal was chosen over the three alternatives the finding records, and the finding's own analysis of
why is correct: the two reconciler-side fixes were measured to fail (containment misses the widening
direction entirely, and re-creates E1 where it does bite), and clearing `AzureResourceId` on edit is
irreversible - reconcile goes permanently blind to the row and re-import is refused. Refusal touches
no reconcile code at all, so **E1 is untouched and genuine Azure drift stays reportable and
deletable**, which is the constraint that killed the others._

_Five tests written first, all against existing xUnit infrastructure. Three failed against the unfixed
action for the defect's own reason: both CIDR-change cases returned `RedirectToActionResult` instead of
the form - `Assert.IsType() Failure: Value is not the exact type` - meaning the edit **succeeded**, and
the GET flag was false. Both directions are covered (`/16`→`/17` narrowing and `/16`→`/15` widening)
because the finding records that a containment-based fix closes only one of them. The other two tests
are the guards against over-correcting and passed throughout: renaming, re-describing and re-tagging a
linked subnet must keep working, and an **unlinked** subnet must keep its editable CIDR - without that
second one, a fix that simply froze every CIDR would pass._

_**The equality check at `SubnetController.Azure.cs:320` was deliberately not implemented**, and this
is the one place the finding's stated fix was not followed. Checking that the parent's prefix is one of
the VNet's requires the VNet's prefixes, which means an ARM read: `SubnetController` has no Azure
service injected, and adding one would put a network round-trip inside a transactional write path so
that a temporary ARM failure turns a working import into a failed one. That is far more invasive than
the defect warrants - the verifier graded that leg low on its own, since `GetCompatibleVNets` filters
the wizard's dropdown to exact prefix matches and only a crafted Admin post can reach it. It is carried
on the watch list instead, where it belongs beside F13 (the same endpoint's missing guard) and the
linked-prefix column, which subsumes it: once a row records the prefix it was linked at, a mismatched
stamp records its own value and the reconciler stops misreporting it._

_Not re-measured against live ARM: the reconciler is unchanged by this fix, and the audit's live
measurement of the defect stands. What changed is that the state can no longer be created._

_Tests 603 → 608 (+5). Build clean, 0 warnings._

---


## F2. The subnet-level prefix check tests equality where the VNet-level check tests membership `[×1]`

_F2 is fixed and committed. `EvaluateSubnetLevel` now tests membership over every IPv4 prefix the
Azure subnet owns, exactly as the VNet-level check ten lines above always did, and the reason text
names all of them rather than the first. `GetVNetInventory` populates a new
`BulkAzureSubnetViewModel.Ipv4AddressPrefixes` from a new `ExtractIpv4Prefixes`, deduplicated because
ARM may report a single prefix in both the singular property and the collection._

_**The finding's plumbing instruction was wrong** and was not followed. It says the list must be
carried "through `AzureLinkedSubnetSnapshot`" — that is Bastet's own row, which has exactly one
prefix and is correct as it stands. The collapse is on the **live** side, so the list belongs on the
inventory view model. `AddressPrefix` was left in place and unchanged rather than replaced: about
twenty planner sites read it, and the import path genuinely can carry only one prefix because it
creates one Bastet subnet per Azure subnet._

_**A second site was fixed that the finding does not mention.** The `FullyAllocatingSubnetDeleted`
check at `AzureReconciler.cs:227` reads the same collapsed value to decide whether any Azure subnet
still covers the target's prefix, so a covering subnet listing another prefix first was reported as
having lost its cause. It is review-only and can never delete anything, which is why it is a footnote
rather than its own finding — but it is the same defect at its second site and the prefix list was
already to hand. Leaving a known-wrong sibling behind is the residue these rounds keep finding._

_**The finding's "second consequence" is not fixed and its "same fix" claim is wrong.**
`GetVNetInventory` still offers a multi-prefix Azure subnet to the bulk import wizard as though it
owned only its first prefix. Closing that means creating several Bastet subnets from one Azure
subnet, which is a feature change, not a bug fix, and is out of scope here. What this change does buy
is the data: the prefixes are now carried on the inventory model, so whoever takes it does not have
to re-plumb ARM. Recorded on the watch list._

_Three tests written first; all three failed against the unfixed reconciler for the defect's own
reasons — two with `Assert.Empty() Failure: Collection was not empty` (a row flagged for a subnet
that still owns Bastet's prefix, and the review row at the second site), one with
`Assert.Contains() Failure: Sub-string not found` because the reason named only the first live
prefix. The middle test is the guard against over-correcting, and it is the one that matters after
E1: a genuine prefix change, where **none** of the live prefixes match, must still be flagged
`SubnetPrefixChanged`. A fix that merely stopped flagging multi-prefix subnets would pass the first
test and re-create exactly the over-blocking E1 was about._

_Still `plausible` on one point, unchanged by the fix and not claimable: nobody has established which
index ARM assigns a newly-added prefix, because settling it needs an ARM write and this round's
credential was read-only. The fix does not depend on the answer — membership is correct whatever the
order — which is the argument for making it regardless._

_Tests 608 → 611 (+3). Build clean, 0 warnings._

---


# Medium

## F3. `ConfirmOneAsync` reads a different Azure resource, and its 404 reads as "confirmed deleted" `[×1]`

_F3 is fixed and committed, at both ends. `AzureResourceIdentity` gained `IsAzureVNet`, so the
question "is this a VNet?" can now be asked instead of inferred from "not a subnet".
`ConfirmOneAsync` establishes the type **before** reading and returns `Unknown` for anything that is
neither, without issuing an ARM call at all. And `BuildPlan`'s routing is three-way rather than
two-way: an unrecognised ID no longer falls down the VNet branch, where absence from the listing
reads as `VNetDeleted`. It becomes a new review-only status, `UnrecognisedResourceId`, telling the
operator to correct or clear the link._

_Both halves were applied deliberately, and the reconciler half is the load-bearing one: it means
such a row never reaches the confirmation path, so the `ConfirmOneAsync` guard is defence in depth
rather than the only barrier. That also answers the verifiers' sequencing worry - because the row is
routed to review rather than withheld, it never reaches the credential-blaming warning that **F12**
is about, so this fix does not make that message more common in the interim._

_Three theory cases plus a guard, all reconciler-level against existing fixtures. The three failed
against the unfixed code with `Assert.Empty() Failure: Collection was not empty` - the resource-group
ID, the storage-account ID and the VM ID whose last segment matches a live VNet were each **offered
for deletion**. The guard passed throughout and is the one that stops this over-correcting: a real
VNet ID that is genuinely absent from the listing must still be offered as `VNetDeleted`, or the
reconciler stops doing its job._

_**The first version of those tests was wrong and had to be redone**, which is worth recording
because it would have proved nothing. They used a fabricated subscription ID, so `BelongsToSubscription`
skipped every row as out-of-scope and the tests failed with an empty **`ReviewItems`** rather than a
non-empty `Items` - a pass-looking failure for an unrelated reason. Rewritten against the fixture's
own subscription constant, they fail where the defect actually is._

_**No badge case was added to the client-side `statusLabel` switch**, deliberately. That switch only
renders rows in the deletable table, and `UnrecognisedResourceId` can never appear there; the review
table renders each row's reason instead. Adding an unreachable case is precisely the residue these
rounds keep finding._

_The review section's banner was reworded because this widens what `ReviewItems` can hold: it said
"These subnets still exist in Azure", which is established for `FullyAllocatingSubnetDeleted` and
**not** for an unrecognised ID. It now says nothing here can be fixed by deleting anything and points
at the reason column, which each row already renders._

_**The refuted parse-guard finding's tidy-up was folded in here**, as its refutation recommended. The
`try`/`catch` around `new ResourceIdentifier` is gone in favour of `ResourceIdentifier.TryParse`,
which returns false rather than throwing: the catch could never run, because the constructor throws
only for the empty string and `ConfirmResourcesAsync` already filters that. The two comments stating
the false premise - that the constructor throws on malformed input - are corrected rather than left
to mislead the next reader. This also removes one unsanitized `ex` from a log statement, which
overlaps with **F6** and does not replace it._

_Not re-measured against live ARM: the audit's measurement stands, the classifier change was checked
offline against every ID shape the wizards write, and this round's credential was read-only and has
since been revoked._

_Tests 611 → 615 (+4). Build clean, 0 warnings._

---


## F4. The reconcile screens state that drift rows no longer exist in Azure `[×2]`

_F4 is fixed and committed. Both sentences now say the rows **no longer match Azure**, which is true
of an absence row and a drift row alike, and the confirmation screen renders each row's own reason
underneath its list entry. That second half is the important one: step 2 already printed the reason
beside the checkbox, but step 3 - the last screen before the archive, and the one carrying the
type-`approved` gate - showed only the status label, so a row whose reason says the Azure resource
still exists was confirmed under a heading saying it did not._

_**The finding's warning against a bare reword was heeded.** Replacing the headline with "You are
about to delete N subnet(s)." would have removed the only statement of fact on that screen, which is
a net information loss for absence rows - the common case, where "the resource is gone" is exactly
what justifies the archive. Keeping a true statement and adding the per-row reason gives both._

_`_StepReview.cshtml:76` was **not** reworded here, as the finding directs - but it is no longer the
sentence the finding examined. F3 widened what `ReviewItems` can hold, so "These subnets still exist
in Azure" stopped being established for every row in that section and was generalised in F3's commit._

_The badge colour was left alone. The finding offers a distinct colour for `VNetPrefixRemoved` as an
optional aggravator fix; with the reason now on both screens, recolouring is a UI preference rather
than a correctness matter, and an unrequested visual change riding along in a fix commit is the
residue these rounds keep finding._

_Verified in chromium against the pinned jQuery 4.0.0, with the shipped script read from the repo at
run time and its three `@Url.Action` expressions substituted programmatically rather than retyped.
The confirm-list block was lifted by offset, not rewritten, and run against a drift row:_

```
"parses": "OK"
"listText": "vnet-visible (10.10.0.0/17) - Prefix removed
             VNet 'vnet-visible' still exists but no longer has the address prefix 10.10.0.0/17."
```

_The parse check is not ceremony: a syntax error inside a `.cshtml` script block is invisible to the
C# compiler and to the test suite, and surfaces only when the page is requested._

_No test ships. There is still no JS harness in the repo and no `WebApplicationFactory`, which the
watch list records; the rig was ephemeral and is deleted._

_Tests 615 → 615 (unchanged). Build clean, 0 warnings._

---


## F5. jQuery 4 breaks client-side validation on every validated form `[×2]`

_F5 is fixed and committed with the one-line shim: `_ValidationScriptsPartial.cshtml` restores
`jQuery.parseJSON = JSON.parse` before the two validation scripts load, guarded so it is a no-op on
any jQuery that still ships the function - including a later rollback to 3.7.1._

_The shim was chosen over pinning jQuery back to 3.7.1, and the reason is that it is **sufficient**
rather than merely smaller: the verifier established that `$.parseJSON` is the only jQuery 4 removal
this library chain actually touches, that `jQuery.validator.unobtrusive.options` is null so the
second removed function is short-circuited before it is reached, and that the app's own scripts use
no removed API at all. Pinning back would revert a deliberate upgrade (`dcd50c2`, #82) to work around
one missing alias. The rollback remains available and the SRI hash it needs is recorded below, since
it **cannot be recovered from git** - that commit predates the integrity attributes._

```
sha384-1H217gwSVyLSIfaLxHbE7dRb3v4mYCKbpQvzx0cegeju1MVsGrX5xXxAvs/HgeFs
```

_Measured in chromium against the artefacts `_Layout.cshtml` and this partial actually pin - jQuery
4.0.0, jquery-validation 1.21.0, jquery-validation-unobtrusive 4.0.0, all fetched from the same CDN
URLs - with the shim lifted verbatim out of the shipped partial rather than retyped:_

```
                          unfixed                                    fixed
submit reached browser    true                                       false
threw                     TypeError: s.parseJSON is not a function   null
message                   ""                                         "Please enter a value less than
                                                                      or equal to 32."
novalidate                novalidate                                 novalidate
```

_**The first version of that measurement was wrong and was redone.** It reported the form posting in
*both* columns, because the recording listener was attached before jQuery's own submit handler and
cancelled the event itself - so it measured "a submit event fired", not "the submit reached the
browser". Re-instrumented to attach last and read `event.defaultPrevented`, which is the question that
matters. `novalidate` is present in both columns, which is the point: the browser's own gate is off
either way, so the shim is load-bearing rather than belt-and-braces._

_**Round 5's record was corrected**, which the finding explicitly asks for. E5's struck entry in
`docs/AUDIT-FINDINGS-5.md` argued its reachability was narrow because "a normal browser blocks the
submit even with JavaScript disabled" and "the vector is a crafted POST". Both are false: the library
sets `novalidate`, and an ordinary Edit-role user reached it by typing a value and clicking Save. A
correction paragraph is appended there rather than rewriting the entry, so the original reasoning and
its refutation both stay visible._

_Scope: four views, not the five both finders listed. `HostIp/Delete.cshtml` has no `data-val`
attributes, so `.validate()` is never called on it and its confirmation box is still gated by the
browser's own `required`._

_No test ships; there is still no JS harness in the repo. The rig was ephemeral and is deleted._

_Tests 615 → 615 (unchanged). Build clean, 0 warnings._

---


# Low

## F6. Every `LogError(ex, …)` on the Azure request paths logs the exception unsanitized `[×2]`

_F6 is fixed and committed with the systemic fix: a new `SanitizingConsoleFormatter` runs every line
it writes - the rendered message **and** `exception.ToString()` - through `LogSanitizer`. It is
registered **unconditionally**, deliberately outside the `if (!builder.Environment.IsDevelopment())`
block, because that block is the only place the console sink was configured and a developer's console
deserves the same protection as a production one._

_The sink was chosen over the alternatives for the reason the finding gives: it covers all 28
`LogX(ex, …)` sites at once, including the two at `AzureController.cs:130` and `:168` that log the
same exception a second time and which **no static analyser flags**, because their template arguments
are integers. Sanitizing `ex` at each call site was rejected - 28 sites to keep in step, and it
destroys the structured exception that round 5's E4 fix went to trouble to preserve._

_**The load-bearing design detail: lines are split before they are sanitized, never after.** A
newline is a control character, so running an exception through the sanitizer as one string would
collapse its stack trace onto a single line. Splitting first means legitimate structure comes from
the formatter and only the content is scrubbed. A test pins exactly that - a real caught exception
with an inner exception must still arrive as four or more lines with its `at …` frames intact._

_The formatter emits no ANSI colour, unlike the default simple formatter it replaces. That is a small
deliberate behaviour change and it is the consistent choice: a formatter whose purpose is to keep
escape sequences out of the log has no business writing its own._

_Four tests ship, against existing xUnit infrastructure - this is a plain class, so unlike most of
this round's client-side findings it can be pinned properly. They could not "fail first" in the usual
way because the class is new, so the sanitization was **reverted in place and the suite re-run**:
`EscapeSequenceInTheMessage_IsStripped` and `EscapeSequenceInTheException_IsStripped` both fail, while
the multi-line and tab tests keep passing - which is the point of having them, since they are the
guards against a fix that sanitizes so hard it destroys the log's structure._

_**The finding's secondary leg - `Guid.TryParse` before the ARM calls - was deliberately not taken**,
and this is a departure worth stating. It is described as cheap, and the parsing half is; the
semantics are not. `GetVNetInventory` reports failure as `Success=false` with a message, but
`GetCompatibleVNets` **throws** and returns `[]` only for an empty id - so rejecting a malformed id
there means choosing between throwing a different exception type and returning an empty list, and the
empty list would conflate "you gave me a bad identifier" with "this subscription has no VNets". That
is precisely the distinction round 5's E-series was about, and picking it while nominally fixing a
log-forging defect is the kind of unrequested behaviour change that rides along in a fix commit.
Recorded on the watch list. The security defect is closed regardless: it is the sink that was
vulnerable, not the parser._

_**Operational note for the three CodeQL alerts, which are open on `main`.** Expect them to stay open
after this commit. CodeQL's flagged flow is action parameter → `SanitizeForLog(x)` → `LogError`
argument, and it does not model `LogSanitizer.SanitizeForLog` as a sanitizer - which is why they are
open today despite the sanitizer already existing. Comment them with this commit, leave them open,
and dismiss only after shipping a CodeQL sanitizer model. Dismissing them now would put an untrue
statement in the security record: the wrong output was real._

_Still to confirm in the closing sweep: that the console provider actually **selects** this formatter
at runtime. The unit tests prove the formatter sanitizes; they do not prove the wiring, which only a
running application shows._

_Tests 615 → 619 (+4). Build clean, 0 warnings._

---


## F7. A JSON `null` for a collection returns an unhandled 500 past the subnet lock `[×2]`

_F7 is fixed and committed at all four sites the two finders listed between them. The reconcile
commit now tests `request.SubnetIds is null or { Count: 0 }`, the same pattern
`SubnetController.Azure.cs:130` already used, and the planner guards its list, its entries and each
entry's `Subnets` collection. Entries are guarded as well as collections because a null **element**
arrives from the body exactly as easily as a null list._

_A null `Subnets` collection is treated as "no subnets", not as an error: a VNet prefix selected with
no Azure subnets under it is a legitimate selection and the target is still created. A null list or a
null entry is reported through `GlobalErrors`, which is the channel the wizard already renders._

_Three tests, and they fail against the unfixed planner with `System.NullReferenceException : Object
reference not set to an instance of an object.` - the defect verbatim. Proven by reverting the guards
in place and re-running rather than by reasoning about them._

_**One consequence to state rather than let a reader discover.** Fixing this in the planner also
changes `/Azure/BulkImportPreview`'s answer to the same body: it used to return HTTP 200 with
`{"success":false,"error":"Failed to build the import preview…"}` because the exception was caught
there, and now returns `success:true` with a plan carrying `globalErrors`. That is the better shape -
the operator is told which selection was empty rather than that the preview failed - but it is a
behaviour change on a second endpoint and it should not be a surprise._

_The finding's cheaper interim - widening the action's `catch (TimeoutException)` to `Exception` - was
not taken, as both finders recommended: it would leave the request classified as a server fault when
it is a malformed request._

_The lock was already correct and is untouched. Both finders measured zero lingering `APPLICATION`
locks after the failing request, with 19 acquires and 19 releases in the log._

_Tests 619 → 622 (+3). Build clean, 0 warnings._

---


## F8. Two bulk-commit failures render a red panel reading only "Commit failed:" `[×2]`

_F8 is fixed and committed at both ends, which the finding is right to insist on. The two
`BadRequest(ModelState)` returns now answer with the wizard's own contract - `error`, `globalErrors`,
`itemErrors` - carrying `ModelStateMessage(...)`, so the operator sees the validator's own sentence.
And `showCommitError` gained the `|| "The import failed."` floor its reconcile sibling always had,
which is the only protection against a body this handler does not recognise._

_Both halves were needed and neither substitutes for the other: the contract fix handles the two known
returns, the fallback handles every unknown one. Verified in chromium with the shipped script read
from the repo, its four `@Url.Action` expressions substituted programmatically, and `showCommitError`
lifted by brace-matching rather than retyped:_

```
parses      : OK
fixed shape : "'10.0.0' is not a valid IPv4 network address. Use dotted-quad notation with no
               leading zeroes (e.g. 10.0.0.0)."
unknown body: "The import failed."          <- was "" before
```

_The finding's preferred source for the message was taken - `ModelStateMessage`, the server's own
words - over reflecting `item.VNetPrefix` back. That value is raw caller-controlled text the
`GlobalSanitizationFilter` never descends into; it is rendered with `.text()` so there was no XSS, but
echoing the validator is both safer and more useful._

_**My first rig was wrong twice and had to be redone**, recorded because it is the sort of failure that
quietly produces a green result: it first sliced the function by searching for the next `function`
keyword, which cut mid-body and threw `Illegal return statement`, and the run before that swallowed
the error entirely and printed nothing at all. Only after adding the error handler and switching to
brace-matching did it measure anything._

_No test ships. The two returns sit inside `BulkCreateFromAzurePlanCore`, which needs a database and
the subnet lock to reach, and the panel itself has no JS harness; the verifier confirmed no existing
test asserts the old `SerializableError` shape, so nothing had to be rewritten. Reachability is a
crafted admin POST - a non-canonical but parseable prefix such as `10.0.0/24` for the target path, and
`10.50.256/24` for the child path - with the blast radius confined to the crafter's own screen._

_Tests 622 → 622 (unchanged). Build clean, 0 warnings._

---


## F9. The prefilled subnet name always contains a `/`, which `[SafeText]` forbids `[×1]`

_F9 is fixed and committed: the generated suffix is now `-{networkAddress}-{cidr}` rather than
`-{networkAddress}/{cidr}`, so the name the app fills in survives the validation the very next POST
applies to it._

_The separator was changed rather than the rule, as the finding directs. `[SafeText]` guards three
properties and round 5's E2 deliberately declined to widen it; loosening a character class to
accommodate a string the app generates would be fixing the wrong end. `SubnetNaming.WithSuffix` was
left alone - it has two callers, and the planner's own suffixes are already inside the class._

_A new theory test pins the real contract rather than the literal string: **the prefilled name must
pass the rule its own POST applies.** It resolves `SafeTextAttribute` against a real
`InputSanitizationService`, because the attribute reads that service from the validation context and
without one every rule fails with "Input sanitization service not available" and the test would pass
vacuously - the trap round 5's E2 fell into. Reverting the separator makes it fail with the message
that names the defect:_

```
the prefilled name 'Parent-10.0.1.0/24' is refused by the rule its own POST applies
```

_The `/32` case is included as a second theory row, because that page prefills a name too and the
finding records that there the Name error **masks** the CIDR error - so an operator got two rejections
in sequence. With F10 also fixed, neither fires._

_Two existing assertions pinned the unusable value and were updated, not deleted:
`SubnetCreateGetPrefillTests.cs:116` and `:142`. They were the reason two rounds walked past this -
round 4's D19 fixed this string's **length** and D8's struck paragraph explicitly reasoned about the
generated name, quoting `Parent-10.0.0.0/33` as a name "which the POST then rejects", while fixing
only the CIDR in it._

_Tests 622 → 624 (+2). Build clean, 0 warnings._

---


## F10. A `/32` subnet offers a Create Subnet button whose POST can never succeed `[×1]`

_F10 is fixed and committed with the view gate the finding recommends: the button is now conditioned
on `Model.Cidr < 32` alongside the existing role and host-IP checks. That removes the impossible state
rather than rendering it more politely - a `/32` has no room for a child at all, so there is nothing
for the modal to offer._

_The gate was chosen over correcting `findOptimalCidr`'s loop bound, which the verifier established
has exactly one observable case and this is it: the body cannot fail to run for any parent CIDR below
32, and its "everything overlapped" fallback is otherwise unreachable because the button only appears
on an unallocated range, where a `/32` at the start address cannot overlap anything. Fixing the loop
would leave the button rendered and the modal computing a range of 33-32._

_Being a `[×1]` the premise was re-checked rather than trusted, and it holds - but the reachability is
narrower than the three reports imply and that is worth recording: the button is already gated on the
`/32` having no host IPs, so the window is a `/32` created and not yet assigned its address. An
ordinary state, but a transient one._

_`/31` is deliberately untouched. Two of the three finders confirmed it is correct - the modal offers
32-32 and a `/32` child under a `/31` parent really is created - so widening the gate to `<= 31` would
break a legitimate operation._

_This also removes the second of two rejections F9 describes: on the `/32` page the prefilled name
error used to fire before the containment error, so an operator got two different refusals in
sequence. With both fixed, neither does._

_No test ships: the gate is a Razor condition on a view, and the repo has no view-rendering harness.
The final sweep requests a `/32` Details page against the running application, which is where this is
actually observable._

_Tests 624 → 624 (unchanged). Build clean, 0 warnings._

---


## F11. "Hide already imported" turns blocked prefixes into a green success banner `[×2]`

_F11 is fixed and committed. The empty-tree message is now an **info** alert that counts what was
suppressed and says how to see it - "N VNet prefix(es) in this subscription cannot be selected, and are
hidden. Untick \"Hide unavailable\" to see why." - and falls back to "This subscription has no VNet
prefixes to import." when nothing was hidden. The switch is relabelled **Hide unavailable**, which is
what it does._

_Pass 2's fix was taken over pass 1's, on the verifier's reasoning. Pass 1 proposed filtering on
`statusName === "AlreadyImported"`, which `AnnotatePrefix` never returns - that would have made the
switch a no-op, deleting the declutter it exists for and making the banner unreachable rather than
truthful._

_**The correction that explains why three rounds walked past this** is worth carrying: the banner was
*right* in the ordinary re-scan case, and neither finder saw why. `AzureSubnetSnapshotService` sets
`HasChildSubnets` from the tree, so a target that really was imported comes back
`Blocked("… already has child subnets. Already imported?")` - the **same bucket**, hidden by the same
filter, and there the sentence was true. The defect is that one `Blocked` bucket carries both the
intended case and the conflict cases, so the wording is now about availability rather than history and
cannot be wrong either way._

_Pass 1's second half was **dropped, not fixed**: the per-prefix "All subnets in this prefix are already
imported." at `:240` survives untouched, because the verifier could not construct a selectable prefix
holding a `Blocked` subnet, and for the reachable case - an `AlreadyImported` subnet - the sentence is
true. Changing a correct message on the strength of an unreachable scenario would be the wrong trade._

_Verified in chromium with the shipped script and its four `@Url.Action` expressions substituted
programmatically: `parses: OK`, the success-alert-asserting-a-prior-import is gone, the suppressed
counter is wired, and the "untick to see why" instruction is present. The alert level changed from
`alert-success` to `alert-info` deliberately - nothing succeeded, so nothing should be green._

_No test ships; no JS harness. `AnnotatePrefix`'s inability to return `AlreadyImported` was established
by the verifier over 4,046 brute-forced planner outcomes and is recorded on the watch list rather than
pinned here, since it is the planner's behaviour and not this fix's._

_Tests 627 → 627 (unchanged). Build clean, 0 warnings._

---


## F12. A row withheld for any non-403 reason is reported as a lost credential `[×1]`

_F12 is fixed and committed. `Unknown` now has its own bucket and its own sentence: "Azure could not be
asked about them - the read failed rather than answering. Nothing is wrong with the subnet itself; try
the scan again." `NotVisible` keeps a sentence naming the credential, now sharpened to say Azure
**denied access** when asked directly, which is what a 403 actually means. **The action is unchanged -
both are still withheld** - only the explanation stops guessing._

_The split was taken over the finding's cheaper interim of widening the existing sentence to cover both,
for the reason the verifier gives: widening makes the message vaguer for the 403 rows, where it is
currently correct and the operator's next step - check the role assignment on that resource group - is
the right one. A message that fits both cases helps neither._

_This is where the finding understates itself and the write-up should not: **`Unknown` needs no crafted
input at all.** An ARM throttle or a transport blip mid-scan produces it, so an operator could be sent
auditing role assignments on a subscription whose permissions are perfectly intact. That, not the 400
case, is why this was worth fixing._

_Three tests, all failing against the unfixed reconciler. Two fail on the defect - the `Unknown` row's
warning contained "lost access", and two rows withheld for different reasons produced **one** warning
instead of two. The third fails on the reworded 403 sentence rather than on a defect, which is worth
stating plainly: it pins the wording so a later round does not collapse the two messages back together._

_Sequenced deliberately after **F3** and **F6**, as both verifiers advised. F3 routes unrecognised
resource IDs to review rather than into this bucket, so it does not make this message more common; had
F12 landed first, F3's rows would have arrived under the credential explanation in the interim._

_The comment at `_ReconcileScripts.cshtml:428-431` - which says in as many words that this text exists
so the operator can tell "Azure would not confirm it" from "the credential lost access" - is now
accurate rather than aspirational. It was the strongest evidence the finding had, and it came from the
codebase itself._

_Tests 624 → 627 (+3). Build clean, 0 warnings._

---


## F13. `BatchCreateChildSubnets` is the one Azure write path with no feature-flag guard `[×1]`

_F13 is fixed and committed - **not** with the guard the finding proposed, because the verifier proved
that one does not close the gap. Gating on `isAzureImport` would leave the child stamp at `:367`
untouched, and it is behind no flag at all: measured, with `isAzureImport` absent entirely and the
feature off, `{"success":true,"subnetIds":[3]}` and the row carried a ghost subnet id. So the guard
tests **the Azure state being written**, not the caller's claim about it: `isAzureImport`, a non-empty
`vnetResourceId`, or any non-empty `subnets[].AzureResourceId`._

_**This narrows the documented non-Azure JSON API, and the commit says so out loud** rather than
leaving it to be discovered: a caller using this endpoint as a plain batch-create may no longer send
`AzureResourceId` or `vnetResourceId` while `BASTET_AZURE_IMPORT` is off. Sending them was never
meaningful in that configuration - the reconcile that would act on such a row cannot run - so what is
lost is the ability to create a row that is inert until someone enables the feature and it arms itself._

_Three tests, and the middle one is the important one: it posts a child `AzureResourceId` with
`isAzureImport` **absent**, which is exactly the path the finding's own proposed fix would have missed.
Both refusal tests fail against the unfixed action; the third passes throughout and is the guard
against over-correcting - a plain batch create carrying no Azure state must still work with the feature
off, or the fix costs more than the defect._

_**Twelve existing tests had to be moved behind the flag**, which is worth recording because it is
evidence of the defect rather than collateral: `SubnetControllerBatchCreateTests` and
`SubnetControllerFullyEncompassingTests` drove the Azure import path without ever setting
`BASTET_AZURE_IMPORT`, and passed - they were relying on the missing guard. Both classes now set it in
their constructor, clear it on dispose, and join `AzureFeatureFlagCollection`, whose whole purpose is
to serialise classes that flip this process-global variable. Any future test touching the flag belongs
there too._

_**My first version of the new tests was wrong and had to be corrected**: they asserted against parent
subnet 1, which the fixture seeds with two children already, so one failed with `Expected: 0 Actual: 2`
- a fixture error, not a code result. Repointed at parent 2, the childless `10.0.0.0/16` the file's
other tests use._

_The immediate wrong output the finding names is closed as a consequence rather than addressed
directly: with no Azure state written while the feature is off, `_SubnetDetails.cshtml` has no stamped
id to build a "View in Azure Portal" link from._

_This finding is **not** credited with closing F2 or F3, as the verifier insisted: both need the flag
**on**, so this guard never fires in their scenario. What helps them is validating the id's shape,
which F3 did._

_Tests 627 → 630 (+3). Build clean, 0 warnings._

---


## F14. The Create-Subnet modal unlocks a field nothing listens to, under a stale explanation `[×1]`

_F14 is fixed and committed with the finding's cheaper interim, which the verifier established is the
better fix: the field is never unlocked. `prop('readonly', false)` is gone, so the value the script
computed is the value that gets posted._

_Nothing is lost by leaving it locked, and this was checked rather than assumed:
`findCompatibleNetworkAddress` searches only within the parent's boundaries and skips every child, so
the address it writes is always inside the parent, aligned, and clear of siblings. The verifier's
warning against the finding's *preferred* fix was heeded - adding a listener means sharing a validator
with the `#cidrInput` handler, which rewrites this field from `#originalNetworkAddress` on every
`input` event and would overwrite what the operator is typing._

_**A stale symbol was cleaned up rather than left behind.** With the unlock removed,
`makeNetworkAddressEditable()` no longer made anything editable - a function whose name states the
opposite of what it does is exactly the residue these rounds keep finding. It is now
`markNetworkAddressAdjusted()`, and its single caller was updated. `makeNetworkAddressReadOnly()` keeps
its name because it still does what it says._

_The finding's anchor was corrected, as the verifier required: the false thing on screen is
`#networkAddressHelp` still reading "This network address has been adjusted to avoid overlaps" under a
value the script never adjusted - not the `is-valid` class, which sits on `#cidrInput` and was
accurate. With the field locked, that sentence is true whenever it appears._

_Verified in chromium: the script parses, no `readonly, false` remains, and the stale name is gone.
**My first rig reported a syntax error that was its own fault** - a regex meant to strip the Razor
`@foreach` swallowed part of the script and produced `Unexpected token 'var'`. Recorded because a rig
that blames the file for its own damage is the fastest way to "fix" something that was never broken;
re-done by replacing the Razor data literal precisely, from `@foreach` to the closing `];`, and
asserting no `@` survived._

_No test ships; no JS harness. The server was already correct here and remains untouched - it refused
an out-of-parent address with the containment rule named, which is why this stayed low._

_Tests 627 → 627 (unchanged). Build clean, 0 warnings._

---


## F15. `BASTET_AUTO_MIGRATE` cannot bootstrap a catalog that does not exist `[×1]`

> **CORRECTION - the fix recorded below shipped in v3.3.0 and broke production. Read this first.**
>
> Scoping the lock connection to `master` unconditionally made every `BASTET_AUTO_MIGRATE=true`
> startup open `master`. The production managed identity is a contained user in the application
> catalog with no login in `master`, so v3.3.0 died on startup before serving a request:
>
> ```
> Unhandled exception. Microsoft.Data.SqlClient.SqlException (0x80131904):
> Login failed for user '<token-identified principal>'.
>    at Program.<Main>$(String[] args) in .../src/Bastet/Program.cs:line 259
> Error Number:18456,State:1,Class:14
> ```
>
> **The finder was right and the adjudication below is wrong.** See the struck paragraph at the end
> of this entry. The rebuttal collapsed two distinct cases: EF's database creator opens a non-target
> catalog *only when the catalog is missing*. When the catalog already exists - every steady-state
> deployment - `Migrate()` never touches `master`, so the pre-fix code worked for a contained user
> and the fixed code did not. "Both have the same caveat" is true only of the bootstrap case, which
> is the one case F15 was actually about.
>
> Also note the 4060 filter never fired in production: a contained user hitting `master` gets 18456,
> not 4060, so the friendly rethrow this entry credits itself with was unreachable on the path that
> actually failed.
>
> Amended in v3.3.1: the lock now opens the **configured catalog first** and falls back to `master`
> only on SQL 4060, so the bootstrap behaviour verified below is retained while managed-identity
> deployments stop needing `master`. The catalog choice moved to `Bastet.Data.MigrationLockConnectionString`
> and now has unit tests - this entry's "no test ships" is what let the regression ship green.
>
> **Do not re-apply an unconditional `master` scope.** That is this finding's second incarnation.

_F15 is fixed and committed with the two-line change the adjudicating verifier found, not the deletion
the finding proposed. The `Bastet:Migration` lock connection is now scoped to `master` via
`SqlConnectionStringBuilder`, so it no longer needs the target catalog to exist before `Migrate()` can
create it - and a 4060 on that connection is caught and rethrown naming `master`, the login's need for
it, and `BASTET_CONNECTION_STRING`._

_Measured against a real SQL Server 2022 container, both directions:_

```
HEAD, missing catalog : Unhandled exception. SqlException: Cannot open database "bastet_f15"
                        requested by the login.  Error Number:4060,State:1,Class:11
fixed, missing catalog: Now listening on: http://127.0.0.1:5402
                        sys.databases -> 1 ; __EFMigrationsHistory -> 6
```

_**The finding's primary fix - delete `Program.cs:233-298` - was rejected, and the reasoning is the
substance of this entry.** Its premise was that the custom lock is redundant now EF Core 10 takes its
own `__EFMigrationsLock`. Half true: EF's lock does serialise two starts against an **existing**
catalog, but it does not cover `CREATE DATABASE` and it does not wait. Deleting the lock trades a
deterministic single-replica crash for a racy multi-replica one, and silently drops the 300-second wait
`README.md:125` promises to ADO.NET's 30-second default with an opaque timeout message. It would also
discard round 4's D12 fix, and - as literally written - delete both `Migrate()` calls with the lock,
leaving no migration at all._

_The master-scoped variant fixes strictly more, which is why it won: `CREATE DATABASE` now happens
**inside** `Bastet:Migration`, so the case EF's own lock cannot protect is protected. Verified with two
simultaneous cold starts against a missing catalog - the exact scenario that produces SQL error 1801
with the lock removed:_

```
5403: listening=1 unhandled=0 1801=0     __EFMigrationsHistory -> 6
5404: listening=1 unhandled=0 1801=0     (six migrations, applied once, one shared history table)
```

~~_The finding's stated reason for preferring deletion over this variant was also false and is recorded
so it is not repeated: it claimed the master variant "needs the login to be able to connect to master,
which a contained Azure SQL user cannot", while the primary fix "has no such caveat". Both have the
same caveat - EF's own database creator opens a non-target catalog to issue `CREATE DATABASE`, so no
login that cannot reach master can create the catalog by either route._~~

> **Struck - this paragraph is the defect.** The finder's objection was correct as stated and the
> rebuttal is not. The caveats are not the same: the primary fix needs `master` only when creating a
> catalog, the master-scoped variant needs it on every startup. Dismissing the objection is what put
> an 18456 crash into v3.3.0. What should have been recorded here is the opposite conclusion - that a
> contained Azure SQL user is the documented deployment model (`README.md`, "Database Setup", which
> asks only for database-level roles and never mentions `master`), so any fix requiring a `master`
> login on the steady-state path is disqualified regardless of what it does for bootstrap.

_Severity was adjudicated down from the finder's medium to low, and that grade is what this fix
reflects: the documented bootstrap is create-then-run (`README.md:31-33`, unchanged since the initial
commit) and the Docker quickstart ships `BASTET_AUTO_MIGRATE=false`. What was really wrong was an
unhandled `SqlException` on a connection the application opened itself, naming neither the catalog nor
the setting that asked for it. Two of three agents declined to raise it at all; the third was right
that a regression with a two-line fix is worth taking._

_No test ships: the scenario is process startup against a real SQL Server, which the SQLite suite
cannot reach and which no existing harness drives. The container was ephemeral and is destroyed._

_Tests 630 → 630 (unchanged). Build clean, 0 warnings._

---


## F16. An unguarded CIDR→mask copy makes the `/0` Create modal offer a subnet from elsewhere `[×1]`

_F16 is fixed and committed. Both unguarded copies in `_SubnetCalculationScripts.cshtml` now carry the
`cidr === 0 ? 0 : …` guard the other four copies of the same expression already had._

_Measured rather than reasoned: the shipped expression and the fixed one were lifted from the file and
from `HEAD` respectively - not retyped - and evaluated across every CIDR 0 to 32 in chromium. Exactly
one value differs:_

```
cidr 0 : HEAD 255.255.255.255  ->  fixed 0.0.0.0
```

_Every other CIDR is byte-identical, which is the property that made this safe to change: the fix
cannot alter any behaviour outside the one case it exists for._

_Both sites were guarded, but the write-up records what each buys, because the finding's own count was
wrong in both reports. The expression exists **six** times across five functions in four files, four
already guarded. Only `:202` is observably wrong - reached through `getSubnetBoundaries(parentNetwork,
parentCidr)` on a `0.0.0.0/0` Details page, where the wrapped mask let the modal enable Create with the
network address silently replaced by a block in a different part of the address space, and the server
would have accepted it. `:269` is **not reachable at `cidr === 0`** - its only caller constrains the
value to `parentCidr + 1` or more - so it is guarded for symmetry and claims no fix._

_The two agents who examined this disagreed and the verifier settled it: the "self-corrects" reading is
true only for a tree whose lower half is allocated, because then the overlap check catches the wrapped
address. With the lower half free and the second unallocated range clicked, it does not._

_The `site.js` consolidation both reports suggest was **not** taken. Six copies across four files is a
real duplication and it is on the watch list, but hoisting them is a refactor touching three pages to
fix a defect that lives in one expression._

_No test ships - there is no JS harness - and the guard is a client-side display path. The rig was
ephemeral and is deleted._

_Tests 624 → 624 (unchanged). Build clean, 0 warnings._

---


## F17. Round 5's stated reason for leaving E6 untested is false, and the fix is unpinned `[×2]`

_F17 is fixed and committed, in both halves the finding asks for. `docs/AUDIT-FINDINGS-5.md`'s E6 entry
carries a correction paragraph, and the test round 5 said could not exist now ships as
`SubnetControllerConcurrencyRedisplayTests`._

_**Only the false clauses were struck, not the paragraph.** Round 5's premise is true - `[Timestamp]
byte[] RowVersion` really is store-generated only on SQL Server - and it is the **inference** from it
that fails, because the Edit POST supplies the original token itself and the comparison becomes an
ordinary `WHERE … AND RowVersion = @posted` that SQLite evaluates fine. Rewriting the whole entry would
have destroyed a true statement to correct a false one; the correction is appended so the original
reasoning and its refutation both stay readable._

_The test is the probe pass 2's beat 7 preserved, renamed from `E6ProbeTests` to say what it pins
rather than which round found it, and re-documented. It passes at HEAD and fails on reverting both
`AsNoTracking()` calls with `Assert.Equal() Failure: Values differ` - so the exit path of every failed
Edit POST is now pinned._

_**The provider caveat is written into the test's own doc comment**, which the verifier specifically
asked for: under SQLite the stored token is `NULL`, so *any* non-null posted `RowVersion` conflicts. The
test reaches the handler faithfully but does not reproduce production's value-versus-value comparison,
and a later round must not read a pass here as proof of the SQL Server path. Without that note this
would look like a test passing for a provider artefact._

_This finding only ever cleared the bar on its documentation half - the coverage gap alone would have
been refuted, since HEAD behaves correctly - and the fix reflects that: the correction is the finding,
the test is the remedy. Recorded plainly because the distinction is what separated F17 from the three
test-coverage findings this round refuted._

_Tests 630 → 631 (+1). Build clean, 0 warnings._

---


# Info

## F18. Bulk import persists a subnet with an empty name `[×1]`

_F18 is fixed and committed. A `TargetName(prefix)` helper gives the auto-created target the same
empty-name fallback the child names four lines away always had, falling back to
`{network}_{cidr}` - so `192.168.0.0/16` becomes `192.168.0.0_16` rather than nothing at all._

_The planner-side fix was taken over the stricter alternative of rejecting a null or whitespace `Name`
in `ValidateSubnetCreation`, for the reason the finding gives: the preview then shows what the commit
will write. The verifier checked the stricter option is also safe - every in-code caller already
supplies a non-empty name - so it remains available if a later round wants belt and braces._

_All three target-name sites route through the helper, not just the two the finding cites. The third is
the `renameMatched` path, which proposed the same sanitized value as a rename: left alone it would have
renamed an existing subnet to nothing, which is the same defect wearing a different hat._

_Five tests: three theory rows for names that sanitize to empty - markup-only, whitespace-only and
empty - which fail against the unfixed planner, plus a guard that an ordinary name is still used
verbatim. Without that guard a fix that always used the prefix would pass._

_Graded **info** rather than low by the verifier, and the fix reflects that this is an invariant repair
rather than a harm repair: it went looking for something that breaks and found nothing - the tree row
stayed clickable with the address as its visible text, Details returned 200 with an empty heading, the
dropdown option was selectable, and the row archived cleanly. What justifies fixing it anyway is that
`EditSubnetViewModel` carries an explicit comment about this exact hazard - "StripHtml can empty a name
outright, defeating `[Required]`" - closed for both interactive models by round 5's E2 and left open on
the one write path with the same sanitizer output and no equivalent guard._

_Reachability is a crafted admin POST only, and that was re-verified rather than inherited: `vNetName`
is only ever set from an ARM VNet name, and Azure names permit only alphanumerics, `_`, `.` and `-`, so
no legal Azure name can sanitize to empty._

_Tests 631 → 635 (+4). Build clean, 0 warnings._

---


# Refuted — reported by a finder, killed by the verifier

7 of 25 findings were killed (28%). Recorded so round 7 does not rediscover them.

| Finding | Why it was killed |
|---|---|
| `IsAbsenceStatus` drops `SubnetDeleted` unpinned by any test | Mutation reproduces and the consequence is serious (offered with `CanCommit=True`, 0 ARM reads) — but HEAD is correct and the finder concedes it. Round 5's refuted table killed the identical shape. Its one distinguishing claim fails: round 5's paragraph makes no claim about the absence side, so there is no false statement to correct. Watch list. |
| E4's five call-site orderings unpinned | Reverting all five leaves 603 green, but HEAD is correct. Stronger: once the helper swallows a failed rollback, the log-before-rollback *ordering* is no longer load-bearing at all, so the reverted state is not even clearly defective. Pass 1 reached this and declined to file. Watch list. |
| E9's `subnets.Count > 1` boundary only exercised at Count = 3 | Mutation to `> 2` leaves 603 green, but HEAD refuses the two-entry batch correctly with E9's own message and writes nothing. Fix is free (change the existing test from three entries to two) — watch list, not a finding. |
| `/Azure/ReconcileScan` returns 200 `success:true` when `scanSucceeded:false` | Nothing misreads it: three independent correct signals in the payload, the sole consumer checks `scanSucceeded` first, and **the write endpoint re-checks server-side and answers 400**, so a scripted caller that trusted `success` still deletes nothing. The other plan-returning endpoint uses the same convention. Flipping it would replace specific operator-useful text with a generic string — the "fix" is a regression. |
| `AzureService.cs:541-550`'s parse guard is unreachable | Unreachability is real, but the finding's wrong-output claim is factually false: it asserts `:548` does not pass `ex` and `:578` does. **Both pass `ex`**, so the `FormatException` naming the bad value prints under either message. What remains is a dead `catch` plus two false comments — fold into F3's commit as tidy-up. |
| Concurrent duplicate commit reports success with zero counts | 8 races reproduced (2 zero-count, 6 informative 409) with **no corruption at all**. Dies on wrong output: for the losing request the counts are literally accurate, and the 409 alternative would say "re-run the scan and review the results" when there is nothing to review. Also **the same window as watch-listed C20** (documented at `SubnetController.AzureReconcile.cs:99-106`), not a new one. Second sentence on the C20 watch-list entry. |
| The encompassing-subnet reason is dropped from the bulk selection tree | Every mechanical claim reproduces — and the explanation appears on the **next and mandatory** screen. Step 4's pill is only unlocked by building the preview, and the commit button lives inside the preview pane, so no path skips it. Azure forbids overlapping subnets in a VNet, so an encompassing subnet is necessarily the only one in its prefix. Pass 2 saw it and declined to file; pass 2 was right. Free polish if that file is edited for F11. |

The pattern matches rounds 4 and 5: **what dies is test-coverage observations** (three of seven) **and
findings whose harm is closed elsewhere in the system** (four of seven). The dead-code beat again produced
nothing that survived on its own terms — its two survivors, F10 and F16, are client-side arithmetic
defects it found while *measuring* the watch list rather than enumerating unused members.

# Watch list — not findings, but worth knowing

Carried forward from round 5, re-checked and still accepted:

- **ForwardedHeaders trust-all with `AllowedHosts: "*"`**; the Development-only `DevAuthHandler` bypass;
  `GlobalSanitizationFilter` skipping nested `System.*` collections; `CollectDescendants` lacking a cycle
  guard; the blind `catch {}` around the DataProtectionKeys probe.
- **C20** (the Azure reconcile check/act window). **New this round:** the losing request of a duplicate
  concurrent commit returns 200 `success:true` with zero counts, rendered as "Deleted 0 stale subnet(s)".
  No corruption; same window.
- **The unreachable IP-change branch in `ValidateHostIpUpdate`** — and *why* it matters, which round 5 did
  not record: it is the one place applying the network/broadcast reservations **without** the `cidr < 31`
  guard the other two sites carry. A trap for whoever makes that field editable.
- **`GlobalSanitizationFilter` runs after model binding and validation.** Now demonstrated three times
  (D7 lengthening, E2 removing, F18 emptying). Any new `[Sanitize*]` attribute needs a matching validator.
- **`MockAzureService.DefaultConfirmation` is `Deleted`.** Any test touching the confirmation path must set
  the verdict explicitly.
- **Still no `WebApplicationFactory`, no integration host, no JS test harness.** F4, F5, F8, F10, F11, F14
  and F16 are all client-side or end-to-end and none can be pinned by an automated test today.
- **Migration `.Designer.cs` snapshots still contain old column widths.** Correct and frozen.
- **A real Azure tenant ID is committed** at `launchSettings.json:41`. Re-checked, still present. It is the
  same tenant this round's live rig authenticated against.

New this round:

- **The usable-IP calculation's three copies now agree at every CIDR 0–32**, measured twice independently
  against the real assembly. The drift moved: it is in the **CIDR→mask** copies (**F16**), of which there
  are **six**, two unguarded. Update the count when reading round 5's entry.
- **Three cheap test gaps, each with a free fix**, from the refuted table: a `SubnetDeleted` case for
  `IsAbsenceStatus`; E4's five call-site orderings; E9's `Count > 1` boundary (change the existing test
  from three entries to two, and assert *which* refusal fired).
- **`DeletedSubnets` does not archive `AzureResourceId` or `IsFullyAllocated`**, and the deleted-subnets
  table renders neither `Tags` nor `OriginalParentId`. Any fix that clears an Azure link is therefore
  irreversible, and hand recovery needs database access. This constrains F1's fix and any future one.
- **`AZURE_TOKEN_CREDENTIALS=dev`, which the launch profiles set, excludes `EnvironmentCredential`** — a
  service-principal environment is silently ignored. A trap for the next person building a live rig.
- **`success` is not uniform across the Azure AJAX endpoints.** `/Azure/BulkGetVNets` reports an Azure read
  failure as `success:false`; `/Azure/ReconcileScan` reports the same failure as `success:true` with the
  reason inside the plan. Defensible — one returns a list, the other a plan built to carry errors — but a
  fourth endpoint's author should know both conventions coexist.
- **`rig/db/stop-app.sh`-style `pkill -f "Bastet.dll"` kills every instance on the box.** Two agents lost
  their app to a sibling this round. Match on `ASPNETCORE_URLS` or a PID file.
- **Headless Chromium never ticks `requestAnimationFrame`**, so jQuery's fx queue never drains and every
  animation assertion is a false pass unless `window.requestAnimationFrame` is deleted first.

# Clean bill

Swept across both passes and produced nothing:

- **The reconcile commit path re-derives what is deletable — proven live, and this is the round's most
  reassuring result.** A withheld row cannot be forced through: **9 of 9 attempts refused, database
  byte-identical before and after every one** — withheld alone, two withheld, a healthy `Live` row, a row
  with no `AzureResourceId`, a nonexistent id, a review-only row, the wrong confirmation word, all rows
  under the other credential, and the case that matters most, **a deletable row mixed with a withheld one
  (409, and the deletable row was not archived either)**. `subscriptionId` cannot widen the set.
- **The archival cascade matches the confirmation screen's promise** — 1 target + 2 children + 1 host IP
  promised, `targetsDeleted:1, subnetsArchived:3, hostIpsArchived:1` delivered, written deepest-first with
  `OriginalParentId` preserved and unrelated rows untouched. E14's fix holds.
- **ARM failure modes past round 5's 403/404.** 429, transport-level, `Status==0`, cancellation,
  mid-enumeration token expiry, 409 registration, a malformed stored id, a partial page, a non-existent
  subscription — all land on `Unknown` or `Success=false`. **Nothing collapses "could not ask" into
  "gone".** The three confirmation statuses are unchanged from round 5 (403 → `NotVisible`, 404 →
  `Deleted`, 200 → `Live`), and `GetVNetInventory` fails closed on auth failure, propagating to a 400 on
  commit. A real credential swap mid-run withheld all 8 now-invisible rows with the correct warning.
- **Server-side IP arithmetic.** `CalculateUnallocatedRanges` brute-forced against an independent
  reference across ~6,100 allocation layouts including `/0`, `/31`, `/32` and `255.255.255.x` — zero
  discrepancies; 19 further shapes checked against `CalculateUsableIpAddresses`; both 32-bit edges. No
  E13-class defect server-side.
- **Tree invariants under real writes.** 6,950 random operations across Create, Edit, HostIp-Create,
  SetAllocationStatus, Delete and BatchCreateChildSubnets, checked after every step against nine
  invariants including "parent is the most specific container" — zero violations. Re-parenting is not
  reachable at all.
- **Soft delete is right in both directions.** Archived rows move to separate tables with no query filter,
  so a deleted row's address space is correctly treated as free — proven by delete → recreate →
  re-assign-the-same-host-IP.
- **Locking and lifecycle.** All eleven subnet/host-IP mutating actions take the applock; the only
  unguarded writes are archive-table purges. E4's helper is correct at all five sites with no sixth, no
  skipped, double or post-commit rollback, and zero lingering `APPLICATION` locks after a
  transaction-inside-lock delete. Both uniqueness rules are backed by a real unique index as well as a
  query. No captive dependencies, no self-implemented disposables, `IHttpContextAccessor` never used
  off-request. The migration lock serializes two simultaneous cold starts correctly (six migrations across
  two `DbContext` types, applied once each, one shared history table).
- **No second instance of E6's identity-resolution defect.** Every post-mutation re-query in the tree
  enumerated; `HostIpController.cs:251-261` is the exact structural analogue and is *correct*, proven by
  measurement. Two hypotheses were killed by measurement and are recorded so they are not re-raised:
  `context.Update()` does **not** smear audit fields across a subtree, and a validation error does **not**
  refresh the concurrency token.
- **Antiforgery.** All eleven `[HttpPost]` actions carry `[ValidateAntiForgeryToken]`, verified against the
  verb attribute; no global filter, no `IgnoreAntiforgeryToken`, no state-changing GET.
- **Authorization.** All 34 actions enumerated against the fallback policy; row-level authorization
  checked, not just action-level; reconcile subscription scoping resists a crafted POST behind three
  independent guards; paging clamps hold.
- **XSS and the Razor output side, which round 5 did not check.** Zero `Html.Raw` across 105 views; one
  `href="@…"` with a hard-coded scheme; three `data-*` expressions with no HTML sink; one static `on*=`.
  All three `escapeHtml` implementations are byte-identical and attribute-safe. Every client-side HTML
  sink (`.append`, `$('<…>'+x)`, `innerHTML`) checked.
- **Injection, SSRF, open redirect, security headers, CORS.** The only raw SQL remains the parameterised
  `sp_getapplock`/`sp_releaseapplock`. No `FromSqlRaw`, no `Process.Start`. Outbound calls are ARM SDK only.
  CORS is opt-in without credentials. A column-width × validator × sanitizer matrix found every sanitizer
  non-expanding; IPv6 as a width-overflow vector is closed by the `AddressFamily` check.
- **Client-side routing and dead references.** All eleven client→server URLs resolve against real routes —
  **no second E8**. Every `@Url.Action`, tag-helper pair, JS selector, CSS class, all 42 TempData writes
  traced to a reader on their redirect target, all ViewData keys, all role and claim names, all four wizard
  enum-name switches. Zero unresolved references. Round 5's two new shared members are correctly scoped:
  `IsAbsenceStatus` has 2 callers, `RollbackQuietlyAsync` exactly E4's 5 sites.
- **The wizards' state machines and payloads.** All three driven in a real browser including backwards
  navigation, re-entry after error and re-submission; `subnets.Index` non-contiguous binding and the
  submit-time `disabled` re-normalisation both correct and load-bearing; the pill `disabled` locks genuinely
  stop Bootstrap 5.3; the reconcile confirmation word matches the server byte-for-byte. E7, E10, E12 and
  E14 all re-verified live, and E7×E12 compose correctly across three tree shapes with real animations.
- **The 14 round-5 fixes, reviewed as atomic commits.** Each diffed against its struck paragraph; every
  checkable claim accurate, including E3's alignment premise, E4's "all five sites, no sixth", E5's "only
  two entry points", E9's "Azure cannot produce this selection", and E2's deliberate `[SafeText]` omission.
  All 53 removed lines accounted for; nothing load-bearing deleted. **F1 is not one of these** — it predates
  the round-5 and round-4 work entirely.
- **Test quality of the ~27 tests round 5 added.** All six fixes claiming tests fail on revert, with the
  failure text matching round 5's record exactly in every case. E2's rewrite is genuinely non-vacuous —
  sabotaging the `IInputSanitizationService` supply fails all six with "Input sanitization service not
  available".

---

## Suggested order of attack

1. **Reconcile step 0** — restore the lost E8 entry to `docs/AUDIT-FINDINGS-5.md`. Its own commit, before
   `F1`.
2. **F1** — a privilege escalation ending in unrestorable data loss, including data with no Azure
   involvement. Take the Edit-side refusal **plus** the `Azure.cs:320` equality check; do not take the
   containment or clear-the-link fixes. E1 must stay.
3. **F2, F3** — the two Azure-side paths that archive a healthy resource. F3's fix must land with **F12**.
4. **F4** — the false statement the operator consents on. Pairs naturally with F1 and F2, and is the only
   one of the four that survives every fix for them.
5. **F5** — client-side validation is dead app-wide. One line as an interim. Amend E5's reachability
   paragraph in the round-5 file while you are there.
6. **F6** — then comment the three CodeQL alerts with the fix commit and leave them open; dismiss only
   after shipping a sanitizer model.
7. **F7–F16** — diagnostics, messaging and affordance defects. F9 and F10 overlap on the same flow; F11 and
   the refuted encompassing-reason polish touch the same file; F15 is two lines plus a catch.
8. **F17, F18** — a false sentence in the round-5 record with a test to add, and one invariant gap with no
   observable harm.
