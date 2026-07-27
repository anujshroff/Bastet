---
name: audit-reconcile
description: Work through an existing Bastet audit findings file in docs/, fixing each finding in numeric order, proving the defect before fixing it, and committing one finding per commit. Use when asked to "reconcile the audit", "fix the audit findings", "work through the findings", or "continue the audit fixes". Defaults to per-finding approval; pass "auto" to run straight through. To produce a findings file in the first place use /audit.
---

# Reconcile audit findings

Works `docs/AUDIT-FINDINGS-<N>.md` from the top. Each finding is proven, fixed, verified, struck from
the file, and committed on its own.

**The findings file is the durable record.** There is no separate handoff document — audits and
reconciliation run on the same machine, and each struck entry carries what a later round needs.

## Mode

**Default: per-finding approval.** Explain the issue and the fix, wait for approval, then apply.
Invoked as `/audit-reconcile auto`: apply, verify, strike and commit each finding without stopping,
pausing only for something that genuinely needs a decision — a missing credential, an ambiguous
requirement, a finding that turns out to be wrong in a way that changes scope.

**State which mode is active before the first finding.**

## Start of run

1. Locate the findings file — `docs/AUDIT-FINDINGS-*.md`, highest number. If every finding in it is
   already struck, say so and stop; there is nothing to reconcile.
2. Read it whole first, including the refuted table and watch list, so an early fix does not
   contradict a later finding.
3. **Baseline:** `dotnet build --no-incremental` (0 warnings) and `dotnet test` (record the count).
   If either differs from what the file claims, **stop and report** rather than building on it.
4. Detect available tooling (see *Rigs* below) and prompt only for what cannot be obtained.

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

### 5. Sweep for orphans the compiler will not report

Deleting a method routinely strands `using` directives, private constants, locals and parameters —
and C# warns about **none** of them. This happened five separate times in round 4.

After every deletion, check what its removal just made dead. Then check the reverse: that anything
you are about to remove as "also dead" is not live somewhere else.

### 6. Verify

```
dotnet build --no-incremental     # 0 warnings — incremental skips the analyzers
dotnet test                       # full suite
```

Re-run whatever rig demonstrated the defect, now against the fix.

### 7. Strike the finding

Replace the finding in the file with an italic paragraph recording:

- what was done and **why that approach**;
- the evidence — what was measured, not what was assumed;
- **every place the audit's suggestion was rejected or found wrong, with the reason**;
- the test-count delta.

This is the durable part. A later round reads these instead of a handoff.

### 8. Commit

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
- **Migration `.Designer.cs` snapshots are frozen history** — never "fix" an old column width in one.
- **The test count must never regress without an explicit, recorded reason.** Round 4 legitimately
  went 588 → 576 by deleting tests of dead code, and said so.
- **Stay in scope.** Fix the finding in front of you. Improvements noticed along the way get
  mentioned, not made — an unrequested refactor riding along in a fix commit is exactly the residue
  these audits keep finding.
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
4. **Run the real app** against real SQL Server and request every major area — subnet list, create,
   details, edit, delete, deleted-subnets, purge, host IPs, all-deleted-host-IPs, error pages, both
   Azure wizards. **Assert real content and titles, not just HTTP 200.** Confirm the security headers
   still ride on a normal response.
5. **Read the log.** Classify every `fail:` / `warn:` line — some are expected, because a deliberate
   permission-denied probe logs an error *by design*. State the difference rather than glossing it.
6. **If Azure credentials are available**, drive both surfaces end to end against live ARM:
   subscriptions → VNet/subnet discovery → single-subnet import → bulk preview and commit → reconcile
   scan → delete commit. Include the two counter-tests that prove the reconciler **discriminates**
   rather than merely blocks:
   - a resource the credential *cannot see* must be **withheld**, with a warning naming it;
   - a genuinely deleted resource must **still be offered and deletable**.

   Checking only the first would let an over-blocking regression pass silently.
7. **`git status` clean**, and no scaffolding in any commit.

## Closing out

Update the header of the findings file with the final build and test numbers, the result of the
sweep, and a short list of what was deliberately not done. The struck entries already carry the
reasoning.

**If the final sweep turned up anything needing resolution, record it in the file and commit that
too** — a closing commit carrying the sweep's result. If the sweep was clean, the last fix commit
already carries the final state.

Then report the clean-up owed: revoke any credentials supplied, remove containers
(`docker rm -f <name>`), delete cloud test resources.
