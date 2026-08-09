using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{

    public interface IAzureService
    {

        Task<bool> IsCredentialValid();

        Task<List<AzureSubscriptionViewModel>> GetSubscriptions();

        Task<AzureVNetInventory> GetVNetInventory(string subscriptionId);

        Task<IReadOnlyDictionary<string, AzureResourceConfirmation>> ConfirmResourcesAsync(
            IEnumerable<string> resourceIds);
    }
}
