using Bastet.Models;
using Bastet.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{

    [Authorize(Policy = "RequireDeleteRole")]
    public async Task<IActionResult> Delete(int id)
    {
        Subnet? subnet = await context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subnet == null)
        {
            return this.RedirectToErrorPage(404, $"The subnet with ID {id} could not be found or may have been deleted.");
        }

        int descendantCount = await CountAllDescendants(id);

        int hostIpCount = subnet.HostIpAssignments.Count;

        hostIpCount += await CountAllDescendantHostIps(id);

        DeleteSubnetViewModel viewModel = new()
        {
            Id = subnet.Id,
            Name = subnet.Name,
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            Description = subnet.Description,
            ChildSubnetCount = descendantCount,
            HostIpCount = hostIpCount
        };

        return View(viewModel);
    }

    private async Task<int> CountAllDescendantHostIps(int subnetId)
    {

        List<Subnet> allSubnets = await context.Subnets
            .Include(s => s.HostIpAssignments)
            .ToListAsync();

        int hostIpCount = 0;

        HashSet<int> processedIds = [];

        Queue<int> queue = new();
        queue.Enqueue(subnetId);
        processedIds.Add(subnetId);

        while (queue.Count > 0)
        {
            int currentId = queue.Dequeue();

            List<Subnet> childSubnets = [.. allSubnets.Where(s => s.ParentSubnetId == currentId)];

            foreach (Subnet? child in childSubnets)
            {
                if (!processedIds.Contains(child.Id))
                {

                    hostIpCount += child.HostIpAssignments.Count;

                    queue.Enqueue(child.Id);
                    processedIds.Add(child.Id);
                }
            }
        }

        return hostIpCount;
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireDeleteRole")]
    public async Task<IActionResult> DeleteConfirmed(int id, string confirmation)
    {

        if (confirmation != "approved")
        {
            TempData["ErrorMessage"] = "You must type 'approved' to confirm deletion.";
            return RedirectToAction(nameof(Delete), new { id });
        }

        try
        {

            return await subnetLockingService.ExecuteWithSubnetLockAsync(() => DeleteConfirmedCore(id));
        }
        catch (TimeoutException)
        {
            TempData["ErrorMessage"] = "The operation timed out because another subnet operation is in progress. Please try again.";
            return RedirectToAction(nameof(Delete), new { id });
        }
    }

    private async Task<IActionResult> DeleteConfirmedCore(int id)
    {

        Subnet? subnet = await context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subnet == null)
        {
            return this.RedirectToErrorPage(404, $"The subnet with ID {id} could not be found or may have been deleted.");
        }

        using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        try
        {
            (int subnetsArchived, int hostIpsArchived) = await ArchiveSubnetSubtreeAsync(subnet);

            await context.SaveChangesAsync();

            await transaction.CommitAsync();

            TempData["SuccessMessage"] = $"Subnet '{subnet.Name}' and {subnetsArchived - 1} child subnet(s) were deleted successfully. " +
                                       $"{hostIpsArchived} host IP assignment(s) were archived.";

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {

            logger.LogError(ex, "Subnet delete failed for subnet {SubnetId}", id);
            await TransactionCleanup.RollbackQuietlyAsync(transaction, logger);
            TempData["ErrorMessage"] = "Error deleting subnet. Details have been logged.";
            return RedirectToAction(nameof(Delete), new { id });
        }
    }

    private async Task<(int SubnetsArchived, int HostIpsArchived)> ArchiveSubnetSubtreeAsync(
        Subnet subnet, List<Subnet>? treeCache = null, List<int>? archivedSubnetIds = null)
    {

        List<Subnet> toDelete = await GetAllDescendantsOrdered(subnet.Id, treeCache);
        toDelete.Add(subnet);

        archivedSubnetIds?.AddRange(toDelete.Select(s => s.Id));

        string? deletedBy = userContextService.GetCurrentUsername();
        DateTime deletedAt = DateTime.UtcNow;

        List<HostIpAssignment> allHostIps = [];
        foreach (Subnet subnetToProcess in toDelete)
        {
            Subnet? subnetWithHostIps = await context.Subnets
                .Include(s => s.HostIpAssignments)
                .FirstOrDefaultAsync(s => s.Id == subnetToProcess.Id);

            if (subnetWithHostIps != null && subnetWithHostIps.HostIpAssignments.Count > 0)
            {
                allHostIps.AddRange(subnetWithHostIps.HostIpAssignments);
            }
        }

        foreach (HostIpAssignment hostIp in allHostIps)
        {
            context.DeletedHostIpAssignments.Add(new DeletedHostIpAssignment
            {
                OriginalIP = hostIp.IP,
                Name = hostIp.Name,
                OriginalSubnetId = hostIp.SubnetId,
                CreatedAt = hostIp.CreatedAt,
                LastModifiedAt = hostIp.LastModifiedAt,
                CreatedBy = hostIp.CreatedBy,
                ModifiedBy = hostIp.ModifiedBy,
                DeletedAt = deletedAt,
                DeletedBy = deletedBy
            });

            context.HostIpAssignments.Remove(hostIp);
        }

        foreach (Subnet subnetToDelete in toDelete)
        {
            context.DeletedSubnets.Add(new DeletedSubnet
            {
                OriginalId = subnetToDelete.Id,
                OriginalParentId = subnetToDelete.ParentSubnetId,
                Name = subnetToDelete.Name,
                NetworkAddress = subnetToDelete.NetworkAddress,
                Cidr = subnetToDelete.Cidr,
                Description = subnetToDelete.Description,
                Tags = subnetToDelete.Tags,
                CreatedAt = subnetToDelete.CreatedAt,
                LastModifiedAt = subnetToDelete.LastModifiedAt,
                CreatedBy = subnetToDelete.CreatedBy,
                ModifiedBy = subnetToDelete.ModifiedBy,
                DeletedAt = deletedAt,
                DeletedBy = deletedBy
            });

            context.Subnets.Remove(subnetToDelete);
        }

        return (toDelete.Count, allHostIps.Count);
    }

    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> DeletedSubnets()
    {

        List<DeletedSubnet> deletedSubnets = await context.DeletedSubnets
            .OrderByDescending(s => s.DeletedAt)
            .ToListAsync();

        List<DeletedSubnetsViewModel> viewModels = [.. deletedSubnets.Select(ds => new DeletedSubnetsViewModel
        {
            OriginalId = ds.OriginalId,
            Name = ds.Name,
            NetworkAddress = ds.NetworkAddress,
            Cidr = ds.Cidr,
            Description = ds.Description,
            OriginalParentId = ds.OriginalParentId,
            DeletedAt = ds.DeletedAt,
            DeletedBy = ds.DeletedBy,
            CreatedAt = ds.CreatedAt,
            LastModifiedAt = ds.LastModifiedAt,
            CreatedBy = ds.CreatedBy,
            ModifiedBy = ds.ModifiedBy
        })];

        DeletedSubnetListViewModel model = new()
        {
            DeletedSubnets = viewModels,
            TotalCount = viewModels.Count
        };

        return View(model);
    }

    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> PurgeAllDeletedSubnets()
    {

        int maxId = await context.DeletedSubnets.MaxAsync(d => (int?)d.Id) ?? 0;
        int count = await context.DeletedSubnets.CountAsync(d => d.Id <= maxId);
        if (count == 0)
        {
            TempData["ErrorMessage"] = "There are no deleted subnet records to purge.";
            return RedirectToAction(nameof(DeletedSubnets));
        }

        return View(new PurgeAllDeletedSubnetsViewModel { Count = count, MaxId = maxId });
    }

    [HttpPost, ActionName("PurgeAllDeletedSubnets")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> PurgeAllDeletedSubnetsConfirmed(string confirmation, int? confirmedMaxId)
    {
        if (confirmation != "approved")
        {
            TempData["ErrorMessage"] = "You must type 'approved' to confirm purge.";
            return RedirectToAction(nameof(PurgeAllDeletedSubnets));
        }

        if (confirmedMaxId is null or <= 0)
        {
            TempData["ErrorMessage"] =
                "The purge scope was missing from the form. Review the archive and confirm again.";
            return RedirectToAction(nameof(PurgeAllDeletedSubnets));
        }

        int removed = await context.DeletedSubnets
            .Where(d => d.Id <= confirmedMaxId)
            .ExecuteDeleteAsync();

        TempData["SuccessMessage"] = $"Permanently purged {removed} deleted subnet record(s).";
        return RedirectToAction(nameof(DeletedSubnets));
    }
}
