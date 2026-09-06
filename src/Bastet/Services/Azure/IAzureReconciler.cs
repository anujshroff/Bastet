using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{

    public interface IAzureReconciler
    {

        AzureReconcilePlanViewModel BuildPlan(
            string subscriptionId,
            AzureVNetInventory inventory,
            IReadOnlyList<AzureLinkedSubnetSnapshot> linkedSubnets);

        void ApplyConfirmations(
            AzureReconcilePlanViewModel plan,
            IReadOnlyDictionary<string, AzureResourceConfirmation> confirmations);
    }
}
