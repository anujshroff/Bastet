using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
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
        if (!ModelState.IsValid)
        {
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
            bool hasFullyEncompassingSubnet = false;
            string? fullyEncompassingSubnetName = null;

            // Initial validation to ensure all subnets are individually valid
            foreach (AzureImportSubnetViewModel subnet in subnets)
            {
                // Ensure parent ID is set correctly
                subnet.ParentSubnetId = parentId;

                // Check if this subnet fully encompasses a VNet address prefix
                if (subnet.FullyEncompassesVNetPrefix)
                {
                    hasFullyEncompassingSubnet = true;
                    fullyEncompassingSubnetName = subnet.Name;
                    continue; // Skip validation for this subnet since we won't create it
                }

                // Use the extracted validation method
                if (!await ValidateSubnetCreation(subnet))
                {
                    // Validation failed, rollback and return errors
                    await transaction.RollbackAsync();
                    return BadRequest(ModelState);
                }
            }

            // Update parent subnet if this is an Azure import
            if (!string.IsNullOrEmpty(vnetName) && isAzureImport)
            {
                // Update the name to match the Azure VNet name
                parentSubnet.Name = vnetName;

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
                    string azureImportInfo = $"Fully allocated by Azure subnet '{fullyEncompassingSubnetName}' which encompasses the entire address space.";
                    parentSubnet.Description = string.IsNullOrEmpty(parentSubnet.Description)
                        ? azureImportInfo
                        : $"{parentSubnet.Description}\n{azureImportInfo}";
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
            return StatusCode(500, ex.Message);
        }
    }
}
