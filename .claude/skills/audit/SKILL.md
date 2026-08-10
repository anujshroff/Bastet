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
at the commit step, after every phase has already run.
Whose name goes on a commit is the user's call; asking costs one line in a message you are already
sending. If the identity resolves, say nothing and do not ask.

Credentials are **never stored**. They are pasted by the user, written once to a scratchpad file the
script points agents at, and die with the machine. Never into the repository, never into a config
file, never into a prompt repeated across sixteen agents, never into a commit. A credential from a
previous round is not evidence of a working one — they rotate and get revoked.

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

# What Bastet is

**An IPAM tool. Its job is to be the authority on which IP space is allocated and which is free, and to
let an operator manage that space.** Every judgement resolves against that, in this order:

1. **Never report allocated space as free.** The worst output the product can produce. Reporting free
   space as allocated is second-worst.
2. **Never destroy an allocation record on incomplete information.** Archives are irreversible.
3. **The operator must be able to act on what they are told.** A message naming a remedy the app refuses
   is a defect.

**Rule 0, which overrides all three: Bastet is the authority, and it answers from its own records.**
Free means free *according to Bastet*. **Azure is not authoritative in Bastet at all** — it is a source
you import *from*. That is the whole reason the import wizard exists: Azure space becomes real to
Bastet by being imported, and until it is imported it does not exist as far as Bastet is concerned.
Reconcile does not change this — it *reports* that a linked Azure resource is gone or re-ranged so the
**operator** can decide. Azure never decides anything.

**Azure state and Bastet state are compared in exactly two places, and nowhere else:**

| surface | the only question it asks |
|---|---|
| the bulk import wizard | **can this be added?** |
| reconcile | **can this be deleted?** |

That is the entire Azure/Bastet arithmetic in the product. Every other screen — the subnet tree,
Details, unallocated ranges, host IPs, search — answers from Bastet's records alone and must never
consult Azure. **A finding that brings Azure state into any surface outside those two is out of bounds
by construction**, whatever it claims to have found. Check which surface a candidate is really about
before you write it up; findings reasoning about Azure on the Details page have been struck on sight.

So this whole shape of finding is **invalid and must never be filed**:

> "Bastet shows 10.20.9.32 as free, but an Azure subnet Bastet never imported holds it."

That is not a defect. It is the product working. Rule 1 governs Bastet's *own* records disagreeing
with each other or with a resource Bastet is linked to — nothing else. A finding that needs Bastet to
know about un-imported Azure space to be a defect **is not a defect**, however good the failure
scenario reads. Rounds have filed these repeatedly and the owner has struck every one on sight.

Corollaries, each of which has killed a filed finding:

- A wizard filter that hides a row which **cannot be imported** is correct — ticking it would change
  nothing, which is exactly what the filter means.
- Reconcile returning a clean scan over a partially imported subscription is correct.
- "The operator might allocate over Azure space" is not a consequence you may escalate on. They ran
  the import wizard or they did not.

**One flat, routable space.** Bastet manages a single IP space in which everything is routable against
everything else, so the same range must never be allocated twice — preventing that collision is the
product's reason to exist. Two consequences that decide findings:

- **"Still allocated" is a question about the whole managed space, never about provenance.** If any live
  resource holds a range, that range is in use, whichever VNet, subscription or import it came from, and
  archiving Bastet's only record of it reports an in-use range as free. Overlapping Azure VNets are not
  a case to defend — inside Bastet's model that overlap *is* the collision, not a legitimate
  configuration, so "but VNets may legitimately overlap" is not a reason to withhold a finding.
- **How the space is carved up is the operator's choice, and only theirs** — by hand, by Azure import,
  or both. Neither origin is privileged: a rule that holds for a manually created subnet holds
  identically for an imported one, and vice versa.
- **One Azure range is one Bastet row.** A VNet with a single address prefix whose single subnet covers
  that whole prefix is **one** row, not a VNet parent plus a byte-identical child. The import marks that
  row fully allocated instead of creating the duplicate. A parent and child with the same CIDR *are* the
  collision the product exists to prevent, and the second row tracks no free space, so it buys nothing.

**A row carrying an Azure resource id is a record the operator asked Bastet to keep in step with Azure.**
Azure is still not authoritative — Bastet is — but for *that row* the operator has said "track this", so
when Azure no longer has the resource, or no longer holds the recorded range, **reconcile must say so
and offer the delete**. It reports; the operator decides; nothing is ever removed on Azure's word alone.
Silently withholding the report is its own defect: it leaves Bastet asserting an allocation the operator
was never told to reconsider. The **only** legitimate reason to refuse is **manual content in that hierarchy — a
hand-added child subnet, or a host IP** — because that is operator-owned data Azure does not know about
and must never be destroyed silently. Nothing else qualifies. The range turning up in another VNet does
not; a prefix still "covered" after a re-carve does not. A finding that proposes withholding on any
other ground is wrong however good its failure scenario looks.

**An imported row's place in the tree mirrors Azure's containment.** Azure has no subnet nesting - a
subnet belongs to a VNet, flat - so a row carrying an `AzureResourceId` hangs directly off the row
representing its VNet. **A hand-made subnet may not sit between a VNet row and its Azure subnets.**
Refusing that is correct, and a finding proposing to resolve the Azure subnet under the hand-made
middle-man is wrong however well it reads: nothing breaks functionally, because reconcile keys on
resource id and free space keys on range, but the tree would then assert a containment Azure never had
and nobody reading it could tell which level was real.

This is the one place the manual and imported paths legitimately differ, and it does **not** contradict
"a rule that holds for a manually created subnet holds identically for an imported one" - the manual
form is free to carve anything, because a hand-made row claims to represent nothing but itself. What
the refusal owes the operator is the rule and a remedy, not just the fact: name the row in the way, say
Azure has no such subnet, and say to delete or re-carve it.

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

**Every write path for a field must accept and refuse exactly the same input.** Create, Edit, the bulk
import commit and any API caller are siblings: a rule on one and not the others produces a tree the app
itself populated but its own form will not re-enter. Check the attribute sets side by side, not the
error messages — the divergence that shipped was `[SafeText]` on the Create view model and not the Edit
one, so an operator could rename a subnet to `core/edge (site B)` and then be refused when creating its
sibling.

**A validation rule that refuses ordinary operator text is a defect, not caution.** Output encoding is
what makes the app safe — Razor encodes at every sink and the wizard's client escapes before it builds
HTML — so an input filter is a usability rule wearing a security badge. `<[^>]*>` treated "temp < 5 and
load > 3" as a tag while accepting the same words with the comparisons reversed. When tightening one,
prove the change against a corpus of real markup **and** real operator text, and assert both directions:
that markup is still refused, and that ordinary text is accepted by every write path.

**The wizard's client must not re-derive a decision the planner already made.** `IsSelectable` is the
planner's answer to "does ticking this do anything?" - so any client-side filter, badge or gate that
re-answers it by enumerating status names is a second implementation that drifts the moment a status is
added. It has already shipped once: "Only show what would change" tested `statusName === "Available"`
and so hid every linkable row, i.e. hid exactly the work it promised to show. **Prefer deleting the
duplicate over extending it.**

The same rule holds **inside** the server. When one decision has two implementations they drift, and
the drift is invisible until the two are compared: the wizard's target naming qualified a name when
the *selection* held more than one prefix of a VNet, while the annotation qualified when the *VNet*
did — so importing a two-prefix VNet one prefix at a time produced two rows with the same name and
disjoint ranges, and the annotation then offered a rename that would undo the qualifier. **A finding
that two code paths answer one question differently is a finding about the duplication, not about
which answer is right.** Fix it by deleting an implementation, and prefer the input that describes the
thing being named over the input that describes how the operator happened to click.

**A live Azure-linked descendant is not a reason to withhold.** It is Azure content, and deleting the
row archives it rather than destroying it — the operator then re-imports and gets it back under the
corrected range, which is the whole point of the delete-then-import loop. Withholding on that ground
buys nothing and costs the report: the range change is never mentioned, so the operator is never told
why the row was flagged and Bastet goes on asserting a range Azure does not have. **Manual content
remains the one refusal**, precisely because re-import cannot restore it.

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

**Reconcile does exactly two things, and a finding that grows it past them is wrong.**

1. **Report Azure resources that are gone**, so the operator can choose to delete the Bastet row.
2. **Report Azure resources whose range changed**, so the operator can choose to delete the Bastet row.

That is the whole feature. Everything it is *not* has been tried and removed:

- **It never edits a row.** Not the range, not the name, not the Azure link. There is no re-link.
- **It never re-adds anything.** After deleting a row whose range changed, the operator goes back
  through the **bulk import wizard** to bring in the current range. Reconcile does not create subnets,
  so a message about a changed range must point at the import wizard — anything else names a remedy
  reconcile does not offer, which is rule 3.
- **It never hunts for un-imported Azure space.** Discovering ranges Bastet does not record is the
  import wizard's job. Reconcile reporting them produces rows nobody can act on from that screen.
- **It decides on the linked resource alone** — the resource id on the row against Azure's current
  state. It does not consult other VNets, other subscriptions, or what else Bastet records.

**The resource id is the only join key, and that is why the refusal list is one item long.** Reconcile
asks exactly one question of Azure: *does the resource this row names still exist, and does it still
hold this range?* Both are answered by looking up `AzureResourceId`. The address range is never a
lookup key — it is only ever compared against **that same resource's own** current prefixes. Gone means
delete. The single exception is manual content, and it is an exception precisely because it is the one
thing the resource id cannot tell you about: Azure has no record of a hand-added child subnet or a host
IP, so deleting on Azure's word would destroy data Azure never knew existed.

**So any proposed withhold that reasons about a range rather than a resource id is wrong by
construction.** "The range showed up in another VNet", "another subscription still holds it", "the
prefix is still covered after the re-carve" — each asks a question reconcile does not ask, and each
answers it with a range match across resources. That is the exact shape of the four withholds rounds
14-17 added, and of `FindLiveOwnerOfRange`, which is deleted. If a finding needs cross-resource range
matching to justify itself, the finding is the defect.

Reconcile is small on purpose. **Prefer the finding that deletes machinery to the finding that adds a
case to it.**

**Reconcile and the bulk import wizard are clients of the IPAM, not privileged writers.** Every change they
make — delete, edit, add — goes through the same validation a manual operation goes through. They must
not write to the database directly to bypass a check, and must not carry their own copy of a rule that
lets them persist a state the validated path would reject. **Any Azure-driven write that reaches the
database without the base validation is a finding, whatever it was trying to achieve.** Where Azure's
state cannot be represented without breaking a validation rule, that is a conflict to report to the
operator — never a licence to write it anyway.

**Rule 1 is symmetric, and only one half of it used to be written down.** "Never report allocated space
as free" has a twin: **never pin an allocation record Azure says is gone.** A withheld deletion is not a
safe default — it leaves Bastet asserting that free space is allocated, indefinitely, with no operator
action that clears it. Weigh every proposed guard against **both** halves and say which one it serves.
While only the first half was recorded, four consecutive rounds widened a single withhold path nobody
had asked for and more than quadrupled the size of `AzureReconciler.cs`.

**If a capability ships, making it work correctly is in scope.** Bastet imports Azure subnets, so
multi-prefix subnets, top-ups and re-carves are in scope. "Feature change, not a bug fix", "the data
model does not support it" and "out of scope" are not verdicts a round may reach — they describe work,
not reasons to decline. One round used such a verdict and it shipped the bug for four more.

Since round 1 there have been **no intended product changes**. Everything filed is a defect against
behaviour the product already promises, either from the original implementation or introduced by a
previous round's fixes. There is no third category.

# The round exists to reduce defects, not to produce findings

**Measure the residue rate and lead with it.** Every finding names which previous-round fix it came out
of, or none. Across recent rounds most findings have traced back to the previous round's own fixes,
which means the fix process, not the codebase, has been the main defect source — and a round that does
not say so plainly in its first sentence has buried the most important thing it knows.

- **Report the defect, not the instance.** A finding naming one call site when the rule is wrong at
  three hands the reconcile step a fix that cannot close it. Name every site.
- **A proposed fix that would introduce a new defect is worse than no proposal.** Roughly half of all
  proposed fixes have been judged unsound on review, several of which would have shipped a new defect.
  That check is the most valuable thing verification produces.
- **A fix proposal must be narrow.** If closing a defect appears to need a component restructured, say
  so explicitly and separately — a restructure smuggled into a fix is where residue comes from.

# Audit the mechanism before proposing to extend it

**A previous round's fix that is already in the tree reads as settled design. It is not.** Four
consecutive rounds once widened the same withhold path, each round's finders reporting it as *still not
wide enough* — because it was in front of them and nothing invited them to ask whether it should exist.
Four rounds compounding in a direction the owner had never asked for.

So, whenever a candidate proposes **extending, widening or adding** a guard, withhold, refusal, special
case or status:

- **Name the contract it serves**, from *What Bastet is*. If you cannot point at the sentence, the
  finding is that the mechanism exists, not that it is incomplete.
- **Check who introduced it.** `git log -S` the identifying string. If a previous round added it, read
  that round's commit message: you are reading an opinion, not a requirement.
- **Prefer the finding that removes it.** "This mechanism is on the wrong axis" is a more valuable
  finding than "this mechanism has a gap", and only the first can end the cycle.

**Growth is evidence.** Report the line count of any component you file more than one finding against,
at this round's HEAD and at the previous audit commit. A component that has doubled across rounds while
the product's requirements did not change is being driven by the audit loop, and that belongs in the
round's headline.

**Check who can reach the remedy a message names, not just whether it is true.** A link is part of the
message: pointing an operator at a page that answers them with AccessDenied, or with a feature-disabled
403, breaks rule 3 exactly as a wrong sentence does. Where the same destination is already linked
elsewhere, copy that gating rather than inventing a second condition - the nav had this right sixteen
lines from a panel that had it wrong. When the link is suppressed, close the sentence as prose instead
of dropping the next step, or the reader is left with a warning and no move.

**A displayed count must match the range it is printed beside.** `AddressCount == EndIp - StartIp + 1`,
always. The free-space table broke this three different ways at once - one branch subtracted 1 from the
count, one subtracted 2, and one trimmed the end instead - because each was separately trying to express
"usable hosts" in a column labelled as a size. **When two questions are being asked of one number, the
defect is the single column, not the arithmetic in it.** Show both: the block's size, which is what a
subnet allocation uses and what a Create button must seed from, and **max usable IPs - the block minus
its own network and broadcast**, which is what you would get by allocating it. Key that off the block,
never off the parent: a first attempt subtracted the *parent's* reserved addresses, which put a
"usable host IPs" figure on a subnet whose own panel said it could not have host IP assignments at all.
A /31 gives 2 and a /32 gives 1, which falls out of the rule rather than being special-cased.

**The owner's product model outranks the finding's reasoning, and outranks yours.** A finding is one
round's read of the code; the owner knows what the product is for. When they contradict, the finding is
wrong by definition — record it struck or inverted, do not argue it through. In one round the owner
inverted four:

- a refusal the finding called a defect was correct, and only its *message* needed fixing, because
  imported rows must mirror Azure's containment;
- a withhold the finding wanted explained better should not have existed at all;
- a column the finding wanted picked one way became two columns answering two questions;
- and an "edge case" flagged for dropping was accepted, because pinning a record forever is not
  softened by being rare.

Each time the owner's answer was smaller, or truer to the product, than the filed fix. **If a fix
starts growing a mechanism, stop and put the product question to the owner in one line** — the filed
fix has often mis-framed the problem, and asking costs a sentence where implementing costs a round.

**The IP arithmetic lives in exactly one place — keep it there.** `IpUtilityService` is the only code in
the application that manipulates addresses as integers; every controller and validator calls into it.
That is worth defending: a finding that a *second* implementation has appeared is a real finding, and
the fix is to delete it rather than to reconcile the two. Two expressions of "usable addresses" once
coexisted - one keyed on a CIDR, one on a raw count - and agreed only by coincidence.

**Arithmetic is audited with properties, not with numbers.** A test that pins `254` passes while three
branches drift apart around it, which is exactly what happened. Assert the invariants instead: a mask
has `cidr` leading one-bits; broadcast is network + size - 1; a free range's count equals
`end - start + 1`; free ranges are disjoint, ordered, inside the parent, and never overlap an
allocation; and **free + allocated == total**, which is the conservation check that catches an
off-by-one anywhere in the walk. Then mutate the arithmetic and confirm the properties fail - a
property suite that survives an injected off-by-one is decorative.

**A subsystem must not re-derive a core rule.** When a component grows its own copy of IP arithmetic,
free-space calculation or containment, the finding is the duplication itself — not each place the copy
disagrees with `IpUtilityService`. One round filed three separate findings that were all a single
duplicated engine drifting.

# No questions, ever

The round asks for **inputs it cannot infer** — credentials, scale, git identity — once, up front, in a
single message. Nothing else.

**Never ask how to fix something, and never ask the owner to choose between fixes.** Both mean the round
has not understood the product. Where a fix implies a change to what the product does, file the finding
with the narrowest correct fix and state the product question *inside the finding* for the owner to read
afterwards. Do not block.

# No comments in source

`.cs` and `.cshtml` files carry none, by the owner's standing instruction. **Never file "this needs a
comment", a missing-doc or stale-comment observation, and never propose a fix whose substance is a
comment.** Refused on sight. If a rule needs explaining, the fix is a named method or a test that fails
when it is broken.

# You are the operator, not a spectator

Launch the workflow, then **watch it and intervene**. Do not launch and look away, and do not answer
status questions by pointing at `/workflows` — it does not exist in the VSCode extension. Merge agents
have stalled dead mid-run, and the round only finishes because someone is watching. Observation is
your job.

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

**Briefing agent** → `BRIEF.md`. **There will usually be no previous findings file** — `/audit-reconcile`
deletes them once reconciled, deliberately, so a round meets the code without inherited beliefs. Brief
from **git history** instead: `git log` since the last audit commit, and `git blame` where it matters.

If a findings file does exist, it means a previous round was never reconciled. Read it, and say so in
the brief.

**If no findings file exists at all**, this is round 1: letter `A`, file `docs/AUDIT-FINDINGS-1.md`,
no refuted table, no struck entries, no watch list. Say so explicitly in the brief so twenty finders
do not go looking for prior context that was never written, and brief against the full history instead
of "commits since the last round".

The brief must contain: the **round letter and number**, derived from the audit commits in `git log`
(`Audit round <N>:` subjects) — file number = previous + 1; a summary of **what the last reconcile
changed**, from its commit messages, so the regression beats know where to look; the sections "What
every finding must carry" and "Constraints on what counts as a finding" copied from this file, because
finders never see it; and — **the longest section** — a map of the codebase good enough to orient a
worker who has never opened it, since six of the eight beats audit the whole application.

Accepted and still open, never re-raised: ForwardedHeaders trust-all with `AllowedHosts: "*"`, the
Development-only `DevAuthHandler` bypass, `GlobalSanitizationFilter` skipping nested `System.*`
collections, `CollectDescendants` lacking a cycle guard, the unreachable IP-change branch in
`ValidateHostIpUpdate`, the blind `catch {}` around the DataProtectionKeys probe, and the bounded
race in the reconcile bulk delete — between Azure confirming a subtree deleted and the cascade
archiving it, a concurrent write can grow that subtree inside the contended lock window. It is
accepted, not unnoticed: Azure cannot be re-checked while holding the lock, the single-subnet delete
has the same confirm-then-cascade semantics, everything is archived rather than destroyed, and
closing it would make bulk stricter than single delete on a flow already behind a typed confirmation.
Re-verifying the target's own NetworkAddress/Cidr/ResourceId does **not** address it — the target is
unchanged; it is the subtree that grows.

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
terminal.

**Memory.** Sixteen concurrent agents, each with a build, container or browser. **Under 16 GB the host
dies mid-run.** Stop and name the figure.

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
`main`. Branching at commit time is not branching, and a round that did it put the findings commit
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
Azure resource created** — a teardown once reported success while removing nothing, because nothing
forced it to enumerate. The inventory is what Phase 5 deletes.

## Phase 2 — the beats, twice (20 agents + 1 merge)

**Six of these eight beats audit the WHOLE APPLICATION. Only beats 6 and 7 are scoped to the last
round's delta, and that is the only reason they exist.**

This is the mistake to avoid, and it is easy to make because beat 6 is where the highest-value findings
have historically come from: pointing every beat at what changed recently. Do not. An audit that only
re-examines the last round's diff is a regression check wearing an audit's name — it cannot find the
long-standing defect in `IpUtilityService`, and after enough rounds that is where the
remaining defects are. Beats 1-5 and 8 sweep their surface across the entire codebase and treat
recently-changed code on exactly the same terms as everything else: neither weighted nor exempt.

A beat prompt that names specific recent findings as "the focus" has been written wrong. Name the
surface, not the diff.


1. **Security / web** — authorization coverage, antiforgery, XSS, injection, SSRF, headers, log forging, secrets.
2. **Logic & data integrity** — subnet/CIDR arithmetic, containment and overlap, host-IP validation, and any path that persists a state the validated path would reject.
3. **Azure integration** — the bulk import wizard, its planner, and the reconciler. Highest stakes: the only code that *deletes* on the strength of what an external system reports. Work partial visibility hard — throttling, an empty page, a 403 on one group, a token expiring mid-enumeration, a paged response whose second page fails. Which of those does it treat as "absent, therefore delete"?
4. **Locking & lifecycle** — `sp_getapplock`, the migration lock, transaction boundaries, check-then-act, EF pooling.
5. **UI & client-JS** — the bulk import and reconcile wizards' state machines and emitted payloads. What gets POSTed is decided by `disabled` attributes, and jQuery's `.prop()` fires no `change`. Drive it in the browser; reading alone is near worthless here.
6. **Regression correctness** — every commit since the last audit, diffed against what it replaced. This beat, and only this beat, is deliberately scoped to the delta: the previous round's fixes are dense in defects, and a round's highest-value findings are routinely all residue of them. That density is why it gets a deep sweep — it is not a reason to point the other beats here.
7. **Regression tests** — do the tests added alongside those commits fail against the unfixed code? Revert the fix hunk in a scratch copy and find out.
8. **Dead code & refactor residue** — orphans from earlier deletions.

**Every worker prompt carries this:** write **nothing** into the repository directory — no PID files,
no logs, no scratch, no notes; everything goes under the rig directory. "Do not modify the working
tree" is not enough: beats have read it as "do not edit source" and left `.pid` files in the
root. One untracked file makes the tree dirty and Phase 5 refuses the commit. Also: own port, own
catalog, kill only by captured PID — never `pkill -f "Bastet.dll"`, which has killed other agents'
applications mid-run.

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
hallucinated defect reaches a human. This routinely kills a fifth to a quarter of all candidates.

A verifier may also change the answer rather than the confidence: kill a proposed *fix* while keeping
the finding, correct a severity, correct a citation. Those corrections go in the file.

**If a finding's own failure scenario opens with "not a runtime defect", it is refuted.** Every round
kills this same test-coverage-observation shape; it is not a defect report.

## Phase 4 — the scribe (2 agents, sequential)

One writes `docs/AUDIT-FINDINGS-<N>.md`. A second re-checks **every** citation against the working tree
and **fixes** what is wrong — stale line numbers and citations that moved are routine. A findings file
is correct only against the HEAD it was written at.

**Every finding names the previous-round fix it came out of, or says it came out of none.** One line
in the finding — the previous round's finding id, or nothing if it is independent. The scribe totals them and
opens the Verdict with the rate:

> Round `<N>` filed `<F>` findings, of which `<R>` are residue of round `<N-1>`'s own fixes.

When most findings are residue, the audit is not finding a rotten codebase — it is finding the fix
loop's own output. It is the single most useful number this round produces about *the process* rather
than the software, it costs one line per finding, and a falling rate is the only evidence that the loop
is converging. `/audit-reconcile` steps 5 and 6 exist to drive it down; this is how anyone can tell
whether they worked.

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
one file, `main` still points where the baseline said it did, and the tree is clean. A round once satisfied
none of the branch conditions and reported success anyway.

---

## What every finding must carry

- **File and line citation**, re-checked against the working tree.
- **Confidence: confirmed or plausible.** *Plausible* names the load-bearing step that could not be
  established. It is not a hedge.
- **A concrete failure scenario** with real inputs and the wrong output.
- **Evidence it was reproduced** — what was run, what came back.
- **A proposed fix**, plus a cheaper interim where one exists.
- **Attribution: which previous-round fix this is residue of**, by that round's finding id, or an
  explicit *none* if it is independent of the last round. Use `git log`/`git blame` on the cited lines
  to settle it rather than guessing. This is what the residue rate is totalled from, and it is the
  round's only measurement of whether the fix loop is converging.

Grouped by severity, numbered sequentially across the file, ordered within severity by consequence.

## Output structure

**The findings file is a work queue for `/audit-reconcile` and for the next round's briefing agent.
Both are machines. Nobody reads it as a report — write it accordingly.**

```
# Bastet — Round-<N> Audit Findings
branch / HEAD / test baseline / date / residue rate

# Critical / High / Medium / Low / Info
# Refuted — reported by a finder, killed by the verifier   (table, with reasons)
```

Each finding is a heading and **four fields, nothing else**:

```
## <this round's letter><n> — <one-line title> `[x2]`
**Where:** src/Bastet/Services/Azure/AzureReconciler.cs:757
**Breaks:** <real inputs, the wrong output, one short paragraph>
**Repro:** <what was run, what came back>
**Fix:** <the narrow change; note if a verifier judged the filed fix unsound>
**Residue of:** <previous round's finding id> | none
```

**No Verdict essay, no "How this audit ran", no funnel table, no watch list.** One round's file reached
181 KB and its value to either consumer would have survived at 15 KB. The one-line residue rate goes in
the header; the human summary goes in chat, not the file.

**No watch list, at all.** An item is settled into a finding or dropped. It was a graveyard: the missing
`[SafeText]` on `EditSubnetViewModel.Name` sat there unexamined for three rounds and only became a
finding when a beat finally drove it. If something is real, next round's finders will find it again.

**A round that finds nothing still writes and commits the file** — header, empty severity sections, and
the Refuted table, which is the whole content in that case and the part worth having.

## Constraints on what counts as a finding

- **Bastet is an open source tool anyone can host any way they like.** Plain-HTTP and air-gapped
  deployments must keep working. "Assumes HTTPS" is not a finding, and a fix that breaks those is a
  bad fix.
- **No literal control characters** — write `(char)0x1B`. Literals are invisible in diffs and get
  mangled through tool round-trips.
- **Migration `.Designer.cs` snapshots are frozen history.** Never report them as stale.
- **No novels.** A finding is a citation, a scenario, a reproduction, a fix.

## Severity is graded on consequence

- **A reproduced defect is filed at the severity its consequence warrants**, whatever the fix costs. Fix
  cost belongs in the Fix field, never in the severity and never in the decision to file.
- **Rarity does not reduce severity.** It is one sentence in the failure scenario. For an IPAM tool,
  *silently asserting an allocated range is free* is top-severity however narrow the path, because being
  the authority on that question is the product's entire purpose.
- **File it and rate it.** A finding the owner declines costs one line. A defect a round declines on
  their behalf has cost four rounds before.

**But grade honestly, and stop stacking.** A round once filed ten High and zero Critical, and the owner
downgraded or struck most of the Highs in minutes. The inflation came from one habit: attaching a
Rule-1 consequence to a finding whose actual defect is a string. Guard against it:

- **Grade the defect you can reproduce, not the worst thing downstream of it.** A wrong message is a
  wrong message. If the only harm you can demonstrate is that the sentence is untrue, it is **Low**.
- **If the fix is one string, the severity is Low.** No exceptions. Write the string fix and move on.
- **A contradiction the operator can see on the same screen is Low**, not High. A round once filed as
  High a message contradicted by the row rendered directly beneath it.
- **Same defect class, same severity.** Three findings that are all "the app names a remedy it does not
  offer" cannot be graded High, High and Low. Sort by class before you grade.
- **Critical means an operator loses or double-allocates real address space with no signal.** If no
  finding reaches that bar, the round has zero Critical, and that is a fine result to report.

**And stop writing essays.** Four fields. A Fix field is one to three sentences: the change, and a
named alternative if the obvious fix is unsound. One round produced 82 KB for 21 findings — the Fix
fields alone averaged 1.2 KB each and the owner read none of them. Under 25 KB for a full round, or
the round has confused volume with rigour.
