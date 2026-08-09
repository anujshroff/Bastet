---
name: audit-reconcile
description: Work through an existing Bastet audit findings file in docs/, fixing each finding in numeric order, proving the defect before fixing it, and committing one finding per commit. Use when asked to "reconcile the audit", "fix the audit findings", "work through the findings", or "continue the audit fixes". Defaults to per-finding approval; pass "auto" to run straight through. To produce a findings file in the first place use /audit.
---

# Reconcile audit findings

Works `docs/AUDIT-FINDINGS-<N>.md` from the top. Each finding is proven, fixed, verified, marked FIXED
and committed on its own.

## What Bastet is

**An IPAM tool. Its job is to be the authority on which IP space is allocated and which is free, and to
let an operator manage that space.** Every judgement call resolves against that, in this order:

1. **Never report allocated space as free.** It is the worst output the product can produce. Reporting
   free space as allocated is second-worst.
2. **Never destroy an allocation record on incomplete information.** Archives are irreversible.
3. **The operator must be able to act on what they are told.** A message naming a remedy the app
   refuses is a defect, not a cosmetic issue.

**Rule 0, which overrides all three: Bastet is the authority, and it answers from its own records.**
Free means free *according to Bastet*. **Azure is not authoritative in Bastet at all** — it is a source
you import *from*, which is exactly why the import wizard exists. Until a range is imported it does not
exist as far as Bastet is concerned.

So a finding of this shape is **invalid, and the job is to strike it, never to implement it**:

> "Bastet shows 10.20.9.32 as free, but an Azure subnet Bastet never imported holds it."

If a finding needs Bastet to know about un-imported Azure space to be a defect, it is not a defect.
Record it as struck with that reason and write no code. Same for a wizard filter hiding a row that
cannot be imported (correct — ticking it would change nothing), and for reconcile returning a clean
scan over a partially imported subscription (also correct). Rounds have filed these repeatedly and the
owner has struck every one.

**Azure state and Bastet state are compared in exactly two places, and nowhere else:** the bulk import
wizard, which asks *can this be added?*, and reconcile, which asks *can this be deleted?* That is the
whole Azure/Bastet arithmetic in the product. Every other screen — the subnet tree, Details,
unallocated ranges, host IPs, search — answers from Bastet's records alone and must never consult
Azure. **A fix that would make any other surface aware of Azure is out of bounds**, and a finding that
asks for one gets struck rather than implemented.

**If the whole of a finding is that a string is untrue, fix the string and nothing else** — no new
status, no new guard, no new branch behind it.

**One flat, routable space.** Bastet manages a single IP space in which everything is routable against
everything else, so the same range must never be allocated twice — preventing that collision is the
product's reason to exist. Two consequences that decide fixes:

- **"Still allocated" is a question about the whole managed space, never about provenance.** If any live
  resource holds a range, that range is in use, whichever VNet, subscription or import it came from, and
  archiving Bastet's only record of it reports an in-use range as free. Overlapping Azure VNets are not
  a case to defend — inside Bastet's model that overlap *is* the collision, not a legitimate
  configuration, so never soften a fix to accommodate it.
- **How the space is carved up is the operator's choice, and only theirs** — by hand, by Azure import,
  or both. Neither origin is privileged: a rule that holds for a manually created subnet holds
  identically for an imported one, and vice versa. When sweeping for the same defect elsewhere, the
  manual path and the import path are always siblings of each other.
- **One Azure range is one Bastet row.** A VNet with a single address prefix whose single subnet covers
  that whole prefix is **one** row, not a VNet parent plus a byte-identical child. The import marks that
  row fully allocated instead of creating the duplicate. Never fix in a direction that produces the
  second row: a parent and child with the same CIDR *are* the collision the product exists to prevent.

**A row carrying an Azure resource id is a record the operator asked Bastet to keep in step with Azure.**
Azure is still not authoritative — Bastet is — but for *that row* the operator said "track this", so when
Azure no longer has the resource, or no longer holds the recorded range, **reconcile must say so and
offer the delete**. It reports; the operator decides; nothing is removed on Azure's word alone. Silently
withholding the report is its own defect — it leaves Bastet asserting an allocation the operator was
never told to reconsider. The **only** legitimate reason to refuse is **manual content in that hierarchy — a
hand-added child subnet, or a host IP** — operator-owned data Azure does not know about, which must
never be destroyed silently. Nothing else qualifies, and a filed fix that withholds on any other ground
must not be applied as filed.

**A row the operator built by hand can be adopted by the VNet it matches.** Linking a not-yet-linked
target is real work and stays offered **whether or not that row already has children** — the operator
may well have carved the space by hand first and now want Bastet to track it against Azure. Having
children is not a conflict, and refusing on it keys on *provenance*, which is never a reason: the
identical shape with the link already present is advertised as "Will add any missing subnets".
The refusals that remain are the real conflicts, and they are decided **per subnet, not per prefix**:
an Azure subnet that would contain, or be contained by, an existing Bastet row is refused by name,
while its clean siblings still import and the operator's own subnets are left untouched and unlinked.
So the correct outcome for a hand-built tree that partly overlaps Azure is **partial adoption**, never
a whole-prefix refusal.

**The wizard's client must not re-derive a decision the planner already made.** `IsSelectable` is the
planner's answer to "does ticking this do anything?" - so any client-side filter, badge or gate that
re-answers it by enumerating status names is a second implementation that drifts the moment a status is
added. It has already shipped once: "Only show what would change" tested `statusName === "Available"`
and so hid every linkable row, i.e. hid exactly the work it promised to show. **Prefer deleting the
duplicate over extending it.**

**The import wizard must not offer work that is not work.** A VNet prefix already linked to its Bastet
subnet, with every Azure subnet under it already recorded, is `AlreadyImported` and **not selectable** —
there is nothing to add. So is a collapsed target this same VNet has already marked fully allocated.
Two things still count as work and must stay offered: **linking a target that is not yet linked**, and
**renaming** when the operator has asked for renames.

**Renaming is gated on the Azure link, and on nothing else.** When the operator asks for renames, the
wizard offers a rename for **both** the VNet target row **and** every already-imported child subnet
whose Bastet name has drifted from its Azure name — the control says "subnets" and must mean it, since
nothing else in the product can bring a drifted child name back into step. The single condition is that
the row **already carries the Azure resource id** it is being renamed to match:

- linked, and the name differs → offer the rename, and perform it
- **not** linked, or linked to a different Azure resource → **"cannot import" stands, and no rename is
  ever performed** — the range matching is not enough, because an unlinked row is operator-owned data
  that this Azure resource has no claim on
- fully allocated makes no difference to a rename *on its own*: a rename creates nothing inside the
  target, so a linked fully-allocated row is renameable. It stays refused the moment the same selection
  would also create a subnet inside it, which is a real conflict. "Only show what would change" hides exactly the rows that
would change nothing, which is only correct while those four cases are classified correctly.

**Reconcile does exactly two things. A fix that grows it past them is not a fix.**

1. **Report Azure resources that are gone**, so the operator can choose to delete the Bastet row.
2. **Report Azure resources whose range changed**, so the operator can choose to delete the Bastet row.

Everything it is *not* has been tried and removed; do not restore any of it under a new name:

- **It never edits a row** — not the range, not the name, not the Azure link. There is no re-link.
- **It never re-adds anything.** After deleting a row whose range changed, the operator goes back
  through the **bulk import wizard**. Any message reconcile shows about a changed range must point
  there; naming any other remedy breaks rule 3, because reconcile cannot create subnets.
- **It never hunts for un-imported Azure space** — that is the import wizard's job, and rows reconcile
  reports that way cannot be acted on from the reconcile screen.
- **It decides on the linked resource alone.** Never widen a fix to consult other VNets, other
  subscriptions, or what else Bastet records.

**The resource id is the only join key, and that is why the refusal list is one item long.** Reconcile
asks Azure one question: *does the resource this row names still exist, and does it still hold this
range?* Both are answered by looking up `AzureResourceId`. The address range is never a lookup key — it
is only compared against **that same resource's own** current prefixes. Gone means delete. Manual
content is the single exception precisely because it is the one thing the resource id cannot tell you
about: Azure has no record of a hand-added child subnet or a host IP, so acting on Azure's word would
destroy data Azure never knew existed.

**A fix that reasons about a range rather than a resource id is wrong by construction.** "The range
showed up in another VNet", "another subscription still holds it", "the prefix is still covered after
the re-carve" — each answers a question reconcile does not ask, by matching ranges across resources.
That is the exact shape of every withhold that has had to be removed from this reconciler. If a filed
fix needs cross-resource range matching, do not apply it; record that the finding misdiagnosed the
defect.

When a finding proposes a new status, guard or withhold path in the reconciler, the first question is
whether the mechanism it extends should exist at all. **Prefer the fix that deletes machinery.**

**Reconcile and the bulk import wizard are clients of the IPAM, not privileged writers.** Every change they
make — delete, edit, add — goes through the same validation a manual operation goes through. They must
not write to the database directly to bypass a check, and no fix may give them their own copy of a rule
that lets them persist a state the validated path would reject. Where Azure's state cannot be
represented without breaking a validation rule, report the conflict to the operator; never widen the
Azure path to write it anyway.

**Rule 1 is symmetric.** "Never report allocated space as free" has a twin: **never pin an allocation
record Azure says is gone.** A withheld deletion is not a safe default — it leaves Bastet asserting that
free space is allocated, indefinitely, with no operator action that clears it. Before applying any fix
that refuses, guards or withholds, say which half it serves and check it does not simply trade one half
for the other.

**If a capability ships, making it work correctly is in scope.** Bastet imports Azure subnets, so it
supports multi-prefix subnets, top-ups and re-carves. "That would be a feature change", "the data model
does not support it" and "out of scope" are not available. They describe work, not reasons to decline.

## Do not add problems

**A round must leave fewer defects than it found.** Nothing else here overrides that.

The evidence, and it is damning: re-auditing a completed round has repeatedly found *more* defects
than the round fixed, the large majority of them residue of those very fixes. The fix process, not the
codebase, is the main source of defects.

Four rules follow, all derived from what actually went wrong:

- **A fix commit may not restructure.** Two structural rewrites in a single round — swapping a core
  predicate for a new one, and converting an endpoint from filtering to annotating — produced more than
  half that round's residue between them. If the correct fix needs a component reshaped, do the narrow
  correct fix now and file the restructure as its own item. A one-finding commit that rewrites a
  component is not a fix, it is an unreviewed refactor with a bug report stapled to it.
- **A green suite is not verification of a fix.** It has been green for every residue finding ever
  filed. Re-run the finding's own reproduction against the fix, and re-run the reproductions of every
  fix already made this round.
- **A fix that adds a guard, refusal, withhold, status or special case is a design change, not a bug
  fix.** Before writing it, name the sentence in *What Bastet is* it serves. If you cannot, the finding
  has misdiagnosed the defect — stop and record that instead of implementing it. Four consecutive
  rounds each added a withhold nobody had asked for, each closing a real-looking failure scenario, and
  together they more than quadrupled the size of `AzureReconciler.cs`. Every one passed a green suite.
- **When a finding proposes extending a mechanism a previous round added, audit the mechanism first.**
  `git log -S` the identifying string and read the commit that introduced it. A fix already in the tree
  reads as settled design and is not — it is one previous round's opinion, and extending it compounds
  the error instead of ending it. The correct outcome may be to remove it, which is a product decision:
  record it in the findings file and stop, do not implement either direction on your own authority.
- **Do not override a verifier's correction on your own reasoning.** A past round shipped a defect
  exactly this way: the verifier said to put `ModelState.Remove` in the concurrency catch, a "better
  placement" was chosen instead, and it refreshed the concurrency token on every failure path —
  silently defeating optimistic concurrency. If a correction looks wrong, reproduce why before
  departing from it.

## No questions

**Never ask how to fix something.** Implementation is yours: which predicate, where the guard goes, what
the message says, which of two sound fixes to take. Asking means you have not understood the product
well enough to be working on it — re-read *What Bastet is* and decide.

**Never make a product decision either.** Where a fix implies a change to what the product does, take
the option that closes the reproduced defect with the smallest behaviour change that is actually
correct, and record the question in the findings file for the owner to read afterwards. Do not stop and
ask. Do not block the run.

In `auto` mode, run to completion. Pause only for something that makes further work impossible — a dead
credential, a baseline that will not build.

## Mode

**Default: per-finding approval.** State the issue and the fix, apply on approval.
`/audit-reconcile auto`: apply, verify, mark and commit each finding without stopping.

State which mode is active before the first finding.

## Start of run

1. **Check the branch.** `/audit` creates `audit/round-<N>` and commits the findings file there;
   reconciliation stacks one commit per finding on it. `main` must be byte-identical before and after.

   ```
   git branch --show-current
   git branch --list 'audit/round-*'
   ```

   If it is `main` or the wrong round, **stop and ask which branch** — that is a fact only the user has,
   not a how-to-fix question. Do not switch, create or rename branches yourself.

2. Locate the findings file — `docs/AUDIT-FINDINGS-*.md`, highest number. If every finding is already
   marked FIXED, say so and stop.
3. Read it whole first, so an early fix does not contradict a later finding.
4. **Baseline:** `dotnet build --no-incremental` (0 warnings) and `dotnet test` (record the count). If
   either differs from what the file claims, stop and report.
5. Detect available tooling (see *Rigs*).

**Order is numeric.** Deviate only if asked.

## Per finding

### 1. Re-verify the finding's claims against the tree as it is now

They are frequently wrong in detail, and earlier fixes move the ground under later ones.

- Check references in **all forms**, including fully-qualified.
- For anything being deleted, require **zero references and zero coverage**.
- Check a same-named symbol elsewhere is not live before removing it.
- **A `[x1]` warrants more scepticism than a `[x2]`** — one full pass missed it.

If the finding is wrong, mark it REFUTED with the evidence and move on. Do not invent a fix for a defect
that is not there.

### 2. Reproduce the defect before fixing it

*Prove it, don't assert it.*

**Write the regression test first and confirm it fails against the unfixed code.** A new test that
passes immediately proves nothing. To prove failure without dirtying the repo, `cp -r` the repo to
scratch, revert only the fix there, and run.

Where no test can reach it — client-side behaviour, framework internals, live Azure — use a rig and
record the measurement in the entry.

### 3. Apply the narrow fix

**The smallest change that actually closes the defect.** If the audit's suggested fix is wrong or would
cause harm, do the right thing and record it.

**If the correct fix requires restructuring, stop and reconsider.** Nearly always a narrow fix exists
that closes the reproduced defect. Take it, and file the restructure as its own item in the file. See
*Do not add problems*.

### 4. Sweep for the same defect elsewhere

The finding names one location. Before committing, establish what else implements the same rule:

- **Every arm of the conditional you touched.**
- **The sibling surface.** The manual path and the Azure path are siblings of each other: a rule
  wrong in `SubnetController.Create` is usually wrong in the bulk import commit, and vice versa.
- **Every other caller**, and every place the same question is asked without the helper.
- **The inverse path.** Fixed a write? Check the read that displays it. Fixed a guard? Check what decides
  whether to offer the guarded action.
- **The other fixes in this round.**

Search for the *concept* as well as the identifier — the prefix string, the enum member, the message
text — because the sibling often does not call the same method.

Fix every site the sweep finds. Record what you searched for and what it returned.

### 5. Check the strings your change made true or false

Any message whose truth depends on what you changed must be re-read. Operator-facing text your fix adds
must name an action that is actually reachable — drive it. A success message must not outlive the action
it announces.

This is not cosmetic. Stale operator-facing text is one of the most reliable sources of residue.

### 6. Sweep for orphans the compiler will not report

Deleting a method strands `using` directives, private constants, locals and parameters, and C# warns
about none of them. After every deletion, check what it made dead — then check anything you are about to
remove as "also dead" is not live elsewhere.

### 7. Verify

```
dotnet build --no-incremental     # 0 warnings
dotnet test                       # full suite
```

Then, because the suite is not enough: **re-run this finding's own reproduction against the fix, and
re-run the reproduction of every fix already made this round.** A fix that no longer demonstrates its
defect closed is this round's problem, not next round's finding.

### 8. Mark it FIXED

Append ` — FIXED` to the finding's heading and replace its body with **at most four lines**:

```
_Fixed in <sha>. <What changed, one sentence.>_
_Swept: <what was searched, what else was fixed>._
_Verified: <what was run, what came back>._
_Not done: <anything deliberately left, and why> — omit this line if nothing._
```

**No essays.** Struck entries have run to thousands of words that nobody read. The file is a work
queue, not a report.

### 9. Commit

**One finding, one commit, one line, no body** — the code change and the updated findings file together.

Verify the commit contains only that finding's files. Hand the message over in a code block with nothing
after it:

```
Reject a CIDR increase that would put a host IP on the new broadcast address
```

In approval mode the user commits. In auto mode, commit directly. **Never push.**

## Standing constraints

- **Open source, self-hosted by anyone.** No fix may assume HTTPS, a reverse proxy, or outbound
  internet. Plain-HTTP and air-gapped deployments must keep working.
- **No comments in `.cs` or `.cshtml`, and do not restore removed ones.** The code carries its own
  explanation through named methods; the reasoning goes in the entry and the test name. A rule worth
  protecting gets a counter-test, not a warning comment — comments did not work, and one round shipped
  a guard written to match a comment that was already false.
- **No literal control characters in source.** Use `private const char Esc = (char)0x1B;`.
- **Migration `.Designer.cs` snapshots are frozen history.**
- **The test count must never regress without a recorded reason.**
- **Scope is the defect, not the line number.** An unrequested refactor is out of scope. The same defect
  at another site never is.

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

## Rigs — ephemeral, never in the repo

Anything needing scaffolding runs against a `cp -r` copy in the scratchpad, never the real tree.

A permanent test ships with a fix **only** when it can be written against existing infrastructure:
xUnit, `TestDbContextFactory`, `MockAzureService`, `ControllerTestHelper`. Otherwise record the
measurement as prose. Verify `git status` is clean of scaffolding before every commit.

| Rig | For | Setup |
|---|---|---|
| **Framework source** | Anything resting on framework internals | Fetch from `dotnet/aspnetcore` or `dotnet/runtime` at the matching tag. Free — try it first |
| **Browser** | Wizard and client-JS findings | Playwright chromium; if absent, `pip install playwright` then `playwright install chromium` (unattended, no sudo). Drive the **running app** and intercept the POST |
| **SQL Server** | Locking, migrations, `sp_getapplock` | `docker run -d -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=<strong> -p 11433:1433 mcr.microsoft.com/mssql/server:2022-latest`. The suite runs SQLite, so this is the only way to execute the real locking path |
| **Coverage** | The dead-code beat | `dotnet-coverage collect -f cobertura -o out.xml "dotnet test"` |
| **Live Azure** | Reconciler and import findings | Needs two service principals with **disjoint** resource-group scope — see below |

### Azure

The reconciler findings need a principal scoped to **one** resource group and a **second** group it
cannot see. With only one, a subscription-scoped list returns everything and the experiment is vacuous
while appearing to work.

Ask for tenant id, subscription id, and both principals' client id and secret **once, up front**, in the
same message as anything else missing. Verify the role assignments are at resource-group scope, not
subscription scope, before measuring anything.

Secrets go in a scratchpad env file and are referenced as variables — never on a command line, never in
the repo. `DefaultAzureCredential` picks up `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` /
`AZURE_TENANT_ID`, so the production path runs unmodified; ensure `AZURE_TOKEN_CREDENTIALS` is **unset**.
Remind the user to revoke them at the end.

## Final verification sweep — mandatory

Per-finding checks are not enough. Several classes of fix compile cleanly and fail only when a page is
requested: dropping Razor runtime compilation, deleting a `_ViewImports`, removing a view-model
property, renaming a partial. Razor resolves at render time.

1. **Clean rebuild** — delete `bin`/`obj`, `dotnet build --no-incremental`. 0 warnings.
2. **Full suite**, reconciled against the baseline.
3. **Re-drive every fix in this round against the final tree.** Each was verified against the tree as it
   stood when written, not the one later fixes produced. Two fixes in one past round were each correct
   in isolation and did not compose.
4. **Run the real app** against real SQL Server and request every major area — subnet list, create,
   details, edit, delete, deleted-subnets, purge, host IPs, all-deleted-host-IPs, error pages, and both
   Azure wizards - bulk import and reconcile. **Assert rendered content, not HTTP 200.** Confirm security headers ride on a normal
   response.
5. **Read the log.** Classify every `fail:` / `warn:`. Some are expected — a deliberate permission-denied
   probe logs an error by design. State the difference.
6. **With Azure credentials**, drive both surfaces end to end: subscriptions → discovery
   → bulk preview and commit → reconcile scan → delete commit. Include the two counter-tests:
   - a resource the credential *cannot see* must be **withheld**, with a warning naming it;
   - a genuinely deleted resource must **still be offered and deletable**.

   Checking only the first lets an over-blocking regression pass.
7. **`git status` clean**, no scaffolding in any commit.

## Closing out

Update the findings file header with the final build and test numbers and the sweep result.

**Report the residue rate — the number that says whether this is converging.** Each finding names the
previous-round fix it came from. Count them:

> Round `<N>` filed `<F>` findings, of which `<R>` were residue of round `<N-1>`'s own fixes.

**If it is not falling, say so plainly as the headline** — it means these steps are not working and the
skill needs changing again, not that the codebase is unusually buggy.

Then report the clean-up owed: revoke credentials, remove containers, delete cloud test resources.

## Delete the findings file — the last thing before release notes

**When every finding is FIXED or REFUTED and the final sweep is clean, delete `docs/AUDIT-FINDINGS-*.md`
— all of them — and commit the deletion.**

```
git rm docs/AUDIT-FINDINGS-*.md
git commit -m "Remove reconciled audit findings"
```

**The files poison the next round.** They are handed to twenty finders as briefing, and what they teach
is what to believe and what not to look at. One round wrote down a wrong decision — that a reproduced
defect was "a feature change, out of scope" — and three successive rounds inherited it without
re-examining, until a later round rediscovered the same live defect independently. Four rounds lost to
a sentence in a file. The struck entries are worse: they encode one round's reasoning as settled fact,
and the next round trusts it instead of looking.

A round should meet the code as it is, with no inherited beliefs. Everything durable is already in git:

- **The residue rate** comes from `git log` and `git blame` on the cited lines, which is how the
  verifiers corrected several attributions this round anyway — more reliable than a finder's claim.
- **What was fixed** is the commit history, one commit per finding, with the reasoning in the message.
- **What is permanently accepted** lives in `/audit`'s own skill file, not in a findings file.

The cost is that a round may re-derive something a previous round refuted. That is the cheaper error: a
refuted finding costs one verifier, while an inherited wrong refutation costs rounds. If it is real, it
deserves the second look.

Do not delete a file with unreconciled findings still in it.

## Release notes — the last thing produced

Write release notes for the round and present them **in chat as one fenced markdown block** the user can
paste into a GitHub release. Do not write them to a file and do not commit them.

Derive every line from the commits actually made (`git log <base>..HEAD`), never from the findings file.
Refuted findings and anything not done must not appear.

Three sections, each **omitted entirely when empty**: `### New Features`, `### Improvements`,
`### Bug Fixes`.

- Write for someone **running** Bastet. One bullet per user-visible change:
  `- **What changed** — what it means, or what was wrong`.
- Configuration keys, headers, routes and identifiers in backticks.
- **Bug Fixes** lead with the **symptom someone could have hit**, then what was wrong. Never describe a
  fix in terms of the code that changed.
- **Group aggressively.** Twenty dead-code deletions get one bullet. A finding with no user-visible
  change gets none.
- Do not number bullets, cite finding ids, mention the audit process, or pad the list.

```
### Improvements
- **Security response headers** now ride on error responses too — the 500 page was the one response
  class shipping without `X-Content-Type-Options`, `Referrer-Policy` and `frame-ancestors`

### Bug Fixes
- The Azure import wizard could **import subnets you had deselected** — toggling "Select All" left
  previously-ticked rows armed, so they were submitted anyway
- Reconcile could offer **live Azure subnets for deletion** when the credential could only see part of
  a subscription — a filtered result was read as "deleted"
```
