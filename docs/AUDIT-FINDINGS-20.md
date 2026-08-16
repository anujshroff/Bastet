# Bastet Audit Round 20 — Findings

- Branch: `audit/round-20`
- HEAD: `6a21950` ("Audit 19 Cleanup (#178)")
- Test baseline: 841 passing (`dotnet test` from repo root)
- Date: 2026-08-16

Round 20 filed 1 findings, of which 1 are residue of round 19's own fixes.

## Critical

None.

## High

None.

## Medium

None.

## Low

### 20-S1: CanMarkFullyAllocated's ChildSubnets term is unguarded — 841/841 tests pass with it deleted (Low) [x2]

Where:      test/Bastet.Tests/SubnetManagement/SubnetDetailsActionGateTests.cs:50

Breaks:     Round 19 (19-S3) lifted the fully-allocated toggle gate into SubnetDetailsViewModel.CanMarkFullyAllocated and the ledger records "every term mutation-load-bearing", but no test pins the ChildSubnets.Count == 0 term. If a future edit drops that term, the Details page offers "Mark fully allocated" on a subnet that has child subnets; the POST then fails server-side in HostIpController.SetAllocationStatus, so the operator is offered an action the app always refuses (product-model rule 3: an offered remedy that cannot be acted on). The delta's own gate tests (ASlash32_CanStillBeMarkedFullyAllocated, AFullyAllocatedSubnet_IsNotOfferedTheMarkToggleAgain, HostIps_BlockMarkingFullyAllocated) cover the other two terms only.

Repro:      In a repo copy at /tmp/claude-1000/-home-anuj-code-Bastet/52de6a9c-c73b-4530-ad7f-b7ed4a3449dd/scratchpad/rig20/scratch-b7p2, changed src/Bastet/Models/ViewModels/SubnetViewModels.cs:95 to `public bool CanMarkFullyAllocated => HostIpAssignments.Count == 0 && !IsFullyAllocated;` (ChildSubnets term removed), rebuilt, ran the ENTIRE suite: 841 total, 0 failed. By contrast, removing the !IsFullyAllocated term fails AFullyAllocatedSubnet_IsNotOfferedTheMarkToggleAgain and removing the host-IP term fails HostIps_BlockMarkingFullyAllocated, so only this term is decoration-guarded. (SetAllocationStatus_SubnetWithChildren_Fails covers the controller, not the view-model gate the view renders from.) Verifier (yes-ran-it): independently reproduced in /tmp/claude-1000/-home-anuj-code-Bastet/52de6a9c-c73b-4530-ad7f-b7ed4a3449dd/scratchpad/rig20/verify-v1 — removed the ChildSubnets.Count == 0 term from CanMarkFullyAllocated (SubnetViewModels.cs:95) and ran `dotnet test`: 841 total, 0 failed, confirming no test pins that term and refuting the ledger's 19-S3 "every term mutation-load-bearing" claim.

Fix:        Add a fourth gate test alongside the two added by 19-S3 in SubnetDetailsActionGateTests: `[Fact] public void ChildSubnets_BlockMarkingFullyAllocated() => Assert.False(Details(24, children: 1).CanMarkFullyAllocated);` — verified red under the mutation, green on unmodified code.

Residue-of: 19-S3

## Info

None.

## Refuted

| title | where | tag | reason |
|---|---|---|---|

None.
