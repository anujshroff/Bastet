using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Data;
using Bastet.Services.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{

    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Edit(int id)
    {
        Subnet? subnet = await context.Subnets
            .Include(s => s.ParentSubnet)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subnet == null)
        {
            return this.RedirectToErrorPage(404, $"The subnet with ID {id} could not be found or may have been deleted.");
        }

        EditSubnetViewModel viewModel = new()
        {
            Id = subnet.Id,
            Name = subnet.Name,
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            OriginalCidr = subnet.Cidr,
            Description = subnet.Description,
            Tags = subnet.Tags,
            SubnetMask = ipUtilityService.CalculateSubnetMask(subnet.Cidr),
            CreatedAt = subnet.CreatedAt,
            LastModifiedAt = subnet.LastModifiedAt,
            RowVersion = subnet.RowVersion,
            IsAzureLinked = !string.IsNullOrEmpty(subnet.AzureResourceId)
        };

        if (subnet.ParentSubnet != null)
        {
            viewModel.ParentSubnetInfo = $"{subnet.ParentSubnet.Name} ({subnet.ParentSubnet.NetworkAddress}/{subnet.ParentSubnet.Cidr})";
        }

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Edit(int id, EditSubnetViewModel viewModel)
    {
        if (id != viewModel.Id)
        {
            return this.RedirectToErrorPage(404, "The ID in the URL doesn't match the ID in the form data.");
        }

        if (ModelState.IsValid)
        {
            try
            {

                Subnet result = await subnetLockingService.ExecuteWithSubnetLockAsync(async () =>
                {

                    Subnet? subnet = await context.Subnets
                        .Include(s => s.ParentSubnet)
                        .FirstOrDefaultAsync(s => s.Id == id) ?? throw new InvalidOperationException($"The subnet with ID {id} could not be found or may have been deleted.");

                    List<Subnet> childSubnets = await context.Subnets
                        .Where(s => s.ParentSubnetId == id)
                        .ToListAsync();

                    bool cidrChanged = viewModel.Cidr != subnet.Cidr;

                    if (cidrChanged && !string.IsNullOrEmpty(subnet.AzureResourceId))
                    {
                        throw new ValidationException(
                            "This subnet is linked to an Azure resource, so its CIDR cannot be changed here. " +
                            "Change the prefix in Azure and re-import, or delete the subnet and recreate it.");
                    }

                    if (viewModel.Cidr != subnet.Cidr)
                    {

                        List<Subnet> siblings = [];
                        if (subnet.ParentSubnetId.HasValue)
                        {
                            siblings = await context.Subnets
                                .Where(s => s.ParentSubnetId == subnet.ParentSubnetId && s.Id != subnet.Id)
                                .ToListAsync();
                        }

                        List<Subnet> allOtherSubnets = await context.Subnets
                            .Where(s => s.Id != subnet.Id)
                            .ToListAsync();

                        ValidationResult validationResult = subnetValidationService.ValidateSubnetCidrChange(
                            subnet.Id,
                            subnet.NetworkAddress,
                            subnet.Cidr,
                            viewModel.Cidr,
                            subnet.ParentSubnet,
                            siblings,
                            childSubnets,
                            allOtherSubnets);

                        if (!validationResult.IsValid)
                        {
                            string errorMessage = string.Join("; ", validationResult.Errors.Select(e => e.Message));
                            throw new ValidationException($"CIDR validation failed: {errorMessage}");
                        }

                        if (viewModel.Cidr != subnet.Cidr)
                        {

                            ValidationResult hostIpValidationResult = hostIpValidationService.ValidateSubnetCidrChangeWithHostIps(
                                subnet.Id,
                                subnet.NetworkAddress,
                                subnet.Cidr,
                                viewModel.Cidr);

                            if (!hostIpValidationResult.IsValid)
                            {
                                string errorMessage = string.Join("; ", hostIpValidationResult.Errors.Select(e => e.Message));
                                throw new ValidationException($"Host IP validation failed: {errorMessage}");
                            }
                        }
                    }

                    subnet.Name = viewModel.Name;
                    subnet.Description = viewModel.Description;
                    subnet.Tags = viewModel.Tags;
                    subnet.LastModifiedAt = DateTime.UtcNow;
                    subnet.ModifiedBy = userContextService.GetCurrentUsername();

                    if (cidrChanged)
                    {
                        subnet.Cidr = viewModel.Cidr;
                    }

                    context.Entry(subnet).OriginalValues["RowVersion"] = viewModel.RowVersion;

                    context.Subnets.Update(subnet);
                    await context.SaveChangesAsync();

                    return subnet;
                });

                TempData["SuccessMessage"] = $"Subnet '{result.Name}' was updated successfully.";
                return RedirectToAction(nameof(Details), new { id = result.Id });
            }
            catch (ValidationException ex)
            {

                ModelState.AddModelError("Cidr", ex.Message);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SubnetExists(id))
                {
                    return this.RedirectToErrorPage(404, "The subnet no longer exists. It may have been deleted by another user.");
                }

                Subnet? currentSubnet = await context.Subnets
                    .AsNoTracking()
                    .Include(s => s.ParentSubnet)
                    .FirstOrDefaultAsync(s => s.Id == id);

                if (currentSubnet != null)
                {

                    viewModel.RowVersion = currentSubnet.RowVersion;
                    viewModel.NetworkAddress = currentSubnet.NetworkAddress;
                    viewModel.OriginalCidr = currentSubnet.Cidr;
                    viewModel.CreatedAt = currentSubnet.CreatedAt;
                    viewModel.LastModifiedAt = currentSubnet.LastModifiedAt;

                    if (currentSubnet.ParentSubnet != null)
                    {
                        viewModel.ParentSubnetInfo = $"{currentSubnet.ParentSubnet.Name} ({currentSubnet.ParentSubnet.NetworkAddress}/{currentSubnet.ParentSubnet.Cidr})";
                    }

                    ModelState.Remove(nameof(viewModel.RowVersion));
                }

                ModelState.AddModelError("",
                    "This subnet was modified by another user while you were editing it. " +
                    "Your changes have been preserved below, but you should review the current values before saving. " +
                    "Click 'Save Changes' again to apply your updates.");
            }
            catch (TimeoutException)
            {
                ModelState.AddModelError("", "The operation timed out due to high concurrency. Please try again.");
            }
            catch (Exception ex) when (SqlSaveOutcome.IsIndeterminate(ex))
            {

                logger.LogError(ex, "Subnet edit outcome unknown for subnet {SubnetId}", id);
                ModelState.AddModelError("",
                    "BASTET could not confirm whether this change was applied. "
                    + "Reload the subnet to see its current state before retrying.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Subnet edit failed for subnet {SubnetId}", id);
                ModelState.AddModelError("", "Error updating subnet. Details have been logged.");
            }
        }

        Subnet? origSubnet = await context.Subnets
            .AsNoTracking()
            .Include(s => s.ParentSubnet)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (origSubnet == null)
        {
            return this.RedirectToErrorPage(404, $"The subnet with ID {id} could not be found or may have been deleted.");
        }

        viewModel.NetworkAddress = origSubnet.NetworkAddress;

        viewModel.IsAzureLinked = !string.IsNullOrEmpty(origSubnet.AzureResourceId);

        viewModel.OriginalCidr = origSubnet.Cidr;

        bool hasUsableCidr = viewModel.Cidr is >= 0 and <= 32;

        if (!ModelState.IsValid || viewModel.Cidr != origSubnet.Cidr)
        {
            viewModel.SubnetMask = hasUsableCidr
                ? ipUtilityService.CalculateSubnetMask(viewModel.Cidr)
                : string.Empty;
        }
        else
        {
            viewModel.Cidr = origSubnet.Cidr;
            viewModel.OriginalCidr = origSubnet.Cidr;
            viewModel.SubnetMask = ipUtilityService.CalculateSubnetMask(origSubnet.Cidr);
        }

        viewModel.CreatedAt = origSubnet.CreatedAt;
        viewModel.LastModifiedAt = origSubnet.LastModifiedAt;

        viewModel.RowVersion = origSubnet.RowVersion;

        ModelState.Remove(nameof(viewModel.RowVersion));

        if (origSubnet.ParentSubnet != null)
        {
            viewModel.ParentSubnetInfo = $"{origSubnet.ParentSubnet.Name} ({origSubnet.ParentSubnet.NetworkAddress}/{origSubnet.ParentSubnet.Cidr})";
        }

        return View(viewModel);
    }
}

public class ValidationException(string message) : Exception(message)
{
}
