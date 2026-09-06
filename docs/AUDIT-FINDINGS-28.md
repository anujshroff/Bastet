# Bastet - Round-28 Audit Findings

Branch: audit/round-28
HEAD: 8891320
Test baseline: 950 passed / 0 failed / 0 build warnings
Date: 2026-09-06

Round 28 filed 12 findings, of which 4 are residue of previous rounds' fixes (23-L2-remainder x3, 17 (round-level; ledger has no per-finding ids before round 18) x1). Funnel: 20 raw from 18 finders -> 15 merged (4 x2, 11 x1) -> 12 survived, 3 refuted by verification, 0 dropped at merge.

GROWTH:
src/Bastet/Controllers/HostIpController.cs: 731 lines (was 731)
src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml: 702 lines (was 702)
src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml: 113 lines (was 286)
test/Bastet.Tests/SubnetManagement/SubnetDetailsModalScriptTests.cs: 101 lines (was 0; not present at 1d4c81b)

# Critical

# High

# Medium

# Low

## L1 - Create refusal tells the operator to select a container that the same validator then refuses (host IPs / fully allocated) `[x1]` `strings` — FIXED
_Fixed in this commit (strings batch with L3, L4, L8, L10). ValidateSubnetCreation now asks, before naming bestParent as the remedy in either branch, whether it holds host IPs or is marked fully allocated (one private helper, RefuseWhenContainerCannotHoldChildren); if so the NetworkAddress error states the fact and no remedy: "This subnet falls inside X (net/cidr), which has host IP assignments | is marked fully allocated, so it cannot be created."_
_Swept: the selected-parent gates (:131-149) and the bulk import planner (per-subnet refusals) are unchanged; no other Create/Edit message names a parent as a remedy._
_Verified: pin SubnetCreateContainerRefusalTests (4 refusal rows red before, green after; 2 control rows keep the existing remedies where the container can hold children); full suite 969/969, 0 warnings. Browser: host-IP container and fully-allocated container, parent omitted and parent = grandparent, all four show the fact sentence; selecting the container itself still shows the existing refusal._
_Reviewed: batch review pending._

## L2 - HostIp Edit redisplay renders a blank Subnet and 'Created 1/1/0001 12:00 AM' after a lock timeout, validation failure or save error `[x1]` — FIXED
_Fixed in this commit. One private `RedisplayEditAsync(ip, viewModel)` reloads the host IP with its subnet (AsNoTracking), returns NotFound when it is gone, stamps SubnetInfo/CreatedAt/LastModifiedAt and returns the view; all four redisplay returns (validation errors incl. the conflict branch, lock timeout, DbUpdateConcurrencyException, indeterminate/generic save error) use it and the inline reload in the conflict branch is deleted._
_Swept: HostIp Create's two redisplay paths and DeleteConfirmed already reload the subnet; Subnet Edit reloads (SubnetController.Edit.cs); no other HostIp view model is redisplayed without its sidebar data._
_Verified: pins HostIpEditRedisplayTests - lock timeout and ModelState-invalid red before (SubnetInfo ""), green after; the stale-RowVersion case is green before and after and guards the conflict branch now routed through the helper (reverting that one return reddens it). Full suite 959/959, 0 warnings. Browser: Edit with Name `<b>bold</b>` and Edit with a stale RowVersion both redisplay "hostsub (172.16.2.0/24)" and the stored Created date._
_Reviewed: (c) acceptable. Reviewer rebuilt 6ce5d27, drove the property-error, stale-RowVersion and a real 60 s sp_getapplock hold (30.6 s timeout banner, sidebar intact, later edit succeeded), and reverted each helper call one at a time: three of the four sites are pinned; the DbUpdateConcurrencyException catch is not (no SQLite seam without a hand-bumped RowVersion; judged beyond the §5 rule). Noted behaviour change: a host IP deleted between GET and POST now answers 404 on the redisplay, matching the Edit GET._

## L3 - Rename-only prefix reason asserts "Everything in this prefix is already imported" above subnets the same screen badges "Cannot import" `[x1]` `strings` — FIXED
_Fixed in this commit (strings batch). The rename-only prefix path now appends "The only change would be renaming the Bastet subnet to match the VNet name." to the planner's reason, the shape the subnet-row path already used, instead of replacing it with "Everything in this prefix is already imported."_
_Swept: the subnet-row rename-only path (:273-275) already appends; no other client string overrides a planner reason._
_Verified: no unit seam (client-side render of a live plan) - recorded in the ledger row and as an /e2e phase F assertion. Browser against live Azure (rig-28r-simple 10.61.0.0/16): imported the target and s1, hand-carved 10.61.2.0/25 inside s2, renamed the target to drifted-simple; rename off: "Already imported as Bastet subnet 'drifted-simple'. Every Azure subnet in this prefix is either already recorded or cannot be imported, so there is nothing to add."; rename on: same sentence + the rename sentence, badge Rename only, s2 badged Cannot import beneath; with the filter on the hidden-row count line follows the same reason._
_Reviewed: batch review pending._

## L4 - Edit sidebar tells the operator CIDR is the modifiable value on an Azure-linked row whose form has fixed it `[x1]` `strings` — FIXED
_Fixed in this commit (strings batch). Edit/_InformationSidebar.cshtml declares `@model EditSubnetViewModel` and branches on the same Model.IsAzureLinked the form uses: linked rows lose the CIDR bullet and the rules block and read "Network address and CIDR cannot be changed here."; unlinked rows render the previous text._
_Swept: HostIp Edit's sidebar ("Host IP addresses cannot be changed") is true; no other static sidebar describes a gated field as editable._
_Verified: pin SubnetEditViewSourceTests red before, green after; full suite 969/969. Browser: a row with AzureResourceId set renders the readonly+disabled CIDR input and the linked sidebar text with no CIDR bullet or rules; an unlinked row renders the CIDR bullet and rules._
_Reviewed: batch review pending._

## L5 - CIDR modal validates a number-input string with parseInt, so 24.5 is approved and handed to a Create form that refuses it (and 2e1 = /20 is refused) `[x1]` — FIXED
_Fixed in this commit. The modal reads `this.valueAsNumber` and posts `prop('valueAsNumber')`, so the table lookup decides (24.5 and NaN index nothing and fall into the existing refuse branch; 2e1 is 20); no integer check, no step attribute._
_Swept: `parseInt` over number inputs - one sibling in Create/_SubnetFormScripts.cshtml (the CIDR preview) now indexes `cidrInfo` by `valueAsNumber` and branches on the lookup, so 24.5 shows "Invalid CIDR" instead of the /24 preview; the reconcile checkbox and bulk data-attribute parseInt calls read ids, not number inputs, and are untouched._
_Verified: pin CidrModalScript_ReadsTheCidrInputAsANumber red before, green after; full suite 953/953, 0 warnings; browser on the rebuilt app: 24.5 refused (Create disabled, "Invalid"), 2e1 accepted as /20 (URL cidr=20, POST created the subnet), 24 unchanged, empty and 40 refused; Create form preview 24.5 -> "Invalid CIDR", 2e1 -> 255.255.240.0, no page errors. Recorded in /e2e phase F._
_Reviewed: (c) acceptable. Reviewer rebuilt b3f82d4, drove 21 modal inputs plus keyboard stepping and the Create-form preview including `?cidr=` prefills, reverted each changed line and confirmed the pin fails; judged the Create-form sibling a within-finding sweep that deletes a second copy of the valid-CIDR decision._

## L6 - HostIp Create renders every model-level error twice, and HostIp Edit shows an empty 'Error' alert for property-level errors `[x2]` — FIXED
_Fixed in this commit. Deleted Views/HostIp/Create/_ErrorAlert.cshtml and its render line in Create.cshtml, leaving the ModelOnly tag-helper summary in Create/_HostIpForm.cshtml as the single renderer; Edit/_Header.cshtml's hand-rolled "Error" block is replaced by the same single ModelOnly summary._
_Swept: no other view carries a `ModelState[""]` loop or an ErrorCount-gated alert; Subnet Create/Edit already use the single summary._
_Verified: pin HostIpViewSourceTests.HostIpCreateAndEdit_RenderModelLevelErrorsExactlyOnce red before, green after; full suite 959/959. Browser: Create with an out-of-range IP shows exactly one alert; Create and Edit with `<b>x</b>` show no alert box and the field message only; Edit with a stale RowVersion shows one conflict alert._
_Reviewed: (c) acceptable. Reviewer confirmed one alert per model-level refusal (out of range, network address, duplicate, stale RowVersion), none for property-level errors, and no empty summary box on Create GET, Edit GET or after a success._

## L7 - 'Back to Host IPs' on the deleted-host-IPs page links to HostIp/Index, which refuses subnets that have children or are fully allocated, so the link lands on an error banner `[x1]` — FIXED
_Fixed in this commit. DeletedHostIps.cshtml now renders the single "Back to Subnet" anchor to Subnet/Details, which never refuses; the @inject and both role branches are deleted._
_Swept: the other links into HostIp/Index (Details' "View All Host IPs", the Create/Edit/Delete Cancel buttons and the Create breadcrumb) are reachable only for a subnet that already holds host IPs, which SetAllocationStatus refuses to mark fully allocated and Create refuses to give children, so Index cannot refuse them; no CanAddHostIp-style property added._
_Verified: pin DeletedHostIpsViewSourceTests red before, green after; full suite 959/959. Browser: subnet with a deleted host IP and a child subnet - the deleted-host-IPs page's only back link is Back to Subnet -> /Subnet/Details/1 with no alert._
_Reviewed: (c) acceptable. Reviewer drove child-subnet, fully-allocated, plain and empty-state cases plus AllDeletedHostIps; confirmed the deleted View-role branch was unreachable under RequireViewRole and the remaining Index links cannot reach the refusal._

## L8 - Wizard read-path failures are always reported as 'Error connecting to server:' (blank on transport failure, HTTP reason on 5xx), and the reconcile page wraps every scan failure in a static 'Because Azure could not be read' `[x1]` `strings` — FIXED
_Fixed in this commit (strings batch). Both wizard scripts define readErrorMessage(xhr) - "The server could not be reached." for status 0, otherwise "The server returned status N." - and the five read-path handlers (bulk subscriptions, VNets, preview; reconcile subscriptions, scan) use it; _StepReview.cshtml's static paragraph no longer blames Azure: "BASTET cannot tell which resources still exist, so nothing is offered for deletion. Fix the problem shown above and scan again."_
_Swept: the two commit handlers keep 25-L3's outcome-unknown wording; no other handler prints errorThrown; "Error connecting to server" is gone from both scripts._
_Verified: pins in AzureWizardClientWordingTests (function shape, exactly 3/2 call sites, old text absent, _StepReview wording) red before, green after; full suite 969/969. Browser: aborted GetSubscriptions on both wizards -> "The server could not be reached."; 503/500 -> "The server returned status 503/500."; BulkGetVNets 503 -> status 503; reconcile scan with the database OFFLINE -> "The reconcile scan failed. Details have been logged." followed by the new paragraph; aborted ReconcileScan -> could not be reached._
_Reviewed: batch review pending._

## L9 - CIDR modal keeps the adjusted address and its "adjusted to avoid overlaps" note on screen while refusing with "No compatible network address found" `[x1]` — FIXED
_Fixed in this commit. `refuse()` now resets the network address to `activeSuggestion.startIp` and clears the adjustment note itself; the duplicate reset in the undefined branch is deleted, so every refusal (out of range, empty, no home) shows the clicked range start._
_Swept: both refuse() call sites (the undefined and null branches) reset now; the click handler's own `val(activeSuggestion.startIp)` is the modal-open path and is unchanged._
_Verified: pin CidrModalScript_RefuseResetsTheNetworkAddressToTheRangeStart red before, green after; full suite 954/954, 0 warnings; browser on 10.11.0.0/24 carved 10.11.0.0/32 + 10.11.0.128/25: 26 -> 10.11.0.64 adjusted with note; 25 -> refused, address 10.11.0.1, no warning border, note hidden, Create disabled; 27 -> 10.11.0.32 adjusted again; empty -> reset. L5 re-driven green. /e2e phase F sentence extended._
_Reviewed: (c) acceptable. Reviewer drove the single- and two-range layouts including cancel-and-reopen and an adjusted Create round trip; reverting the reset into the undefined branch fails the pin. Correction adopted: the sweep line above now says two call sites._

## L10 - Refusal sentence "No compatible network address found for this CIDR size." asserts a parent-wide absence, but the table only searched at or after the clicked range `[x1]` `strings` — FIXED
_Fixed in this commit (strings batch). The null-branch refusal reads "No free /N block starts at or after A.B.C.D." with size text "Invalid - none free at or after this range"; no arithmetic change and the at-or-after axis is kept._
_Swept: SubnetDetailsModalScriptTests pins the new sentence and rejects the old; /e2e phase F now records the refusal as range-scoped and that the earlier range's row offers the block._
_Verified: pin red before, green after; full suite 969/969. Browser on 10.1.0.0/24 carved /26 + /27 + /26: the 10.1.0.128 row with /26 reads "No free /26 block starts at or after 10.1.0.128." with Create disabled._
_Reviewed: batch review pending._

## L11 - Suggestion property suite has no layout with a short tail range, so a scope-hoist mutant of SuggestChildSubnets stays green `[x2]` — FIXED
_Fixed in this commit. Test-only: appended the Layouts row `{ "10.0.0.0", 24, ["10.0.0.128/26", "10.0.0.240/29"] }` so the suggestion theory covers a tail free range shorter than a block an earlier range holds._
_Swept: the ranges theory shares the row and stays green; no production change._
_Verified: scratch tree pristine 38/38; hoisting `lowestBlockAtOrAfter` above the cidr loop fails exactly the new row ("Assert.Null() Failure: Value is not null"); restored 38/38._
_Reviewed: (c) acceptable. Reviewer reproduced 38/38 pristine and the single-row failure under the hoist mutant._

## L12 - CidrModalScript_OnlyIndexesTheServerSuggestionTable binds token presence, not the lookups: a client-side usable-count or address re-derivation in the modal stays green `[x2]` — FIXED
_Fixed in this commit. Test-only: CidrModalScript_OnlyIndexesTheServerSuggestionTable now binds the two use sites (the `const address = activeSuggestion.networkAddressByCidr[cidrValue];` lookup and every `#subnetSizeDisplay` write, exactly three, each `usableByCidr[...]` or `sizeText`) and widens the client-arithmetic denylist to `**`, `16777216`, `65536`, `split('.')` and `[*/%] 256`._
_Swept: the ClientArithmeticIdentifiers theory kept as the literal-revert guard; denylist has zero hits on the pristine script._
_Verified: scratch tree pristine 15/15; mutant P6 (usable count re-derived with `2 ** (32 - cidrValue)`) and P7 (address re-derived from the start IP) each fail exactly this test._
_Reviewed: (c) acceptable. Reviewer reproduced P6 and P7 each failing exactly the bound test, zero pristine hits on the widened denylist._

# Info

# Refuted - reported by a finder, killed by the verifier

| id | title | reason |
|---|---|---|
| b8p2-2 | SqliteSubnetLockingService is production-dead since 24-I2 deleted the provider dispatch; it survives in src only because one test instantiates it | Fact reproduced but not a finding: PRODUCT-MODEL section 5 as amended by the round-26 section 8 rulings defines a finding as an operator-visible wrong behaviour reproducible at HEAD or a broken-rule test finding, "nothing else"; a production-dead class produces no operator-visible behaviour. The cited "deletes machinery" sentence is section 3's Reconcile withhold contract. Earlier Info dead-code rows (23-I5, 24-I2, 24-I9) predate the ruling, and this is exactly the class it targets: a finding the loop generated from its own prior fix. |
| b8p1-2 | Hidden form inputs posted to Subnet Edit and HostIp Create are never read; EditSubnetViewModel.OriginalCidr is write-only | The candidate's own scenario concedes posting these fields "changes nothing the operator sees or the database stores" and the live drive confirms it; dead-input cleanup is not a finding under section 5, and no decision is implemented twice because the posted copy is never consulted. The "never read" claim is also false: when the subnet is deleted between GET and POST, HostIpController.Create's subnet==null branch (:117-124, :163-170 skip the re-stamp) renders the posted SubnetInfo/NetworkAddress/Cidr/SubnetRange, the only thing keeping that page from a blank name, "/0" and empty range; the filed fix and interim would introduce that regression. |
| b8p1-3 | UpdateHostIpDto data annotations never execute and its Name/RowVersion members are never read | Accurate description of the code but not a finding: nothing executes the attributes or reads the members, so there is no reachable wrong output. The cited section 5 write-path parity sentence is not violated: the DTO is not a write path and Create/Edit still accept and refuse identical input via their view models. UpdateHostIpDto exists solely to feed the `dto.IP != originalIp` branch of ValidateHostIpUpdate, which section 7 lists as accepted and never re-filed; trimming the DTO around it is a slice of the same accepted dead machinery and "while we're here" work. |
