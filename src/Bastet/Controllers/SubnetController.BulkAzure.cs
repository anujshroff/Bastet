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

        try
        {
            // Re-plan and commit under the same global lock, so the tree the plan was validated
            // against cannot change before the writes land.
            return await subnetLockingService.ExecuteWithSubnetLockAsync(() =>
                BulkCreateFromAzurePlanCore(selection, planner, snapshotService, sanitizationService));
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new { success = false, error = "The operation timed out because another subnet operation is in progress. Please try again." });
        }
    }

    /// <summary>
    /// Compares the plan just re-derived at commit time against the outcome the preview showed the
    /// operator, and describes every way they disagree.
    /// </summary>
    /// <remarks>
    /// Round-10 J2. The commit posts the *selection*, not the plan, so the preview and commit request
    /// bodies were byte-identical on the wire and nothing noticed when the re-derived plan differed.
    /// A subnet created by another admin while the preview was on screen flipped the target from
    /// AutoCreateTopLevel to ExactMatch, and the commit adopted it: stamped with the VNet's
    /// AzureResourceId (which no view can clear), renamed if the rename box was ticked, and thereby
    /// pulled into the reconcile wizard's deletion scope.
    /// <para>
    /// Nothing from the caller is echoed into the result. Divergences are described from the plan the
    /// server just built, because <c>GlobalSanitizationFilter</c> does not descend into the nested
    /// selection list and so nothing on the expectation has been sanitized. Where a selected prefix
    /// produces no plan item at all there is no server-derived text to name it by, so it is
    /// identified by its position in the submitted list.
    /// </para>
    /// </remarks>
    private List<string> DescribeApprovedPlanDivergences(BulkImportSelectionDto selection, BulkImportPlanViewModel plan)
    {
        List<string> differences = [];
        int unverified = 0;

        // System.Text.Json overwrites the DTO's collection initialiser with null when the body
        // carries an explicit null, and a list element can itself be null. AzureBulkImportPlanner
        // guards both shapes and records a global error - but this walk runs before the CanCommit
        // check that turns those into a modelled 400, so it has to survive them. Without this the
        // two shapes threw from outside the transaction's try, where the action catches only
        // TimeoutException: the documented JSON API stopped answering JSON and the wizard showed
        // "Server error: 500" in place of the planner's own message.
        List<BulkImportSelectedVNetPrefixDto> selectedPrefixes = selection.VNetPrefixes ?? [];

        for (int index = 0; index < selectedPrefixes.Count; index++)
        {
            BulkImportSelectedVNetPrefixDto? selected = selectedPrefixes[index];

            if (selected is null)
            {
                // Already reported by the planner as a global error. Deliberately not counted as an
                // unverified commit: it is a malformed request, not an unchecked one, and logging it
                // as unverified would describe a request that is about to be refused outright.
                continue;
            }

            BulkImportExpectedTargetDto? expected = selected.Expected;

            if (expected is null)
            {
                // A caller that did not preview has approved nothing, so there is nothing to compare
                // against. Deliberately not refused: the endpoint is documented as a direct JSON API
                // and refusing here would break it. Recorded instead, so an unverified commit is
                // visible in the log rather than indistinguishable from a checked one.
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

            // Server-derived and format-constrained, unlike the names round-tripped by the browser.
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
                // The name itself is caller-supplied, so say that it differs without repeating it.
                differences.Add($"{label}: the name the target would be renamed to has changed.");
            }

            if (expected.WillMarkFullyAllocated != item.WillMarkFullyAllocated)
            {
                differences.Add($"{label}: marking the target fully allocated changed to {item.WillMarkFullyAllocated}.");
            }

            // The children are what the commit actually writes, and their names are not
            // selection-only: BuildPlanItem seeds disambiguation from the existing tree, so a
            // concurrent rename of the matched subnet moves them while every field above stays
            // equal. Guarded on null so a caller that did not preview keeps the optional contract.
            // Neither list is repeated in the message - the same rule the NewName check follows,
            // because these names are caller-influenced and nothing here has been sanitized.
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
        // Re-build the plan against the current Bastet tree right now
        IReadOnlyList<ExistingSubnetSnapshot> existing = await snapshotService.GetExistingSubnetsAsync();
        BulkImportPlanViewModel plan = planner.BuildPlan(selection, existing);

        // Re-deriving the plan protects against committing a stale one, but on its own it also
        // silently commits a *different* one when the tree moved under the operator. Refuse that,
        // the way BulkDeleteStaleAzureSubnets already refuses a scan whose verdict has changed.
        //
        // Skipped when the planner recorded a global error, because then it produced no items and
        // every selected prefix would be described as "no longer produces a target to import" - a
        // divergence manufactured by the malformed body rather than by the tree moving. The
        // planner's own message is the useful answer there, and the BadRequest below carries it.
        // The 409-before-400 ordering still holds wherever both are meaningful: a plan that fails
        // only on per-item errors is still reported as diverged first.
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

            // Linking a previously unlinked subnet to Azure is a persisted change that no other
            // counter records; without it an import that only stamps resource IDs reports every
            // count as zero while the database did change.
            int totalTargetsLinked = 0;

            // Order items so any AutoCreateChild/TopLevel that may itself contain another item runs first.
            // In practice items don't contain each other (overlap check guarantees that), so order is by CIDR ascending
            // is a safe, deterministic order regardless.
            List<BulkImportPlanItem> orderedItems = [.. plan.Items.OrderBy(i => i.PrefixCidr)];

            // One read of the tree for the whole commit, appended to as rows are created - see
            // LoadSubnetTreeForBatchAsync. Both created targets and created children must be appended:
            // orderedItems is sorted by CIDR ascending precisely so a containing item runs first, so
            // appending only children would stop a later item seeing an earlier item's target. The
            // ExactMatch branch needs no append - it changes neither NetworkAddress nor Cidr - though
            // the cached row then keeps its pre-rename Name, which only affects the wording of an
            // error message that quotes it.
            List<Subnet> treeCache = await LoadSubnetTreeForBatchAsync();

            foreach (BulkImportPlanItem item in orderedItems)
            {
                Subnet targetSubnet;

                // Sanitize the VNet resource ID once; treat it as untrusted user input even
                // though it originates from the Azure SDK (it round-tripped through the browser).
                string? sanitizedVNetResourceId = string.IsNullOrEmpty(item.VNetResourceId)
                    ? null
                    : sanitizationService?.SanitizeDescription(item.VNetResourceId) ?? item.VNetResourceId;

                // Sanitization trims these at 1000, the column holds 500, and a real ARM ID is around
                // 330 - so an over-long value is crafted or broken input. Rejected rather than trimmed:
                // reconcile matches subnets to live Azure by this ID, and a truncated one matches
                // nothing, which reports the subnet as deleted in Azure for good.
                if (IsAzureResourceIdTooLong(sanitizedVNetResourceId))
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new
                    {
                        success = false,
                        error = $"The Azure resource ID for VNet '{item.VNetName}' is longer than {MaxAzureResourceIdLength} characters and cannot be stored."
                    });
                }

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
                    if (!string.IsNullOrEmpty(sanitizedVNetResourceId))
                    {
                        bool alreadyLinked = !string.IsNullOrEmpty(targetSubnet.AzureResourceId);

                        // Refuse to repoint an existing link at a different VNet. The planner
                        // already errors on this, so a commit only reaches here if the tree moved
                        // underneath the plan - but this is the write itself, and the consequence of
                        // letting it through is a row reconcile will later archive on the strength
                        // of a resource it was never imported from, with no in-app way back.
                        // ARM IDs are path-based, so a delete-and-recreate under the same name
                        // compares equal here and is unaffected.
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
                    // AutoCreateChild or AutoCreateTopLevel — create a fresh Bastet subnet for the VNet prefix
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
                        // The wizard reads error/globalErrors/itemErrors; a bare ModelState carries
                        // none of them, so it rendered a panel containing the words "Commit failed:"
                        // and nothing else. Echo the validator's own messages instead.
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

                // 2) If a fully-encompassing Azure subnet was selected, mark target as fully allocated and skip child creation
                if (item.WillMarkFullyAllocated)
                {
                    targetSubnet.IsFullyAllocated = true;

                    targetSubnet.Description = AppendFullyAllocatedNote(targetSubnet.Description, item.FullyAllocatingAzureSubnetName);

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
                        // See the target path above: same contract, same reason.
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
