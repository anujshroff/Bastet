# Bastet — Round-32 Audit Findings

> Round 32 filed 4 findings, of which 3 are residue of round 31's own fixes.

- **Branch:** audit/round-32
- **HEAD:** f8d1d7a (the delta's base is b406c60, the tree round 31 audited)
- **Test baseline:** 1038 total, 0 failed (dotnet test); dotnet build --no-incremental 0 warnings
- **Date:** 2026-09-22
- **Scale:** Regression-only (scale gate: round 31 residue 5 of 16 > 2); beat 6 x 2 independent passes over b406c60..HEAD -- src/
- **Residue rate:** 3 of 4

# Critical

None.

# High

None.

# Medium

None.

# Low

## L1 — A superseded wizard commit's late 409 voids the fresh re-preview/re-scan (dead Confirm, or Continue disabled with no reason shown) `[x2]`
**Where:** src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:704, src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:538, src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:438, src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:199, src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:665, src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:704-707, src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:411-419, src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:438-443, src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:665 (commitImport error callback acts on a superseded response; fix site), src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:538 (hide moved into renderPlan by 18dcabf; a superseded non-409 failure carries into the fresh step 4), src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:411 (delete error callback acts on a superseded response; fix site), src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:438 (409 branch voids lastPlan and the confirmation on screen), src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:199 (hide moved into renderPlan by 18dcabf), src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:665 (commitImport error callback: where the stale-response guard belongs; it has no staleness check, unlike loadVNets/loadPreview), src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml:538 (renderPlan hide; with the go-commit hide removed at :619-625 by 18dcabf, a superseded non-409 failure that lands after renderPlan survives into step 4 - driven with a 503, cosmetic), src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:438 (same unconditional 409 void; reproduced only with page.route holding the delivery - 0 of 5 natural runs, because every reconcile 409 is decided pre-lock at src/Bastet/Controllers/SubnetController.AzureReconcile.cs:84-121 by the same Azure re-check a re-scan runs; plausible), src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:411 (delete error callback: where the reconcile guard belongs), src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml:199 (renderPlan hide; go-confirm handler at :303-355 no longer hides, so a superseded lock-timeout 503 - which IS lock-delayable because the reconcile delete takes the lock after its re-check - would carry into the next confirmation; same shape as the bulk 503, not driven)
**Breaks:** Bulk wizard: an Admin previews rig-32-base-simple 10.100.0.0/20 with simple-a and simple-b (plan: New top-level) and clicks Confirm Import. The commit waits on the subnet lock while another operation that holds it creates a hand-made 10.100.0.0/20. While the spinner runs, the Admin clicks Back to Preview, Back to Selection, then Next: Preview. The fresh preview correctly shows 'Exact match Bastet subnet "a2-manual-exact"' with no errors. The superseded commit then answers 409 ('The plan changed since it was previewed…'), and 18dcabf's 409 branch calls invalidatePlan() on the fresh preview: lastSelection=null and the step 3/4 tabs are disabled. (A) If the 409 lands before the fresh preview's response, renderPlan re-enables Continue to Commit and hides the error. Confirm Import on step 4 is then enabled but does nothing (`if (!lastSelection) return;`): no request, no progress, no message. (B) If the 409 lands after the fresh preview, the fresh error-free plan shows Continue to Commit disabled and step 3 says nothing about why. The refusal is written into the step-4 pane, whose tab the same handler just disabled. Reconcile has the same shape at _ReconcileScripts.cshtml:438-444. A superseded delete's 409 sets lastPlan=null on top of a fresh re-scan, so Continue is disabled while the fresh scan's row is still ticked, and the refusal sits in the disabled step-3 pane. A superseded non-409 failure also survives: 31-L13 moved the only hide from Continue-to-Commit into renderPlan, and renderPlan had already run. So the fresh plan's commit step opens showing 'The operation timed out because another subnet operation is in progress. Please try again.' for a commit that was never attempted. The pre-round-31 build completes every one of these sequences. At HEAD the operator recovers only by re-previewing, re-scanning or reloading, and nothing on screen tells them to. Also reported, independently, as P1-3 ("Both wizards: a 409 answering an earlier Confirm Import / Delete Stale Subnets voids the plan the operator has since re-previewed or re-scanned"): 31-L13 made the 409 branch of showCommitError destroy wizard state. In bulk, invalidatePlan() nulls lastSelection and disables Continue and the step 3/4 pills. In reconcile, it sets lastPlan = null and calls voidConfirmation() and updateGoConfirmBtn(). The commit and delete handlers never check that the snapshot the response answers is still the current one. The other three wizard requests do check: loadVNets (vnetSeq), loadPreview (previewSeq) and runScan (scanSeq). While a commit is pending, the pills and Back buttons stay live; a commit can wait up to 30 s when the subnet lock is contended, and the reconcile delete re-reads Azure before it answers. Bulk example: the operator previews with 'Rename matched' on (target 'tmp-name' renamed to rig-32-a1-v1, plus create s7). Another operator renames the target. Confirm Import waits on a lock held by another operation. Meanwhile the operator goes back, re-previews (fresh plan: exact match 'rig-32-a1-v1', create s7) and continues. When the first commit's 409 lands, the fresh commit step shows 'The plan changed since it was previewed ... renaming the target changed to False.', a sentence about the plan already replaced. The fresh plan cannot be committed (Confirm, Continue and pills 3/4 disabled) until a third preview. Reconcile behaves the same way: a late 409 for [s4, s5] voided a fresh s5-only confirmation made after a re-scan. It told the operator '1 of the selected subnet(s) are no longer offered for deletion by the latest re-check' about a selection the latest re-check does offer. Before 31-L13, a late 409 only showed its banner and re-enabled the button, so the fresh plan still committed (driven for bulk; for reconcile, read from the pre-18dcabf code).
**Repro:** *Truth lens (yes-ran-it) — ran:* Own fixture: rig-32-a16-v1 in bastet-visible, 10.116.0.0/20, with s1 10.116.0.0/24 and s2 10.116.1.0/24. Own catalogs audit32_a16_{head,ctl,fixa,fixb,fixc} on ports 5260-5264.

Builds:
- head: <rig>/app, unmodified.
- ctl: a HEAD archive with `git show 18dcabf -- src/ | patch -R` applied, i.e. HEAD minus 31-L13 only.
- fixa: the filed P2-1 fix, verbatim.
- fixb: error-callback guards; the bulk guard re-enables Confirm, the reconcile guard is `lastPlan !== postedPlan`.
- fixc: the betterFix, i.e. the bulk re-enable guard plus reconcile `ids !== confirmedIds`.
All published with 0 warnings and 0 errors. I checked each served script with curl.

The concurrent operation was a sqlcmd session. It held sp_getapplock 'Bastet:SubnetOperations' (Exclusive/Session) and inserted a hand-made 10.116.0.0/20 row, 'a16-manual-exact'. sys.dm_tran_locks showed GRANT for that session and WAIT for the app's commit.

I drove headless chromium (Playwright) through /Azure/BulkImport and /Azure/Reconcile, with in-page jQuery ajaxSend/ajaxComplete logging.

Bulk scenarios:
- B: Confirm Import, then Back to Preview, Back to Selection, Next: Preview, with no injected latency.
- A: as B, but page.route fetched the fresh preview's response and held it until the page had processed the 409.
- C: as B, then Continue to Commit before the 409 lands.
- T: lock held 36 s with no insert, which produces a 503.
- K: a 409 that answers the current snapshot.

Reconcile setup: two SQL-seeded rows linked to non-existent subnets a16-gone1 and a16-gone2 under rig-32-a16-v1. They scan as SubnetDeleted, and ARM confirmed them Deleted with a 404. I confirmed row 1, added a hand-made child under it, then clicked Delete. page.route fetched the 409 and held it.

Reconcile scenarios:
- R: Back, Back, rescan, tick row 2.
- R3: as R, then Next and type approved.
- R4: no rescan; untick row 1, tick row 2, Next, approved.
- RK: a 409 that answers the current confirmation.

Cleanup: all five instances stopped by pidfile, catalogs dropped, VNet deleted through teardown with --match, repository status empty.

*Truth lens — came back:* HEAD: every scenario reproduced. 0 page JS errors and 0 fail/crit log lines.
- B: the fresh plan showed 'Exact match Bastet subnet "a16-manual-exact"' with Continue enabled. The 409 landed at 16.8 s. Continue to Commit then went disabled, and the step3 and step4 pills went disabled. Step 3's only alert was the static 'Errors must be resolved before you can commit.', with no errors listed. The 409 text sat in the hidden step-4 pane. DB: only the unlinked manual row.
- A: the ajax log read 'done BulkCreateFromAzurePlan 409 @16516' then 'done BulkImportPreview 200 @16529'. The fresh plan rendered with Continue enabled, and step 4 showed Confirm Import enabled and visible. Clicking it sent 0 requests in 45 s and showed no alert. DB unchanged.
- C: S1's 409 text ('...it now resolves to ExactMatch...') showed on the fresh plan's step 4. Confirm, Continue and both pills were disabled.
- T: the 503 'The operation timed out because another subnet operation is in progress. Please try again.' showed on the fresh plan's step 4 before any commit of that plan.
- R: Next: Confirm deletion was disabled with a16-rec-2 still ticked, and the step-3 pill was disabled. Step 2 showed no error. a16-rec-2 was not deleted.
- R3 and R4: a16-rec-1's held refusal showed on a16-rec-2's confirmation, and Delete was disabled.

Control (HEAD minus 18dcabf):
- A, B, C: 1 request each, answered 200 'linked 1 existing target(s)'; s1 and s2 created.
- T: step 4 opened clean.
- R and R4: a16-rec-2 deleted, 200.

fixa (the filed fix):
- B passed.
- C: Confirm Import stayed disabled on step 4, with no spinner and no message. Nothing was written.
- R4: identical to HEAD.

fixb: A, B, C, T, R and R3 passed. R4 was identical to HEAD.

fixc:
- A, C, R, R3 and R4 all completed with 1 request answered 200.
- Its bulk script is byte-identical to fixb's, which passed B and T.
- K and RK still voided the current snapshot exactly as at HEAD.

*Reach lens (yes-ran-it) — ran:* All work was done under rig/work/a17 with my own fixtures and catalogs. Nothing under test/ was touched, and every git read was scoped to src/.
- Fixtures: VNet rig-32-a17-v1 10.117.0.0/20 (s1 10.117.0.0/24, s2 10.117.1.0/24) and VNet rig-32-a17-v2 10.117.16.0/20 (r1, r2, r3) in bastet-visible, all inventoried.
- Builds:
  - HEAD on :5270 (audit32_a17_main).
  - b406c60 control, from git archive with test/ excluded, on :5271 (audit32_a17_pre).
  - HEAD plus P2-1's fix exactly as written on :5272 (fix2).
  - HEAD plus the alias P1-3's variant (bulk: `if (selection !== lastSelection) { $("#bulk-confirm-commit-btn").prop("disabled", false); return; }` at the top of the commit error callback) on :5273 (fix13).
  - All published with 0 warnings and 0 errors; each served its own script (checked with curl and grep).
- Browser: headless chromium through Playwright, with drivers bulk_drive.py, rec_drive.py and current409.py.
- Concurrency model 1: Admin 2 creates the colliding manual subnet 10.117.0.0/20 'a17-manual-exact' through the real /Subnet/Create form in a second tab. A sqlcmd session then holds the product's own applock (sp_getapplock 'Bastet:SubnetOperations', Exclusive/Session; result 0) for 10-12 s as a stand-in for a long lock-holding operation.
- Operator sequence: preview v1 (New top-level), Continue, Confirm Import (the commit waits on the lock), then Back to Preview, Back to Selection, Next: Preview. Variants:
  - the operator stays on step 3;
  - the operator clicks Continue onto step 4;
  - case A, where page.route holds the fresh preview's response until the 409 has arrived;
  - t503, where the lock is held 36 s with no collision.
- Reconcile: v2 imported through the wizard, then r1 and r2 deleted in Azure (they scan as SubnetDeleted). The operator ticks and confirms r1; Admin 2 adds a manual child 10.117.16.0/28 under r1 through the Create form; the operator clicks Delete, then Back to Review, the Subscription tab, Scan, and ticks r2. This ran 5 times with natural timing (3 at machine speed, 2 with human-paced 0.7 s clicks) and once with page.route holding the 409's delivery, on HEAD, the control and fix2.
- Blame: `git blame -s -L … origin/audit/round-31 -- src/…`.
- Teardown: all 4 instances stopped by pidfile, 4 catalogs dropped, both VNets deleted (0 remain), `git status --porcelain` empty.

*Reach lens — came back:* BULK, HEAD, case B (natural lock contention, no injected latency):
- The fresh preview answered 200 in about 20 ms and rendered 'Exact match Bastet subnet "a17-manual-exact"' with no errors and Continue enabled.
- At +11 s the superseded commit answered 409.
- Continue then went disabled, and the step3 and step4 pills were disabled. Step 3 showed only its static "Review the import plan below. Errors must be resolved before you can commit." over an error-free plan. The refusal was written into the hidden step-4 pane. Clicking Continue did nothing.
- Recovery was a third, identical preview: Back to Selection, Next: Preview, Continue, Confirm, then 200 (manual row linked, s1 and s2 created). No page JS errors.

BULK, HEAD, case B' (operator already on step 4 of the fresh plan):
- Step 4 showed the superseded refusal "The plan changed since it was previewed… it now resolves to ExactMatch… it now targets existing Bastet subnet 2" beside the plan that already shows that exact match.
- Confirm, Continue and both pills were disabled. The DB held only the manual row.

BULK, HEAD, case A (page.route held the fresh preview until after the 409):
- Continue and Confirm were both enabled.
- Clicking Confirm sent 0 requests and showed no progress or alert (`if (!lastSelection) return;`).
- Unheld, the preview round-trip here was 10-40 ms, so case A is naturally a narrow race.

BULK, t503 (superseded 503 after a 36 s hold):
- HEAD's step 4 opened on the fresh plan showing "The operation timed out because another subnet operation is in progress. Please try again."
- Confirm worked (200). The control's step 4 was clean.

CONTROL b406c60:
- The identical case B drive left Continue enabled after the late 409. Continue, then Confirm, returned 200: a17-manual-exact linked, s1 and s2 created.

RECONCILE:
- Natural, 5 of 5 runs: the delete's 409 arrived before the re-scan rendered, even when the re-scan was fired 0.1 s after Delete (409 at +0.58-0.69 s, re-scan 200 about 0.05-0.14 s later). renderPlan cleared it, and r2 stayed ticked and confirmable. HEAD was fine every time.
- Only with page.route holding the 409's delivery did HEAD disable "Next: Confirm deletion" with r2 still ticked and the step-3 pill disabled. The control, and fix2, went on to delete r2 (200, r2 archived, held r1 untouched).
- Cause: every reconcile 409 is decided before the lock (SubnetController.AzureReconcile.cs:84-121) by the same Azure re-check that a re-scan runs, so lock contention cannot delay it.

FIXES:
- P2-1 as written (fix2), case B': the dropped 409 left the operator on step 4 with Confirm disabled, the spinner gone, no message, and 0 requests possible. That is a new dead end of the same class. P2-1's reconcile half worked (driven).
- P1-3's variant (fix13):
  - case B: 200, committed;
  - case B': Confirm re-enabled, 200;
  - case A: 200;
  - a 409 answering the current snapshot (operator waiting on step 4) still voided exactly as on HEAD (Confirm and Continue disabled, pills disabled, refusal shown).

BLAME:
- On origin/audit/round-31, _BulkScripts.cshtml:538, 662, 673, 682 and 704-708, and _ReconcileScripts.cshtml:52-60, 199, 396, 419, 424 and 438-444, all come from 18dcabf ("L13: a stale-plan 409 voids the superseded snapshot…"), except _ReconcileScripts.cshtml:53-56. Those four lines are the body of the old invalidateConfirmation, which 18dcabf kept when it split that function into voidConfirmation (:52-57) and a new invalidateConfirmation (:59-65); line blame gives them to e774d4f (:53, :55), 8afa2df (:54) and bf120d6 (:56).

GROWTH:
- _BulkScripts.cshtml: 707 lines at b406c60, 712 at HEAD.
- _ReconcileScripts.cshtml: 439 lines at b406c60, 450 at HEAD.

**Fix:** A verifier judged the filed fix unsound: both verifiers did, on the truth lens and on the reach lens, and each drove the failure live. The verifiers' betterFix is given here instead of the filed fix.

*Truth verifier's betterFix:* Client-only change to 2 files, with no comments. This is the P1-3 variant.

1. _BulkScripts.cshtml, at the top of commitImport's error callback (:665):
`if (selection !== lastSelection) { $("#bulk-confirm-commit-btn").prop("disabled", false); return; }`

2. _ReconcileScripts.cshtml, at the top of the delete's error callback (:411):
`if (ids !== confirmedIds) { return; }`
`ids` is already captured at :365. The complete handler's refreshDeleteButton() then re-evaluates Delete for the confirmation on screen.

Leave these unchanged: both success callbacks, both 409 branches, and both renderPlan hides. A 409 that answers the current snapshot still voids it; I verified this with K and RK. A superseded success did apply, so its summary and redirect stand.

Driven on fixc: A, B, C, T, R, R3 and R4 all complete with one request answered 200, and K/RK behave exactly as at HEAD. The change adds 4 and 3 lines.

This is the stale-response rule the same files already apply through vnetSeq, previewSeq and scanSeq. It narrows 31-L13's void to the snapshot the response answers, and adds no refusal, status or server path.

Candidate's product question: with this guard, a superseded 500 ('could not confirm whether this import/delete was applied') is not shown. No data is at risk, because the server re-derives the plan on every commit and delete.

*Reach verifier's betterFix:* Bulk (_BulkScripts.cshtml:665, first line of commitImport's error callback): `if (selection !== lastSelection) { $("#bulk-confirm-commit-btn").prop("disabled", false); return; }`. Leave the success callback unchanged.

Reconcile: `const postedPlan = lastPlan;` in the Delete click after the ids check, then `if (lastPlan !== postedPlan) { return; }` at the top of the error callback (:411). `complete`'s refreshDeleteButton() re-arms Delete.

Every ordering was driven live: step 3, step 4, case A, and a current-snapshot 409 that still voids.

*Where the two betterFixes differ:* both put the same guard at the top of the bulk commit error callback (:665). For reconcile, the reach verifier's `lastPlan !== postedPlan` guard is the reconcile guard of the truth verifier's fixb build, and fixb behaved identically to HEAD in R4 (the operator re-selects within the same scan: Back to Review, untick row 1, tick row 2, Next). The truth verifier's `ids !== confirmedIds` guard (fixc) completed R4 with one request answered 200, and the reach verifier's fixNote names that same guard as its reconcile alternative.

*Truth verifier, why the filed fix is unsound:* The filed fix is unsound in three ways, and I drove each one.

1. The bulk guard returns without restoring Confirm Import. Suppose the operator opened the fresh plan's step 4 while the old commit was in flight; go-to-commit then set disabled = committing = true. Once the superseded 409 is dropped, Confirm Import stays disabled with no spinner and no message (fixa, case C; nothing written). That is the same class of defect the finding is about.

2. The reconcile guard compares lastPlan, but the 409 answers the posted confirmation. When the operator re-selects within the same scan (Back to Review, untick row 1, tick row 2, Next), the superseded 409 still voids the new confirmation and shows row 1's refusal on row 2's page. fixa and fixb both behave identically to HEAD in R4.

3. The guards on the success callbacks' !result.success branches are dead code. Neither BulkCreateFromAzurePlan nor BulkDeleteStaleAzureSubnets ever answers 200 with success:false; every refusal is a 400, 403, 409, 500 or 503.

The alsoReportedAs P1-3 variant is the sound one.

*Reach verifier, why the filed fix is unsound:* P2-1's bulk half introduces a new defect.

The problem: the fix returns early from the commit's error and non-success branches when `selection !== lastSelection`, but it does not re-enable Confirm. If the operator clicked Continue on the fresh plan while the superseded commit was still pending, the go-commit handler (:624) disabled Confirm, because `committing` was true. When the stale response is dropped, `complete` only clears `committing` and hides the spinner.

Driven on a fix2 build: step 4 was left with Confirm disabled, no spinner and no message, and no request could be sent. That is the same "disabled with no reason" class the finding reports.

P2-1's reconcile half (capture `postedPlan = lastPlan`, return when `lastPlan !== postedPlan`) is sound. `complete`'s refreshDeleteButton() restores Delete. Driven: the late 409 was dropped and r2 was deleted (200).

The narrow fix that holds, bulk (_BulkScripts.cshtml, top of commitImport's `error` callback at :665):
`if (selection !== lastSelection) { $("#bulk-confirm-commit-btn").prop("disabled", false); return; }`
Leave the success callback alone. A superseded success was applied, so its summary and redirect are right. The non-success branch of `success` is unreachable, because every non-200 body from BulkCreateFromAzurePlan goes to `error`. Driven on a fix13 build:
- step 3: 200, committed;
- step 4: Confirm re-enabled, 200;
- case A: 200;
- a 409 for the current snapshot still voids exactly as 31-L13 intended.

Reconcile: P2-1's postedPlan guard (driven), or `if (ids !== confirmedIds) { return; }` at the top of the delete's `error` callback (:411).

Machinery-deleting alternative: revert 18dcabf. The two 409 branches go, and the hides return to the go-commit and go-confirm handlers. That clears this defect and the stale-banner variant, but restores 31-L13's original Low: a stale Confirm is left enabled after a current 409, the server refuses again, and Back followed by Continue erases the refusal.

Product question (unchanged from the candidate): the guard also drops a superseded status-0 or 500 "could not confirm whether this import was applied". Nothing is at risk, because a fresh commit is re-derived and a diverged plan answers 409, but the operator loses that notice.

*From the filing:* Contract: section 1 rule 3 of the product model (the operator must be able to act); a valid fresh plan's Confirm must work. The mechanism being guarded is 31-L13's own 409 branch. If the owner would rather delete machinery, reverting 18dcabf's two 409 branches and its hide move also clears this, but 31-L13's original defect returns.

*Product question:* Should a superseded commit's 500 ('BASTET could not confirm whether this import was applied') still be shown after the operator has re-previewed? If yes, let status 500 through the early return; it changes no wizard state.

**Residue of:** 31-L13

## L2 — Expand All / Collapse All repaint every toggle once per container, freezing the Subnet Hierarchy tab for seconds to tens of seconds `[x1]`
**Where:** src/Bastet/wwwroot/js/site.js:22, src/Bastet/wwwroot/js/site.js:27, src/Bastet/wwwroot/js/site.js:31-50 (updateToggleIcons: the full walk each callback repeats), src/Bastet/wwwroot/js/site.js:27 (Collapse All; same per-element callback), src/Bastet/wwwroot/js/site.js:31-50 (updateToggleIcons: the whole-tree read/write walk that each per-element callback repeats; not itself changed by the fix), src/Bastet/wwwroot/js/site.js:27 (Collapse All: the same per-element callback, quadratic when the parent rows are visible siblings, as with root-level VNet rows), src/Bastet/wwwroot/js/site.js:31-50 (updateToggleIcons: each per-element callback repeats this whole-tree walk; the :visible read at :44 alternating with the .html() writes at :45/:47 forces one layout per parent row)
**Breaks:** The operator's tree has the shape the bulk import produces: N VNet rows at the root, each holding its subnets. They click Expand All or Collapse All on /Subnet. Since 31-L14, both handlers pass updateToggleIcons as the slideDown/slideUp completion callback on the whole `.subnet-children` set. jQuery runs that callback once per element, so it runs N times in the tick where the animations finish. Each run walks every `.subnet-toggle`, alternating an `.is(':visible')` read (which forces layout) with an `.html()` write. That is N x N forced layouts in one main-thread task. Measured freezes on every click of either button: ~0.75 s at 100 VNet rows (300 rows), ~5.9 s at 250 (750 rows), ~41-42 s at 500 (1,500 rows). Before 31-L14, Expand All repainted once: 44 ms at 250 roots, 100 ms at 500. On a three-level tree the old Collapse All was already this slow (6.1 s), because its deeper-level callback had the same per-element shape; 31-L14 carried that shape to Expand All and to the root level. The final icons are correct; the defect is the hang, which grows with the square of the number of parent rows.
**Repro:** *Truth lens (yes-ran-it) — ran:* Work dir: /tmp/claude-1000/-home-anuj-code-Bastet/6f80fefd-4268-4c27-8569-c92f1e2329cd/scratchpad/rig/work/a10 (called WD below). I created no Azure resources.
1. Started the HEAD build (<rig>/app) twice from WD: `<rig>/bin/start-app.sh 5200 audit32_a10_tree none` and `<rig>/bin/start-app.sh 5201 audit32_a10_seq none`.
2. Seeded the catalogs with WD/seed.sh, which pipes T-SQL through `<rig>/bin/sql.sh`. Shape `two N` is N /24 roots, each with two /25 children (the bulk-import shape). Shape `three N` is one /16 root with N /24 children, each holding two /25s. Seeds run: two 100, two 250, two 500, three 250, two 6 and three 4. I also made a leaf-only tree and an empty tree by hand-written SQL.
3. Made three versions of site.js: `git show dd48723^:src/Bastet/wwwroot/js/site.js > WD/site-pre.js` (before the 31-L14 fix), `git show HEAD:src/Bastet/wwwroot/js/site.js > WD/site-head.js`, and WD/site-fix.js, which is site-head.js with sed rewriting line 22 to `$('.subnet-children').slideDown(200).promise().done(updateToggleIcons);` and line 27 to `...slideUp(200).promise().done(updateToggleIcons);`.
4. `<rig>/azcli/bin/python WD/drive.py <port> {head|pre|fix} <steps> [count]` runs Playwright Chromium against /Subnet. The page.route override serves the pre or fix script at **/js/site.js*. For each click it records:
   - the time from the click until `$('.subnet-children').promise()` resolves;
   - long tasks, from a PerformanceObserver;
   - optionally, the number of `$.fn.html` setter calls;
   - the final state of each toggle and each `.subnet-children` container.
   A `hide` step gives a like-for-like Expand All starting from a collapsed tree.
5. WD/seq.py ran 7 click sequences on HEAD and on the fix. WD/edge.py checked the leaf-only and empty trees. WD/realinput.py made real `page.click` presses and then timed a `page.evaluate('1')` sent 350 ms later. I ran it on the headless shell and again on `channel=chromium`.
6. Attribution: `git blame -s -L 20,28 origin/audit/round-31 -- src/Bastet/wwwroot/js/site.js`; `git log -S'slideUp(200, updateToggleIcons)' -- src/`; `git log -S'slideDown(200, updateToggleIcons)' -- src/`; `git show 8cefc64 -- src/Bastet/wwwroot/js/site.js`.
7. Cleanup: stopped both instances with `stop-app.sh <pidfile>`, ran `drop-catalog.sh` on audit32_a10_tree and audit32_a10_seq, and confirmed `git status --porcelain` is empty.

*Truth lens — came back:* **HEAD, two-level trees.**
- 100 roots (300 rows): each click took 723–757 ms, with one long task of 512–548 ms.
- 250 roots (750 rows): Collapse All took 6235 ms (task 6029). Expand All took 6153 ms (task 5939). Expand All from a collapsed tree took 6425 ms (task 6214).
- Each click at 250 roots made 62,500 icon `.html()` writes, which is 250 callbacks × 250 parent toggles. The fix makes 250.
- The freeze does not depend on anything changing. Expand All on a tree that was already expanded froze for 5485 ms, and a second Collapse All on a collapsed tree froze for 6446 ms.
- 500 roots (1,500 rows): Collapse All took 45,537 ms (task 45,317). Expand All took 43,212 ms (task 42,907).
- Real mouse clicks at 250 roots: the page stayed blocked for 5.70 s and 5.46 s on the headless shell, and 5.47 s and 5.51 s on full Chromium.
- Final icons and display were correct in every HEAD run, with 0 page errors.

**Before 31-L14 (site.js from `dd48723^`).**
- On a two-level tree, Collapse All did nothing: all rows stayed expanded (the 31-L14 defect).
- Expand All from a collapsed tree took 217 ms at 250 roots and 306 ms at 500, including the 200 ms animation. The candidate's 44 ms and 100 ms were measured on a tree that was already expanded, so its slideDown did nothing. This does not change the finding.

**Three-level tree (1 + 250 + 500 rows).**
- HEAD: Collapse All 755 ms; Expand All 6369 ms (task 6133).
- Before 31-L14: Collapse All 6422 ms (task 6209), which confirms the older per-element callback; Expand All 211 ms.
- Fix: 228 ms and 249 ms.

**The proposed fix.**
- Timings: 211–224 ms at 100 roots, 236–253 ms at 250 and 298–321 ms at 500, with correct final state each time.
- In all 7 sequences on a two-level and a three-level tree, HEAD and the fix ended in identical display and icon states. The sequences were: a root collapsed by hand then Collapse All; Collapse+Expand back-to-back; Expand+Collapse back-to-back; Collapse then Expand; a row toggle 80 ms into Expand All; a row toggle 80 ms into Collapse All; and Expand All clicked three times.
- Leaf-only and empty trees: no errors, and the leaf dashes were untouched.

*Reach lens (yes-ran-it) — ran:* All work in rig/work/a11 (ports 5210-5212, catalogs audit32_a11_tree / _three / _edge, principal none, HEAD build rig/app, site.js byte-identical to HEAD).
1. start-app.sh 5210 audit32_a11_tree none. seed.py posts the real Create form (POST /Subnet/Create, antiforgery token harvested with bs4, ParentSubnetId taken from the 302 Location). It built two-level trees in steps: 100, then 250, then 500 roots of /24, each with two /25 children. SQL check at the end: 1500 rows, 500 roots.
2. measure.py (Playwright, headless Chromium): a real page.click on #collapse-all and #expand-all, a capture-phase click timestamp, a longtask PerformanceObserver, then await $('.subnet-children').promise(). Comparison variants were served with page.route('**/js/site.js*'): the pre-31-L14 file (git show dd48723^:src/Bastet/wwwroot/js/site.js) and the candidate's fix (lines 22 and 27 changed to .slideDown(200).promise().done(updateToggleIcons) and .slideUp(200).promise().done(updateToggleIcons)).
3. count.py: a MutationObserver on each .subnet-toggle counts icon rewrites per click.
4. blocked.py: page.mouse.click on the first root's Details link 0.4 s after Collapse All, timing how long until the navigation request is sent. It also times Expand All on a freshly loaded tree.
5. start-app.sh 5211 audit32_a11_three none. seed3.py through the form: one /16 root, 250 /24 children, 500 /25 grandchildren (751 rows, 251 parent rows). Then measure.py for HEAD, the fix and pre-31-L14, and seq.py (8 sequence scenarios, HEAD vs fix, targeting both a deep row and the root row). The two-level 500-root tree also ran seq.py with the fix.
6. The same HEAD timing rerun in full Chrome 153 (channel=chromium). edge.py on 5212 audit32_a11_edge checks an empty tree and a leaf-only tree, HEAD vs fix.
7. Attribution: git blame -s -L 20,50 origin/audit/round-31 -- src/Bastet/wwwroot/js/site.js; git log -L 20,29:src/Bastet/wwwroot/js/site.js -s b406c60..origin/audit/round-31; git blame -s -L 26,36 b406c60 -- src/Bastet/wwwroot/js/site.js.
Cleanup: stop-app.sh on each of the three captured pidfiles and drop-catalog.sh on each catalog. No a11 catalog remains, 0 fail/crit log lines, git status --porcelain is empty, no Azure resources were created.

*Reach lens — came back:* Mechanism, proven directly. At 100 parent rows, HEAD rewrites the toggle icons 10,000 times per click of either button (100 per-element callbacks x 100 parent toggles). The fix and the pre-31-L14 code each rewrite them 100 times.

Timings, HEAD Collapse All / Expand All (click until the promise resolves, with the single longest task):
- 100 roots (300 rows): 842/855 ms, tasks 629/642 ms. A repeat gave 1008/822 ms.
- 250 roots (750 rows): 5904/6232 ms, tasks 5692/6019 ms. The fix: 244/249 ms, tasks 53/67 ms.
- 500 roots (1,500 rows): 46.8/44.2 s, tasks 46.6/44.0 s. The fix: 323/336 ms.
- Pre-31-L14 at 250 roots: Collapse All is the old no-op (all 250 containers stay displayed); Expand All takes 37 ms.

What the operator gets at 250 roots:
- Expand All on a freshly loaded, already-expanded tree, where nothing animates: HEAD 6.02 s (task 5958 ms), the fix 0.09 s.
- A click on a subnet's Details link 0.4 s after Collapse All is not processed until 5.84 s later at HEAD, 0.01 s with the fix. The tab ignores input for the whole freeze.

Three-level tree (251 parent rows):
- HEAD Expand All: 6076 ms (task 5862 ms). Pre-31-L14: 221 ms. The fix: 252 ms.
- HEAD Collapse All: only 737 ms (task 525 ms). The root container finishes first and hides the whole subtree, so the later callbacks lay out nothing.
- Pre-31-L14 Collapse All was already 6615 ms (task 6403 ms), because of the round-5 deeper-level callback.
- Full Chrome 153: HEAD Expand All 7.2 s (task 6945 ms).

Correctness:
- HEAD ended with correct icons and display in every run: 0 icon/display mismatches, leaves kept the flat dash, 0 page errors.
- The per-row toggle at 500 roots takes 0.35 s (task 96 ms), which is linear and not affected.
- With the fix, all 8 scenarios ended in states identical to HEAD with 0 mismatches and 0 errors, on both tree shapes and both target modes. The scenarios: a row collapsed by hand then Collapse All; a row collapsed by hand then Expand All; Collapse then Expand back-to-back; Expand then Collapse back-to-back; a row toggle during a Collapse All animation; a row toggle once and twice; Expand All on a fresh page.
- Empty tree and leaf-only tree: no errors, and the fix behaves the same as HEAD.

**Fix:** Repaint once, after every container has finished, keeping 31-L14's repaint-after-animation rule. site.js:22 becomes `$('.subnet-children').slideDown(200).promise().done(updateToggleIcons);` and site.js:27 becomes `$('.subnet-children').slideUp(200).promise().done(updateToggleIcons);`. `.promise()` resolves only when every element's fx queue is empty, including animations queued by back-to-back clicks. On an empty set it resolves at once, and the repaint is then a no-op. No new mechanism. The per-row toggle (one element, one callback) is already linear and stays as it is.

*Interim:* On large trees, use the per-row toggles instead of Expand All / Collapse All.

*Verifiers:* both judged the filed fix sound.

*Truth verifier:* The fix is sound, and I tested it live.

**Behaviour.**
- `.promise()` resolves only once every element's fx queue is empty, so the rule that 31-L14 added (repaint after the animation) still holds.
- The fix only reduces the number of repaints, from P to one. HEAD and the fix ended in the same final state in all 7 sequences on both tree depths, including back-to-back clicks and a row toggle queued behind the bulk animation.
- On an empty set the promise resolves immediately and the repaint does nothing, as the leaf-only and empty trees showed with no errors.
- `.done` runs synchronously when the promise resolves, so no frame is painted with stale icons.

**Constraints.**
- The vendored jQuery 4.0.0 has `.promise()`, confirmed in the minified source. It needs no CDN, so plain-HTTP and air-gapped deployments are unaffected.
- No `.cs` or `.cshtml` file is touched and no comment is needed.
- It adds, widens or branches no guard, withhold, refusal, special case or status. Two call expressions change, and the row toggle at `:17` stays as it is.

*Truth verifier's betterFix:* Keep the filed fix. site.js:22 becomes `$('.subnet-children').slideDown(200).promise().done(updateToggleIcons);` and site.js:27 becomes `$('.subnet-children').slideUp(200).promise().done(updateToggleIcons);`. Use `.done` rather than `.then`, because jQuery 4's `.then` is asynchronous and would let a frame render stale icons. Leave the row toggle at :17 and the startup repaint at :7 unchanged. Interim: use the per-row toggles on large trees.

*Reach verifier:* Sound. The candidate's fix was served live over /js/site.js: line 22 becomes $('.subnet-children').slideDown(200).promise().done(updateToggleIcons); and line 27 becomes $('.subnet-children').slideUp(200).promise().done(updateToggleIcons);.
- It repaints once. Icon rewrites per click drop from N squared to N (100 instead of 10,000 at 100 parents), and the wait falls to 221-336 ms, most of which is the 200 ms animation.
- .promise() waits until every element's animation queue is empty, including animations queued by back-to-back clicks and by a row toggle clicked mid-animation. The repaint therefore always reads the settled state.
- In all 8 sequence scenarios on both tree shapes, the final states were identical to HEAD, with no icon/display mismatch.
- On an empty or leaf-only set the promise resolves at once, the repaint is a no-op, and no error occurs.
- Line 17 (the per-row slideToggle) animates at most one element per click, so it fires one callback and must stay as it is.
No guard, status or branch is added, and no product question arises. Interim: use the per-row toggles on large trees.

*Reach verifier's betterFix:* None better. The candidate's two-line change is the narrowest correct fix. Two alternatives were considered and are worse. Batching the reads and writes inside updateToggleIcons would still run it once per element and rewrite icons N squared times. Painting known end-state icons synchronously would reintroduce the pre-31-L14 wrong icons after back-to-back clicks.

**Residue of:** 31-L14

## L3 — Both DeleteConfirmed timeout arms still stash TempData for a Delete page that answers 404, stranding 'Please try again' on the next page `[x1]`
**Where:** src/Bastet/Controllers/SubnetController.Delete.cs:114, src/Bastet/Controllers/HostIpController.cs:431, src/Bastet/Controllers/HostIpController.cs:431 (arms 431-435: TempData at :433, redirect at :434; reproduced at HEAD in D2 and closed by the guard on the fixed copy)
**Breaks:** A Delete-role operator confirms deleting subnet X. At that moment another subnet operation holds Bastet:SubnetOperations for at least 30 s, and X is deleted inside that window (by that operation or one queued ahead). The POST passes 31-L10's pre-lock SubnetExists check, waits 30 s and times out. The timeout arm then sets TempData 'The operation timed out because another subnet operation is in progress. Please try again.' and redirects to /Subnet/Delete/X, which now answers the 404 page '…could not be found or may have been deleted.' The error layout does not render TempData, so the banner surfaces on the next page that does, such as the subnet list, telling the operator to retry deleting a subnet that no longer exists. The host-IP delete behaves the same way (HostIpController.cs:431-435, 'The operation timed out due to high concurrency. Please try again.'). This is exactly the defect 31-L10 was filed for. 7e781ba closed it at both pre-lock gates and at SetAllocationStatus's timeout arm (HostIpController.cs:710-714), but not at these two timeout arms.
**Repro:** *Truth lens (yes-ran-it) — ran:* Ran the HEAD f8d1d7a published build (<rig>/app) with rig/bin/start-app.sh on 127.0.0.1:5290, catalog audit32_a19_main, principal none. The driver is <rig>/work/a19/drive.py (requests plus bs4/lxml). Operators A and B are separate cookie sessions. Every Delete form was harvested from the live page and POSTed with confirmation=approved. The long lock holders were sqlcmd sessions run through rig/bin/sql.sh with sp_getapplock 'Bastet:SubnetOperations' Exclusive/Session, and I read the queue order from sys.dm_tran_locks.

Drives:
- D0, the candidate's own method: one sqlcmd holder deletes the row by raw SQL at +5 s and holds for 45 s.
- D1: S1 holds 10 s. B POSTs /Subnet/Delete/2 and queues. S2 queues behind B and holds 45 s once granted. A POSTs /Subnet/Delete/2.
- D2: D1 for POST /HostIp/Delete ip=10.119.12.10.
- C1: D1 without S2.
- C2: S1 holds 40 s while A deletes a row that stays live.

Pure-product chain, with no sqlcmd lock holder, on a second HEAD instance (5291, audit32_a19_big) using work/a19/pp.py. I seeded 20,000 /28 rows by SQL to stand for two large hand-built trees. Then, 0.5 s apart: H1 is a product POST deleting an 8,000-subnet subtree, B deletes X (192.168.119.0/24, created through the form), H2 deletes a 12,000-subnet subtree, and A deletes X. I also timed real product deletes of 2,000- and 8,000-subnet subtrees.

Fix check: git archive HEAD -- . ':(exclude)test/' into work/a19/src. I added the candidate's two guards and ran dotnet publish -c Debug --disable-build-servers (0 warnings, 0 errors). The fixed build ran on 5292 (audit32_a19_mod) with D1, D2, C2 and the pure-product chain.

Attribution:
- git blame -s -L 108,119 origin/audit/round-31 -- src/Bastet/Controllers/SubnetController.Delete.cs
- git blame -s -L 431,436 origin/audit/round-31 -- src/Bastet/Controllers/HostIpController.cs
- git log -L 114,118:src/.../SubnetController.Delete.cs and -L 431,435:src/.../HostIpController.cs over b406c60..origin/audit/round-31
- git show e2d6128:docs/AUDIT-FINDINGS-31.md and 18dcabf:docs/AUDIT-FINDINGS-31.md

Teardown: stop-app.sh by captured PID (30990, 40932, 54931) and drop-catalog.sh on all three catalogs. No Azure resources were created. git status --porcelain stayed empty.

*Truth lens — came back:* At HEAD:
- D0: A's POST returned 302 /Subnet/Delete/1 after 30.02 s, then 302 /Error/404?m=..., then 404 'Not Found - BASTET'. A's next GET /Subnet showed 'The operation timed out because another subnet operation is in progress. Please try again.', and the reload was clean.
- D1: B's product delete committed at 10 s (row 2 archived). S2 was granted when B released and was still holding when A's POST returned after 30.01 s: 302 /Subnet/Delete/2, then 302 /Error/404, then 404. A's next /Subnet showed the same banner, and X was not listed.
- D2: after 30.01 s, 302 /HostIp/Delete?ip=10.119.12.10, then 404. The next /Subnet showed 'The operation timed out due to high concurrency. Please try again.'
- C1 (no second holder): A got the lock right after B and went 302 /Error/404, then 404, after 8.96 s. The next page had no banner.
- C2 (row still live): A landed on 'Delete Subnet - BASTET' with the banner in place, which is correct.
- Pure-product chain, no stand-ins: H1 held the lock for about 13 s. B's delete of X committed. H2 still held the in-process gate when A returned after 30.01 s: 302 /Subnet/Delete/40007, then 302 /Error/404, then 404. The banner appeared on A's next /Subnet.
- Timing: a real delete of a 2,000-subnet subtree took 2.5 s and one of 8,000 took 14.5 s. This is superlinear because ArchiveSubnetSubtreeAsync queries once per subnet (Delete.cs:203-213) and SubtreeSubnetIdsAsync, an O(N*S) walk, runs twice.

On the fixed copy: D1 went 302 /Error/404, then 404. D2 went straight to 404. The pure-product chain went 302 /Error/404, then 404. All three had no alerts on the next /Subnet. C2 was unchanged, with the banner still in place.

Logs: 0 fail/crit lines on 5290 and 5292. The 2 on 5291 came from my own malformed first seed.

Blame: every line of both arms is 6edef5c (#134) or 841c272 (#18). No round-31 commit touches them.

*Reach lens (yes-ran-it) — ran:* All work under /tmp/claude-1000/-home-anuj-code-Bastet/6f80fefd-4268-4c27-8569-c92f1e2329cd/scratchpad/rig/work/a20 (WD). The repo was never written; git status stayed empty.

(1) HEAD build, a mechanism check with the candidate's kind of stand-in: `start-app.sh 5300 audit32_a20_main none`, then WD/standin_subnet.py. X was created through /Subnet/Create. sqlcmd session A held sp_getapplock 'Bastet:SubnetOperations' for 15 s. A real Bastet POST /Subnet/Delete/X (op1) was queued at t=2, the victim POST at t=3, and sqlcmd session B was queued at t=5 and held the lock for 40 s.

(2) Measuring real lock-hold times: `start-app.sh 5301 audit32_a20_perf none`. Valid trees (aligned, contained, unique, parent-linked rows, the same rows the forms create) were seeded with WD/seed_tree.sh and WD/seed_tree2.sh. Each root was deleted with a real form POST (WD/time_delete.py).

(3) Race using real operations only, no sqlcmd lock (WD/real_race.py 5301 …). Q is 10.120.0.0/17 with 28,785 subnets (seeded). X is 10.120.127.0/24, created under Q through the Create form. Every Delete form was harvested with bs4. opB POSTed /Subnet/Delete/X at t=0, opC POSTed /Subnet/Delete/Q at t=0.08, and the victim POSTed /Subnet/Delete/X at t=0.16. Each session then GET /Subnet twice.

(4) Host-IP site in a real browser (WD/browser_race_hostip.py, Playwright Chromium). L (10.120.127.0/24) and H (10.120.127.10) were created through the real forms. opB deleted subnet L (which archives H) at t=0, and opC deleted Q at t=0.06. At t=0.17 the victim typed 'approved' and clicked 'Delete Host IP' on /HostIp/Delete?ip=H, then clicked 'Return to Home', then opened /Subnet, then reloaded. I also read the server's request log (WD/app-5301.log).

(5) Fix check: `git archive HEAD -- . ':(exclude)test/' | tar -x -C WD/src`, patched both timeout arms exactly as proposed, ran `dotnet publish … --disable-build-servers` (0 warnings, 0 errors), then `start-app.sh 5302 audit32_a20_fix none WD/app-fix`. I reran (3) and (4) against it. Positive control on the same build: a sqlcmd session held the lock for 36 s while rows that still exist were deleted from both Delete pages.

(6) Attribution: `git blame -s -L 108,119 origin/audit/round-31 -- src/Bastet/Controllers/SubnetController.Delete.cs`; `git blame -L 429,436` on HostIpController.cs; `git log -L 114,118:… b406c60..origin/audit/round-31` and `git log -L 431,435:…` on the same range; `git log -S'<each timeout sentence>' -- src/`; `git show 7e781ba -- src/`; `git show e2d6128:docs/AUDIT-FINDINGS-31.md` (the L10 entry); and the 31-L10 row of docs/AUDIT-LEDGER.md.

(7) Cleanup: stop-app.sh on 5300, 5301 and 5302; drop-catalog.sh on audit32_a20_main, audit32_a20_perf and audit32_a20_fix. No Azure resource was created.

*Reach lens — came back:* (1) op1 (the real delete of X) got the lock after 13.1 s and archived X. The victim's chain was [(302,'/Subnet/Delete/1',30.0),(302,'/Error/404?m=81749ed05eb2',30.0),(404)], ending on 'Not Found - BASTET'. Its next GET /Subnet showed ['The operation timed out because another subnet operation is in progress. Please try again.', 'No subnets found in the database.']. The GET after that showed only the second alert.

(2) Real delete POST durations on the rig's loopback SQL:
- 21 subnets: 0.2 s
- 1,089 subnets and 4,096 host IPs: 3.3 s
- 4,353 and 16,384: 7.0 s
- 8,321 and 16,384: 12.3 s
- 28,785 subnets: 36.7–38.2 s
That is roughly 1.3 ms per subnet. Deleting a trivial leaf in a 28.8k-row table holds the lock for 0.3 s. Catalogs created by BASTET_AUTO_MIGRATE have READ_COMMITTED_SNAPSHOT on.

(3) HEAD, real operations only: opB returned 302 /Subnet at 0.3 s and archived X at 02:02:02. opC finished at 36.7 s ('Subnet 'Q' and 28784 child subnet(s) were deleted successfully'). The victim's chain was [(302,'/Subnet/Delete/42570',30.0),(302,'/Error/404?m=e30f8881018c',30.0),(404)], ending on 'Not Found - BASTET'. Its next /Subnet showed 'The operation timed out because another subnet operation is in progress. Please try again.' above 'No subnets found in the database.', and the reload was clean. The opB and opC sessions showed nothing.

(4) HEAD, browser: the server log shows POST /HostIp/Delete answering 302 in 30,007.6 ms, then GET /HostIp/Delete?ip=10.120.127.10 answering 404 in 6.7 ms. Chromium showed 'Resource Not Found / Status Code: 404 / The resource you requested could not be found…'. 'Return to Home' showed no alert. The Subnets menu showed a visible, dismissible red alert 'The operation timed out due to high concurrency. Please try again.' above 'No subnets found in the database.' (WD/victim_next_page.png), and the reload was clean. No page JS errors. H was archived by opB's subnet delete.

(5) Patched build, same races:
- Subnet victim: [(302,'/Error/404?m=f2f0adff3971',30.0),(404)], and the next /Subnet showed only 'No subnets found in the database.'
- Host-IP victim: the POST itself answered 404 ('Not Found - BASTET'), and the Subnets menu showed no stale alert.
- Positive control, where the rows still exist at timeout: subnet [(302,'/Subnet/Delete/57573',30.0),(200)] 'Delete Subnet - BASTET' with the original timeout banner; host IP [(302,'/HostIp/Delete?ip=10.120.30.7',30.0),(200)] 'Delete Host IP - BASTET' with its banner. Both rows are still live.

(6) Both arms blame to 6edef5c 'Misc Cleanup (#134)' and 841c272 (#18), and `git log -S` on both sentences returns 6edef5c only. `git log -L` over b406c60..origin/audit/round-31 is empty for both ranges. 7e781ba added the pre-lock checks and SetAllocationStatus's timeout-arm check only.

**Fix:** A verifier judged the filed fix unsound: the truth-lens verifier did, while the reach-lens verifier judged it sound. The truth verifier's betterFix is given here instead of the filed fix.

*Truth verifier's betterFix:* File it as a product question for the owner, not as an omission in 31-L10. The owner chooses between two options.

(a) Accept it, with no code change. Add one clause to the 31-L10 ledger row, the durable record that round 31's close-out lost. The clause records the race-only strand at SubnetController.Delete.cs:114-118, HostIpController.cs:431-435 and the within-lock redirects to the Delete GET as accepted, so it is not re-filed. The standing instruction favours this option.

(b) Close the two arms with the candidate's guards. They copy the shape of SetAllocationStatus (HostIpController.cs:710-713), take 4 lines per arm, and are verified on a copy. They narrow the race but leave the within-lock redirects open.

Widen nothing beyond that. Do not clear TempData on 404 pages, and do not redirect the arms to Index. Either would degrade the common case, where the row still exists and the operator retries from the Delete page.

*Truth verifier, why the filed fix is unsound:* The guards are technically safe. I built both and ran them on a copy:
- D1, D2 and the pure-product chain each became a clean 404 with nothing stranded.
- C2's in-place banner was unchanged.
- The logs had 0 fail lines.
- The check runs after CloseConnectionAsync, on a fresh connection. In a database outage it fails the same way the redirected Delete GET already does.
- No comment is needed, and plain-HTTP and air-gapped deployments are unaffected.

They are still unsound under the lens, for three reasons.
- The fix adds a branch to both timeout arms. That widens 31-L10's existence-then-404 guard into two more catch blocks, which is the change round 31's L10 considered and declined as "adding mechanism" for a race-only residual. The owner's standing instruction (§8 31-L2) presumes it wrong.
- It does not close the class. Round 31 named the within-lock redirects to the Delete GET as the same race-only strand, which happens if the row is deleted before the browser follows the 302: SubnetController.Delete.cs:141-144, :150-153, :185-186 and HostIpController.cs:426-427. I did not drive those. A sub-millisecond gap also remains between the new check and the redirected GET.
- Its justification, "residue by omission", is false.

*Reach verifier, who judged the filed fix sound and gave no betterFix:* The fix is sound and narrow, and I proved it on a patched copy of HEAD built without test/ (0 warnings, 0 errors).

Add `if (!SubnetExists(id)) return this.RedirectToErrorPage(404, SubnetNotFoundMessage(id));` at the top of SubnetController.Delete.cs:114-118. Add `if (!HostIpExists(ip)) return NotFound();` at the top of HostIpController.cs:431-435.

Proof on the patched copy:
- It closes both reproduced races. The subnet victim goes straight to /Error/404 at 30.0 s. The host-IP POST answers 404 itself. No banner is stranded.
- It keeps the legitimate behaviour. When the row still exists at timeout, both arms still redirect to their Delete page with the original banner, and nothing is deleted.

Why it is the right mechanism:
- It reuses the existing helpers and exactly the shapes 7e781ba already ships: the two pre-lock gates and the SetAllocationStatus timeout arm. It serves §1 rule 3 and invents no new condition.
- No fix that removes mechanism is narrower. Dropping the TempData would lose the true banner for rows that still exist. Having the Delete GETs discard pending TempData was rejected in round 31 as wider, because it swallows other tabs' messages.

Known limits, neither worth more mechanism:
- If the slow holder is itself deleting X (X inside its own subtree) and commits between this check and the browser's follow-up GET, the banner can still strand. With READ_COMMITTED_SNAPSHOT, which auto-migrated catalogs have, that gap is milliseconds.
- An exception thrown by the check inside the catch would surface as the 500 page. The Delete GET it replaces hits the same database, so nothing gets worse, and SetAllocationStatus's arm has the same shape.

Interim: none needed. The banner is dismissible and gone on reload.

*The filed fix, which option (b) above takes unchanged as "the candidate's guards":* Mirror 7e781ba's SetAllocationStatus arm. At the top of each DeleteConfirmed timeout arm, check existence before setting TempData:
- Subnet: `if (!SubnetExists(id)) return this.RedirectToErrorPage(404, SubnetNotFoundMessage(id));`
- HostIp: `if (!HostIpExists(ip)) return NotFound();`

Contract: section 1 rule 3 of the product model (the operator must be able to act on what they are told).

Attribution note: blame on the cited lines gives 6edef5c (#134) and 841c272 (#18), both earlier than the audit rounds. The residue is by omission: these are the two sites of 31-L10's own defect class that its fix skipped.

**Residue of:** none

## L4 — Create's seeded child name can cut the parent's real name inside a surrogate pair, pre-filling and saving U+FFFD `[x1]` — FIXED
_Fixed in this commit (subject "L4: Create's seeded child name no longer cuts a character in half"). `SubnetNaming.WithSuffix` drops a trailing lone high surrogate after the cut, so the Create seed ends on a character boundary._
_Swept: every `WithSuffix` caller (the Create seed; the planner's name builders, which take only Azure names that ARM refuses to make astral) and every other name, description and note cut in src/ (all fire only above the form's own limits); nothing else reachable._
_Verified: `APrefillCutNeverSplitsACharacterInTwo` red on HEAD and green fixed; revert plus three one-edit mutants red; suite 1040/1040, 0 warnings; live Create seed 100 units ending U+FFFD on HEAD, 99 units clean on the fix._
_Reviewed: PASS by an independent reviewer on the live operator path through the CIDR modal (12 mutants; the three survivors cannot split a character on reachable input). It also showed the fix closes a planner preview/commit name-parity break on crafted astral input._

# Info

None.

# Refuted — reported by a finder, killed by the verifier

| candidate | title | reason |
| --- | --- | --- |
| K2 (P1-2) [x1] | Bulk import: a stale plan refused with HTTP 400 stays committable, because 31-L13 voids the reviewed snapshot only on 409 | truth: yes-ran-it; The mechanics are true at HEAD, but the behaviour is not a product defect.<br><br>Citations are accurate. `_BulkScripts.cshtml:704-707` is the 409 branch, `:709` re-enables Confirm on every other status, and `SubnetController.BulkAzure.cs:211-222` is the `!plan.CanCommit` 400. The rest of the finding does not hold:<br><br>(1) The premise is false: the 400 is not a second way of saying "the plan you reviewed is no longer what the server would do".<br>- For an item-error 400, the server answers 400 only after DescribeApprovedPlanDivergences finds the re-derived actions identical to the operator's approved Expected block.<br>- For a global-error 400, the divergence check is skipped, but every later POST re-runs it under the lock.<br>- So a 400 means "the reviewed plan is blocked right now", not "superseded". The 409 message tells the operator to re-run the preview; the 400 ("The import failed." plus the blocker) does not.<br>- Round 31's L13 rested on "the client keeps a snapshot its own server has just declared superseded". The server makes no such declaration with a 400.<br><br>(2) The live Confirm is working, safe, deliberate behaviour.<br>- The re-enable at :709 and the 400 both come from the original feature, 73fc76f (#108). 31-L13 kept the re-enable for non-409 statuses; its concrete fix was "branch on 409".<br>- Round 31's own verifier called this retry "provably safe, since the server re-derives and commits only what the operator approved".<br>- I drove it twice (cases a and b). Once the named blocker was removed, the byte-identical body committed 200 with exactly the previewed changes and no re-preview.<br>- The candidate's claim that "every 400 ... repeats identically when the same body is resubmitted" is false for the stale-state class it is about.<br><br>(3) The harmful half of 31-L13 is already closed for every status. "Two clicks erase the refusal" no longer happens: the refusal persisted through Back to Preview and Continue in both cases. "Back to Preview shows the superseded plan with no errors" is also true after a 409 at HEAD (case c), so it is not a state 31-L13 removed. The only difference is that Continue stays enabled, and that leads to the safe retry.<br><br>(4) No PRODUCT-MODEL sentence is broken.<br>- §1 rule 3: the 400 names no remedy the app refuses.<br>- §4: the retry is real work, since it commits the reviewed plan once the blocker is cleared.<br>- The candidate cites no contract and argues by analogy to 31-L13. It is a "this mechanism has a gap" finding. The skill says such findings perpetuate the loop, and this is the second finding this pass files against the same 31-L13 branch (P1-2 and P1-3).<br>- §8 31-L2's standing instruction presumes a widening proposal wrong and says to prefer leaving deliberate behaviour alone.<br><br>Nothing is written and nothing untrue is shown. The operator sees a truthful refusal every time. / reach: yes-ran-it; Refuted. The mechanics reproduce exactly. But the behaviour is not a product defect, it is not a regression, and the proposed fix is the kind of mechanism-widening the owner has ruled against.<br><br>(1) Nothing goes wrong for the operator.<br>- The endpoint answers 400 rather than 409 because the re-derived plan did NOT diverge: same target, parent, rename and child names. The reviewed plan is blocked, not superseded, so the candidate's description of it as 'the superseded plan' is inaccurate.<br>- The enabled Confirm is a working, safe retry. Once the blocker is removed, the byte-identical body commits exactly the reviewed plan: (a) link plus s3, (b) s6, both 200. Until then every resubmit is refused and nothing is written.<br>- At HEAD the refusal is never erased; it survives Back to Preview and then Continue.<br>- Nothing is written or lost, no address space is affected, and both recovery paths work.<br>- What remains is a repeat click that returns the same refusal, and a Preview tab showing the approved plan on a different tab from the refusal.<br>- Round 31's own L13 text rejected the 'offers work that is not work' framing for this identical resubmit ('the server re-derives the plan on every POST and commits only what matches the operator's approved expectations'). It named the erased refusal as what made it a finding, and that erasure no longer happens for the 400.<br>- The 409 tells the operator to 'Re-run the preview'. The 400 names a blocker and says nothing of the kind.<br><br>(2) Not a regression, and not residue.<br>- The behaviour comes from _BulkScripts.cshtml:709 (73fc76f 'Azure Bulk Import (#108)') and the controller's 400 at SubnetController.BulkAzure.cs:211-222 (#108). git log -L shows no round-31 commit touching either range.<br>- 31-L13's branch at 704-707 never runs for a 400.<br>- Driven live, b406c60 behaves identically except that its Continue erased the refusal. Round 31 improved the 400 path.<br>- Round 31's finding explicitly said 'branch on 409', and reconcile did exactly that. The candidate quotes that finding's '503 and status 0' clause as if it were a requirement.<br><br>(3) The fix widens 31-L13's mechanism with no PRODUCT-MODEL sentence behind it.<br>- §8 31-L2 presumes a proposal that 'adds, widens or branches a mechanism' is wrong: 'prefer leaving deliberate behaviour alone'. The skill says 'this mechanism has a gap' perpetuates the cycle. §4 prefers deleting a client re-derivation of a server decision over extending one.<br>- Live, the fix turns a 1-click recovery into a 5-click re-preview of the identical plan, and adds a silent disabled Continue on the Preview step.<br>- By the candidate's own note it also needs P1-3's new guard, or a late 400 voids a newer plan.<br>- The candidate's productQuestion is answered by that standing ruling. |
