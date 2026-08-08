using Bastet.Data;
using Bastet.Models.ViewModels;
using Bastet.Services;
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
        IIpUtilityService ipUtilityService,
        ILogger<AzureController> logger) : Controller
    {

        public async Task<IActionResult> Import(int id)
        {

            if (!IsAzureImportEnabled())
            {
                return this.RedirectToErrorPage(403, "Azure Import feature is not enabled");
            }

            Models.Subnet? subnet = await context.Subnets
                .Include(s => s.ChildSubnets)
                .Include(s => s.HostIpAssignments)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (subnet == null)
            {
                return this.RedirectToErrorPage(404, $"Subnet with ID {id} could not be found.");
            }

            bool isTopUp = subnet.ChildSubnets.Count != 0 && !string.IsNullOrEmpty(subnet.AzureResourceId);

            if ((subnet.ChildSubnets.Count != 0 && !isTopUp) || subnet.HostIpAssignments.Count != 0 || subnet.IsFullyAllocated)
            {
                TempData["ErrorMessage"] = subnet.IsFullyAllocated
                    ? "Subnet must not be marked as fully allocated"
                    : subnet.HostIpAssignments.Count != 0
                        ? "Subnet must not have any host IP assignments"
                        : "Subnet already has child subnets and is not linked to an Azure VNet";
                return RedirectToAction("Details", "Subnet", new { id });
            }

            AzureImportViewModel viewModel = new()
            {
                SubnetId = subnet.Id,
                SubnetName = subnet.Name,
                NetworkAddress = subnet.NetworkAddress,
                Cidr = subnet.Cidr
            };

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

        [HttpGet]
        public async Task<IActionResult> GetVNets(string subscriptionId, int subnetId)
        {

            if (!IsAzureImportEnabled())
            {
                return Json(new { success = false, error = "Azure Import feature is not enabled" });
            }

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

        [HttpGet]
        public async Task<IActionResult> GetSubnets(string vnetResourceId, int subnetId)
        {

            if (!IsAzureImportEnabled())
            {
                return Json(new { success = false, error = "Azure Import feature is not enabled" });
            }

            Models.Subnet? subnet = await context.Subnets.FindAsync(subnetId);
            if (subnet == null)
            {
                return Json(new { success = false, error = "Subnet not found" });
            }

            try
            {
                List<AzureSubnetViewModel> azureSubnets = await azureService.GetCompatibleSubnets(
                    vnetResourceId, subnet.NetworkAddress, subnet.Cidr);

                List<ExistingSubnetSnapshot> existing = await context.Subnets
                    .AsNoTracking()
                    .Select(s => new ExistingSubnetSnapshot
                    {
                        Id = s.Id,
                        Name = s.Name,
                        NetworkAddress = s.NetworkAddress,
                        Cidr = s.Cidr,
                        AzureResourceId = s.AzureResourceId
                    })
                    .ToListAsync();

                foreach (AzureSubnetViewModel azureSubnet in azureSubnets)
                {
                    AnnotateImportCandidate(azureSubnet, subnet, existing);
                }

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

        private void AnnotateImportCandidate(
            AzureSubnetViewModel azureSubnet,
            Models.Subnet target,
            List<ExistingSubnetSnapshot> existing)
        {
            string[] parts = (azureSubnet.AddressPrefix ?? string.Empty).Split('/');

            if (parts.Length != 2 || !int.TryParse(parts[1], out int cidr))
            {
                return;
            }

            string network = parts[0];

            if (azureSubnet.FullyEncompassesVNetPrefix)
            {
                if (existing.Exists(e => e.Id != target.Id
                                         && ipUtilityService.IsSubnetContainedInParent(
                                             e.NetworkAddress, e.Cidr, target.NetworkAddress, target.Cidr)))
                {
                    Block(azureSubnet,
                        $"Covers the whole VNet prefix, which would mark Bastet subnet '{target.Name}' "
                        + "fully allocated, but it already has child subnets.");
                }

                return;
            }

            ExistingSubnetSnapshot? exact = existing.Find(e =>
                string.Equals(e.NetworkAddress, network, StringComparison.OrdinalIgnoreCase)
                && e.Cidr == cidr);

            if (exact is not null)
            {
                bool sameAzureResource = !string.IsNullOrEmpty(exact.AzureResourceId)
                    && string.Equals(exact.AzureResourceId, azureSubnet.ResourceId, StringComparison.OrdinalIgnoreCase);

                azureSubnet.Status = sameAzureResource
                    ? BulkImportAvailability.AlreadyImported
                    : BulkImportAvailability.Blocked;
                azureSubnet.Reason = sameAzureResource
                    ? $"Already imported as Bastet subnet '{exact.Name}'."
                    : $"Bastet subnet '{exact.Name}' already uses {azureSubnet.AddressPrefix}.";
                azureSubnet.IsSelectable = false;
                return;
            }

            ExistingSubnetSnapshot? wouldContain = existing.Find(e =>
                ipUtilityService.IsSubnetContainedInParent(e.NetworkAddress, e.Cidr, network, cidr));

            if (wouldContain is not null)
            {
                Block(azureSubnet,
                    $"Would contain existing Bastet subnet '{wouldContain.Name}' "
                    + $"({wouldContain.NetworkAddress}/{wouldContain.Cidr}), which would create an invalid hierarchy.");
                return;
            }

            ExistingSubnetSnapshot? moreSpecificParent = existing.Find(e =>
                ipUtilityService.IsSubnetContainedInParent(network, cidr, e.NetworkAddress, e.Cidr)
                && ipUtilityService.IsSubnetContainedInParent(
                    e.NetworkAddress, e.Cidr, target.NetworkAddress, target.Cidr));

            if (moreSpecificParent is not null)
            {
                Block(azureSubnet,
                    $"A more specific Bastet parent subnet exists: '{moreSpecificParent.Name}' "
                    + $"({moreSpecificParent.NetworkAddress}/{moreSpecificParent.Cidr}), "
                    + "so this subnet cannot be imported into this target.");
            }
        }

        private static void Block(AzureSubnetViewModel azureSubnet, string reason)
        {
            azureSubnet.Status = BulkImportAvailability.Blocked;
            azureSubnet.Reason = reason;
            azureSubnet.IsSelectable = false;
        }

        public async Task<IActionResult> BulkImport()
        {
            if (!IsAzureImportEnabled())
            {
                return this.RedirectToErrorPage(403, "Azure Import feature is not enabled");
            }

            BulkImportInitialViewModel viewModel = new() { IsFeatureEnabled = true };

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

                List<BulkAzureVNetViewModel> vnets = inventory.VNets;

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

            AzureReconcileInitialViewModel viewModel = new() { IsFeatureEnabled = true };

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

                IReadOnlyList<ExistingSubnetSnapshot> existing = await snapshotService.GetExistingSubnetsAsync();
                AzureReconcilePlanViewModel plan = reconciler.BuildPlan(subscriptionId, subscriptionName, inventory, linked, existing);

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
