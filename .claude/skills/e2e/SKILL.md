---
name: e2e
description: Run a full end-to-end verification pass over Bastet against a live rig - real SQL Server, the real application, a real browser and a live Azure subscription. Covers Azure discovery, the bulk import wizard, the reconciler, ARM failure modes, the client-side wizard state machines, core IPAM behaviour, authorization and locking. Use when asked to "run the e2e tests", "test everything end to end", "verify the whole app works", or after a round of fixes when a green unit suite is not enough. Pass "auto" to run straight through. To find new defects use /audit; to fix an existing findings file use /audit-reconcile.
---

# Run a full end-to-end pass

One operator-run pass over the whole application against a live rig. It asks for its inputs once,
proves its own prerequisites, builds the rig, runs a fixed scenario matrix, reports a pass/fail table,
and tears everything down with the teardown **verified by enumeration rather than asserted**.

Nothing this skill produces enters the repository. Scripts, rigs, scratch copies and logs all live in
the scratchpad and die with the machine. The only durable artefact is the report in the conversation.

## What Bastet is — how every pass/fail is decided

**An IPAM tool. Its job is to be the authority on which IP space is allocated and which is free.**
Never report allocated space as free (the worst output it can produce); never destroy an allocation
record on incomplete information; and the operator must be able to act on what they are told.

**Rule 0, which overrides those: Bastet is the authority, and it answers from its own records.** Free
means free *according to Bastet*. **Azure is not authoritative in Bastet at all** — it is a source you
import *from*, which is why the import wizard exists. Until a range is imported it does not exist as
far as Bastet is concerned, and a phase must never fail because Bastet showed un-imported Azure space
as free. That is the product working.

**Azure state and Bastet state are compared in exactly two places, and nowhere else:** the bulk import
wizard, which asks *can this be added?*, and reconcile, which asks *can this be deleted?* Every other
screen — the subnet tree, Details, unallocated ranges, host IPs, search — answers from Bastet alone.
**Never write an assertion that has any other surface consult Azure**, and never fail one because it
did not.

**One flat, routable space.** Bastet manages a single IP space in which everything is routable against
everything else, so the same range must never be allocated twice — preventing that collision is the
product's reason to exist. Two consequences when classifying a result:

- **"Still allocated" is a question about the whole managed space, never about provenance.** If any
  live resource holds a range, that range is in use, whichever VNet, subscription or import it came
  from. Overlapping Azure VNets are not a case to defend — inside Bastet's model that overlap *is* the
  collision, so never pass a result on the grounds that the duplicate lived in a different VNet.
- **How the space is carved up is the operator's choice, and only theirs** — by hand, by Azure import,
  or both. Neither origin is privileged, so a scenario that must hold for a manually created subnet
  must hold identically for an imported one. Cover both paths, not just the import wizard.
- **One Azure range is one Bastet row.** A VNet with a single address prefix whose single subnet covers
  that whole prefix imports as **one** row, marked fully allocated — not a VNet parent plus a
  byte-identical child. Two rows with the same CIDR is a failure, not a pass.

**Reconcile does exactly two things**, and a run that expects more of it is asserting behaviour the
product deliberately does not have:

1. reports Azure resources that are **gone**, so the operator can choose to delete the Bastet row;
2. reports Azure resources whose **range changed**, so the operator can choose to delete the Bastet row.

It never edits a row, never re-links, never re-adds, and never reports un-imported Azure space. After
deleting a row whose range changed, the operator re-imports through the **bulk import wizard** — so a
changed-range message that does not point there is a defect.

**It joins on the Azure resource id and nothing else.** Gone from Azure means deletable here. The only
refusal is **manual content in the hierarchy: a hand-added child subnet, or a host IP** — the one thing
a resource id cannot tell you about, since Azure has no record of it. So a result where a row is
withheld because its **range** turned up somewhere else — another VNet, another subscription, a prefix
still "covered" after a re-carve — is a **FAIL**, not a cautious pass. Phase C's counter-assertions
exist to catch exactly that.

## Mode

**Default:** run each phase and report as you go.
**`/e2e auto`:** run straight through to the final report, pausing only for a missing prerequisite, a
missing credential, or a result that cannot be classified without a decision.

**State which mode is active before the first phase.**

## All eight phases run. There is no partial pass.

**A run that has not executed A through H is not an e2e pass and must not be reported as one.**

The only legitimate reasons a phase does not run are a **missing prerequisite**, a **missing
credential**, or a **rig that cannot be built**. Each of those stops the whole run and is reported as
a **stop**, naming the blocker — not as a result with a gap in it. Running low on time, budget or
context is **not** one of them; neither is a phase being expensive.

If you genuinely cannot finish, say so **before** the phase table and call it an incomplete run in
the first line. Never present six phases as a pass and disclose the missing two underneath, and
never let *"an explicit statement of what was not covered"* become permission to skip: that clause
exists for a blocked prerequisite, not for work you chose not to do.

The expensive phases are **E, F and H** — they need scratch builds and a browser. They are the ones
most coupled to the code that deletes on external evidence, so they are the last things to drop and
the first things to prepare. See *The rig*: their builds are made up front, not when the phase runs.

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
| Browser | chromium under `~/.cache/ms-playwright` | **Install it here, once** (see below) - it installs unattended. **Stop** only if it demands `install-deps` and root; phase F is worthless without it and must not fail quietly |
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

**`python3 -m venv` prints a loud failure here and still leaves a usable tree.** It ends with
*"The virtual environment was not created successfully because ensurepip is not available … apt install
python3.13-venv"* and a non-zero exit - but `bin/python` exists, and `get-pip.py` then completes the
job. Do **not** treat that message as a stop, and do **not** put `set -e` ahead of it or the whole
toolchain step aborts on a venv that is actually fine. Install `beautifulsoup4` and `lxml` into the
same venv while you are there: the form-harvesting rule needs a real HTML parser.

## The browser

**Do not assume chromium is on disk.** Earlier versions of this file said it "is normally already
cached" and told you not to run `playwright install`. On a fresh box that is simply false —
`~/.cache/ms-playwright` did not exist at all, and following that instruction leaves phase F with no
browser and no explanation. Check, and install if absent:

```bash
ls -d ~/.cache/ms-playwright/chromium-*/ 2>/dev/null || echo "absent - install it"
```

There is no Node and no npm, but the **python** binding ships its own CLI, so install the binding
first and let it fetch the browser. Both steps are unattended and need no `sudo`:

```bash
<rig>/azcli/bin/pip install playwright
<rig>/azcli/bin/playwright install chromium     # NOT install-deps, which needs root
```

If `playwright install chromium` succeeds, phase F is fully live. If it demands `install-deps` and
root, **stop and say so plainly, naming the command a human would need to run** — do not half-configure
the machine and do not let phase F quietly degrade into reading the JavaScript, which proves nothing.

Two ways to drive it, and phase F needs the second:

```bash
# DOM snapshot only - enough for rendering assertions
CH=$(echo ~/.cache/ms-playwright/chromium-*/chrome-linux64/chrome)
"$CH" --headless --no-sandbox --disable-gpu --disable-dev-shm-usage \
      --virtual-time-budget=8000 --dump-dom http://127.0.0.1:<port>/ > out.html

# real interaction - the python binding, from the same rig venv
<rig>/azcli/bin/python -c "from playwright.sync_api import sync_playwright; ..."
```

Verify **both** modes work before phase F starts, rather than discovering the binding is broken
halfway through a wizard run.

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

**`BASTET_AUTO_MIGRATE=true` or nothing works, and the symptom points at Azure.** The application
does not create its own database. Without auto-migrate every Azure endpoint returns *"The reconcile
scan failed. Details have been logged."* - a generic message that reads exactly like a credential or
ARM problem, while the log underneath says `Cannot open database ... The login failed`. Set it when
starting the app, and when an Azure call fails, **read the log before touching the credentials.**

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

## Build all THREE trees here, before any phase runs

Phases E and H each need a modified build. Building them when the phase arrives puts the two most
expensive setups at the end of the run, which is exactly when they get dropped. Build them now, while
there is budget, and assert each compiles **0 warnings** before starting phase A:

| tree | what it is | used by |
|---|---|---|
| **real** | the working tree, unmodified | A-D, F, G, and H's antiforgery/header/locking half |
| **fault** | `git archive HEAD` copy + a fault-injecting decorator over `IAzureService`, registered only when `BASTET_E2E_FAULT` is set, plus an `ArmClientOptions.Transport` seam if mechanism (a) is wanted | E |
| **roles** | `git archive HEAD` copy whose `DevAuthHandler` issues the role set named by `BASTET_E2E_ROLES` (`View`, `Edit`, `Delete`, `Admin`, or `none`) rather than Admin unconditionally | H's role-separation half |

Both modified trees live **under the rig directory**, never in the real tree, and both gate their
behaviour on an environment variable so the same binary still runs unmodified when it is unset.

The fault decorator is the cheap mechanism (b): it substitutes `IAzureService` to drive the decision
layer. Point it at `GetVNetInventory` (return `Success=false` with a distinct `ErrorMessage` per mode)
and at `ConfirmResourcesAsync` (return `Unknown` / `NotVisible` for every id). That covers every case
in phase E's table except the ones that need the real ARM-walking code, which is mechanism (a).

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
  IP added to it. See the host-IP refusal note in phase D for why the target must be empty.
- **manual-child** and **manual-hostip** - the two fixtures that produce `HeldByManualContent`, and
  they are **BASTET-side, not Azure-side**. Import a VNet in phase B, then in Bastet add a child subnet
  by hand under an imported row (one fixture) and a host IP under another (the other fixture), then
  delete both VNets in Azure. Only after both halves exist does the status appear. Building them as
  Azure fixtures produces nothing: the whole point is content Azure has no record of.

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
in any **address field** (scan the prefix lists, not the raw JSON - its own key/value separators are
`:` and a naive substring test fails against correct output); the 3-prefix subnet emitting three rows
**each carrying the complete prefix list**; the
5-prefix subnet emitting five; the two-address-space VNet offering both and its spanning subnet
appearing under each; the two overlapping VNets both discovered with distinct resource ids for
identically-prefixed subnets; `/29` and end-of-range subnets discovered; the empty VNet offered with
its prefix and no subnets; the ten-subnet VNet returning all ten; the delegated subnet treated
ordinarily; **every prefix and every subnet selectable on a clean tree**; and the bulk discovery
endpoint returning IPv4 only for a dual-stack VNet.

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

| status | how to produce it | expected |
|---|---|---|
| `SubnetDeleted` | delete an imported Azure subnet | **deletable** |
| `SubnetPrefixChanged` | move a subnet's prefix | **deletable**, reason names the new prefix and the import wizard |
| `VNetDeleted` | delete a VNet outright | **deletable** |
| `VNetPrefixRemoved` | drop one address prefix from a two-space VNet - **vacate it first**, see below | **deletable**, reason points at the import wizard |
| `HeldByManualContent` | add a child subnet **by hand** under an imported row, then delete it in Azure | **review only**, never deletable, warned |
| `HeldByManualContent` | add a **host IP** to an imported row, then delete it in Azure | **review only**, never deletable, warned |
| `UnrecognisedResourceId` | a row whose `AzureResourceId` is not a parseable ARM id | **review only** |

> **Read the result of every mutation, and verify the state it was supposed to produce.** ARM refuses
> to remove a VNet address prefix while a subnet still occupies it - `NetcfgSubnetRangeOutsideVnet` -
> so the two-space fixture must have its spanning subnet narrowed **before** the prefix is dropped. A
> builder that fires `az` and ignores the exit code leaves the status unproduced, and the phase then
> reports a missing verdict as though the application had failed to emit it. Assert the mutation
> landed (`az network vnet show`) before scanning.

**Four counter-assertions, each guarding against a regression that has actually shipped:**

- **Delete-and-recreate under a new name, same prefix** (Azure has no rename). The old row **must
  still be deletable** — the range turning up under another Azure subnet is not a reason to withhold.
  Four consecutive audit rounds widened a withhold path here that should never have existed.
- **A range held by a second, overlapping VNet.** Still deletable. That overlap is the collision
  Bastet exists to prevent, not a configuration to defend.
- **An Azure range no Bastet row records** — add a subnet in Azure and do not import it. Reconcile
  must report **nothing**: no item, no review row, no warning. Finding it is the import wizard's job.
- **An Azure-imported descendant with no manual content.** The parent is still deletable and takes the
  descendant with it. Only *manual* content holds.

Plus: a resource the credential cannot see is withheld **and named in a warning**, while a genuinely
deleted one is **still offered and deletable**. Checking only the first lets an over-blocking
regression pass.

## D - Refuse, top-up, commit

**Reconcile has no repair path.** Assert the absence positively: the reconcile page contains **no
re-link control**, `POST /Subnet/RelinkAzureSubnet` returns **404**, and a changed-range row's reason
names the **import wizard**. Then drive the actual remedy end to end: delete the changed row, re-import
the current range through the bulk import wizard, and confirm the row comes back with the new range and
reconcile then reports nothing.

**Top-up:** a populated target linked to *this* VNet is selectable with the top-up wording;
already-imported ranges are marked `AlreadyImported` and not offered again; only the genuinely-new
range is offered; the commit adds exactly that one and leaves the existing children untouched.

**The collapse case, followed through:** phase B asserted the `encompass` VNet imports as **one** row
marked fully allocated. Here, add the query that would catch the regression directly - no two undeleted
subnets share a `NetworkAddress`/`Cidr` pair - and confirm reconcile reports that row nowhere while
Azure is unchanged.

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
verdict archiving; and in that same commit the invisible-resource row, the unparseable-id row and the
manual-content row **still not archived**. Then the cascade guard withholding an ancestor whose
descendant is protected, and the archived range only then being reported free.

**No host IP is ever archived by reconcile.** A host IP anywhere in the hierarchy holds the whole
subtree, so `hostIpsArchived` must be **0** on every reconcile commit in the run.

## E - ARM failure modes

Runs against the **fault** tree, which was already built during the rig phase - see *Build all THREE
trees here*. Do not build it now; if it is missing, the rig step was skipped and the run is invalid.

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

> **A transport fault can land at either stage, so do NOT require `scanSucceeded == false`.** The
> fixture matrix is small enough to list in a single ARM page, so a transport gate keyed on "the second
> call to a `virtualNetworks` URI" fires on the per-resource **confirmation** calls, not on the listing.
> The scan then succeeds, `globalErrors` is empty, and every absence row is withheld with the reason in
> **`warnings`** - which is correct fail-closed behaviour. Assert the invariant (`items == 0`, and the
> operator told) against `globalErrors` **or** `warnings`, and report which stage faulted. Demanding a
> failed scan reports two defects that are not there.

> **The confirmation-fault modes are vacuous unless absence rows exist.** "Nothing was offered for
> deletion" proves nothing when nothing was deletable to begin with. Before running the throttled- or
> denied-confirmation modes, run an unfaulted scan and assert there is at least one `VNetDeleted` or
> `SubnetDeleted` item to withhold - create one by deleting an imported VNet in Azure if not. On the
> first attempt both modes passed against zero absence rows and had to be re-run.

## F - Browser-driven wizards

Drive the real pages in headless chromium. Reading the JavaScript proves nothing here - **assert what
the browser actually sent against what was persisted.**

- **Bulk import** (`_BulkScripts.cshtml`, 4-step pill wizard): step navigation; `invalidatePlan()`
  re-locking steps 3-4 on any selection change; Select All not submitting rows the server marked
  un-importable (jQuery `:checked` matches disabled inputs - `:not(:disabled)` is load-bearing); going
  back and changing an earlier step; the `previewSeq` out-of-order guard; double-commit.
- **Every badge, with the filter OFF.** All rows show, so every label must be true. Build one scan
  carrying all five and assert the badge, the reason **and** the checkbox's `disabled` state together -
  a truthful label on an unusable control is still a defect:

  | row | badge | selectable |
  |---|---|---|
  | a prefix Bastet does not record | *(no badge)* — "Will create a new Bastet subnet" | yes |
  | linked, with a subnet still addable | Will update existing | yes |
  | linked, everything recorded | Already imported | no |
  | matched a hand-made subnet holding host IPs | Cannot import, reason naming which | no |
  | matched a hand-made subnet **with children**, unlinked | Will update existing | **yes** — adoption |
  | linked, childless, name differs, **rename on** | Rename only | **yes** |
  | linked and **fully allocated**, name differs, **rename on** | Rename only | **yes** |
  | already-imported **child subnet**, name drifted, **rename on** | Rename only | **yes** |
  | unlinked row on the same range, **rename on** | Cannot import | no |

- **Adopting a hand-built tree that partly overlaps Azure.** Build a Bastet /16 by hand with two
  hand-made children, one of which overlaps an Azure subnet in the matching VNet, and drive the whole
  thing: the prefix must be selectable, the clean Azure subnet must import, the overlapping one must
  stay `Cannot import` **naming the Bastet row in the way**, and committing must link the parent while
  leaving both hand-made rows present and still unlinked. Then POST the overlapping subnet directly,
  bypassing the disabled checkbox, and assert the preview refuses with a global error and the commit
  writes nothing. A whole-prefix refusal here is a regression, not a safeguard.
- **Every toggle must re-derive the preview button.** Select all, then flip each toggle in turn and
  assert the button's `disabled` state matches what is actually ticked. A re-render that rebuilds the
  checkboxes unticked while the button stays enabled posts an empty selection and answers
  "No VNet address prefixes were selected" — assert the count of ticked boxes and the button together,
  because either alone passes.
- **The already-imported wording must match what is underneath it.** A prefix whose contained Azure
  subnets are all recorded says "already recorded"; one where any contained subnet is refused says
  "either already recorded or cannot be imported". Assert the two separately in one scan, or the
  wording drifts back to claiming completeness over a row rendered directly beneath it saying otherwise.
- **The rename toggle re-renders the tree whether or not the filter is on.** It changes the badge and
  the checkbox, not just visibility. Assert `Rename only` appears with the filter **off** too, or a
  regression that gates the re-render on the filter passes unnoticed.
- **`WouldRenameTarget` must agree with `WillRename` in the plan**, or a row promises a rename the
  commit will not perform. Drive both: annotate and preview the same selection and compare the flags.
  **A target with child subnets renames like any other** — that case was once excluded, so cover it
  explicitly: rename it by hand, tick it with rename on, commit, and assert the target took the VNet
  name **and every child survived untouched**.
- **Child subnets rename too, and the assertion is the database, not the banner.** Rename two imported
  children by hand, tick them with rename on, commit, then **read the rows back** and assert the names
  actually changed. A counter incremented over an untracked entity reports "renamed 2" while writing
  nothing, so a pass that only reads the success message proves nothing. Assert the preview says
  *Rename to* rather than *Create* for those rows, and that `createdChildSubnets` is 0.
- **The rename gate is the Azure link.** Cover both refusals in the same run: a row on the matching
  range with **no** `AzureResourceId`, and one linked to a **different** Azure resource. Both must stay
  `Cannot import`, must never be renamed, and must keep the "already exists in Bastet" global error.
- **A rename-only selection must be submittable on its own.** Tick only child subnets, leaving the
  prefix checkbox unticked, and assert the preview button enables and the payload still carries the
  parent prefix — the selection is built from prefix checkboxes, so a child-only selection is exactly
  the case that silently posts nothing.
- **"Only show what would change"** (`#bulk-hide-imported`): with it **on**, nothing that would do no work may
  remain visible. Build all four cases in one scan and assert each: a VNet prefix whose every Azure
  subnet is already recorded is **hidden** and labelled `AlreadyImported`; a collapsed fully-allocated
  target imported from *this* VNet is **hidden** (`AlreadyImported`, not `Blocked`); a prefix with one
  un-imported subnet **stays visible**; and an exact-match target that is **not yet linked** stays
  visible, because importing it links it and that is work - **including one whose Bastet row already has
  children**, which is adoption and is also work. Then flip `#bulk-rename-matched` **without
  re-scanning**: a row whose only difference is its name must appear when rename is on and vanish when
  it is off. The counter-test matters most - if nothing is ever hidden, or everything is, the filter
  is not being exercised.

  **Assert the rule, not just the cases.** The filter must keep every prefix the planner marked
  `IsSelectable`, plus the rename-only rows the client itself enables - and nothing else. A filter that
  decides visibility by listing status names silently mis-files the next status anyone adds, which is
  exactly how a linkable row came to be hidden behind a control labelled "only show what would change".
  Drive it by comparing the visible set against `isSelectable` from the same scan, not against a
  hand-written list of expected VNet names.
- **Validation parity across write paths.** Take one field and drive the same value through every path
  that writes it — Create, Edit, and the bulk import commit — asserting they agree. Cover both
  directions in one run: markup (`<script>alert(1)</script>`, `<img src=x onerror=alert(1)>`) refused
  everywhere, and ordinary operator text (`core/edge (site B)`, `Prod: DC1`, `Zürich core`,
  `HQ <-> DR`, `temp < 5 and load > 3`) accepted everywhere and stored verbatim. Then read the stored
  value back off a rendered page and assert it is HTML-encoded — that, not the input filter, is what
  makes the app safe, so a run that only checks the filter has tested the wrong thing.
- **Reconcile** (`_ReconcileScripts.cshtml`, 3-step): scan; checkbox select-all and the indeterminate
  state; the review table rendering status and reason with **no action column**; the typed `approved` confirmation; the
  `deleting` flag preventing a second POST; and that the commit posts `confirmedIds` /
  `confirmedVerdicts` from the confirmation snapshot rather than live checkbox state.
- **Subnet details** (`_SubnetCalculationScripts.cshtml`): the CIDR modal's overlap detection and
  network-address adjustment against rendered siblings.

Practical notes, all learned the hard way:

- **Take element ids from the views, do not guess them.** They are `#bulk-subscription-select`,
  `#bulk-select-subscription-btn`, `#bulk-select-all-btn`, `#bulk-go-preview-btn`,
  `#bulk-go-commit-btn`, `#bulk-confirm-commit-btn`; and `#rec-subscription-select`, `#rec-scan-btn`,
  `#rec-select-all`, `#rec-go-confirm-btn`, `#rec-confirmation`, `#rec-confirm-delete-btn`,
  `.rec-item-checkbox`.
- **`<option>` elements are never "visible" to Playwright.** Wait with `state="attached"`, or the
  wizard appears never to load when it has loaded fine.
- **The subnet listing is a TREE of anchors, not a table.** There is no `<tr>` per subnet, so
  `closest('tr')` returns null and every "find the row for this CIDR" lookup silently yields nothing.
  Match on the anchor itself - its text carries name and CIDR together, and its href ends in the id:

  ```js
  [...document.querySelectorAll('a[href*="/Subnet/Details/"]')]
      .find(a => a.innerText.includes('10.211.2.0/24')).href.split('/').pop()
  ```
- **Capture what was SENT.** Attach a request listener and read the POST bodies; asserting on the DOM
  cannot distinguish "ticked" from "submitted", which is the entire point of this phase.
- **The reconcile half needs drift to exist.** Delete an imported VNet and move a subnet's prefix in
  Azure first, or there are no stale rows to select and nothing to confirm.
- **The reconcile half also needs a REVIEW row**, or "the review table renders status and reason with
  no action column" asserts against an empty table and cannot fail. Stale rows are not review rows:
  add one Bastet-side (a subnet whose `AzureResourceId` is unparseable is the cheapest).
- **`vnets`, `lastSelection` and `lastPlan` are closure-scoped `let`s, not on `window`.**
  `page.evaluate("() => vnets…")` dies with `ReferenceError: vnets is not defined`. Read the same data
  off the wire instead - a `page.on("response")` handler that keeps the `BulkImportPreview` JSON, or a
  page-side `fetch('/Azure/BulkGetVNets?subscriptionId=…')` for the annotation.
- **Use the ASYNC Playwright API.** Forcing the `previewSeq` out-of-order race means holding response
  #1 while response #2 completes; in the sync API a `time.sleep` inside a route handler blocks the
  driver loop and the responses still arrive in order, so the guard is never exercised. With
  `async_playwright`, `await asyncio.sleep(4)` in the handler for the first request lets the second
  overtake it. Fulfil the held one with a distinctive marker in `globalErrors` and assert the marker
  never renders.
- **A guard that disables its own button cannot be tested with `page.click`.** Playwright auto-waits
  for "visible and enabled", so the second click of a double-commit test times out after 30s (the
  guard worked - the test crashed). Dispatch both clicks synchronously in one evaluate,
  `b.click(); b.click();`, so the JS `committing` / `deleting` flag is the only thing that can stop
  the second POST.
- **Check select-all with a real click, not `el.checked = true`.** After one row is ticked the box is
  `indeterminate`, and setting `.checked` then firing `change` makes the handler read it as unchecked
  and clear every row. `locator("#rec-select-all").check()` behaves like a user and works.
- **To test the confirmation snapshot, untick WITHOUT firing `change`.** The delegated handler calls
  `invalidateConfirmation()`, which discards `confirmedIds` - so a change event destroys the very state
  under test. Set `.checked = false` directly; that is exactly "live state diverged from what was
  confirmed". The step-2 checkboxes are also in a hidden tab pane by then, so `uncheck()` fails on
  visibility anyway.
- **The commit handlers navigate on success.** A `page.goto` issued straight after a commit races that
  redirect and dies with `net::ERR_ABORTED`. Wait a few seconds, then `goto` with
  `wait_until="domcontentloaded"` and one retry.

## G - Core IPAM behaviour

> **Use address space DISJOINT from the Azure fixture matrix.** The matrix imports `172.16.0.0/12`,
> `10.10.x`, `10.100-10.170.x` and `10.120/10.130`, so a "top-level" subnet created in any of those
> ranges is correctly refused with *"This subnet must be a child of ..."*. One such collision failed
> the first create, left the child id empty, and turned every later URL into `?subnetId=` - fourteen
> failures from one bad address. `100.64.0.0/10` is unused by the matrix and works.

> **The network address must actually BE the network address for its CIDR.** `100.70.0.0/12` is not -
> the network of a `/12` containing it is `100.64.0.0` - so the create is correctly refused, the id
> comes back `None`, and the phase dies on the first `int(...)` rather than reporting a check. Use
> `100.64.0.0/10` for phase G and `100.70.0.0/16` for phase H, which are both genuine boundaries and
> do not collide with each other or with the Azure matrix.

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

## H - Authorization, antiforgery, headers, locking

Sweep **every controller action** against its declared policy - `RequireViewRole`, `RequireEditRole`,
`RequireDeleteRole`, `RequireAdminRole`, the authenticated fallback, and the `[AllowAnonymous]`
exceptions (`AccessDenied`, `Logout`, `SignedOut`, `SignInFailed`, the error routes).

**The Development `DevAuthHandler` authenticates unconditionally with every role**, so a normal dev run
makes every policy pass trivially and proves nothing. Role separation is driven from the **roles** tree
built during the rig phase, one restart per role.

> **Do not follow redirects when checking the `[AllowAnonymous]` exceptions.** `SignedOut` and
> `SignInFailed` redirect an *authenticated* caller into the app, which then correctly refuses a
> role-less principal - so following the redirect reports a 403 that belongs to `/Home`, not to the
> anonymous endpoint. Assert on the endpoint's own status: 200 or 302, never 403.

Also: antiforgery rejection on every state-changing endpoint, and the `RequestVerificationToken` header
path the wizards use; security headers on 200, 404 **and** 500 (the middleware sits below the exception
handler so headers survive `Response.Clear()`); `X-Frame-Options: DENY` present when frame-ancestors is
`'none'`; the global `ResponseCache: NoStore`; `BASTET_AZURE_IMPORT` gating every Azure endpoint; and
concurrent writes contending on the **real** `sp_getapplock` against SQL Server, which the SQLite suite
cannot reach - including that a second replica's write is refused honestly rather than silently lost.

> **Make the contention deterministic: hold the lock from a THIRD session.** Racing two app writes and
> hoping they collide is the check that once passed with every write completing in 0.05s. Instead hold
> it yourself from `sqlcmd` -
> `EXEC sp_getapplock @Resource='Bastet:SubnetOperations', @LockMode='Exclusive', @LockOwner='Session',
> @LockTimeout=60000; WAITFOR DELAY '00:00:10';` - and drive a write from each of **two app replicas on
> the same catalog**. Both must wait out the hold and both must persist. Time an uncontended write
> first as the positive control (~0.2s against ~8s proves the wait was contention, not slowness). Then
> hold it past the app's **30 s** `DEFAULT_TIMEOUT_MS` and assert the write is refused **and persisted
> nothing** - a 302 there would mean it claimed success.
>
> **Killing the holder does not release the lock immediately.** The server-side session lingers well
> after `docker exec` dies - 18 s in one run - so a "writes succeed again" control timed straight after
> the kill measures the tail of the old lock and fails against correct behaviour. Poll
> `SELECT APPLOCK_TEST('public','Bastet:SubnetOperations','Exclusive','Session')` until it returns `1`,
> then start timing.

---

# Rules that decide whether the report is true

## A sudden RZ1021 storm means the build server, not your markup

If `dotnet build` starts reporting `RZ1021: Markup in a code block must start with a tag ... Do not
use unclosed tags like "<br>"` across `.cshtml` files **you did not touch**, and the cited lines hold
ordinary valid Razor (`<partial ... />` inside `@if {}`, `<text>` inside `@foreach`), the Razor source
generator in the long-running Roslyn build server has gone bad. The markup is fine.

```bash
dotnet build-server shutdown
```

Then rebuild. **Do not "fix" the views to satisfy it** - you would be rewriting valid Razor to work
around a stale compiler process, and the change would be pure noise in the diff. Confirm the diagnosis
in seconds by building a throwaway `dotnet new mvc` with the same construct: if the fresh template
fails too, it is the toolchain, not the repository.

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

## Every absence assertion needs a positive control in the same run

**A check that something is absent, suppressed, withheld or not reported proves nothing on its own.**
It passes identically when the behaviour is correct and when the fixture never existed. So every such
assertion is paired, in the same run and against the same scan, with a comparable case that *is*
present, reported or offered.

- "the un-imported Azure range is not reported by reconcile" → **and** the same range **is** offered by
  the bulk import wizard, so the absence is reconcile's scope and not a broken fixture
- "the invisible resource is not offered for deletion" → **and** a genuinely deleted one still **is**
- "nothing is offered while ARM is faulted" → **and** the unfaulted scan had absence rows to withhold
- "the disabled row was not submitted" → **and** the enabled rows were

Two real failures this rule exists for. A containment check passed while its fixture was an *exact*
address match, so it exercised the equality arm and never touched containment at all. A locking check
passed with every write completing in 0.05s, so nothing ever contended and the absence of a stranded
lock meant nothing. Both looked green. Both measured nothing.

State the control's result in the check's detail, so the report shows the pair.

## Never guess a route, a field name, or a literal

Read it from the controller, or harvest it from the rendered form. Guessing produces a failure that
looks exactly like a defect, and three separate ones did:

- **`[ActionName]` aliasing** means several POSTs go to the **same URL as their GET**:
  `/Subnet/Delete/{id}` (field `Id`) and `/HostIp/Delete` (field `ip`), not a `...Confirmed` route.
- **The typed confirmation word is `approved`** on every delete and purge path - subnet, host IP, both
  purges and the reconcile commit. It is not `delete` and not `confirm`.
- **`SetAllocationStatus` lives on `HostIpController`**, not `SubnetController`, and binds
  `SubnetAllocationDto { SubnetId, IsFullyAllocated }`.
- **The subnet delete form carries its own scope bounds** - `confirmedMaxSubnetId` and
  `confirmedMaxHostIpTicks`. A hand-built delete POST that sends only `Id` and `confirmation` returns
  **302 and archives nothing**, which reads exactly like a broken delete path. Harvest the form.

Harvest forms with a real HTML parser over `input`/`textarea`/`select`, not a regex: a regex that
assumes `name` precedes `value` silently drops `RowVersion`, and the POST then redisplays the form as
**HTTP 200 with the row unchanged** - indistinguishable from a rejected edit.

**Harvest the antiforgery token from a page that actually renders one.** `/Subnet` and `/Subnet/Details`
do not; `/Subnet/Create`, `/Azure/BulkImport` and `/Azure/Reconcile` do. A token lookup that returns
empty makes every state-changing POST come back **400 with an HTML body**, which is indistinguishable
from the antiforgery or feature-gate refusal you were trying to measure.

## Database schema facts the checks depend on

Assert against the real schema, not the one you would have designed. Each of these produced a
`sqlcmd` `Msg 207 (invalid column name)` mid-phase, which surfaces as a `ValueError` in the driver
rather than as a failed check:

- **There is no `IsDeleted` column.** Deletion is a move, not a flag: rows go to **`DeletedSubnets`**
  and **`DeletedHostIpAssignments`**. `SELECT ... FROM Subnets` already sees only live rows, and
  "was it archived rather than silently dropped" is a count against the archive table.
- **The archive's IP column is `OriginalIP`**, not `IP`. `DeletedSubnets` keeps `NetworkAddress`/`Cidr`
  but renames the identity columns to `OriginalId` / `OriginalParentId`.
- **`Subnets`** is `Id, Name, NetworkAddress, Cidr, Description, Tags, ParentSubnetId, RowVersion,
  CreatedAt, LastModifiedAt, CreatedBy, ModifiedBy, IsFullyAllocated, AzureResourceId`.
- **The fully-allocated note is a whole LINE, not a fragment.** `FullyAllocatedNote.Strip` splits on
  `\n` and drops lines that both start with `Fully allocated by Azure subnet '` and end with
  `' which encompasses the entire address space.` A fixture that puts the note on the same line as
  other text is **correctly** left alone - and reporting that as a failure to clear the note is a
  fabrication. Build the fixture with a real newline.
- **Counting rendered rows: count DISTINCT values, and exclude the subnet's own network address.**
  Each host-IP row renders its IP more than once, and the parent's `x.y.z.0` matches the same regex,
  so a naive `len(re.findall(...))` reports 100 or 51 rows on a 50-row page and the pagination check
  fails against correct behaviour.

## Restart the application after any rebuild before measuring

`dotnet run` compiles at start. A result measured from a process that predates an edit describes the
old code. Any phase that follows a code change restarts the instance first and confirms it is serving
the new build.

## Standing rig rules

- Assert on **rendered content and page titles**, never a bare HTTP 200.
- Kill only by **captured PID**. Never `pkill -f Bastet` or `pkill dotnet` - it kills sibling instances,
  and a careless pattern has more than once killed the operator's own shell.
- Give every app instance its **own port and own catalog** so runs cannot collide.
- **One catalog per phase, and every phase builds its own preconditions.** Never inherit state from an
  earlier phase. Sharing a database is what turns a green check into a meaningless one: a phase that
  archives absence rows leaves the next one with nothing to select, and a phase that assumes an import
  an earlier one never performed reports a refusal that never happened. Both occurred. If a phase needs
  drift, it creates that drift itself and asserts the precondition before asserting the behaviour.
- **One AZURE FIXTURE SET per destructive phase, too - a private prefix, not the shared matrix.**
  Phases C, D and F delete VNets, move subnet prefixes and drop address spaces. Run them against the
  base matrix and they consume the fixtures A and B depend on, so nothing can be re-run without
  rebuilding Azure. Give each its own prefix (`<run>c-`, `<run>d-`, `<run>f-`) built at the start of
  the phase, append every id to the same inventory file, and leave the base matrix untouched. This is
  also what makes a phase **re-runnable**: a failed driver is fixed and re-run by dropping its catalog
  and rebuilding only its own prefix.
- **Re-running a phase means resetting BOTH sides.** Drop the catalog
  (`ALTER DATABASE … SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE …`), **restart the app so
  auto-migrate recreates it**, and restore any Azure fixture the previous attempt mutated. A re-run
  against a half-mutated Azure reports refusals and absences that belong to the last attempt.
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

## The report, in this shape

**Application failures come first, before any table.** That is the only part the owner asked for. If
there are none, say so in one line. Do **not** open with what passed, and do **not** lead with, dwell
on, or itemise your own broken checks - a bad check is a note in the failure list, not the story.

Then the phase table, with a **run** column, so an unrun phase cannot be written up as a pass:

| phase | run | checks | app failures |
|---|---|---|---|
| A discovery | yes | 22/22 | 0 |
| ... | | | |
| E ARM failure modes | yes | 20/20 | 0 |

A phase that did not run says **no** and gives the blocker. Any `no` in that column means the run is
**incomplete**, and the first line of the report says so.

Then, and only then: each failure classified *application defect* or *invalid fixture/check* with the
evidence that settled it; anything teardown failed to clean; and - always - a reminder to **revoke
both service principal secrets**. This skill asked for them; this skill reminds you to kill them.

A phase that finds nothing still reports its counts. "All green" without numbers is not a result, and
neither is a count from a phase whose absence assertions had no positive control.
