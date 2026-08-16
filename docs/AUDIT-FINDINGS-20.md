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

### 20-S1: CanMarkFullyAllocated's ChildSubnets term is unguarded — 841/841 tests pass with it deleted (Low) [x2] — FIXED

_Fixed. Added ChildSubnets_BlockMarkingFullyAllocated, and swept the sibling gate CanAddHostIp (zero coverage on either term): ABareSubnet_OffersAddHostIp, ChildSubnets_BlockAddingHostIps, FullAllocation_BlocksAddingHostIps. Tests only, no src change._
_Swept: every `=> bool` gate in the view models; CanAddChildSubnet's three terms already pinned, CanCommit already covered, no other unguarded term._
_Verified: four mutations in a scratch copy each killed exactly one new test; full suite 845/845, 0 warnings._
_Reviewed: (c) — reviewer independently re-ran all four mutations and the over-pinning check against HostIpValidationService; no corrections._

Residue-of: 19-S3

## Info

None.

## Refuted

| title | where | tag | reason |
|---|---|---|---|

None.
