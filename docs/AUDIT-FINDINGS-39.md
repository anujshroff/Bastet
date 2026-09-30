# Bastet — Round-39 Audit Findings

branch audit/round-39 / HEAD f017e3821d34882dd7d3dd462ab2fff950c4902c / test baseline 1161 / 2026-09-28 / residue rate 0 of 2

Round 39 filed 2 findings, of which 0 are residue of round 38's own fixes. 2 of the 2 filed findings are residue of an earlier round: M1 of round 8 (a8f669b, the ApplyConfirmations withheld set) and L1 of round 17 (f4a0a87, the planner's linked-child skip).

# Critical

# High

# Medium

## M1 — Reconcile withholds a confirmed-deleted VNet row because a descendant carries an unrecognisable Azure resource id: ApplyConfirmations' second cascade-withhold keys on every review row while BuildPlan's keys on manual content only `[x1]` — FIXED
_Fixed in the 39-M1 commit on audit/round-39. Deleted ApplyConfirmations' second cascade-withhold (AzureReconciler.cs:196-201: the set keyed on every review row and its WithholdTargetsWhoseCascadeIsBlocked call, 358 → 352 lines); BuildPlan's manual-content-keyed site is the one implementation left._
_Swept: every writer and reader of ReviewItems and DescendantSubnetIds under src/ — the only writers are BuildPlan :68 and :97, the bulk delete's held set (SubnetController.AzureReconcile.cs:78-80) already filters to HeldByManualContent, the reconcile client only renders; no other site. The drive is recorded in the e2e skill, phase C._
_Verified: clean build 0 warnings, 1165/1165 (1161 + 4). The new still-offered theory goes red under the exact revert (its 2 rows) and the manual-content theory goes red under loss of either term of the :88 hold; the verifier's drive.py against the fixed build: an unrecognised-id child no longer withholds its confirmed-gone parent (offered VNetDeleted, descendantCount 1), a host IP or hand-made grandchild under that child still holds it, other cases unchanged, 0 fail lines._
_Reviewed: none — no objection. Nine tree shapes on the unfixed and fixed trees incl. confirm POSTs and Chromium: manual-content, NotVisible and live descendants unchanged; only ancestors over an Unrecognised descendant moved from withheld to offered (a VNetPrefixRemoved ancestor among them), each cascade count matching the rows archived._

# Low

## L1 — Bulk import preview prints "No child subnets selected." for a prefix whose selected child subnets were all already imported `[x1]` strings — FIXED
_Fixed in the 39-L1 commit on audit/round-39 (strings batch). One string in renderPlan's empty-children branch (_BulkScripts.cshtml:605): "No child subnets selected." → "No child subnets will be created or renamed."; no logic changed._
_Swept: grep for "No child subnets" and "subnets selected" over src/ — the sentence lived at one site; the Details page's "No child subnets have been created yet." is a different, true statement. Pinned by AzureWizardClientWordingTests.APreviewCardWithNoChildEntries_SaysWhatWillHappen_NotWhatWasSelected (binds the sentence to its branch, asserts the old one is gone); the drive is recorded in the e2e skill, phase F._
_Verified: clean build 0 warnings, 1166/1166 (1165 + 1); the pin fails against the old wording ("Pattern not found") and passes with the fix; Chromium on the fixed build against a rebuilt VNet (a39-r39l1-many): the two-tab 409 path's re-preview card reads the new sentence with the old absent, Confirm answers 200 with every counter 0, and a prefix ticked alone renders the same sentence; 0 fail lines._
_Reviewed: none — no objection. Both trees through the race; the sentence shown to be true in every state reaching the branch (all children already imported with rename off, prefix alone, both planner error states with Continue disabled) and absent whenever a rename or create will happen (rename on lists "Rename to ..."; fully-allocated skips the branch)._
_Not done: the reviewer noted that a selection tree loaded before another tab imported an encompassing subnet can still re-preview a "will be marked fully allocated" header with Continue enabled; that is the wizard's stale-tree path (the commit re-plans and answers 409), not this sentence, and was left alone._

# Info

# Refuted — reported by a finder, killed by the verifier

| id | title | file | killed by | reason |
|---|---|---|---|---|

None.
