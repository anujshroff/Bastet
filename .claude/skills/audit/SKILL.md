---
name: audit
description: Run a fresh multi-agent security and correctness audit of the Bastet codebase, producing a numbered findings file in docs/. Use when asked to "run an audit", "start a new audit round", "audit the codebase", or "find bugs across the whole app". For reviewing a single PR or working diff use the built-in /code-review instead; to fix findings from an audit that already exists use /audit-reconcile.
---

# Run an audit round

Produces `docs/AUDIT-FINDINGS-<N>.md`: a numbered list of verified defects, each with a citation, a
confidence level, a concrete failure scenario and a proposed fix.

The output is worked afterwards by `/audit-reconcile`. Write it for that reader — precise citations,
honest confidence, and no findings that cannot be acted on.

## Before starting

**Say what this will cost.** Round 4 ran 88 agents across two passes. Confirm the user wants that
before spawning anything.

**Establish the baseline.** A moving baseline invalidates everything downstream.

```
dotnet build --no-incremental      # expect 0 warnings
dotnet test                        # record the count
git rev-parse --short HEAD ; git branch --show-current
```

Use `--no-incremental`: an incremental build does not re-run the analyzers and reports 0 warnings
even when there are some. If the build is dirty or tests fail, stop and report — do not audit a tree
that is already broken.

**Read the previous round.** `docs/AUDIT-FINDINGS-*.md`, highest number — rounds 3 and 4 are there.
From it, take:

- the **round letter**: round 3 used `C1..C23`, round 4 used `D1..D43`, so the next is `E`;
- the **refuted table** — do not re-raise anything in it;
- **struck entries** (italic paragraphs starting `_D12 is fixed and committed…`) — already fixed, and
  the paragraph explains what was deliberately *not* done and why. Re-raising one of those is the
  most annoying kind of noise;
- the **watch list** of accepted risks, carried forward again unless something has changed.

Accepted and still open, do not re-raise as new: ForwardedHeaders trust-all with `AllowedHosts: "*"`,
the Development-only `DevAuthHandler` bypass, `GlobalSanitizationFilter` skipping nested `System.*`
collections, `CollectDescendants` lacking a cycle guard, the unreachable IP-change branch in
`ValidateHostIpUpdate`, the blind `catch {}` around the DataProtectionKeys probe, and **C20** (the
Azure reconcile check/act window).

## The beats

Seven subagents in parallel, each with one beat and the baseline context:

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
6. **Regression review** of every commit since the last audit — diff them line by line against what
   they replaced, and check the tests added alongside actually assert the new behaviour.
7. **Dead code & refactor residue** — orphans left by earlier deletions. This beat exists because a
   round-3 deletion left a helper behind, and the round-4 version of it found nineteen more.

## Adversarial verification

**Every finding goes to an independent verifier**, prompted to **refute** it and to default to "not
real" when uncertain. Only survivors reach the file.

The verifier's job is to kill findings, not to confirm them. Round 4 refuted five this way, all of
which reduced to preference wearing a severity label: an unused optional parameter, three constants
that "could" drift while currently agreeing, an interface member with no external caller. **If the
finding's own failure scenario opens with words like "not a runtime defect", it belongs in the
refuted table.**

Record refuted findings in a table at the end with the reason they were killed, so the next round
does not spend agents re-discovering them.

## Run the beats twice

Round 4 did this by accident and it produced the most useful signal in the file.

Tag every finding **`[×2]`** (both passes found it independently) or **`[×1]`** (one pass only). The
corollary is the important half: **absence is weak evidence.** A `[×1]` is not weaker in truth, but it
means one full pass missed it — so it deserves *more* scrutiny during reconciliation, not less. State
what the tags mean in the file itself.

For reference, round 4's passes: 31 survived / 1 refuted, and 37 survived / 4 refuted.

## What every finding must carry

- **File and line citation**, re-checked against the working tree before the file is written. Round 4
  re-checked all of D1–D10 plus a sample of the rest and found no invented line numbers; keep that
  record clean.
- **Confidence: confirmed or plausible.** *Plausible* means a load-bearing step could not be
  established — say which step. Do not use it to hedge a finding you simply did not check.
- **A concrete failure scenario** with real inputs and the wrong output. "This could be a problem" is
  not a finding.
- **A proposed fix** — and where a cheaper interim exists, both.

Group by severity (critical / high / medium / low / info), number sequentially across the whole file,
and order within severity by consequence.

## Output

Write `docs/AUDIT-FINDINGS-<N>.md`, creating `docs/` if absent, and **commit it**. It is a versioned
record: `/audit-reconcile` then commits an updated copy alongside each fix, so the reasoning and the
change land together.

This machine has read-only access to the remote and never pushes — publishing is handled elsewhere.
Do not attempt it.

Structure, matching round 4:

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
looked at, so its agents can go somewhere new.

## Constraints on what counts as a finding

- **"This is an open source tool that anyone can host in any way."** Plain-HTTP and air-gapped
  deployments must keep working. "Assumes HTTPS" is not a finding, and a fix that breaks those hosting
  models is a bad fix.
- **No literal control characters** in any example or fix — use a named constant such as
  `(char)0x1B`. Literals are invisible in diffs and get mangled through tool round-trips.
- **Migration `.Designer.cs` snapshots are frozen history.** They contain old column widths on
  purpose. Never report them as stale.
- **No novels.** A finding is a citation, a scenario, a fix.
