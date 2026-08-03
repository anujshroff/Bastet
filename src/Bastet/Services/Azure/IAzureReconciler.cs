using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{
    /// <summary>
    /// Compares the Azure-linked subnets in Bastet against what actually exists in Azure and reports
    /// what has drifted. Like <see cref="IAzureBulkImportPlanner"/> this is pure: the caller supplies
    /// both sides, so all of the decision-making is testable without EF or a live subscription.
    /// </summary>
    public interface IAzureReconciler
    {
        /// <summary>
        /// Works out which of <paramref name="linkedSubnets"/> no longer match Azure.
        /// </summary>
        /// <param name="subscriptionId">The subscription being reconciled. Subnets whose resource ID belongs to another subscription are ignored.</param>
        /// <param name="subscriptionName">Display name, for the report.</param>
        /// <param name="inventory">Live Azure state. If it reports failure, the plan comes back empty and cannot be committed.</param>
        /// <param name="linkedSubnets">Every Bastet subnet carrying an Azure resource ID.</param>
        /// <param name="existingSubnets">
        /// Every Bastet subnet, linked or not. Needed for the INBOUND direction: an Azure range that
        /// no Bastet subnet records is reported from here, and a range created by hand carries no
        /// Azure resource ID, so it is absent from <paramref name="linkedSubnets"/> while
        /// legitimately accounting for the address space.
        /// </param>
        AzureReconcilePlanViewModel BuildPlan(
            string subscriptionId,
            string? subscriptionName,
            AzureVNetInventory inventory,
            IReadOnlyList<AzureLinkedSubnetSnapshot> linkedSubnets,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets);

        /// <summary>
        /// Removes from <see cref="AzureReconcilePlanViewModel.Items"/> everything whose absence
        /// from the inventory has not been individually confirmed as a deletion, and explains why.
        /// </summary>
        /// <remarks>
        /// <see cref="BuildPlan"/> can only see what a list operation returned, and those are
        /// RBAC-filtered - so it cannot tell a deleted resource from one this credential can no
        /// longer see. It therefore proposes; this disposes. Kept pure and separate so the caller
        /// owns the Azure round-trips and only pays for the items actually being proposed.
        /// </remarks>
        /// <param name="plan">The plan to filter. Mutated in place.</param>
        /// <param name="confirmations">
        /// What a direct read said about each resource ID. An ID missing from this map is treated as
        /// <see cref="AzureResourceConfirmation.Unknown"/>.
        /// </param>
        void ApplyConfirmations(
            AzureReconcilePlanViewModel plan,
            IReadOnlyDictionary<string, AzureResourceConfirmation> confirmations);
    }
}
