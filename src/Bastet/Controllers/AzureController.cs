using Bastet.Models.ViewModels;
using Bastet.Services.Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers
{
    [Authorize(Policy = "RequireAdminRole")]
    public class AzureController(
        IAzureService azureService,
        IAzureSubnetSnapshotService snapshotService,
        ILogger<AzureController> logger) : Controller
    {

        [HttpGet]
        public async Task<IActionResult> GetSubscriptions()
        {

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

        public async Task<IActionResult> BulkImport()
        {
            if (!IsAzureImportEnabled())
            {
                return this.RedirectToErrorPage(403, "Azure Import feature is not enabled");
            }

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

            return View();
        }

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

                AzureVNetInventory inventory = await azureService.GetVNetInventory(subscriptionId);
                if (!inventory.Success)
                {
                    return Json(new { success = false, error = inventory.ErrorMessage });
                }

                List<BulkAzureVNetViewModel> vnets = [.. inventory.VNets.Where(v => v.Ipv4AddressPrefixes.Count > 0)];

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

        public async Task<IActionResult> Reconcile()
        {
            if (!IsAzureImportEnabled())
            {
                return this.RedirectToErrorPage(403, "Azure Import feature is not enabled");
            }

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

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReconcileScan(
            string subscriptionId,
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

                AzureReconcilePlanViewModel plan = reconciler.BuildPlan(subscriptionId, inventory, linked);

                await ConfirmProposedDeletionsAsync(plan, azureService, reconciler);

                return Json(new { success = true, plan });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reconcile scan failed");
                return Json(new { success = false, error = "The reconcile scan failed. Details have been logged." });
            }
        }

        internal static async Task ConfirmProposedDeletionsAsync(
            AzureReconcilePlanViewModel plan,
            IAzureService azureService,
            IAzureReconciler reconciler)
        {
            string[] absenceClaims = [.. plan.Items
                .Where(i => AzureReconciler.IsAbsenceStatus(i.Status))
                .Select(i => i.AzureResourceId)];

            IReadOnlyDictionary<string, AzureResourceConfirmation> confirmations =
                absenceClaims.Length == 0
                    ? new Dictionary<string, AzureResourceConfirmation>(StringComparer.OrdinalIgnoreCase)
                    : await azureService.ConfirmResourcesAsync(absenceClaims);

            reconciler.ApplyConfirmations(plan, confirmations);
        }

        internal static bool IsAzureImportEnabled() => bool.TryParse(
                Environment.GetEnvironmentVariable("BASTET_AZURE_IMPORT"),
                out bool result) && result;
    }
}
