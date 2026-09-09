# Bastet — Round-29 Audit Findings

- Branch: `audit/round-29`
- HEAD: `c38d031405e9a75a53917eb9289914272cd920b9`
- Test baseline: 969 passed, 0 failed, 0 skipped, 0 build warnings
- Date: 2026-09-08
- Scope: Regression-only over 8891320..HEAD (round 28 residue 4 > 2)
- Beat structure: beats 6 and 7 × 2 passes
- Funnel: 7 raw → 5 merged (2 x2, 3 x1) → 5 survived, 0 refuted, 0 dropped at merge
- Round 29 filed 5 findings, of which 5 are residue of round 28's own fixes.
- `test/Bastet.Tests/SubnetManagement/SubnetDetailsModalScriptTests.cs` carries L3 and L5: 137 lines at HEAD, 101 lines at 8891320.

# Critical

None.

# High

None.

# Medium

None.

# Low

## L1 — HostIpViewSourceTests binds incidental tokens, not the rendering: a conditional wrapper on Edit/_Header brings back the empty 'Error' alert with the pin green (and a hand-rolled second rendering on Create also passes) `[x2]` — FIXED

_Fixed on audit/round-29 (test-only). HostIpViewSourceTests now asserts exactly two model-level renderings across every view a HostIp page can compose from (Views root, Views/Shared and Views/HostIp, recursively) and, per page, that the one summary is the unwrapped tag-helper line carrying the alert classes itself._
_Swept: Subnet Create and Edit each render model-level errors exactly once (no sibling production defect); the deleted ModelOnlySummaries helper had no other reference._
_Verified: build 0 warnings, suite green; pin red under the four filed mutants (wrapper, hand-rolled partial, second tag helper, dismissible wrapper), the 8891320 revert, and the reviewer's root-partial, Shared-partial and _Layout wrappers; the reviewer drove each shape live and saw the empty 'Error' alert or the doubled model-level error return._
_Reviewed: first review (a) — a wrapper placed outside the page's own folder passed the pin; revised once to the widened read set; re-review PASS. Not done, (c) under §5: two controller-carried duplicate channels (TempData alert partial, ViewBag banner) that no view-source pin can see; the reviewer's optional two-line redisplay assertion is hardening and was declined._

## L2 — 28-L2 redisplay pin does not bind LastModifiedAt; a helper that drops it shows "Last Modified: Never" and stays green `[x2]` — FIXED

_Fixed on audit/round-29 (test-only). HostIpEditRedisplayTests seeds LastModifiedAt/ModifiedBy, captures the stored stamp from the AsNoTracking read-back, and asserts it on all three redisplay tests (renamed ...AndDates)._
_Swept: SubnetControllerConcurrencyRedisplayTests.cs:53,82 already binds LastModifiedAt for the subnet card; no other redisplay pin lacks it._
_Verified: build 0 warnings, suite green; pin green at HEAD, 3/3 red under the dropped L317 stamp, 2/3 red under the 8891320 controller revert; reviewer's five further helper regressions (posted-value stamp, swapped stamps, dropped Include, FindAsync reload, tracked reload) all red or caught by the suite except the recorded DbUpdateConcurrencyException site._
_Reviewed: PASS. Note adopted as fact, not fix: HostIpEditConcurrencyCatchTests already has a SQLite seam (SaveThrowingBastetDbContext) for the catch the 28-L2 row called seamless, so that site is pinnable at fix time if ever reopened._

## L3 — 28-L10's pin checks the refusal sentence's presence, not which refuse() branch carries it; a branch swap stays green `[x1]` — FIXED

_Fixed on audit/round-29 (test-only), by owner instruction after an initial strike. CidrModalScript_OnlyIndexesTheServerSuggestionTable binds each refusal sentence to its refuse() branch with two regexes._
_Swept: the sibling refusal branch is bound only by its strict-equality condition, not its wording, after the gate's product-model reviewer flagged the wording pin as beyond the 28-L10 row._
_Verified: build 0 warnings, suite green; pin red under the branch swap, the == undefined dead-condition mutant and the 8891320 script revert; the same assertions were validated by three independent audit verifiers against the same mutants and a live browser drive._
_Reviewed: by the audit's three verifiers (the filed assertions are byte-identical to theirs); no separate reconcile reviewer, by owner instruction on token spend._

## L4 — 28-L4 sidebar pin passes with IsAzureLinked read backwards (linked row told its CIDR is modifiable) `[x1]` — FIXED

_Fixed on audit/round-29 (test-only). SubnetEditViewSourceTests binds each sidebar sentence to its own IsAzureLinked branch with two regexes, counts the four pinned strings exactly once, and binds the form's readonly input to the form's linked branch._
_Swept: the Edit form's own IsAzureLinked branch was the one sibling of the same shape (token presence only) and is now bound; no other view-source pin was widened._
_Verified: build 0 warnings, suite green; pin red under both-site inversion, each single flip, the rules-block hoist, the 8891320 revert, the form inversion, the form content swap, a ViewData predicate, and four added-duplicate regressions; green under a whitespace reflow and an attribute reorder._
_Reviewed: first review (a) — added duplicates of a sentence outside its branch passed the pin; revised once with exactly-once counts and a looser form regex; re-review PASS. Non-blocking (c): a paraphrase outside the pin's vocabulary and a readonly→disabled tidy remain uncovered/false-red respectively, hardening by the reviewer's own account._

## L5 — CidrModalScript_RefuseResetsTheNetworkAddressToTheRangeStart accepts a reset gated inside refuse(); the 28-L9 defect returns with the pin green `[x1]` — FIXED

_Fixed on audit/round-29 (test-only), by owner instruction after an initial strike. CidrModalScript_RefuseResetsTheNetworkAddressToTheRangeStart pins the two reset statements at the head of refuse() positionally._
_Verified: build 0 warnings, suite green; pin red under the braced gate, the keepAddress-flag gate and the 8891320 script revert; validated by two independent audit verifiers against the same mutants and a live browser drive._
_Reviewed: by the audit's two verifiers (assertion byte-identical to theirs); no separate reconcile reviewer, by owner instruction on token spend._

# Info

None.

# Refuted — reported by a finder, killed by the verifier

| id | title | where | reason |
|---|---|---|---|

None.
