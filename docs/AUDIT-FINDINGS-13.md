# Bastet — Round-13 Audit Findings

| | Value |
|---|---|
| Round | **13** (finding letter **M** — findings are `M1` … `M3`, numbered sequentially across the whole file) |
| Branch | `audit/round-13` |
| HEAD at audit time | `78fc4c9` — *"Audit 12 Cleanup (#157)"* |
| Build | **0 warnings, 0 errors** |
| Tests | **738 passed**, 0 failed, 0 skipped |
| Date | 2026-08-02 |
| Status | **3 findings open** — 1 **high**, 1 low, 1 info |

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

**Severity:** **High** (filed by the round at medium; re-rated — see disposition below)  **Tag:** `[x1]`  **Beat:** 3
**Disposition: MUST FIX. Not eligible for deferral to round 14.**
**File:** `src/Bastet/Services/Azure/AzureService.cs:352`
(siblings: `AzureService.cs:275`, `AzureService.cs:418-439`, `AzureBulkImportPlanner.cs:520-527`)

**Confidence:** **confirmed.** Mechanism, the absence of any warning, the persisted rows and the
free-space claim were each observed on a live rig, not inferred.

**Failure scenario.** Azure has allowed several IPv4 prefixes on one subnet since Sept 2025 and ARM
accepts it today. Given VNet `rig-13-b3p2-multi` (10.31.0.0/16) with one subnet
`rig-13-b3p2-sn-multi` carrying `addressPrefixes: [10.31.0.0/24, 10.31.1.0/24]`, `GetVNetInventory`
builds one `BulkAzureSubnetViewModel` per Azure subnet and sets
`AddressPrefix = ExtractIpv4Prefix(subnet)` — the **first** prefix only (`AzureService.cs:352`). The
single-VNet wizard does the same, with an explicit `break; // Take only the first valid IPv4 address`
(`AzureService.cs:275`). The planner then creates exactly one Bastet child.

The model *does* carry the full list (`BulkAzureSubnetViewModel.Ipv4AddressPrefixes`, populated at
`AzureService.cs:366`) but its only consumer is the reconciler (`AzureReconciler.cs:391-394`) — no
view and no planner path reads it. Nothing in either wizard, in the preview, or in `AnnotateSubnet`
mentions the other prefixes.

**Wrong output:** after import, `10.31.1.0/24` exists in Azure but nowhere in BASTET, and the target's
Details page advertises it as unallocated with a *Create Subnet* button. An operator allocating from
BASTET hands out addresses Azure has already assigned. The reconciler cannot correct it: it walks only
Bastet rows that carry an `AzureResourceId` and has no "present in Azure, absent from Bastet" status at
all. The wrong state is permanent and invisible.

**Reproduction — ran it.** Own instance on port 5313, own catalog `bastet_rig13_v5c`, SP A, real
Chromium via Playwright, live ARM. The multi-prefix fixture already existed; no new Azure resource was
created.

1. ARM ground truth, `az network vnet show -g bastet-visible -n rig-13-b3p2-multi`:
   `space ["10.31.0.0/16"], subnets [{ n: rig-13-b3p2-sn-multi, p: null, ps: ["10.31.0.0/24","10.31.1.0/24"] }]`
   — the singular `addressPrefix` really is `null` once there are two, exactly as the XML doc at
   `AzureService.cs:390-396` says.
2. `GET /Azure/BulkGetVNets` returned
   `{ name: rig-13-b3p2-sn-multi, addressPrefix: "10.31.0.0/24", ipv4AddressPrefixes: ["10.31.0.0/24","10.31.1.0/24"], statusName: "Available", reason: null, isSelectable: true }`.
   Step-2 card `innerText`, verbatim and complete:
   `rig-13-b3p2-multi 10.31.0.0/16 / Will create a new Bastet subnet. / rig-13-b3p2-sn-multi 10.31.0.0/24`
   — no badge, no reason line, no mention of `10.31.1.0/24`. Zero page errors.
3. `POST /Azure/BulkImportPreview` → `childSubnets = [{ name: rig-13-b3p2-sn-multi, networkAddress: 10.31.0.0, cidr: 24 }]`,
   `errors []`, `warnings []`, `canCommit true`.
4. Confirm Import clicked. SQL on the fresh catalog:
   `1|rig-13-b3p2-multi|10.31.0.0|16|NULL` and `2|rig-13-b3p2-sn-multi|10.31.0.0|24|1`. Nothing for
   `10.31.1.0/24`.
5. `GET /Subnet/Details/1`, verbatim:
   `Unallocated IP Ranges — 10.31.1.0 … 10.31.255.254 … 65,279 IP addresses … [Create Subnet]`.
6. Single-VNet wizard, same subnet: `GET /Azure/GetSubnets?vnetResourceId=…/rig-13-b3p2-multi&subnetId=1`
   returned **one** entry —
   `{"resourceId":"…/subnets/rig-13-b3p2-sn-multi","name":"rig-13-b3p2-sn-multi","addressPrefix":"10.31.0.0/24","hasMultipleAddressSchemes":false,"fullyEncompassesVNetPrefix":false}`
   — and `hasMultipleAddressSchemes` is `false` (it is IPv4-only), so not even the dual-stack badge
   fires. Matches the `break; // Take only the first valid IPv4 address` at `AzureService.cs:275`.

Instance killed by captured PID 442806, catalog dropped, `git status --porcelain` empty.

**Fix.** Emit one selectable entry per IPv4 prefix: in `GetVNetInventory` (`AzureService.cs:350-368`)
and `GetCompatibleSubnets` (`AzureService.cs:239-277`), produce a `BulkAzureSubnetViewModel` per
element of `ExtractIpv4Prefixes(subnet)` rather than per Azure subnet, each carrying the same
`ResourceId`. The reconciler already copes with several Bastet rows sharing one Azure subnet id,
because `EvaluateSubnetLevel` tests *membership* in `Ipv4PrefixesOf` (`AzureReconciler.cs:376`).

> **The finder's fix was corrected by the verifier on two points.**
>
> 1. **Wrong claim, removed.** The original said *"`DisambiguateName` already handles the resulting
>    name collision."* That is true of the **bulk** planner only (`AzureBulkImportPlanner.cs:485-527`,
>    where `usedNames` + `DisambiguateName` run per plan item). The **single-VNet** commit path has no
>    disambiguation at all — `SubnetController.Azure.cs:404` writes `Name = subnet.Name` straight from
>    the posted view model, and `Subnet.Name` carries a non-unique index, so two identically-named rows
>    would persist silently. If the single-VNet leg is taken, `BatchCreateChildSubnetsCore` needs the
>    same used-names pass the planner has, or the wizard must suffix the prefix into the name it posts
>    at `_ImportScripts.cshtml:330`.
> 2. **Confused claim, removed.** The original said `AnnotateSubnet` *"must then compare per prefix
>    rather than against `subnet.AddressPrefix`."* That is a no-op — once one view model is emitted per
>    prefix, `subnet.AddressPrefix` **is** the per-prefix value, and `AnnotateSubnet`
>    (`AzureBulkImportPlanner.cs:259-307`) needs no change. `encompassesAPrefix` at `:267-268` already
>    compares against the *VNet's* prefix list, which is correct as it stands. Making the instructed
>    change would be a regression.
>
> Confirmed sound: the duplicate-`AzureResourceId` reasoning. Nothing keys on it in a way that breaks —
> `AzureSubnetSnapshotService` keys on `SubnetId`/`ParentSubnetId`, `SubnetController.AzureReconcile.cs:78`
> keys `stillStale` on `SubnetId`, and `AzureService.cs:537`'s `ToDictionary` is over resource ids that
> `ConfirmResourcesAsync` has already deduplicated at `:524-526`, so a repeated id cannot collide. And
> `p.Subnets.Count > 1` beside a `FullyEncompasses` entry (`AzureBulkImportPlanner.cs:464`) cannot
> false-fire, because a prefix equal to the whole VNet prefix leaves no room for the subnet's second
> prefix.

**Cheaper interim — stop the silence, one file.** Round 6 declined the full change as *"a feature
change, not a bug fix"*, so an interim that only removes the invisibility is worth having. In
`GetVNetInventory` (`AzureService.cs:358-367`) and `GetCompatibleSubnets`, when
`ExtractIpv4Prefixes(subnet)` yields more than one, set `Reason` to name the prefixes that will **not**
be imported, and have `AnnotateSubnet` *preserve* rather than null that reason (it currently overwrites
`Reason = null` at `:294` on the Available path). Add the same sentence to `BulkImportPlanItem.Warnings`
so it survives into step 3 and into the commit-time divergence text. One Bastet row per Azure subnet —
no data-model change, no name collisions, no new reconciler tests — while removing the property the
finding is actually about. It does **not** close the "Details page advertises the range as free" half,
so it is an interim, not a substitute.

**Harder interim — refuse rather than truncate.** Server-side, so the browser is not the authority: in
`AzureBulkImportPlanner.AnnotateSubnet` (`AzureBulkImportPlanner.cs:259`) set `Status = Blocked`,
`IsSelectable = false` and a reason naming every prefix whenever `subnet.Ipv4AddressPrefixes.Count > 1`,
plus the matching hard error in `BuildPlanItem` so a crafted or stale POST is refused too. The operator
cannot import the subnet, but BASTET never asserts that an Azure-assigned range is free.

### Disposition — fix it, and why the standing deferral is overturned

**This is to be fixed. The interims above are fallbacks if the full change cannot ship immediately;
they are not the resolution.** The wizards must offer **one selectable entry per IPv4 prefix**, and
the operator imports the ones they want. No auto-import, no guessing, no truncation.

Round 6 deferred this as *"creating several Bastet subnets from one Azure subnet, which is a feature
change, not a bug fix."* **That framing is rejected, on the codebase's own evidence:**

- **The pattern already exists in this application, one level up.** `AzureBulkImportPlanner.cs:165` is
  `vnet.Prefixes = [.. vnet.Ipv4AddressPrefixes.Select(p => AnnotatePrefix(p, vnet, existingSubnets))]`
  — a prefix list fanned out into one entry per prefix. A **VNet** with four IPv4 prefixes is handled
  correctly today, and a VNet with four **subnets** is handled correctly today. Only a **subnet** with
  several prefixes is truncated. Applying an established internal pattern at the level where it was
  missed is not a new feature.
- **The truncation predates the problem it fails to handle.** The `break; // Take only the first valid
  IPv4 address` (`AzureService.cs:275`) comes from `9f220e0` (2025-05-01), the original Azure import
  feature — written when an Azure subnet could not have more than one IPv4 prefix. Azure changed that
  in September 2025. This is code that was correct when written and became wrong when the external
  contract moved: a regression against reality, not an unimplemented feature.
- **The data has been ready since round 6.** `BulkAzureSubnetViewModel.Ipv4AddressPrefixes` is
  populated at `AzureService.cs:366`, sits one line below the truncation, and no view or planner path
  reads it. Round 6 plumbed it specifically so that whoever closed this would not have to re-plumb ARM.
  The remaining work is rendering and naming, not integration.
- **Azure's behaviour is confirmed against live ARM, not assumed.** This round ran
  `az network vnet subnet create ... --address-prefixes 10.31.0.0/24 10.31.1.0/24`; ARM returned 200 and
  reported `addressPrefix: null, addressPrefixes: ["10.31.0.0/24","10.31.1.0/24"]`. The singular field
  going null once there are two is exactly what `AzureService.cs:390-395` already documents.

**Deferral history — four rounds, no decision.** Filed as round 6 `F2`; the reconciler half was fixed
and the import half deferred on cost. It rode the watch list through rounds 7, 8 and 9, then fell off
in rounds 10-12 without ever being closed or formally accepted. Round 13 rediscovered it independently
and measured the operator-visible consequence for the first time. **The consequence was never priced in
any of the four deferrals** — only the cost of the fix was. That is the error being corrected here.

**Definition of done:** a multi-prefix Azure subnet presents every one of its IPv4 prefixes as a
separate selectable row in both wizards; importing all of them produces one Bastet row per prefix with
non-colliding names through **both** commit paths (the single-VNet path needs the planner's
`usedNames`/`DisambiguateName` pass — see the watch-list entry on `SubnetController.Azure.cs:404`); and
no range Azure has assigned is ever shown as unallocated. Regression test must fail against HEAD.

---

# Low

## M2 — Round 12's `L4` double-commit guard was applied only to the bulk-import wizard; the reconcile delete wizard, edited in the same commit for `L3`, still re-arms its Confirm Delete button mid-flight

**Severity:** Low  **Tag:** `[x1]`  **Beat:** 6
**File:** `src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:76`
(supporting: `:14`, `:345-408`, `:410`, `:416`, `:435-442`, `:453`, `:489`;
server: `SubnetController.AzureReconcile.cs:78-93`, `:144-149`, `:193-204`)

**Confidence:** **confirmed.** Both re-arm routes driven end to end on the HEAD build, and the fix
built and re-run on a patched scratch copy.

**Failure scenario.** Round 12's `L4` added `committing`/`committed` to `_BulkScripts.cshtml` so
re-entering step 4 cannot fire a second commit. The same commit (`78fc4c9`) also edited
`_ReconcileScripts.cshtml` for `L3` but left the identical re-arm in the only Azure-driven DELETE path.
`refreshDeleteButton()` (`:73-77`) gates `#rec-confirm-delete-btn` on `confirmedIds` + the typed word
only — **no in-flight flag** — and it is reachable from two ordinary handlers while a delete POST is
still running: `$("#rec-confirmation").on("input", refreshDeleteButton)` (`:410`) and the
`#rec-go-confirm-btn` rebuild (`:345-408`). `beforeSend` (`:435`) disables the button but sets no flag,
so anything that calls `refreshDeleteButton` re-enables it.

Concrete inputs: an Admin imports Azure VNet `rig-13-b6p2-vnet` (10.171.0.0/16 + child
`rig-13-b6p2-sn-a` 10.171.0.0/24); the VNet is then deleted in Azure. On */Azure/Reconcile* the
operator scans, ticks the stale row, types `approved`, clicks Confirm Delete.
`POST /Subnet/BulkDeleteStaleAzureSubnets` runs a **live ARM re-scan before it touches the DB**, so it
is in flight for ~0.7 s on the rig and multi-second against a real subscription. While the spinner is
up the operator either **(a)** clicks Back to Review, unticks/re-ticks, Next, retypes `approved`,
Confirm Delete, or **(b)** simply presses one key and Backspace in the confirmation box. Either
re-arms the button and a second identical DELETE is posted.

**Wrong output.** The second request's ARM re-scan overlaps the first request's transaction, so its
`stillStale` map is built *before* the rows are archived and it does **not** hit the 409 at
`SubnetController.AzureReconcile.cs:80-92`. It queues on `_localGate`, finds every
`context.Subnets.FindAsync(id)` returns null (`:143-148`), skips them all, and returns
**HTTP 200 `{"success":true,"targetsDeleted":0,"subnetsArchived":0}`**. That response lands last, so
its `TempData["SuccessMessage"]` (`:193-195`) overwrites the first one. The operator is redirected to
*/Subnet* and reads **"Azure reconcile: deleted 0 stale subnet(s), archiving 0 subnet(s) and 0 host IP
assignment(s) in total."** while `DeletedSubnets` in fact holds both archived rows. This is strictly
worse than the `L4` case round 12 fixed: there the server *refused* the second attempt; here it accepts
it and reports a destructive operation as a no-op.

This is not a narrow race. The two requests are ~50 ms apart while the ARM leg is ~700 ms, so the
200/zero outcome is the **normal** result of a double submit here — it happened on every run.

**Reproduction — ran it.** App on 127.0.0.1:5317, catalog `bastet_rig13_verc2`, SP A,
Playwright/Chromium, live ARM.

Fixture built for real:

```
az network vnet create -g bastet-visible -n rig-13-c2ver-vnet \
  --address-prefixes 10.181.0.0/16 --subnet-name rig-13-c2ver-sn-a --subnet-prefixes 10.181.0.0/24
# two Azure-linked Bastet rows seeded with those exact ARM ids, then:
az network vnet delete -g bastet-visible -n rig-13-c2ver-vnet   -> DELETED
```

Route **(b)** — one keystroke plus Backspace, no navigation, HEAD build:

```
S2 2.447 stale rows: ['1', '2'] ['rig-13-c2ver-vnet', 'rig-13-c2ver-sn-a']
K1 2.555 first click sent
K2 2.558 {'delDisabled': True,  'progressHidden': False}
K3 2.562 typed "x":  {'delDisabled': True,  'confirmVal': 'approvedx'}
K4 2.565 Backspace:  {'delDisabled': False, 'progressHidden': False, 'confirmVal': 'approved'}
K5 2.604 SECOND CLICK SENT
(2.555, 'REQ',  'BulkDeleteStaleAzureSubnets')
(2.604, 'REQ',  'BulkDeleteStaleAzureSubnets')
(3.333, 'RESP', 200, '{"success":true,"redirectUrl":"/Subnet","targetsDeleted":1,"subnetsArchived":2,"hostIpsArchived":0}')
(3.408, 'RESP', 200, '{"success":true,"redirectUrl":"/Subnet","targetsDeleted":0,"subnetsArchived":0,"hostIpsArchived":0}')
K8 landed message: Azure reconcile: deleted 0 stale subnet(s), archiving 0 subnet(s) and 0 host IP assignment(s) in total.
pageerrors: []
```

Route **(a)** — Back to Review → untick/re-tick → Next → retype → Confirm, HEAD build:

```
A3 2.088 {'delDisabled': True,  'progressHidden': False}
A4 2.122 Back to Review:       {'delDisabled': True,  'progressHidden': False}
A5 2.187 untick+retick:        {'delDisabled': True,  'progressHidden': False}
A6 2.225 re-confirmed+retyped: {'delDisabled': False, 'progressHidden': False}   <- re-armed, delete still in flight
A7 2.255 SECOND CLICK SENT
RESP 2.800 200 targetsDeleted:1 subnetsArchived:2
RESP 2.830 200 targetsDeleted:0 subnetsArchived:0
A10 landed: deleted 0 stale subnet(s), archiving 0 subnet(s) and 0 host IP assignment(s) in total.
```

Database after each run:

```
SELECT COUNT(*) FROM Subnets; SELECT OriginalId,Name,NetworkAddress,Cidr FROM DeletedSubnets
LiveSubnets 0
2|rig-13-c2ver-sn-a|10.181.0.0|24
1|rig-13-c2ver-vnet|10.181.0.0|16
```

Two subnets archived while the operator is told zero were.

**The fix was built and run, not read.** Patched scratch copy, port 5318, catalog
`bastet_rig13_verc2fix`, `dotnet build` 0 warnings / 0 errors:

```
route b: K4 {'delDisabled': True}  -> second click impossible; one POST; landed "deleted 1 stale subnet(s), archiving 2 subnet(s)"
route a: A6 {'delDisabled': True}  -> refused;                one POST; landed "deleted 1 stale subnet(s), archiving 2 subnet(s)"
non-regression (real 409 forced by an out-of-band row delete between confirm and click):
   HEAD    F3 {'delDisabled': False, 'hidden': False, msg '1 of the selected subnet(s) are no longer reported as deleted in Azure...'}
   PATCHED F3 {'delDisabled': False, 'hidden': False, msg identical}
pageerrors: [] on every run of both builds
```

Both instances killed by captured PID (443034, 448206 — never `pkill`), both catalogs dropped, ports
free, `git status --porcelain` on the repo empty.

**Fix — verdict: sound, as proposed.** Mirror `L4` in `_ReconcileScripts.cshtml`:

- declare `let deleting = false;` beside `confirmedIds` (`:14`);
- set `deleting = true` in the delete AJAX `beforeSend` (`:435`);
- in `complete` (`:440-442`) set `deleting = false;` then call `refreshDeleteButton();` — `complete`
  runs *after* `success`/`error`, so `showCommitError`'s existing `refreshDeleteButton()` at `:489`
  still leaves a genuinely-failed delete retryable (verified by the non-regression leg above);
- make the single choke point honest at `:76`:
  `$("#rec-confirm-delete-btn").prop("disabled", deleting || !hasSnapshot || !confirmed);`

Because both re-arm routes go through `refreshDeleteButton`, that one conjunct closes both. A
`committed`-style flag is **not** needed: `#rec-confirm-delete-btn` is already `.addClass("d-none")`'d
on success (`:453`), so the post-success re-enable is unreachable by a click.

**Optional server-side half — do it separately.** `SubnetController.AzureReconcile.cs:193-204` reports
`success:true` and stamps `TempData["SuccessMessage"]` even when every requested id resolved to null and
`targetsDeleted == 0`. Returning the 409 shape instead when `targetsDeleted == 0` with a non-empty
`SubnetIds` cannot false-fire on a legitimate single request (the smallest-CIDR target always resolves,
so `targetsDeleted >= 1`), but it would convert the honest out-of-band-concurrent-delete case into an
error *after* a transaction that has already committed. The client guard alone closes both reachable
routes; ship that first. This half was **not** built.

**Cheaper interim — one line, no state to reason about.** Guard the click handler itself. In
`$("#rec-confirm-delete-btn").on("click", …)` at `:416`, after `const ids = confirmedIds || [];`, add a
module-scope `deleting` flag and `if (deleting) { return; }` before the `$.ajax` call, setting it in
`beforeSend` and clearing it in `complete`. The button still visually re-arms, but no second POST is
issued, so the false "deleted 0" message cannot be produced.

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
