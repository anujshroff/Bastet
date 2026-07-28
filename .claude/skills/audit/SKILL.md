---
name: audit
description: Run a fresh multi-agent security and correctness audit of the Bastet codebase, producing a numbered findings file in docs/. Use when asked to "run an audit", "start a new audit round", "audit the codebase", or "find bugs across the whole app". For reviewing a single PR or working diff use the built-in /code-review instead; to fix findings from an audit that already exists use /audit-reconcile.
---

# Run an audit round

## STOP. Read this before you touch anything.

**The round runs as a single `Workflow` call. You write the script, launch it, and shut up.**

This is not a preference about tidiness. Spawning workers with the `Agent` tool puts every one of
their tool calls into the user's conversation, and a round is thirty-odd workers deep. The user does
not want to see any of it. A `Workflow` executes its agents outside the conversation — progress is in
`/workflows` if they choose to look, and a single notification arrives when the round lands.

**Never run an audit round with the `Agent` tool.** One `Workflow` call, or nothing.

**Rounds 1 through 5 ran as workflows. Rounds 6 and 7 did not, and that is why this section exists.**
The skill described what an audit round contains but never said what executes it, so both rounds
drifted into foreground `Agent` calls with the baseline, the git archaeology, the containers and the
cloud fixtures scrolling through the user's terminal. Nothing about the round changed — only who ran
it, and it was never written down. It is written down now.

### Your entire tool budget

| Tool | For |
|---|---|
| `Workflow` | The round. One call. This is how everything happens. |
| `Write` / `Edit` | The workflow script only, into the scratchpad — never a repo file. |
| `TodoWrite` | The phase list the user sees. |
| `AskUserQuestion` | Phase 0 only — the one cost question. |
| `Read` | **Only** files the workflow wrote into the scratchpad for you. Never a repo file. |
| `TaskStop` | Killing the run. |

**Nothing else. Ever.** No `Bash`. No `Grep`, no `Glob`. No `git`. No `dotnet`. No `docker`. No
`curl`. No `Agent`. No opening a controller "just to understand the finding".

If you are about to reach for a tool that is not in that table, the answer is always the same: **it
belongs inside the workflow.** No exception, no matter how trivial the command, how fast it would be,
or how sure you are of the answer.

### Things previous rounds did in the foreground. Every one goes in the script now.

Running the build. Running the tests. `git log`, `git status`, `git rev-parse`. Reading the previous
findings file. Reading any source file. Standing up the database container. Starting the application.
Creating cloud fixtures. Pulling the browser image. Probing credentials. Merging the two passes.
Verifying a finding. Writing the findings file. Re-checking citations. Tearing the rig down. Making
the commit. **All of it. In the script.**

### What you say to the user

The launch, and then nothing until it lands:

```
Round 7 away — one workflow, ~45 agents, six phases. Nothing further until it lands.
```

No tool transcripts. No narrating what a worker is doing. No progress guesses — you genuinely do not
know, and the workflow will tell you. At the end: the verdict in a few lines and the path to the file.

When the user asks a question mid-run, say the round is still running and offer `/workflows`. Do not
go and look yourself, and do not invent progress.

### Writing the script

Give it a `meta` block whose `phases` match the `phase()` calls below. Prefer `pipeline()` so a
candidate can be verified while another beat is still finding; use `parallel()` only where a stage
genuinely needs every prior result at once — the merge does, the beats do not.

Use `schema` on every agent whose output the script branches on: the rig's baseline verdict, each
beat's finding list, the merge's candidate list, each verifier's survive/refute call. Prose that the
script then has to parse is how these runs go wrong.

**The baseline is a hard gate.** If the rig agent reports a dirty tree, a failing test or a build
warning, `return` immediately with the reason. Do not audit a broken tree.

---

## Phase 0 — the one question you ask

The only part that happens outside the workflow. Say what the round will cost, once. Round 4 ran 88
agents; round 6 ran 32. Offer a scale choice, and — where the round would use a live cloud rig — a rig
choice. The answers become the script's `args`. Then stop asking, write the script, and launch it.

If the user has already answered these earlier in the conversation, do not ask again.

**If the round includes the live Azure rig, have the credentials in hand before you launch.** The
user supplies them; your job is to be certain you have them rather than to discover forty minutes in
that you do not. Needed: two service principals — client id and secret each, with disjoint RBAC scope
over the two resource groups — the tenant id, the subscription id, and both resource group ids. If
any part is missing, **ask for it now.** Besides the cost question this is the one thing worth
blocking on: five of the eight beats have Azure surface, and a credential cannot be inferred.

A credential from a previous round is not evidence of a working one — they are rotated after a round,
and round 6's was revoked. The rig agent proves it below rather than assuming it.

Credentials go into a scratchpad file the script points agents at. **Never pasted into sixteen
prompts, never into the repository, never into a commit.**

## Phase 1 — briefing and baseline (`parallel`, 2 agents)

Everything later phases know comes from these two. Both write files into the scratchpad, and every
later agent in the script is handed those paths rather than the contents — a brief pasted into
sixteen prompts is sixteen copies to keep in step.

**Briefing agent** → `BRIEF.md`. Reads `docs/AUDIT-FINDINGS-*.md` highest-number-first and every commit
since that round's HEAD. Extracts:

- the **round letter** — rounds 3–6 used C, D, E, F; the next round takes the next letter, and its
  file number is the previous file's plus one;
- the **refuted table**, verbatim with reasons — nothing in it may be re-raised;
- the **struck entries** (italic paragraphs starting `_D12 is fixed and committed…`) — already fixed,
  and each explains what was deliberately *not* done and why. Re-raising one is the most annoying
  kind of noise;
- the **watch list** of accepted risks, carried forward unless something changed;
- the **clean bill**, so this round's workers go somewhere new;
- the sections "What every finding must carry", "Output structure" and "Constraints on what counts as
  a finding" from this file, copied in — finders never see the skill;
- a map of the codebase good enough to orient a worker who has never seen it.

Accepted and still open, never re-raised as new: ForwardedHeaders trust-all with `AllowedHosts: "*"`,
the Development-only `DevAuthHandler` bypass, `GlobalSanitizationFilter` skipping nested `System.*`
collections, `CollectDescendants` lacking a cycle guard, the unreachable IP-change branch in
`ValidateHostIpUpdate`, the blind `catch {}` around the DataProtectionKeys probe, and **C20** (the
Azure reconcile check/act window).

**Rig agent** → `RIG.md`. Runs the preflight, then establishes the baseline.

### Preflight — three checks, before anything else, because each has already cost a round

**Memory.** A round runs sixteen agents concurrently, each with its own build, container or browser.
**Under 16 GB of RAM the host dies mid-run** — round 7 died this way on a VM whose allocation had not
been raised, partway through the beats. Check total RAM first and **stop the round** if it is under
16 GB, naming the figure. It costs a second, and it fails in seconds rather than forty minutes in.

**Git identity.** `git commit` refuses to run without one, so this is a hard failure rather than a
warning. Round 7 found `user.name` and `user.email` unset in every scope with no `~/.gitconfig`; the
round would have completed every phase and then failed on its last action, and `/audit-reconcile`
commits once per finding and would stop dead on the first.

```
git var GIT_AUTHOR_IDENT           # "Author identity unknown" means stop
```

If it is unset, **report it and stop rather than setting it** — whose name goes on a commit is the
user's call, not an agent's. Tell them what the repository's existing commits use so the answer is
one word. A checkpoint restore takes `user.name`, `user.email` and `~/.gnupg` together, so expect the
signing key to be gone in the same breath. Unsigned commits are acceptable; an anonymous one is not.

**Azure credentials**, where the round uses the live rig. Prove them before the beats start, or five
of the eight fail slowly and separately instead of once and clearly. For each service principal:
fetch a token, then probe **both** resource groups.

The discrimination is the point, not the authentication. A credential that sees everything proves
nothing about the reconciler's withhold path, and a rotated one presents as a rig that authenticates
and then returns nothing — indistinguishable from a healthy subscription with no VNets. Expect SP_A
to get 200 on its own group and **403** on the other, and SP_B the exact reverse. If that matrix does
not reproduce, **stop and name the leg that failed**: an expired secret, a missing role assignment and
a wrong subscription id all present differently, and the user can fix any of them in a minute.

### The baseline, because a moving baseline invalidates everything downstream

```
dotnet build --no-incremental      # expect 0 warnings
dotnet test                        # record the count
git rev-parse --short HEAD ; git branch --show-current ; git status --porcelain
```

`--no-incremental` matters: an incremental build does not re-run the analyzers and reports 0 warnings
even when there are some. It returns that verdict under a `schema`, and **the script gates on it: a
dirty tree, a failing test or a build warning ends the run with the reason.** Do not audit a tree
that is already broken.

Where the round includes a live rig, this agent also stands up the database, the application, the
browser image and any cloud fixtures, and writes the etiquette that stops workers destroying each
other's environment — own port, own catalog, kill only by PID. Round 6 lost two agents' applications
to a sibling's `pkill -f "Bastet.dll"`.

This phase is the one place a barrier is unarguable: nothing can start until the baseline is known
good and the brief exists.

## Phase 2 — the beats, twice (16 agents + 1 merge)

Eight beats, each an independent agent given the brief path and nothing else:

1. **Security / web** — authorization coverage, antiforgery on state-changing actions, XSS, injection,
   SSRF, response headers, log forging, secrets handling.
2. **Logic & data integrity** — the subnet/CIDR arithmetic, containment and overlap rules, host-IP
   validation, anything that can persist a state the validated path would reject.
3. **Azure integration** — the import wizards, the bulk planner, the reconciler. Highest stakes: this
   is the only code that *deletes* on the strength of what an external system reports.
4. **Locking & lifecycle** — `sp_getapplock` usage, the migration lock, transaction boundaries,
   check-then-act windows, EF connection pooling.
5. **UI & client-JS** — the three wizards' state machines and emitted form payloads. Read carefully:
   what gets POSTed is decided by `disabled` attributes, and jQuery's `.prop()` fires no `change`.
6. **Regression correctness** — every commit since the last audit, diffed line by line against what it
   replaced.
7. **Regression tests** — do the tests added alongside those commits actually assert the new
   behaviour, or do they pass against the unfixed code?
8. **Dead code & refactor residue** — orphans left by earlier deletions. This beat exists because a
   round-3 deletion left a helper behind, and the round-4 version of it found nineteen more.

**Every worker prompt carries this, and "do not modify the working tree" is not enough on its own.**
Round 7's beats read that as "do not edit source" — a fair reading — and parked PID files in the
repository root: `g7lk-5487.pid`, `p1r-app-5432.pid`, `sec2-5481.pid`, `x7ui-app.pid`, with round 6's
`app-5407.pid` and `w2-master.txt` already sitting there from the same habit. Say the stronger thing:
**write nothing into the repository directory at all** — no PID files, no logs, no scratch output, no
notes — and put every created file under the rig directory. An untracked file makes the tree dirty,
and Phase 5 is instructed to refuse the commit when anything but the findings file is dirty, so one
stray `.pid` costs the round its output.

**Run all eight twice**, as two passes of fresh workers with no knowledge of each other. Round 4 did
this by accident and it produced the most useful signal in the file.

Tag every finding **`[×2]`** (both passes found it independently) or **`[×1]`** (one pass only). The
corollary is the important half: **absence is weak evidence.** A `[×1]` is not weaker in truth, but it
means one full pass missed it — so it deserves *more* scrutiny during reconciliation, not less.

A **merge agent** de-duplicates the two passes into the candidate list and applies the tags. This is
the round's one genuine barrier after Phase 1 — it needs every beat's output at once to tell a
`[×2]` from a `[×1]`.

## Phase 3 — adversarial verification (1 agent per candidate)

**Every candidate goes to an independent verifier**, prompted to **refute** it and to default to "not
real" when uncertain. Only survivors reach the file.

The verifier's job is to kill findings, not confirm them. Round 4 refuted five this way, all of which
reduced to preference wearing a severity label: an unused optional parameter, three constants that
"could" drift while currently agreeing, an interface member with no external caller. **If the finding's
own failure scenario opens with words like "not a runtime defect", it belongs in the refuted table.**

A verifier may change the answer rather than the confidence — kill a proposed *fix* while keeping the
finding, correct a severity, correct where the defect came from. Round 6's verification killed four
proposed fixes by measuring them. Those corrections belong in the file.

Refuted findings go in a table at the end with the reason they were killed, so the next round does not
spend agents rediscovering them.

Reference: round 4's passes ran 31 survived / 1 refuted and 37 survived / 4 refuted; round 6 merged to
25 candidates, of which 18 survived.

## Phase 4 — the scribe (2 agents, sequential)

**An agent writes `docs/AUDIT-FINDINGS-<N>.md`.** Give it the brief path, the rig report's baseline
block, the survivors, the refuted list, the verifiers' corrections and the clean-bill material from
every beat.

**A second agent then re-checks every file-and-line citation** against the working tree, and fixes
what is wrong rather than only reporting it. Round 4 re-checked all of D1–D10 plus a sample of the
rest and found no invented line numbers; keep that record clean. Note that a findings file is
correct only against the HEAD it was written at — round 6's citations were all pre-fix, and the
cleanup commit moved twenty-two source files under them.

## Phase 5 — teardown and commit (1 agent)

One agent tears the rig down — containers, processes, any cloud fixtures the round created — then
**sweeps the scratch files the beats left in the repository**: untracked `*.pid` and similar strays
in the root, including any left by an earlier round, each guarded with `git ls-files --error-unmatch`
so nothing tracked is ever removed and nothing under `src/`, `test/` or `docs/` is touched. Then it
confirms the tree carries nothing but the findings file (no scaffolding, no credential, no stray
edit) and **makes the commit**. It is a versioned record: `/audit-reconcile` then commits an updated
copy alongside each fix, so the reasoning and the change land together.

This machine has read-only access to the remote and never pushes — publishing is handled elsewhere.
Nobody attempts it.

The script's return value is what you report: the counts, the verdict, and the path. Read it, say it
in a few lines, and stop. That is your last act.

---

## What every finding must carry

Put this in every finder's and every verifier's prompt.

- **File and line citation**, re-checked against the working tree.
- **Confidence: confirmed or plausible.** *Plausible* means a load-bearing step could not be
  established — say which step. Not for hedging a finding you simply did not check.
- **A concrete failure scenario** with real inputs and the wrong output. "This could be a problem" is
  not a finding.
- **A proposed fix** — and where a cheaper interim exists, both.

Grouped by severity (critical / high / medium / low / info), numbered sequentially across the whole
file, ordered within severity by consequence.

## Output structure

```
# Bastet — Round-<N> Audit Findings
target branch / HEAD / test baseline / date

## Verdict            — the shape of what was found, and what to read first
## How this audit ran — beats, verification, what [×2]/[×1] mean

# Critical / High / Medium / Low / Info   — findings, numbered sequentially

# Refuted — reported by a finder, killed by the verifier   (table, with reasons)
# Watch list — not findings, but worth knowing
# Clean bill — swept and produced nothing
```

The **clean bill** matters as much as the findings: it tells the next round what has already been
looked at, so its workers go somewhere new.

## Constraints on what counts as a finding

- **"This is an open source tool that anyone can host in any way."** Plain-HTTP and air-gapped
  deployments must keep working. "Assumes HTTPS" is not a finding, and a fix that breaks those hosting
  models is a bad fix.
- **No literal control characters** in any example or fix — use a named constant such as
  `(char)0x1B`. Literals are invisible in diffs and get mangled through tool round-trips.
- **Migration `.Designer.cs` snapshots are frozen history.** They contain old column widths on
  purpose. Never report them as stale.
- **No novels.** A finding is a citation, a scenario, a fix.
