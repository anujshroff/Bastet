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

## S1. Clock-behind-writer delete-guard test passes with its fix reverted; the section-8 counter-test does not enforce the 18-R2 ruling (Low) [x2]

**Where:** `test/Bastet.Tests/HostIpManagement/SubnetHostIpInteractionTests.cs:614`
**Breaks:** If the delete-scope guard regresses from the subtree host-IP count back to the
wall-clock watermark (the exact 18-R2 defect), this test stays green: its posted count of 1 binds
positionally into the old `long? confirmedMaxHostIpTicks` parameter, and any real `CreatedAt` tick
value exceeds 1, so the old broken guard also refuses. The refusal it asserts is satisfied by any
refusing implementation, so a delete that archives host IPs a clock-behind concurrent writer added
could ship silently (rule 2). Product model section 8 names this test as the counter-test enforcing
the 18-R2 owner ruling, and it does not enforce it. Severity demoted Medium → Low on verification:
the shipped guard is correct today, so no wrong answer is given and no data is at risk unless the
defect relands; a naive watermark reland is even accidentally caught by the sibling test
`DeleteSubnet_WithNestedHostIps_ArchivesAllHostIps`, so "could ship silently" requires the
re-introducer to also rewire that test's caller.
**Repro:** In a copy of the repo, reverted `src/Bastet/Controllers/SubnetController.Delete.cs` and
`src/Bastet/Models/ViewModels/DeleteSubnetViewModel.cs` to `f2050fa^` (the pre-fix watermark
guard); the unchanged test compiles (int→long implicit conversion at line 646) and PASSES. Rewiring
the same call to the old contract (passing `reviewed.ConfirmedMaxHostIpTicks` as the old form
posted) makes it FAIL with `Assert.NotNull`: the watermark guard lets the delete of subnet 730
proceed despite the backdated concurrent host IP — proving the scenario is real and the shipped
wiring is what is decorative. Independently reproduced by the verifier in a scratch clone.
**Fix:** Extend the test with a second act: after the refused confirm, confirm again with the
current count (2) and assert the delete then proceeds. Under a watermark reintroduction the second
confirm still refuses (subtree max ticks vastly exceeds 2), turning the test red; under the count
guard it succeeds, also pinning rule 3 (the operator can act after re-review).
**Residue-of:** 18-R2

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

## S3. ASlash32_CanStillBeMarkedFullyAllocated is vacuous — it asserts its own constructor inputs and the /32 fully-allocated toggle is unguarded (Low) [x2]

**Where:** `test/Bastet.Tests/SubnetManagement/SubnetDetailsActionGateTests.cs:41`
**Breaks:** The test builds `Details(32)` and asserts `HostIpAssignments.Count == 0 &&
ChildSubnets.Count == 0 && !IsFullyAllocated` — exactly the values it just constructed, touching no
product predicate. The behaviour its name claims to pin (a /32 keeps the Mark as Fully Allocated
button after 18-R8 added `Cidr < 32` to `CanAddChildSubnet`) lives only as an inline Razor
expression in `_HostIpAssignments.cshtml`, which no test reaches. If the view gate regresses to the
pre-fix form (`Model.CanAddChildSubnet && ...`), every /32 loses the toggle, the operator cannot
record a fully-allocated /32, and Bastet reports allocated space as free (rule 1) until they find
the host-IP workaround — with the whole suite green.
**Repro:** In a copy of the repo, reverted
`src/Bastet/Views/Subnet/Details/_HostIpAssignments.cshtml` to `f2050fa^` (restoring
`Model.CanAddChildSubnet &&` to the toggle gate, which with the fixed `CanAddChildSubnet` removes
the button from every /32) and ran the full suite: 838 passed, 0 failed, including this test. The
test also passed every other revert experiment run in this beat; it contains no expression that any
code change can falsify. Verifier independently reproduced the mutation experiment (0 build errors,
suite green).
**Fix:** Lift the toggle gate into a named view-model property (e.g. `CanMarkFullyAllocated =>
HostIpAssignments.Count == 0 && ChildSubnets.Count == 0 && !IsFullyAllocated` on
`SubnetDetailsViewModel`), use it in `_HostIpAssignments.cshtml`, and re-point the test at
`Details(32).CanMarkFullyAllocated` — deleting the second inline implementation per section 5.
**Residue-of:** 18-R8

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
