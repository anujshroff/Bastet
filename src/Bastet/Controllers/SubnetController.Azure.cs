using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Security;
using Bastet.Services.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
    /// <summary>
    /// Maximum length for <see cref="Models.Subnet.Name"/>; matches the [MaxLength(100)] attribute on
    /// the entity, and the same limit the bulk import planner applies to Azure-derived names.
    /// </summary>
    private const int MaxSubnetNameLength = 100;

    /// <summary>
    /// Maximum length for <see cref="Models.Subnet.Description"/>; matches the [MaxLength(1000)]
    /// attribute on the entity.
    /// </summary>
    private const int MaxSubnetDescriptionLength = 1000;

    /// <summary>
    /// Builds the description for a subnet an Azure import has just marked fully allocated. The note
    /// is only appended when it fits: descriptions are capped, the note repeats what the
    /// IsFullyAllocated flag already records, and overflowing the column fails the insert and rolls
    /// back the entire import behind a generic error. Existing text is never sacrificed for the note.
    /// </summary>
    private static string AppendFullyAllocatedNote(string? existingDescription, string? azureSubnetName)
    {
        string note = $"Fully allocated by Azure subnet '{azureSubnetName}' which encompasses the entire address space.";

        if (string.IsNullOrEmpty(existingDescription))
        {
            return Truncate(note);
        }

        string combined = $"{existingDescription}\n{note}";
        return combined.Length <= MaxSubnetDescriptionLength
            ? combined
            : Truncate(existingDescription);

        static string Truncate(string value) =>
            value.Length > MaxSubnetDescriptionLength ? value[..MaxSubnetDescriptionLength] : value;
    }

    // POST: Subnet/BatchCreateChildSubnets
    /// <param name="isAzureImport">
    /// True when called from the Azure import wizard, which additionally renames the parent to the
    /// VNet name, stamps its resource ID, and redirects to Details instead of returning JSON.
    /// This used to be inferred from the Referer header, which is client-supplied: it could be
    /// forged, and a browser that strips it silently disabled the rename. Defaults to false, so
    /// callers using this as a plain batch-create API keep their existing JSON behaviour.
    /// </param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> BatchCreateChildSubnets(int parentId, List<AzureImportSubnetViewModel> subnets, string? vnetName = null, string? vnetResourceId = null, bool isAzureImport = false, [FromServices] IInputSanitizationService? sanitizationService = null)
    {
        // Note on name length: the import wizard trims Azure names to Subnet.Name's limit before
        // posting them, so the length rule inherited from CreateSubnetViewModel is never the thing
        // that fails a real import - it is the guard for a caller posting directly. Trimming here
        // instead would mean clearing the binder's errors for these fields, which would also drop the
        // HTML and safe-text errors on the same field and make those rules apply only to short names.
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Nothing bound means nothing was selected, or the post was malformed. Without this the
        // import would fall through to the parent rename below and report "imported 0 child
        // subnets" as a success, which reads as though the selection was honoured.
        if (subnets is null or { Count: 0 })
        {
            ModelState.AddModelError("subnets", "No subnets were submitted for import.");
            return BadRequest(ModelState);
        }

        // Sanitize user inputs before processing
        if (sanitizationService != null)
        {
            foreach (AzureImportSubnetViewModel subnet in subnets)
            {
                subnet.Name = sanitizationService.SanitizeName(subnet.Name);
                subnet.NetworkAddress = sanitizationService.SanitizeNetworkInput(subnet.NetworkAddress);
                subnet.Description = sanitizationService.SanitizeDescription(subnet.Description);
                subnet.Tags = sanitizationService.SanitizeTags(subnet.Tags);
                if (!string.IsNullOrEmpty(subnet.AzureResourceId))
                {
                    subnet.AzureResourceId = sanitizationService.SanitizeDescription(subnet.AzureResourceId);
                }
            }

            // Also sanitize vnetName if provided
            if (!string.IsNullOrEmpty(vnetName))
            {
                vnetName = sanitizationService.SanitizeName(vnetName);
            }

            if (!string.IsNullOrEmpty(vnetResourceId))
            {
                vnetResourceId = sanitizationService.SanitizeDescription(vnetResourceId);
            }
        }

        try
        {
            // Validation reads and writes must happen under the global lock, or a concurrent
            // create/import could pass overlap validation against a tree this batch is changing.
            return await subnetLockingService.ExecuteWithSubnetLockAsync(() =>
                BatchCreateChildSubnetsCore(parentId, subnets, vnetName, vnetResourceId, isAzureImport));
        }
        catch (TimeoutException)
        {
            return StatusCode(503, "The operation timed out because another subnet operation is in progress. Please try again.");
        }
    }

    private async Task<IActionResult> BatchCreateChildSubnetsCore(int parentId, List<AzureImportSubnetViewModel> subnets, string? vnetName, string? vnetResourceId, bool isAzureImport)
    {
        // Begin transaction
        using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        try
        {
            // Get the parent subnet first - validate early
            Subnet? parentSubnet = await context.Subnets.FindAsync(parentId);
            if (parentSubnet == null)
            {
                await transaction.RollbackAsync();
                return NotFound($"Parent subnet with ID {parentId} not found");
            }

            List<int> createdSubnetIds = [];
            AzureImportSubnetViewModel? fullyEncompassingSubnet = null;

            // Initial validation to ensure all subnets are individually valid
            foreach (AzureImportSubnetViewModel subnet in subnets)
            {
                // Ensure parent ID is set correctly
                subnet.ParentSubnetId = parentId;

                // Check if this subnet fully encompasses a VNet address prefix
                if (subnet.FullyEncompassesVNetPrefix)
                {
                    fullyEncompassingSubnet = subnet;
                    continue; // Not created as a child; it marks the parent fully allocated instead
                }

                // Use the extracted validation method
                if (!await ValidateSubnetCreation(subnet))
                {
                    // Validation failed, rollback and return errors
                    await transaction.RollbackAsync();
                    return BadRequest(ModelState);
                }
            }

            bool hasFullyEncompassingSubnet = fullyEncompassingSubnet != null;
            string? fullyEncompassingSubnetName = fullyEncompassingSubnet?.Name;

            // An encompassing entry is never created, so it skips the creation checks above - but it
            // still drives a write to the parent, so the parent has to be validated for it. Without
            // this, a caller could mark a parent that has children or host IPs as fully allocated (a
            // state SetAllocationStatus and the bulk planner both forbid), or claim any unrelated
            // prefix encompasses it.
            if (fullyEncompassingSubnet != null)
            {
                if (!string.Equals(fullyEncompassingSubnet.NetworkAddress, parentSubnet.NetworkAddress, StringComparison.Ordinal)
                    || fullyEncompassingSubnet.Cidr != parentSubnet.Cidr)
                {
                    ModelState.AddModelError("subnets",
                        $"Subnet '{fullyEncompassingSubnet.Name}' ({fullyEncompassingSubnet.NetworkAddress}/{fullyEncompassingSubnet.Cidr}) " +
                        $"does not cover the whole of {parentSubnet.NetworkAddress}/{parentSubnet.Cidr} and cannot mark it fully allocated.");
                    await transaction.RollbackAsync();
                    return BadRequest(ModelState);
                }

                ValidationResult allocationValidation = hostIpValidationService.ValidateSubnetCanBeFullyAllocated(parentId);
                if (!allocationValidation.IsValid)
                {
                    foreach (ValidationError error in allocationValidation.Errors)
                    {
                        ModelState.AddModelError("subnets", error.Message);
                    }

                    await transaction.RollbackAsync();
                    return BadRequest(ModelState);
                }
            }

            // Update parent subnet if this is an Azure import
            if (!string.IsNullOrEmpty(vnetName) && isAzureImport)
            {
                // Update the name to match the Azure VNet name. Azure VNet names reach 64 characters
                // and the column holds 100, so this never truncates a real Azure name - it is a guard
                // against a hand-crafted post, since SanitizeName trims at the same 100.
                parentSubnet.Name = vnetName.Length > MaxSubnetNameLength
                    ? vnetName[..MaxSubnetNameLength]
                    : vnetName;

                // Stamp the VNet resource ID onto the parent so the Details page can link to Azure.
                if (!string.IsNullOrEmpty(vnetResourceId))
                {
                    parentSubnet.AzureResourceId = vnetResourceId;
                }

                // If a subnet fully encompasses the VNet address prefix, mark parent as fully allocated
                if (hasFullyEncompassingSubnet)
                {
                    parentSubnet.IsFullyAllocated = true;

                    // Update description, preserving existing description if present
                    parentSubnet.Description = AppendFullyAllocatedNote(parentSubnet.Description, fullyEncompassingSubnetName);
                }

                parentSubnet.LastModifiedAt = DateTime.UtcNow;
                parentSubnet.ModifiedBy = userContextService.GetCurrentUsername();
                await context.SaveChangesAsync();
            }

            // If we have a subnet that fully encompasses the VNet address prefix,
            // we don't create any child subnets
            if (!hasFullyEncompassingSubnet)
            {
                // Create each subnet - with validation right before adding to catch overlaps
                foreach (AzureImportSubnetViewModel subnet in subnets)
                {
                    // Skip subnets that fully encompass the VNet address prefix
                    if (subnet.FullyEncompassesVNetPrefix)
                    {
                        continue;
                    }

                    // Validate again before adding to catch conflicts with previously added subnets in this batch
                    if (!await ValidateSubnetCreation(subnet))
                    {
                        // Validation failed, rollback and return errors
                        await transaction.RollbackAsync();
                        return BadRequest(ModelState);
                    }

                    // Create the subnet entity
                    Subnet newSubnet = new()
                    {
                        Name = subnet.Name,
                        NetworkAddress = subnet.NetworkAddress,
                        Cidr = subnet.Cidr,
                        Description = subnet.Description,
                        Tags = subnet.Tags,
                        AzureResourceId = subnet.AzureResourceId,
                        ParentSubnetId = parentId,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = userContextService.GetCurrentUsername()
                    };

                    context.Subnets.Add(newSubnet);
                    await context.SaveChangesAsync();

                    createdSubnetIds.Add(newSubnet.Id);
                }
            }

            await transaction.CommitAsync();

            // Add appropriate success message
            TempData["SuccessMessage"] = hasFullyEncompassingSubnet
                ? $"Successfully renamed parent subnet to '{vnetName}' and marked it as fully allocated by Azure subnet '{fullyEncompassingSubnetName}'."
                : !string.IsNullOrEmpty(vnetName) && isAzureImport
                    ? $"Successfully renamed parent subnet to '{vnetName}' and imported {createdSubnetIds.Count} child subnets."
                    : (object)$"Successfully imported {createdSubnetIds.Count} subnets.";

            // If this was called from the Azure import flow, redirect to details
            if (isAzureImport)
            {
                return RedirectToAction("Details", new { id = parentId });
            }

            // Otherwise return JSON (for API usage)
            return Ok(new { success = true, subnetIds = createdSubnetIds });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Batch create of child subnets under parent {ParentId} failed", parentId);
            return StatusCode(500, "An unexpected error occurred while creating subnets. Details have been logged.");
        }
    }
}
