# Bastet — Round-38 Audit Findings
Round 38 filed 3 findings, of which 2 are residue of previous rounds' fixes (M1 ← round 8, L1 ← 31-L1).
branch: audit/round-38, made off audit/round-37 at 37e41de on the owner's instruction (main 8ae8961, unchanged)
HEAD: 37e41de (src/ identical to main 8ae8961)
test baseline: 1154 of 1154 passing, clean build, 0 warnings
date: 2026-09-26
residue rate: 2 of 3
scale: Standard (scale gate open: round 37 residue 0 of 0) — beats 1–5 and 7 × 2 independent passes; beat 6 and its deep sweep skipped on an empty src/ delta (8ae8961..HEAD)

# Critical

None.

# High

None.

# Medium

## M1 — Reconcile withholds a row Azure confirmed deleted because an Azure-linked row beneath it answered NotVisible or Unknown `[x1]` — FIXED
_Fixed in the commit "M1: reconcile offers a confirmed-deleted ancestor whose Azure-linked descendants could not be confirmed". ApplyConfirmations no longer withholds an ancestor because a NotVisible or Unknown Azure-linked row sits beneath it: the `notVisible` and `unknown` members of the withheld set are deleted (AzureReconciler.cs:197), leaving only the ReviewItems member._
_Swept: WithholdTargetsWhoseCascadeIsBlocked has two callers (BuildPlan's manual-content cascade and ApplyConfirmations' review-row cascade), both kept; the scan and the delete endpoint's re-check share ApplyConfirmations, so both inherit the fix; the reconcile client's only descendantSubnetIds use is the confirm step's count. No other site._
_Verified: new AzureReconcilerTests theory (NotVisible, Unknown) red on the unfixed code and on each single-member restoration, green fixed; suite 1154 → 1156, 0 warnings; live on the rig: the unfixed tree withheld the confirmed-deleted VNet row behind the cascade warning, the fixed tree offered it with 3 descendants, the RBAC-hidden rows stayed withheld and named, the delete archived exactly the 4-row subtree with 0 host IPs, and the hidden credential re-imported its VNet top-level; recorded in /e2e phase C._
_Reviewed: (c) no failure. The reviewer re-ran the drive, drove the Unknown half through a subnet-only 503 with the VNet GET reaching real ARM, manual content beneath the hidden rows (ancestor still held, delete 409), UnrecognisedResourceId ancestors (still withheld), the Chromium confirm step and delete, and the mutation copies; two non-binding /e2e wording notes adopted._
_Not done: the NotVisible/Unknown warnings still say "withheld from deletion" while an offered ancestor's cascade archives those rows, the shape 18-R1 left for stillLive; no model sentence makes it a defect, and the model gate struck the structural remainder._

# Low

## L1 — Bulk import commit stores a child name or Description its own Edit form refuses, silently stripping a composed name instead of refusing it `[x2]` — FIXED
_Fixed in the commit "L1: bulk import refuses a composed child name or Description its own form refuses". BulkCreateFromAzurePlanCore now asks the shared ContainsHtmlTags predicate of every planned child name and of the Description the fully-allocated note would produce for an ExactMatch target, on the plan re-derived under the lock and before the transaction opens, and answers 400 with nothing written; the posted-name guard and its walk are unchanged._
_Swept: every name or Description write in the commit path — the target rename is guarded through the approved Expected.NewName, the auto-created target name is a stripped VNet name plus a (network-cidr) suffix that cannot close a tag, and the only other note writer is Strip on un-mark; the manual Create and Edit forms already carry [NoHtml]. No other site._
_Verified: BulkImportNameParityTests and BulkImportDescriptionParityTests — the two refusal pins red on the unfixed code and under each check removed or pointed at the input name, the two controls green on both; suite 1157 → 1161, 0 warnings; live on the rig: the unfixed tree stored 'p<s>)' and the composed description and its Edit form refused both rows, the fixed tree refused both commits naming the composed name and the Bastet and Azure subnets with nothing written, while the benign controls imported and the 31-L1 guard still fired._
_Reviewed: (c) no failure. The reviewer re-ran both trees, drove the Edit refusals in Chromium on the unfixed tree, probed multi-prefix plans, rename-only and top-up passes, target renames, auto-created targets, over-refusal controls and a target deleted between preview and commit, followed both remedies the messages name, and ran five mutations; non-binding notes only._
_Not done: a pre-existing no-op rename offer for an already-disambiguated child when renames are on (the planner compares the stored name to the base name, AzureBulkImportPlanner.cs ~657), outside this diff and not one of this round's findings; noted for the next audit._

## L2 — HostIp Create's lock-timeout arm tells the operator to 'try again' on a subnet that was deleted while the request waited; the retry is refused with 'Subnet not found' `[x1]` — FIXED
_Fixed in the commit "L2: HostIp Create's lock-timeout arm answers 404 for a subnet that no longer exists". The catch (TimeoutException) in the Create POST (HostIpController.cs:147) now asks the existence question SetAllocationStatus's arm already asks, with the same code, and answers NotFound before adding the retry message; nothing else in the POST changed._
_Swept: all nine lock-timeout arms. HostIp Edit (RedisplayEditAsync), both DeleteConfirmed arms, SetAllocationStatus and Subnet Edit's redisplay already answer 404 for a vanished subject; the two JSON endpoints answer 503; Subnet Create's subject is the new subnet, and a retry with a parent deleted mid-wait is processed and answers the field-level "Selected parent subnet does not exist" with the parent list re-populated, so it is not this class (driven by the reviewer). No other site._
_Verified: SubnetLockTimeoutTests — the deleted-while-waiting case red on the unfixed code and under the removed, inverted and misplaced check, the live-subnet control green on both; suite 1156 → 1157, 0 warnings; live on the rig: the unfixed tree re-rendered "Please try again" for a deleted subnet and refused the retry with "Subnet not found", the fixed tree answered 404 after the 30 s wait with nothing written, and the live-subnet control still retried and created the host IP._
_Reviewed: (c) no failure. The reviewer re-ran both trees, drove the form in Chromium with the subnet deleted from a second session and the lock held by a third, the two-replica ordered queue and the single-instance queue, every sibling arm including 31-L5's subnet-gone redisplay (unchanged), and the three mutations; non-binding notes only._

# Info

None.

# Refuted — reported by a finder, killed by the verifier

| id | title | reported by | killed by | reason |
| --- | --- | --- | --- | --- |
