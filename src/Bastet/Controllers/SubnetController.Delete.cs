using Bastet.Models;
using Bastet.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
    // GET: Subnet/Delete/5
    [Authorize(Policy = "RequireDeleteRole")]
    public async Task<IActionResult> Delete(int id)
    {
        Subnet? subnet = await context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subnet == null)
        {
            return RedirectToAction("HttpStatusCodeHandler", "Error", new
            {
                statusCode = 404,
                errorMessage = $"The subnet with ID {id} could not be found or may have been deleted."
            });
        }

        // Count all descendants (not just direct children)
        int descendantCount = await CountAllDescendants(id);

        // Count all host IPs in this subnet
        int hostIpCount = subnet.HostIpAssignments.Count;

        // Count host IPs in all descendant subnets
        hostIpCount += await CountAllDescendantHostIps(id);

        DeleteSubnetViewModel viewModel = new()
        {
            Id = subnet.Id,
            Name = subnet.Name,
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            Description = subnet.Description,
            ChildSubnetCount = descendantCount,
            HostIpCount = hostIpCount,
            IsFullyAllocated = subnet.IsFullyAllocated
        };

        return View(viewModel);
    }

    // Helper method to count all host IPs in descendant subnets
    private async Task<int> CountAllDescendantHostIps(int subnetId)
    {
        // Get all subnets with their host IP assignments
        List<Subnet> allSubnets = await context.Subnets
            .Include(s => s.HostIpAssignments)
            .ToListAsync();

        int hostIpCount = 0;

        // Set to keep track of processed IDs to avoid circular references
        HashSet<int> processedIds = [];

        // Queue for breadth-first traversal
        Queue<int> queue = new();
        queue.Enqueue(subnetId);
        processedIds.Add(subnetId);

        while (queue.Count > 0)
        {
            int currentId = queue.Dequeue();

            // Find all direct children of the current subnet
            List<Subnet> childSubnets = [.. allSubnets.Where(s => s.ParentSubnetId == currentId)];

            foreach (Subnet? child in childSubnets)
            {
                if (!processedIds.Contains(child.Id))
                {
                    // Count host IPs in this child subnet
                    hostIpCount += child.HostIpAssignments.Count;

                    queue.Enqueue(child.Id);
                    processedIds.Add(child.Id);
                }
            }
        }

        return hostIpCount;
    }

    // POST: Subnet/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireDeleteRole")]
    public async Task<IActionResult> DeleteConfirmed(int id, string confirmation)
    {
        // Verify the confirmation text
        if (confirmation != "approved")
        {
            TempData["ErrorMessage"] = "You must type 'approved' to confirm deletion.";
            return RedirectToAction(nameof(Delete), new { id });
        }

        // Load the main subnet with its child relationships and host IPs
        Subnet? subnet = await context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subnet == null)
        {
            return RedirectToAction("HttpStatusCodeHandler", "Error", new
            {
                statusCode = 404,
                errorMessage = $"The subnet with ID {id} could not be found or may have been deleted."
            });
        }

        // Begin a transaction to ensure data consistency
        using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        try
        {
            // Get all descendants to delete (ordered for proper deletion - deepest first)
            List<Subnet> allDescendants = await GetAllDescendantsOrdered(id);

            // Add the subnet itself to be deleted (will be processed last)
            allDescendants.Add(subnet);

            // Get all host IPs for all subnets to be deleted
            List<HostIpAssignment> allHostIps = [];

            // First, load all host IPs for each subnet to be deleted
            foreach (Subnet subnetToProcess in allDescendants)
            {
                // We need to load host IPs for each subnet since they're not included in GetAllDescendantsOrdered
                Subnet? subnetWithHostIps = await context.Subnets
                    .Include(s => s.HostIpAssignments)
                    .FirstOrDefaultAsync(s => s.Id == subnetToProcess.Id);

                if (subnetWithHostIps != null && subnetWithHostIps.HostIpAssignments.Count > 0)
                {
                    allHostIps.AddRange(subnetWithHostIps.HostIpAssignments);
                }
            }

            // First handle all host IPs (archive them in DeletedHostIpAssignments)
            foreach (HostIpAssignment hostIp in allHostIps)
            {
                // Create a deletion record
                DeletedHostIpAssignment deletedHostIp = new()
                {
                    OriginalIP = hostIp.IP,
                    Name = hostIp.Name,
                    OriginalSubnetId = hostIp.SubnetId,
                    CreatedAt = hostIp.CreatedAt,
                    LastModifiedAt = hostIp.LastModifiedAt,
                    CreatedBy = hostIp.CreatedBy,
                    ModifiedBy = hostIp.ModifiedBy,
                    DeletedAt = DateTime.UtcNow,
                    DeletedBy = userContextService.GetCurrentUsername()
                };

                // Add to DeletedHostIpAssignments table
                context.DeletedHostIpAssignments.Add(deletedHostIp);

                // Remove from HostIpAssignments table
                context.HostIpAssignments.Remove(hostIp);
            }

            // Now process each subnet
            foreach (Subnet subnetToDelete in allDescendants)
            {
                // Add to DeletedSubnets table
                DeletedSubnet deletedSubnet = new()
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
                    DeletedAt = DateTime.UtcNow,
                    DeletedBy = userContextService.GetCurrentUsername()
                };

                context.DeletedSubnets.Add(deletedSubnet);

                // Remove from Subnets table
                context.Subnets.Remove(subnetToDelete);
            }

            // Save all changes
            await context.SaveChangesAsync();

            // Commit the transaction
            await transaction.CommitAsync();

            int totalHostIpsDeleted = allHostIps.Count;
            TempData["SuccessMessage"] = $"Subnet '{subnet.Name}' and {allDescendants.Count - 1} child subnet(s) were deleted successfully. " +
                                       $"{totalHostIpsDeleted} host IP assignment(s) were archived.";

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            // Rollback the transaction on error
            await transaction.RollbackAsync();
            TempData["ErrorMessage"] = $"Error deleting subnet: {ex.Message}";
            return RedirectToAction(nameof(Delete), new { id });
        }
    }

    // GET: Subnet/DeletedSubnets
    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> DeletedSubnets()
    {
        // Get deleted subnets from the database
        List<DeletedSubnet> deletedSubnets = await context.DeletedSubnets
            .OrderByDescending(s => s.DeletedAt)
            .ToListAsync();

        // Map to view models
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

        // Create the list view model
        DeletedSubnetListViewModel model = new()
        {
            DeletedSubnets = viewModels,
            TotalCount = viewModels.Count
        };

        return View(model);
    }
}
