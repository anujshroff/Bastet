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

## S2. HostIpController half of the 18-R3 fail-closed concurrency fix has no guarding test — full suite green with it reverted (Low) [x1]

**Where:** `src/Bastet/Controllers/HostIpController.cs:243`
**Breaks:** 18-R3 made concurrency conflicts fail closed in two sibling places:
`SubnetController.Edit` (guarded by the new `Edit_POST_ConcurrencyConflict_KeepsTheStaleToken`
test) and `HostIpController.Edit` (guarded by nothing). Restoring the two deleted lines in
`HostIpController` (`viewModel.RowVersion = currentHostIp.RowVersion;`
`ModelState.Remove(RowVersion)`) re-opens the blind-retry overwrite on host IP edits — a second
submit silently destroys another user's saved host IP changes — and no test goes red, so the
regression ships silently. Section 5's every-write-path-siblings invariant says a rule on one path
and not the other is exactly the divergence that shipped before. Severity demoted Medium → Low on
verification (both verifiers): at HEAD the code is correct and fail-closed, so the operator sees no
wrong behaviour today; the exposure is a latent silent-regression guard gap, comparable to round
18's test-pinning items, not the Medium behavioural defect 18-R3 itself was.
**Repro:** In a copy of the repo, reverted only `src/Bastet/Controllers/HostIpController.cs` to
`f2050fa^` (restoring the token refresh in the `DbUpdateConcurrencyException` branch) and ran the
full suite: 838 passed, 0 failed. By contrast the same experiment on `SubnetController.Edit.cs`
fails `Edit_POST_ConcurrencyConflict_KeepsTheStaleToken_SoABlindRetryCannotOverwrite`, proving the
harness can catch this shape. Both verifiers independently reproduced the 838-green result with the
two 18-R3-deleted lines restored.
**Fix:** Add the sibling test for `HostIpController.Edit` mirroring
`Edit_POST_ConcurrencyConflict_KeepsTheStaleToken_SoABlindRetryCannotOverwrite`: post an edit with
a stale `RowVersion`, assert the redisplayed view model keeps the stale token and `ModelState`
carries no `RowVersion` override.
**Residue-of:** 18-R3

## S3. ASlash32_CanStillBeMarkedFullyAllocated is vacuous — it asserts its own constructor inputs and the /32 fully-allocated toggle is unguarded (Low) [x2] — FIXED
_Fixed. `CanMarkFullyAllocated` added to SubnetDetailsViewModel, the view consumes it (inline predicate deleted), test re-pointed plus two negative tests._
_Swept: no remaining inline copy of the predicate anywhere in src; server validator agrees with the gate (no CIDR term either side)._
_Verified: build 0 warnings, 840/840; three mutations each kill exactly one distinct test — every term is load-bearing._
_Reviewed: pass. Residual: a Razor re-inline still can't go red in unit tests (no render harness) — covered by /e2e and §5's duplication rule; judged acceptable._

## S4. CIDR-edit refusal test fails on correct code when BASTET_AZURE_IMPORT=true, and the flag-on remedy branch is unguarded (Low) [x1]

**Where:** `test/Bastet.Tests/SubnetManagement/SubnetControllerCidrEditTests.cs:701`
**Breaks:** The 18-R15 fix makes the Azure-linked CIDR refusal message flag-dependent: with the
import feature enabled it says "Change the prefix in Azure, then ask an administrator to re-import
it." The test asserts `DoesNotContain("re-import")` without arranging the flag, so it only pins the
flag-off branch by accident of ambient environment: running `dotnet test` in a shell configured
like a real Azure-enabled deployment (the rig's own app runs with `BASTET_AZURE_IMPORT=true`) turns
a correct build red, and nothing anywhere pins the flag-on remedy sentence, so that branch can
regress to naming an unreachable remedy (rule 3) unguarded.
**Repro:** On unmodified `f2050fa` code, ran `dotnet test` with `BASTET_AZURE_IMPORT=true` in the
environment, filtered to `*CidrChangeOnAzureLinkedSubnet*`: both theory cases (newCidr 15 and 17)
fail with `Assert.DoesNotContain() Failure: Sub-string found` at
`SubnetControllerCidrEditTests.cs:701`. The same tests pass with the variable unset, and fail
correctly against the pre-fix code (old message contained "re-import" unconditionally), confirming
the assertion only discriminates in the flag-off environment. Both verifiers independently
reproduced the flag-on failure on unmodified HEAD.
**Fix:** Have the test pin both branches explicitly: set `BASTET_AZURE_IMPORT=true` around one act
and assert the administrator re-import sentence, clear it around another and assert the message
ends as prose with no remedy named, restoring the prior value in a `finally` — the pattern the
Azure controller tests already use.
**Residue-of:** 18-R15

## Info

None.

## Refuted

| Title | Where | Tag | Reason |
|---|---|---|---|
| *None this round* | — | — | — |
