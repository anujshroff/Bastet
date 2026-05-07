using Bastet.Data;
using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Controllers
{
    [Authorize(Policy = "RequireAdminRole")]
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

            // Check if subnet has no children or host IPs and is not fully allocated
            if (subnet.ChildSubnets.Count != 0 || subnet.HostIpAssignments.Count != 0 || subnet.IsFullyAllocated)
            {
                TempData["ErrorMessage"] = subnet.IsFullyAllocated
                    ? "Subnet must not be marked as fully allocated"
                    : "Subnet must not have any child subnets or host IP assignments";
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

        // Removed ImportSubnets action - we now submit directly to SubnetController.BatchCreate

        // -------------------------------------------------------------------
        // Bulk Azure Import endpoints
        // -------------------------------------------------------------------

        // GET: /Azure/BulkImport — landing page; user picks subscription and selects VNets/subnets via AJAX
        public async Task<IActionResult> BulkImport()
        {
            if (!IsAzureImportEnabled())
            {
                return RedirectToAction("HttpStatusCodeHandler", "Error", new
                {
                    statusCode = 403,
                    errorMessage = "Azure Import feature is not enabled"
                });
            }

            BulkImportInitialViewModel viewModel = new() { IsFeatureEnabled = true };

            // Initial connectivity check (mirrors the single-import flow)
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

        // AJAX: Get every IPv4 VNet+subnet in the chosen subscription
        [HttpGet]
        public async Task<IActionResult> BulkGetVNets(string subscriptionId)
        {
            if (!IsAzureImportEnabled())
            {
                return Json(new { success = false, error = "Azure Import feature is not enabled" });
            }

            if (string.IsNullOrWhiteSpace(subscriptionId))
            {
                return Json(new { success = false, error = "Subscription ID is required" });
            }

            try
            {
                List<BulkAzureVNetViewModel> vnets = await azureService.GetAllVNetsWithSubnets(subscriptionId);
                return Json(new { success = true, vnets });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // AJAX: Build a plan from a selection. Plan includes any conflict errors.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkImportPreview(
            [FromBody] BulkImportSelectionDto selection,
            [FromServices] IAzureBulkImportPlanner planner)
        {
            if (!IsAzureImportEnabled())
            {
                return Json(new { success = false, error = "Azure Import feature is not enabled" });
            }

            if (selection is null)
            {
                return Json(new { success = false, error = "No selection was provided." });
            }

            try
            {
                IReadOnlyList<ExistingSubnetSnapshot> existing = await BuildExistingSnapshotAsync();
                BulkImportPlanViewModel plan = planner.BuildPlan(selection, existing);
                return Json(new { success = true, plan });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Loads every Bastet subnet (with the booleans the planner needs) into a snapshot list,
        /// avoiding the planner having to know about EF.
        /// </summary>
        private async Task<IReadOnlyList<ExistingSubnetSnapshot>> BuildExistingSnapshotAsync()
        {
            // Pull ids and counts in a single query rather than including children/host IPs which would be expensive
            // for large trees. We only need flags (any-children / any-host-ips), not the entities themselves.
            List<Subnet> all = await context.Subnets
                .AsNoTracking()
                .ToListAsync();

            // Need parent-child counts too. Compute via the in-memory list since we already loaded it.
            HashSet<int> parentsWithChildren = [.. all.Where(s => s.ParentSubnetId.HasValue).Select(s => s.ParentSubnetId!.Value).Distinct()];

            // Host IP counts: need a small DB query grouped by SubnetId.
            HashSet<int> subnetsWithHostIps = await context.HostIpAssignments
                .AsNoTracking()
                .Select(h => h.SubnetId)
                .Distinct()
                .ToHashSetAsync();

            return [.. all.Select(s => new ExistingSubnetSnapshot
            {
                Id = s.Id,
                Name = s.Name,
                NetworkAddress = s.NetworkAddress,
                Cidr = s.Cidr,
                HasChildSubnets = parentsWithChildren.Contains(s.Id),
                HasHostIpAssignments = subnetsWithHostIps.Contains(s.Id),
                IsFullyAllocated = s.IsFullyAllocated
            })];
        }

        // Helper method to check Azure Import environment variable
        internal static bool IsAzureImportEnabled() => bool.TryParse(
                Environment.GetEnvironmentVariable("BASTET_AZURE_IMPORT"),
                out bool result) && result;
    }
}
