using Bastet.Data;
using Bastet.Models.ViewModels;
using Bastet.Services.Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AzureController(
        BastetDbContext context,
        IAzureService azureService) : Controller
    {

        // GET: Azure/Import/{id}
        public async Task<IActionResult> Import(int id)
        {
            // Check environment variable
            if (!IsAzureImportEnabled())
            {
                return RedirectToAction("HttpStatusCodeHandler", "Error", new
                {
                    statusCode = 403,
                    errorMessage = "Azure Import feature is not enabled"
                });
            }

            // Get the subnet
            Models.Subnet? subnet = await context.Subnets
                .Include(s => s.ChildSubnets)
                .Include(s => s.HostIpAssignments)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (subnet == null)
            {
                return RedirectToAction("HttpStatusCodeHandler", "Error", new
                {
                    statusCode = 404,
                    errorMessage = $"Subnet with ID {id} could not be found."
                });
            }

            // Check if subnet has no children or host IPs
            if (subnet.ChildSubnets.Count != 0 || subnet.HostIpAssignments.Count != 0)
            {
                TempData["ErrorMessage"] = "Subnet must not have any child subnets or host IP assignments";
                return RedirectToAction("Details", "Subnet", new { id });
            }

            // Create initial view model
            AzureImportViewModel viewModel = new()
            {
                SubnetId = subnet.Id,
                SubnetName = subnet.Name,
                NetworkAddress = subnet.NetworkAddress,
                Cidr = subnet.Cidr
            };

            // Initial connectivity check
            try
            {
                if (!await azureService.IsCredentialValid())
                {
                    ModelState.AddModelError("", "Failed to authenticate with Azure. Please check your credentials.");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error connecting to Azure: {ex.Message}");
            }

            return View(viewModel);
        }

        // AJAX: Get Azure Subscriptions
        [HttpGet]
        public async Task<IActionResult> GetSubscriptions()
        {
            // Check environment variable
            if (!IsAzureImportEnabled())
            {
                return Json(new { success = false, error = "Azure Import feature is not enabled" });
            }

            try
            {
                List<AzureSubscriptionViewModel> subscriptions = await azureService.GetSubscriptions();
                return Json(new { success = true, subscriptions });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // AJAX: Get Azure VNets for a subscription
        [HttpGet]
        public async Task<IActionResult> GetVNets(string subscriptionId, int subnetId)
        {
            // Check environment variable
            if (!IsAzureImportEnabled())
            {
                return Json(new { success = false, error = "Azure Import feature is not enabled" });
            }

            // Get the subnet
            Models.Subnet? subnet = await context.Subnets.FindAsync(subnetId);
            if (subnet == null)
            {
                return Json(new { success = false, error = "Subnet not found" });
            }

            try
            {
                List<AzureVNetViewModel> vnets = await azureService.GetCompatibleVNets(
                    subscriptionId, subnet.NetworkAddress, subnet.Cidr);

                return vnets.Count == 0
                    ? Json(new
                    {
                        success = true,
                        vnets,
                        message = "No matching VNets found in this subscription"
                    })
                    : (IActionResult)Json(new { success = true, vnets });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // AJAX: Get Azure Subnets for a VNet
        [HttpGet]
        public async Task<IActionResult> GetSubnets(string vnetResourceId, int subnetId)
        {
            // Check environment variable
            if (!IsAzureImportEnabled())
            {
                return Json(new { success = false, error = "Azure Import feature is not enabled" });
            }

            // Get the subnet
            Models.Subnet? subnet = await context.Subnets.FindAsync(subnetId);
            if (subnet == null)
            {
                return Json(new { success = false, error = "Subnet not found" });
            }

            try
            {
                List<AzureSubnetViewModel> azureSubnets = await azureService.GetCompatibleSubnets(
                    vnetResourceId, subnet.NetworkAddress, subnet.Cidr);

                return azureSubnets.Count == 0
                    ? Json(new
                    {
                        success = true,
                        subnets = azureSubnets,
                        message = "No compatible subnets found in this VNet"
                    })
                    : (IActionResult)Json(new { success = true, subnets = azureSubnets });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // POST: Azure/ImportSubnets
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportSubnets(
            int subnetId,
            string vnetName,
            List<string> selectedSubnetIds,
            List<string> selectedSubnetNames,
            List<string> selectedSubnetPrefixes)
        {
            // Check environment variable
            if (!IsAzureImportEnabled())
            {
                return RedirectToAction("HttpStatusCodeHandler", "Error", new
                {
                    statusCode = 403,
                    errorMessage = "Azure Import feature is not enabled"
                });
            }

            if (selectedSubnetIds == null || selectedSubnetIds.Count == 0 ||
                selectedSubnetNames == null || selectedSubnetNames.Count == 0 ||
                selectedSubnetPrefixes == null || selectedSubnetPrefixes.Count == 0 ||
                selectedSubnetIds.Count != selectedSubnetNames.Count ||
                selectedSubnetIds.Count != selectedSubnetPrefixes.Count)
            {
                TempData["ErrorMessage"] = "No subnets were selected for import or invalid data was provided.";
                return RedirectToAction("Import", new { id = subnetId });
            }

            try
            {
                // Convert to CreateSubnetViewModels
                List<CreateSubnetViewModel> subnetsToCreate = [];

                for (int i = 0 ; i < selectedSubnetIds.Count ; i++)
                {
                    // Parse CIDR notation to get network and prefix
                    string addressPrefix = selectedSubnetPrefixes[i];
                    string[] parts = addressPrefix.Split('/');

                    if (parts.Length != 2 || !int.TryParse(parts[1], out int cidr))
                    {
                        continue; // Skip invalid prefixes
                    }

                    subnetsToCreate.Add(new CreateSubnetViewModel
                    {
                        Name = selectedSubnetNames[i],
                        NetworkAddress = parts[0],
                        Cidr = cidr,
                        Description = $"Imported from Azure VNet: {vnetName}",
                        Tags = "Azure",
                        ParentSubnetId = subnetId
                    });
                }

                if (subnetsToCreate.Count == 0)
                {
                    TempData["ErrorMessage"] = "No valid subnets were selected for import.";
                    return RedirectToAction("Import", new { id = subnetId });
                }

                // Call SubnetController.BatchCreate
                // Get SubnetController instance from DI
                SubnetController subnetController = (SubnetController)HttpContext.RequestServices
                    .GetRequiredService<SubnetController>();

                // Configure the controller context
                subnetController.ControllerContext = new ControllerContext()
                {
                    HttpContext = HttpContext
                };

                IActionResult result = await subnetController.BatchCreate(subnetId, subnetsToCreate);

                if (result is OkObjectResult okResult)
                {
                    TempData["SuccessMessage"] = $"Successfully imported {subnetsToCreate.Count} subnets from Azure.";
                    return RedirectToAction("Details", "Subnet", new { id = subnetId });
                }

                // Handle errors
                TempData["ErrorMessage"] = result is BadRequestObjectResult badResult
                    ? "Failed to import subnets: validation errors occurred."
                    : (object)"An error occurred during subnet import.";

                return RedirectToAction("Import", new { id = subnetId });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error importing subnets: {ex.Message}";
                return RedirectToAction("Import", new { id = subnetId });
            }
        }

        // Helper method to check Azure Import environment variable
        private static bool IsAzureImportEnabled() => bool.TryParse(
                Environment.GetEnvironmentVariable("BASTET_AZURE_IMPORT"),
                out bool result) && result;
    }
}
