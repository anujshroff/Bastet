using Bastet.Models.ViewModels;
using Bastet.Models;
using Bastet.Services.Azure;
using Bastet.Services.Data;
using Bastet.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> BulkCreateFromAzurePlan(
        [FromBody] BulkImportSelectionDto selection,
        [FromServices] IAzureBulkImportPlanner planner,
        [FromServices] IAzureSubnetSnapshotService snapshotService,
        [FromServices] IInputSanitizationService? sanitizationService = null)
    {

        if (!AzureController.IsAzureImportEnabled())
        {
            return StatusCode(403, new { success = false, error = "Azure Import feature is not enabled" });
        }

        if (selection is null)
        {
            return BadRequest(new { success = false, error = "No selection was provided." });
        }

        try
        {

            return await subnetLockingService.ExecuteWithSubnetLockAsync(() =>
                BulkCreateFromAzurePlanCore(selection, planner, snapshotService, sanitizationService));
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new { success = false, error = "The operation timed out because another subnet operation is in progress. Please try again." });
        }
    }

    private List<string> DescribeApprovedPlanDivergences(BulkImportSelectionDto selection, BulkImportPlanViewModel plan)
    {
        List<string> differences = [];
        int unverified = 0;

        List<BulkImportSelectedVNetPrefixDto> selectedPrefixes = selection.VNetPrefixes ?? [];

        for (int index = 0; index < selectedPrefixes.Count; index++)
        {
            BulkImportSelectedVNetPrefixDto? selected = selectedPrefixes[index];

            if (selected is null)
            {

                continue;
            }

            BulkImportExpectedTargetDto? expected = selected.Expected;

            if (expected is null)
            {

                unverified++;
                continue;
            }

            BulkImportPlanItem? item = plan.Items.FirstOrDefault(i =>
                string.Equals(i.VNetResourceId, selected.VNetResourceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.VNetPrefix, selected.AddressPrefix, StringComparison.OrdinalIgnoreCase));

            if (item is null)
            {
                differences.Add($"Selected prefix #{index + 1} no longer produces a target to import.");
                continue;
            }

            string label = $"{item.PrefixNetworkAddress}/{item.PrefixCidr}";

            if (!Enum.TryParse(expected.TargetType, out BulkImportTargetType expectedTargetType)
                || expectedTargetType != item.TargetType)
            {
                differences.Add($"{label}: the preview showed a different action; it now resolves to {item.TargetTypeName}.");
            }

            if (expected.ExistingTargetSubnetId != item.ExistingTargetSubnetId)
            {
                differences.Add(item.ExistingTargetSubnetId is null
                    ? $"{label}: the preview matched an existing Bastet subnet; it no longer does."
                    : $"{label}: it now targets existing Bastet subnet {item.ExistingTargetSubnetId}.");
            }

            if (expected.AutoCreateParentSubnetId != item.AutoCreateParentSubnetId)
            {
                differences.Add(item.AutoCreateParentSubnetId is null
                    ? $"{label}: it would no longer be created under a parent subnet."
                    : $"{label}: it would now be created under subnet {item.AutoCreateParentSubnetId}.");
            }

            if (expected.WillRename != item.WillRename)
            {
                differences.Add($"{label}: renaming the target changed to {item.WillRename}.");
            }
            else if (item.WillRename && !string.Equals(expected.NewName, item.NewName, StringComparison.Ordinal))
            {

                differences.Add($"{label}: the name the target would be renamed to has changed.");
            }

            if (expected.WillMarkFullyAllocated != item.WillMarkFullyAllocated)
            {
                differences.Add($"{label}: marking the target fully allocated changed to {item.WillMarkFullyAllocated}.");
            }

            if (expected.ChildNames is not null
                && !expected.ChildNames.SequenceEqual(
                    item.ChildSubnets.Select(c => c.Name), StringComparer.Ordinal))
            {
                differences.Add($"{label}: the child subnet names have changed.");
            }
        }

        if (unverified > 0)
        {
            logger.LogWarning(
                "Bulk Azure import: {Unverified} of {Total} selected prefix(es) carried no previewed outcome, "
                + "so the re-derived plan for them was not compared against anything the operator approved.",
                unverified,
                selectedPrefixes.Count);
        }

        return differences;
    }

    private async Task<IActionResult> BulkCreateFromAzurePlanCore(
        BulkImportSelectionDto selection,
        IAzureBulkImportPlanner planner,
        IAzureSubnetSnapshotService snapshotService,
        IInputSanitizationService? sanitizationService)
    {

        IReadOnlyList<ExistingSubnetSnapshot> existing = await snapshotService.GetExistingSubnetsAsync();
        BulkImportPlanViewModel plan = planner.BuildPlan(selection, existing);

        List<string> divergences = plan.GlobalErrors.Count > 0
            ? []
            : DescribeApprovedPlanDivergences(selection, plan);

        if (divergences.Count > 0)
        {
            return Conflict(new
            {
                success = false,
                error = "The plan changed since it was previewed, so nothing was imported. "
                        + "Re-run the preview, review what it now says, and confirm again.",
                differences = divergences
            });
        }

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

        using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync();

        try
        {

            int totalSubnetsCreated = 0;
            int totalTargetsRenamed = 0;
            int totalTargetsCreated = 0;
            int totalTargetsMarkedFullyAllocated = 0;

            int totalTargetsLinked = 0;

            List<BulkImportPlanItem> orderedItems = [.. plan.Items.OrderBy(i => i.PrefixCidr)];

            List<Subnet> treeCache = await LoadSubnetTreeForBatchAsync();

            foreach (BulkImportPlanItem item in orderedItems)
            {
                Subnet targetSubnet;

                string? sanitizedVNetResourceId = string.IsNullOrEmpty(item.VNetResourceId)
                    ? null
                    : sanitizationService?.SanitizeDescription(item.VNetResourceId) ?? item.VNetResourceId;

                if (IsAzureResourceIdTooLong(sanitizedVNetResourceId))
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new
                    {
                        success = false,
                        error = $"The Azure resource ID for VNet '{item.VNetName}' is longer than {MaxAzureResourceIdLength} characters and cannot be stored."
                    });
                }

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

                    if (!string.IsNullOrEmpty(sanitizedVNetResourceId))
                    {
                        bool alreadyLinked = !string.IsNullOrEmpty(targetSubnet.AzureResourceId);

                        if (alreadyLinked
                            && !string.Equals(targetSubnet.AzureResourceId, sanitizedVNetResourceId, StringComparison.OrdinalIgnoreCase))
                        {
                            await transaction.RollbackAsync();
                            return Conflict(new
                            {
                                success = false,
                                error = $"Bastet subnet '{targetSubnet.Name}' ({targetSubnet.NetworkAddress}/{targetSubnet.Cidr}) "
                                    + $"is already linked to Azure VNet '{targetSubnet.AzureResourceId}' and cannot be re-linked to "
                                    + $"'{sanitizedVNetResourceId}' by an import. If the VNet was renamed or moved, delete the Bastet "
                                    + "subnet and import it again."
                            });
                        }

                        if (!string.Equals(targetSubnet.AzureResourceId, sanitizedVNetResourceId, StringComparison.Ordinal))
                        {
                            targetSubnet.AzureResourceId = sanitizedVNetResourceId;
                            targetModified = true;
                            if (!alreadyLinked)
                            {
                                totalTargetsLinked++;
                            }
                        }
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

                    string targetName = sanitizationService?.SanitizeName(item.AutoCreateTargetName) ?? item.AutoCreateTargetName ?? string.Empty;

                    AzureImportSubnetViewModel targetVm = new()
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

                    if (!await ValidateSubnetCreation(targetVm, treeCache))
                    {
                        await transaction.RollbackAsync();

                        return BadRequest(new
                        {
                            success = false,
                            error = ModelStateMessage("The import could not be applied."),
                            globalErrors = Array.Empty<string>(),
                            itemErrors = Array.Empty<object>()
                        });
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
                    treeCache.Add(targetSubnet);
                    totalTargetsCreated++;
                }

                if (item.WillMarkFullyAllocated)
                {
                    targetSubnet.IsFullyAllocated = true;

                    targetSubnet.Description = AppendFullyAllocatedNote(targetSubnet.Description, item.FullyAllocatingAzureSubnetName);

                    targetSubnet.LastModifiedAt = DateTime.UtcNow;
                    targetSubnet.ModifiedBy = userContextService.GetCurrentUsername();
                    await context.SaveChangesAsync();
                    totalTargetsMarkedFullyAllocated++;
                    continue;
                }

                foreach (BulkImportPlannedChildSubnet child in item.ChildSubnets)
                {
                    string childName = sanitizationService?.SanitizeName(child.Name) ?? child.Name;
                    string childNetwork = sanitizationService?.SanitizeNetworkInput(child.NetworkAddress) ?? child.NetworkAddress;
                    string? sanitizedChildResourceId = string.IsNullOrEmpty(child.AzureResourceId)
                        ? null
                        : sanitizationService?.SanitizeDescription(child.AzureResourceId) ?? child.AzureResourceId;

                    if (IsAzureResourceIdTooLong(sanitizedChildResourceId))
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new
                        {
                            success = false,
                            error = $"The Azure resource ID for subnet '{child.Name}' is longer than {MaxAzureResourceIdLength} characters and cannot be stored."
                        });
                    }

                    AzureImportSubnetViewModel childVm = new()
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

                    if (!await ValidateSubnetCreation(childVm, treeCache))
                    {
                        await transaction.RollbackAsync();

                        return BadRequest(new
                        {
                            success = false,
                            error = ModelStateMessage("The import could not be applied."),
                            globalErrors = Array.Empty<string>(),
                            itemErrors = Array.Empty<object>()
                        });
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
                    treeCache.Add(newChild);
                    totalSubnetsCreated++;
                }
            }

            await transaction.CommitAsync();

            TempData["SuccessMessage"] =
                $"Bulk import succeeded: created {totalTargetsCreated} VNet target subnet(s), " +
                $"created {totalSubnetsCreated} Azure child subnet(s), " +
                $"renamed {totalTargetsRenamed} target(s), " +
                $"linked {totalTargetsLinked} existing target(s) to Azure, " +
                $"and marked {totalTargetsMarkedFullyAllocated} target(s) as fully allocated.";

            return Ok(new
            {
                success = true,
                redirectUrl = Url.Action("Index", "Subnet"),
                createdTargets = totalTargetsCreated,
                createdChildSubnets = totalSubnetsCreated,
                renamedTargets = totalTargetsRenamed,
                linkedTargets = totalTargetsLinked,
                fullyAllocatedTargets = totalTargetsMarkedFullyAllocated
            });
        }
        catch (Exception ex) when (SqlSaveOutcome.IsIndeterminateTransaction(ex))
        {
            logger.LogError(ex, "Bulk Azure import outcome unknown");
            await TransactionCleanup.RollbackQuietlyAsync(transaction, logger);
            return StatusCode(500, new { success = false, error = "BASTET could not confirm whether this import was applied. Reload the subnet list to see its current state before retrying." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bulk Azure import commit failed");
            await TransactionCleanup.RollbackQuietlyAsync(transaction, logger);
            return StatusCode(500, new { success = false, error = "The bulk import failed and no changes were saved. Details have been logged." });
        }
    }

}
