using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Azure;
using Bastet.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
    /// <summary>
    /// POST: Subnet/BulkCreateFromAzurePlan — commits a previously-built Bulk Azure Import plan.
    ///
    /// All work happens inside a single transaction. We re-run the planner (using fresh data) before
    /// applying anything, so even if the database changed between preview and commit we won't import
    /// a stale plan. Every Bastet subnet creation is funnelled through the same
    /// <see cref="ValidateSubnetCreation"/> helper used by <c>BatchCreateChildSubnets</c>, ensuring
    /// the same validation rules apply to bulk imports as to interactive creation.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> BulkCreateFromAzurePlan(
        [FromBody] BulkImportSelectionDto selection,
        [FromServices] IAzureBulkImportPlanner planner,
        [FromServices] IAzureSubnetSnapshotService snapshotService,
        [FromServices] IInputSanitizationService? sanitizationService = null)
    {
        // Feature flag guard — same as the AzureController endpoints
        if (!AzureController.IsAzureImportEnabled())
        {
            return StatusCode(403, new { success = false, error = "Azure Import feature is not enabled" });
        }

        if (selection is null)
        {
            return BadRequest(new { success = false, error = "No selection was provided." });
        }

        // Re-build the plan against the current Bastet tree right now
        IReadOnlyList<ExistingSubnetSnapshot> existing = await snapshotService.GetExistingSubnetsAsync();
        BulkImportPlanViewModel plan = planner.BuildPlan(selection, existing);

        if (!plan.CanCommit)
        {
            return BadRequest(new
            {
                success = false,
                globalErrors = plan.GlobalErrors,
                itemErrors = plan.Items
                    .Where(i => i.Errors.Count > 0)
                    .Select(i => new { i.VNetName, i.VNetPrefix, errors = i.Errors })
                    .ToList()
            });
        }

        // Begin transaction (mirror BatchCreateChildSubnets behaviour)
        using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync();

        try
        {
            // Track newly-created subnets so subsequent operations within this transaction can
            // resolve their parent-by-network/CIDR even before SaveChangesAsync has assigned IDs.
            int totalSubnetsCreated = 0;
            int totalTargetsRenamed = 0;
            int totalTargetsCreated = 0;
            int totalTargetsMarkedFullyAllocated = 0;

            // Order items so any AutoCreateChild/TopLevel that may itself contain another item runs first.
            // In practice items don't contain each other (overlap check guarantees that), so order is by CIDR ascending
            // is a safe, deterministic order regardless.
            List<BulkImportPlanItem> orderedItems = [.. plan.Items.OrderBy(i => i.PrefixCidr)];

            foreach (BulkImportPlanItem item in orderedItems)
            {
                Subnet targetSubnet;

                // Sanitize the VNet resource ID once; treat it as untrusted user input even
                // though it originates from the Azure SDK (it round-tripped through the browser).
                string? sanitizedVNetResourceId = string.IsNullOrEmpty(item.VNetResourceId)
                    ? null
                    : sanitizationService?.SanitizeDescription(item.VNetResourceId) ?? item.VNetResourceId;

                // 1) Resolve / create the target Bastet subnet for this VNet prefix
                if (item.TargetType == BulkImportTargetType.ExactMatch)
                {
                    Subnet? existingSubnet = await context.Subnets.FindAsync(item.ExistingTargetSubnetId);
                    if (existingSubnet is null)
                    {
                        await transaction.RollbackAsync();
                        return Conflict(new
                        {
                            success = false,
                            error = $"Matched Bastet subnet (id={item.ExistingTargetSubnetId}) for VNet '{item.VNetName}' was not found. Another user may have deleted it."
                        });
                    }
                    targetSubnet = existingSubnet;

                    bool targetModified = false;

                    // Apply rename if the plan calls for it
                    if (item.WillRename && !string.IsNullOrEmpty(item.NewName))
                    {
                        string newName = sanitizationService?.SanitizeName(item.NewName) ?? item.NewName;
                        if (!string.Equals(targetSubnet.Name, newName, StringComparison.Ordinal))
                        {
                            targetSubnet.Name = newName;
                            targetModified = true;
                            totalTargetsRenamed++;
                        }
                    }

                    // Stamp the VNet resource ID onto the matched target so the Details page can link to Azure.
                    if (!string.IsNullOrEmpty(sanitizedVNetResourceId)
                        && !string.Equals(targetSubnet.AzureResourceId, sanitizedVNetResourceId, StringComparison.Ordinal))
                    {
                        targetSubnet.AzureResourceId = sanitizedVNetResourceId;
                        targetModified = true;
                    }

                    if (targetModified)
                    {
                        targetSubnet.LastModifiedAt = DateTime.UtcNow;
                        targetSubnet.ModifiedBy = userContextService.GetCurrentUsername();
                        await context.SaveChangesAsync();
                    }
                }
                else
                {
                    // AutoCreateChild or AutoCreateTopLevel — create a fresh Bastet subnet for the VNet prefix
                    string targetName = sanitizationService?.SanitizeName(item.AutoCreateTargetName) ?? item.AutoCreateTargetName ?? string.Empty;

                    CreateSubnetViewModel targetVm = new()
                    {
                        Name = targetName,
                        NetworkAddress = item.PrefixNetworkAddress,
                        Cidr = item.PrefixCidr,
                        Description = null,
                        Tags = null,
                        ParentSubnetId = item.AutoCreateParentSubnetId,
                        FullyEncompassesVNetPrefix = false,
                        AzureResourceId = sanitizedVNetResourceId
                    };

                    if (!await ValidateSubnetCreation(targetVm))
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(ModelState);
                    }

                    targetSubnet = new Subnet
                    {
                        Name = targetVm.Name,
                        NetworkAddress = targetVm.NetworkAddress,
                        Cidr = targetVm.Cidr,
                        Description = targetVm.Description,
                        Tags = targetVm.Tags,
                        AzureResourceId = targetVm.AzureResourceId,
                        ParentSubnetId = targetVm.ParentSubnetId,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = userContextService.GetCurrentUsername()
                    };
                    context.Subnets.Add(targetSubnet);
                    await context.SaveChangesAsync();
                    totalTargetsCreated++;
                }

                // 2) If a fully-encompassing Azure subnet was selected, mark target as fully allocated and skip child creation
                if (item.WillMarkFullyAllocated)
                {
                    targetSubnet.IsFullyAllocated = true;

                    string azureImportInfo = $"Fully allocated by Azure subnet '{item.FullyAllocatingAzureSubnetName}' which encompasses the entire address space.";
                    targetSubnet.Description = string.IsNullOrEmpty(targetSubnet.Description)
                        ? azureImportInfo
                        : $"{targetSubnet.Description}\n{azureImportInfo}";

                    targetSubnet.LastModifiedAt = DateTime.UtcNow;
                    targetSubnet.ModifiedBy = userContextService.GetCurrentUsername();
                    await context.SaveChangesAsync();
                    totalTargetsMarkedFullyAllocated++;
                    continue; // do not create children
                }

                // 3) Create each planned child subnet, validating each one through the standard creation pipeline
                foreach (BulkImportPlannedChildSubnet child in item.ChildSubnets)
                {
                    string childName = sanitizationService?.SanitizeName(child.Name) ?? child.Name;
                    string childNetwork = sanitizationService?.SanitizeNetworkInput(child.NetworkAddress) ?? child.NetworkAddress;
                    string? sanitizedChildResourceId = string.IsNullOrEmpty(child.AzureResourceId)
                        ? null
                        : sanitizationService?.SanitizeDescription(child.AzureResourceId) ?? child.AzureResourceId;

                    CreateSubnetViewModel childVm = new()
                    {
                        Name = childName,
                        NetworkAddress = childNetwork,
                        Cidr = child.Cidr,
                        Description = null,
                        Tags = null,
                        ParentSubnetId = targetSubnet.Id,
                        FullyEncompassesVNetPrefix = false,
                        AzureResourceId = sanitizedChildResourceId
                    };

                    if (!await ValidateSubnetCreation(childVm))
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(ModelState);
                    }

                    Subnet newChild = new()
                    {
                        Name = childVm.Name,
                        NetworkAddress = childVm.NetworkAddress,
                        Cidr = childVm.Cidr,
                        Description = childVm.Description,
                        Tags = childVm.Tags,
                        AzureResourceId = childVm.AzureResourceId,
                        ParentSubnetId = targetSubnet.Id,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = userContextService.GetCurrentUsername()
                    };
                    context.Subnets.Add(newChild);
                    await context.SaveChangesAsync();
                    totalSubnetsCreated++;
                }
            }

            await transaction.CommitAsync();

            TempData["SuccessMessage"] =
                $"Bulk import succeeded: created {totalTargetsCreated} VNet target subnet(s), " +
                $"created {totalSubnetsCreated} Azure child subnet(s), " +
                $"renamed {totalTargetsRenamed} target(s), " +
                $"and marked {totalTargetsMarkedFullyAllocated} target(s) as fully allocated.";

            return Ok(new
            {
                success = true,
                redirectUrl = Url.Action("Index", "Subnet"),
                createdTargets = totalTargetsCreated,
                createdChildSubnets = totalSubnetsCreated,
                renamedTargets = totalTargetsRenamed,
                fullyAllocatedTargets = totalTargetsMarkedFullyAllocated
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

}
