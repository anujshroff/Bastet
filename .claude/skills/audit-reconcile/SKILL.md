---
name: audit-reconcile
description: Work through an existing Bastet audit findings file in docs/, fixing each finding in numeric order, proving the defect before fixing it, and committing one finding per commit. Use when asked to "reconcile the audit", "fix the audit findings", "work through the findings", or "continue the audit fixes". Defaults to per-finding approval; pass "auto" to run straight through. To produce a findings file in the first place use /audit.
---

# Reconcile audit findings

Works `docs/AUDIT-FINDINGS-<N>.md` from the top. Each finding is proven, fixed, verified, struck from
the file, and committed on its own.

**The findings file is the durable record.** There is no separate handoff document — audits and
reconciliation run on the same machine, and each struck entry carries what a later round needs.

## The master rule: do not add problems

**A round must leave the codebase with fewer defects than it found. Nothing else this file says
overrides that.** The purpose is not to produce fixes, or findings, or a record — it is to reduce the
number of real defects in the product, permanently.

That has one blunt consequence and it is the rule most likely to be broken:

- **A fix that closes one site and leaves the same defect at three others has not reduced anything.**
  It has converted one known defect into three unknown ones, which resurface next round with new
  numbers and cost another full cycle. Step 5 is mandatory for this reason and no other.
- **A fix that introduces a new defect is worse than no fix.** Every change is checked for what it
  breaks, not only for what it closes — that is what the over-blocking counter-tests are for. If a
  fix cannot be shown not to break the working case, it does not ship.
- **A fix whose message now says something untrue has added a problem**, even though the code is
  correct. Step 6 exists because four of round 16's fifteen findings were exactly that.

**Sixteen rounds is the evidence.** Round 16 filed fifteen findings and **eleven were residue of
round 15's own fixes** — the audit was not finding a rotten codebase, it was finding the previous
round's output. Measure the residue rate every round (see *Closing out*) and drive it to zero. A
round that leaves the residue rate flat has not worked, whatever else it produced.

## No comments in source

**Do not write comments in `.cs` or `.cshtml` files, and do not restore ones that have been removed.**
This is the repository owner's standing instruction and it is not negotiable inside a round.

What this means in practice, because the previous version of this file leaned hard the other way:

- **The code must carry its own explanation.** A guard whose reason needed a paragraph gets a named
  method or a named local instead — `HasPersistedSiblingFromSameAzureSubnet`, `IsIndeterminate`,
  `FindMoreSpecificParent`. If the name cannot carry it, the shape is wrong.
- **The reasoning goes in the struck entry and the test name**, which are the durable record a later
  round actually reads. `ARangeFullyCoveredByRowsInsideIt_IsNotReported` states the rule; a test that
  fails when someone reverses it is a stronger guard than a comment asking them not to.
- **A rule worth protecting gets a counter-test, not a warning comment.** Comments were being used to
  stop future rounds undoing a fix; they did not work — round 16's P11 exists because a comment
  asserted a false premise and the guard beside it was written to match the comment.

## Mode

**Default: per-finding approval.** Explain the issue and the fix, wait for approval, then apply.
Invoked as `/audit-reconcile auto`: apply, verify, strike and commit each finding without stopping,
pausing only for something that genuinely needs a decision — a missing credential, an ambiguous
requirement, a finding that turns out to be wrong in a way that changes scope.

**State which mode is active before the first finding.**

## Start of run

1. **Check the branch first, before anything else.** `/audit` creates `audit/round-<N>` and commits
   the findings file there; reconciliation then stacks one commit per finding on the same branch.
   `main` must be byte-identical before and after the whole audit-and-reconcile cycle.

   ```
   git branch --show-current
   ```

   If it is `main`, or any branch that is not the `audit/round-<N>` branch carrying the findings file,
   **stop and ask the user to switch to the right one.** Name the candidates:

   ```
   git branch --list 'audit/round-*'
   ```

   **Do not switch, create or rename branches yourself.** Which round is being reconciled is the
   user's call, and guessing it puts a dozen commits on the wrong branch — which is expensive to undo
   once the fixes are interleaved with the findings-file updates.

2. Locate the findings file — `docs/AUDIT-FINDINGS-*.md`, highest number. If every finding in it is
   already struck, say so and stop; there is nothing to reconcile.
3. Read it whole first, including the refuted table and watch list, so an early fix does not
   contradict a later finding.
4. **Baseline:** `dotnet build --no-incremental` (0 warnings) and `dotnet test` (record the count).
   If either differs from what the file claims, **stop and report** rather than building on it.
5. Detect available tooling (see *Rigs* below) and prompt only for what cannot be obtained.

**Order is numeric — D1, D2, D3 — not the audit's "suggested order of attack".** That section is
advice about consequence; the numbering is what keeps the run auditable. Deviate only if asked.

## Per finding

### 1. Re-verify the finding's own claims, against the tree as it is now

They are frequently wrong in detail. Round 4 found **eight** wrong or imprecise, two of which would
have broken the build or the app if applied as written.

- Check references in **all forms**, including fully-qualified. A `[Tags` grep reported zero uses of a
  live attribute applied as `[Bastet.Services.Security.Tags(...)]`.
- **Re-check scope, because earlier fixes change it.** Two DTOs were live when the audit was written
  and dead by the time their finding came up, because the preceding finding removed their only
  consumers.
- For anything being **deleted**, require **zero references *and* zero coverage**. Coverage alone is
  not enough — tests happily cover code with no production caller, which is exactly what two findings
  were about.
- Check that a same-named symbol elsewhere is not live before removing it. `SUBNET_HAS_CHILDREN`
  exists in two services; one died with its method, the other is live in three places.
- **A `[×1]` finding warrants more scepticism than a `[×2]`** — one whole audit pass missed it.

### 2. If the finding is wrong, strike it as refuted

Record the evidence and move on. **Do not invent a fix for a defect that is not there.** A finding
that survives verification can still be wrong once the tree has moved.

### 3. Reproduce the defect before fixing it

*Prove it, don't assert it.* A round-3 finding was confidently wrong and only a probe test disproved
it.

**Write the regression test first and confirm it fails against the unfixed code.** A new test that
passes immediately proves nothing.

To prove failure without dirtying the repo: copy the new test into the scratch copy, revert **only**
the fix there, and run. Expect the failure message to describe the actual defect — if it fails for an
unrelated reason, the test is wrong.

Where no test can reach it (client-side behaviour, framework internals, live Azure), use a rig and
record the measurement as prose in the struck entry.

### 4. Apply the fix

If the audit's suggested fix is wrong, would cause harm, or is more invasive than the defect
warrants, **do the right thing and say so in the record**. Examples worth imitating: one finding's
suggested remedy would have silently dropped user data, and another's would have deleted a working
error display.

### 5. Sweep for the same defect elsewhere — the step that stops the loop

**This is the highest-value step in this file and it is not optional.** Round 15 fixed sixteen
findings; round 16 then filed fifteen, of which **eleven were residue of those very fixes**. Almost
all of them were this shape: the fix closed the call site the finding named and stopped there.

The finding names one location. Before you commit, establish what *else* implements the same rule:

- **Every arm of the conditional you just touched.** If a ternary, an `if/else if/else` or a `switch`
  produced the wrong answer in one branch, read the others and satisfy yourself each is right. O8's
  rename guard covered the middle arm of a three-arm flash ternary; the encompassing arm went on
  announcing *"Successfully renamed parent subnet to 'X'"* for a rename that never happened.
- **The sibling surface.** Bastet has **two** Azure import wizards, single-VNet and bulk. A rule that
  is wrong in one is wrong in the other unless you can point at the difference. Two of round 16's
  findings were "one wizard learned the rule, the other did not".
- **Every other caller of the helper**, and every place the same question is asked *without* the
  helper. A duplicated rule that has since diverged is a defect, not untidiness.
- **The inverse path.** If you fixed a write, check the read that displays it. If you fixed a guard,
  check the code that decides whether to *offer* the guarded action — O5's fix was sound and the
  screen went on advertising a remedy the wizard refuses.
- **The other fixes in this same round.** O6 and O12 were both correct alone and did not compose.

Search for the *concept* as well as the identifier — the prefix string, the status enum member, the
message text — because the sibling frequently does not call the same method:

```
grep -rn "<distinguishing token>" src/ --include=*.cs --include=*.cshtml --include=*.js
```

**Record the sweep in the struck entry**: what you searched for, what it returned, which sites you
fixed, and for any site you did not fix, why. A struck entry with no sweep line is incomplete.

If the sweep turns up a sibling you think should not be fixed, that is the **owner's** decision, not
yours — put it in the struck entry as a recommendation and say what it would cost.

### 6. Check the strings the fix just made true or false

Four of round 16's fifteen findings were the application **stating something untrue** after a fix
changed the behaviour underneath the message. Cheap to prevent, expensive to ship.

- **Any message whose truth depends on what you changed must be re-read.** O1 and O2 added reconcile
  reason text telling the operator to *"correct the recorded range"* — `SubnetController.Edit` refuses
  that for every row that can carry those statuses, so the app's own advice was impossible to follow.
- **Operator-facing text your fix *adds* must name an action that is actually reachable.** Before
  shipping "Import it to mark that subnet fully allocated", drive that import against a target in the
  state the message appears for and confirm it works.
- **A success message must not outlive the action it announces.** If a fix makes a write conditional,
  the flash announcing that write becomes conditional with it.

Grep the view and controller for the strings adjacent to your change, read each against the new
behaviour, and record which ones you checked.

### 7. Sweep for orphans the compiler will not report

Deleting a method routinely strands `using` directives, private constants, locals and parameters —
and C# warns about **none** of them. This happened five separate times in round 4.

After every deletion, check what its removal just made dead. Then check the reverse: that anything
you are about to remove as "also dead" is not live somewhere else.

### 8. Verify

```
dotnet build --no-incremental     # 0 warnings — incremental skips the analyzers
dotnet test                       # full suite
```

Re-run whatever rig demonstrated the defect, now against the fix.

### 9. Strike the finding

Replace the finding in the file with an italic paragraph recording:

- what was done and **why that approach**;
- the evidence — what was measured, not what was assumed;
- **every place the audit's suggestion was rejected or found wrong, with the reason**;
- **the sibling sweep from step 5** — what was searched for, what it returned, what was fixed with it,
  and any site deliberately left alone with the reason;
- **the strings checked in step 6**;
- the test-count delta.

This is the durable part. A later round reads these instead of a handoff.

### 10. Commit

**One finding, one commit, one line, no body** — the code change **and the updated findings file
together**, so the record of why lands with the change itself.

Verify the commit contains only that finding's files — a stray `git rm` from a later finding can be
swept in by `git add -A`, which happened once and needed `git reset --soft` to split. If a recorded
figure turns out wrong, amend rather than leave the record inaccurate.

Hand the message over in a code block with nothing after it:

```
Reject a CIDR increase that would put a host IP on the new broadcast address
```

In approval mode the user commits. In auto mode, commit directly.

**Never push.** This machine has read-only access to the remote; publishing is handled outside this
process.

## Standing constraints

- **"This is an open source tool that anyone can host in any way."** No fix may assume HTTPS, a
  reverse proxy, or outbound internet. Plain-HTTP and air-gapped deployments must keep working.
- **No literal control characters in source.** Use a named constant, e.g.
  `private const char Esc = (char)0x1B;`. Literals are invisible in diffs and get mangled through
  tool round-trips.
- **No comments in `.cs` or `.cshtml`.** See the master rule at the top. Reasoning goes in the struck
  entry, the method name and the test name.
- **Migration `.Designer.cs` snapshots are frozen history** — never "fix" an old column width in one.
- **The test count must never regress without an explicit, recorded reason.** Round 4 legitimately
  went 588 → 576 by deleting tests of dead code, and said so.
- **Stay in scope — but the scope is the defect, not the line number.** An unrequested refactor riding
  along in a fix commit is out of scope and gets mentioned, not made. **The same defect at another
  site is never out of scope**: the other arm of the conditional, the other wizard, the sibling
  controller action, the second implementation of the same rule. Fixing one and leaving the other is
  not discipline — it is how a closed finding comes back next round with a new number, which is
  exactly what eleven of round 16's fifteen findings were. See step 5; it is mandatory.
- **No novels.** Short explanations, one-line commit messages.

## Rigs — ephemeral, never in the repo

The findings file is versioned and commits with each fix. **Tooling is not.** Anything needing
scaffolding runs against a `cp -r` copy of the repo in the scratchpad, never the real tree.

A permanent test ships with a fix **only** when it can be written against infrastructure that already
exists: xUnit, `TestDbContextFactory`, `MockAzureService`, `ControllerTestHelper`. Otherwise the
verification is recorded as prose. Verify `git status` is clean of scaffolding before every commit.

Detect what is present, set up what you can, prompt only for the gap.

| Rig | For | Setup |
|---|---|---|
| **Framework source** | Anything resting on framework internals | Fetch from `dotnet/aspnetcore` or `dotnet/runtime` at the matching release tag. No install. **Try this first — it is free** and it settled two findings outright |
| **Browser** | Wizard and client-JS findings | `Microsoft.Playwright` + chromium in the scratch copy. Build the page from the **shipped** `.cshtml` with only Razor expressions stripped, so nothing is retyped, and load the **exact library versions `_Layout.cshtml` pins** — one finding was jQuery `.prop()` behaviour, so a different jQuery would prove nothing. Better still, drive the **running app** and intercept the POST so the flow is exercised without writing |
| **SQL Server** | Locking, migrations, `sp_getapplock` | `docker run -d -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=<strong> -p 11433:1433 mcr.microsoft.com/mssql/server:2022-latest`. The suite runs SQLite, so this is the only way to execute the real locking path |
| **Coverage** | The dead-code beat | `dotnet-coverage collect -f cobertura -o out.xml "dotnet test"`, cross-referenced against a reference sweep. Attribute per declaring class — method names collide with test names |
| **Live Azure** | Reconciler and import findings | **Must prompt** — see below |

### Azure — prompt for this, it cannot be inferred

The reconciler findings need a service principal scoped to **one** resource group *and* a **second**
resource group it cannot see. With only one, a subscription-scoped list returns everything, nothing is
filtered, and the experiment is vacuous while still appearing to work.

Ask for:

- tenant id, subscription id, client id and secret;
- confirmation that a second resource group exists holding a VNet, which that credential has **no**
  assignment on.

**Warn explicitly:** any role assignment at *subscription* scope inherits into both resource groups
and silently defeats the whole setup. Verify the actual assignments before measuring anything rather
than trusting the description.

Secrets go in **environment variables only** — never a file that reaches the repo. `DefaultAzureCredential`
picks up `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` / `AZURE_TENANT_ID`, so the real production code
path can be driven unmodified. Remind the user to revoke them at the end.

## Final verification sweep — mandatory

Per-finding checks are not enough. Several classes of fix **compile cleanly and fail only when a page
is requested**: dropping the Razor runtime-compilation package, deleting a `_ViewImports`, removing a
view-model property, renaming a partial. Round 4 did all four. Razor resolves views, partials and
imports at *render* time, so a green build and a green suite would not have caught any of them.

1. **Clean rebuild** — delete `bin`/`obj`, then `dotnet build --no-incremental`. 0 warnings.
2. **Full suite**, count reconciled against the baseline. Any drop needs a recorded reason.
3. **Coverage re-run** if the round touched dead code, compared against the pre-round run: the deleted
   methods are gone, and nothing new went dark.
4. **Re-drive every fix in this round against the final tree.** Each was verified against the tree as
   it stood when it was written, not against the tree the later fixes produced. Round 15's O6 and O12
   were each correct in isolation and did not compose: O6 restored the fully-encompassing row to the
   single-VNet wizard, O12 added the population guard elsewhere, and together they offer exactly one
   selectable row whose only outcome is an error the other wizard greys out. Walk
   `git log <base>..HEAD` and re-run each commit's own reproduction. **A fix that no longer
   demonstrates its defect closed is this round's problem, not next round's finding.**
5. **Run the real app** against real SQL Server and request every major area — subnet list, create,
   details, edit, delete, deleted-subnets, purge, host IPs, all-deleted-host-IPs, error pages, both
   Azure wizards. **Assert real content and titles, not just HTTP 200.** Confirm the security headers
   still ride on a normal response.
6. **Read the log.** Classify every `fail:` / `warn:` line — some are expected, because a deliberate
   permission-denied probe logs an error *by design*. State the difference rather than glossing it.
7. **If Azure credentials are available**, drive both surfaces end to end against live ARM:
   subscriptions → VNet/subnet discovery → single-subnet import → bulk preview and commit → reconcile
   scan → delete commit. Include the two counter-tests that prove the reconciler **discriminates**
   rather than merely blocks:
   - a resource the credential *cannot see* must be **withheld**, with a warning naming it;
   - a genuinely deleted resource must **still be offered and deletable**.

   Checking only the first would let an over-blocking regression pass silently.
8. **`git status` clean**, and no scaffolding in any commit.

## Closing out

Update the header of the findings file with the final build and test numbers, the result of the
sweep, and a short list of what was deliberately not done. The struck entries already carry the
reasoning.

**Report the residue rate — the one number that says whether this loop is converging.** The findings
file names, for each finding, which previous-round fix it came out of. Count them and state it plainly
in the closing summary and in the findings file header:

> Round `<N>` filed `<F>` findings, of which `<R>` were residue of round `<N-1>`'s own fixes.

Round 16's was **11 of 15**, and that is why steps 5 and 6 exist. If the next round's residue is not
materially lower, **say so rather than letting it drift** — it means these steps are not working and
the skill needs changing again, not that the codebase is unusually buggy.

**If the final sweep turned up anything needing resolution, record it in the file and commit that
too** — a closing commit carrying the sweep's result. If the sweep was clean, the last fix commit
already carries the final state.

Then report the clean-up owed: revoke any credentials supplied, remove containers
(`docker rm -f <name>`), delete cloud test resources.

## Release notes — the last thing produced

Finish by writing release notes for the round and presenting them **in chat as one fenced markdown
block the user can paste straight into a GitHub release**. Do not write them to a file and do not
commit them — they are a deliverable, not an artefact of the repo.

Derive every line from the commits actually made (`git log <base>..HEAD`), never from the findings
file. Findings struck as refuted, and anything deliberately not done, must not appear.

### Shape

Three sections, in this order, each **omitted entirely when it would be empty**:

```
### New Features
### Improvements
### Bug Fixes
```

### Voice

- Write for someone **running** Bastet, not someone reading the diff. They want to know what changes
  for them, not which method was edited.
- One bullet per user-visible change: `- **What changed** — what it means, or what was wrong`.
- The bold span is the subject, not decoration. It may sit mid-sentence where that reads better.
- Configuration keys, headers, routes and identifiers go in backticks.
- A parenthetical is the right place for a caveat or an opt-out — typically the env var that restores
  the previous behaviour.

**New Features** — genuinely new capability: a page or workflow that did not exist before.
**Improvements** — hardening, defaults, messaging, anything now safer or clearer.
**Bug Fixes** — lead with the **symptom someone could have hit**, then what was actually wrong.
"X could Y" is the usual shape. Never describe a fix in terms of the code that changed.

### Judgement

- **Group aggressively.** A round that deletes twenty pieces of dead code gets *one* bullet, not
  twenty. Internal cleanup with no observable effect is worth a single summary line, or nothing.
- **A finding that produced no user-visible change belongs in no bullet.** Leaving it out is correct,
  not an omission.
- Do not number bullets, cite finding IDs (`D1`, `D2`…), mention the audit process, or pad the list to
  look substantial. Short and accurate beats long.

### Example of the target register

```
### Improvements
- **Security response headers** now ride on error responses too — the 500 page was the one response
  class shipping without `X-Content-Type-Options`, `Referrer-Policy` and `frame-ancestors`
- CDN scripts load with **Subresource Integrity**, so a tampered CDN cannot inject code

### Bug Fixes
- The Azure import wizard could **import subnets you had deselected** — toggling "Select All" left
  previously-ticked rows armed, so they were submitted anyway
- Reconcile could offer **live Azure subnets for deletion** when the credential could only see part of
  a subscription — a filtered result was read as "deleted"
```
