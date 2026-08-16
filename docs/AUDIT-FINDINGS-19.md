# Bastet Audit — Round 19 Findings

- **Branch:** `audit/round-19`
- **HEAD:** `f2050fa` (Audit 18 Cleanup, #177)
- **Test baseline:** 838 passing
- **Date:** 2026-08-16
- **Residue:** Round 19 filed 4 findings, of which 4 are residue of round 18's own fixes

All four findings were independently verified by re-running the filed repro (`yes-ran-it` on every
verdict). Two arrived as Medium and were demoted to Low on verifier severity correction: in both
cases the shipped code at HEAD is correct and fail-closed, so the operator sees no wrong answer
today — the exposure is a latent regression-guard gap, not a live behavioural defect, and the brief
grades severity on consequence to the operator.

## Critical

None.

## High

None.

## Medium

None.

## Low

## S1. Clock-behind-writer delete-guard test passes with its fix reverted; the section-8 counter-test does not enforce the 18-R2 ruling (Low) [x2] — FIXED
_Fixed. The test gains a second act: re-review via GET (page shows count 2), confirm with fresh scope, assert the delete proceeds — any always-refusing implementation, including a watermark reland, now goes red._
_Swept: the first act still independently pins the stale-scope refusal (guard-deleted mutation goes red at the first NotNull)._
_Verified: reviewer relanded the actual watermark signature in scratch — old code compiled via the positional int→long escape and the extended test went RED; 840/840 on the real tree, src untouched._
_Reviewed: pass — the §8 counter-test claim is now true in the strong sense, and the second act also pins rule 3 (the refusal's remedy is actionable)._

## S2. HostIpController half of the 18-R3 fail-closed concurrency fix has no guarding test — full suite green with it reverted (Low) [x1] — FIXED
_Fixed. Sibling test added mirroring the subnet-side stale-token test: stale POST redisplays with the posted token kept, the conflict message renders, no ModelState override._
_Swept: assertions match the subnet sibling verbatim where in scope; the NULL-RowVersion trigger path confirmed to be the CONCURRENCY_CONFLICT branch by the reland experiment._
_Verified: red with the two 18-R3-deleted lines relanded (and with the dangerous token-assignment line alone); green restored; 841/841, src untouched._
_Reviewed: pass. Residual: a ModelState.Remove-only reland is not discriminated — reviewer judged it harmless (the rendered token stays stale either way)._

## S3. ASlash32_CanStillBeMarkedFullyAllocated is vacuous — it asserts its own constructor inputs and the /32 fully-allocated toggle is unguarded (Low) [x2] — FIXED
_Fixed. `CanMarkFullyAllocated` added to SubnetDetailsViewModel, the view consumes it (inline predicate deleted), test re-pointed plus two negative tests._
_Swept: no remaining inline copy of the predicate anywhere in src; server validator agrees with the gate (no CIDR term either side)._
_Verified: build 0 warnings, 840/840; three mutations each kill exactly one distinct test — every term is load-bearing._
_Reviewed: pass. Residual: a Razor re-inline still can't go red in unit tests (no render harness) — covered by /e2e and §5's duplication rule; judged acceptable._

## S4. CIDR-edit refusal test fails on correct code when BASTET_AZURE_IMPORT=true, and the flag-on remedy branch is unguarded (Low) [x1] — FIXED
_Fixed. The theory pins both branches explicitly — flag on asserts the re-import sentence, flag off asserts the message closes at the fact — restoring the prior value in finally; the class joins AzureFeatureFlagCollection so the env-var handling is structurally parallel-safe._
_Swept: every other BASTET_AZURE_IMPORT-touching test class already sits in that serialized collection._
_Verified: green with flag unset AND ambient-true; two mutations (unconditional sentence; inverted conditional) each redden their own act; 841/841, src untouched._
_Reviewed: pass — reviewer's collection-membership advisory adopted._

## Info

None.

## Refuted

| Title | Where | Tag | Reason |
|---|---|---|---|
| *None this round* | — | — | — |
