using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{

    public interface IAzureSubnetSnapshotService
    {

        Task<IReadOnlyList<ExistingSubnetSnapshot>> GetExistingSubnetsAsync();

        Task<IReadOnlyList<AzureLinkedSubnetSnapshot>> GetAzureLinkedSubnetsAsync();
    }
}
