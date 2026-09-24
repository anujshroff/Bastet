# Bastet — Round-34 Audit Findings

branch `audit/round-34` · HEAD `b17adb8` · test baseline 1047/1047 passing · 2026-09-23 · residue rate 9/11

> Round 34 filed 11 findings, of which 0 are residue of round 33's own fixes. Round 33 made no fixes (it filed 0 findings, and `3af5afd..HEAD -- src/` is empty), and 9 of the 11 are residue of an earlier round's fix: 18-R15 (L1), 18-R6 (L3), 25-L2 (L4), 18-R5 (L5) and 18-R3 (L9), plus four at round granularity with no per-finding ledger id, round 16 (M2), round 6 (L6) and round 17 (L7, L8).

Growth: src/Bastet/Controllers/HostIpController.cs 743 lines at HEAD, 743 at the previous audit commit (3af5afd).
Growth: src/Bastet/Controllers/SubnetController.Edit.cs 280 lines at HEAD, 280 at the previous audit commit (3af5afd).
Growth: src/Bastet/Services/Azure/AzureBulkImportPlanner.cs 942 lines at HEAD, 942 at the previous audit commit (3af5afd).
Growth: src/Bastet/Controllers/SubnetController.BulkAzure.cs 511 lines at HEAD, 511 at the previous audit commit (3af5afd).

# Critical

None.

# High

None.

# Medium

## M1 — A host-IP delete confirmation archives whichever record holds that address when it is submitted, even one renamed or re-created after the operator reviewed it `[x1]` — FIXED
_Fixed in "Audit 34 M1: bind the host-IP delete confirmation to the reviewed row". The Delete page posts the reviewed row's RowVersion and DeleteConfirmed refuses inside the lock, back to the Delete page, when it is missing or differs (round 17's subnet pattern, f4a0a87)._
_Swept: every irreversible single-record path: the subnet delete was already bound, purges act on immutable archive rows, the subnet cascade keeps §8 18-R2's count; no other unbound path; the e2e route note now names the new form field._
_Verified: HostIpDeleteStaleRowTests (10 tests), 4 red on the unfixed code; each mutant red (full revert, check dropped or inverted, null token accepted, GET stamp or form input dropped, input outside the form, check hoisted before the lock, word gate bypassed); live on two replicas: stale rename and delete-and-re-create refused with the current name shown, re-confirm archives, controls unchanged; suite 1079/1079, 0 warnings._
_Reviewed: (c) preference, no failure found; its three optional corrections adopted (form-scoped pin, in-lock pin, e2e note)._
_Not done: product question for the owner, does §8 18-R2's count tolerance extend to the single host-IP delete page? The rename half is bound either way._

## M2 — Subnet delete archives a child created while the confirmation page was loading: the page leaves it out of the child count, but the scope guard already covers it `[x1]` — FIXED
_Fixed in "Audit 34 M2: take the subnet delete page's count and scope from one walk". The Delete GET takes the shown child count and the posted scope bound from one subtree walk; CountAllDescendants, whose only caller that was, is deleted._
_Swept: no other caller; the POST's in-lock check and §8 18-R2's host-IP count are unchanged; every subnet insert runs under the global lock and nothing re-parents a row, so the max-id bound stays valid._
_Verified: SubnetDeletePageSnapshotTests (14 tests: a child inserted at each GET command boundary, plus a deep-tree round trip), 4 red on the unfixed code; mutants red (full revert, count or bound from a second walk or from the Include, a Min() bound; the walk-order swap stays green as an equivalent); live race, 41 attempts each: HEAD 6 over-deletions, fix 0; suite green, 0 warnings._
_Reviewed: (c) preference, 0 over-deletions in 164 unpadded and 30 padded races; its two test-hardening notes adopted, its one-helper preference declined._

# Low

## L1 — Edit page and Edit refusal tell the operator to change an Azure-linked prefix in Azure and re-import it, but the import wizard refuses the re-import until the old row is deleted `[x2]` `strings` — FIXED
_Fixed in "Audit 34 L1 L4 L6 L8 L9: make five operator messages name only what is true and reachable". The Edit form and the Edit CIDR refusal on an Azure-linked row now say "Change it in Azure, then ask an administrator to delete this subnet and import it again."_
_Swept: the only other "import it again" text in src/ (the bulk commit's) already names the delete; with the import flag off both sentences still end at the fact._
_Verified: the refusal pin and a new Edit-form pin red on the old wording; live: an imported subnet's Edit page and a CIDR-change POST show the new sentence; suite green, 0 warnings._
_Reviewed: none found; the remedy checked reachable (Delete and import are Admin-reachable, the Edit role is refused both, hence "ask an administrator")._

## L2 — With 'Rename matched Bastet subnets' ticked, adopting an unlinked hand-built row renames it to the VNet name on range match alone `[x2]` — FIXED
_Fixed in "Audit 34 L2: rename a matched target only when it already carries this VNet's link". BuildPlanItem renames an exact target only when the row is already linked to this VNet, the one condition §4 names; the annotation's WouldRenameTarget, the same decision asked a second time, is gated the same way so it agrees with the plan._
_Swept: child renames were already link-gated; the commit acts only on the plan's WillRename, re-planned under the lock with its Expected check; RenameOnlyCandidate, the flag's only reader, already required a linked row, so nothing on screen changes but the rename; the toggle's label claims nothing about unlinked rows._
_Verified: new and corrected tests (unlinked adoption with and without children, fully allocated, a row linked to a different VNet, a differently cased id, annotation-plan agreement, two tests that assumed an unlinked row would be renamed given its link), red on the unfixed code; mutants red (full revert, inverted gate, any link counts, ordinal id comparison, annotation flag ungated, always rename); live: HEAD renamed two hand-built rows on adoption, the fix linked them under their own names and a second pass offered Rename only and renamed; suite green, 0 warnings._
_Reviewed: (c) preference, including a nine-prefix mixed browser run and a preview-to-commit race answered by a precise 409; its three notes adopted (case test, the equal-names test given its link, the annotation flag)._

## L3 — The bulk import wizard keeps offering a 'Rename only' for a child subnet whose name it chose itself, and the rename changes nothing `[x2]` — FIXED
_Fixed in "Audit 34 L3: stop suffixing children that share their target's name". BuildPlanItem no longer seeds its child-name collision set with the target's own names, so a child named like its VNet or its adopted target keeps its Azure name and an existing 'X (X)' row gets one real rename._
_Swept: the annotation (ProposedChildName) and the plan now agree for every Azure-producible selection; DisambiguateName stays for sibling collisions, reached only by a renames-off selection holding a linked sibling hand-renamed to a new subnet's Azure name (e.g. a stale second wizard tab), unchanged from HEAD, whose later offer is a real rename._
_Verified: six new AzureBulkImportChildNamingTests, red on the unfixed code with the audit's exact names; each seed restored alone (existing, renamed, created, children-only, inside-a-container) reds its test; live: HEAD made 'X (X)' rows and a no-op rename, the fix renamed both for real with nothing offered after, and a fresh import used the Azure names; suite green, 0 warnings._
_Reviewed: (c) preference; its rationale correction (stale-tab reachability) and both test additions adopted._
_Not done: product question for the owner, may an imported child carry the same name as its VNet target row? §4 and the schema read yes and the fix relies on it._

## L4 — ARM throttling or a server error on the subscription list is shown as 'Could not authenticate with Azure' `[x2]` `strings` — FIXED
_Fixed in "Audit 34 L1 L4 L6 L8 L9: make five operator messages name only what is true and reachable", then the gate repair "Audit 34 L4: drop the credential banner's unbacked logging claim". The Failed credential banner on both Azure pages now reads "Could not list subscriptions from Azure. The sign-in may have failed, Azure may be unreachable from this host, or Azure may have refused, throttled or failed the request. Reload this page once the cause is resolved."_
_Swept: both page actions carried the one literal; AzureService, the credential tri-state and the controller branches are unchanged; every other "Details have been logged" in src/ is LogError-backed, and the client panel below the banner still points at the log truthfully._
_Verified: AzureCredentialBannerTests pin the sentence at both actions, red on each earlier wording; live under persistent ARM 500, 429 and 503, a 401, a transport failure and a null client, at the default and at the Error log level, both pages show it; a transient failure and no fault show none after a reload; suite green._
_Reviewed: none at first; the whole-diff gate's correctness lens then showed the first wording's "Details have been logged" unbacked at the documented Error log level (the failure is logged at Warning); repaired once by dropping the claim; repair review: none._
_Not done: product question for the owner, should the server banner be deleted instead, leaving the client's "Failed to load subscriptions" panel as the only message on the page?_

## L5 — The bulk import commit marks a subnet that holds host IPs fully allocated when the posted encompassing subnet carries the VNet's own resource id `[x1]` — DEFERRED
_Deferred: its fix would be the fourth fix-commit this round in AzureBulkImportPlanner.cs (L3, L2 and the strings batch with L8 took the three the cap allows); of the four Low findings in that file it is the one only a hand-built request reaches._
_Transplanted whole, repro included, to docs/DEFERRED-FINDINGS.md; nothing in this round's diff touches the waiver it describes._

## L6 — When a confirming read fails, reconcile says 'Nothing is wrong with the subnet itself' about rows whose Azure resource is deleted `[x1]` `strings` — FIXED
_Fixed in "Audit 34 L1 L4 L6 L8 L9: make five operator messages name only what is true and reachable". The reconcile Unknown warning no longer says "Nothing is wrong with the subnet itself"; it now reads "... the read failed rather than answering. Try the scan again. They have been withheld from deletion: ..."._
_Swept: the string existed once; the scan panel and the delete 409's warnings list both come from that line._
_Verified: new AzureReconcilerTests pin red on the old wording; live: with the VNet listing emptied and 503 on the confirmation reads the scan withheld all three rows with the new warning, and the reviewer saw the same sentence in the delete 409; suite green._
_Reviewed: none found._

## L7 — When the in-lock host IP lookup fails, host IP delete sends its 'could not confirm' banner to All Host IPs, which never shows it; it appears later on an unrelated page `[x1]` — FIXED
_Fixed in "Audit 34 L7: show the stranded host-IP delete banner on All Host IPs". All Host IPs now renders the shared TempData alerts partial above its list, as All Deleted Host IPs already does._
_Swept: every TempData write in the controllers lands on a page that renders it; the in-lock lookup fallback in HostIpController is the only redirect to All Host IPs; no controller change._
_Verified: AllHostIpsTempDataTests red on the unfixed view and for the line deleted, moved into either branch, Razor- or HTML-commented, or gated; live: a schema lock timing out the in-lock lookup lands on All Host IPs with the banner above the count and the next page clean; the archive-lock control unchanged; suite green, 0 warnings._
_Reviewed: (c) preference, no failure found (19 other redirect flows showed no stray or duplicate alert); its whole-line pin tightening adopted._
_Not done: when the IP sorts past row 50 the banner lands on page 1 without that row; the redirect-to-Delete alternative the finding weighed would avoid it and was not taken._

## L8 — Bulk import wizard describes adopting an unlinked Bastet row as adding or importing subnets and never says the row will be linked, which is the only change the commit makes `[x1]` `strings` — FIXED
_Fixed in "Audit 34 L1 L4 L6 L8 L9: make five operator messages name only what is true and reachable". An unlinked exact row the VNet would adopt is described with the wizard's adoption sentence "Will import into existing Bastet subnet 'X'."; the top-up sentence is kept for rows already linked to this VNet._
_Swept: the childless branch already used the adoption sentence and the fully-allocated link-only branch has its own; the preview's Exact match line carries no link flag (question below)._
_Verified: AzureBulkImportAdoptionReasonTests red on the old wording for an unlinked row with children; live: a hand-built parent with a child against a matching VNet reads the adoption sentence, and after adoption the linked row reads the top-up sentence; suite green._
_Reviewed: none found; naming the link instead was judged a preference (the Select legend defines "imports into")._
_Not done: product question for the owner, should the preview's Exact match line say that a row will be linked? It would need a new plan field._

## L9 — Edit conflict message says 'Reload the page to see the current values', but reloading resubmits the stale form and returns the identical refusal; for host IPs the page's URL answers 404 `[x1]` `strings` — FIXED
_Fixed in "Audit 34 L1 L4 L6 L8 L9: make five operator messages name only what is true and reachable". Both Edit conflict messages, the host IP Edit sidebar and both Edit pages' indeterminate-outcome sentences now name "Use Cancel, then Edit" instead of a reload that re-POSTs the stale form, and the host IP Edit redisplay points Cancel at the host IP's current subnet._
_Swept: every "reload" left in src/ sits on a GET page or in a JSON answer, where a reload does what it says; HostIpValidationService.cs:109 never renders; the two indeterminate sentences (no unit seam) are recorded in /e2e._
_Verified: conflict, sidebar and redisplay pins red on the old code; live: both conflicts name the new step and Cancel then Edit loads the other operator's values, also after the host IP was re-recorded in a re-created subnet or a new child; a held row lock timing out each save shows both indeterminate sentences with nothing written; suite green._
_Reviewed: (a) on first review, because Cancel followed the posted subnet id and dead-ended after a re-record elsewhere; revised once with the reviewer's one-line correction and its pin; re-review: none found (H1, H2 and both indeterminate pages re-driven; a re-save of the redisplayed form still writes nothing)._

# Info

None.

# Refuted — reported by a finder, killed by the verifier

| id | title | reported by | killed because |
|---|---|---|---|
| G1 | Another site can sign the operator out: /Account/Logout ends the session on a cross-site GET without an antiforgery token | b1p2-1 | Deliberate, owner-accepted behaviour, not a defect: the tokenless GET Logout is original (e05c3b1; the layout link from #35), the owner's #133 (aedd0bd) recorded logout CSRF as accepted in source ("the worst outcome is an unwanted sign-out"; a POST-only Logout would 405 external GET logout links), and rounds 10, 15 (65d1fc6, which fixed only the third-party harm and deliberately kept the signed-in self-sign-out) and 16 each edited the method and left it a tokenless GET; no PRODUCT-MODEL sentence covers session integrity and the fix adds an antiforgery guard, hardening that §5 excludes and the §8 31-L2 standing instruction presumes wrong (the truth verifier also judged the fix unsound); the operator-visible sign-out did not reproduce on the rig (truth: could not, the operator stayed authenticated and the sign-out path exists only in Production; reach: ran it, only a pending "created successfully" banner was dropped, the row still created), so Info at most |
| G13 | README still advertises the deleted per-target Azure import and other Azure UI that later fixes removed or renamed | b7p1-1, b7p2-1 | Out of scope, not a product defect: both verifiers ran it and every claim reproduces, but the only untrue text is README.md prose that the application never builds, publishes or renders (the Dockerfile copies only the publish output); at HEAD the product behaves as PRODUCT-MODEL §3 and ledger fixes 23-L5, 18-R20 and 18-R17 require (single-VNet wizard routes answer 404 and nothing offers them, the rename label reads "to their Azure names", the only cascade note counts child subnets, the unrecognised-link reason ends at what is true); a finding must be an operator-visible wrong behaviour with a src/ citation (§5, the audit skill, BRIEF §4), b7p2-1 has no src/ site and b7p1-1's src/ sites are places where the product is right; stale documentation is the §6 class; the beat wording that brought docs into scope is not in the audit skill and cannot widen the owner's definition; no ledger row has ever filed a README finding; the drift is real and cheap to fix as a README edit for the owner outside the audit loop (Low if ever ruled in scope) |
| G14 | README tells operators to 'correct or clear' an unrecognised Azure link, a remedy the product refuses | b7p2-2 | Out of scope and unreachable: every claim reproduces (truth), but the only site is README.md:220, not src/, and there is no src/ site to correct it to, because every in-product text for the state is already true (the reconcile reason at AzureReconciler.cs:68-70 fixed by 18-R17, the Needs-review explainer at Reconcile/_StepReview.cshtml:73-76, the Edit sidebar at Subnet/Edit/_InformationSidebar.cshtml:8-17) and the product does exactly what §3 prescribes (no re-link, nothing edits the Azure link); no real operator can reach the state (reach: could not), since the only writer of AzureResourceId is the bulk import commit and the wizard posts only ARM listing ids (AzureService.cs:101 and :119), so it takes a direct DB write or a hand-made Admin JSON body naming a non-VNet id; even then "clear" works (Details, then Delete, archived the rows) and "correct" works (delete, then re-import through the wizard, the §8 31-L2 operation); a Low strings one-sentence README edit if docs are ruled in scope |
