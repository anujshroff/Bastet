---
name: e2e
description: Run a full end-to-end verification pass over Bastet against a live rig - real SQL Server, the real application, a real browser and a live Azure subscription. Covers Azure discovery, both import wizards, the reconciler, ARM failure modes, the client-side wizard state machines, core IPAM behaviour, authorization and locking. Use when asked to "run the e2e tests", "test everything end to end", "verify the whole app works", or after a round of fixes when a green unit suite is not enough. Pass "auto" to run straight through. To find new defects use /audit; to fix an existing findings file use /audit-reconcile.
---

# Run a full end-to-end pass

One operator-run pass over the whole application against a live rig. It asks for its inputs once,
proves its own prerequisites, builds the rig, runs a fixed scenario matrix, reports a pass/fail table,
and tears everything down with the teardown **verified by enumeration rather than asserted**.

Nothing this skill produces enters the repository. Scripts, rigs, scratch copies and logs all live in
the scratchpad and die with the machine. The only durable artefact is the report in the conversation.

## Mode

**Default:** run each phase and report as you go.
**`/e2e auto`:** run straight through to the final report, pausing only for a missing prerequisite, a
missing credential, or a result that cannot be classified without a decision.

**State which mode is active before the first phase.**

## Why this exists

A green unit suite does not establish that the application works. Several classes of defect compile
cleanly, pass every test, and fail only when a page is requested or a wizard is clicked:

- Razor resolves views, partials and imports at **render** time.
- What a wizard actually POSTs is decided by `disabled` attributes at submit time, and jQuery's
  `.prop()` fires no `change` event - so the model the JS holds and the model the server binds diverge.
- The reconciler is the only code that **deletes** on the strength of what an external system reports,
  and its fail-closed rules only mean anything against real ARM failures.
- `sp_getapplock` cannot be exercised at all by the test suite, which runs SQLite.

---

# Ask once, for inputs only

Ask for everything missing in **one message**, before anything runs. Never trickle questions out, and
never ask again later.

- tenant id
- subscription id
- two service principals - client id and secret each, with **disjoint** RBAC over two resource groups
- both resource group names

**Ask nothing else. Ever.** Not scale, not which phases, not "shall I proceed". This skill makes no
commits, so git identity is irrelevant - do not check for it and do not ask.

**Credentials are never stored.** Written once to `<rig>/env.sh`, referenced as shell variables, never
placed on a command line, never written into the repository, never committed. The final report reminds
the owner to revoke them.

Why two principals with disjoint scope: the reconciler's withhold path is the difference between
"deleted" and "I cannot see it", and a credential that sees everything proves nothing about it. One
principal seeing both groups makes half this suite vacuous while still appearing to pass.

---

# Preflight - prove every prerequisite, then stop naming what is missing

Assume nothing is installed. The target is a fresh Debian box with a checkout and nothing else.

**Policy: install what installs unattended; stop and name what does not.** Anything needing `sudo`, a
group change or a re-login is the owner's action - report the exact command and stop, rather than
half-configuring the machine.

| check | how | on failure |
|---|---|---|
| .NET SDK | `dotnet --version`, major matching the `TargetFramework` in the `.csproj` files | **Stop.** Name the required SDK |
| Docker daemon **as this user** | `docker info` | **Stop.** A group-membership fix needs a re-login you cannot perform |
| SQL Server image | `docker image inspect mcr.microsoft.com/mssql/server:2022-latest` | Pull it **here**, once |
| Browser | chromium under `~/.cache/ms-playwright` | **Stop** if absent - phase G is worthless without it |
| Python 3 + `requests` | `python3 -c "import requests"` | Install into the rig venv |
| **Azure CLI** | `az version` | Install **here**, once (see below). **Stop** if it cannot be installed |
| `curl` | `curl --version` | Install |
| Disk headroom | SDK + image + browser + `bin`/`obj` + scratch copies | **Stop** if tight; a mid-run `ENOSPC` looks like something else |
| Memory / cores | `free -g`, `nproc` | Report. Several app instances plus SQL Server plus a browser is the peak |
| Network egress | NuGet, `mcr.microsoft.com`, `management.azure.com` | **Stop** naming the unreachable host |
| Free TCP ports | one per app instance; pick a private block and record it | Pick fresh |
| Repo tree | `git status --porcelain` clean; record branch and HEAD | **Stop** if dirty - a run must never be confused with uncommitted work |
| Baseline | `dotnet build --no-incremental` (expect 0 warnings) and `dotnet test` | **Stop and report** rather than testing a broken tree |

## Azure CLI, if absent

Installs unattended with no `sudo`:

```bash
python3 -m venv <rig>/azcli
curl -sSL https://bootstrap.pypa.io/get-pip.py | <rig>/azcli/bin/python -
<rig>/azcli/bin/pip install azure-cli
```

Debian's `venv` ships without `ensurepip`, so `python -m ensurepip` fails and the bootstrap script is
the way through. Record the absolute path to the `az` binary. That venv's python also carries
`requests`, which is what the drivers use.

## The browser

Playwright's chromium is normally already on disk, but **there is no Node and no `playwright` CLI**.
Two ways to use it, and phase G needs the second:

```bash
# DOM snapshot only - enough for rendering assertions
CH=~/.cache/ms-playwright/chromium-*/chrome-linux64/chrome
"$CH" --headless --no-sandbox --disable-gpu --disable-dev-shm-usage \
      --virtual-time-budget=8000 --dump-dom http://127.0.0.1:<port>/ > out.html

# real interaction - install the python binding into the rig venv
<rig>/azcli/bin/pip install playwright     # browsers already cached; do NOT run `playwright install`
```

The `optimization_guide_on_device_model_installer` line on stderr is benign noise.

## Azure credentials - the discrimination is the point

1. **Confirm both resource groups exist and are distinct first.** A typo'd group returns 403 and is
   indistinguishable from a missing role assignment, which sends you debugging RBAC that was never
   wrong.
2. For each principal: log in with its own `AZURE_CONFIG_DIR` so two logins do not overwrite one
   another, then

   ```bash
   az role assignment list --all --assignee "$SP_A_CLIENT_ID" \
      --query "[].{role:roleDefinitionName,scope:scope}"
   ```

   **Assert the scope is a resource group, not the subscription.** A single subscription-scoped
   assignment inherits into both groups, filters nothing, and makes every visibility test vacuous.
3. Prove the matrix both ways, for reads and writes: each principal 200 on its own group and **403** on
   the other. Record the observed matrix.
4. If the matrix does not reproduce, **stop and name the failing leg.**

**`AZURE_TOKEN_CREDENTIALS` must be unset** when driving the application. The launch profiles set it to
`dev`, which excludes `EnvironmentCredential` and produces a credential failure that reads exactly like
a permissions problem. Export `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` / `AZURE_TENANT_ID` so
`DefaultAzureCredential` picks them up and the production code path runs unmodified.

---

# The rig

**Sweep the wreckage of a dead run first.** A run killed mid-flight leaves its container and its cloud
fixtures behind; building on top of them means testing state this run did not create. Remove stale
containers and any leftover fixtures in **both** resource groups before standing anything up.

Then:

- **SQL Server container** on a private port, with a strong password.
- **App instances** on private ports and private catalogs, `BASTET_AUTO_MIGRATE=true`,
  `BASTET_AZURE_IMPORT=true`, `ASPNETCORE_ENVIRONMENT=Development`. Start each with a wrapper script
  and capture its PID.
- **Azure fixtures**, built with `az`, named with a run-specific prefix.
- **An inventory file**: append the full resource id of every Azure resource created, one per line.
  That file is what teardown deletes.

## Fixture matrix

Build all of it. Each row exists because something in the application behaves differently for it.

| fixture | shape | what it exercises |
|---|---|---|
| simple | one `/16`, two `/24` subnets | the ordinary path |
| multi | subnet with 3 prefixes, non-contiguous, supplied out of order | one row per prefix, prefix-qualified names |
| fiveprefix | subnet with 5 prefixes, mixed `/24` and `/25` | scale of the same |
| twospace | VNet with two address prefixes, one subnet spanning both | per-target and per-child qualification |
| encompass | subnet covering the entire VNet prefix | fully-allocated marking, no child created |
| dual | dual-stack VNet **and** dual-stack subnet | IPv4 extracted, IPv6 dropped everywhere |
| overlap-a / overlap-b | two VNets with identical address space, same subnet prefix in each | overlapping RFC1918 must not cross-match |
| clash | a VNet whose prefix equals an already-linked Bastet target | the different-VNet refusal |
| edges | `/12` VNet with `/29` at range start and `/28` at range end | boundary CIDRs |
| empty | VNet with no subnets | target created, no children |
| many | VNet with ten subnets | scale |
| delegated | a delegated subnet | delegation must not change IPAM behaviour |
| longname | 59-char VNet name, 76-char subnet name with 2 prefixes | truncation inside the 100-char column |
| hidden | a VNet in the **other** resource group | the withhold path |

**Verify the fixture reproduces the ARM shape before trusting any of it.** Fetch a multi-prefix subnet
back and confirm singular `addressPrefix` is **null** while `addressPrefixes` is populated:

```bash
az network vnet subnet show -g <rg> --vnet-name <v> -n <s> \
   --query "{singular:addressPrefix,plural:addressPrefixes}"
```

That null is the exact shape the application must handle. A fixture that does not reproduce it makes
phases A-D vacuous while appearing to pass.

## Two fixtures are built LATER, not here

- **clash** - a VNet whose prefix equals an **already-linked** Bastet target. That state cannot exist
  until phase B has imported something, so build it at the start of **phase D**. Built up front it is
  just another overlapping pair, which `overlap-a`/`overlap-b` already cover.
- **hostip** - an empty VNet, imported in phase D to create an **empty** target, which then has a host
  IP added to it. See the D5 note below for why it must be empty.

## ARM serializes writes against a single VNet

Creating two subnets on the **same** VNet concurrently fails one of them with *"Another operation on
this or dependent resource is in progress"*. Building the matrix naively in parallel silently drops
subnets and every later phase then measures the wrong thing.

So: **parallel across VNets, serial within a VNet.** Make the builder idempotent - check with
`az network vnet subnet show` before creating - and retry the conflict a few times with a short sleep.
Then **verify the matrix by counting subnets per VNet against what was asked for** before running any
phase. This was observed: a first parallel build produced 3 of 10 subnets on the ten-subnet fixture and
1 of 2 on four others, and every count looked plausible until compared against the expectation.

Azure notes: subnet minimum is `/29`; VNet names max 64 chars, subnet names max 80; a full matrix build
takes a couple of minutes even parallelised, so use a generous command timeout, not the 2-minute
default.

---

# Scenario matrix

Each phase is a driver script under the rig directory with a shared harness that prints
`PASS`/`FAIL` per check and exits non-zero on any failure. Every check states the behaviour it expects,
so a failure names the behaviour rather than a mismatched string.

## A - Discovery and annotation (read-only, nothing imported)

Every VNet in the visible group discovered and the hidden one absent; the dual-stack VNet offering
only its IPv4 prefix and its dual-stack subnet appearing exactly once carrying only IPv4 with no `:`
anywhere; the 3-prefix subnet emitting three rows **each carrying the complete prefix list**; the
5-prefix subnet emitting five; the two-address-space VNet offering both and its spanning subnet
appearing under each; the two overlapping VNets both discovered with distinct resource ids for
identically-prefixed subnets; `/29` and end-of-range subnets discovered; the empty VNet offered with
its prefix and no subnets; the ten-subnet VNet returning all ten; the delegated subnet treated
ordinarily; **every prefix and every subnet selectable on a clean tree**; and both wizards' discovery
endpoints agreeing, including the single-VNet wizard returning IPv4 only for a dual-stack VNet.

## B - Bulk import

Target selection: `ExactMatch` onto an existing empty subnet, `AutoCreateTopLevel`, `AutoCreateChild`.
Names: single-prefix subnets keeping their bare Azure name; multi-prefix subnets qualified per range;
a subnet spanning two VNet prefixes qualified on both sides; a multi-address-space VNet naming each
**target** for the prefix it holds. The encompassing subnet marking its target fully allocated with
**exactly one** note in the description and no child created. Dual-stack importing IPv4 only, with an
assertion that **no IPv6 address was persisted anywhere**. Long names inside the 100-char column and
still mutually distinguishable after truncation. Overlapping VNets: the second blocked with the reason
naming the conflict. Empty VNet, ten-subnet VNet, boundary CIDRs. `renameMatchedBastetSubnets` both
ways. **Several VNets selected in one commit.** Partial selection within a prefix. Finally the
free-space assertion in both directions: an imported range is **not** offered as free, and a range
Azure does not hold still **is**.

## C - Reconcile - every status in one scan

Mutate Azure to produce all of them at once, then scan and assert each:

| status | how to produce it |
|---|---|
| `SubnetDeleted` | delete a subnet whose range nothing else holds - must be **deletable** |
| `RangeStillAllocatedInAzure` | delete and recreate a subnet under a new name, same prefix (Azure has no rename) - must be **withheld**, name the new owner, and warn |
| `SubnetPrefixChanged` | move a subnet's prefix |
| `VNetDeleted` | delete a VNet outright |
| `VNetPrefixRemoved` | drop one address prefix from a two-space VNet |
| `FullyAllocatingSubnetDeleted` | delete the subnet that marked a target fully allocated - review only |
| `AzureRangeNotImported` | add a prefix to an already-imported subnet - inbound, **never deletable** |
| `UnrecognisedResourceId` | a row whose `AzureResourceId` is not a parseable ARM id |

Plus: the inbound report fires **exactly once** for an n-prefix subnet - the inventory emits one row
per prefix each carrying the whole list, so a naive walk reports it n times; a resource the credential
cannot see is withheld **and named in a warning**; and the containment case - a coarser Bastet subnet
legitimately covering an Azure range must **not** be reported.

## D - Repair, refuse, commit

**Re-link:** the renamed subnet is offered, the re-link succeeds and points at the new Azure subnet,
the row then reports nowhere, and a second attempt is refused.

**Top-up:** a populated target linked to *this* VNet is selectable with the top-up wording;
already-imported ranges are marked `AlreadyImported` and not offered again; only the genuinely-new
range is offered; the commit adds exactly that one and leaves the existing children untouched; and the
inbound report for that range disappears afterwards.

**Refusals** - each is a way the top-up allowance could have gone wrong:

- a target linked to a **different** Azure VNet (and a hand-built POST refused server-side too)
- a target marked **fully allocated**
- a populated target with **no Azure link** (adoption)
- a target carrying **host IP assignments** - see the trap below

> **The host-IP refusal must be tested against an EMPTY target, and the fixture must be proven.**
> BASTET refuses host IPs on a subnet that has child subnets - *"This subnet has child subnets, so it
> cannot have host IP assignments"* - and `GET /HostIp/Create?subnetId=<populated>` redirects rather
> than rendering a form, so the antiforgery lookup returns empty and the POST silently does nothing.
> A check that adds a host IP to a *populated* target is therefore testing an unreachable state: the
> fixture never exists, the annotation correctly says the target is importable, and the check reports
> a defect that is not there. **This has misled three separate runs.** Build the fixture as an empty
> imported target, then assert `SELECT COUNT(*) FROM HostIpAssignments WHERE SubnetId=<target>` is 1
> **before** looking at the annotation.

**Delete consent:** no verdict, wrong verdict, and missing typed confirmation each refused; the correct
verdict archiving; and in that same commit the invisible-resource row and the unparseable-id row
**still not archived**. Then a cascade that actually carries host IPs (`hostIpsArchived > 0`), the
cascade guard withholding an ancestor whose descendant is protected, and the archived range only then
being reported free.

## E - Single-VNet wizard

Its own import path end to end: discovery, batch create, server-side name resolution producing
prefix-qualified names for a multi-prefix subnet, no generated name containing `/`, and an idempotent
re-import. Also the dual-mode `BatchCreateChildSubnets` - HTML redirect when `isAzureImport`, JSON
otherwise - and its **conditional** feature-flag gate, which applies only when the request writes
Azure state.

## F - ARM failure modes

There is **no transport seam**: `AzureArmClientProvider` builds `new ArmClient(credential)` with no
`ArmClientOptions`. So use two mechanisms, both in **scratch copies** of the repo under the rig
directory, never the real tree:

- **(a)** a fault-injecting `ArmClientOptions.Transport`, to exercise the real `AzureService`
  ARM-walking code;
- **(b)** a substituted `IAzureService` in DI, to exercise the decision layer (`AzureReconciler`,
  `AzureBulkImportPlanner`, the controllers) cheaply.

Cases: 429 throttling; a token expiring mid-enumeration; a paged response whose first page succeeds and
whose second fails; a transient 500; a subscription the credential cannot see at all; and an
empty-but-successful subscription.

**The assertion is the same every time: nothing is offered for deletion on an unanswered question, and
the operator is told which fact was missing.** An empty subscription that really is empty must still
produce the "Azure reported no VNets at all" warning rather than a silent mass deletion.

> **The confirmation-fault modes are vacuous unless absence rows exist.** "Nothing was offered for
> deletion" proves nothing when nothing was deletable to begin with. Before running the throttled- or
> denied-confirmation modes, run an unfaulted scan and assert there is at least one `VNetDeleted` or
> `SubnetDeleted` item to withhold - create one by deleting an imported VNet in Azure if not. On the
> first attempt both modes passed against zero absence rows and had to be re-run.

## G - Browser-driven wizards

Drive the real pages in headless chromium. Reading the JavaScript proves nothing here - **assert what
the browser actually sent against what was persisted.**

- **Bulk import** (`_BulkScripts.cshtml`, 4-step pill wizard): step navigation; `invalidatePlan()`
  re-locking steps 3-4 on any selection change; Select All not submitting rows the server marked
  un-importable (jQuery `:checked` matches disabled inputs - `:not(:disabled)` is load-bearing); going
  back and changing an earlier step; the `previewSeq` out-of-order guard; double-commit.
- **Reconcile** (`_ReconcileScripts.cshtml`, 3-step): scan; checkbox select-all and the indeterminate
  state; the per-row **Re-link** button and its in-flight guard; the typed `approved` confirmation; the
  `deleting` flag preventing a second POST; and that the commit posts `confirmedIds` /
  `confirmedVerdicts` from the confirmation snapshot rather than live checkbox state.
- **Single import** (`_ImportScripts.cshtml`, 3-step, classic form POST): the disable/re-enable of
  hidden inputs so the payload matches the checkboxes; `subnets.Index` explicit indexing (the binder
  stops at the first missing index otherwise); `importSubmitting` double-submit guard; the `pageshow`
  bfcache reset.
- **Subnet details** (`_SubnetCalculationScripts.cshtml`): the CIDR modal's overlap detection and
  network-address adjustment against rendered siblings.

Practical notes, all learned the hard way:

- **Take element ids from the views, do not guess them.** They are `#bulk-subscription-select`,
  `#bulk-select-subscription-btn`, `#bulk-select-all-btn`, `#bulk-go-preview-btn`,
  `#bulk-go-commit-btn`, `#bulk-confirm-commit-btn`; and `#rec-subscription-select`, `#rec-scan-btn`,
  `#rec-select-all`, `#rec-go-confirm-btn`, `#rec-confirmation`, `#rec-confirm-delete-btn`,
  `.rec-item-checkbox`, `.rec-relink-btn`.
- **`<option>` elements are never "visible" to Playwright.** Wait with `state="attached"`, or the
  wizard appears never to load when it has loaded fine.
- **Capture what was SENT.** Attach a request listener and read the POST bodies; asserting on the DOM
  cannot distinguish "ticked" from "submitted", which is the entire point of this phase.
- **The reconcile half needs drift to exist.** Delete an imported VNet and rename a subnet in Azure
  first, or there are no stale rows to select and no Re-link button to press.

## H - Core IPAM behaviour

> **Use address space DISJOINT from the Azure fixture matrix.** The matrix imports `172.16.0.0/12`,
> `10.10.x`, `10.100-10.170.x` and `10.120/10.130`, so a "top-level" subnet created in any of those
> ranges is correctly refused with *"This subnet must be a child of ..."*. One such collision failed
> the first create, left the child id empty, and turned every later URL into `?subnetId=` - fourteen
> failures from one bad address. `100.64.0.0/10` is unused by the matrix and works.

> **Submit forms by HARVESTING the rendered fields, never by hand-listing them.** Edit carries a
> `RowVersion` concurrency token and an `OriginalCidr` pair; a POST missing them redisplays the form
> as HTTP 200 with the row unchanged, which is indistinguishable from a rejected edit. Harvest every
> `input`/`textarea` from the GET, override only what the check changes, and post that.

> **Give each run's rows unique names.** A leftover row from an earlier attempt with the same name
> makes a `COUNT(*) WHERE Name=...` assertion lie - it did, once, reporting a refused create as
> accepted.

Every non-Azure action driven as a request, not asserted in a unit test:

- subnet create / edit / delete / deleted-list / purge; host IP create / edit / delete / deleted-list /
  purge; `SetAllocationStatus` clearing the fully-allocated note when the flag is cleared
- the typed `approved` confirmation on **all four** delete and purge paths
- the `confirmedMaxId` scope bound on both purges - a row created after the confirmation screen was
  built must not be purged by it
- `[ActionName]` aliasing, where GET and POST share a URL
- pagination on `AllHostIps` and `AllDeletedHostIps` (page size 50)
- validation as requests: overlap, containment, parent fit, CIDR boundaries, a host IP on the network
  or broadcast address, a host IP on a subnet that has children (refused)
- every page asserted on **rendered content and title**, never a bare HTTP 200

## I - Authorization, antiforgery, headers, locking

Sweep **every controller action** against its declared policy - `RequireViewRole`, `RequireEditRole`,
`RequireDeleteRole`, `RequireAdminRole`, the authenticated fallback, and the `[AllowAnonymous]`
exceptions (`AccessDenied`, `Logout`, `SignedOut`, `SignInFailed`, the error routes).

**The Development `DevAuthHandler` authenticates unconditionally with every role**, so a normal dev run
makes every policy pass trivially and proves nothing. Role separation must be driven from a **scratch
copy** whose handler issues a restricted role set, one run per role.

Also: antiforgery rejection on every state-changing endpoint, and the `RequestVerificationToken` header
path the wizards use; security headers on 200, 404 **and** 500 (the middleware sits below the exception
handler so headers survive `Response.Clear()`); `X-Frame-Options: DENY` present when frame-ancestors is
`'none'`; the global `ResponseCache: NoStore`; `BASTET_AZURE_IMPORT` gating every Azure endpoint; and
concurrent writes contending on the **real** `sp_getapplock` against SQL Server, which the SQLite suite
cannot reach - including that a second replica's write is refused honestly rather than silently lost.

---

# Rules that decide whether the report is true

## Triage every failure as fixture-invalid before calling it a defect

A failing check has two possible causes and they are not equally likely. **Prove the fixture exists in
the state the check assumes before believing the application is wrong.**

This is not hypothetical. A check twice reported that a target carrying host IP assignments was wrongly
offered for import. Both times the host IP had never been created - the antiforgery token lookup
returned empty because the application *correctly* refuses host IPs on a subnet that has children, so
the state under test was unreachable. Reported as a defect, it would have been a fabrication.

So: on any failure, query the database or the API for the precondition, confirm it holds, and only then
report. When the answer is that the test was wrong, **say so plainly** rather than quietly adjusting
the check until it goes green.

## Restart the application after any rebuild before measuring

`dotnet run` compiles at start. A result measured from a process that predates an edit describes the
old code. Any phase that follows a code change restarts the instance first and confirms it is serving
the new build.

## Standing rig rules

- Assert on **rendered content and page titles**, never a bare HTTP 200.
- Kill only by **captured PID**. Never `pkill -f Bastet` or `pkill dotnet` - it kills sibling instances,
  and a careless pattern has more than once killed the operator's own shell.
- Give every app instance its **own port and own catalog** so runs cannot collide.
- Write **nothing** into the repository working tree - no scratch files, no logs, no PID files. One
  untracked file makes the tree dirty and invalidates the closing assertion.
- Scratch copies of the repo live under the rig directory and are modified freely; the real tree is
  never modified.

---

# Teardown and reporting

In this order:

1. **Kill app processes** by captured PID; remove the containers.
2. **Azure - the part that outlives this machine.** Delete every resource in the inventory and **read
   the result of every delete**. Then re-enumerate **both** resource groups across **all resource
   types**, not just VNets:

   ```bash
   az resource list -g <rg> --query "length(@)"
   ```

   and assert zero. An empty deletion list with a success verdict is itself a bug. If the inventory is
   missing, enumerate the groups directly and delete anything matching the run's prefix.
3. **Confirm the repository is untouched**: `git status --porcelain` empty, branch and HEAD exactly as
   recorded at preflight.
4. **Report.**

The report carries: a pass/fail table per phase; the full failure list with each failure classified as
*application defect* or *invalid fixture*; **an explicit statement of what was not covered**; anything
teardown failed to clean; and - always - a reminder to **revoke both service principal secrets**. This
skill asked for them; this skill reminds you to kill them.

A phase that finds nothing still reports its counts. "All green" without numbers is not a result.
