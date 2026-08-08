using Azure.Identity;
using Azure.ResourceManager;

namespace Bastet.Services.Azure
{

    public class AzureArmClientProvider
    {

        public ArmClient? Client { get; }

        public AzureArmClientProvider(ILogger<AzureArmClientProvider> logger)
        {
            try
            {
                DefaultAzureCredential credential = new();
                Client = new ArmClient(credential);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create Azure credential; Azure features will be unavailable");
                Client = null;
            }
        }
    }
}
