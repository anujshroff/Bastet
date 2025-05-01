using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Validation;
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
    public async Task<IActionResult> Create(CreateSubnetViewModel viewModel)
    {
        if (ModelState.IsValid)
        {
            try
            {
                // Check if parent subnet exists if specified
                Subnet? parentSubnet = null;
                if (viewModel.ParentSubnetId.HasValue)
                {
                    parentSubnet = await context.Subnets
                        .FirstOrDefaultAsync(s => s.Id == viewModel.ParentSubnetId.Value);

                    if (parentSubnet == null)
                    {
                        ModelState.AddModelError("ParentSubnetId", "Selected parent subnet does not exist");
                        await LoadParentSubnets(viewModel);
                        return View(viewModel);
                    }

                    // Validate that parent doesn't have host IPs
                    ValidationResult hostIpValidation = subnetValidationService.ValidateParentCanHaveChildSubnets(
                        parentSubnet.Id,
                        parentSubnet.HostIpAssignments);

                    if (!hostIpValidation.IsValid)
                    {
                        foreach (ValidationError error in hostIpValidation.Errors)
                        {
                            ModelState.AddModelError("ParentSubnetId", error.Message);
                        }

                        await LoadParentSubnets(viewModel);
                        return View(viewModel);
                    }

                    // Validate that child subnet is within parent subnet range
                    if (!ipUtilityService.IsSubnetContainedInParent(
                        viewModel.NetworkAddress, viewModel.Cidr,
                        parentSubnet.NetworkAddress, parentSubnet.Cidr))
                    {
                        ModelState.AddModelError("NetworkAddress",
                            $"Child subnet must be contained within the parent subnet range. " +
                            $"Parent subnet is {parentSubnet.NetworkAddress}/{parentSubnet.Cidr}");
                        await LoadParentSubnets(viewModel);
                        return View(viewModel);
                    }

                    // Validate that child CIDR is larger than parent
                    if (viewModel.Cidr <= parentSubnet.Cidr)
                    {
                        ModelState.AddModelError("Cidr",
                            "Child subnet CIDR must be larger than parent subnet CIDR. " +
                            $"Parent subnet CIDR is {parentSubnet.Cidr}");
                        await LoadParentSubnets(viewModel);
                        return View(viewModel);
                    }
                }

                // Explicitly validate network address and CIDR alignment
                if (!ipUtilityService.IsValidSubnet(viewModel.NetworkAddress, viewModel.Cidr))
                {
                    ModelState.AddModelError("NetworkAddress",
                        $"Network address {viewModel.NetworkAddress} is not valid for CIDR /{viewModel.Cidr}. " +
                        $"The network address must align with the subnet boundary.");
                    await LoadParentSubnets(viewModel);
                    return View(viewModel);
                }

                // Check for subnet with same network/cidr
                Subnet? existingSubnet = await context.Subnets
                    .FirstOrDefaultAsync(s => s.NetworkAddress == viewModel.NetworkAddress &&
                                           s.Cidr == viewModel.Cidr);

                if (existingSubnet != null)
                {
                    ModelState.AddModelError("NetworkAddress",
                        $"A subnet with network {viewModel.NetworkAddress}/{viewModel.Cidr} already exists");
                    await LoadParentSubnets(viewModel);
                    return View(viewModel);
                }

                // Always check for the most specific parent subnet
                // Get all existing subnets
                List<Subnet> allSubnets = await context.Subnets.ToListAsync();

                // Find closest containing subnet (most specific parent)
                Subnet? bestParent = null;
                int bestParentCidr = -1;

                foreach (Subnet? candidateParent in allSubnets)
                {
                    // Skip the subnet if it's the same as our input
                    if (candidateParent.NetworkAddress == viewModel.NetworkAddress && candidateParent.Cidr == viewModel.Cidr)
                    {
                        continue;
                    }

                    // Check if this subnet would contain our new subnet
                    if (ipUtilityService.IsSubnetContainedInParent(
                        viewModel.NetworkAddress, viewModel.Cidr,
                        candidateParent.NetworkAddress, candidateParent.Cidr))
                    {
                        // If we found a better (more specific) parent
                        if (candidateParent.Cidr > bestParentCidr)
                        {
                            bestParent = candidateParent;
                            bestParentCidr = candidateParent.Cidr;
                        }
                    }
                }

                // If we found a better parent than what was selected
                if (bestParent != null)
                {
                    if (!viewModel.ParentSubnetId.HasValue)
                    {
                        // No parent was selected, but one is required
                        ModelState.AddModelError("ParentSubnetId",
                            $"This subnet must be a child of subnet {bestParent.Name} " +
                            $"({bestParent.NetworkAddress}/{bestParent.Cidr}).");
                        await LoadParentSubnets(viewModel);
                        return View(viewModel);
                    }
                    else if (viewModel.ParentSubnetId.Value != bestParent.Id)
                    {
                        // Wrong parent was selected
                        Subnet? selectedParent = await context.Subnets.FindAsync(viewModel.ParentSubnetId.Value);

                        if (selectedParent != null && bestParent.Cidr > selectedParent.Cidr)
                        {
                            // Selected parent is less specific than best parent
                            ModelState.AddModelError("ParentSubnetId",
                                $"A more specific parent subnet exists: {bestParent.Name} " +
                                $"({bestParent.NetworkAddress}/{bestParent.Cidr}). Please select it instead.");
                            await LoadParentSubnets(viewModel);
                            return View(viewModel);
                        }
                    }
                }

                // Check if new subnet would contain any existing subnets
                foreach (Subnet? potentialChildSubnet in allSubnets)
                {
                    if (ipUtilityService.IsSubnetContainedInParent(
                        potentialChildSubnet.NetworkAddress, potentialChildSubnet.Cidr,
                        viewModel.NetworkAddress, viewModel.Cidr))
                    {
                        ModelState.AddModelError("NetworkAddress",
                            $"This subnet would contain existing subnet {potentialChildSubnet.Name} " +
                            $"({potentialChildSubnet.NetworkAddress}/{potentialChildSubnet.Cidr}). This would create an invalid hierarchy.");
                        await LoadParentSubnets(viewModel);
                        return View(viewModel);
                    }
                }

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

                TempData["SuccessMessage"] = $"Subnet '{subnet.Name}' was created successfully.";
                return RedirectToAction(nameof(Details), new { id = subnet.Id });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error creating subnet: {ex.Message}");
            }
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
