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

**If a capability ships, making it work correctly is in scope.** Bastet imports Azure subnets, so it
supports multi-prefix subnets, top-ups and re-carves. "That would be a feature change", "the data model
does not support it" and "out of scope" are not available. They describe work, not reasons to decline.

## Do not add problems

**A round must leave fewer defects than it found.** Nothing else here overrides that.

The evidence, and it is damning: round 16 fixed fifteen findings and the re-audit of its own output
found twenty, **twelve of them residue of those fifteen fixes**. The round before was 11 of 15. The fix
process, not the codebase, is the main source of defects.

Three rules follow, all derived from what actually went wrong:

- **A fix commit may not restructure.** Round 16's two structural rewrites — replacing `AccountsFor`
  with a new predicate, and converting an endpoint from filtering to annotating — produced **seven of
  the twelve** residue findings between them. If the correct fix needs a component reshaped, do the
  narrow correct fix now and file the restructure as its own item. A one-finding commit that rewrites a
  component is not a fix, it is an unreviewed refactor with a bug report stapled to it.
- **A green suite is not verification of a fix.** It was green for all twelve. Re-run the finding's own
  reproduction against the fix, and re-run the reproductions of every fix already made this round.
- **Do not override a verifier's correction on your own reasoning.** Round 16's P18 exists because the
  verifier said to put `ModelState.Remove` in the concurrency catch, and a "better placement" was chosen
  instead — which refreshed the concurrency token on every failure path and silently defeated optimistic
  concurrency. If a correction looks wrong, reproduce why before departing from it.

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
- **The sibling surface.** Bastet has two Azure import wizards; a rule wrong in one is wrong in the
  other unless you can point at the difference.
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

This is not cosmetic. It generated four of round 16's findings and several of the re-audit's.

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

**No essays.** Round 16's struck entries ran to thousands of words that nobody read. The file is a work
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
  protecting gets a counter-test, not a warning comment — comments did not work, and round 16's P11
  exists because a guard was written to match a comment that was false.
- **No literal control characters in source.** Use `private const char Esc = (char)0x1B;`.
- **Migration `.Designer.cs` snapshots are frozen history.**
- **The test count must never regress without a recorded reason.**
- **Scope is the defect, not the line number.** An unrequested refactor is out of scope. The same defect
  at another site never is.

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
   stood when written, not the one later fixes produced. Round 15's O6 and O12 were each correct alone
   and did not compose.
4. **Run the real app** against real SQL Server and request every major area — subnet list, create,
   details, edit, delete, deleted-subnets, purge, host IPs, all-deleted-host-IPs, error pages, all three
   Azure wizards. **Assert rendered content, not HTTP 200.** Confirm security headers ride on a normal
   response.
5. **Read the log.** Classify every `fail:` / `warn:`. Some are expected — a deliberate permission-denied
   probe logs an error by design. State the difference.
6. **With Azure credentials**, drive both surfaces end to end: subscriptions → discovery → single import
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

Round 16: 11 of 15. Its re-audit: 12 of 20. **If it is not falling, say so plainly as the headline** —
it means these steps are not working and the skill needs changing again, not that the codebase is
unusually buggy.

Then report the clean-up owed: revoke credentials, remove containers, delete cloud test resources.

## Delete the findings file — the last thing before release notes

**When every finding is FIXED or REFUTED and the final sweep is clean, delete `docs/AUDIT-FINDINGS-*.md`
— all of them — and commit the deletion.**

```
git rm docs/AUDIT-FINDINGS-*.md
git commit -m "Remove reconciled audit findings"
```

**The files poison the next round.** They are handed to twenty finders as briefing, and what they teach
is what to believe and what not to look at. Round 6 wrote down a wrong decision — that a reproduced
defect was "a feature change, out of scope" — and rounds 7, 8 and 9 inherited it without re-examining,
rounds 10-12 dropped it, and round 13 rediscovered the same live defect independently. Four rounds lost
to a sentence in a file. The struck entries are worse: they encode one round's reasoning as settled
fact, and the next round trusts it instead of looking.

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
