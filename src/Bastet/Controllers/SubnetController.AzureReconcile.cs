using Bastet.Models.ViewModels;
using Bastet.Models;
using Bastet.Services.Azure;
using Bastet.Services.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
    /// <summary>
    /// POST: Subnet/BulkDeleteStaleAzureSubnets — deletes Bastet subnets whose Azure resources are gone.
    ///
    /// The client sends subnet IDs, never a plan. We re-scan Azure and the Bastet tree here and only
    /// delete rows that are still reported stale, so a stale browser view, a concurrent edit, or a
    /// resource that reappeared in Azure cannot cause the wrong subnets to be archived. Everything
    /// runs in one transaction and reuses the same archive path as the single-subnet delete.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> BulkDeleteStaleAzureSubnets(
        [FromBody] AzureReconcileDeleteDto request,
        [FromServices] IAzureService azureService,
        [FromServices] IAzureReconciler reconciler,
        [FromServices] IAzureSubnetSnapshotService snapshotService)
    {
        // Feature flag guard — same as the AzureController endpoints
        if (!AzureController.IsAzureImportEnabled())
        {
            return StatusCode(403, new { success = false, error = "Azure Import feature is not enabled" });
        }

        if (request is null)
        {
            return BadRequest(new { success = false, error = "No request was provided." });
        }

        // Same typed confirmation the single-subnet delete requires, validated server-side
        if (request.Confirmation != "approved")
        {
            return BadRequest(new { success = false, error = "You must type 'approved' to confirm deletion." });
        }

        // System.Text.Json overwrites the DTO's collection initialiser with null when the body
        // carries an explicit null, so the initialiser is not a guarantee. Same shape the batch
        // create path uses, and the difference matters: this path answers with modelled JSON, and
        // dereferencing instead produces an HTML 500 the wizard cannot read.
        if (request.SubnetIds is null or { Count: 0 })
        {
            return BadRequest(new { success = false, error = "No subnets were selected for deletion." });
        }

        // The collection can be non-null and still contain a null ELEMENT: Statuses holds reference
        // types, and System.Text.Json materialises "statuses":[null] as a one-element list holding
        // null. Grouping over it dereferenced that element and threw before this action's only try
        // block, so the documented JSON API answered an HTML 500 and the wizard rendered
        // "Server error: 500" in place of a modelled message - the exact failure the sibling bulk
        // path documents itself as existing to prevent.
        //
        // Refused here with its own message rather than only filtered below, and before the ARM
        // round trip is paid. A mixed [null, {valid}] body IS malformed, and silently dropping the
        // element would let it through to a destructive write; filtering alone also answers it with
        // "the reason ... has changed since you reviewed them", which is misleading when nothing
        // changed and the caller simply sent garbage.
        if (request.Statuses is not null && request.Statuses.Exists(s => s is null))
        {
            return BadRequest(new { success = false, error = "An approved verdict entry was empty." });
        }

        // Re-scan against live Azure and the current tree
        AzureVNetInventory inventory = await azureService.GetVNetInventory(request.SubscriptionId);
        IReadOnlyList<AzureLinkedSubnetSnapshot> linked = await snapshotService.GetAzureLinkedSubnetsAsync();
        IReadOnlyList<ExistingSubnetSnapshot> existing = await snapshotService.GetExistingSubnetsAsync();
        AzureReconcilePlanViewModel plan = reconciler.BuildPlan(request.SubscriptionId, null, inventory, linked, existing);

        // A failed scan produces no items, so this also covers "Azure was unreachable"
        if (!plan.ScanSucceeded || plan.GlobalErrors.Count > 0)
        {
            return BadRequest(new
            {
                success = false,
                error = "Azure could not be re-checked, so nothing was deleted.",
                globalErrors = plan.GlobalErrors
            });
        }

        // Read every proposed row from Azure directly before archiving anything: the plan above is
        // built from an RBAC-filtered listing, in which a resource this credential can no longer see
        // is indistinguishable from one that was deleted. Rows Azure will not confirm as gone are
        // dropped from plan.Items here, so the staleness check below refuses them for free.
        await AzureController.ConfirmProposedDeletionsAsync(plan, azureService, reconciler);

        // Only delete what the fresh scan still considers stale
        Dictionary<int, AzureReconcileItem> stillStale = plan.Items.ToDictionary(i => i.SubnetId);
        List<int> noLongerStale = [.. request.SubnetIds.Where(id => !stillStale.ContainsKey(id))];

        // Set membership is not consent. Check the row is still stale FOR THE REASON THE OPERATOR
        // SAW - the endpoint's own docstring promises "a resource that reappeared in Azure cannot
        // cause the wrong subnets to be archived", and comparing ids alone does not deliver that.
        // Ordered before the membership refusal only in the sense that both are computed here; the
        // membership message is more specific, so it still wins when both apply.
        // Second layer. The guard above already refuses a null element, so this cannot fire today;
        // it is here so that a future caller reaching this line by another route degrades to
        // "no approved verdict" - which VerdictMatchesApproval documents as licensing nothing -
        // rather than throwing. Same two-layer shape the maintainers chose on the bulk-import path.
        Dictionary<int, AzureReconcileApprovedVerdict> approved =
            (request.Statuses ?? [])
                .Where(s => s is not null)
                .GroupBy(s => s.SubnetId)
                .ToDictionary(g => g.Key, g => g.Last());

        List<int> verdictChanged = [.. request.SubnetIds
            .Where(stillStale.ContainsKey)
            .Where(id => !VerdictMatchesApproval(stillStale[id], approved.GetValueOrDefault(id)))];

        if (noLongerStale.Count > 0)
        {
            return Conflict(new
            {
                success = false,
                error = $"{noLongerStale.Count} of the selected subnet(s) are no longer reported as deleted in Azure. " +
                        "Nothing was deleted. Re-run the scan and review the results.",
                subnetIds = noLongerStale,
                // Carries the reason a row was withheld - most usefully "Azure would not confirm it
                // is deleted", which otherwise looks indistinguishable from an out-of-date scan.
                warnings = plan.Warnings
            });
        }

        if (verdictChanged.Count > 0)
        {
            // Deliberately not merged with the message above. "No longer reported as deleted" and
            // "flagged for a different reason" call for different operator actions: the first means
            // the row is fine, the second means it is still wrong but wrong in a way they have not
            // seen and might well choose to re-import rather than archive.
            return Conflict(new
            {
                success = false,
                error = $"The reason {verdictChanged.Count} of the selected subnet(s) were flagged has changed since " +
                        "you reviewed them. Nothing was deleted. Re-run the scan and review the results.",
                subnetIds = verdictChanged,
                warnings = plan.Warnings
            });
        }

        int subnetsArchived = 0;
        int hostIpsArchived = 0;
        int targetsDeleted = 0;

        // Only the database work is guarded (and holds the global subnet lock - the Azure re-scan
        // above must not run while holding it). Building the response happens after the commit, so
        // a failure there can't send us into a rollback of an already-committed transaction - which
        // would throw and mask the real error while the rows were already gone.
        //
        // Residual race, accepted: the staleness verdict above is fixed before the lock is taken,
        // while each subtree is read after it. Acquiring the lock can wait behind another operation,
        // and in that window a concurrent write can add a child to a target - which is then archived
        // along with it, having never appeared in a reviewed plan. Left as is deliberately: Azure
        // cannot be re-checked while holding the lock, the single-subnet delete has the same
        // confirm-then-cascade semantics, and everything here is archived rather than destroyed.
        // Closing it would mean comparing each subtree against stillStale[id].DescendantSubnetIds
        // inside the lock and failing with 409 on any difference.
        try
        {
            IActionResult? failure = await subnetLockingService.ExecuteWithSubnetLockAsync<IActionResult?>(async () =>
            {
                using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
                    await context.Database.BeginTransactionAsync();

                try
                {
                    // Parents first (smaller CIDR = larger network). Archiving a parent takes its whole
                    // subtree, so a selected child may already be gone by the time we reach it - skip those
                    // rather than failing on a missing row.
                    List<int> ordered = [.. request.SubnetIds
                        .Distinct()
                        .OrderBy(id => stillStale[id].Cidr)];

                    HashSet<int> alreadyArchived = [];

                    // Read the tree once for the whole batch rather than twice per target. Every
                    // target's subtree is archived from this one snapshot: subtrees are disjoint, so
                    // rows removed by an earlier target are never named by a later one. Tracking, not
                    // AsNoTracking - ArchiveSubnetSubtreeAsync removes these very instances, and a
                    // detached duplicate throws once a target has descendants.
                    List<Subnet> subnetTree = await context.Subnets.ToListAsync();

                    foreach (int subnetId in ordered)
                    {
                        if (alreadyArchived.Contains(subnetId))
                        {
                            continue;
                        }

                        Subnet? subnet = await context.Subnets.FindAsync(subnetId);
                        if (subnet is null)
                        {
                            // Cascaded away as part of an earlier subtree in this same transaction
                            continue;
                        }

                        List<int> archivedIds = [];
                        (int archivedSubnets, int archivedHostIps) =
                            await ArchiveSubnetSubtreeAsync(subnet, subnetTree, archivedIds);

                        // Covers the target itself as well as its descendants.
                        foreach (int archivedId in archivedIds)
                        {
                            alreadyArchived.Add(archivedId);
                        }

                        subnetsArchived += archivedSubnets;
                        hostIpsArchived += archivedHostIps;
                        targetsDeleted++;
                    }

                    // One save for the batch. It was per-target, which re-ran DetectChanges over
                    // every tracked entity once per target while holding the global write lock; the
                    // whole loop is a single transaction committed just below, so nothing between
                    // iterations needs the rows flushed.
                    await context.SaveChangesAsync();

                    await transaction.CommitAsync();
                    return null;
                }
                catch (Exception ex) when (SqlSaveOutcome.IsIndeterminateTransaction(ex))
                {
                    logger.LogError(ex, "Azure reconcile delete outcome unknown");
                    await TransactionCleanup.RollbackQuietlyAsync(transaction, logger);
                    return StatusCode(500, new { success = false, error = "BASTET could not confirm whether this delete was applied. Re-run the scan to see the current state before retrying." });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Azure reconcile delete failed");
                    await TransactionCleanup.RollbackQuietlyAsync(transaction, logger);
                    return StatusCode(500, new { success = false, error = "The delete failed and no changes were saved. Details have been logged." });
                }
            });

            if (failure is not null)
            {
                return failure;
            }
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new { success = false, error = "The operation timed out because another subnet operation is in progress. Nothing was deleted. Please try again." });
        }

        TempData["SuccessMessage"] =
            $"Azure reconcile: deleted {targetsDeleted} stale subnet(s), archiving {subnetsArchived} subnet(s) " +
            $"and {hostIpsArchived} host IP assignment(s) in total.";

        return Ok(new
        {
            success = true,
            redirectUrl = Url.Action("Index", "Subnet"),
            targetsDeleted,
            subnetsArchived,
            hostIpsArchived
        });
    }

    /// <summary>
    /// True when the freshly derived verdict is the same one the operator approved.
    /// </summary>
    /// <remarks>
    /// Three cases are all treated as a divergence rather than as consent:
    /// a MISSING verdict (nothing was approved for this row, so nothing licenses archiving it),
    /// an UNPARSEABLE status name (a caller-supplied string that names no status establishes
    /// nothing - the same rule DescribeApprovedPlanDivergences applies to TargetType), and a
    /// matching status whose REASON differs (same verdict, different facts: a prefix that has moved
    /// again re-derives as SubnetPrefixChanged both times while naming a different live prefix).
    /// </remarks>
    private static bool VerdictMatchesApproval(AzureReconcileItem current, AzureReconcileApprovedVerdict? approvedVerdict)
    {
        if (approvedVerdict is null)
        {
            return false;
        }

        return Enum.TryParse(approvedVerdict.StatusName, ignoreCase: true, out AzureReconcileStatus approvedStatus)
               && approvedStatus == current.Status
               && string.Equals(approvedVerdict.Reason, current.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// POST: Subnet/RelinkAzureSubnet — re-points a Bastet subnet at the Azure subnet that now holds
    /// its range, after a rename or a prefix move left the recorded resource ID naming nothing.
    ///
    /// Azure has no subnet rename, so re-organising one is delete-and-recreate. Before this existed
    /// the resulting row could only be archived — which made BASTET advertise a range Azure had
    /// already assigned as free space — because nothing in the application could edit
    /// <see cref="Subnet.AzureResourceId"/>. This is the repair path that makes withholding those
    /// rows from deletion a correction rather than a dead end.
    /// </summary>
    /// <remarks>
    /// The caller supplies no resource ID. We re-scan Azure and re-derive the link here, and accept
    /// only a row the fresh plan itself reports as RangeStillAllocatedInAzure, so neither a stale
    /// browser view nor a crafted post can point a subnet at an arbitrary resource.
    /// </remarks>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> RelinkAzureSubnet(
        [FromBody] AzureRelinkDto request,
        [FromServices] IAzureService azureService,
        [FromServices] IAzureReconciler reconciler,
        [FromServices] IAzureSubnetSnapshotService snapshotService)
    {
        if (!AzureController.IsAzureImportEnabled())
        {
            return StatusCode(403, new { success = false, error = "Azure Import feature is not enabled" });
        }

        if (request is null)
        {
            return BadRequest(new { success = false, error = "No request was provided." });
        }

        AzureVNetInventory inventory = await azureService.GetVNetInventory(request.SubscriptionId);
        IReadOnlyList<AzureLinkedSubnetSnapshot> linked = await snapshotService.GetAzureLinkedSubnetsAsync();
        IReadOnlyList<ExistingSubnetSnapshot> existing = await snapshotService.GetExistingSubnetsAsync();
        AzureReconcilePlanViewModel plan = reconciler.BuildPlan(request.SubscriptionId, null, inventory, linked, existing);

        if (!plan.ScanSucceeded || plan.GlobalErrors.Count > 0)
        {
            return BadRequest(new
            {
                success = false,
                error = "Azure could not be re-checked, so nothing was changed.",
                globalErrors = plan.GlobalErrors
            });
        }

        AzureReconcileItem? target = plan.ReviewItems.FirstOrDefault(i =>
            i.SubnetId == request.SubnetId
            && i.Status == AzureReconcileStatus.RangeStillAllocatedInAzure
            && !string.IsNullOrEmpty(i.SuggestedAzureResourceId));

        if (target is null)
        {
            return Conflict(new
            {
                success = false,
                error = "This subnet is no longer reported as holding a range that moved to another Azure subnet. "
                        + "Nothing was changed. Re-run the scan and review the results."
            });
        }

        try
        {
            IActionResult? failure = await subnetLockingService.ExecuteWithSubnetLockAsync<IActionResult?>(async () =>
            {
                Subnet? subnet = await context.Subnets.FindAsync(request.SubnetId);

                if (subnet is null)
                {
                    return NotFound(new { success = false, error = "That subnet no longer exists." });
                }

                // Re-check under the lock. The plan was built before it was taken, and a concurrent
                // import could have re-linked this row in the meantime - in which case the verdict
                // this action rests on is about a state that no longer exists.
                if (!string.Equals(subnet.AzureResourceId, target.AzureResourceId, StringComparison.OrdinalIgnoreCase))
                {
                    return Conflict(new
                    {
                        success = false,
                        error = "This subnet's Azure link changed while the scan was being reviewed. Nothing was changed."
                    });
                }

                subnet.AzureResourceId = target.SuggestedAzureResourceId;
                await context.SaveChangesAsync();
                return null;
            });

            if (failure is not null)
            {
                return failure;
            }
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new
            {
                success = false,
                error = "The operation timed out because another subnet operation is in progress. Nothing was changed. Please try again."
            });
        }

        logger.LogInformation(
            "Azure reconcile: re-linked subnet {SubnetId} to {AzureResourceId}",
            request.SubnetId, target.SuggestedAzureResourceId);

        // Deliberately no TempData here. This endpoint answers AJAX with no redirectUrl and the
        // wizard never navigates - it just re-scans - and Views/Azure/Reconcile.cshtml does not
        // render _TempDataAlerts, so nothing consumed the entry. ASP.NET Core only removes a
        // TempData entry when it is READ, so it survived request after request and then rendered as
        // a green success banner on whatever page happened to render the partial next - measured
        // landing on /Subnet/Delete/{id}, a destructive confirmation page, five loads later. The
        // client already gives correct feedback by re-scanning, and the re-link is logged above.

        return Ok(new
        {
            success = true,
            subnetId = request.SubnetId,
            azureResourceId = target.SuggestedAzureResourceId,
            azureSubnetName = target.SuggestedAzureSubnetName
        });
    }
}
