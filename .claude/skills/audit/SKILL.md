---
name: audit
description: Run a fresh multi-agent security and correctness audit of the Bastet codebase, producing a numbered findings file in docs/. Use when asked to "run an audit", "start a new audit round", "audit the codebase", or "find bugs across the whole app". For reviewing a single PR or working diff use the built-in /code-review instead; to fix findings from an audit that already exists use /audit-reconcile.
---

# THE ABSOLUTE RULE. READ THIS FIRST. IT SUPERSEDES EVERY OTHER RULE IN THIS FILE.

**DO NOT FUCKING READ A GOD DAMN FUCKING TEST FILE.**

**THE AUDIT DOES NOT FUCKING READ THE TESTS. EVER.**

Nothing under `test/` — no test file, no test helper, no test fixture, no test project file, no
test name, no test output beyond a pass/fail count — is opened, read, grepped, listed, diffed,
blamed, quoted, cited, summarised or reasoned about by ANY agent this skill launches: not the
briefing agent, not the rig agent, not a finder, not the merge, not a verifier, not the scribe, not
the committer, and not the operator running the round. **NO CODE FROM THE FUCKING TESTS ENTERS THE
CONTEXT OF THE AUDIT SKILL. NONE. NOT ONE FUCKING LINE.** The audit looks at the product — `src/` —
and at what an operator can see. Tests serve the product, never the other way around. Whether a
test is good, weak, missing or decorative is reconcile's fucking problem, at fix time, and is never
an audit finding.

Consequences, so nobody has to think:

- **The briefing's codebase map describes `src/` only.** As far as this skill is concerned the test
  project does not fucking exist.
- **The one permitted contact with tests is the baseline `dotnet test` in Phase 1**, run for its
  pass/fail count and nothing else. Its output is a number. If it prints test names because
  something failed, the round stops anyway.
- **Every `git log -p`, `git diff`, `git show` and `git blame` is scoped `-- src/`** (or another
  non-test path). A delta beat that wants "the whole commit" gets the commit without `test/`.
- **Every worker prompt carries this rule verbatim.** A worker that touches `test/` has its output
  discarded. A candidate that cites a test file, names a test, quotes a test, or argues from what a
  test does or does not cover is **refuted on sight** and is never written to the findings file.
- **If following any other rule in this file, in `docs/PRODUCT-MODEL.md`, or in a worker's own
  judgement would require reading a test, that other rule fucking loses.** This rule supersedes
  all of them, and `docs/PRODUCT-MODEL.md` §5 says so in its own words.
- **Test names inside documents the audit legitimately reads are not "reading a test".**
  `docs/AUDIT-LEDGER.md` rows and `docs/PRODUCT-MODEL.md` §8 mention test names in prose; a
  worker passes over them. The prohibition is on the `test/` tree itself and on reasoning from
  what a test does or does not cover.

Owner, round 29, verbatim: "edit the audit skill and add an absolute rule that that skill shall NOT
READ ANYTHING IN THE TESTS AT ALL. NO CODE FROM THE FUCKING TESTS SHOULD ENTER THE CONTEXT FOR THE
AUDIT SKILL"; "that rule supercedes all other rules"; "all we're doing is fucking around with
tests"; "Make it as clear as can be"; "MAKE IT CLEAR IN THE SKILL VERBATIM: DO NOT FUCKING READ A GOD
DAMN FUCKING TEST FILE".

# Run an audit round

A round is **one `Workflow` call** that you launch and then actively operate. It always runs the
same shape against the same rig and commits the same way.

**What Bastet is, what counts as a finding, and what must never be filed live in
`docs/PRODUCT-MODEL.md` — the single copy, shared with `/audit-reconcile`. Read it before doing
anything else. Nothing in this file overrides it, except the ABSOLUTE RULE above, which §5 of the
model itself places over everything for the audit.** `docs/AUDIT-LEDGER.md` is the loop's memory
(main is squash-merged, so commit history is not): round outcomes, finding verdicts, residue rates.

## The scale gate

Read the Rounds table in `docs/AUDIT-LEDGER.md`. **If the most recent completed round's residue is
above 2, this round runs Regression-only, whatever was asked** — say so at launch and do not offer
the choice. The loop has diverged before (residue 11/15 → 12/21 → 20/23 across rounds 16–18) and a
full discovery round during divergence audits the fix process's own output. A Regression-only round
is the measurement instrument: it scopes to the delta since the last audit and writes the residue
number that re-opens discovery.

## This machine is disposable

The VM is reverted daily to a state before you existed. Nothing in `~/.claude` survives — not memory
files, not config, not credentials, not a signing key. **Only committed files survive**, carried off
by the audit-findings commit, which the host replays and re-signs. Anything that must hold across
runs belongs in this file, `docs/PRODUCT-MODEL.md`, or `docs/AUDIT-LEDGER.md`. Do not write
preferences to memory and expect them back. Do not assume a credential or a git identity from a
previous round exists.

## Ask once, for inputs only

Ask for **the inputs the round cannot infer, once, in a single message, before launching.** Check
what is missing first, then ask for everything missing in one go — never trickle questions, never
ask again later.

Always needed:

- **the scale**, unless the scale gate has already fixed it at Regression-only:

  | | finders | verifiers | total |
  |---|---|---|---|
  | Regression-only | beat 6, 2 passes = 2 | ~4 | ~9 |
  | Standard (default) | 7 beats x 2 passes + deep sweep on beat 6 = 16 | ~30 | ~48 |
  | Deep | 7 beats x 2 passes + deep sweep on beats 1, 3, 6 = 18 | ~36 | ~58 |

  **Every scale keeps two independent passes** — the `[x2]`/`[x1]` signal drives verification depth
  and is the most useful thing the round produces. Shrink beats or deep sweeps, never passes.

- two service principals — client id and secret each, with disjoint RBAC scope over the two resource
  groups; the tenant id; the subscription id; both resource group ids.

Conditionally needed — **check before asking**:

```
git var GIT_AUTHOR_IDENT        # "Author identity unknown" (exit 128) means it is unset
```

If that fails, **ask for the git name and email in the same message.** Do not guess them, do not
scrape them out of `git log`, and do not discover the problem forty minutes later at the commit
step. Whose name goes on a commit is the user's call. If the identity resolves, say nothing.

Credentials are **never stored**. Pasted by the user, written once to a scratchpad file the script
points agents at, and dead with the machine. Never into the repository, a config file, a prompt
repeated across sixteen agents, or a commit. A credential from a previous round is not evidence of a
working one — they rotate and get revoked.

**Ask nothing else. Ever.** Not rig, not verification depth, not "shall I proceed". If a required
input is missing at run time, the rig agent stops the round naming it — that is the only way this
skill returns without a findings file.

## Fixed configuration

| | |
|---|---|
| Verification | 2 adversarial verifiers per candidate, `[x2]` included (truth lens + reachability lens); a 3rd only to break a tie; two surviving votes to file |
| Rig | Always live: database, application, browser, Azure fixtures in both resource groups |
| Branch | `audit/round-<N>`, created in Phase 1 **before any work runs**. **`main` is never touched** |
| Output | `docs/AUDIT-FINDINGS-<N>.md`, committed, never pushed |

# The round exists to reduce defects, not to produce findings

**The loop's terminal state is a zero-finding round, and zero filed is the success condition.** A
finding is an operator-visible wrong behaviour reproducible at HEAD — a product defect — and
nothing else. **The audit files no test findings** (`docs/PRODUCT-MODEL.md` §5, owner ruling in
§8, round 29): a missing, weak or unrecorded pin is reconcile's fix-time duty and its gate's
violation, never a finding here. Write the empty findings file, commit it, and report zero
proudly.

**Measure the residue rate and lead with it.** Every finding names the previous-round fix it came
out of — by **ledger id** (`<round>-<finding>`, e.g. `18-R4`) from `docs/AUDIT-LEDGER.md` — or an
explicit *none*. When most findings are residue, the audit is reporting the fix loop's own output,
and a round that does not say so in its first sentence has buried the most important thing it knows.

- **Report the defect, not the instance.** A finding naming one call site when the rule is wrong at
  three hands reconcile a fix that cannot close it. Name every site.
- **A proposed fix that would introduce a new defect is worse than no proposal.** Roughly half of
  all proposed fixes have been judged unsound on review; that check is the most valuable thing
  verification produces.
- **A fix proposal must be narrow.** If closing a defect appears to need a component restructured,
  say so explicitly and separately.

**Audit the mechanism before proposing to extend it.** A previous round's fix already in the tree
reads as settled design; it is not. Whenever a candidate proposes extending, widening or adding a
guard, withhold, refusal, special case or status:

- **Name the contract it serves**, from `docs/PRODUCT-MODEL.md`. If you cannot point at the
  sentence, the finding is that the mechanism exists, not that it is incomplete.
- **Check who introduced it.** `git log -S` the identifying string. A previous round's commit is an
  opinion, not a requirement.
- **Prefer the finding that removes it.** Only "this mechanism is on the wrong axis" can end the
  cycle; "this mechanism has a gap" perpetuates it. Rounds 14–17 each widened the same withhold
  path this way.

**Growth is evidence.** Report the line count of any component you file more than one finding
against, at this round's HEAD and at the previous audit commit. A component that has doubled across
rounds while the product's requirements did not change is being driven by the audit loop, and that
belongs in the round's headline.

**The owner's product model outranks the finding's reasoning, and outranks yours.** When they
contradict, the finding is wrong by definition — record it struck or inverted, do not argue it
through. Owner rulings land verbatim in PRODUCT-MODEL.md §8.

# No questions, ever

The round asks for inputs it cannot infer — credentials, scale, git identity — once, up front.
Nothing else. **Never ask how to fix something, and never ask the owner to choose between fixes.**
Where a fix implies a product change, file the finding with the narrowest correct fix and state the
product question *inside the finding*. Do not block.

# You are the operator, not a spectator

Launch the workflow, then **watch it and intervene**. Do not launch and look away, and do not answer
status questions by pointing at `/workflows` — it does not exist in the VSCode extension. Merge
agents have stalled dead mid-run; the round only finishes because someone is watching.

## Your tool budget

| Tool | For |
|---|---|
| `Workflow` | The round. Launch, and resume after an intervention. |
| `Bash` | **Read-only** run inspection and git state. Never build, test, or touch the app. |
| `Write` / `Edit` | The workflow script, in scratchpad. **Never a repo file while a round is running** |
| `Read` | Scratchpad files and the run journal. |
| `TaskStop` | Killing a stalled run before resuming it. |
| `TodoWrite` | The phase list. |

**Never `Agent` — during an audit round.** Spawning workers directly puts every one of their tool
calls in the user's conversation, sixty workers deep. One `Workflow` call, or nothing. (This ban is
scoped to the audit round; `/audit-reconcile` legitimately uses single reviewer subagents.)
Everything that *does* audit work — build, tests, git archaeology, containers, cloud fixtures,
reading source, verifying, writing, committing — happens **inside the script**.

## Polling

Use **`python3`** — it is present on Debian, and `grep` against JSON gives wrong answers the moment
a finding's text contains the string you are matching on. (Node is neither present nor needed.)

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

Read verdict fields out of `results` the same way — `survives`, `reproduced`, `tag`, `memberIds` —
rather than grepping. A `result` line means an agent **returned**; that work is banked. Agents
killed in an earlier attempt never get a result line and linger in the started-minus-results
arithmetic — check each in-flight id's transcript age before believing the count.

## Stall detection and escalation — fixed thresholds, no judgement

| condition | action |
|---|---|
| active transcript static **< 8 min** | normal; a single long generation looks like this |
| static **≥ 8 min** | stalled. `TaskStop` the run, relaunch with `resumeFromRunId` |
| **2nd stall at the same step** | structural. Stop, fix the script, resume — no third retry |

Resume replays every completed agent from cache and re-runs only what did not finish; an
intervention costs one agent, not sixty. **Editing the script is free for any step that has not
completed. Resume is same-session only** — if the session ends, every banked result is lost. That
is the reason to intervene rather than wait something out.

## Reporting

**Status questions get a markdown table and nothing else** — same rows every time (time, started/
results, in flight, active agent + age, candidates with the x2/x1 split, judged/survived/refuted,
reproduced/failed/not-runnable, findings file bytes, dirty-tree count, HEAD). No narrative, no
reassurance. If something is broken, one short sentence of data.

At launch: one line. At completion: the funnel, the severities, the headline finding, **the residue
rate**, the commit sha, anything teardown failed to clean, and — **always** — a reminder to revoke
the service principal secrets. **If the residue rate is high, it is the headline, not a footnote.**

---

# The script

**Effort and model are the UI's, never the script's.** The owner sets effort and model in the
UI before the round; every agent inherits them. **No `agent()` call ever passes `effort` or
`model`**, at any phase, for any reason — not "low for the mechanical stages", not "max for the
verifiers". A script carrying either option is wrong and is fixed before launch. The same holds for
the operator: never change the session's effort, and never choose a tier on the owner's behalf.

`meta.phases` must match the `phase()` calls. `pipeline()` by default. Only two genuine barriers:
Phase 1 (nothing starts until the baseline is known good) and the merge (telling `[x2]` from `[x1]`
needs every beat's output at once). Put a `schema` on every agent the script branches on. **The
baseline is a hard gate:** dirty tree, failing test or build warning → `return` immediately.

**The merge must return ids, not prose.** Give it a flat list of findings, each with an id, carrying
only what it must reason about — title, severity, file, line, confidence, scenario, fix. It returns
groupings of ids (`memberIds`, `canonicalId`, `tag`, drop list); the script rebuilds full candidates
in plain JavaScript. Two consecutive merge agents once stalled dead re-transcribing the corpus as
one structured payload; the id-based version landed in under four minutes.

## Phase 1 — briefing and baseline (`parallel`, 2 agents)

Both write files into the scratchpad; every later agent is handed the **paths**, never the contents.

**Briefing agent** → `BRIEF.md` (which never mentions `test/` — see the ABSOLUTE RULE), built from: `docs/PRODUCT-MODEL.md` **whole** (finders never see
the skill file — the brief carries the model, the finding format, and the constraints); the Rounds
and Findings tables of `docs/AUDIT-LEDGER.md` (what the last reconcile fixed, by id, so the
regression beats know where to look — this replaces reconstructing history from squashed `git log`);
`git log` since the last audit commit for the actual delta; and — **the longest section** — a map of
the codebase good enough to orient a worker who has never opened it. If a `docs/AUDIT-FINDINGS-*.md`
survives, a previous round was never reconciled: read it and say so in the brief. The round number
is previous + 1, derived from the audit commits in `git log`.

**Rig agent** → `RIG.md`. Preflight, baseline, then stands the rig up.

### Preflight — the environment, then the checks that have each cost a round

Assume nothing is installed: a fresh Debian box with VSCode, the Claude Code extension and a fresh
checkout — no .NET, no Docker, no browser, possibly no `curl`. **Install what installs unattended;
stop and name what does not.** Anything needing `sudo`, a group change or a re-login is the user's
action — report the exact command and stop.

| check | command | if missing |
|---|---|---|
| .NET SDK | `dotnet --version`, major matches the TFM | **Stop.** Name the version required |
| Docker daemon, **as this user** | `docker info` | **Stop.** A group-membership failure needs a re-login you cannot do |
| SQL Server image | `docker image inspect` the tag | Pull it **here** — never leave sixteen beats to pull 1.5 GB concurrently |
| Browser for beat 5 | chromium present for Playwright | Install if unattended; if it wants `install-deps` and root, **stop and say so** — beat 5 must not fail quietly |
| `curl` | `curl --version` | Install; every ARM probe uses it |
| **Azure CLI** | `az version` | **Install it — required.** No sudo: `python3 -m venv <rig>/azcli`, then `curl -sSL https://bootstrap.pypa.io/get-pip.py \| <rig>/azcli/bin/python -` (Debian's `venv` ships without `ensurepip`, so the bootstrap script is the way through), then `<rig>/azcli/bin/pip install azure-cli`. Do it **here**, once. If it genuinely cannot install, **stop and say so** |
| Disk headroom | SDK + image + browser + `bin`/`obj` across all agents | **Stop** if tight. A mid-round `ENOSPC` mimics the memory death |
| CPU count | `nproc` | Concurrency is `min(16, cores-2)`. Report it — on 4 cores the finders are nearly serial |
| Network egress | NuGet, MCR, `management.azure.com` | **Stop** and name the unreachable host |
| Memory | ≥ 16 GB | **Under 16 GB the host dies mid-run.** Stop and name the figure |

**Why `az` is mandatory:** the Azure beats rest on two service principals with **disjoint** RBAC,
which is vacuous if merely asserted — a subscription-scope assignment inherits into both groups and
still looks like it works. `az role assignment list --all --assignee <id> --query
"[].{role:roleDefinitionName,scope:scope}"` settles it in one call, **before** anything is measured.
It is also how VNet fixtures are created and torn down.

**Credentials never go on a command line.** Env file in the rig, referenced as variables; give each
principal its own `AZURE_CONFIG_DIR` so two logins do not overwrite one another. For driving the
**application**, export `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` / `AZURE_TENANT_ID` so
`DefaultAzureCredential` runs the production path unmodified — and make sure
`AZURE_TOKEN_CREDENTIALS` is **unset**: the launch profiles set it to `dev`, which excludes
`EnvironmentCredential` and produces a credential failure that looks like a permissions problem.

Then, for each principal: confirm the two resource groups **exist and are distinct** first — a
typo'd resource group id returns 403 and is indistinguishable from a missing role assignment. Fetch
a token and probe **both** groups: expect 200 on its own and **403** on the other, reversed for the
second principal, for reads *and* writes, through the application as well as `curl`. The
*discrimination* is the point. If the matrix does not reproduce, stop and name the failing leg.

**No Node is required anywhere.** Workflow scripts are JavaScript but the Workflow tool runs them
itself. Never syntax-check a script with `node --check` — on a machine without Node that silently
passes and proves nothing; `Workflow` reports syntax errors on launch. **`Workflow` must be
available** — if not, **stop and say so**; never quietly fall back to `Agent`.

**Git identity:** `git var GIT_AUTHOR_IDENT`. If unset, set the supplied name and email at
**global scope** (`git config --global`) — identity belongs to the machine, and a `--local` fix
leaves every other checkout broken. If unset *and* not supplied, stop; do not invent one. Unsigned
commits are fine — the host re-signs on replay.

### Baseline

```
dotnet build --no-incremental      # expect 0 warnings; incremental does not re-run the analyzers
dotnet test                        # record the count
git rev-parse --short HEAD ; git branch --show-current ; git status --porcelain
```

Untracked strays from a previous round are the one dirt you may clear, each guarded with
`git ls-files --error-unmatch`.

### The branch — created here, not at commit time

The moment the baseline is green:

```
N=$(ls docs/AUDIT-FINDINGS-*.md 2>/dev/null | sed 's/.*-\([0-9]*\)\.md/\1/' | sort -n | tail -1)
git checkout -b "audit/round-$((N+1))"
```

If no findings file exists, derive `N` from the ledger's Rounds table instead. The script **asserts
this number matches the briefing agent's derivation and stops if they disagree** — two independent
derivations is a cheap identity check. Branching here means there is never a window in which the
round could commit to `main`; a round that branched at commit time landed its findings on `main`.
`main` must be byte-identical before and after a round.

### The rig

**Sweep the wreckage of a dead round first**: stale `bastet-audit-*` containers, leftover `rig-*`
fixtures in both resource groups. A killed round leaves its rig running, and rebuilding on top of it
means auditing state you did not create. Then: database, application, browser, and Azure fixtures in
**both** resource groups. **Keep an explicit inventory of every Azure resource created** — a
teardown once reported success while removing nothing, because nothing forced it to enumerate. The
inventory is what Phase 5 deletes.

## Phase 2 — the beats, twice (+ 1 merge)

**Beats 1–5 and 7 audit the WHOLE APPLICATION. Only beat 6 is scoped to the delta since the last
audit, and that is the only reason it exists.** Do not point the other beats at what changed
recently, however tempting — an audit that only re-examines the last round's diff cannot find the
long-standing defect, and recently-changed code is neither weighted nor exempt in the full beats. A
beat prompt that names specific recent findings as "the focus" has been written wrong: name the
surface, not the diff. (In a Regression-only round, beat 6 is the whole round.)

1. **Security / web** — authorization coverage, antiforgery, XSS, injection, SSRF, headers, log forging, secrets.
2. **Logic & data integrity** — subnet/CIDR arithmetic, containment and overlap, host-IP validation, any path that persists a state the validated path would reject.
3. **Azure integration** — the bulk import wizard, its planner, and the reconciler. Highest stakes: the only code that *deletes* on the strength of what an external system reports. Work partial visibility hard — throttling, an empty page, a 403 on one group, a token expiring mid-enumeration, a paged response whose second page fails. Which of those does it treat as "absent, therefore delete"?
4. **Locking & lifecycle** — `sp_getapplock`, the migration lock, transaction boundaries, check-then-act, EF pooling.
5. **UI & client-JS** — the wizards' state machines and emitted payloads. What gets POSTed is decided by `disabled` attributes, and jQuery's `.prop()` fires no `change`. Drive it in the browser; reading alone is near worthless here.
6. **Regression correctness** — every commit since the last audit, diffed against what it replaced, **production files only (`git log -p <base>..HEAD -- src/`)**. The previous round's fixes are dense in defects; that is why this beat gets the Standard deep sweep, and not a reason to point other beats here.
7. **Dead code & refactor residue** — orphans from earlier deletions.

There is no regression-tests beat. Whether last round's pins bind is reconcile's question, answered
at fix time under its proof rule; a beat that files it here is the round-26-to-29 churn the owner
ended (PRODUCT-MODEL §8, round 29).

**Every worker prompt carries this:** the ABSOLUTE RULE at the top of this file, verbatim — no agent reads, greps, lists or cites anything under `test/`; and: write **nothing** into the repository directory — no PID
files, no logs, no scratch; everything under the rig directory. "Do not modify the working tree" is
not enough: beats have read it as "do not edit source" and left `.pid` files in the root, and one
untracked file makes Phase 5 refuse the commit. Also: own port, own catalog, kill only by captured
PID — never `pkill -f "Bastet.dll"`, which has killed other agents' applications mid-run.

Tag `[x2]` (both passes, independently) or `[x1]`. **Absence is weak evidence** — a `[x1]` deserves
*more* scrutiny, not less. The deep sweep is a third population and does not make anything `[x2]`.

## Phase 3 — adversarial verification (2-3 agents per candidate)

Every candidate — `[x2]` included — goes to two verifiers prompted to **refute** it, defaulting
to "not real" when uncertain: one on a truth lens, one on a reachability-and-consequence lens. If
they disagree, a third breaks the tie. **A finding needs two surviving votes to be filed; a single
verdict never carries one.** Round 29 ran this shape after the owner asked for certainty: two
ephemeral audits of the same delta had filed nothing, and every candidate that survived did so on
two independent end-to-end reproductions.

**Reproduce it or kill it.** The rig is live. The verifier drives the failure and records
`reproduced` as `yes-ran-it` (with the actual command and observed result), `no-could-not`
(**refuted**), or `not-runnable` (the narrow exception for dead code, reason stated). A finding nobody executed is how a hallucinated defect reaches a human; this routinely
kills a fifth to a quarter of candidates. A verifier may also correct rather than refute: kill a
proposed *fix* while keeping the finding, correct a severity, correct a citation. **If a finding's
own failure scenario opens with "not a runtime defect", it is refuted.**

**A test-only candidate is refuted on sight**, citing `docs/PRODUCT-MODEL.md` §5: the audit files
product defects only. A verifier does not run its mutation, judge its pin or propose a better one.

## Phase 4 — the scribe (2 agents, sequential)

One writes `docs/AUDIT-FINDINGS-<N>.md`. A second re-checks **every** citation against the working
tree and **fixes** what is wrong — stale line numbers are routine. The scribe totals the residue
attributions and opens the header with the rate:

> Round `<N>` filed `<F>` findings, of which `<R>` are residue of round `<N-1>`'s own fixes.

## Phase 5 — commit, then teardown (1 agent)

**The commit comes first.** Only committed files survive this machine, the VM checkpoint erases the
local rig regardless, and stray cloud fixtures are a one-command manual sweep — the findings commit
is the round's product; teardown is housekeeping. A round once had its commit queued behind a slow
Azure teardown while a usage cap counted down; nothing about cleaning up is worth losing the
round's output.

In order:

1. Sweep untracked root-level strays, each guarded with `git ls-files --error-unmatch`, touching
   nothing under `src/`, `test/` or `docs/`.
2. Confirm `git branch --show-current` is `audit/round-<N>`. **If it is `main`, stop and report.**
3. Confirm the tree carries nothing but the findings file, then commit it alone.
4. **Then** tear down, best-effort: containers, processes, and every Azure resource in the rig
   inventory — enumerate and delete, then **re-list both resource groups**. An empty removal list
   with a success verdict is still a bug, and so is a delete whose error nobody read — but a
   teardown failure is reported in the completion summary with the surviving fixture list for a
   manual sweep; it is never a round failure and never blocks or amends the commit.

Commit subject, fixed shape:

```
Audit round <N>: <S> findings survived (<a> critical, <b> high, <c> medium, <d> low, <e> info), <R> refuted
```

Body: baseline branch/HEAD/test count, the beat and pass structure, the funnel. No trailers — the
host strips them on replay. **Never push.** Then assert, and report failure loudly if any is false:
the commit exists, it touches exactly one file, `main` still points where the baseline said, the
tree is clean. A round once satisfied none of these and reported success anyway.

---

## What every finding must carry

- **File and line citation under `src/`**, re-checked against the working tree. A citation under `test/` is refuted, not filed.
- **Confidence: confirmed or plausible.** *Plausible* names the load-bearing step that could not be
  established. It is not a hedge.
- **A concrete failure scenario** with real inputs and the wrong output.
- **Evidence it was reproduced** — what was run, what came back.
- **A proposed fix**, plus a cheaper interim where one exists. A wording-class finding — the whole
  defect is an untrue operator-facing message — is filed individually with its real fix (some need a
  conditional, not a string swap) and tagged `strings` so reconcile can batch the commits.
- **Attribution: which previous-round fix this is residue of**, by ledger id from
  `docs/AUDIT-LEDGER.md`, or an explicit *none*. Settle it with `git log`/`git blame` on the cited
  lines, not a guess.

## Output structure

**The findings file is a work queue for `/audit-reconcile` and the next round's briefing agent.
Both are machines. Nobody reads it as a report.**

```
# Bastet — Round-<N> Audit Findings
branch / HEAD / test baseline / date / residue rate

# Critical / High / Medium / Low / Info
# Refuted — reported by a finder, killed by the verifier   (table, with reasons)
```

Each finding is a heading and these fields, nothing else:

```
## <letter><n> — <one-line title> `[x2]` `strings`?
**Where:** src/Bastet/Services/Azure/AzureReconciler.cs:757
**Breaks:** <real inputs, the wrong output, one short paragraph>
**Repro:** <what was run, what came back>
**Fix:** <the narrow change; note if a verifier judged the filed fix unsound>
**Residue of:** <ledger id> | none
```

**No Verdict essay, no funnel table, no watch list.** A watch-list item is settled into a finding or
dropped — the watch list was a graveyard where real defects sat unexamined for rounds. A round that
finds nothing still writes and commits the file — header, empty sections, and the Refuted table.
Under 50 KB for a full round, or the round has confused volume with rigour. Do not spend agent time tightening a file that is already under the limit.

## Severity is graded on consequence

- **A reproduced defect is filed at the severity its consequence warrants.** Fix cost belongs in the
  Fix field, never in the severity or the decision to file. **Rarity does not reduce severity** —
  for an IPAM tool, *silently asserting an allocated range is free* is top-severity however narrow
  the path. **File it and rate it:** a finding the owner declines costs one line; a defect a round
  declines on their behalf has cost four rounds.
- **But grade honestly, and stop stacking.** Grade the defect you can reproduce, not the worst thing
  downstream of it. **If the fix is one string, the severity is Low. No exceptions.** A
  contradiction the operator can see on the same screen is Low. Same defect class, same severity.
  **Critical means an operator loses or double-allocates real address space with no signal** — zero
  Critical is a fine result to report.
