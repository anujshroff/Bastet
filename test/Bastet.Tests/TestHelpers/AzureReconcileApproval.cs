using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;

namespace Bastet.Tests.TestHelpers;

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
