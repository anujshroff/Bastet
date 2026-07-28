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

## G1 — Bulk import silently repoints an existing subnet's Azure link to a different VNet, and reconcile then archives the subtree on the strength of the wrong resource `[x1]`

**File:** `src/Bastet/Controllers/SubnetController.BulkAzure.cs:152`
**Also:** `src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:209`, `:283`, `:353`;
`src/Bastet/Controllers/SubnetController.Azure.cs:341`; `src/Bastet/Services/Azure/AzureReconciler.cs:241`
**Confidence:** confirmed

### Scenario

An operator has Bastet subnet `10.98.0.0/16` imported from VNet `va` — a VNet imported with no
subnets selected, so the target row is childless. A second VNet `vb` in the same subscription uses
the same RFC1918 range; Azure permits this (both `PUT`s return 201) and it is ordinary in
hub-and-spoke and dev/prod topologies.

The operator runs Bulk Import. `/Azure/BulkGetVNets` offers **vb's** `10.98.0.0/16` as
`statusName=WillUpdateExisting, isSelectable=true`, with reason *"Will import into existing Bastet
subnet 'va'"* — which names a Bastet subnet, not an ARM resource, and does not say a link will be
replaced. The preview returns `targetTypeName=ExactMatch, errors=[], warnings=[], canCommit=true`.
The commit returns **`createdTargets:0, createdChildSubnets:0, renamedTargets:0,
fullyAllocatedTargets:0`** — every counter zero, nothing on screen says anything changed.

State did change: `Subnet.AzureResourceId` was rewritten from va's ARM id to vb's. The row's *name*
is untouched (unless "rename matched" is ticked), so it keeps advertising va.

When vb is later decommissioned, `/Azure/ReconcileScan` reports the row as `statusName=VNetDeleted,
canCommit=true, warnings=[]`, reason *"The VNet this subnet was imported from no longer exists in
Azure"* — false; va is alive and is what it was imported from. The direct-read confirmation guard
cannot help: it correctly reads vb, and vb really is 404. Committing archives the row and its whole
subtree. `DeletedSubnets` does not archive `AzureResourceId` and there is no restore path anywhere
in the app, so recovery needs direct database access.

**Corrected by the verifiers, and load-bearing:** at the moment of the repoint the ExactMatch target
must be **empty** — `AnnotatePrefix` (`:196-207`) and `BuildPlanItem` (`:363-377`) both block an
exact match carrying child subnets, host IP assignments, or `IsFullyAllocated`. So the finder's
clause *"including children still linked to live Azure subnets in va"* is **not reachable** and must
be dropped. The archived subtree can only contain children added by hand after the repoint, or
children imported from vb (which are legitimately stale). The finding stands without it: the target
row itself is archived on the strength of a resource it was never imported from.

### Evidence — reproduced: yes, driven against live ARM, twice, independently

Verifier 1 (port 5307, catalog `bastet_verify-bulk-import-repoints-azure-link`, SP_A): created
`rig-verify-bulkrepoint-va` and `-vb` in RG `bastet`, both `10.98.0.0/16` (ARM 201 for both);
imported va → `createdTargets:1`, Details/1 links `virtualNetworks/rig-verify-bulkrepoint-va`;
`BulkGetVNets` then offered **both** prefixes as `WillUpdateExisting/isSelectable:true`; preview for
vb `canCommit=true, errors=[], warnings=[]`; commit returned all-zero counters and Details/1 now
links `…-vb`; DELETE vb (202, then 404 for vb / 200 for va); `ReconcileScan` → one item
`statusName=VNetDeleted, canCommit=true, warnings=[]`; `BulkDeleteStaleAzureSubnets` with
confirmation `approved` → `targetsDeleted:1, subnetsArchived:1`, row now on `/Subnet/DeletedSubnets`
while va still returns 200 from ARM.

Verifier 2, independently (port 5297, catalog `bastet_x2reach9297`, its own fixtures `rig-x2reach-va`
/`-vb`): identical chain, plus a hand-created child `10.98.1.0/24` before the delete →
`ReconcileScan` reported `descendantCount 1, descendantSubnetIds [2]`, and the commit returned
`targetsDeleted:1, subnetsArchived:2` with both rows on `/Subnet/DeletedSubnets`. It also measured
the **second write site**: two `POST /Subnet/BatchCreateChildSubnets` for the same parent with
`vnetResourceId=va` then `=vb` moved the parent's portal link and renamed it.

Both verifiers left the RG clean. Neither found an alternative relink path: `EditSubnetViewModel`
never binds `AzureResourceId` (`SubnetController.Edit.cs:39,260` derive only a display-only
`IsAzureLinked`), so these two import writes are the only code that can change an Azure link.

### Fix — corrected; take the refusal, not the disclosure

Two finders proposed opposed fixes. **Take the refusal.** The objection to it — *"blocking breaks
the legitimate delete-and-recreate-with-same-prefix case"* — was measured and is **empirically
false**: ARM ids are path-based, so deleting a VNet and re-creating it with the same name in the
same resource group returns HTTP 201 with a **byte-identical** id string (only `resourceGuid`
changes, and Bastet never stores that). That case is already skipped by the equality test at
`BulkAzure.cs:150` and can never trip a "differs from the recorded id" block. Only a genuine
**rename or move** produces a different id.

Apply all four parts:

1. **Refuse at both write sites.** At `SubnetController.BulkAzure.cs:149-154` and
   `SubnetController.Azure.cs:339-342`: when the row's existing `AzureResourceId` is non-empty and
   differs from the selected VNet's id, roll back and refuse, **naming both ARM ids**.
2. **Surface it in the planner** — the same test `AnnotateSubnet` already applies to child subnets
   at `AzureBulkImportPlanner.cs:283-289`. Do **not** emit a bare `Blocked`: emit a distinguishable
   status/warning on the item naming the recorded VNet id and the newly selected one, so the wizard
   can render it (per-item warnings do render — `_BulkScripts.cshtml:515-517`).
   *Mechanical note:* `AnnotatePrefix`'s signature is `(string addressPrefix,
   IReadOnlyList<ExistingSubnetSnapshot>)` and has **no access to the selected VNet's ResourceId**.
   Thread the `BulkAzureVNetViewModel` through the way `AnnotateSubnet` already receives it —
   `AnnotateAvailability:163-171` has it in scope.
3. **Ship a deliberate relink affordance in the same change.** A hard block bites one real case: a
   VNet renamed or moved to another RG/subscription gets a new id, and there is **no in-app way to
   relink** (Edit never binds `AzureResourceId`, there is no Restore action). Without an opt-in, the
   operator's only recourse is reconcile-archive-then-reimport, which is itself irreversible. Add an
   explicit per-item "replace the existing Azure link" opt-in, defaulting to off, that the commit
   honours — or, at minimum, make the refusal text state that recourse.
4. **Count relinks in the commit response.** Not optional: without it the success banner reads all
   zeros while persisted state changed.

Nothing here touches transport or hosting; plain-HTTP and air-gapped deployments are unaffected.

### Interim fix

Refuse at the **write site only**, without touching the planner or the wizard: at
`SubnetController.BulkAzure.cs:149-154`, when `targetSubnet.AzureResourceId` is non-empty and
differs from `sanitizedVNetResourceId`, roll back and return `Conflict` naming both ids — the same
shape the ExactMatch not-found branch at `:126-130` already uses. This closes the persisted-state
defect on the bulk path in isolation; the preview still offers the prefix, but nothing is silently
rewritten. Its cost, which is acceptable for an interim: a multi-prefix batch containing one
colliding prefix is rolled back whole.

---

# Medium

## G2 — F11's fix throws a `ReferenceError` on the exact screen it exists for: `suppressedPrefixes` is declared inside the per-VNet loop and read outside it `[x2]`

**File:** `src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:264`
**Also:** `:180` (the declaration), `:186` (the increment), `:268` (a dead `else` arm)
**Confidence:** confirmed

### Scenario

`let suppressedPrefixes = 0;` at `:180` sits **inside** the `$.each(vnets, function (vIdx, vnet) { … })`
callback, which closes at `:255`. It is read at `:264` and `:266`, in `renderVNetTree`'s own scope.
`let` is block-scoped and not hoisted out of the callback, so the moment
`$tree.children().length === 0` is true the function dies with
`ReferenceError: suppressedPrefixes is not defined` and the `$tree.append($empty)` at `:272` never
runs.

Precondition: every VNet prefix in the subscription is `isSelectable:false` — any partially
overlapping Bastet subnet per VNet prefix achieves this. The operator selects the subscription, then
ticks **"Hide unavailable"**. Wrong output: a **completely blank selection panel** — no message, no
count, no instruction — in precisely the case F11 was written to explain
(*"N VNet prefix(es) in this subscription cannot be selected, and are hidden. Untick 'Hide
unavailable' to see why."*).

**The second-order consequence is what turns a blank panel into a stuck wizard, and the verifier
insisted it be carried:** the throw escapes jQuery's deferred, which does not catch it, so the AJAX
`complete:` handler at `:150-152` never runs. The switch is sticky, so on re-entry to step 2
(*Back to Subscription* → re-select) `loadVNets` → `renderVNetTree` throws again and the operator is
left with `#bulk-vnet-loading` visible, `#bulk-vnet-selection` hidden, and therefore **the switch
itself hidden** — a permanent "Loading VNets…" spinner recoverable only by a page reload.

Why round 6 missed it: its record claims *"the suppressed counter is wired"*, verified by lifting
the function out of the file, which discards the enclosing scope.

The `else` arm at `:268` is also dead: `loadVNets` returns early to `#bulk-no-vnets` when
`vnets.length === 0` (`:137-141`), and `GetVNetInventory` `continue`s past any VNet with
`Ipv4AddressPrefixes.Count == 0`, so an empty tree can only arise from suppression.

### Evidence — reproduced: yes, driven in a real browser

Verifier ran its own instance (port 5259, catalog `bastet_verify_bulksup`, SP_A) and its own
`zenika/alpine-chrome` container on CDP 9259 with a hand-rolled RFC6455/CDP client, against the
shipped page with the pinned jQuery 4.0.0. Created six blocker subnets through the real
`POST /Subnet/Create`, confirmed via `/Azure/BulkGetVNets` that every VNet prefix came back
`Blocked / isSelectable:false` with reason *"Would contain existing Bastet subnet …"*. Selecting the
subscription rendered 6 cards with 6 "Cannot import" badges; a real click on
`#bulk-hide-imported` fired `Runtime.exceptionThrown`:

```
ReferenceError: suppressedPrefixes is not defined
    at renderVNetTree (http://127.0.0.1:5259/Azure/BulkImport:601:16)
    at HTMLInputElement.<anonymous> (…:669) via jQuery 4.0.0 dispatch/v.handle
```

Observed after: `$('#bulk-vnet-tree').children().length === 0` and
`$('#bulk-vnet-tree').html() === ''`. Re-entry measured: `#bulk-vnet-loading` visible = true,
`#bulk-vnet-selection` = false, `#bulk-vnet-error` = false, `#bulk-no-vnets` = false,
`#bulk-hide-imported` = false.

**The fix was measured, not assumed.** Using `Fetch.enable` on the document URL only, the verifier
served a patched document with the declaration moved and re-drove the page: 5 cards before the tick,
1 child after, text = *"5 VNet prefix(es) in this subscription cannot be selected, and are hidden.
Untick 'Hide unavailable' to see why."*, `#bulk-vnet-selection` still visible, untick restores the
cards, `Runtime.exceptionThrown` count = 0. The repository was never touched.

### Fix

Delete `let suppressedPrefixes = 0;` from `:180` and declare it (as `let`, not `const`) immediately
after `const hideImported = …` at `:174`, in `renderVNetTree`'s own scope. That is also the
semantically correct scope: the message says *"in this subscription"*, so it must be a
subscription-wide total rather than the last VNet's. No caller changes, no server change. While the
file is open, drop the unreachable `else` arm at `:268-271`.

### Interim fix — do not take it

Wrapping the empty-state block in `try`/`catch` is the same size as the correct change and leaves
the panel blank in exactly the case the block exists for. The declaration move is both cheaper and
correct.

---

## G3 — One VNet deleted during a scan makes `GetVNetInventory` report the whole subscription unreadable, disabling bulk import and reconcile `[x1]`

**File:** `src/Bastet/Services/Azure/AzureService.cs:344`
**Also:** `:369` (the single outer catch), `src/Bastet/Controllers/AzureController.cs:231`,
`src/Bastet/Controllers/SubnetController.AzureReconcile.cs:55`
**Confidence:** confirmed

### Scenario

`GetVNetInventory` lists the subscription's VNets once (`:317`) and then issues a **separate** ARM
call per VNet to enumerate its subnets (`:344`). A 404 from any of those child calls escapes the
inner `await foreach`, is caught by the **single** outer `catch (Exception ex)` at `:369`, and the
entire inventory is returned as `Success=false, ErrorMessage="Azure could not be read for this
subscription"`.

Consumers then fail wholesale: `/Azure/BulkGetVNets` answers `success:false` so the wizard lists
nothing; `/Azure/ReconcileScan` builds a plan with `scanSucceeded:false` and a global error and zero
items; `POST /Subnet/BulkDeleteStaleAzureSubnets` answers 400 *"Azure could not be re-checked, so
nothing was deleted."* The output is wrong on its own terms — the subscription **was** read
successfully and every other VNet enumerated fine — and it fires on exactly the event reconcile
exists to detect.

**The finder's stated mechanism is wrong and both verifiers corrected it.** ARM does **not** keep a
deleted VNet in the subscription listing "for a period": measured, listing and child collection
converge within ~600 ms (LIST still contained the VNet at t+1.39 s while `…/subnets` already 404'd at
t+1.66 s; LIST was clear by t+1.97 s). The window that matters is **Bastet's own**: the list response
is a point-in-time snapshot and the N serial `vnet.GetSubnets()` calls follow it inside the same
request — measured at ~1.3–2 s over 5–6 VNets on this rig, proportionally minutes for a large
subscription. Any VNet deleted in that span poisons the run.

**Consequence, stated precisely:** every consumer fails **closed**. Nothing wrong is persisted, no
row is lost, and the condition self-clears on the next request. This is a wrongly-attributed
transient refusal, not a data defect — which is why it is medium and not high.

One verifier additionally observed that ARM `429 ResourceCollectionRequestsThrottled` on
`virtualNetworks/read` escapes the same inner loop to the same catch and yields the identical
whole-subscription failure, **with no delete involved**. Same 1+N structure; it strengthens the
primary fix.

### Evidence — reproduced: yes, driven against live ARM, twice, first attempt each time

Verifier 1 (port 5233, catalog `bastet_verify_azure`, SP_A): created `rig-verify-azure-uks` in
uksouth (the region-grouped listing puts it last, widening the window), issued
`GET /Azure/BulkGetVNets` on a thread, slept 350 ms, then `DELETE …/rig-verify-azure-uks` (202 at
t+0.81 s). The in-flight request returned
`{"success": false, "error": "Azure could not be read for this subscription. Details have been logged."}`
with `app.log` showing `Azure.RequestFailedException: Resource …/RIG-VERIFY-AZURE-UKS not found`,
stack `SubnetsGetAllAsyncCollectionResultOfT.GetNextResponseAsync → … → AzureService.GetVNetInventory
… AzureService.cs:line 344` then `… line 317`. Repeated for reconcile: `POST /Azure/ReconcileScan` →
HTTP 200, `scanSucceeded=False`, `globalErrors=['Could not read VNets from Azure, so nothing can be
reported as deleted: …']`, `items=0`.

Verifier 2, independently (port 5378, catalog `bastet_v404reach`, own fixtures `rig-v404reach-*`):
three sequential requests around one DELETE —

```
start= 0.00 end= 1.91 success=True   target_in_result=True
t= 3.03 DELETE -> 202
start= 1.91 end= 3.40 success=False  err="Azure could not be read for this subscription. …"
start= 3.40 end= 4.80 success=True   (self-heals on the very next request)
```

Both verifiers deleted their fixtures afterwards; `git status --porcelain --untracked-files=all`
empty.

### Fix — primary verified sound, interim corrected

**Primary.** Read the subnets already present on the listing instead of re-fetching them: iterate
`vnet.Data.Subnets` rather than `vnet.GetSubnets()`. This removes the N round-trips, the throttling
surface and the failure mode entirely. **Verified against the real SDK** (Azure.ResourceManager.Network
1.16.1) in a standalone console app: `vnet.Data.Subnets` comes back fully populated from the list
response — name, `Id` (a `ResourceIdentifier` byte-identical to the `subnet.Id.ToString()` the
current code emits, same casing), `AddressPrefix`, and `AddressPrefixes` including the multi-prefix
fixtures `multi` (2 prefixes) and `g2multi` (3). No information is lost.
`ExtractIpv4Prefix`/`ExtractIpv4Prefixes` are **private and called only from `GetVNetInventory`**
(`:346`, `:360`), so retype both parameters from `SubnetResource` to `SubnetData` — no overload is
needed and no caller breaks.

**Interim — corrected; the finder's version is unsafe.** Wrapping the inner `await foreach` in
`catch (global::Azure.RequestFailedException) { continue; }` also swallows **429, 403 and 500**, and
because the VNet is then simply *absent* from the inventory, a throttled VNet silently vanishes from
the bulk-import wizard. That is the exact "empty subscription vs. Azure could not be reached"
conflation the fail-loud comment at `AzureController.cs:227-230` exists to prevent — and unlike the
reconcile path, bulk import has **no** `ConfirmProposedDeletionsAsync` re-read to catch it. Narrow
the catch to `catch (global::Azure.RequestFailedException ex) when (ex.Status == 404)`, log the VNet
name, and `continue`; rethrow everything else.

---

## G4 — `BASTET_LOG_LEVEL_DEFAULT` does nothing, and `BASTET_LOG_LEVEL_ENTITYFRAMEWORK` cannot silence the SQL: `appsettings.json`'s shipped rules outrank both `[x1]`

**File:** `src/Bastet/Program.cs:20`
**Also:** `:22`, `src/Bastet/appsettings.json:7` and `:9`, `README.md:141` and `:143`
**Confidence:** confirmed

### Scenario

An operator deploys to Container Apps with logs shipped to a shared workspace and, following
`README.md:141/143` (*"Default logging level for all categories"*, default `Warning`, *"Only applied
in non-development environments"*), sets `BASTET_LOG_LEVEL_DEFAULT=None` and
`BASTET_LOG_LEVEL_ENTITYFRAMEWORK=None`. The application still writes **the complete text of every
SQL statement it executes**, plus its host lifetime messages, at Information. The same operator,
troubleshooting later, sets `BASTET_LOG_LEVEL_DEFAULT=Debug` and gets **no additional output at
all**. The knob is inert in both directions.

Mechanism: `SetMinimumLevel` writes `LoggerFilterOptions.MinLevel`, which `LoggerRuleSelector.Select`
consults **only when no rule matches**; `appsettings.json:7`'s `"Default": "Information"` installs a
rule with a null `CategoryName` that matches every category. And `appsettings.json:9`'s
`"Microsoft.EntityFrameworkCore.Database.Command"` is a strictly longer prefix than the code's
`AddFilter("Microsoft.EntityFrameworkCore", …)` at `:22`, so it wins for the one EF category that
prints queries. `BASTET_LOG_LEVEL_ASPNETCORE` works because its rule is exactly equal in length and,
being registered after the configuration rules, wins the tie.

**Two corrections from the verifiers, both load-bearing:**

- The finder's *"forever, with no configuration that can stop it"* is **too absolute and must be
  dropped**. Measured against the unmodified tree, the standard framework variables
  `Logging__LogLevel__Default=None` and
  `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=None` reduce the same run to
  **0 log lines**. The defect is therefore not unsuppressable logging — it is that **the knob Bastet
  ships and documents is a lie**: the operator sets it, gets no error, no warning and no effect, and
  believes the sink is quiet.
- There is **no** `EnableSensitiveDataLogging` anywhere in the tree, so EF renders parameter values
  as placeholders. The exposure is SQL text and schema shape, not subnet or host-IP data. This is a
  log-volume and troubleshooting defect, not an information-disclosure one.
- The finder's fixed count of "19 Information entries" is boot-dependent (20 on a warm boot, 42–47
  on a migrating first boot) and should be stated qualitatively.

### Evidence — reproduced: yes, ran the shipped DLL, both verifiers, plus a framework harness

Verifier 1 (port 5468, catalog `bastet_vloglvl`), Production, boot + two requests, categories counted
with `grep -oE '^[a-z]+: [A-Za-z.]+' | sort | uniq -c`:

- **All three knobs = `None`:** 15 `info: Microsoft.EntityFrameworkCore.Database.Command` (each
  printing full SQL — `CREATE DATABASE`, `ALTER DATABASE … READ_COMMITTED_SNAPSHOT ON`, the migration
  history SELECTs) + 5 `info: Microsoft.Hosting.Lifetime`.
- **`DEFAULT=Debug, ASPNETCORE=Debug, ENTITYFRAMEWORK=Warning`:** 207 `dbug:` lines, of which **0**
  outside `Microsoft.AspNetCore.*`, and the 15 EF Information lines still present.
- **All unset:** identical 15 + 5.

A standalone harness loading the identical rule set confirmed the mechanism directly:
`…Database.Command`, `Microsoft.Hosting.Lifetime` and `Bastet.Whatever` all report
`IsEnabled(Information) == True` after `SetMinimumLevel(LogLevel.None)`.

Verifier 2 (port 5299, catalog `bastet_vlogreach`) reproduced all three runs independently and found
the framework-variable escape hatch quoted above.

**The fix was measured**, using an alternate `--contentRoot` with symlinked `Views`/`wwwroot` so the
repository was never touched: with the `Logging` section removed from `appsettings.json`, `None` →
**0-line log**; unset → only 2 `fail` + 1 `warn`; `ENTITYFRAMEWORK=Information` → the 15
`Database.Command` lines return; `DEFAULT=Debug` → `dbug: Microsoft.Extensions.Hosting.Internal.Host`
lines appear, i.e. debug output outside `Microsoft.AspNetCore` for the first time. Development
category tallies diffed **identical** to HEAD.

### Fix

Move the whole `Logging` section out of `src/Bastet/appsettings.json` into
`src/Bastet/appsettings.Development.json`. With no configuration-derived rules present in
Production, `SetMinimumLevel` becomes the matching-rule fallback again and the
`Microsoft.EntityFrameworkCore` filter becomes the longest match for `…Database.Command`.

Three notes for whoever applies it:

- `appsettings.Development.json` already carries `"Default": "Information"` and
  `"Microsoft.AspNetCore": "Information"`, so the only key that genuinely needs adding is
  `"Microsoft.EntityFrameworkCore.Database.Command": "Information"`. That is a real change to
  Development inputs (verified behaviour-neutral, because Development currently inherits it from the
  base file) and the commit should say so.
- Removing the section changes the shipped Production default from Information to Warning — which is
  what `README.md:141` already promises — but the `"Now listening on: …"` / `"Application started"`
  `Microsoft.Hosting.Lifetime` lines then disappear at the default level. If those are wanted
  unconditionally, keep them with `AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information)`,
  not by leaving the appsettings rules in place.
- `README.md:141-143` should stop saying *"In development, falls back to appsettings.json"* for a
  section that will no longer be in `appsettings.json`, and should mention that `Logging__LogLevel__*`
  remains a higher-precedence override.

### Interim fix — do not take it

Replacing `SetMinimumLevel(level)` with `AddFilter((string?)null, level)` plus a
`"Microsoft.EntityFrameworkCore.Database.Command"` filter does work mechanically, **but it also wins
the null-category tie against `Logging__LogLevel__Default`** — the one override an operator has
today — converting a working escape hatch into a second inert knob. Take the primary fix.

---

# Low

## G5 — A request waiting for the subnet lock already holds a pooled SQL connection, so a burst of writers exhausts the pool and read-only pages return HTTP 500 `[x2]`

**File:** `src/Bastet/Services/Locking/SqlServerSubnetLockingService.cs:34`
**Also:** `:22` (`DEFAULT_TIMEOUT_MS = 30000`), `:37`, `src/Bastet/Program.cs:53`, `README.md:129`
**Confidence:** confirmed
**Severity corrected from medium to low by the verifier** — see below.

### Scenario

One mutating operation holds `Bastet:SubnetOperations`. Other write requests arrive; each enters
`ExecuteWithSubnetLockAsync`, calls `context.Database.OpenConnectionAsync()` at `:34` — checking a
connection **out of the pool** — and only then blocks inside `sp_getapplock` at `:37` for up to 30 s.
A queued writer therefore occupies a pooled connection for the whole wait while doing no work. At
100 queued writers the pool is empty (SqlClient's default `Max Pool Size`; `grep` finds no
`Max Pool Size` / `Pooling` anywhere in `src`, `README.md` or `appsettings`, and both documented
`BASTET_CONNECTION_STRING` samples omit it, so the default applies in every documented deployment).

Every subsequent request then fails at connection acquisition after the 15 s pool timeout —
**including read-only requests that take no lock and need one SELECT**. `GET /Subnet/Index` → HTTP
500, `Timeout expired … max pool size was reached`.

**Corrected by the verifier:** the finder's claim that *"the writers themselves degrade gracefully"*
holds only for the first 100. In the 110-writer run, 10 writers never got a connection at all — they
threw the pool-exhaustion `InvalidOperationException` at `:34`, which is **not** a `TimeoutException`,
so `catch (TimeoutException)` at `SubnetController.Create.cs:128` is bypassed and the operator sees
the generic *"Error creating subnet. Details have been logged."* instead of the modelled contention
message. Same for the JSON endpoints, where the contended-lock contract is 503.

**Why low, not medium (the verifier's ruling):** reaching the threshold needs ~100 concurrent
authenticated Edit-role writers *per replica* coinciding with a lock held long enough for them to
pile up. Bastet is an IPAM tool with no fan-out — no single user action generates concurrent writes —
so this is far outside plausible operation for its user population. The outage self-heals within 30 s
and nothing is corrupted (`sys.dm_tran_locks` showed **0** `APPLICATION` rows after every run). What
keeps it a finding at all is the amplification, isolated cleanly by control (c) below.

### Evidence — reproduced: yes, against real SQL Server, with three controls, and the fix measured

Verifier ran its own instance (port 5271, catalog `bastet_verify_lockpool`, unmodified build at
`6a1fe75`) against the shared SQL Server 2022 with the default connection string.

**Main run:** held `Bastet:SubnetOperations` from an outside `sqlcmd` session
(`sp_getapplock` Exclusive/Session + `WAITFOR 00:02:00`), then fired 110 concurrent authenticated
`POST /Subnet/Create` with real antiforgery token and cookie. After 10 s,
`SELECT wait_type, COUNT(*) FROM sys.dm_exec_requests` → `LCK_M_X = 100` exactly. A plain
`GET /Subnet/Index` issued at that moment → **HTTP 500 after 15.016 s**; `app.log` records
`System.InvalidOperationException: Timeout expired. The timeout period elapsed prior to obtaining a
connection from the pool … max pool size was reached`, stack ending at `SubnetController.Index() …
Read.cs:line 16`, with eleven sibling stacks naming
`SqlServerSubnetLockingService.ExecuteWithSubnetLockAsync … SqlServerSubnetLockingService.cs:line 34`.

**Controls:** (a) 60 queued writers under the same held lock → `LCK_M_X = 60`, reader 200 in 0.013 s;
(b) 110 concurrent `GET /Subnet/Index` with no lock held → all 200, reader 200 in 0.003 s;
(c) 110 concurrent `POST /Subnet/Create` with **no** external lock holder → all 302, reader 200 in
0.004 s. So it is not HTTP concurrency or write load — it is queued waiters pinning connections,
thresholded at exactly 100.

**Fix measured:** the verifier copied `src/` to scratch (repository untouched), added the
`SemaphoreSlim` gate, rebuilt (0 warnings, 0 errors) and reran the identical experiment:
`LCK_M_X = 1`, `GET /Subnet/Index` **200 in 0.128 s**, zero `max pool size` entries, all 110 writers
returned at 30.04–30.11 s (**not** 60 s), two contended writers rendered the modelled *"The operation
timed out due to high concurrency. Please try again."*, an uncontended write still succeeded (302),
and `APPLICATION` locks = 0 afterwards.

### Fix

Do not hold a pooled connection while merely waiting. Put a process-local gate in front of the
database lock so at most one request per replica is ever parked inside `sp_getapplock`:

- add `private static readonly SemaphoreSlim _localGate = new(1, 1);`
- in `ExecuteWithSubnetLockAsync`, wait on `_localGate` for the caller's timeout, and only then
  `OpenConnectionAsync()` / `AcquireAppLockAsync()`; release `_localGate` in an **outer** `finally`
- charge the gate wait against the same overall budget (pass the remaining time as `sp_getapplock`'s
  `@LockTimeout`) so a contended caller still surfaces `TimeoutException` in ~30 s rather than 60,
  and the existing contention messages keep their meaning.

Safe for multi-replica: `sp_getapplock` remains the cross-replica mutex and its semantics are
untouched; the gate only caps how many connections **one replica** can park in it, from unbounded to
one. All ten `ExecuteWithSubnetLockAsync` call sites are top-level in controller actions with no
nesting, so the gate cannot self-deadlock.

A no-static-state alternative with the same effect: acquire with `@LockTimeout=0` and retry in a
short backoff loop, calling `CloseConnectionAsync()` between attempts so the connection returns to
the pool while waiting.

### Interim fix

Append an explicit `Max Pool Size=` well above expected write concurrency to
`BASTET_CONNECTION_STRING` and document it in `README.md`'s connection-string table, and/or lower
`DEFAULT_TIMEOUT_MS` (`:22`) so a queued writer frees its connection sooner. Both move the threshold
rather than removing the amplification.

---

## G6 — Authenticated listing pages ship with no cache directives at all; the protection is present only by accident on pages that emit an antiforgery token `[x1]`

**File:** `src/Bastet/Program.cs:438`
**Also:** `:443`, `:38`
**Confidence:** confirmed

### Scenario

On a shared or kiosk workstation, user A signs in and opens `/HostIp/AllHostIps` (every host IP
assignment Bastet knows) and `/Subnet` (the whole address-space tree). A signs out via
`/Account/Logout`, which deletes the cookies. User B, on the same browser, presses **Back**. Because
the response carried no `Cache-Control` and no validator, the browser serves the stored
representation without revalidating and renders A's full inventory to B.

The same URLs one click away — `/Subnet/Details/1`, `/Subnet/Create`, `/Subnet/Edit/1` — **are**
protected, because the antiforgery middleware sets `Cache-Control: no-cache, no-store` +
`Pragma: no-cache` on views that render a token. Nothing in the application does. The coverage
therefore tracks *"does this view happen to render an antiforgery token"*, not *"is this page
authenticated"*.

**Corrected by the verifiers — two mechanisms, both measured, and this matters for the fix
rationale:** verifier 1 proved the **HTTP disk cache** path (the response body is written to a file
in `Chrome/Default/Cache/Cache_Data` keyed on the URL, survives the browser process, and is served on
history navigation without revalidation); verifier 2 proved the **back/forward cache** path (same
tab, **zero network requests at all**). A bfcache-only problem would not be fixed by a header; the
disk-cache path is, which is why the header fix is the right one.

The exposure is also **wider than the four actions in the interim fix**: `/` (Home),
`/Account/Roles`, `/HostIp/Index?subnetId=` and `/HostIp/DeletedHostIps` are all authenticated, all
carry inventory or role data, and all ship with no directives.

### Evidence — reproduced: yes, in a real browser, twice, with a differential control

Headers (verifier 1, PID-confirmed own instance): `/`, `/Subnet`, `/HostIp/AllHostIps`,
`/Account/Roles`, `/Subnet/DeletedSubnets`, `/HostIp/AllDeletedHostIps` → 200 `text/html` with **no**
`Cache-Control`, **no** `Pragma`, **no** `Expires`, **no** `ETag`/`Last-Modified`.
`/Subnet/Create`, `/Subnet/Edit/1`, `/Subnet/Details/1` → `Cache-Control: no-cache, no-store` +
`Pragma: no-cache`.

**Storage proven:** created subnet `SECRETINVENTORY7` / `10.99.0.0/16` desc `zzconfidentialzz`,
loaded `/Subnet` in Chrome with a persistent profile; the disk-cache file
`Default/Cache/Cache_Data/cdce0916ba833241_0` is keyed `http://127.0.0.1:5259/Subnet` and `grep -a`
finds both strings in it. `/Subnet/Details/1` and `/Subnet/Edit/1` produce **no cache entry at all**.

**Scenario driven end to end over CDP:** navigate `/Subnet` → navigate `/Account/Logout` →
`Network.clearBrowserCookies` → **kill the application** (`curl` → 000, connection refused) →
`history.back()`. Result: `location.href = …/Subnet`, `document.title = "Subnet Hierarchy - BASTET"`,
`SECRETINVENTORY7` present in the rendered DOM, **zero `Network.*` events**. Differential control on
`/Subnet/Details/1` (the no-store page): back gave `chrome-error://chromewebdata/`,
`net::ERR_CONNECTION_REFUSED`, no marker, nothing on disk.

Verifier 2 reproduced independently with a staleness test rather than a shutdown: loaded `/Subnet`
showing `SECRET-INVENTORY-ALPHA`, renamed the subnet server-side to `RENAMED-INVENTORY-BETA`
(confirmed by `curl`), then `Page.navigateToHistoryEntry` back → DOM contained ALPHA, not BETA, with
**zero** `requestWillBeSent` events. Same procedure on `/Subnet/Details/1` → stale value absent,
fresh value present, `responseReceived 200 fromDiskCache False`.

**Fix measured:** the patched build (scratch copy, repository untouched, 0 warnings/0 errors) returns
`Cache-Control: no-store,no-cache` + `Pragma: no-cache` on `/`, `/Subnet`, `/HostIp/AllHostIps`,
`/Account/Roles`, `/Subnet/Details/1`, `/Subnet/Create`, `/Error/404` and `/Account/SignedOut`, while
`/css/site.css` and `/js/site.js` stay cacheable. Re-running the CDP scenario against it: back after
logout with the server down → `chrome-error://chromewebdata/`, no marker, nothing in the disk cache.

### Fix

Register a global response-cache filter beside the existing `GlobalSanitizationFilter` at
`Program.cs:38`:

```csharp
options.Filters.Add(new ResponseCacheAttribute { NoStore = true, Location = ResponseCacheLocation.None });
```

`ResponseCacheAttribute` is an `IFilterFactory`, so `FilterCollection` accepts an instance directly.
It emits the directives on **controller responses only**, so `UseStaticFiles` keeps serving CSS/JS
cacheably. Bastet maps only controllers and returns no `FileResult` anywhere, so this is complete
coverage for the HTML surface. It names no scheme — **plain-HTTP and air-gapped hosting are
unaffected**. Note that line 38 becomes a braced lambda registering both filters, and the
`Microsoft.AspNetCore.Mvc` types must be in scope.

### Interim fix — incomplete on its own terms

`[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]` on `SubnetController.Index`,
`SubnetController.DeletedSubnets`, `HostIpController.AllHostIps` and
`HostIpController.AllDeletedHostIps` closes the pages carrying the most data, but leaves `/`,
`/Account/Roles`, `/HostIp/Index` and `/HostIp/DeletedHostIps` uncovered. Prefer the global filter.

---

## G7 — The OIDC handler requests and stores a refresh token that no code in the application ever reads `[x1]`

**File:** `src/Bastet/Program.cs:206`
**Also:** `:199` (`SaveTokens = true`), `:200` (`UseTokenLifetime = true`),
`src/Bastet/Controllers/AccountController.cs:51`
**Confidence:** **corrected from plausible to confirmed** — the finder could not drive an OIDC
sign-in and inferred the token's presence; both verifiers built a spec-conformant IdP, drove a real
sign-in, and decrypted the cookie.

### Scenario

A deployment follows the configuration `README.md:139` explicitly supports — *"providers that support
PKCE without client authentication (e.g., Auth0)"*, i.e. `BASTET_OIDC_CLIENT_SECRET` unset, a public
client. A user signs in; the token response includes a refresh token because `offline_access` was
requested, and `SaveTokens = true` writes it into the auth cookie. **Bastet never uses it** — the
only hit for `GetTokenAsync|access_token|SaveTokens|id_token|refresh_token` across `src/` and `test/`
is the `SaveTokens` line itself.

Anyone who can both read the Bastet database (the DataProtection keys are persisted there
unencrypted by `Program.cs:100-102`) and capture one cookie can decrypt it and lift a long-lived IdP
credential that Bastet had no reason to hold. For a public client it is redeemable against the IdP
with only the public client id.

**Corrections carried from the verifiers:**

- The scenario's *"the cookie's own lifetime is 1 hour (`Program.cs:187`)"* is wrong.
  `UseTokenLifetime = true` at `:200` overrides `ExpireTimeSpan` — the ticket expiry comes from the
  id_token. Measured: `.issued Tue, 28 Jul 2026 03:00:10 GMT` / `.expires 04:00:10 GMT` against a
  1-hour id_token. The substance is unchanged.
- **Honest framing of the precondition:** whoever can read the database already has the unencrypted
  DataProtection keys and can forge any Bastet session. The marginal loss is specifically the
  **IdP-side** credential — reach *beyond* Bastet's blast radius. That is what keeps this at low and
  no higher.
- **A second cost the finding does not mention:** with a realistically sized (4 KB, Entra-shaped)
  refresh token the auth cookie crossed 4096 bytes and the framework **chunked** it —
  `.AspNetCore.Cookies=chunks-3` with `C1`/`C2`/`C3` totalling 8526 bytes, against 3142 with no
  refresh token. Every request from every signed-in user carries ~5.4 KB of extra header for a value
  nothing reads, and some reverse proxies cap request-header size.

### Evidence — reproduced: yes, real OIDC sign-in against a purpose-built IdP, cookie decrypted, twice

Verifier 1: built a spec-conformant RS256 OIDC provider (discovery/JWKS/token/userinfo) on
`https://localhost:18259` behind a locally generated CA, and ran the shipped
`bin/Debug/net10.0/Bastet.dll` in **Production** as a **public client** (`BASTET_OIDC_CLIENT_ID` set,
**no** `BASTET_OIDC_CLIENT_SECRET`) on `http://127.0.0.1:18260`, own catalog `bastet_verify_oidc_rt`.

1. `GET /` → 302 to the IdP with `scope=openid profile email roles offline_access` — the app really
   does ask for a refresh token, from an anonymous GET of the default route.
2. Completed the callback; the token endpoint returned
   `refresh_token = "RTMARKER." + 280x"R" + ".RTEND"`. `Set-Cookie: .AspNetCore.Cookies` = 3376 chars.
3. Control run, identical except the token endpoint omitted `refresh_token`: cookie 2928 chars.
4. Pulled the single `DataProtectionKeys` row out of the application database via `sqlcmd`; the XML
   contains a plaintext `masterKey` and no `encryptedSecret`. A 30-line console app rebuilding the
   protector (`…CookieAuthenticationMiddleware`, `Cookies`, `v2`) unprotected the cookie:
   ```
   PROP .Token.refresh_token (len=295) = RTMARKER.RRRR…RTEND
   PROP .TokenNames = access_token;id_token;refresh_token;token_type;expires_at
   ```
   Control cookie: no `.Token.refresh_token`; `.TokenNames = access_token;id_token;token_type;expires_at`.
5. **Fix check:** `GET /Account/Logout` with the auth cookie → 302 to
   `…/endsession?…&id_token_hint=<924 chars>&state=…`. `SaveTokens` **is** load-bearing.

Verifier 2 reproduced the whole chain independently (own IdP on `127.0.0.1:5473`, app on `:5273`,
catalog `bastet_lblreach`-adjacent), measured the 4 KB chunking above, and found one additional fact
that changes the fix (below).

The only step neither verifier could drive is whether a *commercial* IdP returns a refresh token at
all; that is documented Auth0/Entra behaviour for a granted `offline_access` and is not Bastet code.
Every step that **is** Bastet code was measured.

### Fix — corrected; the one-line deletion is necessary but not sufficient

Delete `options.Scope.Add("offline_access");` at `Program.cs:206`. **Keep `SaveTokens = true`** — the
saved id_token is what supplies `id_token_hint` on the end-session request at
`AccountController.cs:51-54`, measured present.

Two additions, both from the verifiers:

1. **`README.md:60-65` lists `offline_access` — annotated *"(for refresh tokens)"* — among the scopes
   the operator's IdP must support and grant.** Delete that bullet in the same commit, leaving
   `openid`/`profile`/`email`/`roles`. Otherwise the documentation keeps telling every deployment to
   grant a scope the app no longer asks for and to believe Bastet uses refresh tokens.
2. **`SaveTokens` has no scope gate — measured.** With the token response granting only
   `"openid profile email roles"` (offline_access explicitly **not** granted) but still returning a
   refresh token, the decryptor still found
   `ITEM .Token.refresh_token len=326 value=UNSOLICITED-REFRESH-TOKEN-UUU…`. So on an IdP that
   returns a refresh token for the authorization-code grant without being asked (Keycloak's standard
   flow does exactly this), dropping the scope does **not** stop the token reaching the cookie. If a
   guarantee is wanted rather than a best effort, add an `Events.OnTicketReceived` handler — which
   runs after `SaveTokens` — that re-stores only the id_token:
   ```csharp
   ctx.Properties!.StoreTokens(ctx.Properties.GetTokens().Where(t => t.Name == "id_token").ToList());
   ```
   That also drops the equally unused access_token and shrinks the cookie further.

Nothing outbound is added or removed; the Development `DevAuthHandler` branch never reaches this
code. Plain-HTTP and air-gapped hosting are unaffected.

### Interim fix

None cheaper than the one-line scope deletion.

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
