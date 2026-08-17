# Bastet — Round-22 Audit Findings

Round 22 filed 2 findings, of which 2 are residue of round 21's own fixes. Branch `audit/round-22`, HEAD `85b1819`, test baseline 876, date 2026-08-16, residue rate 2/2 (100%).

# Critical

# High

# Medium

# Low

## L1 — Case-insensitivity term of the 21-L1 same-VNet gate is unpinned: IsSameVNet Ordinal mutant survives the full 876-test suite `[x2]` — FIXED

_Fixed in this commit. Two tests: ACollapsedFullyAllocatedTarget_LinkedWithDifferentIdCasing_IsStillAlreadyImported (ZeroWork, prefix + subnet asserts) and ATargetLinkedWithDifferentIdCasing_IsNotRefusedAsALinkReplacement (Selectability, plan path); no production change — the case-insensitivity exists and is now pinned._
_Swept: all five cited sites — IsSameVNet (17, consumers 204/338/550) and both inline OrdinalIgnoreCase compares (197, 565) — covered by the two tests._
_Verified: 878/878 clean rebuild; three separate Ordinal mutants (17, 197, 565) each red on exactly one test._
_Reviewed: pass — reviewer re-ran all three mutants (distinct failure signatures) and proved the asserts load-bearing by substituting genuinely different ids (tests then fail on unmutated code)._
**Residue of:** 21-L1

## L2 — 21-L3 tests pin each concurrency-message clause firing but not staying silent: always-fire mutants of the Description and CIDR clauses survive the full suite `[x1]` — FIXED
_Fixed in this commit. Two absence asserts added exactly as filed: DoesNotContain("Description is now") in the Name-only id-52 case, DoesNotContain("CIDR is now") in the empty-wording id-56 case, mirroring the existing Name/Tags pins._
_Swept: reviewer confirmed via grep these were the only two clauses without absence pins; condition-swap mutants (Description reading Tags, CIDR reading OriginalCidr) already killed by existing tests._
_Verified: 878/878 clean rebuild; each if(true) mutant red on exactly the test carrying its new assert; reviewer proved each assert is the unique killer (mutant + assert removed → suite green)._
_Reviewed: pass — assert substring choices probed for false positives (none reachable in those scenarios)._
**Residue of:** 21-L3

# Info

# Refuted

| id | title | reason |
|---|---|---|
