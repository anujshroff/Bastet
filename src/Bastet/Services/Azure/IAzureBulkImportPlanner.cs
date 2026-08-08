using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{

    public interface IAzureBulkImportPlanner
    {

        BulkImportPlanViewModel BuildPlan(
            BulkImportSelectionDto selection,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets);

        void AnnotateAvailability(
            IReadOnlyList<BulkAzureVNetViewModel> vnets,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets);
    }
}
