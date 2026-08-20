using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Azure;
using Bastet.Services.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> BulkDeleteStaleAzureSubnets(
        [FromBody] AzureReconcileDeleteDto request,
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

        if (request.Confirmation != "approved")
        {
            return BadRequest(new { success = false, error = "You must type 'approved' to confirm deletion." });
        }

        if (request.SubnetIds is null or { Count: 0 })
        {
            return BadRequest(new { success = false, error = "No subnets were selected for deletion." });
        }

        if (request.Statuses is not null && request.Statuses.Exists(s => s is null))
        {
            return BadRequest(new { success = false, error = "An approved verdict entry was empty." });
        }

        AzureVNetInventory inventory = await azureService.GetVNetInventory(request.SubscriptionId);
        IReadOnlyList<AzureLinkedSubnetSnapshot> linked = await snapshotService.GetAzureLinkedSubnetsAsync();
        AzureReconcilePlanViewModel plan = reconciler.BuildPlan(request.SubscriptionId, inventory, linked);

        if (!plan.ScanSucceeded || plan.GlobalErrors.Count > 0)
        {
            return BadRequest(new
            {
                success = false,
                error = "Azure could not be re-checked, so nothing was deleted.",
                globalErrors = plan.GlobalErrors
            });
        }

        await AzureController.ConfirmProposedDeletionsAsync(plan, azureService, reconciler);

        Dictionary<int, AzureReconcileItem> stillStale = plan.Items.ToDictionary(i => i.SubnetId);
        List<int> noLongerStale = [.. request.SubnetIds.Where(id => !stillStale.ContainsKey(id))];

        Dictionary<int, AzureReconcileApprovedVerdict> approved =
            (request.Statuses ?? [])
                .Where(s => s is not null)
                .GroupBy(s => s.SubnetId)
                .ToDictionary(g => g.Key, g => g.Last());

        List<int> verdictChanged = [.. request.SubnetIds
            .Where(stillStale.ContainsKey)
            .Where(id => !VerdictMatchesApproval(stillStale[id], approved.GetValueOrDefault(id)))];

        Dictionary<int, AzureReconcileItem> held = plan.ReviewItems
            .Where(i => i.Status == AzureReconcileStatus.HeldByManualContent)
            .ToDictionary(i => i.SubnetId);

        List<int> heldIds = [.. noLongerStale.Where(held.ContainsKey)];

        if (heldIds.Count > 0)
        {
            return Conflict(new
            {
                success = false,
                error = $"{heldIds.Count} of the selected subnet(s) hold subnets or host IP assignments that were "
                        + "created here rather than imported from Azure, so nothing was deleted. "
                        + string.Join(" ", heldIds.Select(id => held[id].Reason)),
                subnetIds = heldIds,
                warnings = plan.Warnings
            });
        }

        if (noLongerStale.Count > 0)
        {
            return Conflict(new
            {
                success = false,
                error = $"{noLongerStale.Count} of the selected subnet(s) are no longer offered for deletion by the latest re-check, " +
                        "so nothing was deleted. Re-run the scan and review the results and any warnings shown.",
                subnetIds = noLongerStale,

                warnings = plan.Warnings
            });
        }

        if (verdictChanged.Count > 0)
        {

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

        try
        {
            IActionResult? failure = await subnetLockingService.ExecuteWithSubnetLockAsync<IActionResult?>(async () =>
            {
                using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
                    await context.Database.BeginTransactionAsync();

                try
                {

                    List<int> ordered = [.. request.SubnetIds
                        .Distinct()
                        .OrderBy(id => stillStale[id].Cidr)];

                    HashSet<int> alreadyArchived = [];

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

                            continue;
                        }

                        List<int> archivedIds = [];
                        (int archivedSubnets, int archivedHostIps) =
                            await ArchiveSubnetSubtreeAsync(subnet, subnetTree, archivedIds);

                        foreach (int archivedId in archivedIds)
                        {
                            alreadyArchived.Add(archivedId);
                        }

                        subnetsArchived += archivedSubnets;
                        hostIpsArchived += archivedHostIps;
                        targetsDeleted++;
                    }

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
}
