using Bastet.Models;
using Bastet.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
    // POST: Subnet/BatchCreate
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> BatchCreate(int parentId, List<CreateSubnetViewModel> subnets)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Begin transaction
        using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        try
        {
            List<int> createdSubnetIds = [];

            // Validate all subnets first before creating any
            foreach (CreateSubnetViewModel subnet in subnets)
            {
                // Ensure parent ID is set correctly
                subnet.ParentSubnetId = parentId;

                // Use the extracted validation method
                if (!await ValidateSubnetCreation(subnet))
                {
                    // Validation failed, rollback and return errors
                    await transaction.RollbackAsync();
                    return BadRequest(ModelState);
                }
            }

            // All subnets are valid, create them
            foreach (CreateSubnetViewModel subnet in subnets)
            {
                // Create the subnet entity (same as in Create action)
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

            await transaction.CommitAsync();

            // Add success message and redirect
            TempData["SuccessMessage"] = $"Successfully imported {subnets.Count} subnets.";

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
