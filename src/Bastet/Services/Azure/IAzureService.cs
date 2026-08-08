using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{

    public interface IAzureService
    {

        Task<bool> IsCredentialValid();

        Task<List<AzureSubscriptionViewModel>> GetSubscriptions();

        Task<List<AzureVNetViewModel>> GetCompatibleVNets(
            string subscriptionId,
            string networkAddress,
            int cidr);

        Task<List<AzureSubnetViewModel>> GetCompatibleSubnets(
            string vnetResourceId,
            string networkAddress,
            int cidr);

        Task<AzureVNetInventory> GetVNetInventory(string subscriptionId);

        Task<IReadOnlyDictionary<string, AzureResourceConfirmation>> ConfirmResourcesAsync(
            IEnumerable<string> resourceIds);
    }
}
