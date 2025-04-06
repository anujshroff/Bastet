using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Models.DTOs;
using Bastet.Services;
using Bastet.Services.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Bastet.Controllers;

public class HostIpController : Controller
{
    private readonly BastetDbContext _context;
    private readonly IHostIpValidationService _hostIpValidationService;
    private readonly IIpUtilityService _ipUtilityService;
    private readonly IUserContextService _userContextService;

    public HostIpController(
        BastetDbContext context,
        IHostIpValidationService hostIpValidationService,
        IIpUtilityService ipUtilityService,
        IUserContextService userContextService)
    {
        _context = context;
        _hostIpValidationService = hostIpValidationService;
        _ipUtilityService = ipUtilityService;
        _userContextService = userContextService;
    }

    // GET: HostIp/Index/5 (5 is the subnetId)
    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> Index(int subnetId)
    {
        Subnet? subnet = await _context.Subnets
            .Include(s => s.HostIpAssignments)
            .FirstOrDefaultAsync(s => s.Id == subnetId);

        if (subnet == null)
        {
            return NotFound();
        }

        // Check if subnet can have host IPs
        if (subnet.ChildSubnets.Count > 0 || subnet.IsFullyAllocated)
        {
            TempData["ErrorMessage"] = "This subnet cannot have host IP assignments because it has child subnets or is fully allocated.";
            return RedirectToAction("Details", "Subnet", new { id = subnetId });
        }

        // Order host IPs by address
        List<HostIpViewModel> hostIps = subnet.HostIpAssignments
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
            })
            .ToList();

        ViewBag.SubnetId = subnetId;
        ViewBag.SubnetName = subnet.Name;
        ViewBag.NetworkAddress = subnet.NetworkAddress;
        ViewBag.Cidr = subnet.Cidr;

        return View(hostIps);
    }

    // GET: HostIp/Create/5 (5 is the subnetId)
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Create(int subnetId)
    {
        Subnet? subnet = await _context.Subnets.FindAsync(subnetId);
        if (subnet == null)
        {
            return NotFound();
        }

        // Check if subnet can have host IPs
        ValidationResult validationResult = _hostIpValidationService.ValidateSubnetCanContainHostIp(subnetId);
        if (!validationResult.IsValid)
        {
            foreach (ValidationError error in validationResult.Errors)
            {
                ModelState.AddModelError("", error.Message);
            }
            return RedirectToAction("Details", "Subnet", new { id = subnetId });
        }

        // Create the view model
        CreateHostIpViewModel viewModel = new()
        {
            SubnetId = subnetId,
            SubnetInfo = $"{subnet.Name} ({subnet.NetworkAddress}/{subnet.Cidr})",
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            SubnetRange = $"{subnet.NetworkAddress} - {_ipUtilityService.CalculateBroadcastAddress(subnet.NetworkAddress, subnet.Cidr)}"
        };

        return View(viewModel);
    }

    // POST: HostIp/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Create(CreateHostIpViewModel viewModel)
    {
        if (ModelState.IsValid)
        {
            try
            {
                // Validate host IP assignment
                ValidationResult validationResult = _hostIpValidationService.ValidateNewHostIp(viewModel.IP, viewModel.SubnetId);
                if (!validationResult.IsValid)
                {
                    foreach (ValidationError error in validationResult.Errors)
                    {
                        ModelState.AddModelError("", error.Message);
                    }

                    // Refresh subnet info for display
                    Subnet? subnet = await _context.Subnets.FindAsync(viewModel.SubnetId);
                    if (subnet != null)
                    {
                        viewModel.SubnetInfo = $"{subnet.Name} ({subnet.NetworkAddress}/{subnet.Cidr})";
                        viewModel.NetworkAddress = subnet.NetworkAddress;
                        viewModel.Cidr = subnet.Cidr;
                        viewModel.SubnetRange = $"{subnet.NetworkAddress} - {_ipUtilityService.CalculateBroadcastAddress(subnet.NetworkAddress, subnet.Cidr)}";
                    }

                    return View(viewModel);
                }

                // Create host IP assignment
                HostIpAssignment hostIp = new()
                {
                    IP = viewModel.IP,
                    Name = viewModel.Name,
                    SubnetId = viewModel.SubnetId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _userContextService.GetCurrentUsername()
                };

                _context.HostIpAssignments.Add(hostIp);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Host IP {hostIp.IP} was created successfully.";
                return RedirectToAction(nameof(Index), new { subnetId = viewModel.SubnetId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error creating host IP: {ex.Message}");
            }
        }

        // If we get here, something went wrong
        // Refresh subnet info for display
        Subnet? subnetForError = await _context.Subnets.FindAsync(viewModel.SubnetId);
        if (subnetForError != null)
        {
            viewModel.SubnetInfo = $"{subnetForError.Name} ({subnetForError.NetworkAddress}/{subnetForError.Cidr})";
            viewModel.NetworkAddress = subnetForError.NetworkAddress;
            viewModel.Cidr = subnetForError.Cidr;
            viewModel.SubnetRange = $"{subnetForError.NetworkAddress} - {_ipUtilityService.CalculateBroadcastAddress(subnetForError.NetworkAddress, subnetForError.Cidr)}";
        }

        return View(viewModel);
    }

    // GET: HostIp/Edit/{ip}
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Edit(string ip)
    {
        HostIpAssignment? hostIp = await _context.HostIpAssignments
            .Include(h => h.Subnet)
            .FirstOrDefaultAsync(h => h.IP == ip);

        if (hostIp == null)
        {
            return NotFound();
        }

        EditHostIpViewModel viewModel = new()
        {
            IP = hostIp.IP,
            Name = hostIp.Name,
            SubnetId = hostIp.SubnetId,
            SubnetInfo = $"{hostIp.Subnet.Name} ({hostIp.Subnet.NetworkAddress}/{hostIp.Subnet.Cidr})",
            CreatedAt = hostIp.CreatedAt,
            LastModifiedAt = hostIp.LastModifiedAt,
            RowVersion = hostIp.RowVersion ?? Array.Empty<byte>()
        };

        return View(viewModel);
    }

    // POST: HostIp/Edit/{ip}
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Edit(string ip, EditHostIpViewModel viewModel)
    {
        if (ip != viewModel.IP)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                // Validate host IP update
                ValidationResult validationResult = _hostIpValidationService.ValidateHostIpUpdate(
                    ip, 
                    new UpdateHostIpDto 
                    { 
                        IP = viewModel.IP, 
                        Name = viewModel.Name, 
                        RowVersion = viewModel.RowVersion 
                    }, 
                    viewModel.RowVersion);

                if (!validationResult.IsValid)
                {
                    foreach (ValidationError error in validationResult.Errors)
                    {
                        ModelState.AddModelError("", error.Message);
                    }
                    return View(viewModel);
                }

                // Find and update the host IP
                HostIpAssignment? hostIp = await _context.HostIpAssignments.FindAsync(ip);
                if (hostIp == null)
                {
                    return NotFound();
                }

                hostIp.Name = viewModel.Name;
                hostIp.LastModifiedAt = DateTime.UtcNow;
                hostIp.ModifiedBy = _userContextService.GetCurrentUsername();

                _context.Update(hostIp);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Host IP {hostIp.IP} was updated successfully.";
                return RedirectToAction(nameof(Index), new { subnetId = hostIp.SubnetId });
            }
            catch (DbUpdateConcurrencyException)
            {
                // Handle concurrency conflict
                if (!HostIpExists(ip))
                {
                    return NotFound();
                }

                ModelState.AddModelError("", "The host IP was modified by another user. Please reload and try again.");
                return View(viewModel);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error updating host IP: {ex.Message}");
            }
        }

        return View(viewModel);
    }

    // GET: HostIp/Delete/{ip}
    [Authorize(Policy = "RequireDeleteRole")]
    public async Task<IActionResult> Delete(string ip)
    {
        HostIpAssignment? hostIp = await _context.HostIpAssignments
            .Include(h => h.Subnet)
            .FirstOrDefaultAsync(h => h.IP == ip);

        if (hostIp == null)
        {
            return NotFound();
        }

        DeleteHostIpViewModel viewModel = new()
        {
            IP = hostIp.IP,
            Name = hostIp.Name,
            SubnetInfo = $"{hostIp.Subnet.Name} ({hostIp.Subnet.NetworkAddress}/{hostIp.Subnet.Cidr})",
            CreatedAt = hostIp.CreatedAt,
            CreatedBy = hostIp.CreatedBy
        };

        return View(viewModel);
    }

    // POST: HostIp/Delete/{ip}
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireDeleteRole")]
    public async Task<IActionResult> DeleteConfirmed(string ip)
    {
        // Validate host IP deletion
        ValidationResult validationResult = _hostIpValidationService.ValidateHostIpDeletion(ip);
        if (!validationResult.IsValid)
        {
            foreach (ValidationError error in validationResult.Errors)
            {
                TempData["ErrorMessage"] = error.Message;
            }
            return RedirectToAction(nameof(Delete), new { ip });
        }

        // Begin a transaction to ensure data consistency
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Get the host IP assignment
            HostIpAssignment? hostIp = await _context.HostIpAssignments.FindAsync(ip);
            if (hostIp == null)
            {
                return NotFound();
            }

            int subnetId = hostIp.SubnetId;

            // Create record in DeletedHostIpAssignments
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
                DeletedBy = _userContextService.GetCurrentUsername()
            };

            _context.DeletedHostIpAssignments.Add(deletedHostIp);

            // Remove the host IP assignment
            _context.HostIpAssignments.Remove(hostIp);
            await _context.SaveChangesAsync();

            // Commit the transaction
            await transaction.CommitAsync();

            TempData["SuccessMessage"] = $"Host IP {ip} was deleted successfully.";
            return RedirectToAction(nameof(Index), new { subnetId });
        }
        catch (Exception ex)
        {
            // Rollback transaction on error
            await transaction.RollbackAsync();
            TempData["ErrorMessage"] = $"Error deleting host IP: {ex.Message}";
            return RedirectToAction(nameof(Delete), new { ip });
        }
    }

    // GET: HostIp/DeletedHostIps/{subnetId}
    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> DeletedHostIps(int subnetId)
    {
        Subnet? subnet = await _context.Subnets.FindAsync(subnetId);
        if (subnet == null)
        {
            return NotFound();
        }

        // Get deleted host IPs for this subnet
        List<DeletedHostIpAssignment> deletedHostIps = await _context.DeletedHostIpAssignments
            .Where(h => h.OriginalSubnetId == subnetId)
            .OrderByDescending(h => h.DeletedAt)
            .ToListAsync();

        // Map to view models
        List<DeletedHostIpViewModel> viewModels = deletedHostIps.Select(d => new DeletedHostIpViewModel
        {
            Id = d.Id,
            OriginalIP = d.OriginalIP,
            Name = d.Name,
            OriginalSubnetId = d.OriginalSubnetId,
            DeletedAt = d.DeletedAt,
            DeletedBy = d.DeletedBy,
            CreatedAt = d.CreatedAt,
            CreatedBy = d.CreatedBy,
            LastModifiedAt = d.LastModifiedAt,
            ModifiedBy = d.ModifiedBy
        }).ToList();

        // Create the list view model
        DeletedHostIpListViewModel model = new()
        {
            DeletedHostIps = viewModels,
            TotalCount = viewModels.Count,
            SubnetId = subnetId,
            SubnetName = subnet.Name,
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr
        };

        return View(model);
    }

    // Post: HostIp/SetAllocationStatus
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> SetAllocationStatus(SubnetAllocationDto dto)
    {
        if (!ModelState.IsValid)
        {
            return RedirectToAction("Details", "Subnet", new { id = dto.SubnetId });
        }

        // Find the subnet
        Subnet? subnet = await _context.Subnets
            .Include(s => s.ChildSubnets)
            .Include(s => s.HostIpAssignments)
            .FirstOrDefaultAsync(s => s.Id == dto.SubnetId);

        if (subnet == null)
        {
            return NotFound();
        }

        // If we're trying to mark as fully allocated, validate
        if (dto.IsFullyAllocated)
        {
            ValidationResult validationResult = _hostIpValidationService.ValidateSubnetCanBeFullyAllocated(dto.SubnetId);
            if (!validationResult.IsValid)
            {
                foreach (ValidationError error in validationResult.Errors)
                {
                    TempData["ErrorMessage"] = error.Message;
                }
                return RedirectToAction("Details", "Subnet", new { id = dto.SubnetId });
            }
        }

        // Update the subnet
        subnet.IsFullyAllocated = dto.IsFullyAllocated;
        subnet.LastModifiedAt = DateTime.UtcNow;
        subnet.ModifiedBy = _userContextService.GetCurrentUsername();

        _context.Update(subnet);
        await _context.SaveChangesAsync();

        string statusMessage = dto.IsFullyAllocated
            ? $"Subnet '{subnet.Name}' was marked as fully allocated."
            : $"Subnet '{subnet.Name}' was marked as not fully allocated.";

        TempData["SuccessMessage"] = statusMessage;
        return RedirectToAction("Details", "Subnet", new { id = dto.SubnetId });
    }

    private bool HostIpExists(string ip) => 
        _context.HostIpAssignments.Any(e => e.IP == ip);
}
