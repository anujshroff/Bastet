---
name: audit-reconcile
description: Work through an existing Bastet audit findings file in docs/, fixing each finding with proof, independent adversarial review of every fix, and a whole-diff gate before the round is done. Use when asked to "reconcile the audit", "fix the audit findings", "work through the findings", or "continue the audit fixes". Defaults to a single up-front triage approval; pass "auto" to run straight through. To produce a findings file in the first place use /audit.
---

# Reconcile audit findings

Works `docs/AUDIT-FINDINGS-<N>.md`. Each finding is proven, fixed, **reviewed by an agent that did
not write the fix**, marked FIXED and committed. The round ends with a whole-diff gate, not a green
suite.

**What Bastet is, what counts as a defect, and what a fix may never do live in
`docs/PRODUCT-MODEL.md` — the single copy, shared with `/audit`. Read it whole before the first
finding. Nothing in this file overrides it.** `docs/AUDIT-LEDGER.md` is the loop's memory — main is
squash-merged, so per-finding commits and messages do not survive to it; the ledger does.

## Why this skill is shaped this way

Re-auditing completed rounds repeatedly found *more* defects than the round fixed, the large
majority residue of those very fixes — residue ran 11/15, 12/21, then 20/23 across rounds 16–18.
The fix process, not the codebase, was the main defect source, and the single biggest cause was
that **every fix was verified only by its own author**, whose defects surfaced a full round later.
Hence: independent review of every fix (step 8), a whole-diff gate before the round is declared
done, a cap on how much churn one round may push into one file, and **a round must leave fewer
defects than it found — nothing else here overrides that.** The loop's terminal state is a
zero-finding round (`docs/PRODUCT-MODEL.md` §5): a fix closes its defect without manufacturing the
next round's audit surface.

## Mode

**Default:** one up-front triage approval (below), then run to completion without further pauses.
**`auto`:** skip the triage pause too — apply the recommended dispositions and run straight
through, pausing only for something that makes work impossible (a dead credential, a baseline that
will not build). State which mode is active before triage.

**Never ask how to fix something, and never make a product decision.** Implementation is yours:
which predicate, where the guard goes, what the message says. Where a fix implies a change to what
the product does, take the option that closes the reproduced defect with the smallest behaviour
change that is actually correct, and record the one-line product question in the findings file for
the owner to read afterwards. Do not block.

## Start of run

1. **Commit the findings file if it is untracked.** The VM reverts daily and only committed files
   survive; an uncommitted findings file is a whole audit round one revert from oblivion. Standing
   check, every run.
2. **Check the branch.** `/audit` creates `audit/round-<N>` and commits the findings file there;
   reconciliation stacks commits on it. `main` must be byte-identical before and after.

   ```
   git branch --show-current
   git branch --list 'audit/round-*'
   ```

   If it is `main` or the wrong round, **stop and ask which branch** — that is a fact only the user
   has, not a how-to-fix question. Do not switch, create or rename branches yourself.
3. Locate the findings file — highest `docs/AUDIT-FINDINGS-*.md`. If every finding is already
   terminal, say so and stop.
4. Read `docs/PRODUCT-MODEL.md` and `docs/AUDIT-LEDGER.md`, then the findings file whole, so an
   early fix does not contradict a later finding.
5. **Baseline:** `dotnet build --no-incremental` (0 warnings) and `dotnet test` (record the count).
   If either differs from what the file claims, stop and report.
6. Detect available tooling (see *Rigs*).

## Triage — one pass, before any fix

Build one table — finding id, severity, one line, and a recommended disposition — and put it to the
owner in a single message. Dispositions:

- **fix** — will be fixed this round.
- **defer** — real, but its correct fix is structural work this round must not do (see the split
  rule); or it overflows the WIP cap. Deferred findings are transplanted **whole** (all fields,
  including the repro) into `docs/DEFERRED-FINDINGS.md`, committed, and get a `deferred` ledger row.
  Deferral is a terminal state for this round, not a euphemism for dropped: the file is the work
  queue for the effort that closes them.
- **strike** — invalid per the product model, with the sentence cited. A struck finding gets a
  `struck` ledger row, and **the owner's words go into PRODUCT-MODEL.md §8 verbatim** — never your
  paraphrase; a wrong paraphrase written there becomes canon. Where the ruling is testable, write
  the counter-test as a work item this round.

Recommend dispositions from the product model and the ledger — a finding proposing to extend a
mechanism gets `git log -S` on the identifying string first, and if a previous round added it, the
recommendation leans **remove**, not extend. The owner edits the table in one reply; in `auto`
mode the recommendations stand, except that striking on your own authority requires a product-model
citation.

## Batching and the WIP cap

Group approved findings by component and fix a component's batch together — an early fix moves the
ground under later ones, and interleaving twenty fixes across three files is how fixes stop
composing. **Order machinery-deletions before string fixes in the same file**, and re-cite line
numbers after each fix lands.

**Cap: three fix-commits per source file per round.** Overflow → defer, highest severity first
stays. Two exemptions: a sibling sweep is one fix at N sites, not N fixes; and one commit may close
several findings when the fix is genuinely shared — list every id in the FIXED entries. Findings
tagged `strings` are fixed individually but committed as one batch commit listing their ids.

## Per finding

### 1. Re-verify the claims against the tree as it is now

They are frequently wrong in detail, and earlier fixes move the ground. Check references in **all
forms**, including fully-qualified; for anything being deleted, require **zero references and zero
coverage**; check a same-named symbol elsewhere is not live. **A `[x1]` warrants more scepticism
than a `[x2]`** — one full pass missed it. If the finding is wrong, mark it REFUTED with the
evidence and move on; do not invent a fix for a defect that is not there.

### 2. Reproduce the defect before fixing it

*Prove it, don't assert it.* **Write the regression test first and confirm it fails against the
unfixed code** wherever existing test infrastructure can reach the behaviour — a new test that
passes immediately proves nothing. To prove failure without dirtying the repo, `cp -r` the repo to
scratch, revert only the fix there, and run. Where no seam exists — client-side behaviour,
framework internals, live Azure — use a rig, record the measurement in the entry, **and record the
surface durably: name it in the fix's ledger row and add the drive to `/e2e`'s coverage
(PRODUCT-MODEL §5). The FIXED entry alone is not a record — the findings file is deleted at
close-out, which is exactly how rounds 25-and-earlier leaked unpinned fixes into the next audit.**
A recorded gap is settled; a fix left both unpinned and unrecorded is handing the next round a
finding.

### 3. Apply the narrow fix — and split when it wants to grow

**The smallest change that actually closes the defect.** If the audit's suggested fix is wrong or
would cause harm, do the right thing and record it.

**The split rule.** When the correct fix needs structure — a component reshaped, a duplicate
implementation collapsed, a mechanism removed — do **both** halves deliberately: extract the
**narrow safe kernel** that closes the live defect now (often a deletion or a one-site change), and
**defer the structural remainder** with the repro transplanted. Never leave a live defect open
wholesale because its full fix is structural, and never smuggle the restructure into the fix — a
one-finding commit that rewrites a component is an unreviewed refactor with a bug report stapled to
it, and two such rewrites once produced more than half a round's residue between them.

**A fix that adds a guard, refusal, withhold, status or special case is a design change, not a bug
fix.** Before writing it, name the product-model sentence it serves; if you cannot, the finding has
misdiagnosed the defect — record that instead of implementing it. Prefer the fix that deletes
machinery, and check `docs/PRODUCT-MODEL.md` §3's deleted-machinery list before building anything
that resembles it.

### 4. Sweep for the same defect elsewhere

The finding names one location. Before committing: **every arm of the conditional you touched; the
sibling surface** (the manual path and the Azure path are siblings — a rule wrong in
`SubnetController.Create` is usually wrong in the bulk import commit, and vice versa); **every
other caller**, and every place the same question is asked without the helper; **the inverse path**
(fixed a write? check the read that displays it); **the other fixes in this round.** Search for the
*concept* as well as the identifier — the prefix string, the enum member, the message text — because
the sibling often does not call the same method. Fix every site found; record what you searched and
what it returned.

### 5. Check the strings your change made true or false

Any message whose truth depends on what you changed must be re-read. Operator-facing text your fix
adds must name an action that is actually reachable — drive it. A success message must not outlive
the action it announces. Stale operator-facing text is one of the most reliable residue sources.

### 6. Sweep for orphans the compiler will not report

Deleting a method strands `using` directives, private constants, locals and parameters, and C#
warns about none of them. After every deletion, check what it made dead — then check anything you
are about to remove as "also dead" is not live elsewhere.

### 7. Verify

```
dotnet build --no-incremental     # 0 warnings
dotnet test                       # full suite
```

Then, because the suite is not enough — **it has been green for every residue finding ever filed**:
re-run this finding's own reproduction against the fix, and re-run the reproductions of every fix
already made this round.

**Prove every new test discriminates, by breaking the code it guards.** Revert the specific line
the test exists for, confirm the test fails, restore. If it still passes, the test is decoration.
**A pin binds the row's defect, not the tokens of the diff.** Prove it the way the audit's
verifiers will (PRODUCT-MODEL §5): restore the defect — the full revert, and the one-edit
regressions a maintainer could make (an inverted condition, a dropped stamp, a gated statement, a
sentence moved to the other branch) — confirm the suite, or the recorded `/e2e` drive where no
unit seam exists, goes red, and confirm the defect is visibly back in the running build. Round 29
found five round-28 pins that bound file names, incidental tokens or a sentence's presence and
accepted every one of those regressions with the suite green.
Three separate times in one round a test looked right and proved nothing:

- an invariant over free-space ranges whose generated fixtures all began at the parent's network
  address, so the head-gap branch it was written for was never exercised;
- concurrency tests seeding a `[Timestamp]` column, which EF ignores as store-generated, so SQLite
  stored NULL and the guard never ran;
- two application instances timed to prove they no longer contend, which came up 13.2s and 13.3s
  **whether or not they did** — it took an external lock-holder measuring the block to distinguish
  the outcomes at all.

**If a test cannot fail, it is not testing.** Where the harness genuinely cannot reach a path, say
so in the FIXED entry and put the coverage in `/e2e`.

### 8. Independent review — every fix, no exceptions

Spawn **one fresh reviewer subagent** (the `Agent` tool is legitimate here — the audit skill's ban
on it is scoped to audit rounds; one reviewer at a time is not sixty workers). The reviewer gets:
the finding text, the diff, `docs/PRODUCT-MODEL.md`, the reproduction script, and the live rig —
and is prompted to **refute the fix**: re-run the repro, probe what the diff breaks, check the
sibling surfaces, check the model. A reviewer without the rig degenerates into code-reading, which
is near worthless for client-JS — hand over ports, catalog names and fixture ids.

The verdict is typed, and the protocol is decidable:

- **(a) demonstrated failure** — the reviewer ran something and it went wrong. The fix loses.
  Revise once, re-review; a second demonstrated failure → **revert the fix and defer the finding**.
- **(b) product-model violation, sentence cited** — decided by the text. If the text is genuinely
  ambiguous, neither side wins: revert, defer, and record the one-line product question in the
  findings file for the owner.
- **(c) "I would have fixed it differently"** — the author wins automatically. Demands for
  hardening or for coverage beyond the §5 test rule are this category; **a fix shipping neither
  its pin nor its ledger + `/e2e` record is (b), §5 cited.** The reviewer's schema must force
  verdicts into (a)/(b)/(c) so preference cannot masquerade as failure.

One rebuttal each, no third round — if you believe a correction is wrong, **reproduce why before
departing from it**: a past round shipped a defect by "improving" a reviewer's `ModelState.Remove`
placement into refreshing the concurrency token on every failure path, silently defeating
optimistic concurrency. Reverting is a first-class, non-shameful outcome; fix-of-fix-of-fix is the
exact divergence mechanism this skill exists to stop.

### 9. Mark it terminal

Append ` — FIXED` (or REFUTED / DEFERRED / STRUCK) to the heading and replace the body with at most
five lines:

```
_Fixed in <sha>. <What changed, one sentence.>_
_Swept: <what was searched, what else was fixed>._
_Verified: <what was run, what came back>._
_Reviewed: <verdict, and any correction taken>._
_Not done: <anything deliberately left, and why> — omit if nothing._
```

**No essays.** The file is a work queue, not a report.

### 10. Commit

**One commit, one line, no body** — the code change and the updated findings file together, ids in
the subject when a commit closes several findings. Verify the commit contains only this fix's
files. In default mode commit directly (approval already happened at triage); hand the message
over in a code block. **Never push.**

## The round-end gate — mandatory, before teardown

Per-finding checks are not enough: several classes of fix compile cleanly and fail only when a page
is requested, and fixes that were each correct alone have failed to compose. In order:

1. **Clean rebuild** — delete `bin`/`obj`, `dotnet build --no-incremental`, 0 warnings.
2. **Full suite**, reconciled against the baseline. The test count must never regress without a
   recorded reason.
3. **Re-drive every fix in this round against the final tree** — each was verified against the tree
   as it stood when written, not the one later fixes produced.
4. **Run the real app** against real SQL Server and request every major area — subnet list, create,
   details, edit, delete, deleted-subnets, purge, host IPs, all-deleted-host-IPs, error pages, both
   Azure wizards. **Assert rendered content, not HTTP 200.** Confirm security headers ride on a
   normal response.
5. **Read the log.** Classify every `fail:` / `warn:` — some are expected (a deliberate
   permission-denied probe logs an error by design); state the difference. This step once caught a
   fix that renamed a lock resource on acquire but not release: nothing failed, no test covered it,
   and the only symptom was one `fail:` line in a startup log.
6. **With Azure credentials**, drive both surfaces end to end: subscriptions → discovery → bulk
   preview and commit → reconcile scan → delete commit. Include **both** counter-tests:
   - a resource the credential *cannot see* must be **withheld**, with a warning naming it;
   - a genuinely deleted resource must **still be offered and deletable**.
   Checking only the first lets an over-blocking regression pass — the exact rounds-14–17 failure.
7. **The whole-diff review.** Two fresh reviewer subagents audit the round's entire diff
   (`git diff <baseline>..HEAD`), one on correctness-and-regressions, one against the product
   model. Refutation-default: a review finding exists only if it demonstrates a concrete failure or
   cites the violated model sentence — "I'd have done it differently" is not a finding; a fix
   this round left both unpinned and unrecorded is one, citing §5. This is a
   **gate, not a queue**: at most **one** repair iteration (each repair through steps 1–10,
   WIP cap still enforced), then anything still standing is resolved by **reverting the offending
   fix commit and deferring its finding**. No second iteration, ever — the gate must terminate.
8. **`git status` clean**, no scaffolding in any commit.

**Only after the gate passes:** tear down rigs and containers, and remind the user to revoke the
service principal secrets. The gate needs the rig; a round that tears down first cannot run it.

## Close-out

1. **Append to `docs/AUDIT-LEDGER.md`:** one Findings row per finding — id
   (`<round>-<finding>`), severity, terminal verdict (fixed / refuted / struck / inverted /
   deferred), one line of what — and the round's row in the Rounds table with the residue rate.
   **No shas in new rows**: they are written before the squash merge exists, so any sha dies with
   the branch; the round number is the durable key, and the merge commit is always findable via
   `git log main --grep "Audit <N>"`. Instead, **backfill the previous round**: its merge is on
   main beneath you now — put its merge sha and PR number into that round's Rounds-table cell.
   Facts only; the ledger must not carry an argument.
2. **Append owner rulings from triage to `docs/PRODUCT-MODEL.md` §8, verbatim**, each with its
   counter-test pointer.
3. **Delete the findings file** — `git rm docs/AUDIT-FINDINGS-*.md`, committed. The files poison
   the next round: they hand twenty finders inherited beliefs, and one wrong sentence in one once
   cost four rounds. Everything durable is now actually durable — verdicts in the ledger, rulings
   in the product model, deferred work in `docs/DEFERRED-FINDINGS.md` — so nothing else survives on
   purpose. (Commit history is *not* the durable record: main squash-merges.) Do not delete a file
   that still has non-terminal findings.
4. **Report the residue rate** from the ledger, and say plainly whether it is falling. If it is not,
   the headline is that this skill needs changing again — not that the codebase is unusually buggy.
5. Report the clean-up owed: revoke credentials, remove containers, delete cloud test resources.

## Release notes — the last thing produced

Present them **in chat as one fenced markdown block** the user can paste into a GitHub release. Do
not write them to a file and do not commit them. Derive every line from the commits actually made
(`git log <base>..HEAD`), never from the findings file; refuted and deferred work must not appear.

Three sections, each **omitted entirely when empty**: `### New Features`, `### Improvements`,
`### Bug Fixes`. Write for someone **running** Bastet — one bullet per user-visible change:
`- **What changed** — what it means, or what was wrong`. Configuration keys, headers, routes and
identifiers in backticks. **Bug Fixes** lead with the **symptom someone could have hit**, never the
code that changed. **Group aggressively** — twenty dead-code deletions get one bullet; a finding
with no user-visible change gets none. No numbering, no finding ids, no mention of the audit
process.

## Standing constraints

- The product-model constraints (`docs/PRODUCT-MODEL.md` §6) bind every fix: open-source/plain-HTTP
  deployments keep working, **no comments in `.cs`/`.cshtml`**, no literal control characters,
  `.Designer.cs` files are frozen.
- **Scope is the defect, not the line number.** An unrequested refactor is out of scope. The same
  defect at another site never is.

## A sudden RZ1021 storm means the build server, not your markup

If `dotnet build` starts reporting `RZ1021: Markup in a code block must start with a tag ... Do not
use unclosed tags like "<br>"` across `.cshtml` files **you did not touch**, and the cited lines
hold ordinary valid Razor (`<partial ... />` inside `@if {}`, `<text>` inside `@foreach`), the
Razor source generator in the long-running Roslyn build server has gone bad. The markup is fine.

```bash
dotnet build-server shutdown
```

Then rebuild. **Do not "fix" the views to satisfy it.** Confirm the diagnosis in seconds by
building a throwaway `dotnet new mvc` with the same construct: if the fresh template fails too, it
is the toolchain, not the repository.

## Rigs — ephemeral, never in the repo

Anything needing scaffolding runs against a `cp -r` copy in the scratchpad, never the real tree. A
permanent test ships with a fix **only** when it can be written against existing infrastructure:
xUnit, `TestDbContextFactory`, `MockAzureService`, `ControllerTestHelper`. Otherwise record the
measurement as prose. Verify `git status` is clean of scaffolding before every commit.

| Rig | For | Setup |
|---|---|---|
| **Framework source** | Anything resting on framework internals | Fetch from `dotnet/aspnetcore` or `dotnet/runtime` at the matching tag. Free — try it first |
| **Browser** | Wizard and client-JS findings | Playwright chromium; if absent, `pip install playwright` then `playwright install chromium` (unattended, no sudo). Drive the **running app** and intercept the POST |
| **SQL Server** | Locking, migrations, `sp_getapplock` | `docker run -d -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=<strong> -p 11433:1433 mcr.microsoft.com/mssql/server:2022-latest`. The suite runs SQLite, so this is the only way to execute the real locking path |
| **Coverage** | Dead-code findings | `dotnet-coverage collect -f cobertura -o out.xml "dotnet test"` |
| **Live Azure** | Reconciler and import findings | Two service principals with **disjoint** resource-group scope. With one, a subscription-scoped list returns everything and the experiment is vacuous while appearing to work. Verify assignments are at resource-group scope before measuring |

Azure secrets go in a scratchpad env file, referenced as variables — never on a command line, never
in the repo. `DefaultAzureCredential` picks up `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` /
`AZURE_TENANT_ID`, so the production path runs unmodified; ensure `AZURE_TOKEN_CREDENTIALS` is
**unset** — the launch profiles set it to `dev`, which excludes `EnvironmentCredential`. Ask for
anything missing once, up front, with the triage message. Remind the user to revoke at the end.
