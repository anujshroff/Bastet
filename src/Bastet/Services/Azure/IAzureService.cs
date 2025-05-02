using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{
    /// <summary>
    /// Service for interacting with the Azure API to import VNets and subnets
    /// </summary>
    public interface IAzureService
    {
        /// <summary>
        /// Checks if the Azure credential is valid and can authenticate with Azure
        /// </summary>
        /// <returns>True if the credential is valid, false otherwise</returns>
        Task<bool> IsCredentialValid();

        /// <summary>
        /// Gets all available Azure subscriptions
        /// </summary>
        /// <returns>List of Azure subscriptions</returns>
        Task<List<AzureSubscriptionViewModel>> GetSubscriptions();

        /// <summary>
        /// Gets Azure VNets in a subscription that match the specified network address and CIDR
        /// </summary>
        /// <param name="subscriptionId">The Azure subscription ID</param>
        /// <param name="networkAddress">The network address to match</param>
        /// <param name="cidr">The CIDR to match</param>
        /// <returns>List of matching Azure VNets</returns>
        Task<List<AzureVNetViewModel>> GetCompatibleVNets(
            string subscriptionId,
            string networkAddress,
            int cidr);

        /// <summary>
        /// Gets Azure subnets from a VNet that would be valid children of the specified subnet
        /// </summary>
        /// <param name="vnetResourceId">The Azure VNet resource ID</param>
        /// <param name="networkAddress">The parent subnet network address</param>
        /// <param name="cidr">The parent subnet CIDR</param>
        /// <returns>List of compatible Azure subnets</returns>
        Task<List<AzureSubnetViewModel>> GetCompatibleSubnets(
            string vnetResourceId,
            string networkAddress,
            int cidr);
    }
}
