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

        /// <summary>
        /// Fills in the availability of every VNet prefix and Azure subnet, so the selection UI can
        /// show what is already imported and stop the user picking something that cannot work.
        /// </summary>
        /// <remarks>
        /// Applies the same rules as <see cref="BuildPlan"/>, which is why it lives here: anything
        /// left selectable must produce a committable plan, and anything blocked must be something
        /// BuildPlan would reject. Mutates the passed-in view models in place.
        /// </remarks>
        /// <param name="vnets">The Azure inventory to annotate</param>
        /// <param name="existingSubnets">Snapshot of every Bastet subnet currently in the database</param>
        void AnnotateAvailability(
            IReadOnlyList<BulkAzureVNetViewModel> vnets,
            IReadOnlyList<ExistingSubnetSnapshot> existingSubnets);
    }
}
