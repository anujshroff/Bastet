using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

        if (request.SubnetIds.Count == 0)
        {
            return BadRequest(new { success = false, error = "No subnets were selected for deletion." });
        }

        // Re-scan against live Azure and the current tree
        AzureVNetInventory inventory = await azureService.GetVNetInventory(request.SubscriptionId);
        IReadOnlyList<AzureLinkedSubnetSnapshot> linked = await snapshotService.GetAzureLinkedSubnetsAsync();
        AzureReconcilePlanViewModel plan = reconciler.BuildPlan(request.SubscriptionId, null, inventory, linked);

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

        // Only delete what the fresh scan still considers stale
        Dictionary<int, AzureReconcileItem> stillStale = plan.Items.ToDictionary(i => i.SubnetId);
        List<int> noLongerStale = [.. request.SubnetIds.Where(id => !stillStale.ContainsKey(id))];

        if (noLongerStale.Count > 0)
        {
            return Conflict(new
            {
                success = false,
                error = $"{noLongerStale.Count} of the selected subnet(s) are no longer reported as deleted in Azure. " +
                        "Nothing was deleted. Re-run the scan and review the results.",
                subnetIds = noLongerStale
            });
        }

        int subnetsArchived = 0;
        int hostIpsArchived = 0;
        int targetsDeleted = 0;

        // Only the database work is guarded (and holds the global subnet lock - the Azure re-scan
        // above must not run while holding it). Building the response happens after the commit, so
        // a failure there can't send us into a rollback of an already-committed transaction - which
        // would throw and mask the real error while the rows were already gone.
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

                        List<Subnet> descendants = await GetAllDescendantsOrdered(subnetId);
                        (int archivedSubnets, int archivedHostIps) = await ArchiveSubnetSubtreeAsync(subnet);

                        foreach (Subnet descendant in descendants)
                        {
                            alreadyArchived.Add(descendant.Id);
                        }

                        alreadyArchived.Add(subnetId);
                        subnetsArchived += archivedSubnets;
                        hostIpsArchived += archivedHostIps;
                        targetsDeleted++;

                        await context.SaveChangesAsync();
                    }

                    await transaction.CommitAsync();
                    return null;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    logger.LogError(ex, "Azure reconcile delete failed");
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
}
