# Bastet — Round-22 Audit Findings

Round 22 filed 2 findings, of which 2 are residue of round 21's own fixes. Branch `audit/round-22`, HEAD `85b1819`, test baseline 876, date 2026-08-16, residue rate 2/2 (100%).

# Critical

# High

# Medium

# Low

## L1 — Case-insensitivity term of the 21-L1 same-VNet gate is unpinned: IsSameVNet Ordinal mutant survives the full 876-test suite `[x2]`

**Where:** src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:17; also src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:204 (isTopUp consumes the same unpinned IsSameVNet term), src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:550 (plan-side host-IP refusal consumes the same term), src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:197 (inline OrdinalIgnoreCase other-VNet-link compare, equally uncovered by any case-divergent fixture), src/Bastet/Services/Azure/AzureBulkImportPlanner.cs:565 (inline OrdinalIgnoreCase replace-link error compare, same absence)

**Breaks:** A Bastet VNet-target row is linked and marked fully allocated, with its stored AzureResourceId differing from ARM's currently-listed VNet id only in casing (e.g. `/subscriptions/TEST/.../virtualNetworks/vnet-c` stored vs `/subscriptions/test/...` listed — real across Azure SDK/API-version upgrades, which have historically varied segment casing such as `/resourcegroups/`). If the comparison mode in IsSameVNet regresses to case-sensitive (Ordinal, or any re-implementation that compares bytes), the 21-L1 branch at AzureBulkImportPlanner.cs:338 classifies the whole-prefix Azure subnet Blocked instead of AlreadyImported — re-shipping exactly the badge falsehood 21-L1 fixed, and additionally dropping the rename offer because RenameOnlyCandidate requires Status==AlreadyImported — and the entire test suite stays green, so the regression ships unseen. The same untested term also feeds isTopUp (line 204) and the plan-side host-IP refusal (line 550).

**Repro:** Finder: clone at 85b1819; before every verdict rm -rf all bin/obj + dotnet build-server shutdown, then full `dotnet test`. Baseline 876/876 green. Mutant (line 17 StringComparison.OrdinalIgnoreCase -> Ordinal): 876/876 GREEN. Harness sensitivity proven by 8 sibling mutants/reverts all going red on exactly the predicted delta tests (M1 revert of the 21-L1 gate -> both new ZeroWork tests red; M2 always-Blocked -> pre-existing ACollapsedFullyAllocatedTargetFromThisVNet red; M456 property mutants -> each 21-L2 test red; M7a-e message/attribution mutants -> each 21-L3 test red; M8 catch-message mutant -> 21-L4 test red). Non-equivalence + fix proven: added a counter-test (collapsed fully-allocated target whose Row azureResourceId is VNetId("vnet-c").ToUpperInvariant(), asserting the whole-prefix subnet is AlreadyImported) — 877/877 green at HEAD, red under the Ordinal mutant. Verifier ran the whole chain independently in a fresh clone: citations confirmed against the working tree (OrdinalIgnoreCase at line 17 inside IsSameVNet 14-20, consumers at 204/338/550, inline compares at 197/565; git diff 6fb8557..85b1819 confirms the 21-L1 gate at 338 is the delta's only production change and consumes IsSameVNet); baseline 876/876; mutant 876/876 GREEN with full bin/obj wipe + build-server shutdown; counter-test FAILS under the mutant (877 total, 1 failed, exactly this test) and PASSES at HEAD (877/877, clean rebuild both times). RenameOnlyCandidate => Status==AlreadyImported && WouldRename* confirmed at AzureBulkImportViewModels.cs:37/57, so the dropped-rename consequence is real.

**Fix:** Add one regression test to test/Bastet.Tests/Azure/AzureBulkImportZeroWorkTests.cs pinning the case-equivalence half of the same-VNet decision, e.g. `ACollapsedFullyAllocatedTarget_LinkedWithDifferentIdCasing_IsStillAlreadyImported` — VNet("vnet-c", ["10.63.0.0/16"], Sub("vnet-c", "whole", "10.63.0.0/16")) annotated against Row(1, "vnet-c", "10.63.0.0", 16, VNetId("vnet-c").ToUpperInvariant(), fullyAllocated: true); assert the single subnet's Status is AlreadyImported. Demonstrated: passes at HEAD, fails under the surviving mutant. No production change needed.

**Residue of:** 21-L1

## L2 — 21-L3 tests pin each concurrency-message clause firing but not staying silent: always-fire mutants of the Description and CIDR clauses survive the full suite `[x1]`

**Where:** test/Bastet.Tests/SubnetManagement/SubnetControllerConcurrencyRedisplayTests.cs:195; also src/Bastet/Controllers/SubnetController.Edit.cs:242 (Description clause, unpinned silent direction), src/Bastet/Controllers/SubnetController.Edit.cs:256 (CIDR clause, unpinned silent direction)

**Breaks:** The Subnet Edit concurrency message lists "Stored values that differ from this form". The delta's four tests pin that Description, Tags and CIDR clauses appear with the stored value when the field differs, and the file pins absence-when-unchanged for Name (line 235) and Tags (lines 118, 316) — but no test anywhere asserts the Description or CIDR clause is ABSENT when the field is unchanged. A regression that makes either clause fire unconditionally (condition inverted, dropped, or comparing the wrong pair) ships green: on any conflict the operator is told e.g. "CIDR is now /24" and "Description is now empty" for fields that did not differ, sending them to re-check and possibly re-enter values that were never in conflict — the message becomes untrue in exactly the direction these tests were added to guard.

**Repro:** Finder: in a clone at 85b1819, changed SubnetController.Edit.cs so the Description-differs condition (line 242) and the CIDR-differs condition (line 256) each became `if (true)`; wiped all bin/obj, dotnet build-server shutdown, rebuilt --no-incremental (0 errors), full suite: 876/876 passed, 0 failed. Control: an attribution mutant (Description empty-check reading origSubnet.Tags) was killed by Edit_POST_ConcurrencyConflict_AttributesTheEmptyWordingToTheFieldThatIsEmpty and ...AttributesEachWordingToItsOwnField, confirming the tests are otherwise live. Verifier 1 reproduced the combined mutant run (876/876 green) plus a stale-build control under the identical protocol (Name clause -> if (true): killed exactly at test line 235, 1 failed / 9); grep over the whole suite confirmed the only absence pins are DoesNotContain("Tags") at lines 118/316 and DoesNotContain("Name is now") at line 235 — none for "Description is now" or "CIDR is now". Verifier 2 independently ran each mutant alone: Description always-fire alone 876/876 passed; CIDR always-fire alone 876/876 passed — both survive individually, stronger than the combined run; control mutant 874/876 killed by exactly the two attribution tests.

**Fix:** In Edit_POST_ConcurrencyConflict_SaysEmptyWhenTheStoredDescriptionAndTagsAreEmpty (CIDR is unchanged there: 24/24) add `Assert.DoesNotContain("CIDR is now", message);` and add a test (or extend the Name-only test at line 86, where stored and posted Description are both null) asserting `Assert.DoesNotContain("Description is now", message);`. Two asserts, mirroring the existing Name/Tags absence pins. Verified sound both directions: each assert passes on correct code and kills its mutant.

**Residue of:** 21-L3

# Info

# Refuted

| id | title | reason |
|---|---|---|
