# Bastet - Round-26 Audit Findings

branch audit/round-26 | HEAD f8bd697 | baseline 900 tests passed, 0 failed, 0 build warnings | regression-only round over 9d3c7b2..f8bd697 | date: 2026-08-21

> Round 26 filed 3 findings, of which 3 are residue of round 25's own fixes.

Every finding this round traces to round 25's L1 or L3 fixes; discovery produced nothing outside that delta.

# Critical

# High

# Medium

# Low

## L1 - 25-L1 grant-access panel also fires on the no-credential branch, contradicting the Failed banner on the same screen `[x2]` — FIXED
_Fixed in this commit. GetSubscriptions' null-client branch now throws the same classification GetVNetInventory gives the state; the controller catch renders the truthful subscription-error panel._
_Swept: all four _armClient==null branches now uniform (Failed / Success=false / throw / reconcile Unknown fail-safe untouched); sole caller AzureController.cs:16._
_Verified: 905/905, 0 warnings; browser drive of both wizards under AZURE_TOKEN_CREDENTIALS=bogusvalue shows Failed banner + error panel, grant-access panel hidden; new pin red against the reverted hunk._
_Reviewed: (c) acceptable — reviewer independently re-drove both surfaces, proved the pin discriminates, found no caller or sibling regression._

## L2 - 25-L1 no-subscriptions panel rewording has no regression pin on either wizard; the old false remedy can silently reland `[x2]` — FIXED
_Fixed in this commit. AzureWizardClientWordingTests pins the grant-access sentence present and the false credential remedy absent in both _StepSubscription.cshtml partials._
_Swept: both wizard partials covered by one theory; the sentences appear in no other view._
_Verified: revert to 9d3c7b2 reds both theories; single-word mutants red per file; 905/905._
_Reviewed: (c) acceptable — eight mutations all caught, root-walk holds in CI's Release shape._

## L3 - 25-L3 xhr.status-0 commit-error branching has no regression pin in either wizard's client JS; "Server error: 0" can silently reland `[x2]` — FIXED
_Fixed in this commit. The same test file pins the status-0 outcome-unknown sentence, the bare-status sentence and the xhr.status === 0 branch marker in both *Scripts.cshtml, and the absence of "Server error: "._
_Swept: xhr.status === 0 occurs exactly once per script file; no other view carries the commit-error wording._
_Verified: revert to 9d3c7b2 reds both theories; word and operator mutants red; a split-string false-pass probe still caught; 905/905._
_Reviewed: (c) acceptable — the required-wording asserts are the discriminating half; dead-code shadowing is out of a source pin's reach and behavioral firing is L1's pinned ground._

# Info

# Refuted

| id | title | where | reason |
|----|-------|-------|--------|
