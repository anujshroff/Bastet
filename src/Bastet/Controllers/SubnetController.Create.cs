using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{

    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Create(string? networkAddress = null, int? cidr = null, int? parentId = null)
    {

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

        if (!string.IsNullOrEmpty(networkAddress))
        {
            viewModel.NetworkAddress = networkAddress;
        }

        bool hasUsableCidr = cidr is >= 0 and <= 32;

        if (hasUsableCidr)
        {
            viewModel.Cidr = cidr!.Value;

            viewModel.CalculatedSubnetMask = ipUtilityService.CalculateSubnetMask(cidr.Value);
        }

        if (parentId.HasValue)
        {
            viewModel.ParentSubnetId = parentId.Value;

            Subnet? parentSubnet = await context.Subnets.FindAsync(parentId.Value);
            if (parentSubnet != null && !string.IsNullOrEmpty(networkAddress) && hasUsableCidr)
            {

                string safeParentName = SubnetNaming.ToSafeText(parentSubnet.Name);

                viewModel.Name = string.IsNullOrEmpty(safeParentName)
                    ? $"{networkAddress}-{cidr}"
                    : SubnetNaming.WithSuffix(
                        safeParentName, $"-{networkAddress}-{cidr}", MaxSubnetNameLength);
            }
        }

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Create(CreateSubnetViewModel viewModel)
    {
        if (!ModelState.IsValid)
        {
            await LoadParentSubnets(viewModel);
            return View(viewModel);
        }

        try
        {

            Subnet? result = await subnetLockingService.ExecuteWithSubnetLockAsync(async () =>
            {

                if (await ValidateSubnetCreation(viewModel))
                {

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
        catch (Exception ex) when (SqlSaveOutcome.IsIndeterminate(ex))
        {

            logger.LogError(ex, "Subnet create outcome unknown");
            ModelState.AddModelError("",
                "BASTET could not confirm whether this subnet was created. "
                + "Check the subnet list before retrying.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Subnet create failed");
            ModelState.AddModelError("", "Error creating subnet. Details have been logged.");
        }

        await LoadParentSubnets(viewModel);
        return View(viewModel);
    }

    private async Task LoadParentSubnets(CreateSubnetViewModel viewModel) =>

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
