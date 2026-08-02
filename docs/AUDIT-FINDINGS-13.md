# Bastet — Round-13 Audit Findings

| | Value |
|---|---|
| Round | **13** (finding letter **M** — findings are `M1` … `M3`, numbered sequentially across the whole file) |
| Branch | `audit/round-13` |
| HEAD at audit time | `78fc4c9` — *"Audit 12 Cleanup (#157)"* |
| Build | **0 warnings, 0 errors** |
| Tests | **738 passed**, 0 failed, 0 skipped |
| Date | 2026-08-02 |
| Status | **2 fixed** (M1 high, M2 low), **1 open** — 1 info |

> **Post-round amendment (2026-08-02, by the repository owner).** `M1` was filed by this round at
> **medium** and has been **re-rated HIGH**, and it is **to be fixed, not deferred again**. The round's
> own reasoning for medium — *"it needs a multi-prefix Azure subnet to fire — not the default and not
> common"* — prices the **frequency** of the trigger and ignores the **nature** of the output. BASTET's
> entire purpose is to be the authority on which IPv4 ranges are allocated. A silent, permanent, false
> *"this range is free"* is not a mid-severity defect in that product; it is the product failing at the
> only question it exists to answer, and an operator who trusts the answer double-allocates addresses
> Azure has already assigned. Rarity of trigger does not reduce the severity of a wrong allocation — it
> only reduces how often you find out. See the amendment block inside `M1` for the disposition and the
> reason the round-6 "feature change, not a bug fix" framing is rejected.

## Verdict

**`M1` requires action.** No finding in this round destroys data, leaks data, or bypasses
authorisation — but `M1` causes BASTET to assert something false about IP allocation, which for this
application is the failure that matters most.

**Read `M1` first.** Both Azure import wizards silently drop every IPv4 prefix after the first when an
Azure subnet carries several, and BASTET then advertises the dropped ranges as free space with a
*Create Subnet* button over them. Measured, not argued: after importing a two-prefix Azure subnet, the
target's Details page offered *"10.31.1.0 — 10.31.255.254, 65,279 IP addresses, [Create Subnet]"* over
an /24 that Azure has already assigned. That is the one output an IPAM exists to prevent. It is also
**not new**: it is the explicitly-deferred half of round 6's `F2`, which rode the watch list through
rounds 7-9 and then fell off. What this round adds is the measurement of what the operator actually
sees, which was never taken when the deferral was priced — and that measurement is precisely what
overturns the deferral.

**Read `M2` second, because it is a lesson about round 12.** `L4`'s double-commit guard was applied to
the bulk-import wizard only. The reconcile delete wizard — edited in the *same commit* for `L3` — still
re-arms its Confirm Delete button mid-flight, and one keystroke plus Backspace during the spinner fires
a second DELETE. The server accepts it, returns `200 {"targetsDeleted":0}`, and its `TempData` banner
overwrites the true one, so the operator lands on */Subnet* reading *"deleted 0 stale subnet(s)"* while
two rows have in fact been archived. Strictly worse than the `L4` case round 12 shipped at Info (there
the server *refused* the duplicate; here it accepts it and lies), but the archive itself is correct,
complete and recoverable — hence low. The fix is four lines of inline JS, built and run.

`M3` is cosmetic: a persisted description accumulates the same sentence once per import cycle. Filed
for completeness.

The rest of the value in this file is below the findings. **Six candidates were killed by verification**
(section *Refuted*), including one `[x2]` that both passes found and one severity-high claim whose
headline consequence turned out to reproduce at HEAD with the alleged defect playing no part. Those
entries exist so round 14 does not spend twenty agents rediscovering them.

## How this audit ran

Eight beats over the codebase, each covering a distinct surface. **Two independent passes** ran every
beat without sight of each other, plus a **deep sweep** on beats 1, 3, 6 and 7 — the Azure import,
reconcile and wizard-state surfaces where the last several rounds have concentrated.

Twenty finder agents were dispatched; twenty returned. They produced **12 raw findings**. Merge dropped
**1** as a duplicate or out-of-scope shape, leaving **9 candidates**. Of those, **1 was tagged `[x2]`**
and **8 `[x1]`**. Nineteen verifier agents then took the candidates adversarially — the job was to kill
the finding, not to confirm it — with instructions to build the fix and run it rather than read it.
**3 survived, 6 were refuted, and all 3 survivors were reproduced live** against the running
application and, where the surface required it, live Azure Resource Manager.

| Stage | Count |
|---|---|
| Finders dispatched | 20 |
| Finders returned | 20 |
| Raw findings | 12 |
| Dropped at merge | 1 |
| Candidates | 9 |
| `[x2]` | 1 |
| `[x1]` | 8 |
| Verifiers | 19 |
| Survived | 3 |
| Refuted | 6 |
| Reproduced live | 3 |

### What `[x2]` and `[x1]` mean

- **`[x2]`** — found independently by *both* passes. Two agents with no knowledge of each other landed
  on the same defect.
- **`[x1]`** — found by *one* pass only.

**`[x1]` is weak evidence of absence, not evidence of a weak finding, and it got MORE scrutiny, not
less.** One pass missing something is the expected outcome for a narrow surface — a multi-prefix Azure
subnet, a mid-flight keystroke — because only one agent happened to build the fixture that exposes it.
So every `[x1]` candidate drew a **second verifier on a reachability lens** (can a real user, with real
roles, through shipped UI, actually get here?) and a **third verifier on disagreement** whenever the
first two split. All three survivors in this file are `[x1]`. The single `[x2]` candidate, `C1`, was
**refuted** — both passes agreeing is not verification either.

---

# High

## M1 — Both Azure import wizards silently drop every IPv4 prefix after the first on a multi-prefix Azure subnet, and BASTET then reports the dropped ranges as free space

_M1 is fixed and committed. Both wizards now emit one selectable entry per IPv4 prefix an Azure
subnet owns, so every prefix can be imported and none is silently dropped._

_**What was done.** The collapse had two sites, both of them the same mistake made when an Azure
subnet could still only hold one IPv4 prefix. `GetVNetInventory` (`AzureService.cs:350`) built one
`BulkAzureSubnetViewModel` per Azure subnet from the singular `ExtractIpv4Prefix`; it now calls a new
`AzureService.BuildInventorySubnetRows`, which returns one row per prefix. `GetCompatibleSubnets`
(`AzureService.cs:239-277`) carried an explicit `break; // Take only the first valid IPv4 address`,
which is gone. Nothing else in either wizard needed changing: the bulk wizard already keys its
checkboxes on `data-address-prefix` and posts `{name, addressPrefix, azureResourceId}` per row, and
the single-VNet wizard already renders `result.subnets` by index, so both render N rows the moment N
are returned._

_**The naming was the only real obstacle, and it is a product decision the owner made, not one this
reconciliation made for them.** Two Bastet rows from one Azure subnet would otherwise both carry the
bare Azure name, and `Subnet.Name` has a **non-unique** index (`BastetDbContext.cs:36`) — they would
persist as rows distinguishable only by CIDR. Each row is now named for the range it holds:
`rec13-sn-multi (10.41.0.0/24)` and `rec13-sn-multi (10.41.1.0/24)`. **A subnet contributing a single
row is untouched** — `rec13-sn-single` imported as exactly `rec13-sn-single`, so no existing install
sees a rename. Applied in the planner (`AzureBulkImportPlanner.cs:486-533`) and, independently,
server-side in the single-VNet commit via a new `ResolveImportNames` (`SubnetController.Azure.cs:243`),
because the browser is not the authority there and a crafted or replayed post carries whatever names
it likes._

_**Three things the finding asked for were not built, each for a reason established by reading the
code rather than assuming.** (1) `AnnotateSubnet` needed **no** change: it keys the already-exists
test on `{NetworkAddress, Cidr}` (`AzureBulkImportPlanner.cs:288-289`), not on the resource id, so
importing one prefix leaves its sibling `Available` and an unrelated Bastet row still `Blocked` — both
measured live. The round-13 verifier had already corrected the finder on this point and was right;
making the instructed change would have been a regression. (2) The **reconciler** needed no change,
but it did constrain the fix: it shares `GetVNetInventory` (`SubnetController.AzureReconcile.cs:56`)
and indexes prefixes by resource id at `AzureReconciler.cs:68` with an indexer assignment — not
`ToDictionary`, so duplicate ids overwrite rather than throw. Every expanded row therefore carries the
subnet's **complete** prefix list; a row reporting only its own would make the last write win and the
reconciler would believe the subnet had lost the others, offering a live row for deletion. That
invariant has its own test. (3) A **new** `PrefixQualifiedName` helper was written and then deleted —
`SubnetNaming.WithSuffix` (`SubnetNaming.cs:52`) already does exactly this, length-aware, and is used
by the planner and `SubnetController.Create.cs:81`. Adding a second one would have been the residue
these rounds keep finding._

_**Proved by A/B against live ARM on the same fixture, not by reading.** Azure subnet
`rec13-sn-multi` in VNet `rec13-multi` (10.41.0.0/16) created with
`az network vnet subnet create --address-prefixes 10.41.0.0/24 10.41.1.0/24`; ARM returned
`addressPrefix: null, addressPrefixes: ["10.41.0.0/24","10.41.1.0/24"]`, confirming both the GA
behaviour and that the singular field nulls once there are two. A sibling `rec13-sn-single`
(10.41.5.0/24, singular field set) exercised the unchanged path. The unfixed build was a clone at
`c04fe21` on port 5402/catalog `bastet_rec13_head`; the fixed build ran on 5401/`bastet_rec13`. Same
subscription, same fixture, same posts:_

```
                    unfixed (c04fe21)                    fixed
GET /Azure/GetSubnets
  rows offered      2                                    3
                    sn-multi 10.41.0.0/24                sn-multi 10.41.0.0/24
                    sn-single 10.41.5.0/24               sn-multi 10.41.1.0/24
                                                         sn-single 10.41.5.0/24
after import, GET /Subnet/Details/1 "Unallocated IP Ranges"
                    10.41.1.0 - 10.41.4.255              10.41.2.0 - 10.41.4.255
                    1,024 IP addresses  [Create Subnet]  768 IP addresses  [Create Subnet]
                    10.41.6.0 - 10.41.255.254            10.41.6.0 - 10.41.255.254
```

_The 256-address difference is exactly the dropped /24. On the unfixed build BASTET offered a *Create
Subnet* button over a range Azure had already assigned; on the fixed build that range is child subnet
id 3 and is absent from the free list. Persisted rows on the fixed build:_

```
2|rec13-sn-multi (10.41.0.0/24)|10.41.0.0|24|.../subnets/rec13-sn-multi
3|rec13-sn-multi (10.41.1.0/24)|10.41.1.0|24|.../subnets/rec13-sn-multi
4|rec13-sn-single             |10.41.5.0|24|.../subnets/rec13-sn-single
```

_**The bulk wizard and the reconciler were driven too, including the counter-test that proves the
reconciler still discriminates rather than merely having gone quiet.** `GET /Azure/BulkGetVNets`
returned both prefixes as separate rows, each resolving to its own Bastet subnet
(`Already imported as Bastet subnet 'rec13-sn-multi (10.41.1.0/24)'.`) and each carrying the full
`ipv4AddressPrefixes` list. `POST /Azure/ReconcileScan` with both prefixes live reported
**0 items, 0 warnings** — no false drift on the row linked at the second prefix. Then
`az network vnet subnet update --address-prefixes 10.41.0.0/24` genuinely removed the second prefix in
Azure, and the same scan reported **exactly 1 item**: subnetId 3, `SubnetPrefixChanged`, reason
*"The Azure subnet still exists but its address prefix is now 10.41.0.0/24, not 10.41.1.0/24."* The
fixture was then restored._

_**Tests: 738 → 754** (+16, no test removed). Two failed against the unfixed code for the defect's own
reason — `Assert.Equal() Failure: Values differ` on the distinct-name assertion, both prefixes having
persisted under one name. The rest are guards that must never fail: single-prefix subnets keep their
plain name and shape; ARM reporting a prefix in both the singular property and the collection collapses
to one row; a prefix occupied by an unrelated Bastet subnet stays `Blocked`; two rows covering the same
range are still refused by overlap validation rather than renamed and created; and the reconciler
reports no drift for a row linked at the second prefix but still reports it when the prefix genuinely
goes. `dotnet build --no-incremental`: 0 warnings._

_**Not done, deliberately.** The two interim mitigations the finding offered — a `Reason` naming the
dropped prefixes, and blocking multi-prefix subnets outright — are moot now that no prefix is dropped;
building either would leave dead advisory code. The finding's remark that a fully-encompassing subnet
"cannot false-fire" because `p.Subnets.Count > 1` was left as it stands, verified rather than changed:
a prefix equal to the whole VNet prefix leaves no room for a second prefix on the same subnet, so the
encompassing path cannot now be reached with siblings from one Azure subnet._

---

# Low

## M2 — Round 12's `L4` double-commit guard was applied only to the bulk-import wizard; the reconcile delete wizard, edited in the same commit for `L3`, still re-arms its Confirm Delete button mid-flight

_M2 is fixed and committed. The reconcile delete wizard now carries the same in-flight guard `L4` gave
the bulk-import wizard, so neither re-arm route can fire a second DELETE._

_**What was done**, exactly as the finding proposed — it was assessed sound and needed no correction.
`_ReconcileScripts.cshtml` gains a module-scope `deleting` flag beside `confirmedIds`; `beforeSend`
sets it; `complete` clears it and then calls `refreshDeleteButton()`; and the single choke point at
`refreshDeleteButton` became `deleting || !hasSnapshot || !confirmed`. Both re-arm routes run through
`refreshDeleteButton`, so that one conjunct closes both. No `committed`-style flag was added: the
button is already `.addClass("d-none")`'d on success, so the post-success re-enable is unreachable by
a click — confirmed by reading, and by the `hidden` field captured in every run below._

_**Reproduced first, on the HEAD build, before anything was changed.** Fixture: Azure VNet
`rec13-stale` (10.42.0.0/16, child `rec13-stale-sn` 10.42.1.0/24) created, imported through the real
wizard, then deleted in Azure so both Bastet rows went stale. Chromium via Playwright, app on
127.0.0.1:5401 against SQL Server 2022, live ARM. Route (b), one keystroke plus Backspace:_

```
K2 1.432 {'delDisabled': True,  'progressHidden': False}     <- delete in flight
K4 1.454 {'delDisabled': False, 'progressHidden': False}     <- RE-ARMED, still in flight
K5 1.482 SECOND CLICK SENT
REQ  1.432 BulkDeleteStaleAzureSubnets
REQ  1.481 BulkDeleteStaleAzureSubnets                        <- 49 ms apart
RESP 2.450 200 {"targetsDeleted":1,"subnetsArchived":2,"hostIpsArchived":0}
RESP 2.450 200 {"targetsDeleted":0,"subnetsArchived":0,"hostIpsArchived":0}
```

_and the database immediately after, showing the response the operator sees is the false one:_

```
live=4      DeletedSubnets: 5|rec13-stale|10.42.0.0|16
                            6|rec13-stale-sn|10.42.1.0|24
```

_Two rows archived while the last response reported zero._

_**Both routes then re-run against the fixed build**, each on its own fresh live fixture rather than a
replay:_

```
route (b)  fixture rec13-stale2 (10.43.0.0/16)
  K4 1.963 {'delDisabled': True} confirmVal='approved'  -> second click impossible
  one REQ; RESP 200 {"targetsDeleted":1,"subnetsArchived":2}

route (a)  fixture rec13-stale3 (10.44.0.0/16)   Back to Review -> untick/re-tick -> Next -> retype
  A4 2.567 {'delDisabled': True}    back to review, delete still in flight
  A5 2.651 {'delDisabled': True}    untick + re-tick
  A6 2.694 {'delDisabled': True}    re-confirmed and retyped -> second click impossible
  one REQ; RESP 200 {"targetsDeleted":1,"subnetsArchived":2}
```

_**The non-regression leg matters more than either**, because a guard that made a failed delete
un-retryable would be a worse bug than the one being fixed. Fixture `rec13-stale4` (10.45.0.0/16) was
imported, deleted in Azure, taken to the confirm screen — and then the VNet was **re-created in Azure
out of band**, inside the wizard's window, so the commit's own ARM re-scan would find it live:_

```
R1 after failed delete: {'delDisabled': False, 'hidden': False, 'progressHidden': True}
R2 error: '2 of the selected subnet(s) are no longer reported as deleted in Azure. Nothing was
           deleted. Re-run the scan and review the results.'
R3 retryable (enabled and visible): True
RESP 409 {"success":false,"error":"...no longer reported as deleted in Azure..."}
```

_`complete` runs after `error`, so clearing the flag there lets `showCommitError`'s own
`refreshDeleteButton()` bring the button back. `pageerrors: []` on every run of both builds._

_**No permanent test ships with this.** The defect is client-side wizard state — jQuery handlers,
`disabled` attributes and AJAX lifecycle callbacks — and the suite has no browser seam
(`Microsoft.Playwright` is not referenced by `Bastet.Tests`, and the rig rules forbid adding
scaffolding to the repo for one finding). The measurements above are the record. Test count is
unchanged at **754**; `dotnet build --no-incremental` 0 warnings._

_**The server-side half was deliberately not built**, as the finding itself recommended.
`SubnetController.AzureReconcile.cs:193-204` still reports `success:true` when every requested id
resolved to null. Returning the 409 shape on `targetsDeleted == 0` with a non-empty `SubnetIds` would
convert an honest out-of-band concurrent delete into an error **after** a transaction that has already
committed — a worse outcome than the message it fixes. The client guard closes both reachable routes;
the underlying property (no idempotency token, no dedup, no per-user in-flight lock on
`BulkDeleteStaleAzureSubnets`) stays on the watch list for anything that can post it twice without a
browser._

---

# Info

## M3 — Azure import re-appends the "fully allocated" note to the target's Description on every run, so a wizard → un-mark → wizard cycle persists the same sentence N times

**Severity:** Info  **Tag:** `[x1]`  **Beat:** 5
**File:** `src/Bastet/Controllers/SubnetController.Azure.cs:85`
(callers: `SubnetController.Azure.cs:370`, `SubnetController.BulkAzure.cs:417`)

**Confidence:** **confirmed.** Four cycles driven through the shipped forms and the persisted column
read back.

**Failure scenario.** Azure VNet `rig-13-b5b-vnet` (10.99.0.0/24) has one subnet `rig-13-b5b-sn` whose
prefix is the whole VNet prefix, so `GetCompatibleSubnets` returns it with
`FullyEncompassesVNetPrefix=true`. Bastet subnet 10.99.0.0/24 exists. Running `/Azure/Import/{id}` and
importing sets `IsFullyAllocated=1` and
`Description = AppendFullyAllocatedNote(null, 'rig-13-b5b-sn')` (91 chars). The Details page's own
*Mark as Not Fully Allocated* form (`Views/Subnet/Details/_HostIpAssignments.cshtml:102`, POST
`HostIp/SetAllocationStatus`) clears the flag but leaves the note. `/Azure/Import/{id}` is now
reachable again (no children, no host IPs, not fully allocated), so the same import runs again — and
`AppendFullyAllocatedNote` has **no idempotence check** (`:94` is a bare
`string combined = $"{existingDescription}\n{note}"`), so it concatenates the identical sentence again.

**Wrong state** after four ordinary UI cycles: `Description` is the same 91-char sentence four times
separated by newlines (367 chars) on a row whose `IsFullyAllocated` is **0** — the description asserts
*"Fully allocated by Azure subnet 'rig-13-b5b-sn' which encompasses the entire address space."* four
times about a subnet that is not fully allocated. Growth is bounded only by the 1000-char cap (~10
repeats), after which the helper silently discards the note. `SubnetController.BulkAzure.cs:417` calls
the same helper, so the bulk wizard has the identical write.

**Reproduction — ran it.** Own app on 127.0.0.1:5273 (PID 442252), own catalog `bastet_rig13_v7c`,
SP A. Reused the existing fixture read-only; created no new Azure resource.

1. Real ARM through the app:
   `GET /Azure/GetSubnets?vnetResourceId=…/rig-13-b5b-vnet&subnetId=1` →
   `{"success":true,"subnets":[{"name":"rig-13-b5b-sn","addressPrefix":"10.99.0.0/24","hasMultipleAddressSchemes":false,"fullyEncompassesVNetPrefix":true}]}`
2. Four cycles of the exact form `_SubnetList.cshtml` posts, each followed by the Details page's own
   un-mark form, both with a live `__RequestVerificationToken` scraped from the rendered page:

```
cycle 1: importpage=200 import=302->/Subnet/Details/1 unmark=302 dblen|flag=91|0
cycle 2: importpage=200 import=302->/Subnet/Details/1 unmark=302 dblen|flag=183|0
cycle 3: importpage=200 import=302->/Subnet/Details/1 unmark=302 dblen|flag=275|0
cycle 4: importpage=200 import=302->/Subnet/Details/1 unmark=302 dblen|flag=367|0
```

3. The persisted row (`sqlcmd … -d bastet_rig13_v7c`, newlines rendered as `<NL>`):

```
1 | rig-13-b5b-vnet | full=0 | len=367 |
Fully allocated by Azure subnet 'rig-13-b5b-sn' which encompasses the entire address space. <NL>
(same sentence) <NL> (same sentence) <NL> (same sentence)
```

+92 bytes per repeat, on a row whose `IsFullyAllocated` is 0. Instance killed by captured PID 442252,
catalog dropped, `git status --porcelain` empty.

**Fix — the finder's proposal was corrected.** The core idea (dedupe before append) is right, but as
written it had one unsound branch and one gap.

> **Unsound branch, removed.** The original offered, as an alternative, stripping lines *"matching the
> `Fully allocated by Azure subnet '…' which encompasses the entire address space.` shape"* via a loose
> pattern. That can delete operator-authored text and breaks the helper's own documented contract at
> `:79-84` (*"Existing text is never sacrificed for the note"*). **Do not use a loose shape match.**
>
> **Gap.** Exact-line equality against the *current* note alone does not dedupe a note written for a
> differently-named Azure subnet (rename the Azure subnet between cycles and two distinct notes
> accumulate), and it leaves the note asserting a state the row no longer has.

Corrected fix, minimal and safe:

1. In `AppendFullyAllocatedNote`, split `existingDescription` on `\n` and drop any line that **both**
   starts with the literal `"Fully allocated by Azure subnet '"` **and** ends with the literal
   `"' which encompasses the entire address space."` (ordinal, whole line — narrow enough that operator
   prose cannot collide, wide enough to catch a renamed Azure subnet). Re-join, then append once.
2. Keep the overflow contract unchanged: if the deduped-plus-note string still exceeds
   `MaxSubnetDescriptionLength`, return the deduped existing description rather than truncating
   mid-note. Dedupe alone already frees ~92 bytes per stale copy, so this is strictly better than today.
3. Give `HostIpController.SetAllocationStatus` the mirror when it clears the flag: apply the same
   line-strip to `subnet.Description` so the row stops asserting fully-allocated after an un-mark. The
   finder listed this as *"ideally"*; it is **required** — without it the finding's own scenario still
   ends with a stale sentence.
4. Both call sites are fixed by (1) since they share the helper; factor the strip into a private static
   so (3) can reuse it.

This is pinnable by ordinary unit tests on the helper — no rendered-view seam needed — which is worth
doing, since the suite has **no coverage of `AppendFullyAllocatedNote` at all**.

**Cheaper interim — one line.** In `AppendFullyAllocatedNote`, before building `combined`:
`if (existingDescription is not null && existingDescription.Contains(note, StringComparison.Ordinal)) return existingDescription;`
Stops the duplication with no change to first-import behaviour and no change to either call site.

**Why Info and not Low.** `docs/AUDIT-FINDINGS-10.md:533` already records the sibling half verbatim as
a residue of a round-10 kill — *"after un-flagging, the appended note … stays in the description
(`DescLen 91` with `IsFullyAllocated=0`)"* — which establishes append-and-preserve as deliberate
design. What is new here is that the append is not **idempotent**, so the residue accumulates. That is
real but strictly cosmetic and self-limiting: bounded by the 1000-char cap, after which the helper
correctly drops the note and keeps existing text; no operator-authored text is ever destroyed (a
pre-existing 950-char description makes `combined.Length > 1000` on the very first import, so nothing
accumulates); and no count, validation or reconcile decision reads `Description`.

---

# Refuted — reported by a finder, killed by the verifier

Six candidates died in verification. **This section is the point of the round as much as the findings
are** — it is what stops round 14 spending agents re-deriving the same non-defects. Every one of these
was reproduced at the render or request level; what failed was the *harm*, not the observation.

| id | Title | file:line | Why it was killed |
|---|---|---|---|
| `C1` `[x2]` | Single-VNet import wizard renders a fully-encompassing Azure subnet as an ordinary child to import; selecting it creates zero children and instead flips the target to fully-allocated, "rewriting" its Description | `src/Bastet/Views/Azure/Import/_ImportScripts.cshtml:338` | Both differentiating consequences are **false when measured**. The Description is **appended**, not rewritten (`AppendFullyAllocatedNote`, `SubnetController.Azure.cs:85-101`) — proven against the reporter's own seeded-description fixture. And the target is **not a one-way door**: `_HostIpAssignments.cshtml:100-109` renders *Mark as Not Fully Allocated* for any Edit-role user, and one click restores `/Azure/Import/{id}` (driven end to end). The rename and `AzureResourceId` stamp happen on **every** single-VNet import regardless of this flag, so they are not consequences of this row either. What remains is one missing line of advisory UI copy on a row whose write is truthful, announced by the flash immediately after, semantically correct, and undoable in two clicks — the exact "one sentence of UI copy" / not-a-runtime-defect shape rounds 4-12 killed every time (round 12 `R3`; the *"'Add Child Subnet' is capacity-gated nowhere"* standing kill from rounds 7 and 11). **Note: this was the round's only `[x2]`. Two passes agreeing is not verification.** |
| `C3` `[x1]` | Layout nav offers five links that an authenticated user with no Bastet role is refused, on the only two pages such a user can reach | `src/Bastet/Views/Shared/_Layout.cshtml:32` | **Reproduced the render, not the harm.** The Development consequence the finding leads with **cannot occur in an unmodified build** — `DevAuthHandler` grants Admin to every request, and all five targets measured 200 with the header absent. The only real-deployment consequence is a click landing on `/Account/AccessDenied`, the page that states the exact cause and remedy, from a page that already says *"You don't have any Bastet application roles assigned"*. No wrong output, no state change, every target fails closed at 403. The premise is contradicted by deliberate code in the same views: `AccessDenied`'s own *Return to Home* button and `SignedOut`/`SignInFailed`'s Home links are links to pages the current principal cannot render, used as affordances **on purpose**. Unlike round 12's `L2`, **no action or mutation is offered** — that is the whole difference. |
| `C4` `[x1]` | Azure single-VNet import commit never re-checks the "target must be empty" precondition its own wizard page, the Details page gate and the bulk planner all enforce — so it Azure-links a subnet holding non-Azure children, and reconcile then archives them | `src/Bastet/Controllers/SubnetController.Azure.cs:328` | The commit **really does** skip the GET's precondition (reproduced: wizard GET 302-refuses, the POST 302-accepts and renames + Azure-stamps the parent). **But the claimed harm is not caused by it.** The identical tree shape was built through **fully sanctioned** operations — import onto an empty parent (wizard GET 200), then an ordinary `POST /Subnet/Create` of a manual child under the now-Azure-linked parent, which `SubnetController.Create.cs` permits with **no Azure guard at all**. `POST /Azure/ReconcileScan` reported that control tree exactly like the defect tree (`descendants=[5, 6]`, `warnings: []`), and `POST /Subnet/BulkDeleteStaleAzureSubnets {"subnetIds":[4]}` returned `subnetsArchived:3`, archiving `ctrl-manual-child` — a subnet with no Azure provenance created through the ordinary Create form. **The headline consequence reproduces at HEAD with the alleged defect playing no part.** The premise that this is "a state three independent read-side gates declare impossible" is false — those gates guard an import's *entry point*, not the resulting state. The second leg is empty too: ordinary children onto a fully-allocated parent are already refused and rolled back at `SubnetController.Helpers.cs:197`, and the fully-encompassing leg writes a state identical to a sanctioned import onto an empty parent (`SubnetController.Azure.cs:367` sets `IsFullyAllocated = true` regardless). Rows are **archived** to `DeletedSubnets` and listed at `/Subnet/DeletedSubnets`, not destroyed, behind a plan rendering *"This also archives N child subnet(s)"* and a typed `approved`. What is left is a gate-symmetry hardening observation over a legitimately reachable state, closely covered by the brief's accepted-and-still-open item 7 (`C20`). |
| `C6` `[x1]` | The delete archive drops `AzureResourceId`, so a wrong Azure reconcile deletion destroys the only record of which Azure resource each row was linked to | `src/Bastet/Controllers/SubnetController.Delete.cs:225` | **Reached and reproduced, but harmless.** The stated harm — *"operator must rediscover the mapping in the Azure portal by hand"* — was **disproven by running it**: after deleting three Azure-linked subnets, a plain re-import restamped all three `AzureResourceId`s byte-for-byte from ARM without consulting the archive. No code anywhere reads `DeletedSubnet` for Azure data, there is **no restore feature** (the archive is a display-only audit list plus a purge), and the reconcile path can only archive a row after a **direct ARM 404** — so the field, if archived, would name a resource Azure has just confirmed does not exist. The archive equally drops `RowVersion` and `IsFullyAllocated` and does not even display stored `Tags`/`OriginalParentId`, so *"the archive is lossy"* is a **design property**, not a defect in this one field. Schema-enhancement observation with no runtime misbehaviour — the "not a runtime defect, but…" shape killed in every round 4-12. |
| `C8` `[x1]` | Reconciler's cascade guard treats "found in Azure with a different prefix" as unprotected, so archiving a drift target silently archives a child whose Azure resource the same scan just read | `src/Bastet/Services/Azure/AzureReconciler.cs:128` | The cascade guard protects descendants **the operator cannot approve on the review screen** (invisible live rows, invisible out-of-subscription rows, checkbox-less review items, confirm-withheld rows) — not descendants that merely "exist in Azure". **Drift rows are the one Azure-linked class rendered with their own checkbox and their own reason**, so they need no guard. Archiving a row whose Azure resource still exists at a different prefix is the drift feature's **explicitly documented purpose** (`AzureController.ConfirmProposedDeletionsAsync` remarks; `AzureReconciler.cs:180-186` plus the `SurvivesEveryVerdict` tests). The *"silently / never named"* framing is contradicted by the UI as driven: the parent row shows *"Also archives 1 child subnet(s)."* in red, the child row with its reason is on the same screen, and the confirm step restates the cascade count. Unticked descendants being cascaded is the **universal** semantic here (`ParentSubnetId` is `DeleteBehavior.Restrict`; the accepted test `ApplyConfirmations_TargetWhoseDescendantIsAlsoDeleted_IsStillCommittable` has the same shape). |
| `C9` `[x1]` | `L4`'s `committed` flag is set by the previous plan's commit, permanently hiding Confirm for a different plan the operator has already previewed — and the struck entry's stated recovery does not work | `src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:667` | **The headline consequence does not hold.** `invalidatePlan()` (`_BulkScripts.cshtml:31`, called at `:343` on every checkbox tick and at `:435` in the preview handler) **clears `committed`** at `:36`, so *Back to Selection → Preview → Continue to Commit* re-arms Confirm and P2 commits 200 — measured on the HEAD build. **P2 is not uncommittable.** The stale success banner and the hidden Confirm at the moment the superseded commit lands are produced by the **untouched** success handler (`:698-707`) and reproduce identically on a simulated pre-`78fc4c9` build, so they are not caused by the cited line; and the 2 s `redirectUrl` navigation that "discards P2" is unconditional on every build. The only delta `78fc4c9` introduced is that the shortest recovery is **one click longer**, inside a 2 s window that ends in an automatic navigation. That is precisely the residue round 12 recorded in `L4`'s struck entry and its *Deliberately not done* list — and the novel part (*"the struck entry's stated recovery does not work"*) is a **correction to prose in a prior findings file**, not a runtime defect. |

---

# Watch list

Not findings. Things a verifier could not settle, patterns that will bite later, and places where the
evidence is thinner than the confidence.

- **The `L4` pattern is a family, not an incident.** `M2` exists because round 12 fixed one member of a
  two-member family and the struck entry did not say the sibling was left. Before round 14 files
  anything new here, grep every `.cshtml` under `Views/Azure/` for an AJAX `beforeSend` that disables a
  button without setting a module-scope in-flight flag. There are three commit-shaped wizards in this
  app (single-VNet import, bulk import, reconcile delete) and only one of them was guarded at HEAD.
- **A duplicate destructive POST is accepted by the server, not just re-armed by the client.** `M2`'s
  fix is client-side and closes both reachable routes, but the underlying property survives:
  `BulkDeleteStaleAzureSubnets` has **no idempotency token, no dedup, no in-flight per-user lock**, and
  its 409 is defeated by its own live ARM re-scan latency (~700 ms) whenever two submissions land
  ~50 ms apart. Any future path that can issue that POST twice — a retry, a proxy, a second tab —
  reproduces the false "deleted 0" banner. The server-side half of `M2`'s fix was designed but **not
  built**, and it has a real downside (it converts an honest out-of-band concurrent delete into an
  error after a committed transaction). Unsettled.
- **`Ipv4AddressPrefixes` is carried everywhere and read almost nowhere.** `grep -rn Ipv4AddressPrefixes src/`
  gives thirteen hits, five of them doc comments and model declarations. Of the seven code sites, three
  (`AzureBulkImportPlanner.cs:165`, `:267`, `AzureReconciler.cs:335`) read the **VNet's** list, three are
  the producer (`AzureService.cs:332`, `:338`, `:366`), and only `AzureReconciler.cs:392-393` reads the
  **subnet's**. `M1` is the
  visible consequence. Any other field on `BulkAzureSubnetViewModel` that no view reads is a candidate
  for the same class of silent truncation.
- **BASTET has no "present in Azure, absent from Bastet" reconcile status.** `AzureReconcileStatus` is
  `VNetDeleted` / `VNetPrefixRemoved` / `FullyAllocatingSubnetDeleted` / `SubnetDeleted` /
  `SubnetPrefixChanged` / `UnrecognisedResourceId` — every one of which starts from a Bastet row that
  carries an `AzureResourceId`. The reconciler structurally cannot notice something Azure has that
  BASTET does not. That is what makes `M1` permanent rather than self-healing, and it will make any
  future import-side omission permanent too.
- **`M1` is round 6's `F2` returning, and the process failure matters more than the defect.** It was
  deferred in round 6 on the *cost* of the fix, rode the watch list through rounds 7-9 as "deliberately
  left, small", and fell off in 10-12 without ever being closed or formally accepted. The consequence
  was never measured until now. **Round 14 may not defer `M1`, and no round may make this kind of call
  again.** Deciding that a reproduced defect is "out of scope" or "a feature change, not a bug fix" is
  a product decision belonging to the repository owner; four consecutive rounds made it on their behalf
  and never asked. The rule is now written into `.claude/skills/audit/SKILL.md` under *"Scope is the
  owner's call, never the round's"*: file it, rate it on consequence, put the fix cost in the *Fix*
  section, and let the owner say no. Every one of the four deferrals priced the cost of the fix and
  none priced the consequence of the defect — that is the specific error to not repeat.
- **`AppendFullyAllocatedNote` has zero test coverage** and is called from two controllers. `M3`'s
  corrected fix is trivially unit-testable with no rendered-view seam, which makes it the cheapest
  test-debt repayment currently visible in the Azure surface.
- **The single-VNet commit path has no name disambiguation.** Surfaced while correcting `M1`'s fix:
  `SubnetController.Azure.cs:404` writes `Name = subnet.Name` straight from the posted view model, and
  `Subnet.Name` carries a **non-unique** index. The bulk planner has `usedNames` + `DisambiguateName`;
  the single-VNet path has nothing. No finding was filed — nobody demonstrated a reachable collision at
  HEAD — but any change that makes one Azure subnet produce more than one Bastet row through that path
  will silently persist duplicates.
- **The single-VNet import commit does not re-check its own GET's precondition.** This is real
  (`C4` reproduced it: wizard GET 302-refuses, POST 302-accepts) and was refuted only because the harm
  it claimed is reachable through sanctioned operations anyway. It remains a gate asymmetry. It
  overlaps the brief's accepted-and-still-open item 7 (`C20`), and if `C20` is ever closed, this should
  be re-examined rather than assumed closed with it.
- **Rig hygiene held, and should keep being enforced.** Every verifier that ran the app used its own
  port and its own catalog, killed by **captured PID** rather than `pkill`, dropped its catalog, and
  left `git status --porcelain` on `/home/anuj/code/Bastet` empty. Two Azure resources were created for
  `M2`'s fixture (`rig-13-c2ver-vnet`, `rig-13-c2ver-sn-a`) and both ids were appended to
  `azure-inventory.txt`. Round 14 should confirm they are gone.
