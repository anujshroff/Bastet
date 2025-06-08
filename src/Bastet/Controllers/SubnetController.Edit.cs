using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
    // GET: Subnet/Edit/5
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Edit(int id)
    {
        Subnet? subnet = await context.Subnets
            .Include(s => s.ParentSubnet)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subnet == null)
        {
            return RedirectToAction("HttpStatusCodeHandler", "Error", new
            {
                statusCode = 404,
                errorMessage = $"The subnet with ID {id} could not be found or may have been deleted."
            });
        }

        EditSubnetViewModel viewModel = new()
        {
            Id = subnet.Id,
            Name = subnet.Name,
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            OriginalCidr = subnet.Cidr, // Store original CIDR for comparison
            Description = subnet.Description,
            Tags = subnet.Tags,
            SubnetMask = ipUtilityService.CalculateSubnetMask(subnet.Cidr),
            CreatedAt = subnet.CreatedAt,
            LastModifiedAt = subnet.LastModifiedAt,
            RowVersion = subnet.RowVersion
        };

        // Add parent subnet info if exists
        if (subnet.ParentSubnet != null)
        {
            viewModel.ParentSubnetInfo = $"{subnet.ParentSubnet.Name} ({subnet.ParentSubnet.NetworkAddress}/{subnet.ParentSubnet.Cidr})";
        }

        return View(viewModel);
    }

    // POST: Subnet/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Edit(int id, EditSubnetViewModel viewModel)
    {
        if (id != viewModel.Id)
        {
            return RedirectToAction("HttpStatusCodeHandler", "Error", new
            {
                statusCode = 404,
                errorMessage = "The ID in the URL doesn't match the ID in the form data."
            });
        }

        if (ModelState.IsValid)
        {
            try
            {
                // Begin a transaction to ensure data consistency for CIDR changes
                using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    // Retrieve existing subnet with relations for validation
                    Subnet? subnet = await context.Subnets
                        .Include(s => s.ParentSubnet)
                        .FirstOrDefaultAsync(s => s.Id == id);

                    if (subnet == null)
                    {
                        return RedirectToAction("HttpStatusCodeHandler", "Error", new
                        {
                            statusCode = 404,
                            errorMessage = $"The subnet with ID {id} could not be found or may have been deleted."
                        });
                    }

                    // Load child subnets directly to avoid navigation property issues
                    List<Subnet> childSubnets = await context.Subnets
                        .Where(s => s.ParentSubnetId == id)
                        .ToListAsync();

                    // Check if CIDR has changed
                    bool cidrChanged = viewModel.Cidr != viewModel.OriginalCidr;

                    // Always validate CIDR changes, regardless of whether this is a first or subsequent attempt
                    // This ensures validation is never bypassed, even on multiple form submissions
                    if (viewModel.Cidr != subnet.Cidr)
                    {
                        // Get siblings for validation if we have a parent
                        List<Subnet> siblings = [];
                        if (subnet.ParentSubnetId.HasValue)
                        {
                            siblings = await context.Subnets
                                .Where(s => s.ParentSubnetId == subnet.ParentSubnetId && s.Id != subnet.Id)
                                .ToListAsync();
                        }

                        // Get all other subnets for comprehensive overlap validation
                        List<Subnet> allOtherSubnets = await context.Subnets
                            .Where(s => s.Id != subnet.Id)
                            .ToListAsync();

                        // Always use the actual database value for original CIDR, not the viewModel value
                        // This prevents validation bypass on subsequent attempts
                        ValidationResult validationResult = subnetValidationService.ValidateSubnetCidrChange(
                            subnet.Id,
                            subnet.NetworkAddress,
                            subnet.Cidr, // Use actual DB value instead of viewModel.OriginalCidr
                            viewModel.Cidr,
                            subnet.ParentSubnet,
                            siblings,
                            childSubnets,
                            allOtherSubnets);

                        if (!validationResult.IsValid)
                        {
                            foreach (ValidationError error in validationResult.Errors)
                            {
                                ModelState.AddModelError("Cidr", error.Message);
                            }

                            // Early return with validation errors
                            // Populate info for the view
                            viewModel.SubnetMask = ipUtilityService.CalculateSubnetMask(viewModel.Cidr);
                            viewModel.CreatedAt = subnet.CreatedAt;
                            viewModel.LastModifiedAt = subnet.LastModifiedAt;
                            viewModel.RowVersion = subnet.RowVersion;

                            if (subnet.ParentSubnet != null)
                            {
                                viewModel.ParentSubnetInfo = $"{subnet.ParentSubnet.Name} ({subnet.ParentSubnet.NetworkAddress}/{subnet.ParentSubnet.Cidr})";
                            }

                            return View(viewModel);
                        }

                        // Update subnet mask for display after validation
                        viewModel.SubnetMask = ipUtilityService.CalculateSubnetMask(viewModel.Cidr);

                        // Only validate host IPs when CIDR is increasing (making subnet smaller)
                        if (viewModel.Cidr > subnet.Cidr)
                        {
                            // Validate that all host IPs are still within the subnet range after CIDR change
                            ValidationResult hostIpValidationResult = hostIpValidationService.ValidateSubnetCidrChangeWithHostIps(
                                subnet.Id,
                                subnet.NetworkAddress,
                                subnet.Cidr,
                                viewModel.Cidr);

                            if (!hostIpValidationResult.IsValid)
                            {
                                foreach (ValidationError error in hostIpValidationResult.Errors)
                                {
                                    ModelState.AddModelError("Cidr", error.Message);
                                }

                                // Early return with validation errors
                                viewModel.CreatedAt = subnet.CreatedAt;
                                viewModel.LastModifiedAt = subnet.LastModifiedAt;
                                viewModel.RowVersion = subnet.RowVersion;

                                if (subnet.ParentSubnet != null)
                                {
                                    viewModel.ParentSubnetInfo = $"{subnet.ParentSubnet.Name} ({subnet.ParentSubnet.NetworkAddress}/{subnet.ParentSubnet.Cidr})";
                                }

                                return View(viewModel);
                            }
                        }
                        // For CIDR decreases (subnet expansion), no host IP validation is needed
                        // since making a subnet larger cannot cause host IPs to fall outside its range
                    }

                    // Update all editable properties including CIDR now
                    subnet.Name = viewModel.Name;
                    subnet.Description = viewModel.Description;
                    subnet.Tags = viewModel.Tags;
                    subnet.LastModifiedAt = DateTime.UtcNow;
                    subnet.ModifiedBy = userContextService.GetCurrentUsername();

                    if (cidrChanged)
                    {
                        subnet.Cidr = viewModel.Cidr;
                    }

                    // Set the original RowVersion for concurrency control
                    // This tells EF what the RowVersion was when the user started editing
                    context.Entry(subnet).OriginalValues["RowVersion"] = viewModel.RowVersion;

                    context.Subnets.Update(subnet);
                    await context.SaveChangesAsync();

                    // Commit the transaction
                    await transaction.CommitAsync();

                    TempData["SuccessMessage"] = $"Subnet '{subnet.Name}' was updated successfully.";
                    return RedirectToAction(nameof(Details), new { id = subnet.Id });
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Rollback the transaction on concurrency error
                    await transaction.RollbackAsync();

                    if (!SubnetExists(id))
                    {
                        return RedirectToAction("HttpStatusCodeHandler", "Error", new
                        {
                            statusCode = 404,
                            errorMessage = "The subnet no longer exists. It may have been deleted by another user."
                        });
                    }

                    // Handle concurrency conflict - reload current data and show user-friendly message
                    Subnet? currentSubnet = await context.Subnets
                        .Include(s => s.ParentSubnet)
                        .FirstOrDefaultAsync(s => s.Id == id);

                    if (currentSubnet != null)
                    {
                        // Update the view model with current database values for concurrency control
                        viewModel.RowVersion = currentSubnet.RowVersion;
                        viewModel.NetworkAddress = currentSubnet.NetworkAddress;
                        viewModel.OriginalCidr = currentSubnet.Cidr;
                        viewModel.CreatedAt = currentSubnet.CreatedAt;
                        viewModel.LastModifiedAt = currentSubnet.LastModifiedAt;

                        if (currentSubnet.ParentSubnet != null)
                        {
                            viewModel.ParentSubnetInfo = $"{currentSubnet.ParentSubnet.Name} ({currentSubnet.ParentSubnet.NetworkAddress}/{currentSubnet.ParentSubnet.Cidr})";
                        }

                        // Clear the RowVersion from ModelState so the form field uses the updated model value
                        ModelState.Remove(nameof(viewModel.RowVersion));
                    }

                    ModelState.AddModelError("",
                        "This subnet was modified by another user while you were editing it. " +
                        "Your changes have been preserved below, but you should review the current values before saving. " +
                        "Click 'Save Changes' again to apply your updates.");
                }
                catch (Exception ex)
                {
                    // Rollback the transaction on error
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", $"Error updating subnet: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                // Handle any other exceptions outside the transaction
                ModelState.AddModelError("", $"Error updating subnet: {ex.Message}");
            }
        }

        // If we got this far, something failed - repopulate the view model and return to the form
        Subnet? origSubnet = await context.Subnets
            .Include(s => s.ParentSubnet)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (origSubnet == null)
        {
            return RedirectToAction("HttpStatusCodeHandler", "Error", new
            {
                statusCode = 404,
                errorMessage = $"The subnet with ID {id} could not be found or may have been deleted."
            });
        }

        // Repopulate the display-only properties
        viewModel.NetworkAddress = origSubnet.NetworkAddress;

        // Always set original CIDR to the actual DB value to prevent validation bypass
        viewModel.OriginalCidr = origSubnet.Cidr;

        // Update the subnet mask based on user's input CIDR value
        if (!ModelState.IsValid || viewModel.Cidr != origSubnet.Cidr)
        {
            viewModel.SubnetMask = ipUtilityService.CalculateSubnetMask(viewModel.Cidr);
        }
        else
        {
            viewModel.Cidr = origSubnet.Cidr;
            viewModel.OriginalCidr = origSubnet.Cidr;
            viewModel.SubnetMask = ipUtilityService.CalculateSubnetMask(origSubnet.Cidr);
        }

        viewModel.CreatedAt = origSubnet.CreatedAt;
        viewModel.LastModifiedAt = origSubnet.LastModifiedAt;
        // Ensure RowVersion is updated for concurrency control
        viewModel.RowVersion = origSubnet.RowVersion;

        if (origSubnet.ParentSubnet != null)
        {
            viewModel.ParentSubnetInfo = $"{origSubnet.ParentSubnet.Name} ({origSubnet.ParentSubnet.NetworkAddress}/{origSubnet.ParentSubnet.Cidr})";
        }

        return View(viewModel);
    }
}
