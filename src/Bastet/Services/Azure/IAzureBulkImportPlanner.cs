using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{
    /// <summary>
    /// Computes a Bulk Azure Import plan from a user's selection plus the
    /// current Bastet subnet tree, performing all overlap/conflict detection
    /// up front so the commit step does not surface surprises.
    /// </summary>
    public interface IAzureBulkImportPlanner
    {
        /// <summary>
        /// Build a plan for the given selection. The returned plan is always
        /// well-formed; check <see cref="BulkImportPlanViewModel.CanCommit"/>
        /// to determine whether it can be safely executed.
        /// </summary>
        /// <param name="selection">The user's selection from the Bulk Azure Import UI</param>
        /// <param name="existingSubnets">Snapshot of every Bastet subnet currently in the database</param>
        BulkImportPlanViewModel BuildPlan(
            BulkImportSelectionDto selection,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets);
    }
}
