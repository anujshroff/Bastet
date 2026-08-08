using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{

    public interface IAzureReconciler
    {

        AzureReconcilePlanViewModel BuildPlan(
            string subscriptionId,
            string? subscriptionName,
            AzureVNetInventory inventory,
            IReadOnlyList<AzureLinkedSubnetSnapshot> linkedSubnets,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets);

        void ApplyConfirmations(
            AzureReconcilePlanViewModel plan,
            IReadOnlyDictionary<string, AzureResourceConfirmation> confirmations);
    }
}
