using Azure.Identity;
using Azure.ResourceManager;

namespace Bastet.Services.Azure
{
    /// <summary>
    /// Holds the single, process-wide ArmClient used to talk to Azure.
    /// Registered as a singleton so the underlying credential's token cache is shared
    /// across requests rather than re-authenticating on every one.
    /// </summary>
    public class AzureArmClientProvider
    {
        /// <summary>
        /// The ArmClient, or null if a credential could not be created
        /// </summary>
        public ArmClient? Client { get; }

        /// <summary>
        /// Creates the ArmClient using DefaultAzureCredential, which selects an authentication
        /// method based on the environment. Set AZURE_TOKEN_CREDENTIALS=dev for local development
        /// so the chain skips the managed identity probe and uses the Azure CLI login instead.
        /// </summary>
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
