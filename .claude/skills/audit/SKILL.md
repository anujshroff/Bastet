---
name: audit
description: Run a fresh multi-agent security and correctness audit of the Bastet codebase, producing a numbered findings file in docs/. Use when asked to "run an audit", "start a new audit round", "audit the codebase", or "find bugs across the whole app". For reviewing a single PR or working diff use the built-in /code-review instead; to fix findings from an audit that already exists use /audit-reconcile.
---

# Run an audit round

A round is **one `Workflow` call** that you launch and then actively operate. It always runs the same
shape, at the same scale, against the same rig, and commits the same way. There is nothing to decide
and nothing to ask.

## This machine is disposable

The VM is reverted daily to a state before you existed. Nothing in `~/.claude` survives — not memory
files, not config, not credentials, not a signing key. **Only committed files survive**, carried off
by the audit-findings commit and the reconciliation commits, which the host replays and re-signs.

Anything that must hold across runs belongs in this file. Do not write preferences to memory and
expect them back. Do not assume a credential or a git identity from a previous round exists.

## Ask once, for inputs only

The round asks the user for **the inputs it cannot infer, once, in a single message, before
launching.** Check what is missing first, then ask for everything missing in one go — never trickle
questions out one at a time, and never ask again later.

Always needed:

- **the scale of the round** — offer three, with the agent count for each, and take the answer as
  given. Standard is the default and is what the numbers below describe.

  | | finders | verifiers | total | what you lose |
  |---|---|---|---|---|
  | Light | 8 (one pass) | ~10 | ~22 | no `[x2]`/`[x1]` signal at all — the most useful thing the round produces |
  | Standard | 20 (2 passes + deep sweep) | ~36 | ~62 | nothing |
  | Deep | 28 (2 passes + deep sweep on all 8) | ~48 | ~80 | nothing; more coverage of the tail, ~30% longer |

- two service principals — client id and secret each, with disjoint RBAC scope over the two resource groups
- the tenant id
- the subscription id
- both resource group ids

Conditionally needed — **check before asking**:

```
git var GIT_AUTHOR_IDENT        # "Author identity unknown" (exit 128) means it is unset
```

If that fails, **ask for the git name and email in the same message as the credentials.** Do not
guess them, do not scrape them out of `git log`, and do not discover the problem forty minutes later
at the commit step — round 7 would have completed every phase and then died on its last action.
Whose name goes on a commit is the user's call; asking costs one line in a message you are already
sending. If the identity resolves, say nothing and do not ask.

Credentials are **never stored**. They are pasted by the user, written once to a scratchpad file the
script points agents at, and die with the machine. Never into the repository, never into a config
file, never into a prompt repeated across sixteen agents, never into a commit. A credential from a
previous round is not evidence of a working one — they rotate, and round 6's was revoked.

**Ask nothing else. Ever.** Not scale, not rig, not verification depth, not "shall I proceed", not
"should I continue". Everything else is fixed below or discovered by the script. If a required input
is missing at run time, the rig agent stops the round naming it — that is the only way this skill
returns without a findings file.

## Fixed configuration

| | |
|---|---|
| Scale | Asked every round: Light / **Standard** / Deep. Everything below describes Standard |
| Finders | 8 beats x 2 independent passes + deep sweep on beats 1, 3, 6, 7 = **20** |
| Verification | 1 adversarial verifier per candidate; a 2nd for every `[x1]`; a 3rd only to break a tie |
| Rig | Always live: database, application, browser, Azure fixtures in both resource groups |
| Credentials | Asked for once, up front; written to scratchpad only; never stored, never committed |
| Branch | `audit/round-<N>`, created in Phase 1 **before any work runs**. **`main` is never touched** |
| Output | `docs/AUDIT-FINDINGS-<N>.md`, committed, never pushed |
| Total | ~62 agents, ~45 min |

Do not offer a smaller or larger round. If the user wants a different scale they will say so
unprompted, and only then does it change.

---

# The master rule: do not add problems

**The round exists to reduce the number of real defects in the product, permanently. Nothing else in
this file overrides that.** A finding that is not real, a fix proposal that would break a working
path, or a severity graded on anything but consequence all make the next round more expensive rather
than less.

Two things follow, and they are what the beats and the verifiers are actually for:

- **Report the defect, not the instance.** A finding that names one call site when the same rule is
  wrong at three others hands the reconcile step a fix that cannot close it. Where a beat can
  establish that a rule is duplicated, the finding says so and names every site.
- **A proposed fix that would introduce a new defect is worse than no proposal.** Round 16 had eight
  of fifteen judged unsound, three of which would have shipped a new defect. That check is the single
  most valuable thing verification produces — keep it.

**Measure the residue rate and lead with it** (see Phase 4). Round 16's was 11 of 15. If it is not
falling, the fix process is the problem, not the codebase, and the round says so in the Verdict.

# No comments in source

**Bastet source carries no comments.** `.cs` and `.cshtml` files have none, by the owner's standing
instruction. Two consequences for a finder:

- **Never file "this needs a comment", a missing-doc observation, or a stale-comment finding.** Those
  are not runtime defects and are refused on sight.
- **Never propose a fix whose substance is a comment.** If a rule needs explaining, the fix is a named
  method or a test that fails when the rule is broken.

Round 16's P11 is the cautionary case in the other direction: a comment asserted "the parent was
always renamed on the path that reaches it", the guard beside it was written to match the comment
rather than the code, and the wrong one was believed. The test that now pins it cannot be believed
wrongly.

# You are the operator, not a spectator

Launch the workflow, then **watch it and intervene**. Round 7's skill said to launch and say nothing,
answer status questions by pointing at `/workflows`, and never look at the run. All three were wrong:
`/workflows` does not exist in the VSCode extension, the merge agent stalled dead twice, and the round
only finished because those rules were broken. Observation is your job.

## Your tool budget

| Tool | For |
|---|---|
| `Workflow` | The round. Launch, and resume after an intervention. |
| `Bash` | **Read-only** run inspection and git state. Never build, test, or touch the app. |
| `Write` / `Edit` | The workflow script, in scratchpad. **Never a repo file while a round is running** — an untracked file dirties the tree and Phase 5 refuses the commit. |
| `Read` | Scratchpad files and the run journal. |
| `TaskStop` | Killing a stalled run before resuming it. |
| `TodoWrite` | The phase list. |

**Never `Agent`.** Spawning workers directly puts every one of their tool calls in the user's
conversation, sixty workers deep. One `Workflow` call, or nothing.

Everything that *does* audit work — build, tests, git archaeology, containers, cloud fixtures,
reading source, verifying, writing, committing — happens **inside the script**, never in the
foreground.

## Polling

Use **`python3`** — it is present on Debian, and `grep` against JSON gives wrong answers the moment a
finding's text contains the string you are matching on. (Node is neither present nor needed; see the
preflight.)

```python
import json, os, time, glob, sys
D = sys.argv[1]                                    # transcriptDir, returned by the Workflow tool
started, results = set(), {}
for line in open(os.path.join(D, "journal.jsonl")):
    r = json.loads(line)
    (started.add if r["type"] == "started" else lambda a: None)(r["agentId"])
    if r["type"] == "result":
        results[r["agentId"]] = r.get("result")
now = time.time()
def age(a):
    p = os.path.join(D, f"agent-{a}.jsonl")
    return int(now - os.path.getmtime(p)) if os.path.exists(p) else -1
inflight = [(a, age(a)) for a in started - set(results)]
print("started", len(started), "results", len(results))
for a, s in sorted(inflight, key=lambda x: x[1]):
    print(f"  inflight {a} {s}s{'   <-- STALLED' if s > 480 else ''}")
```

Read the verdict fields out of `results` the same way — `survives`, `reproduced`, `tag`, `memberIds` —
rather than grepping for them.

A `result` line means an agent **returned**; that work is banked and survives any later failure.
Started-minus-results is in flight. Agents killed in an earlier attempt never get a result line, so
they linger in that arithmetic — check each in-flight id's transcript age before believing the count.

## Stall detection and escalation — fixed thresholds, no judgement

| condition | action |
|---|---|
| active transcript static **< 8 min** | normal; a single long generation looks like this |
| static **≥ 8 min** | stalled. `TaskStop` the run, relaunch with `resumeFromRunId` |
| **2nd stall at the same step** | structural. Stop, fix the script, resume — do not retry a third time |

Resume replays every completed agent from cache and re-runs only what did not finish, so an
intervention costs one agent, not sixty. **Editing the script is free for any step that has not
completed** — completed agents sit earlier in the prefix and still replay.

**Resume is same-session only.** If the session ends, every banked result is lost and the whole round
re-runs from scratch. That is the reason to intervene rather than wait something out.

## Reporting

**Status questions get a markdown table and nothing else.** Same rows every time so successive checks
diff by eye. No narrative, no interpretation, no reassurance, no speculation about what comes next, no
remarking that a number is good or notable. If something is broken, state it in one short sentence —
that is data. Analysis only when asked for.

| row | content |
|---|---|
| time | wall clock |
| started / results | journal counts |
| in flight | started minus results, discounting known-dead agents |
| active | newest agent id + seconds since last write |
| candidates | merge output with the x2/x1 split |
| judged / survived / refuted | verifier verdicts |
| reproduced / failed / not-runnable | reproduction outcomes |
| findings file | bytes, or none |
| tree | dirty entry count |
| HEAD | short sha |

At launch: one line. At completion: the funnel, the severities, the headline finding, **the residue
rate** (`<R>` of `<F>` findings were residue of round `<N-1>`'s fixes), the commit sha, anything the
teardown failed to clean, and — **always** — a reminder to revoke the service principal secrets. This
skill asked for them; this skill reminds you to kill them.

**If the residue rate is high, say so as the headline, not as a footnote.** A round where most
findings trace back to the last round's fixes is not a report about the codebase — it is a report
about the fix process, and burying that is how sixteen rounds went by without anyone measuring it.

---

# The script

`meta.phases` must match the `phase()` calls. `pipeline()` by default. Only two genuine barriers exist:
Phase 1 (nothing starts until the baseline is known good) and the merge (telling `[x2]` from `[x1]`
needs every beat's output at once). Put a `schema` on every agent the script branches on.

**The baseline is a hard gate.** Dirty tree, failing test or build warning → `return` immediately.

## The merge must return ids, not prose

Give the merge a flat list of every finding, each with an id, carrying only what it must reason about
— title, severity, file, line, confidence, scenario, fix. Strip evidence prose. It returns **groupings
of ids**: `memberIds`, `canonicalId`, `tag`, and a drop list. The script rebuilds full candidates in
plain JavaScript from the originals.

This is not style. Two consecutive round-7 merge agents stalled dead trying to emit the whole corpus
as one structured payload — writes for three minutes, then a flat line, twice, at the same step. The
id-based version landed in under four minutes. **Never make an agent re-transcribe text the script
already has.**

---

## Phase 1 — briefing and baseline (`parallel`, 2 agents)

Both write files into the scratchpad; every later agent is handed the **paths**, never the contents.

**Briefing agent** → `BRIEF.md`. Reads `docs/AUDIT-FINDINGS-*.md` highest-number-first and every commit
since that round's HEAD.

**If no findings file exists at all**, this is round 1: letter `A`, file `docs/AUDIT-FINDINGS-1.md`,
no refuted table, no struck entries, no watch list. Say so explicitly in the brief so twenty finders
do not go looking for prior context that was never written, and brief against the full history instead
of "commits since the last round".

Otherwise the brief must contain: the **round letter** (rounds 3-7 used C, D, E, F, G; take the
next, file number = previous + 1); the **refuted table** verbatim with reasons; the **struck entries**
(italic paragraphs starting `_D12 is fixed and committed…`, each explaining what was deliberately not
done and why); the **watch list**; the sections "What every finding must carry" and "Constraints on
what counts as a finding" copied from this file, because finders never see it; and a map of the
codebase good enough to orient a worker who has never seen it.

Accepted and still open, never re-raised: ForwardedHeaders trust-all with `AllowedHosts: "*"`, the
Development-only `DevAuthHandler` bypass, `GlobalSanitizationFilter` skipping nested `System.*`
collections, `CollectDescendants` lacking a cycle guard, the unreachable IP-change branch in
`ValidateHostIpUpdate`, the blind `catch {}` around the DataProtectionKeys probe, and **C20**.

**Rig agent** → `RIG.md`. Preflight, baseline, then stands the rig up.

### Preflight — the environment, then the three checks that have each cost a round

Assume nothing is installed. The target is a fresh Debian box with VSCode, the Claude Code extension
and a fresh checkout — no .NET, no Docker, no browser, possibly no `curl`. Every one of these failures
looks like something else when it happens mid-round, which is why they are checked up front and named.

**Policy: install what installs unattended, stop and name what does not.** Anything needing `sudo`,
a group change or a re-login is the user's action, not yours — report the exact command and stop
rather than half-configuring the machine.

| check | command | if missing |
|---|---|---|
| .NET SDK | `dotnet --version`, and the major matches the project's TFM | **Stop.** Name the SDK version required |
| Docker daemon, **as this user** | `docker info` | **Stop.** `docker info` failing on group membership needs a re-login, which you cannot do |
| SQL Server image | `docker image inspect` the tag | Pull it **here**, in Phase 1 — never leave sixteen beats to pull 1.5 GB concurrently |
| Browser for beat 5 | chromium present for Playwright | Install the browser if it installs unattended; if it wants `install-deps` and root, **stop and say so** — beat 5 is near worthless without it and must not fail quietly |
| `curl` | `curl --version` | Install; every ARM probe uses it |
| **Azure CLI** | `az version` | **Install it — it is required, not optional.** It installs unattended with no `sudo`: `python3 -m venv <rig>/azcli`, then `curl -sSL https://bootstrap.pypa.io/get-pip.py \| <rig>/azcli/bin/python -` (Debian's `venv` ships without `ensurepip`, so `python -m ensurepip` fails and the bootstrap script is the way through), then `<rig>/azcli/bin/pip install azure-cli`. Takes a few minutes; do it **here**, in Phase 1, not sixteen times concurrently. If it genuinely cannot be installed, **stop and say so** — see below for what is lost |
| Disk headroom | SDK + image + browser + `bin`/`obj` across 20 agents | **Stop** if tight. A mid-round `ENOSPC` is indistinguishable from the memory death |
| CPU count | `nproc` | Concurrency is `min(16, cores-2)`. Report it — on 4 cores, twenty finders are nearly serial and the round takes far longer |
| Network egress | NuGet, MCR, `management.azure.com` | **Stop** and name the unreachable host. Three slow failures otherwise |

**Why `az` is mandatory, and what it is for.** The Azure beats rest on two service principals with
**disjoint** RBAC, and the whole experiment is vacuous if that is merely asserted — a single
assignment at *subscription* scope inherits into both resource groups, filters nothing, and still
looks like it works. `az role assignment list --all --assignee <id> --query "[].{role:roleDefinitionName,scope:scope}"`
settles it in one call, **before** anything is measured; there is no comparably cheap way to do it
with raw REST. It is also how VNet fixtures get created and torn down
(`az network vnet create|delete|list`), which every reconcile and import finding needs.

**Credentials never go on a command line.** Put them in the rig's env file and reference them as
variables (`az login --service-principal -u "$SP_A_CLIENT_ID" -p "$SP_A_CLIENT_SECRET" --tenant "$AZURE_TENANT_ID"`),
and give each principal its own `AZURE_CONFIG_DIR` so two logins do not overwrite one another. For
driving the **application** rather than ARM, export `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` /
`AZURE_TENANT_ID` and let `DefaultAzureCredential` pick them up, so the production code path runs
unmodified — and make sure `AZURE_TOKEN_CREDENTIALS` is **unset**, since the launch profiles set it
to `dev`, which excludes `EnvironmentCredential` and produces a credential failure that looks like a
permissions problem.

**No Node is required anywhere in this round.** Workflow scripts are JavaScript but the Workflow tool
runs them itself. Do not install Node, and do not try to syntax-check a script with `node --check` —
on a machine without Node that check silently passes and proves nothing. `Workflow` reports a syntax
error immediately on launch, which is the only validation needed.

**`Workflow` must be available.** It is the entire skill. If the tool is not present, **stop and say
so** — never quietly fall back to `Agent`, which is what put sixty workers' tool calls into the user's
terminal in rounds 6 and 7.

**Memory.** Sixteen concurrent agents, each with a build, container or browser. **Under 16 GB the host
dies mid-run** — round 7's first attempt died this way. Stop and name the figure.

**Git identity.** `git var GIT_AUTHOR_IDENT`. If it resolves, move on. If it does not, the name and
email were collected in the up-front ask — set them at **global (user) scope**:

```
git config --global user.name  "<supplied>"
git config --global user.email "<supplied>"
```

**Global, never `--local`.** Identity belongs to the machine, not one repository — a `--local` fix
leaves every other checkout on the box still broken, and it evaporates with the daily revert either
way. If the identity is unset *and* was not supplied, stop the round and say so; do not invent one.
Unsigned commits are fine — no signing key survives the revert, and the host re-signs on replay.

**Azure credentials.** First confirm the two resource groups **exist and are distinct** — a typo'd
resource group id returns 403 and is indistinguishable from a missing role assignment, which sends
you debugging RBAC that was never wrong. Then, for each principal: fetch a token, and probe **both**
resource groups.
The *discrimination* is the point, not the authentication — a credential that sees everything proves
nothing about the reconciler's withhold path, and a rotated one looks identical to a healthy
subscription with no VNets. Expect 200 on its own group and **403** on the other, and the reverse for
the second. Prove it for reads *and* writes, and through the application, not just `curl`. If the
matrix does not reproduce, stop and name the failing leg.

### Baseline

```
dotnet build --no-incremental      # expect 0 warnings; incremental does not re-run the analyzers
dotnet test                        # record the count
git rev-parse --short HEAD ; git branch --show-current ; git status --porcelain
```

Untracked strays from a previous round are the one dirt you may clear, each guarded with
`git ls-files --error-unmatch`.

### The branch — created here, not at commit time

The moment the baseline is green, before a single beat runs:

```
N=$(ls docs/AUDIT-FINDINGS-*.md | sed 's/.*-\([0-9]*\)\.md/\1/' | sort -n | tail -1)
git checkout -b "audit/round-$((N+1))"
```

Derive `N` yourself from the files on disk rather than waiting on the briefing agent — the two run in
parallel, and the script **asserts the two round numbers match and stops if they disagree**. Two
independent derivations of the same number is a cheap correctness check on the round's own identity.

Branching here rather than at Phase 5 means there is never a window in which the round could commit to
`main`. Round 7 branched at commit time, which is to say it did not branch, and the findings commit
landed on `main` and had to be moved afterwards. `main` must be byte-identical before and after a
round.

### The rig

**Sweep the wreckage of a dead round first.** A round killed mid-flight leaves its rig running: round
7's SQL container outlived the workflow, and its Azure fixtures outlived the whole session. Before
standing anything up, remove stale `bastet-audit-*` containers and any leftover `rig-*` fixtures in
both resource groups. Rebuilding on top of a previous round's rig means auditing against state you did
not create.

Then: database, application, browser image, and Azure fixtures in **both** resource groups so the
disjoint principals see genuinely different slices of reality. **Keep an explicit inventory of every
Azure resource created** — round 7's teardown reported success while removing none, because nothing
forced it to enumerate. The inventory is what Phase 5 deletes.

## Phase 2 — the beats, twice (20 agents + 1 merge)

**Six of these eight beats audit the WHOLE APPLICATION. Only beats 6 and 7 are scoped to the last
round's delta, and that is the only reason they exist.**

This is the mistake to avoid, and it is easy to make because beat 6 is where the highest-value findings
have historically come from: pointing every beat at what changed recently. Do not. An audit that only
re-examines the last round's diff is a regression check wearing an audit's name — it cannot find the
defect that has been in `IpUtilityService` since round 3, and after enough rounds that is where the
remaining defects are. Beats 1-5 and 8 sweep their surface across the entire codebase and treat
recently-changed code on exactly the same terms as everything else: neither weighted nor exempt.

A beat prompt that names specific recent findings as "the focus" has been written wrong. Name the
surface, not the diff.


1. **Security / web** — authorization coverage, antiforgery, XSS, injection, SSRF, headers, log forging, secrets.
2. **Logic & data integrity** — subnet/CIDR arithmetic, containment and overlap, host-IP validation, and any path that persists a state the validated path would reject.
3. **Azure integration** — import wizards, bulk planner, reconciler. Highest stakes: the only code that *deletes* on the strength of what an external system reports. Work partial visibility hard — throttling, an empty page, a 403 on one group, a token expiring mid-enumeration, a paged response whose second page fails. Which of those does it treat as "absent, therefore delete"?
4. **Locking & lifecycle** — `sp_getapplock`, the migration lock, transaction boundaries, check-then-act, EF pooling.
5. **UI & client-JS** — the three wizards' state machines and emitted payloads. What gets POSTed is decided by `disabled` attributes, and jQuery's `.prop()` fires no `change`. Drive it in the browser; reading alone is near worthless here.
6. **Regression correctness** — every commit since the last audit, diffed against what it replaced. This beat, and only this beat, is deliberately scoped to the delta: the round-N-1 fixes are dense in defects, and round 7's highest-value findings were all residue of round 6's. That density is why it gets a deep sweep — it is not a reason to point the other beats here.
7. **Regression tests** — do the tests added alongside those commits fail against the unfixed code? Revert the fix hunk in a scratch copy and find out.
8. **Dead code & refactor residue** — orphans from earlier deletions.

**Every worker prompt carries this:** write **nothing** into the repository directory — no PID files,
no logs, no scratch, no notes; everything goes under the rig directory. "Do not modify the working
tree" is not enough: round 7's beats read it as "do not edit source" and left four `.pid` files in the
root. One untracked file makes the tree dirty and Phase 5 refuses the commit. Also: own port, own
catalog, kill only by captured PID — never `pkill -f "Bastet.dll"`, which cost two agents their
applications in round 6.

Tag `[x2]` (both passes, independently) or `[x1]` (one pass). **Absence is weak evidence** — a `[x1]`
deserves *more* scrutiny, not less. The deep sweep is a third population and does not by itself make
anything `[x2]`.

## Phase 3 — adversarial verification (1-2 agents per candidate)

Every candidate goes to a verifier prompted to **refute** it, defaulting to "not real" when uncertain.

`[x2]` candidates get one verifier and that verdict stands. `[x1]` candidates get a second on a
reachability-and-consequence lens, because one full independent pass already missed them. **If the two
disagree, a third breaks the tie and the majority wins** — one aggressive verifier should not be able
to bury a real defect on its own, and a candidate two of three verifiers can refute was not solid
enough to hand a human. The third only runs on disagreement, so it costs almost nothing.

**Reproduce it or kill it.** The rig is live. The verifier drives the failure — sends the request, runs
the query, clicks the wizard — and records `reproduced` as `yes-ran-it` (with the actual command and
the actual observed result), `no-could-not` (**refuted**), or `not-runnable` (the narrow exception for
dead code and missing assertions, with the reason stated). A finding nobody executed is how a
hallucinated defect reaches a human. In round 7 this killed 8 of 36 — nearly a quarter.

A verifier may also change the answer rather than the confidence: kill a proposed *fix* while keeping
the finding, correct a severity, correct a citation. Round 7 found 8 of 20 proposed fixes unsound or
incomplete. Those corrections go in the file.

**If a finding's own failure scenario opens with "not a runtime defect", it is refuted.** Rounds 4-7
killed the same test-coverage-observation shape every time.

## Phase 4 — the scribe (2 agents, sequential)

One writes `docs/AUDIT-FINDINGS-<N>.md`. A second re-checks **every** citation against the working tree
and **fixes** what is wrong — round 7's checker corrected 5 of 130, including one stale line number
carried over from round 6. A findings file is correct only against the HEAD it was written at.

**Every finding names the previous-round fix it came out of, or says it came out of none.** One line
in the finding: *"residue of O8"*, or nothing if it is independent. The scribe then totals them and
opens the Verdict with the rate:

> Round `<N>` filed `<F>` findings, of which `<R>` are residue of round `<N-1>`'s own fixes.

Round 16's was **11 of 15** — the audit was not finding a rotten codebase, it was finding the fix
loop's own output, and nobody had measured that in sixteen rounds. It is the single most useful number
this round produces about *the process* rather than the software, it costs one line per finding, and a
falling rate is the only evidence that the loop is converging. `/audit-reconcile` steps 5 and 6 exist
to drive it down; this is how anyone can tell whether they worked.

## Phase 5 — teardown and commit (1 agent)

In this order:

1. Tear down containers, processes, and **every Azure resource in the rig inventory** — enumerate and
   delete, then **re-list both resource groups and assert no round fixture remains**. Report what was
   deleted. An empty removal list with a success verdict is a bug, and so is a delete whose error
   nobody read. Cloud resources are the one part of the rig that outlives the machine.
2. Sweep untracked root-level strays, each guarded with `git ls-files --error-unmatch`, touching
   nothing under `src/`, `test/` or `docs/`.
3. Confirm `git branch --show-current` is `audit/round-<N>`. **If it is `main`, stop and report** —
   Phase 1 failed to branch and the commit must not land here.
4. Confirm the tree carries nothing but the findings file, then commit it alone.

Commit subject, fixed shape so a glance at `git log` tells you the round's outcome:

```
Audit round <N>: <S> findings survived (<a> critical, <b> high, <c> medium, <d> low, <e> info), <R> refuted
```

Body: baseline branch/HEAD/test count, the beat and pass structure, and the funnel — raw findings,
candidates, survived, refuted, and how many were reproduced live. No trailers; the host strips them
on replay, so they are noise.

**Never push.** The remote is read-only here and publishing happens on the host.

Then assert, and report failure loudly if any of these is false: the commit exists, it touches exactly
one file, `main` still points where the baseline said it did, and the tree is clean. Round 7 satisfied
none of the branch conditions and reported success anyway.

---

## What every finding must carry

- **File and line citation**, re-checked against the working tree.
- **Confidence: confirmed or plausible.** *Plausible* names the load-bearing step that could not be
  established. It is not a hedge.
- **A concrete failure scenario** with real inputs and the wrong output.
- **Evidence it was reproduced** — what was run, what came back.
- **A proposed fix**, plus a cheaper interim where one exists.
- **Attribution: which previous-round fix this is residue of**, by its id (*"residue of O8"*), or an
  explicit *none* if it is independent of the last round. Use `git log`/`git blame` on the cited lines
  to settle it rather than guessing. This is what the residue rate is totalled from, and it is the
  round's only measurement of whether the fix loop is converging.

Grouped by severity, numbered sequentially across the file, ordered within severity by consequence.

## Output structure

```
# Bastet — Round-<N> Audit Findings
target branch / HEAD / test baseline / date

## Verdict            — what was found, and what to read first
## How this audit ran — beats, verification, what [x2]/[x1] mean

# Critical / High / Medium / Low / Info

# Refuted — reported by a finder, killed by the verifier   (table, with reasons)
# Watch list — not findings, but worth knowing
```

**No clean-bill section.** Dropped deliberately: it bulked the file and fed itself back into later
rounds. Do not reintroduce it.

**A round that finds nothing still writes and commits the file.** Verdict section says so plainly,
with the baseline, the agent counts, the funnel that collapsed to zero, and the refuted table — which
is the whole content in that case, and the part worth having. A clean round is the goal, not a
failure, and it must leave the same durable record as any other so the next one can see it happened.

## Constraints on what counts as a finding

- **Bastet is an open source tool anyone can host any way they like.** Plain-HTTP and air-gapped
  deployments must keep working. "Assumes HTTPS" is not a finding, and a fix that breaks those is a
  bad fix.
- **No literal control characters** — write `(char)0x1B`. Literals are invisible in diffs and get
  mangled through tool round-trips.
- **Migration `.Designer.cs` snapshots are frozen history.** Never report them as stale.
- **No novels.** A finding is a citation, a scenario, a reproduction, a fix.

## Scope is the owner's call, never the round's

**A round may decide whether a defect is REAL. It may never decide whether a real defect is WORTH
FIXING, or that fixing it is "out of scope", "a feature change, not a bug fix", or "too expensive".
That is a product decision and it belongs to the repository owner. Not to a finder, not to a verifier,
not to the scribe, and not to you.**

Round 6 broke this and it cost four rounds. It reproduced a real defect — both Azure import wizards
silently truncate a multi-prefix Azure subnet to its first prefix, after which BASTET advertises
Azure-assigned ranges as free space — then decided by itself that closing it "means creating several
Bastet subnets from one Azure subnet, which is a feature change, not a bug fix, and is out of scope
here." It demoted the finding to a watch-list line. Rounds 7-9 inherited the demotion without
re-examining it; rounds 10-12 dropped it entirely. Round 13 rediscovered it independently and measured
the operator-visible consequence for the first time — a *Create Subnet* button over an /24 Azure had
already assigned. **The owner was never asked, across four rounds, whether an IPAM tool silently lying
about free space was acceptable.** The answer, when finally put to them, was not close.

The failure mode is specific and worth naming: **every one of those deferrals priced the COST of the
fix and none of them priced the CONSEQUENCE of the defect.** Cost is visible from inside the code.
Consequence requires running the thing and looking at what the operator sees. A round that defers on
cost alone has not done the work that would justify deferring.

Therefore:

- **A reproduced defect gets filed as a finding at the severity its consequence warrants**, whatever
  the fix costs. Fix cost belongs in the *Fix* section, never in the severity and never in the
  decision to file.
- **"Out of scope", "feature change not a bug fix", "too big for this round", "the data model does not
  support it"** are not verdicts a round may reach. They are *recommendations to the owner*, and they
  go in the finding's own text where the owner will see them — not in a watch-list line, and never as
  a reason to lower severity or drop the finding.
- **Severity is graded on what the software does wrong, not on how often it does it.** Rarity of the
  trigger is one sentence in the failure scenario. It does not reduce severity. For an IPAM tool
  specifically, *silently asserting that an allocated range is free* is a top-severity output no matter
  how narrow the path to it, because the product's entire purpose is being the authority on that
  question.
- **Never use the watch list as a place to put a real defect you have decided not to fix.** The watch
  list is for things a verifier *could not settle* — thin evidence, unproven reachability, patterns
  worth grepping next round. A reproduced defect on the watch list is a finding that has been hidden,
  and it will fall off within three rounds. It did.
- **If a round believes a finding needs a scope decision, say so at the top of the Verdict**, in the
  words "this needs a decision from you and here is what it costs" — so it is the first thing read,
  not the last thing skimmed.
- **The same rule binds `/audit-reconcile`.** Declining to implement a filed finding, narrowing it, or
  substituting an interim for the real fix is the owner's call. Ask; do not decide and record the
  decision as though it were settled.

When in doubt: **file it, rate it on consequence, and let the owner say no.** A finding the owner
declines costs one line in a table. A defect a round declines on their behalf costs four rounds and
ships the bug.
