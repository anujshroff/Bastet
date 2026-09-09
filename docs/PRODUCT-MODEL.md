# Bastet Product Model

This file is the single authority on what Bastet is and what counts as a defect. It is read by
every audit finder, verifier, briefing agent, reconcile fixer and fix reviewer. Neither skill file
carries its own copy of anything written here — two copies drift, and drift in this document costs
rounds.

Maintenance rules:

- Sections 1–7 change only when the owner's rulings require it, and the change rides a round's own
  commits like any other.
- Section 8 is append-only and carries the owner's strike/approval text **verbatim** — never an
  agent's paraphrase of it. A wrong paraphrase written here becomes canon that outranks correct
  reasoning; four rounds were once lost to a single wrong sentence in an inherited file.
- Wherever a ruling is testable, the entry points at the counter-test that enforces it. A rule in
  prose must be re-derived by every agent that reads it; a rule in a test enforces itself.

## 1. The product in four rules

**Bastet is an IPAM tool. Its job is to be the authority on which IP space is allocated and which is
free, and to let an operator manage that space.** Every judgement resolves against that, in this
order:

1. **Never report allocated space as free.** The worst output the product can produce. Reporting
   free space as allocated is second-worst.
2. **Never destroy an allocation record on incomplete information.** Archives are irreversible.
3. **The operator must be able to act on what they are told.** A message naming a remedy the app
   refuses is a defect.

**Rule 0, which overrides all three: Bastet is the authority, and it answers from its own records.**
Free means free *according to Bastet*. **Azure is not authoritative in Bastet at all** — it is a
source you import *from*. Azure space becomes real to Bastet by being imported, and until it is
imported it does not exist as far as Bastet is concerned. Reconcile does not change this — it
*reports* that a linked Azure resource is gone or re-ranged so the **operator** can decide. Azure
never decides anything.

**Rule 1 is symmetric.** "Never report allocated space as free" has a twin: **never pin an
allocation record Azure says is gone.** A withheld deletion is not a safe default — it leaves Bastet
asserting that free space is allocated, indefinitely, with no operator action that clears it. Weigh
every proposed guard against **both** halves and say which one it serves. While only the first half
was written down, four consecutive rounds widened a single withhold path nobody had asked for and
more than quadrupled the size of `AzureReconciler.cs`.

**One flat, routable space.** Bastet manages a single IP space in which everything is routable
against everything else, so the same range must never be allocated twice — preventing that collision
is the product's reason to exist. Consequences:

- **"Still allocated" is a question about the whole managed space, never about provenance.** If any
  live resource holds a range, that range is in use, whichever VNet, subscription or import it came
  from. Overlapping Azure VNets are not a case to defend — inside Bastet's model that overlap *is*
  the collision, not a legitimate configuration.
- **How the space is carved up is the operator's choice, and only theirs** — by hand, by Azure
  import, or both. Neither origin is privileged: a rule that holds for a manually created subnet
  holds identically for an imported one, and vice versa.
- **One Azure range is one Bastet row.** A VNet with a single address prefix whose single subnet
  covers that whole prefix is **one** row, not a VNet parent plus a byte-identical child. The import
  marks that row fully allocated instead of creating the duplicate. A parent and child with the same
  CIDR *are* the collision the product exists to prevent, and the second row tracks no free space.

## 2. The two comparison surfaces

**Azure state and Bastet state are compared in exactly two places, and nowhere else:**

| surface | the only question it asks |
|---|---|
| the bulk import wizard | **can this be added?** |
| reconcile | **can this be deleted?** |

That is the entire Azure/Bastet arithmetic in the product. Every other screen — the subnet tree,
Details, unallocated ranges, host IPs, search — answers from Bastet's records alone and must never
consult Azure. **A finding or fix that brings Azure state into any surface outside those two is out
of bounds by construction**, whatever it claims to have found.

So this whole shape of finding is **invalid and must never be filed — and if filed, is struck, never
implemented**:

> "Bastet shows 10.20.9.32 as free, but an Azure subnet Bastet never imported holds it."

That is not a defect. It is the product working. Rule 1 governs Bastet's *own* records disagreeing
with each other or with a resource Bastet is linked to — nothing else. Corollaries, each of which
has killed a filed finding:

- A wizard filter that hides a row which **cannot be imported** is correct — ticking it would change
  nothing, which is exactly what the filter means.
- Reconcile returning a clean scan over a partially imported subscription is correct.
- "The operator might allocate over Azure space" is not a consequence you may escalate on. They ran
  the import wizard or they did not.

## 3. Reconcile's contract

**Reconcile does exactly two things, and anything that grows it past them is wrong.**

1. **Report Azure resources that are gone**, so the operator can choose to delete the Bastet row.
2. **Report Azure resources whose range changed**, so the operator can choose to delete the Bastet
   row.

That is the whole feature. Everything it is *not* has been tried and removed; do not restore any of
it under a new name:

- **It never edits a row.** Not the range, not the name, not the Azure link. There is no re-link.
- **It never re-adds anything.** After deleting a row whose range changed, the operator goes back
  through the **bulk import wizard** — a message about a changed range must point there; anything
  else names a remedy reconcile does not offer, which breaks rule 3.
- **It never hunts for un-imported Azure space.** That is the import wizard's job; rows reconcile
  reports that way cannot be acted on from the reconcile screen.
- **It decides on the linked resource alone** — the resource id on the row against Azure's current
  state. It does not consult other VNets, other subscriptions, or what else Bastet records.

**A row carrying an Azure resource id is a record the operator asked Bastet to keep in step with
Azure.** Azure is still not authoritative — but for *that row* the operator has said "track this",
so when Azure no longer has the resource, or no longer holds the recorded range, **reconcile must
say so and offer the delete**. It reports; the operator decides. Silently withholding the report is
its own defect: it leaves Bastet asserting an allocation the operator was never told to reconsider.

**The resource id is the only join key, and that is why the refusal list is one item long.**
Reconcile asks exactly one question of Azure: *does the resource this row names still exist, and
does it still hold this range?* Both are answered by looking up `AzureResourceId`. The address range
is never a lookup key — it is only ever compared against **that same resource's own** current
prefixes. Gone means delete. The **only** legitimate reason to withhold is **manual content in that
hierarchy — a hand-added child subnet, or a host IP** — because that is the one thing the resource
id cannot tell you about: Azure has no record of it, so deleting on Azure's word would destroy data
Azure never knew existed. Nothing else qualifies.

**Any withhold that reasons about a range rather than a resource id is wrong by construction.** "The
range showed up in another VNet", "another subscription still holds it", "the prefix is still
covered after the re-carve" — each asks a question reconcile does not ask, and answers it with a
range match across resources. If a finding or fix needs cross-resource range matching to justify
itself, the finding is the defect.

**A live Azure-linked descendant is not a reason to withhold.** It is Azure content, and deleting
the row archives it rather than destroying it — the operator re-imports and gets it back under the
corrected range, which is the whole point of the delete-then-import loop. **Manual content remains
the one refusal**, precisely because re-import cannot restore it.

**Machinery that has been deliberately deleted — never rebuild it, under any name:** the
rounds-14–17 withhold paths and `FindLiveOwnerOfRange` (cross-resource range matching), the
reconcile **re-link** flow (`RelinkAzureSubnet` and its suggestion UI), the **inbound verdict**
(reporting Azure ranges no Bastet subnet records), **VNet prefix coverage** reporting, and the
single-VNet import wizard (`Views/Azure/Import/*` — the bulk wizard is the import surface). Each
closed a real-looking failure scenario, and each was removed because it answered a question the
product does not ask. Reconcile is small on purpose. **Prefer the change that deletes machinery to
the change that adds a case to it.**

## 4. The bulk import wizard's contract

**The wizard must not offer work that is not work.** A VNet prefix already linked to its Bastet
subnet, with every Azure subnet under it already recorded, is `AlreadyImported` and **not
selectable** — there is nothing to add. So is a collapsed target this same VNet has already marked
fully allocated. Two things still count as work and must stay offered: **linking a target that is
not yet linked**, and **renaming** when the operator has asked for renames.

**Renaming is gated on the Azure link, and on nothing else.** When the operator asks for renames,
the wizard offers a rename for **both** the VNet target row **and** every already-imported child
subnet whose Bastet name has drifted from its Azure name — the control says "subnets" and must mean
it, since nothing else in the product can bring a drifted child name back into step. The single
condition is that the row **already carries the Azure resource id** it is being renamed to match:

- linked, and the name differs → offer the rename, and perform it
- **not** linked, or linked to a different Azure resource → **"cannot import" stands, and no rename
  is ever performed** — range matching is not enough, because an unlinked row is operator-owned data
  that this Azure resource has no claim on
- fully allocated makes no difference to a rename *on its own*: a rename creates nothing inside the
  target, so a linked fully-allocated row is renameable. It stays refused the moment the same
  selection would also create a subnet inside it, which is a real conflict.

"Only show what would change" hides exactly the rows that would change nothing, which is only
correct while those cases are classified correctly.

**A row the operator built by hand can be adopted by the VNet it matches.** Linking a not-yet-linked
target is real work and stays offered **whether or not that row already has children** — the
operator may well have carved the space by hand first and now want Bastet to track it against Azure.
Having children is not a conflict, and refusing on it keys on *provenance*, which is never a reason:
the identical shape with the link already present is advertised as "Will add any missing subnets".
The refusals that remain are the real conflicts, decided **per subnet, not per prefix**: an Azure
subnet that would contain, or be contained by, an existing Bastet row is refused by name, while its
clean siblings still import and the operator's own subnets are left untouched and unlinked. The
correct outcome for a hand-built tree that partly overlaps Azure is **partial adoption**, never a
whole-prefix refusal.

**An imported row's place in the tree mirrors Azure's containment.** Azure has no subnet nesting — a
subnet belongs to a VNet, flat — so a row carrying an `AzureResourceId` hangs directly off the row
representing its VNet. **A hand-made subnet may not sit between a VNet row and its Azure subnets.**
Refusing that is correct, and resolving the Azure subnet under the hand-made middle-man is wrong
however well it reads: nothing breaks functionally, but the tree would assert a containment Azure
never had and nobody reading it could tell which level was real. This is the one place the manual
and imported paths legitimately differ, and it does **not** contradict "a rule that holds for a
manually created subnet holds identically for an imported one" — the manual form is free to carve
anything, because a hand-made row claims to represent nothing but itself. What the refusal owes the
operator is the rule and a remedy, not just the fact: name the row in the way, say Azure has no such
subnet, and say to delete or re-carve it.

**The wizard's client must not re-derive a decision the planner already made.** `IsSelectable` is
the planner's answer to "does ticking this do anything?" — any client-side filter, badge or gate
that re-answers it by enumerating status names is a second implementation that drifts the moment a
status is added. It has already shipped once: "Only show what would change" tested
`statusName === "Available"` and so hid every linkable row — exactly the work it promised to show.
**Prefer deleting the duplicate over extending it.**

## 5. Invariants that decide findings and fixes

- **When one decision has two implementations they drift**, and the drift is invisible until the two
  are compared. A finding that two code paths answer one question differently is a finding about the
  duplication, not about which answer is right. Fix it by deleting an implementation, and prefer the
  input that describes the thing being named over the input that describes how the operator happened
  to click. (The shipped example: target naming qualified on the *selection*, the annotation on the
  *VNet* — two rows with the same name and disjoint ranges, plus a rename offer that would undo the
  qualifier.)
- **Every write path for a field must accept and refuse exactly the same input.** Create, Edit, the
  bulk import commit and any API caller are siblings: a rule on one and not the others produces a
  tree the app itself populated but its own form will not re-enter. Check the attribute sets side by
  side, not the error messages — the divergence that shipped was `[SafeText]` on the Create view
  model and not the Edit one.
- **A validation rule that refuses ordinary operator text is a defect, not caution.** Output
  encoding is what makes the app safe — Razor encodes at every sink and the wizard's client escapes
  before it builds HTML — so an input filter is a usability rule wearing a security badge.
  `<[^>]*>` treated "temp < 5 and load > 3" as a tag. When tightening one, prove the change against
  real markup **and** real operator text, in both directions, on every write path.
- **Reconcile and the bulk import wizard are clients of the IPAM, not privileged writers.** Every
  change they make goes through the same validation a manual operation goes through. **Any
  Azure-driven write that reaches the database without the base validation is a defect, whatever it
  was trying to achieve.** Where Azure's state cannot be represented without breaking a validation
  rule, that is a conflict to report to the operator — never a licence to write it anyway.
- **The IP arithmetic lives in exactly one place.** `IpUtilityService` is the only code that
  manipulates addresses as integers; every controller and validator calls into it. A *second*
  implementation appearing is a real finding, and the fix is to delete it, not reconcile the two.
  A subsystem growing its own copy of IP arithmetic, free-space calculation or containment is one
  finding about the duplication, not one finding per disagreement.
- **Arithmetic is audited with properties, not with numbers.** A test that pins `254` passes while
  three branches drift apart around it. Assert the invariants: a mask has `cidr` leading one-bits;
  broadcast is network + size − 1; a free range's count equals `end − start + 1`; free ranges are
  disjoint, ordered, inside the parent, and never overlap an allocation; **free + allocated ==
  total**. Then mutate the arithmetic and confirm the properties fail — a property suite that
  survives an injected off-by-one is decorative.
- **A displayed count must match the range it is printed beside.** `AddressCount == EndIp − StartIp
  + 1`, always. When two questions are being asked of one number, the defect is the single column,
  not the arithmetic in it: show the block's size (what a subnet allocation uses and a Create button
  seeds from) and **max usable IPs** (the block minus its own network and broadcast) separately. Key
  the latter off the block, never off the parent. A /31 gives 2 and a /32 gives 1, falling out of
  the rule rather than being special-cased.
- **Check who can reach the remedy a message names, not just whether it is true.** A link is part of
  the message: pointing an operator at a page that answers with AccessDenied or a feature-disabled
  403 breaks rule 3 exactly as a wrong sentence does. Where the same destination is already linked
  elsewhere, copy that gating rather than inventing a second condition. When the link is suppressed,
  close the sentence as prose instead of dropping the next step.
- **If a capability ships, making it work correctly is in scope.** Bastet imports Azure subnets, so
  multi-prefix subnets, top-ups and re-carves are in scope. "Feature change, not a bug fix", "the
  data model does not support it" and "out of scope" describe work, not reasons to decline. One
  round used such a verdict and it shipped the bug for four more.
- **Tests serve the product, never the other way around. The audit files product defects only,
  and no test finding of any kind** - not a missing pin, not a weak pin, not an unrecorded gap.
  Test quality is reconcile's duty at fix time: every fix ships with the test that fails against
  the unfixed code, proven the reconcile way (the full revert reds the suite, or the recorded
  `/e2e` drive where no unit seam exists, and the defect is visibly back in the running build).
  Where no seam exists (client-JS partials, framework internals, live Azure), the surface is
  recorded once - in the fix's ledger row, which is durable, and as `/e2e` coverage, which is
  executable; a FIXED entry is neither, because the findings file is deleted at close-out.
  Reconcile's independent fix review and its whole-diff gate enforce the proof and repair a fix
  shipped without it in the same round; the next audit never inherits it as a finding.
- **The loop's terminal state is a zero-finding round, and every round must move toward it.** A
  finding is an operator-visible wrong behaviour reproducible at HEAD; nothing else. A fix is the
  minimal change that makes the wrong
  behaviour right, plus its pin or its record - no hardening, no widening, no "while we're here"
  work for later rounds to audit.
- **Since round 1 there have been no intended product changes.** Everything filed is a defect
  against behaviour the product already promises — from the original implementation or introduced by
  a previous round's fixes. There is no third category.

## 6. Standing constraints on code and fixes

- **Bastet is an open source tool anyone can host any way they like.** Plain-HTTP and air-gapped
  deployments must keep working. "Assumes HTTPS" is not a finding, and a fix that breaks those
  deployments is a bad fix. No fix may assume a reverse proxy or outbound internet.
- **No comments in `.cs` or `.cshtml` files**, by the owner's standing instruction, and do not
  restore removed ones. If a rule needs explaining, the fix is a named method or a test that fails
  when it is broken — never a comment, a missing-doc finding, or a stale-comment observation.
- **No literal control characters in source** — write `(char)0x1B`. Literals are invisible in diffs
  and get mangled through tool round-trips.
- **Migration `.Designer.cs` snapshots are frozen history.** Never report them as stale.

## 7. Accepted findings — never re-file

Accepted and still open, deliberately: ForwardedHeaders trust-all with `AllowedHosts: "*"`, the
Development-only `DevAuthHandler` bypass, `GlobalSanitizationFilter` skipping nested `System.*`
collections, `CollectDescendants` lacking a cycle guard, the unreachable IP-change branch in
`ValidateHostIpUpdate`, the blind `catch {}` around the DataProtectionKeys probe, and the bounded
race in the reconcile bulk delete — between Azure confirming a subtree deleted and the cascade
archiving it, a concurrent write can grow that subtree inside the contended lock window. It is
accepted, not unnoticed: Azure cannot be re-checked while holding the lock, the single-subnet delete
has the same confirm-then-cascade semantics, everything is archived rather than destroyed, and
closing it would make bulk stricter than single delete on a flow already behind a typed
confirmation. Re-verifying the target's own NetworkAddress/Cidr/ResourceId does **not** address it —
the target is unchanged; it is the subtree that grows.

## 8. Decided questions — owner rulings, verbatim only

Append-only. Each entry: the round and finding id, the owner's words **verbatim**, and the
counter-test that enforces the ruling (or `untestable` with one line why). No agent paraphrase, no
summary, no reasoning added. Rulings made before this file existed are already folded into sections
1–7 and are not re-listed here.

- **18-R2** — asked whether the delete-scope guard should refuse on any host-IP churn (posting the
  reviewed IP set) or stay count-based, accepting that a delete-then-add netting the count equal
  during the review window can slip. Owner: "Accept the count (Recommended)". Counter-test:
  `DeleteConfirmed_AHostIpAddedByAClockBehindWriter_StillRefusesTheDelete` pins the half that must
  refuse; the accepted swap residual is deliberate and must not be re-filed as a finding.

- **26 (round-wide)** — on the finding classes the audit loop generates from its own fixes. Owner:
  "at the end you need to update the skill to stop finding bullshit problems"; "this shit overall
  needs to be such that we reduce shit found to 0, not go in fucking circles"; "so be sure the
  logic behind how the audit and reconcile work support that"; "after you make the skill change,
  apply it to this fucking audit". Enforced by the two convergence bullets in §5 (a finding is an
  operator-visible wrong behaviour reproducible at HEAD; the loop's terminal state is a
  zero-finding round). Counter-test: untestable (process rule, no code seam).
- **26 (round-wide, second ruling)** — refining the first. Owner: "sigh, im so tired. i dont want
  to ignore fucking test issues"; asked why prior rounds left the gaps, the record-keeping hole was
  identified (unpinnable-fix records lived in the FIXED entry, which close-out deletes, and the
  `/e2e` fallback was never enforced). Folded into §5: test gaps close at fix time — pin where a
  seam exists, otherwise record once in the ledger row and `/e2e`; recorded gaps are never
  re-filed; the audit files only broken-rule test findings. Counter-test: untestable (process
  rule, no code seam).
- **29 (round-wide)** — on test-only findings, after round 29 filed five of them (all round-28 pins)
  as its entire output. Owner: "i only care about the fucking producti dont care about the tests";
  "the test serve to make the product betternot the other way around"; "i need this stupid shit to
  not pop up in the audit itself"; "its a waste of fucking tokens". Folded into §5: the audit files
  product defects only and no test findings; test quality is reconcile's fix-time duty under its
  proof rule, enforced by its fix review and whole-diff gate. Counter-test: untestable (process
  rule); the audit skill's beat list no longer has a regression-tests beat.
