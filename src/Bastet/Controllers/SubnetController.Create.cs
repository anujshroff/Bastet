using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
    // GET: Subnet/Create
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Create(string? networkAddress = null, int? cidr = null, int? parentId = null)
    {
        // Load all potential parent subnets for dropdown
        List<SubnetViewModel> parentOptions = await context.Subnets
            .OrderBy(s => s.Name)
            .Select(s => new SubnetViewModel
            {
                Id = s.Id,
                Name = s.Name,
                NetworkAddress = s.NetworkAddress,
                Cidr = s.Cidr
            })
            .ToListAsync();

        CreateSubnetViewModel viewModel = new()
        {
            ParentSubnetOptions = parentOptions
        };

        // Pre-populate values if provided (for creating from unallocated range)
        if (!string.IsNullOrEmpty(networkAddress))
        {
            viewModel.NetworkAddress = networkAddress;
        }

        if (cidr.HasValue)
        {
            viewModel.Cidr = cidr.Value;
            // Calculate and set subnet mask
            viewModel.CalculatedSubnetMask = ipUtilityService.CalculateSubnetMask(cidr.Value);
        }

        if (parentId.HasValue)
        {
            viewModel.ParentSubnetId = parentId.Value;

            // Optionally generate a default name based on the parent subnet
            Subnet? parentSubnet = await context.Subnets.FindAsync(parentId.Value);
            if (parentSubnet != null && !string.IsNullOrEmpty(networkAddress) && cidr.HasValue)
            {
                viewModel.Name = $"{parentSubnet.Name}-{networkAddress}/{cidr}";
            }
        }

        return View(viewModel);
    }

    // POST: Subnet/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Create(CreateSubnetViewModel viewModel, [FromServices] IInputSanitizationService sanitizationService)
    {
        // Sanitize user inputs before validation
        viewModel.Name = sanitizationService.SanitizeName(viewModel.Name);
        viewModel.NetworkAddress = sanitizationService.SanitizeNetworkInput(viewModel.NetworkAddress);
        viewModel.Description = sanitizationService.SanitizeDescription(viewModel.Description);
        viewModel.Tags = sanitizationService.SanitizeTags(viewModel.Tags);

        if (!ModelState.IsValid)
        {
            await LoadParentSubnets(viewModel);
            return View(viewModel);
        }

        try
        {
            // Execute validation and creation within distributed lock to prevent race conditions
            Subnet? result = await subnetLockingService.ExecuteWithSubnetLockAsync(async () =>
            {
                // Validate the subnet using our helper method (now within the lock)
                if (await ValidateSubnetCreation(viewModel))
                {
                    // Create subnet directly in the database
                    Subnet subnet = new()
                    {
                        Name = viewModel.Name,
                        NetworkAddress = viewModel.NetworkAddress,
                        Cidr = viewModel.Cidr,
                        Description = viewModel.Description,
                        Tags = viewModel.Tags,
                        ParentSubnetId = viewModel.ParentSubnetId,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = userContextService.GetCurrentUsername()
                    };

                    context.Subnets.Add(subnet);
                    await context.SaveChangesAsync();

                    return subnet;
                }

                return null;
            });

            if (result != null)
            {
                TempData["SuccessMessage"] = $"Subnet '{result.Name}' was created successfully.";
                return RedirectToAction(nameof(Details), new { id = result.Id });
            }
        }
        catch (TimeoutException)
        {
            ModelState.AddModelError("", "The operation timed out due to high concurrency. Please try again.");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Error creating subnet: {ex.Message}");
        }

        // If we get here, something went wrong
        await LoadParentSubnets(viewModel);
        return View(viewModel);
    }

    private async Task LoadParentSubnets(CreateSubnetViewModel viewModel) =>
        // Load all parent options
        viewModel.ParentSubnetOptions = await context.Subnets
            .OrderBy(s => s.Name)
            .Select(s => new SubnetViewModel
            {
                Id = s.Id,
                Name = s.Name,
                NetworkAddress = s.NetworkAddress,
                Cidr = s.Cidr
            })
            .ToListAsync();
}
