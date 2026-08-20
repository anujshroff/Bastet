using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{

    public enum CredentialCheckResult
    {
        Failed,
        NoVisibleSubscriptions,
        Valid
    }

    public interface IAzureService
    {

        Task<CredentialCheckResult> CheckCredential();

        Task<List<AzureSubscriptionViewModel>> GetSubscriptions();

        Task<AzureVNetInventory> GetVNetInventory(string subscriptionId);

        Task<IReadOnlyDictionary<string, AzureResourceConfirmation>> ConfirmResourcesAsync(
            IEnumerable<string> resourceIds);
    }
}
