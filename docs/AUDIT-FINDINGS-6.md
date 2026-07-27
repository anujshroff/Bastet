# Bastet — Round-6 Audit Findings

**Target:** branch `main`, HEAD `8cefc64` "Audit 5 Cleanup (#142)" (all 14 round-5 fixes squashed).
**Test baseline:** 603 passing, 0 failed. `dotnet build --no-incremental` clean, 0 warnings.
**Date:** 2026-07-26

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

**Confidence: confirmed.** Measured on real rendered pages in real chromium with real trusted clicks.

**Where:** [_Layout.cshtml:103](../src/Bastet/Views/Shared/_Layout.cshtml#L103) (pins `jquery@4.0.0`,
which removed `$.parseJSON`),
[_ValidationScriptsPartial.cshtml:3](../src/Bastet/Views/Shared/_ValidationScriptsPartial.cshtml#L3)
(`jquery-validation-unobtrusive@4.0.0`, which calls it at its lines 58 and 91). Affects **four** views:
`Subnet/Create.cshtml:21`, `Subnet/Edit.cshtml:21`, `HostIp/Create.cshtml:22`, `HostIp/Edit.cshtml:20`.
Regression from `dcd50c2` (#82), the jQuery 3.7.1 → 4.0.0 bump.

**Failure scenario.** On the shipped `/Subnet/Create`, a real click on the submit button with Name blank
and `Cidr=99`:

```
POST body : Name=&NetworkAddress=10.99.0.0&Cidr=99&...
Runtime.exceptionThrown: TypeError: s.parseJSON is not a function
```

The throw happens inside `showLabel` → `errorPlacement` → unobtrusive `onError`, *before*
jquery-validation can `preventDefault()`. The library has already set `novalidate` on the form, so the
browser's own `rangeOverflow` gate — confirmed live and would otherwise block it — is switched off.
Three controls on the same real page (validation scripts blocked; a one-line shim; jQuery 3.7.1 with a
valid SRI hash) all block the submit. **Every submit of those four forms throws, valid or not** — via
`defaultShowErrors` iterating `successList`.

**Blast radius, verified.** Nothing invalid is persisted and nothing 500s: `Cidr` of `-1, 33, 99,
2147483647, 4294967296, abc` on both Create and Edit all return 200 re-renders with the range error, and
the row is unchanged. The destructive paths check `confirmation != "approved"` server-side.

**The real cost is one extra HTTP round trip per validation error, plus a console `TypeError`** — not a
missing message, because the fail-open POST lands on the same action and writes the same messages into
the same validation spans. Both finders' framing overstated this; medium is the honest grade.

**This falsifies a claim in round 5's record.** E5's struck paragraph argues its reachability was narrow
because "`asp-for` on an int emits `type="number"` with `min`/`max`, so a normal browser blocks the
submit even with JavaScript disabled… the vector is a crafted POST". With JavaScript **enabled** — the
shipped case — an ordinary `Edit`-role user posts `Cidr=99` by typing it. Amend that paragraph when this
is fixed.

**Fix.** No jQuery-4-clean release of `jquery-validation-unobtrusive` exists — 4.0.0 is the latest
published. Two options, both verified to produce zero new errors across six pages:

- **Interim, one line, reversible:** `jQuery.parseJSON = JSON.parse` before the validation scripts.
  Sufficient — `unobtrusive.options` is `null` and the app never calls the other removed function.
- **Pin jQuery back to 3.7.1.** The SRI hash cannot be recovered from git (`dcd50c2` predates the
  integrity attributes), so it is recorded here, computed from the CDN bytes and verified in-browser:
  `sha384-1H217gwSVyLSIfaLxHbE7dRb3v4mYCKbpQvzx0cegeju1MVsGrX5xXxAvs/HgeFs`

---

# Low

## F6. Every `LogError(ex, …)` on the Azure request paths logs the exception unsanitized `[×2]`

**Confidence: confirmed.** **This resolves the three open CodeQL alerts #10/#11/#12 — they are true
positives. Do not dismiss them, do not suppress them in code.**

**Where:** the throwing statements are
[AzureService.cs:106](../src/Bastet/Services/Azure/AzureService.cs#L106) (alert #10, **not** `:105` —
`CreateResourceIdentifier` does not throw), [:167](../src/Bastet/Services/Azure/AzureService.cs#L167)
(#11, **not** `:166` — neither `new ResourceIdentifier` nor `GetVirtualNetworkResource` throws), and
[:315](../src/Bastet/Services/Azure/AzureService.cs#L315) (#12, **not** `:314`). The logging sites are
`:147`, `:288`, `:367` plus **two the alerts do not flag** —
[AzureController.cs:130](../src/Bastet/Controllers/AzureController.cs#L130) and
[:168](../src/Bastet/Controllers/AzureController.cs#L168), which log the *same* exception a second time
and are invisible to CodeQL because their template arguments are ints. Sink:
[Program.cs:14-21](../src/Bastet/Program.cs#L14). Sanitizer:
[LogSanitizer.cs:29-39](../src/Bastet/Services/Security/LogSanitizer.cs#L29).

**Failure scenario.** `SanitizeForLog` protects the template argument. `LogError(ex, …)` writes
`ex.ToString()`, and the ARM SDK's purely *local* id validation echoes the caller's string verbatim into
`ex.Message`. `new DefaultAzureCredential()` and `new ArmClient(cred)` both succeed with **no `AZURE_*`
variable set**, so no Azure credential and no network call are needed. One authenticated-Admin GET to
`/Azure/BulkGetVNets` with a percent-encoded `%1B` (ESC) sequence in `subscriptionId` produces a log
stream that, rendered through an independent VT100 emulator, erases the genuine line and substitutes:

```
fail: Bastet.Services.Azure.AzureService[0]
warn: Bastet.Services.Azure.AzureReconciler[0]
      Archived 42 stale subnet(s) for operator 'admin'.
```

Every byte survives in a persisted log (`cat -v` recovers it); what is corrupted is the rendering. The
precise one-line erasure is terminal-width dependent, but `ESC[2J` + `ESC[H` wipes the visible screen
independent of width.

**`char.IsControl` is the right predicate for this sink** — measured, `U+2028`/`U+2029`/`U+202E` emit
as-is, one physical line each, no forged entry. Latent only if a JSON or file sink is added.

**Why these three alerts and not the four other `SanitizeForLog` sites:** taint-source reachability. The
flagged three take an MVC action parameter; `:548`, `:573`, `:578` take a value read from EF, which
`cs/log-forging` does not treat as a source. So sanitization was never what CodeQL was tracking — but a
suppression is still wrong, because the unsanitized exception is a live wrong output on those lines.

**Severity.** Low, and the disagreement is recorded: one finder said medium. The console log is not
Bastet's system of record — destructive operations are recorded in the database with
`CreatedBy`/`ModifiedBy` and archive rows the forgery cannot touch — and the forger must already hold
Admin. It stays at low rather than info because it defeats a control written specifically to prevent it
(`LogSanitizer.cs:20-28` describes this exact attack), one GET is enough, and it fires twice per request.

This is residue of round 4's **D16**, whose close-out note claims "every log statement carrying a user-
or Azure-controlled string routes through `LogSanitizer`" — true of the structured arguments, false of
`ex`. That sentence is why three rounds did not see it.

**Fix, in this order.**

1. **Primary, systemic:** a `ConsoleFormatter` running `LogSanitizer.SanitizeForLog` over the rendered
   message **and** `exception.ToString()`, selected via `AddConsole(o => o.FormatterName = …)` +
   `AddConsoleFormatter<,>`. The only fix covering all 29 `LogX(ex, …)` sites, the two unflagged
   duplicates, the persisted variant at `:578`, and anything added later. **Register it
   unconditionally** — `Program.cs:14-21` sits inside `if (!builder.Environment.IsDevelopment())`, so a
   formatter registered there leaves the Development sink raw.
2. **Secondary, cheap:** reject the identifier before calling ARM — `Guid.TryParse` before `:105` and
   `:314` (and the early-out at `:96`). Note **`ResourceIdentifier.TryParse` does not filter control
   characters** — measured, it returns `true` with the ESC still in the parsed identifier — so it is not
   sufficient on its own for #11.
3. **Rejected:** sanitizing `ex` at each `LogError`. 29 sites to keep in step, and it destroys the
   structured exception that round 5's E4 fix went to trouble to preserve.

**Fixing this will not close the alerts, and that is not a reason to skip it.** CodeQL's flagged flow is
action parameter → `SanitizeForLog(x)` → `LogError` argument, and it does not model `SanitizeForLog` as a
sanitizer — which is why they are open today *despite* the sanitizer. Land the fix, comment the alerts
with the fix commit, leave them open, then ship a CodeQL sanitizer model for
`LogSanitizer.SanitizeForLog` and dismiss truthfully.

---

## F7. A JSON `null` for a collection returns an unhandled 500 past the subnet lock `[×2]`

**Confidence: confirmed.**

**Where:** [AzureBulkImportPlanner.cs:37](../src/Bastet/Services/Azure/AzureBulkImportPlanner.cs#L37)
(`"vNetPrefixes":null`), [:49](../src/Bastet/Services/Azure/AzureBulkImportPlanner.cs#L49)
(`"vNetPrefixes":[null]`), [:69](../src/Bastet/Services/Azure/AzureBulkImportPlanner.cs#L69)
(`"subnets":null`), reached via
[BulkAzure.cs:62](../src/Bastet/Controllers/SubnetController.BulkAzure.cs#L62) inside
`ExecuteWithSubnetLockAsync` and outside the try at
[:81](../src/Bastet/Controllers/SubnetController.BulkAzure.cs#L81), whose only `catch` is
`TimeoutException`; and
[SubnetController.AzureReconcile.cs:45](../src/Bastet/Controllers/SubnetController.AzureReconcile.cs#L45)
(`"subnetIds":null`) — which fires **before** any lock is taken.

**Failure scenario.** `System.Text.Json` overwrites the DTO's `= []` initializer with `null`. Both bulk
endpoints answer 500 with a non-JSON body (a `text/plain` stack trace in Development, the `text/html`
error page in Production), where the field *omitted* correctly yields
`400 {"success":false,"globalErrors":["No VNet address prefixes were selected."]}`. The sibling
`/Azure/BulkImportPreview` handles the same body correctly. Reachable as a scripted mistake, not only a
crafted one: an empty shell variable in the documented `jpost '{"subnetIds":[...]}'` idiom produces
exactly `"subnetIds":null`.

**The lock is correctly released** — zero `APPLICATION` rows in `sys.dm_tran_locks` afterwards, 19
acquires and 19 releases, next request served in 16 ms.

**Fix.** Guard the three planner sites and add `is null or { Count: 0 }` at `AzureReconcile.cs:45`,
matching `SubnetController.Azure.cs:130`. **State the side effect in the commit:** fixing it in the
planner changes `/Azure/BulkImportPreview`'s answer to the same body from
`{"success":false,"error":"Failed to build the import preview…"}` to `success:true` with a `globalErrors`
plan. That is the better shape, but it is a behaviour change on a second endpoint. Do **not** merely
widen the `catch` to `Exception` — that leaves the request classified as a server fault.

---

## F8. Two bulk-commit failures render a red panel reading only "Commit failed:" `[×2]`

**Confidence: confirmed.**

**Where:** [BulkAzure.cs:183](../src/Bastet/Controllers/SubnetController.BulkAzure.cs#L183) (target
path) and [:251](../src/Bastet/Controllers/SubnetController.BulkAzure.cs#L251) (child path) return
`BadRequest(ModelState)`, a `SerializableError` carrying none of the three fields
[_BulkScripts.cshtml:593-610](../src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml#L593) reads. The
sibling reconcile handler at
[_ReconcileScripts.cshtml:421](../src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml#L421) has the
`|| "The deletion failed."` fallback this one lacks.

**Failure scenario.** Two inputs are needed, one per line — a prefix must be non-canonical *and*
CIDR-aligned:

- `:183` — target prefix `10.0.0/24`. Previews with `canCommit:true`; commit answers
  `400 {"NetworkAddress":["'10.0.0' is not a valid IPv4 network address…"]}`.
- `:251` — child prefix `10.50.256/24` (or `0x0A.50.1.0/24`, `10.50.0x0100/24`) under a canonical
  `10.50.0.0/16` VNet prefix.

Fed the byte-exact live bodies through the shipped handler:

```
whole panel text  = "Commit failed:"
message span      = ""
error list <li>   = 0
confirm btn armed = true
```

**Note for whoever fixes this:** `10.50.1/24` does **not** reproduce it — it is caught earlier by the
planner's alignment check and renders correctly as a `globalErrors` entry.

**Reachability:** crafted POST by an authenticated Admin, the bar round 5 accepted E5, E9 and D22 under,
with the blast radius confined to the crafter's own screen. The `!plan.CanCommit` 400 at `:66` is the
milder, non-crafted half of the same mismatch — blank headline, populated bullet list.

**Fix.** Return `ModelStateMessage(...)`
([SubnetController.Azure.cs:56-63](../src/Bastet/Controllers/SubnetController.Azure.cs#L56)) at both
sites, echoing the server's own message — **and** add the `|| "The deletion failed."`-style fallback at
`_BulkScripts.cshtml:594`, which is the only floor under an unexpected body. Prefer this over reflecting
`item.VNetPrefix`: that is raw caller-controlled text the `GlobalSanitizationFilter` never descends into
(no XSS, since it is rendered with `.text()`, but the server's message is the better source). No test
asserts on the `SerializableError` shape.

---

## F9. The prefilled subnet name always contains a `/`, which `[SafeText]` forbids `[×1]`

**Confidence: confirmed.** Independently rediscovered by a verifier working an unrelated finding, so
three agents reached it.

**Where:** [SubnetController.Create.cs:67](../src/Bastet/Controllers/SubnetController.Create.cs#L67)
(`SubnetNaming.WithSuffix`, suffix `-{networkAddress}/{cidr}`) against
[SubnetViewModels.cs:11](../src/Bastet/Models/ViewModels/SubnetViewModels.cs#L11) (`[SafeText]`), whose
class `^[a-zA-Z0-9\s\-_.,!?@#$%&()+=]*$`
([InputSanitizationService.cs:14](../src/Bastet/Services/Security/InputSanitizationService.cs#L14))
contains no `/`. Pinned by `SubnetCreateGetPrefillTests.cs:116` and `:142`.

**Failure scenario.** Every "Create Subnet from an unallocated range" flow that accepts the default:

```
GET  /Subnet/Create?networkAddress=10.0.1.0&cidr=24&parentId=1
     value="prod-vnet-10.0.1.0/24"
POST it unchanged -> 200, "Subnet name contains invalid characters", nothing created
POST with "-24" instead of "/24" -> 302 /Subnet/Details/2
```

Nothing saves the operator: `SafeTextAttribute` is not an `IClientModelValidator`, so no client rule
fires; `[SanitizeName]` runs after validation and would not strip `/` anyway; and the message names no
character, so the operator must guess which one. On a `/32` parent the Name error also *masks* the CIDR
error, so two different rejections arrive in sequence (see **F10**).

**Two rounds stood on this line.** Round 4's D8 struck paragraph explicitly reasons about the generated
name — "would have stopped the 500 while still offering the operator a pre-filled name reading
`Parent-10.0.0.0/33`, which the POST then rejects" — and fixed the CIDR-in-the-name case while walking
past the `/` that is in *every* generated name. D19 fixed its *length*.

**Fix.** Change the controller's interpolation to a character inside the SafeText class (`-` or `_`).
`SubnetNaming.WithSuffix` has exactly two callers and the planner's own suffixes are already inside the
class, so changing only `Create.cs:67` is safe. Update both pinning assertions. **Do not widen
`[SafeText]`** — it guards three properties and round 5's E2 deliberately declined to extend it.

---

## F10. A `/32` subnet offers a Create Subnet button whose POST can never succeed `[×1]`

**Confidence: confirmed.** Found by three pass-2 beats and none in pass 1.

**Where:** [_UnallocatedRanges.cshtml:30-38](../src/Bastet/Views/Subnet/Details/_UnallocatedRanges.cshtml#L30)
(renders the button without considering the parent's CIDR),
[_SubnetCalculationScripts.cshtml:39-41](../src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml#L39)
(`findOptimalCidr`'s `while (cidr <= 32)` entered at 33, so the body never runs and it returns its
"everything overlapped" fallback), [:59](../src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml#L59)
(the unconditional `prop('disabled', false)` — this is the loose line, not the input handler).

**Failure scenario.** A childless `/32` — reachable through Bastet's own Create form, as a root or a
child — has one unallocated range and a live button. The modal renders `min="33" max="32"`, "Valid range:
**33 - 32** (recommended: 32)", size 1, Create enabled. `#createSubnetBtn` is `type="button"` outside any
submit, so no HTML5 constraint validation intervenes. The POST is refused with the *containment* message
("Child subnet must be contained within the parent subnet range. Parent subnet is 10.0.9.9/32") because
`IsSubnetContainedInParent` returns false at `childCidr <= parentCidr`, which runs before the CIDR check.
`/31` is correct and a `/32` child under a `/31` parent really is created.

The dead end is reached by the click-click path only: touching the CIDR field marks it `is-invalid` and
disables Create. The window is a `/32` created but not yet assigned its host IP — the button is gated on
having none.

**Fix.** Add `Model.Cidr < 32` to the view gate at `_UnallocatedRanges.cshtml:30`, which removes the
impossible state rather than rendering it better. Optionally have `findOptimalCidr` return `null` instead
of an untested `maxCidr` — hygiene, but it fixes nothing on its own.

---

## F11. "Hide already imported" turns blocked prefixes into a green success banner `[×2]`

**Confidence: confirmed.**

**Where:** [_BulkScripts.cshtml:184-186](../src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml#L184)
(the filter drops every `!isSelectable` prefix),
[:255-258](../src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml#L255) (the success alert),
[_StepSelection.cshtml:44-47](../src/Bastet/Views/Azure/BulkImport/_StepSelection.cshtml#L44),
[AzureBulkImportPlanner.cs:165-223](../src/Bastet/Services/Azure/AzureBulkImportPlanner.cs#L165)
(`AnnotatePrefix`). `AlreadyImported` is never returned — established by running the real planner over
4,046 brute-forced input combinations: `Available 1113 | Blocked 2903 | WillUpdateExisting 30 |
AlreadyImported 0`.

**Failure scenario.** A subscription whose prefixes are `Blocked` by conflicts with hand-made subnets,
with the switch on:

```
switch OFF: "Cannot import — Would contain existing Bastet subnet 'legacy' (10.0.1.0/24) …"
switch ON : "Everything in this subscription has already been imported. Nothing left to select."
            alert-success = true, reason text on screen = false
```

Nothing was imported, and the only explanation is hidden rather than shown.

**The correction all three reports missed, and it is why nobody noticed:** the banner *is* right in the
ordinary re-scan case. `AzureSubnetSnapshotService.cs:27` sets `HasChildSubnets` from the tree, so a
genuinely imported target comes back `Blocked("Bastet subnet 'X' already has child subnets. Already
imported?")` — **the same bucket**, hidden by the same filter, and there the sentence is true. The defect
is that one `Blocked` bucket carries both the intended case and the conflict cases.

**Fix.** Count the suppressed prefixes and say so, relabelling the switch "Hide unavailable" — reserving
the "already imported" wording for when every suppressed prefix carries the `HasChildSubnets` reason, or
simply always naming the count with "untick to see why". **Do not** filter on
`statusName === "AlreadyImported"`: that makes the switch a no-op and deletes the declutter it exists
for. Round 5's E11 fixed this class in the *reconcile* wizard and never touched `_BulkScripts.cshtml`, so
this is not a re-raise of a deliberate omission.

---

## F12. A row withheld for any non-403 reason is reported as a lost credential `[×1]`

**Confidence: confirmed (live).**

**Where:** [AzureReconciler.cs:154-156](../src/Bastet/Services/Azure/AzureReconciler.cs#L154) (the
`default:` arm puts `Unknown` and `NotVisible` in one bucket) and
[:162-167](../src/Bastet/Services/Azure/AzureReconciler.cs#L162) (the single warning sentence).

**Failure scenario.** Measured live in one run: an HTTP 400 `InvalidDoubleEncodedRequestUri` and a
`FormatException` both map to `Unknown`, joining a genuine 403 in one warning:

```
3 Azure-linked subnet(s) were missing from the subscription listing, but Azure would not confirm they
are deleted - the credential may simply have lost access to them. They have been withheld from
deletion: 'unknown-verdict-400', 'unknown-verdict-badguid', 'notvisible-row-403'.
```

Two of those three rows are in a resource group the credential reads successfully in the same run. That
sentence is the only explanation the reconcile screen and the delete-refusal 409 carry, and the real
cause reaches the log but no UI surface.

**`Unknown` needs no crafted input** — an ARM 429 or a transport blip mid-scan produces it, so an
operator can be told the credential lost access on a perfectly healthy subscription.

**The text misses its own documented intent:** the comment at
[_ReconcileScripts.cshtml:428-431](../src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml#L428)
says this text exists so the operator can tell "Azure would not confirm it" from "the credential lost
access" — the exact distinction the single sentence fails to make.

**Fix.** Split `Unknown` from `NotVisible` for the message only — the action stays identical — and give it
its own sentence naming "Azure could not be asked". **Do not** widen the existing sentence to cover both:
that makes it vaguer for the 403 rows where it is currently correct and actionable.

**Sequencing.** **F3**'s and **F6**'s fixes both push more rows into this bucket, making this message the
standing explanation for a newly-withheld class of row. Land this *with* them.

---

## F13. `BatchCreateChildSubnets` is the one Azure write path with no feature-flag guard `[×1]`

**Confidence: confirmed.**

**Where:** [SubnetController.Azure.cs:114](../src/Bastet/Controllers/SubnetController.Azure.cs#L114) (no
`IsAzureImportEnabled()` check), against **eleven** guarded siblings — all nine `AzureController` actions
plus `SubnetController.AzureReconcile.cs:29` and `SubnetController.BulkAzure.cs:31`. Stamps at
[:320](../src/Bastet/Controllers/SubnetController.Azure.cs#L320) (gated on `isAzureImport`) and
[:367](../src/Bastet/Controllers/SubnetController.Azure.cs#L367) (**gated on nothing**). Immediate
consequence at
[_SubnetDetails.cshtml:21-26](../src/Bastet/Views/Subnet/Details/_SubnetDetails.cshtml#L21).

**Failure scenario.** With `BASTET_AZURE_IMPORT` unset, all eleven siblings refuse (403 / "Azure Import
feature is not enabled" / redirect to `/Error/403`). The unguarded one accepts an Admin POST with
`isAzureImport=true`: 302, parent renamed and stamped with `AzureResourceId`, child created and stamped.
The Details page then renders a live "View in Azure Portal" link built from that id **with Azure entirely
off** — the one immediate wrong output. The rest is latent: it arms itself if the flag is later enabled.

**Fix, and the finding's own proposal does not work.** A branch-scoped
`if (isAzureImport && !IsAzureImportEnabled())` leaves `:367` open — measured, with `isAzureImport`
absent and the flag unset, `{"success":true,"subnetIds":[3]}` and the row carries a ghost subnet id. The
guard must reject a non-empty `subnets[].AzureResourceId` **or** `vnetResourceId` whenever the flag is
off, independent of `isAzureImport`. **Say out loud in the commit** that this changes the documented
non-Azure JSON API's contract.

**Do not credit this with closing F3 or F2** — those require the flag *on*, so the guard never fires in
their scenario. What helps them is validating the stored id's shape.

---

## F14. The Create-Subnet modal unlocks a field nothing listens to, under a stale explanation `[×1]`

**Confidence: confirmed.**

**Where:** [_SubnetCalculationScripts.cshtml:63-68](../src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml#L63)
(clears `readonly` and sets the help text), [:78](../src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml#L78),
[:141-147](../src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml#L141). "Nothing listens" is
airtight: instrumenting `$.fn.on` and `addEventListener` *before* the shipped script ran captured exactly
three bindings — `.create-subnet-btn` click, `#cidrInput` input, `#createSubnetBtn` click — **zero** on the
address field and **zero delegated bindings anywhere**.

**Failure scenario.** Open the modal on an unallocated range, then type `192.168.99.0` — outside the
parent entirely — into the now-editable network-address field:

```
#networkAddressHelp : "This network address has been adjusted to avoid overlaps."
#createSubnetBtn disabled : false
Create -> /Subnet/Create?networkAddress=192.168.99.0&cidr=25&parentId=703
```

`checkForOverlap` is silently skipped. The **actually-false thing on screen is the help text**, under a
value the script neither adjusted nor overlap-checked. (The finder's "`is-valid`" is a misattribution:
that class is on `#cidrInput`, and the CIDR genuinely is valid.) The server holds cleanly — the POST is
refused with the containment rule named and nothing is persisted — so this is a client-side affordance
defect.

**Fix.** Drop the `prop('readonly', false)` at `:64`. Nothing is lost:
`findCompatibleNetworkAddress` searches only within the parent's boundaries and skips every child, so the
address it writes is always inside the parent, aligned and non-overlapping. Prefer this over adding a
listener — `#cidrInput`'s handler rewrites the field from `#originalNetworkAddress` on every `input`, so a
shared validator must not reuse that branch or it will overwrite what the operator is typing.

---

## F15. `BASTET_AUTO_MIGRATE` cannot bootstrap a catalog that does not exist `[×1]`

**Confidence: confirmed.** Two finders declined to raise this on the grounds that `README.md:31-33`
makes creating the database a prerequisite; a third raised it as medium. Adjudicated to **low**.

**Where:** [Program.cs:234](../src/Bastet/Program.cs#L234) (`migrationLockConnection.Open()` against the
*target* catalog), [:236-255](../src/Bastet/Program.cs#L236) (the `Bastet:Migration` applock),
[:261](../src/Bastet/Program.cs#L261) and [:265](../src/Bastet/Program.cs#L265) (the two `Migrate()`
calls — the calls that would create it). Regression from `6edef5c`.

**Failure scenario.** `BASTET_AUTO_MIGRATE=true` against a connection string whose catalog does not
exist:

```
Unhandled exception. SqlException: Cannot open database "vb1_a" requested by the login.
   at Program.<Main>$(String[] args) in .../Program.cs:line 234
Error Number:4060
```

A binary built from `6edef5c^` creates the catalog, applies its migrations and serves. The message names
neither the missing catalog nor `BASTET_AUTO_MIGRATE`.

**Why low, not medium.** The documented bootstrap is create-then-run (`README.md:31-33`, unchanged since
the initial commit), and the Docker quickstart ships `BASTET_AUTO_MIGRATE=false`. The only in-repo
artifact pointing at a fresh catalog with auto-migrate on is the dev-only `launchSettings.json`. What
survives is a diagnostics defect: an unhandled `SqlException` on a connection the app itself opened.

**The custom lock is not redundant** — the claim that would have made this medium. EF Core 10's
`__EFMigrationsLock` does serialize two simultaneous starts against an *existing* catalog (measured, six
migrations across both `DbContext` types applied once each), but it does **not** cover `CREATE DATABASE`
and does **not** wait:

```
lock deleted, 2 instances, missing catalog -> twin2: SqlException 1801 "Database already exists"
lock deleted, __EFMigrationsLock held      -> exit 134 at 31s, "Execution Timeout Expired"
HEAD, Bastet:Migration held 60s            -> waited it out, migrated, served
```

**Fix.** Scope **only the lock connection** to `master` — two lines,
`SqlConnectionStringBuilder lockCsb = new(connectionString) { InitialCatalog = "master" }` — plus
`catch (SqlException ex) when (ex.Number == 4060)` on the `Open()`, naming the catalog and
`BASTET_AUTO_MIGRATE`. Measured bootstrapping a fresh catalog cleanly both single-instance and with two
simultaneous instances, because `CREATE DATABASE` then happens inside `Bastet:Migration`.

**Do not delete `Program.cs:233-298`.** It trades a deterministic single-replica crash for a racy
multi-replica 1801, silently downgrades the 300-second wait `README.md:125` promises to ADO.NET's 30-second
default, discards round 4's **D12** fix, and — as literally written — would delete both `Migrate()` calls
too, leaving no migration at all.

---

## F16. An unguarded CIDR→mask copy makes the `/0` Create modal offer a subnet from elsewhere `[×1]`

**Confidence: confirmed.** Two agents reached opposite conclusions; the finder was right and the
"self-corrects" reading was wrong.

**Where:** [_SubnetCalculationScripts.cshtml:202](../src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml#L202)
(`const mask = ~((1 << (32 - cidr)) - 1)` inside `normalizeIpToSubnetBoundary`, reached via `:263`
`getSubnetBoundaries(parentNetwork, parentCidr)`) and
[:269](../src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml#L269) (`cidrBitMask`).

**The expression exists six times across five functions in four files, not the three or four the finders
counted** — four of the six are guarded:

| Site | Guarded? |
|---|---|
| `IpUtilityService.cs:21-27`, `:469` | yes |
| `_SubnetFormScripts.cshtml:7,10` | yes (`if (cidr === 0) return "0.0.0.0"`) |
| `_BulkScripts.cshtml:361` | yes (`pc === 0 ? 0 : …`) |
| `_SubnetCalculationScripts.cshtml:202`, `:269` | **no** |

At `cidr === 0`, `1 << 32 === 1` in JS, so `~((1<<32)-1) === -1` and the mask reads `255.255.255.255`
where the server and the Create page both return `0.0.0.0`.

**Failure scenario.** `IsValidSubnet` explicitly permits `0.0.0.0/0`
([IpUtilityService.cs:130-134](../src/Bastet/Services/IpUtilityService.cs#L130)), so a `/0` root is
supported. With one child `128.0.0.0/2`, the server's own `CalculateUnallocatedRanges` returns a second
gap starting at `192.0.0.0`, and the view renders a button for it. Click it, type `/1`:

```
                  shipped              guarded
addressField      0.0.0.0              192.0.0.0
cidrValid         true                 false
createBtnDisabled false                true
href              ...networkAddress=0.0.0.0&cidr=1    ...networkAddress=192.0.0.0&cidr=1
```

The button is enabled with the address silently replaced by a block in a different part of the address
space, and the server would accept it. It self-corrects only when the *lower* half is allocated, because
then `checkForOverlap` catches the wrapped `0.0.0.0` — the shape the second agent traced, hence its wrong
generalisation.

**`:269` is not reachable at `cidr === 0`** (`findCompatibleNetworkAddress` is only called where
`cidr >= parentCidr + 1 >= 1`) and produces no wrong output today. Guard it for symmetry; do not claim
harm for it.

**Fix.** `const mask = cidr === 0 ? 0 : ~((1 << (32 - cidr)) - 1)` at both sites. Byte-identical at CIDR
1–32. Hoisting all six into `site.js` is a separate refactor; the guard is the fix.

---

## F17. Round 5's stated reason for leaving E6 untested is false, and the fix is unpinned `[×2]`

**Confidence: confirmed.** Category note: the coverage gap alone would be refuted — HEAD behaves
correctly. What survives is a **false statement committed in a findings file** that the next round would
rely on.

**Where:** `docs/AUDIT-FINDINGS-5.md:313-314` ("so `DbUpdateConcurrencyException` never fires under the
test provider") and `:334-335` ("a SQLite test would either not compile the scenario or pass vacuously"),
against [SubnetController.Edit.cs:155](../src/Bastet/Controllers/SubnetController.Edit.cs#L155), which
writes the *posted* `RowVersion` into `OriginalValues` — making the conflict an ordinary
`WHERE … AND RowVersion = @posted` that SQLite evaluates fine.

**Round 5's premise is true and its inference is false.** `[Timestamp] byte[] RowVersion` really is
store-generated only on SQL Server. That does not make the exception unreachable.

**Failure scenario.** The test round 5 said could not exist was written and runs on SQLite
(`DataSource=:memory:`, no container). At HEAD it shows `LastModifiedAt=10:05` — the saved value. With
E6's two `.AsNoTracking()` calls reverted:

```
Expected: 2026-01-02T10:05:00Z
Actual:   2026-07-27T03:45:41Z     <- wall clock at the failed save
db still holds 10:05
```

So the exit path of **every** failed Edit POST is currently unpinned, on the strength of a sentence that
is wrong.

**Fix.** Two things. Strike the two false clauses above (not the whole paragraph — the premise stands),
and add the test; a working copy is preserved at `scratchpad/p2-beat7/E6ProbeTests.cs.keep`. **Put this in
the test's doc comment:** under SQLite the stored token is `NULL`, so *any* non-null posted `RowVersion`
conflicts. The test pins the fall-through repopulation faithfully but does not reproduce production's
value-versus-value comparison — say so, or a later round will re-flag it as passing for a provider
artefact.

---

# Info

## F18. Bulk import persists a subnet with an empty name `[×1]`

**Confidence: confirmed.** Downgraded from low: a harm probe found nothing that breaks.

**Where:** [AzureBulkImportPlanner.cs:386](../src/Bastet/Services/Azure/AzureBulkImportPlanner.cs#L386)
and [:403](../src/Bastet/Services/Azure/AzureBulkImportPlanner.cs#L403) (the target's name, with no
empty-name fallback) against [:470-474](../src/Bastet/Services/Azure/AzureBulkImportPlanner.cs#L470) (the
*child* fallback, four lines away), written at
[BulkAzure.cs:198](../src/Bastet/Controllers/SubnetController.BulkAzure.cs#L198).
`ValidateSubnetCreation` never inspects `Name`.

**Failure scenario.** A crafted Admin POST with `vNetName` that sanitizes to empty (e.g. markup only):

```
{"success":true,"createdTargets":1,"createdChildSubnets":1}
8|[]|0|192.168.0.0|16|NULL      <- nameless target persisted
9|[web]|3|192.168.1.0|24|8      <- the child kept its name
```

The same three values through the interactive Create POST are all refused ("HTML tags are not allowed in
subnet names" / "Name is required"). No genuine wizard path can reach it: `vNetName` comes only from an
ARM VNet name, and Azure names permit only alphanumerics, `_`, `.` and `-`.

**Why info rather than low.** Everything downstream survives: the tree row is clickable with the address
as its visible text, Details returns 200 with an empty `<h1>`, the parent dropdown option is selectable,
Edit/Delete/DeletedSubnets all 200, and the row archives cleanly. Nothing fails.

**Why keep it at all.** [EditSubnetViewModel.cs:37-41](../src/Bastet/Models/ViewModels/EditSubnetViewModel.cs#L37)
carries an explicit comment — "StripHtml can empty a name outright, defeating `[Required]`" — which is
precisely this hazard, closed for both interactive models by E2 and left open on the one write that has
the same sanitizer output and no equivalent guard.

**Fix.** Mirror the child fallback at `:386`/`:403`, so the preview shows what the commit will write.
Rejecting a null/whitespace `Name` in `ValidateSubnetCreation` is also safe — every in-code caller already
supplies a non-empty name.

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
