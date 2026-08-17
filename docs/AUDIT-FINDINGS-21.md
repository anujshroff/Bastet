# Bastet — Round-21 Audit Findings

Round 21 filed 4 findings, of which 4 are residue of round 20's own fixes. Branch `audit/round-21`, HEAD `6fb8557`, test baseline 866 passed / 0 failed / 0 skipped, date 2026-08-16, residue rate 4/4 (100%).

# Critical

# High

# Medium

# Low

## L1 — Whole-prefix Azure subnet over a never-imported unlinked row is badged "Already imported" `[x1]` `strings` — FIXED
_Fixed in this commit. AnnotateSubnet's fully-allocated encompassed branch link-gates AlreadyImported again (`IsSameVNet ? AlreadyImported : Blocked`), reason text unchanged; two regression tests (unlinked, other-VNet-linked) added to AzureBulkImportZeroWorkTests, both red before the code change._
_Swept: all other AlreadyImported assignments (planner 211-224, 233-241, 419) — each link-gated; client consumers (badge, hidden-row bucketing, renameOnlyCandidate, AnySubnetCannotBeImported) traced — only badge and bucketing change, both now truthful._
_Verified: new tests failed pre-fix, 868/868 post-fix, 0 warnings._
_Reviewed: pass — reviewer independently reverted the code (tests kept) and got exactly the 2 new failures; confirmed sibling-path consistency (exact-match 421, container 403-409) and §4 "'cannot import' stands" for unlinked/other-linked targets._
**Residue of:** 18-R4

## L2 — 18-R10's server-computed wizard selectability properties (CanCarrySubnetWork, both RenameOnlyCandidate) have no test at all — each mutates to a constant with the full 866-test suite green `[x2]` — FIXED
_Fixed in this commit. Three whole-domain tests added to AzureBulkImportSelectabilityTests, each sweeping every BulkImportAvailability value (× both bools for the rename pair) and asserting the property's full truth table._
_Swept: case-insensitive grep over src+test — the three properties are consumed only by _BulkScripts.cshtml (144/148/174/239); no other test references them._
_Verified: 871/871 green; author ran the finding's 3 constant mutations, each red._
_Reviewed: pass — reviewer ran 6 mutations (constants + wrong-term + dropped-conjunct variants), each red on exactly the intended test; confirmed the asserted contract matches the client consumers and §4's no-re-derivation rule._
**Residue of:** 18-R10

## L3 — Edit concurrency message: the Description, Tags and CIDR 'stored values that differ' branches are not mutation-load-bearing — deleting all three survives the suite green `[x1]` — FIXED
_Fixed in this commit. Three tests added to SubnetControllerConcurrencyRedisplayTests: all-three-differ (Description/Tags/CIDR clauses each asserted, Name silent), both-empty wordings, and empty-wording attribution (Description-only-empty with DoesNotContain("Tags"))._
_Swept: the route drives the same differing-list block a genuine DbUpdateConcurrencyException reaches (one shared implementation, reviewer-traced); no parallel copy exists._
_Verified: 8/8 on the file, 875/875 full; author mutation (all three branches deleted) red; reviewer ran 5 finer single-fault mutations, each red._
_Reviewed: pass — one disclosed residual (symmetric swap of the two empty literals) taken as a strengthening: the attribution test was added and the swap mutant confirmed red._
**Residue of:** 18-R3-rem

## L4 — 18-R3-rem's HostIp concurrency-message alignment has no guarding test — reverting the hunk leaves all 866 tests green `[x2]` — FIXED
_Fixed in this commit. New HostIpEditConcurrencyCatchTests drives the line-294 catch via a SaveThrowingBastetDbContext (armed one-shot DbUpdateConcurrencyException from SaveChangesAsync after validation passes on a matching RowVersion) and pins the full aligned message._
_Swept: validation-branch message (248-250) confirmed already aligned and separately exercised by the existing SubnetHostIpInteractionTests assertion._
_Verified: 875/875 full suite; reverting the 6a21950 hunk in a scratch copy fails exactly the new test; reviewer's inverse probe (mutate only the validation-branch tail) leaves the new test green, proving it binds the catch._
_Reviewed: pass — reviewer noted an incremental build masks the revert (stale Bastet.dll); clean rebuild required to reproduce the red._
**Residue of:** 18-R3-rem

# Info

# Refuted

| id | title | reason |
|---|---|---|
