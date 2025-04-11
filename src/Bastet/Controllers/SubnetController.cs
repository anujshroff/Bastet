using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Bastet.Controllers;

public class SubnetController(BastetDbContext context, IIpUtilityService ipUtilityService, ISubnetValidationService subnetValidationService, IHostIpValidationService hostIpValidationService, IUserContextService userContextService) : Controller
{
    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> Index()
    {
        // Get all subnets with their relationships
        List<Subnet> allSubnets = await context.Subnets
            .Include(s => s.ChildSubnets)
            .ToListAsync();

        // Build the subnet hierarchy
        List<Subnet> rootSubnets = [.. allSubnets.Where(s => !s.ParentSubnetId.HasValue)];
        List<SubnetTreeViewModel> hierarchicalSubnets = [];

        foreach (Subnet? rootSubnet in rootSubnets)
        {
            hierarchicalSubnets.Add(BuildSubnetTreeViewModel(rootSubnet, allSubnets));
        }

        return View(hierarchicalSubnets);
    }

    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> Details(int id)
    {
        Subnet? subnet = await context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subnet == null)
        {
            // Use our custom 404 page with helpful context
            return RedirectToAction("HttpStatusCodeHandler", "Error", new
            {
                statusCode = 404,
                errorMessage = $"Subnet with ID {id} could not be found."
            });
        }

        SubnetDetailsViewModel viewModel = new()
        {
            Id = subnet.Id,
            Name = subnet.Name,
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            Description = subnet.Description,
            Tags = subnet.Tags,
            ParentSubnetId = subnet.ParentSubnetId,
            CreatedAt = subnet.CreatedAt,
            LastModifiedAt = subnet.LastModifiedAt,
            CreatedBy = subnet.CreatedBy,
            ModifiedBy = subnet.ModifiedBy,
            IsFullyAllocated = subnet.IsFullyAllocated,
            // Calculate subnet properties
            SubnetMask = ipUtilityService.CalculateSubnetMask(subnet.Cidr),
            BroadcastAddress = ipUtilityService.CalculateBroadcastAddress(subnet.NetworkAddress, subnet.Cidr),
            TotalIpAddresses = ipUtilityService.CalculateTotalIpAddresses(subnet.Cidr),
            UsableIpAddresses = ipUtilityService.CalculateUsableIpAddresses(subnet.Cidr),
            // Include children, ordered by network address
            ChildSubnets = [.. subnet.ChildSubnets
                .OrderBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[0])
                .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[1])
                .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[2])
                .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[3])
                .Select(c => new SubnetViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    NetworkAddress = c.NetworkAddress,
                    Cidr = c.Cidr
                })],
            // Include host IP assignments if any
            HostIpAssignments = [.. subnet.HostIpAssignments
                .OrderBy(h => IPAddress.Parse(h.IP).GetAddressBytes()[0])
                .ThenBy(h => IPAddress.Parse(h.IP).GetAddressBytes()[1])
                .ThenBy(h => IPAddress.Parse(h.IP).GetAddressBytes()[2])
                .ThenBy(h => IPAddress.Parse(h.IP).GetAddressBytes()[3])
                .Select(h => new HostIpViewModel
                {
                    IP = h.IP,
                    Name = h.Name,
                    CreatedAt = h.CreatedAt,
                    CreatedBy = h.CreatedBy,
                    LastModifiedAt = h.LastModifiedAt,
                    ModifiedBy = h.ModifiedBy
                })],
            // Get unallocated IP ranges, factoring in both child subnets and host IPs
            UnallocatedRanges = [.. ipUtilityService.CalculateUnallocatedRanges(
                subnet.NetworkAddress,
                subnet.Cidr,
                subnet.ChildSubnets,
                subnet.HostIpAssignments)]
        };

        // Try to get parent subnet if exists
        if (subnet.ParentSubnetId.HasValue)
        {
            Subnet? parentSubnet = await context.Subnets.FindAsync(subnet.ParentSubnetId.Value);
            if (parentSubnet != null)
            {
                viewModel.ParentSubnetName = parentSubnet.Name;
                viewModel.ParentNetworkAddress = $"{parentSubnet.NetworkAddress}/{parentSubnet.Cidr}";
            }
        }

        return View(viewModel);
    }

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
            LastModifiedAt = subnet.LastModifiedAt
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

                    context.Subnets.Update(subnet);
                    await context.SaveChangesAsync();

                    // Commit the transaction
                    await transaction.CommitAsync();

                    TempData["SuccessMessage"] = $"Subnet '{subnet.Name}' was updated successfully.";
                    return RedirectToAction(nameof(Details), new { id = subnet.Id });
                }
                catch (Exception ex)
                {
                    // Rollback the transaction on error
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", $"Error updating subnet: {ex.Message}");
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SubnetExists(id))
                {
                    return RedirectToAction("HttpStatusCodeHandler", "Error", new
                    {
                        statusCode = 404,
                        errorMessage = "The subnet no longer exists. It may have been deleted by another user."
                    });
                }

                // Handle concurrency conflict
                ModelState.AddModelError("", "The subnet was modified by another user. Please reload and try again.");
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

        if (origSubnet.ParentSubnet != null)
        {
            viewModel.ParentSubnetInfo = $"{origSubnet.ParentSubnet.Name} ({origSubnet.ParentSubnet.NetworkAddress}/{origSubnet.ParentSubnet.Cidr})";
        }

        return View(viewModel);
    }

    // GET: Subnet/Delete/5
    [Authorize(Policy = "RequireDeleteRole")]
    public async Task<IActionResult> Delete(int id)
    {
        Subnet? subnet = await context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subnet == null)
        {
            return RedirectToAction("HttpStatusCodeHandler", "Error", new
            {
                statusCode = 404,
                errorMessage = $"The subnet with ID {id} could not be found or may have been deleted."
            });
        }

        // Count all descendants (not just direct children)
        int descendantCount = await CountAllDescendants(id);

        // Count all host IPs in this subnet
        int hostIpCount = subnet.HostIpAssignments.Count;

        // Count host IPs in all descendant subnets
        hostIpCount += await CountAllDescendantHostIps(id);

        DeleteSubnetViewModel viewModel = new()
        {
            Id = subnet.Id,
            Name = subnet.Name,
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            Description = subnet.Description,
            ChildSubnetCount = descendantCount,
            HostIpCount = hostIpCount,
            IsFullyAllocated = subnet.IsFullyAllocated
        };

        return View(viewModel);
    }

    // Helper method to count all host IPs in descendant subnets
    private async Task<int> CountAllDescendantHostIps(int subnetId)
    {
        // Get all subnets with their host IP assignments
        List<Subnet> allSubnets = await context.Subnets
            .Include(s => s.HostIpAssignments)
            .ToListAsync();

        int hostIpCount = 0;

        // Set to keep track of processed IDs to avoid circular references
        HashSet<int> processedIds = [];

        // Queue for breadth-first traversal
        Queue<int> queue = new();
        queue.Enqueue(subnetId);
        processedIds.Add(subnetId);

        while (queue.Count > 0)
        {
            int currentId = queue.Dequeue();

            // Find all direct children of the current subnet
            List<Subnet> childSubnets = [.. allSubnets.Where(s => s.ParentSubnetId == currentId)];

            foreach (Subnet? child in childSubnets)
            {
                if (!processedIds.Contains(child.Id))
                {
                    // Count host IPs in this child subnet
                    hostIpCount += child.HostIpAssignments.Count;

                    queue.Enqueue(child.Id);
                    processedIds.Add(child.Id);
                }
            }
        }

        return hostIpCount;
    }

    // POST: Subnet/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireDeleteRole")]
    public async Task<IActionResult> DeleteConfirmed(int id, string confirmation)
    {
        // Verify the confirmation text
        if (confirmation != "approved")
        {
            TempData["ErrorMessage"] = "You must type 'approved' to confirm deletion.";
            return RedirectToAction(nameof(Delete), new { id });
        }

        // Load the main subnet with its child relationships and host IPs
        Subnet? subnet = await context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subnet == null)
        {
            return RedirectToAction("HttpStatusCodeHandler", "Error", new
            {
                statusCode = 404,
                errorMessage = $"The subnet with ID {id} could not be found or may have been deleted."
            });
        }

        // Begin a transaction to ensure data consistency
        using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        try
        {
            // Get all descendants to delete (ordered for proper deletion - deepest first)
            List<Subnet> allDescendants = await GetAllDescendantsOrdered(id);

            // Add the subnet itself to be deleted (will be processed last)
            allDescendants.Add(subnet);

            // Get all host IPs for all subnets to be deleted
            List<HostIpAssignment> allHostIps = [];

            // First, load all host IPs for each subnet to be deleted
            foreach (Subnet subnetToProcess in allDescendants)
            {
                // We need to load host IPs for each subnet since they're not included in GetAllDescendantsOrdered
                Subnet? subnetWithHostIps = await context.Subnets
                    .Include(s => s.HostIpAssignments)
                    .FirstOrDefaultAsync(s => s.Id == subnetToProcess.Id);

                if (subnetWithHostIps != null && subnetWithHostIps.HostIpAssignments.Count > 0)
                {
                    allHostIps.AddRange(subnetWithHostIps.HostIpAssignments);
                }
            }

            // First handle all host IPs (archive them in DeletedHostIpAssignments)
            foreach (HostIpAssignment hostIp in allHostIps)
            {
                // Create a deletion record
                DeletedHostIpAssignment deletedHostIp = new()
                {
                    OriginalIP = hostIp.IP,
                    Name = hostIp.Name,
                    OriginalSubnetId = hostIp.SubnetId,
                    CreatedAt = hostIp.CreatedAt,
                    LastModifiedAt = hostIp.LastModifiedAt,
                    CreatedBy = hostIp.CreatedBy,
                    ModifiedBy = hostIp.ModifiedBy,
                    DeletedAt = DateTime.UtcNow,
                    DeletedBy = userContextService.GetCurrentUsername()
                };

                // Add to DeletedHostIpAssignments table
                context.DeletedHostIpAssignments.Add(deletedHostIp);

                // Remove from HostIpAssignments table
                context.HostIpAssignments.Remove(hostIp);
            }

            // Now process each subnet
            foreach (Subnet subnetToDelete in allDescendants)
            {
                // Add to DeletedSubnets table
                DeletedSubnet deletedSubnet = new()
                {
                    OriginalId = subnetToDelete.Id,
                    OriginalParentId = subnetToDelete.ParentSubnetId,
                    Name = subnetToDelete.Name,
                    NetworkAddress = subnetToDelete.NetworkAddress,
                    Cidr = subnetToDelete.Cidr,
                    Description = subnetToDelete.Description,
                    Tags = subnetToDelete.Tags,
                    CreatedAt = subnetToDelete.CreatedAt,
                    LastModifiedAt = subnetToDelete.LastModifiedAt,
                    CreatedBy = subnetToDelete.CreatedBy,
                    ModifiedBy = subnetToDelete.ModifiedBy,
                    DeletedAt = DateTime.UtcNow,
                    DeletedBy = userContextService.GetCurrentUsername()
                };

                context.DeletedSubnets.Add(deletedSubnet);

                // Remove from Subnets table
                context.Subnets.Remove(subnetToDelete);
            }

            // Save all changes
            await context.SaveChangesAsync();

            // Commit the transaction
            await transaction.CommitAsync();

            int totalHostIpsDeleted = allHostIps.Count;
            TempData["SuccessMessage"] = $"Subnet '{subnet.Name}' and {allDescendants.Count - 1} child subnet(s) were deleted successfully. " +
                                       $"{totalHostIpsDeleted} host IP assignment(s) were archived.";

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            // Rollback the transaction on error
            await transaction.RollbackAsync();
            TempData["ErrorMessage"] = $"Error deleting subnet: {ex.Message}";
            return RedirectToAction(nameof(Delete), new { id });
        }
    }

    // Helper method to count all descendants of a subnet using in-memory approach
    private async Task<int> CountAllDescendants(int subnetId)
    {
        // Get all subnets from the database
        List<Subnet> allSubnets = await context.Subnets.ToListAsync();

        // Store the count of descendants
        int descendantCount = 0;

        // Set to keep track of processed IDs to avoid circular references
        HashSet<int> processedIds = [];

        // Queue for breadth-first traversal
        Queue<int> queue = new();
        queue.Enqueue(subnetId);
        processedIds.Add(subnetId);

        while (queue.Count > 0)
        {
            int currentId = queue.Dequeue();

            // Find all direct children of the current subnet
            List<Subnet> childSubnets = [.. allSubnets.Where(s => s.ParentSubnetId == currentId)];

            foreach (Subnet? child in childSubnets)
            {
                if (!processedIds.Contains(child.Id))
                {
                    descendantCount++;
                    queue.Enqueue(child.Id);
                    processedIds.Add(child.Id);
                }
            }
        }

        return descendantCount;
    }

    // Helper method to get all descendants ordered for deletion (deepest children first)
    private async Task<List<Subnet>> GetAllDescendantsOrdered(int subnetId)
    {
        // Start with all subnets
        List<Subnet> allSubnets = await context.Subnets.ToListAsync();

        // Create a dictionary for faster lookup
        Dictionary<int, Subnet> subnetDict = allSubnets.ToDictionary(s => s.Id);

        // Build a tree structure
        Dictionary<int, List<int>> tree = [];
        foreach (Subnet? s in allSubnets)
        {
            if (!tree.ContainsKey(s.Id))
            {
                tree[s.Id] = [];
            }

            if (s.ParentSubnetId.HasValue)
            {
                if (!tree.TryGetValue(s.ParentSubnetId.Value, out List<int>? value))
                {
                    value = [];
                    tree[s.ParentSubnetId.Value] = value;
                }

                value.Add(s.Id);
            }
        }

        // Recursively collect descendants in order
        List<Subnet> result = [];
        CollectDescendants(subnetId, tree, subnetDict, result);

        // Remove the root subnet itself (it will be added later)
        result.RemoveAll(s => s.Id == subnetId);

        return result;
    }

    // Helper method for recursively collecting descendants
    private static void CollectDescendants(int subnetId, Dictionary<int, List<int>> tree,
                                  Dictionary<int, Subnet> subnetDict, List<Subnet> result)
    {
        // Process children first (depth-first)
        if (tree.TryGetValue(subnetId, out List<int>? value))
        {
            foreach (int childId in value)
            {
                CollectDescendants(childId, tree, subnetDict, result);
            }
        }

        // Add this subnet to the result
        if (subnetDict.TryGetValue(subnetId, out Subnet? value2))
        {
            result.Add(value2);
        }
    }

    // GET: Subnet/DeletedSubnets
    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> DeletedSubnets()
    {
        // Get deleted subnets from the database
        List<DeletedSubnet> deletedSubnets = await context.DeletedSubnets
            .OrderByDescending(s => s.DeletedAt)
            .ToListAsync();

        // Map to view models
        List<DeletedSubnetsViewModel> viewModels = [.. deletedSubnets.Select(ds => new DeletedSubnetsViewModel
        {
            OriginalId = ds.OriginalId,
            Name = ds.Name,
            NetworkAddress = ds.NetworkAddress,
            Cidr = ds.Cidr,
            Description = ds.Description,
            OriginalParentId = ds.OriginalParentId,
            DeletedAt = ds.DeletedAt,
            DeletedBy = ds.DeletedBy,
            CreatedAt = ds.CreatedAt,
            LastModifiedAt = ds.LastModifiedAt,
            CreatedBy = ds.CreatedBy,
            ModifiedBy = ds.ModifiedBy
        })];

        // Create the list view model
        DeletedSubnetListViewModel model = new()
        {
            DeletedSubnets = viewModels,
            TotalCount = viewModels.Count
        };

        return View(model);
    }

    private bool SubnetExists(int id) => context.Subnets.Any(e => e.Id == id);

    private SubnetTreeViewModel BuildSubnetTreeViewModel(Subnet subnet, List<Subnet> allSubnets)
    {
        SubnetTreeViewModel viewModel = new()
        {
            Id = subnet.Id,
            Name = subnet.Name,
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            Description = subnet.Description,
            SubnetMask = ipUtilityService.CalculateSubnetMask(subnet.Cidr),
            TotalIpAddresses = ipUtilityService.CalculateTotalIpAddresses(subnet.Cidr),
            UsableIpAddresses = ipUtilityService.CalculateUsableIpAddresses(subnet.Cidr),
            ParentSubnetId = subnet.ParentSubnetId,
            ChildSubnets = []
        };

        // Recursively build child subnet trees, ordered by network address
        foreach (Subnet? childSubnet in subnet.ChildSubnets
            .OrderBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[0])
            .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[1])
            .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[2])
            .ThenBy(s => IPAddress.Parse(s.NetworkAddress).GetAddressBytes()[3]))
        {
            viewModel.ChildSubnets.Add(BuildSubnetTreeViewModel(childSubnet, allSubnets));
        }

        return viewModel;
    }
}
