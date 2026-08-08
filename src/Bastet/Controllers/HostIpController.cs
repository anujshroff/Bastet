using Bastet.Data;
using Bastet.Models;
using Bastet.Models.DTOs;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Data;
using Bastet.Services.Locking;
using Bastet.Services.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Bastet.Controllers;

[Authorize(Policy = "RequireViewRole")]
public class HostIpController(
    BastetDbContext context,
    IHostIpValidationService hostIpValidationService,
    IIpUtilityService ipUtilityService,
    IUserContextService userContextService,
    ISubnetLockingService subnetLockingService,
    ILogger<HostIpController> logger) : Controller
{

    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> Index(int subnetId)
    {
        Subnet? subnet = await context.Subnets
            .Include(s => s.HostIpAssignments)
            .Include(s => s.ChildSubnets)
            .FirstOrDefaultAsync(s => s.Id == subnetId);

        if (subnet == null)
        {
            return NotFound();
        }

        if (subnet.ChildSubnets.Count > 0 || subnet.IsFullyAllocated)
        {
            TempData["ErrorMessage"] = "This subnet cannot have host IP assignments because it has child subnets or is fully allocated.";
            return RedirectToAction("Details", "Subnet", new { id = subnetId });
        }

        List<HostIpViewModel> hostIps = [.. subnet.HostIpAssignments
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
            })];

        ViewBag.SubnetId = subnetId;
        ViewBag.SubnetName = subnet.Name;
        ViewBag.NetworkAddress = subnet.NetworkAddress;
        ViewBag.Cidr = subnet.Cidr;

        return View(hostIps);
    }

    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Create(int subnetId)
    {
        Subnet? subnet = await context.Subnets.FindAsync(subnetId);
        if (subnet == null)
        {
            return NotFound();
        }

        ValidationResult validationResult = hostIpValidationService.ValidateSubnetCanContainHostIp(subnetId);
        if (!validationResult.IsValid)
        {

            TempData["ErrorMessage"] = string.Join(" ", validationResult.Errors.Select(e => e.Message));
            return RedirectToAction("Details", "Subnet", new { id = subnetId });
        }

        CreateHostIpViewModel viewModel = new()
        {
            SubnetId = subnetId,
            SubnetInfo = $"{subnet.Name} ({subnet.NetworkAddress}/{subnet.Cidr})",
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            SubnetRange = $"{subnet.NetworkAddress} - {ipUtilityService.CalculateBroadcastAddress(subnet.NetworkAddress, subnet.Cidr)}"
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Create(CreateHostIpViewModel viewModel)
    {
        if (ModelState.IsValid)
        {
            try
            {

                return await subnetLockingService.ExecuteWithSubnetLockAsync<IActionResult>(async () =>
                {

                    ValidationResult validationResult = hostIpValidationService.ValidateNewHostIp(viewModel.IP, viewModel.SubnetId);
                    if (!validationResult.IsValid)
                    {
                        foreach (ValidationError error in validationResult.Errors)
                        {
                            ModelState.AddModelError("", error.Message);
                        }

                        Subnet? subnet = await context.Subnets.FindAsync(viewModel.SubnetId);
                        if (subnet != null)
                        {
                            viewModel.SubnetInfo = $"{subnet.Name} ({subnet.NetworkAddress}/{subnet.Cidr})";
                            viewModel.NetworkAddress = subnet.NetworkAddress;
                            viewModel.Cidr = subnet.Cidr;
                            viewModel.SubnetRange = $"{subnet.NetworkAddress} - {ipUtilityService.CalculateBroadcastAddress(subnet.NetworkAddress, subnet.Cidr)}";
                        }

                        return View(viewModel);
                    }

                    HostIpAssignment hostIp = new()
                    {
                        IP = viewModel.IP,
                        Name = viewModel.Name,
                        SubnetId = viewModel.SubnetId,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = userContextService.GetCurrentUsername()
                    };

                    context.HostIpAssignments.Add(hostIp);
                    await context.SaveChangesAsync();

                    TempData["SuccessMessage"] = $"Host IP {hostIp.IP} was created successfully.";
                    return RedirectToAction(nameof(Index), new { subnetId = viewModel.SubnetId });
                });
            }
            catch (TimeoutException)
            {
                ModelState.AddModelError("", "The operation timed out due to high concurrency. Please try again.");
            }
            catch (Exception ex) when (SqlSaveOutcome.IsIndeterminate(ex))
            {
                logger.LogError(ex, "Host IP create outcome unknown for subnet {SubnetId}", viewModel.SubnetId);
                ModelState.AddModelError("",
                    "BASTET could not confirm whether this host IP was created. "
                    + "Check the subnet's host IPs before retrying.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Host IP create failed for subnet {SubnetId}", viewModel.SubnetId);
                ModelState.AddModelError("", "Error creating host IP. Details have been logged.");
            }
        }

        Subnet? subnetForError = await context.Subnets.FindAsync(viewModel.SubnetId);
        if (subnetForError != null)
        {
            viewModel.SubnetInfo = $"{subnetForError.Name} ({subnetForError.NetworkAddress}/{subnetForError.Cidr})";
            viewModel.NetworkAddress = subnetForError.NetworkAddress;
            viewModel.Cidr = subnetForError.Cidr;
            viewModel.SubnetRange = $"{subnetForError.NetworkAddress} - {ipUtilityService.CalculateBroadcastAddress(subnetForError.NetworkAddress, subnetForError.Cidr)}";
        }

        return View(viewModel);
    }

    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Edit(string ip)
    {
        HostIpAssignment? hostIp = await context.HostIpAssignments
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
            RowVersion = hostIp.RowVersion ?? []
        };

        return View(viewModel);
    }

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
                return await subnetLockingService.ExecuteWithSubnetLockAsync<IActionResult>(async () =>
                {

                    ValidationResult validationResult = hostIpValidationService.ValidateHostIpUpdate(
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

                        bool isConcurrencyConflict = validationResult.Errors.Any(e => e.Code == "CONCURRENCY_CONFLICT");

                        if (isConcurrencyConflict)
                        {

                            HostIpAssignment? currentHostIp = await context.HostIpAssignments
                                .Include(h => h.Subnet)
                                .FirstOrDefaultAsync(h => h.IP == ip);

                            if (currentHostIp != null)
                            {

                                viewModel.RowVersion = currentHostIp.RowVersion ?? [];
                                viewModel.SubnetInfo = $"{currentHostIp.Subnet.Name} ({currentHostIp.Subnet.NetworkAddress}/{currentHostIp.Subnet.Cidr})";
                                viewModel.CreatedAt = currentHostIp.CreatedAt;
                                viewModel.LastModifiedAt = currentHostIp.LastModifiedAt;

                                ModelState.Remove(nameof(viewModel.RowVersion));
                            }

                            ModelState.AddModelError("",
                                "This host IP was modified by another user while you were editing it. " +
                                "Your changes have been preserved below, but you should review the current values before saving. " +
                                "Click 'Save Changes' again to apply your updates.");
                        }
                        else
                        {

                            foreach (ValidationError error in validationResult.Errors)
                            {
                                ModelState.AddModelError("", error.Message);
                            }
                        }

                        return View(viewModel);
                    }

                    HostIpAssignment? hostIp = await context.HostIpAssignments.FindAsync(ip);
                    if (hostIp == null)
                    {
                        return NotFound();
                    }

                    hostIp.Name = viewModel.Name;
                    hostIp.LastModifiedAt = DateTime.UtcNow;
                    hostIp.ModifiedBy = userContextService.GetCurrentUsername();

                    context.Update(hostIp);
                    await context.SaveChangesAsync();

                    TempData["SuccessMessage"] = $"Host IP {hostIp.IP} was updated successfully.";
                    return RedirectToAction(nameof(Index), new { subnetId = hostIp.SubnetId });
                });
            }
            catch (TimeoutException)
            {
                ModelState.AddModelError("", "The operation timed out due to high concurrency. Please try again.");
                return View(viewModel);
            }
            catch (DbUpdateConcurrencyException)
            {

                if (!HostIpExists(ip))
                {
                    return NotFound();
                }

                ModelState.AddModelError("", "The host IP was modified by another user. Please reload and try again.");
                return View(viewModel);
            }
            catch (Exception ex) when (SqlSaveOutcome.IsIndeterminate(ex))
            {
                logger.LogError(ex, "Host IP edit outcome unknown");
                ModelState.AddModelError("",
                    "BASTET could not confirm whether this change was applied. "
                    + "Reload the host IP to see its current state before retrying.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Host IP edit failed");
                ModelState.AddModelError("", "Error updating host IP. Details have been logged.");
            }
        }

        return View(viewModel);
    }

    [Authorize(Policy = "RequireDeleteRole")]
    public async Task<IActionResult> Delete(string ip)
    {
        HostIpAssignment? hostIp = await context.HostIpAssignments
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
            SubnetId = hostIp.SubnetId,
            CreatedAt = hostIp.CreatedAt,
            CreatedBy = hostIp.CreatedBy
        };

        return View(viewModel);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireDeleteRole")]
    public async Task<IActionResult> DeleteConfirmed(string ip, string confirmation)
    {
        if (confirmation != "approved")
        {
            TempData["ErrorMessage"] = "You must type 'approved' to confirm deletion.";
            return RedirectToAction(nameof(Delete), new { ip });
        }

        try
        {
            return await subnetLockingService.ExecuteWithSubnetLockAsync<IActionResult>(async () =>
            {

                ValidationResult validationResult = hostIpValidationService.ValidateHostIpDeletion(ip);
                if (!validationResult.IsValid)
                {
                    foreach (ValidationError error in validationResult.Errors)
                    {
                        TempData["ErrorMessage"] = error.Message;
                    }

                    return RedirectToAction(nameof(Delete), new { ip });
                }

                using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

                try
                {

                    HostIpAssignment? hostIp = await context.HostIpAssignments.FindAsync(ip);
                    if (hostIp == null)
                    {
                        return NotFound();
                    }

                    int subnetId = hostIp.SubnetId;

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

                    context.DeletedHostIpAssignments.Add(deletedHostIp);

                    context.HostIpAssignments.Remove(hostIp);
                    await context.SaveChangesAsync();

                    await transaction.CommitAsync();

                    TempData["SuccessMessage"] = $"Host IP {ip} was deleted successfully.";
                    return RedirectToAction(nameof(Index), new { subnetId });
                }
                catch (Exception ex)
                {

                    logger.LogError(ex, "Host IP delete failed");
                    await TransactionCleanup.RollbackQuietlyAsync(transaction, logger);
                    TempData["ErrorMessage"] = "Error deleting host IP. Details have been logged.";
                    return RedirectToAction(nameof(Delete), new { ip });
                }
            });
        }
        catch (TimeoutException)
        {
            TempData["ErrorMessage"] = "The operation timed out due to high concurrency. Please try again.";
            return RedirectToAction(nameof(Delete), new { ip });
        }
    }

    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> AllHostIps(int page = 1)
    {

        page = Math.Max(1, page);
        int pageSize = 50;

        List<Subnet> allSubnetsWithHostIps = await context.Subnets
            .Include(s => s.HostIpAssignments)
            .Where(s => s.HostIpAssignments.Count > 0)
            .ToListAsync();

        List<(HostIpAssignment HostIp, Subnet Subnet)> allHostIps = [];
        foreach (Subnet? subnet in allSubnetsWithHostIps)
        {
            foreach (HostIpAssignment hostIp in subnet.HostIpAssignments)
            {
                allHostIps.Add((hostIp, subnet));
            }
        }

        List<(HostIpAssignment HostIp, Subnet Subnet)> orderedHostIps = [.. allHostIps
            .OrderBy(h => h.Subnet.Name)
            .ThenBy(h => IPAddress.Parse(h.HostIp.IP).GetAddressBytes()[0])
            .ThenBy(h => IPAddress.Parse(h.HostIp.IP).GetAddressBytes()[1])
            .ThenBy(h => IPAddress.Parse(h.HostIp.IP).GetAddressBytes()[2])
            .ThenBy(h => IPAddress.Parse(h.HostIp.IP).GetAddressBytes()[3])];

        int totalCount = orderedHostIps.Count;

        int totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));
        page = Math.Clamp(page, 1, totalPages);

        List<AllHostIpItemViewModel> pagedHostIps = [.. orderedHostIps
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(h => new AllHostIpItemViewModel
            {
                IP = h.HostIp.IP,
                Name = h.HostIp.Name,
                SubnetId = h.Subnet.Id,
                SubnetName = h.Subnet.Name,
                NetworkAddress = h.Subnet.NetworkAddress,
                Cidr = h.Subnet.Cidr,
                CreatedAt = h.HostIp.CreatedAt,
                CreatedBy = h.HostIp.CreatedBy,
                LastModifiedAt = h.HostIp.LastModifiedAt,
                ModifiedBy = h.HostIp.ModifiedBy
            })];

        AllHostIpsViewModel viewModel = new()
        {
            HostIps = pagedHostIps,
            TotalCount = totalCount,
            CurrentPage = page,
            PageSize = pageSize
        };

        return View(viewModel);
    }

    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> AllDeletedHostIps(int page = 1)
    {

        page = Math.Max(1, page);
        int pageSize = 50;

        List<DeletedHostIpAssignment> deletedHostIps = await context.DeletedHostIpAssignments
            .OrderByDescending(h => h.DeletedAt)
            .ToListAsync();

        int totalCount = deletedHostIps.Count;

        int totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));
        page = Math.Clamp(page, 1, totalPages);

        List<Subnet> allSubnets = await context.Subnets.ToListAsync();
        List<DeletedSubnet> allDeletedSubnets = await context.DeletedSubnets.ToListAsync();

        List<DeletedHostIpAssignment> pagedDeletedHostIps = [.. deletedHostIps
            .Skip((page - 1) * pageSize)
            .Take(pageSize)];

        List<AllDeletedHostIpItemViewModel> viewModels = [];

        foreach (DeletedHostIpAssignment? deletedHostIp in pagedDeletedHostIps)
        {
            AllDeletedHostIpItemViewModel viewModel = new()
            {
                Id = deletedHostIp.Id,
                OriginalIP = deletedHostIp.OriginalIP,
                Name = deletedHostIp.Name,
                OriginalSubnetId = deletedHostIp.OriginalSubnetId,
                CreatedAt = deletedHostIp.CreatedAt,
                CreatedBy = deletedHostIp.CreatedBy,
                LastModifiedAt = deletedHostIp.LastModifiedAt,
                ModifiedBy = deletedHostIp.ModifiedBy,
                DeletedAt = deletedHostIp.DeletedAt,
                DeletedBy = deletedHostIp.DeletedBy
            };

            Subnet? subnet = allSubnets.FirstOrDefault(s => s.Id == deletedHostIp.OriginalSubnetId);
            if (subnet != null)
            {

                viewModel.SubnetName = subnet.Name;
            }
            else
            {

                DeletedSubnet? deletedSubnet = allDeletedSubnets.FirstOrDefault(s => s.OriginalId == deletedHostIp.OriginalSubnetId);
                if (deletedSubnet != null)
                {
                    viewModel.SubnetName = $"{deletedSubnet.Name} (deleted)";
                }
                else
                {

                    viewModel.SubnetName = "Unknown";
                }
            }

            viewModels.Add(viewModel);
        }

        AllDeletedHostIpsViewModel allDeletedHostIpsViewModel = new()
        {
            DeletedHostIps = viewModels,
            TotalCount = totalCount,
            CurrentPage = page,
            PageSize = pageSize
        };

        return View(allDeletedHostIpsViewModel);
    }

    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> PurgeAllDeletedHostIps()
    {

        int maxId = await context.DeletedHostIpAssignments.MaxAsync(d => (int?)d.Id) ?? 0;
        int count = await context.DeletedHostIpAssignments.CountAsync(d => d.Id <= maxId);
        if (count == 0)
        {
            TempData["ErrorMessage"] = "There are no deleted host IP records to purge.";
            return RedirectToAction(nameof(AllDeletedHostIps));
        }

        return View(new PurgeAllDeletedHostIpsViewModel { Count = count, MaxId = maxId });
    }

    [HttpPost, ActionName("PurgeAllDeletedHostIps")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> PurgeAllDeletedHostIpsConfirmed(string confirmation, int? confirmedMaxId)
    {
        if (confirmation != "approved")
        {
            TempData["ErrorMessage"] = "You must type 'approved' to confirm purge.";
            return RedirectToAction(nameof(PurgeAllDeletedHostIps));
        }

        if (confirmedMaxId is null or <= 0)
        {
            TempData["ErrorMessage"] =
                "The purge scope was missing from the form. Review the archive and confirm again.";
            return RedirectToAction(nameof(PurgeAllDeletedHostIps));
        }

        int removed = await context.DeletedHostIpAssignments
            .Where(d => d.Id <= confirmedMaxId)
            .ExecuteDeleteAsync();

        TempData["SuccessMessage"] = $"Permanently purged {removed} deleted host IP record(s).";
        return RedirectToAction(nameof(AllDeletedHostIps));
    }

    [Authorize(Policy = "RequireViewRole")]
    public async Task<IActionResult> DeletedHostIps(int subnetId)
    {
        Subnet? subnet = await context.Subnets.FindAsync(subnetId);
        if (subnet == null)
        {
            return NotFound();
        }

        List<DeletedHostIpAssignment> deletedHostIps = await context.DeletedHostIpAssignments
            .Where(h => h.OriginalSubnetId == subnetId)
            .OrderByDescending(h => h.DeletedAt)
            .ToListAsync();

        List<DeletedHostIpViewModel> viewModels = [.. deletedHostIps.Select(d => new DeletedHostIpViewModel
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
        })];

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

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> SetAllocationStatus(SubnetAllocationDto dto)
    {
        if (!ModelState.IsValid)
        {
            return RedirectToAction("Details", "Subnet", new { id = dto.SubnetId });
        }

        try
        {

            return await subnetLockingService.ExecuteWithSubnetLockAsync<IActionResult>(async () =>
            {

                Subnet? subnet = await context.Subnets
                    .Include(s => s.ChildSubnets)
                    .Include(s => s.HostIpAssignments)
                    .FirstOrDefaultAsync(s => s.Id == dto.SubnetId);

                if (subnet == null)
                {
                    return NotFound();
                }

                if (dto.IsFullyAllocated)
                {
                    ValidationResult validationResult = hostIpValidationService.ValidateSubnetCanBeFullyAllocated(dto.SubnetId);
                    if (!validationResult.IsValid)
                    {
                        foreach (ValidationError error in validationResult.Errors)
                        {
                            TempData["ErrorMessage"] = error.Message;
                        }

                        return RedirectToAction("Details", "Subnet", new { id = dto.SubnetId });
                    }
                }

                subnet.IsFullyAllocated = dto.IsFullyAllocated;

                if (!dto.IsFullyAllocated && !string.IsNullOrEmpty(subnet.Description))
                {
                    string stripped = FullyAllocatedNote.Strip(subnet.Description);
                    subnet.Description = string.IsNullOrEmpty(stripped) ? null : stripped;
                }

                subnet.LastModifiedAt = DateTime.UtcNow;
                subnet.ModifiedBy = userContextService.GetCurrentUsername();

                context.Update(subnet);
                await context.SaveChangesAsync();

                string statusMessage = dto.IsFullyAllocated
                    ? $"Subnet '{subnet.Name}' was marked as fully allocated."
                    : $"Subnet '{subnet.Name}' was marked as not fully allocated.";

                TempData["SuccessMessage"] = statusMessage;
                return RedirectToAction("Details", "Subnet", new { id = dto.SubnetId });
            });
        }
        catch (TimeoutException)
        {
            TempData["ErrorMessage"] = "The operation timed out due to high concurrency. Please try again.";
            return RedirectToAction("Details", "Subnet", new { id = dto.SubnetId });
        }
        catch (Exception ex) when (SqlSaveOutcome.IsIndeterminate(ex))
        {

            logger.LogError(ex, "Set allocation status outcome unknown for subnet {SubnetId}", dto.SubnetId);
            TempData["ErrorMessage"] =
                "BASTET could not confirm whether the allocation status was changed. "
                + "Reload the subnet to see its current state before retrying.";
            return RedirectToAction("Details", "Subnet", new { id = dto.SubnetId });
        }
        catch (Exception ex)
        {

            logger.LogError(ex, "Set allocation status failed for subnet {SubnetId}", dto.SubnetId);
            TempData["ErrorMessage"] = "Error updating allocation status. Details have been logged.";
            return RedirectToAction("Details", "Subnet", new { id = dto.SubnetId });
        }
    }

    private bool HostIpExists(string ip) =>
        context.HostIpAssignments.Any(e => e.IP == ip);
}
