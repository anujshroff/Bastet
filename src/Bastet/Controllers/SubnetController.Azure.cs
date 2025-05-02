using Bastet.Models;
using Bastet.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
    // POST: Subnet/BatchCreateChildSubnets
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> BatchCreateChildSubnets(int parentId, List<CreateSubnetViewModel> subnets, string? vnetName = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
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
            foreach (CreateSubnetViewModel subnet in subnets)
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
            if (!string.IsNullOrEmpty(vnetName) && Request.Headers.Referer.ToString().Contains("/Azure/Import/"))
            {
                // Update the name to match the Azure VNet name
                parentSubnet.Name = vnetName;

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
                foreach (CreateSubnetViewModel subnet in subnets)
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
                : !string.IsNullOrEmpty(vnetName) && Request.Headers.Referer.ToString().Contains("/Azure/Import/")
                    ? $"Successfully renamed parent subnet to '{vnetName}' and imported {createdSubnetIds.Count} child subnets."
                    : (object)$"Successfully imported {createdSubnetIds.Count} subnets.";

            // If this was called from the Azure import flow, redirect to details
            if (Request.Headers.Referer.ToString().Contains("/Azure/Import/"))
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
