using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;

namespace Bastet.Tests.TestHelpers;

/// <summary>
/// Builds the approved-verdict snapshot the reconcile delete now requires, the same way the wizard
/// does: scan first, then approve exactly the verdicts that scan showed.
/// </summary>
/// <remarks>
/// Deliberately derived from a real plan rather than hand-written. A test that hardcodes the status
/// it expects would keep passing if the reconciler started reporting something else, which is the
/// very drift the check exists to catch.
/// </remarks>
public static class AzureReconcileApproval
{
    public static async Task<List<AzureReconcileApprovedVerdict>> ForAsync(
        IAzureService azureService,
        IAzureSubnetSnapshotService snapshotService,
        string subscriptionId,
        IEnumerable<int> subnetIds)
    {
        AzureVNetInventory inventory = await azureService.GetVNetInventory(subscriptionId);
        IReadOnlyList<AzureLinkedSubnetSnapshot> linked = await snapshotService.GetAzureLinkedSubnetsAsync();
        AzureReconcilePlanViewModel plan = new AzureReconciler(new IpUtilityService()).BuildPlan(subscriptionId, null, inventory, linked, []);

        HashSet<int> wanted = [.. subnetIds];

        return
        [
            .. plan.Items
                .Where(i => wanted.Contains(i.SubnetId))
                .Select(i => new AzureReconcileApprovedVerdict
                {
                    SubnetId = i.SubnetId,
                    StatusName = i.StatusName,
                    Reason = i.Reason
                })
        ];
    }
}
