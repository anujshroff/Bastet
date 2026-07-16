using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{
    /// <summary>
    /// Reads the Bastet subnet tree into the plain snapshots the Azure planners work from, so those
    /// planners never need to know about EF.
    /// </summary>
    public interface IAzureSubnetSnapshotService
    {
        /// <summary>
        /// Every Bastet subnet, with the flags the bulk import planner needs to pick and validate
        /// import targets.
        /// </summary>
        Task<IReadOnlyList<ExistingSubnetSnapshot>> GetExistingSubnetsAsync();

        /// <summary>
        /// Only the subnets that were imported from Azure (those carrying an Azure resource ID),
        /// each with the number of descendants and host IPs that deleting it would archive.
        /// </summary>
        Task<IReadOnlyList<AzureLinkedSubnetSnapshot>> GetAzureLinkedSubnetsAsync();
    }
}
