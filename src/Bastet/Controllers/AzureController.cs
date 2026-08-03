using Bastet.Data;
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
        IAzureService azureService,
        IAzureSubnetSnapshotService snapshotService,
        ILogger<AzureController> logger) : Controller
    {

        // GET: Azure/Import/{id}
        public async Task<IActionResult> Import(int id)
        {
            // Check environment variable
            if (!IsAzureImportEnabled())
            {
                return this.RedirectToErrorPage(403, "Azure Import feature is not enabled");
            }

            // Get the subnet
            Models.Subnet? subnet = await context.Subnets
                .Include(s => s.ChildSubnets)
                .Include(s => s.HostIpAssignments)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (subnet == null)
            {
                return this.RedirectToErrorPage(404, $"Subnet with ID {id} could not be found.");
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
                logger.LogError(ex, "Azure connectivity check failed on the Import page");
                ModelState.AddModelError("", "Error connecting to Azure. Details have been logged.");
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
                logger.LogError(ex, "Failed to load Azure subscriptions");
                return Json(new { success = false, error = "Failed to load subscriptions from Azure. Details have been logged." });
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
                logger.LogError(ex, "Failed to load compatible VNets for subnet {SubnetId}", subnetId);
                return Json(new { success = false, error = "Failed to load VNets from Azure. Details have been logged." });
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
                logger.LogError(ex, "Failed to load compatible Azure subnets for subnet {SubnetId}", subnetId);
                return Json(new { success = false, error = "Failed to load subnets from Azure. Details have been logged." });
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
                return this.RedirectToErrorPage(403, "Azure Import feature is not enabled");
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
                logger.LogError(ex, "Azure connectivity check failed on the Bulk Import page");
                ModelState.AddModelError("", "Error connecting to Azure. Details have been logged.");
            }

            return View(viewModel);
        }

        /// <summary>
        /// AJAX: every IPv4 VNet+subnet in the chosen subscription, annotated with what Bastet
        /// already has so the selection UI can grey out anything that cannot be imported.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> BulkGetVNets(
            string subscriptionId,
            [FromServices] IAzureBulkImportPlanner planner)
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
                // A failed Azure read must be reported as a failure, not an empty subscription -
                // otherwise the wizard renders "nothing to import" over a credential or throttling
                // error. Same fail-loud treatment as ReconcileScan.
                AzureVNetInventory inventory = await azureService.GetVNetInventory(subscriptionId);
                if (!inventory.Success)
                {
                    return Json(new { success = false, error = inventory.ErrorMessage });
                }

                List<BulkAzureVNetViewModel> vnets = inventory.VNets;

                // Annotate with the planner's own rules, so what the UI lets you select and what the
                // planner will accept cannot drift apart.
                IReadOnlyList<ExistingSubnetSnapshot> existing = await snapshotService.GetExistingSubnetsAsync();
                planner.AnnotateAvailability(vnets, existing);

                return Json(new { success = true, vnets });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load the subscription's VNets for bulk import");
                return Json(new { success = false, error = "Failed to load VNets from Azure. Details have been logged." });
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
                IReadOnlyList<ExistingSubnetSnapshot> existing = await snapshotService.GetExistingSubnetsAsync();
                BulkImportPlanViewModel plan = planner.BuildPlan(selection, existing);
                return Json(new { success = true, plan });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to build the bulk import preview plan");
                return Json(new { success = false, error = "Failed to build the import preview. Details have been logged." });
            }
        }

        // -------------------------------------------------------------------
        // Azure Reconcile — find Bastet subnets whose Azure resources are gone
        // -------------------------------------------------------------------

        // GET: /Azure/Reconcile — landing page; user picks a subscription and scans it via AJAX
        public async Task<IActionResult> Reconcile()
        {
            if (!IsAzureImportEnabled())
            {
                return this.RedirectToErrorPage(403, "Azure Import feature is not enabled");
            }

            AzureReconcileInitialViewModel viewModel = new() { IsFeatureEnabled = true };

            // Initial connectivity check (mirrors the import flows)
            try
            {
                if (!await azureService.IsCredentialValid())
                {
                    ModelState.AddModelError("", "Failed to authenticate with Azure. Please check your credentials.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Azure connectivity check failed on the Reconcile page");
                ModelState.AddModelError("", "Error connecting to Azure. Details have been logged.");
            }

            return View(viewModel);
        }

        /// <summary>
        /// AJAX: compare one subscription's live VNets against the Azure-linked subnets in Bastet.
        /// </summary>
        /// <remarks>
        /// Uses <see cref="IAzureService.GetVNetInventory"/> so a failed Azure call is reported as a
        /// failure instead of an empty inventory that would make every imported subnet look deleted.
        /// </remarks>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReconcileScan(
            string subscriptionId,
            string? subscriptionName,
            [FromServices] IAzureReconciler reconciler)
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
                AzureVNetInventory inventory = await azureService.GetVNetInventory(subscriptionId);
                IReadOnlyList<AzureLinkedSubnetSnapshot> linked = await snapshotService.GetAzureLinkedSubnetsAsync();
                // The whole tree, not just the linked rows: the inbound direction has to know about
                // a range someone created by hand, which carries no Azure resource ID.
                IReadOnlyList<ExistingSubnetSnapshot> existing = await snapshotService.GetExistingSubnetsAsync();
                AzureReconcilePlanViewModel plan = reconciler.BuildPlan(subscriptionId, subscriptionName, inventory, linked, existing);

                // The inventory is a list result and ARM filters those by RBAC, so "missing" is not
                // the same fact as "deleted". Read each proposed row directly before offering it for
                // archival. Only the proposed rows are checked, so a healthy scan costs nothing.
                await ConfirmProposedDeletionsAsync(plan, azureService, reconciler);

                return Json(new { success = true, plan });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reconcile scan failed");
                return Json(new { success = false, error = "The reconcile scan failed. Details have been logged." });
            }
        }

        /// <summary>
        /// Confirms every row a reconcile plan proposes for deletion by reading it from Azure
        /// directly, and drops any that Azure will not confirm is gone.
        /// </summary>
        /// <remarks>
        /// <see cref="IAzureReconciler.BuildPlan"/> can only work from a subscription listing, and
        /// ARM filters those by RBAC: a credential that has lost access to a resource group gets
        /// HTTP 200 with those resources simply absent, which is indistinguishable from deletion. A
        /// direct read tells them apart (404 versus 403). Only the rows that claim the resource is
        /// gone are read: a drift row was built from a listing that contained the resource, so
        /// reading it back can only ever answer Live and would withhold the row for no reason.
        /// Shared by the scan and the delete paths so the two cannot diverge on what is deletable.
        /// </remarks>
        internal static async Task ConfirmProposedDeletionsAsync(
            AzureReconcilePlanViewModel plan,
            IAzureService azureService,
            IAzureReconciler reconciler)
        {
            string[] absenceClaims = [.. plan.Items
                .Where(i => AzureReconciler.IsAbsenceStatus(i.Status))
                .Select(i => i.AzureResourceId)];

            // No absence claim means there is nothing to ask Azure about, so the ARM round trip is
            // skipped - but ApplyConfirmations must still run. It also applies the cascade guard that
            // protects plan.ReviewItems, and those are independent of any confirmation: a plan made
            // entirely of drift would otherwise archive a review-item descendant this same scan had
            // just verified live. Returning early here made that guard conditional on some unrelated
            // row happening to be absent.
            IReadOnlyDictionary<string, AzureResourceConfirmation> confirmations =
                absenceClaims.Length == 0
                    ? new Dictionary<string, AzureResourceConfirmation>(StringComparer.OrdinalIgnoreCase)
                    : await azureService.ConfirmResourcesAsync(absenceClaims);

            reconciler.ApplyConfirmations(plan, confirmations);
        }

        // Helper method to check Azure Import environment variable
        internal static bool IsAzureImportEnabled() => bool.TryParse(
                Environment.GetEnvironmentVariable("BASTET_AZURE_IMPORT"),
                out bool result) && result;
    }
}
