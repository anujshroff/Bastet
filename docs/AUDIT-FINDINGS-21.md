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

## L3 — Edit concurrency message: the Description, Tags and CIDR 'stored values that differ' branches are not mutation-load-bearing — deleting all three survives the suite green `[x1]`
**Where:** src/Bastet/Controllers/SubnetController.Edit.cs:242, plus src/Bastet/Controllers/SubnetController.Edit.cs:249 and src/Bastet/Controllers/SubnetController.Edit.cs:256
**Breaks:** 18-R3-rem's ledger claim is 'one unified message names each differing stored value', but only the Name clause is pinned. Delete the Description branch (lines 242-247), the Tags branch (249-254) and the CIDR branch (256-259) and the suite stays 866/866 green: an operator whose edit collided with another user's Description/Tags/CIDR change gets a message naming nothing that differs, telling them to 're-apply the changes that still make sense' with no way to see which values moved — the exact defect the fix closed, back, with a green suite. The 'is now empty' wording forms are likewise unreachable by any test.
**Repro:** Finder, in a clone at 6fb8557: removed the three branches from the differing-list construction, rebuilt, full suite → 866 total, 0 failed. SubnetControllerConcurrencyRedisplayTests only drives a Name difference (Assert.Contains("Name is now 'web'")) and asserts DoesNotContain("Tags") on a Name-only conflict; reverting the whole Edit.cs fix does fail all 3 new tests, so the tests guard the unified-path mechanism — just not three of its four value clauses. Both verifiers reran the exact mutation in fresh clones → 866/866 green; grep across the test tree finds zero references to "Description is now", "Tags are now", "CIDR is now" or "is now empty"; git blame/git log -S attribute all three branches solely to 6fb8557; verifier confirmed the CIDR clause and both empty-wording forms are reachable through the same redisplay path the existing tests drive (including the stale-token-on-validation-failure route).
**Fix:** Extend Edit_POST_ConcurrencyConflict_NamesTheStoredValuesThatDiffer (or add one sibling case) with a stored row whose Description, Tags and Cidr all differ from the posted form, asserting "Description is now", "Tags are now" and "CIDR is now /" each appear, plus one case asserting the "Tags are now empty" form.
**Residue of:** 18-R3-rem

## L4 — 18-R3-rem's HostIp concurrency-message alignment has no guarding test — reverting the hunk leaves all 866 tests green `[x2]`
**Where:** src/Bastet/Controllers/HostIpController.cs:294
**Breaks:** Round 20 aligned the HostIp Edit DbUpdateConcurrencyException message with the unified Subnet Edit message ('...Reload the page to see the current values, then re-apply the changes that still make sense.'). The only test touching a HostIp concurrency message (SubnetHostIpInteractionTests.cs:681) asserts Contains("modified by another user"), a substring the pre-fix message ('The host IP was modified by another user. Please reload and try again.') also contains — and its stale-RowVersion setup drives the validation branch (line 248), not the line-294 catch. A regression restoring the old short message at line 294, silently un-aligning the sibling that 18-R3-rem aligned, ships with the suite green.
**Repro:** Finder, in a scratch clone of 6fb8557: `git checkout 6a21950 -- src/Bastet/Controllers/HostIpController.cs` (reverts exactly the delta's one HostIp hunk), dotnet test → 866 passed, 0 failed, 0 skipped; grep "host IP was modified" over test/ → no hits. Verifier reran the revert in a fresh clone (git diff --stat: 1 insertion, 3 deletions) → 866/866 green; confirmed the delta's HostIpController.cs diff is exactly the one catch-message hunk, that the validation-path message (line 248-250) was already aligned at 6a21950, and that the sole existing assertion exercises that validation path, not the catch.
**Fix:** Add a test that drives the DbUpdateConcurrencyException catch itself with the row still existing (e.g. a context/interceptor that throws DbUpdateConcurrencyException from SaveChangesAsync after HostIpExists returns true) and pin the aligned sentence, e.g. Assert.Contains("then re-apply the changes that still make sense", message). The verifier established that strengthening only the existing SubnetHostIpInteractionTests.cs:681 assertion would not fail on this revert — that test exercises the validation-path message, already aligned pre-delta — so the new catch-path test is the part that closes the gap.
**Residue of:** 18-R3-rem

# Info

# Refuted

| id | title | reason |
|---|---|---|
