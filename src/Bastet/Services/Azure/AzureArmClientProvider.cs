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
        /// method based on the environment.
        /// </summary>
        /// <remarks>
        /// Local development requires AZURE_TOKEN_CREDENTIALS=dev (set by the launch profiles).
        /// Without it, ManagedIdentityCredential throws instead of reporting itself unavailable
        /// when the IMDS endpoint is unreachable, which terminates the chain before the Azure CLI
        /// credential is tried. This is an Azure.Core regression (1.59.0; incompletely fixed in
        /// 1.60.0) and the workaround can be removed once a later release fixes it. See the README.
        /// </remarks>
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
