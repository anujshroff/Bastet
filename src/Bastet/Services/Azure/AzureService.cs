using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Resources;
using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{
    /// <summary>
    /// Implementation of the Azure service for interacting with Azure APIs
    /// </summary>
    public class AzureService : IAzureService
    {
        private readonly ArmClient? _armClient;
        private readonly IIpUtilityService _ipUtilityService;

        /// <summary>
        /// Creates a new instance of the AzureService
        /// </summary>
        /// <param name="ipUtilityService">The IP utility service for subnet calculations</param>
        public AzureService(IIpUtilityService ipUtilityService)
        {
            _ipUtilityService = ipUtilityService;

            try
            {
                // DefaultAzureCredential attempts multiple authentication methods
                // including environment variables, managed identity, and Visual Studio/CLI credentials
                DefaultAzureCredential credential = new();
                _armClient = new ArmClient(credential);
            }
            catch (Exception)
            {
                // Handle credential creation failure
                _armClient = null;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> IsCredentialValid()
        {
            if (_armClient == null)
            {
                return false;
            }

            try
            {
                // Try to access Azure resources to verify credentials
                SubscriptionCollection subscriptions = _armClient.GetSubscriptions();
                // Just check if we can enumerate subscriptions without error
                await foreach (SubscriptionResource? _ in subscriptions)
                {
                    // Just need one subscription to verify credentials
                    return true;
                }

                // No error, but no subscriptions either
                return false;
            }
            catch
            {
                // Error occurred during access
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<List<AzureSubscriptionViewModel>> GetSubscriptions()
        {
            if (_armClient == null)
            {
                return [];
            }

            List<AzureSubscriptionViewModel> result = [];

            await foreach (SubscriptionResource? subscription in _armClient.GetSubscriptions())
            {
                result.Add(new AzureSubscriptionViewModel
                {
                    SubscriptionId = subscription.Data.SubscriptionId,
                    DisplayName = subscription.Data.DisplayName
                });
            }

            return result;
        }

        /// <inheritdoc/>
        public async Task<List<AzureVNetViewModel>> GetCompatibleVNets(
            string subscriptionId, string networkAddress, int cidr)
        {
            if (_armClient == null || string.IsNullOrEmpty(subscriptionId))
            {
                return [];
            }

            List<AzureVNetViewModel> result = [];

            try
            {
                ResourceIdentifier resourceIdentifier = SubscriptionResource.CreateResourceIdentifier(subscriptionId);
                SubscriptionResource selectedSubscription = _armClient.GetSubscriptionResource(resourceIdentifier);

                await foreach (VirtualNetworkResource vnet in selectedSubscription.GetVirtualNetworksAsync())
                {
                    if (vnet.Data.AddressSpace.AddressPrefixes == null)
                    {
                        continue;
                    }

                    // Check if any address prefix matches our Bastet subnet
                    foreach (string? addressPrefix in vnet.Data.AddressSpace.AddressPrefixes)
                    {
                        if (string.IsNullOrEmpty(addressPrefix))
                        {
                            continue;
                        }

                        // Parse the CIDR notation
                        string vnetNetworkAddress = GetNetworkAddressFromCidr(addressPrefix);
                        int vnetCidr = GetCidrFromAddressPrefix(addressPrefix);

                        // Check if this VNet address space matches our Bastet subnet
                        if (vnetNetworkAddress == networkAddress && vnetCidr == cidr)
                        {
                            result.Add(new AzureVNetViewModel
                            {
                                ResourceId = vnet.Id.ToString(),
                                Name = vnet.Data.Name,
                                AddressPrefixes = [.. vnet.Data.AddressSpace.AddressPrefixes]
                            });
                            break; // Found a match, no need to check other address prefixes
                        }
                    }
                }

                return result;
            }
            catch (Exception)
            {
                return [];
            }
        }

        /// <inheritdoc/>
        public async Task<List<AzureSubnetViewModel>> GetCompatibleSubnets(
            string vnetResourceId, string networkAddress, int cidr)
        {
            if (_armClient == null || string.IsNullOrEmpty(vnetResourceId))
            {
                return [];
            }

            List<AzureSubnetViewModel> result = [];

            try
            {
                // Get the VNet resource and its address prefixes for comparison
                VirtualNetworkResource vnetResource = _armClient.GetVirtualNetworkResource(new ResourceIdentifier(vnetResourceId));
                List<string> vnetAddressPrefixes = vnetResource.Data.AddressSpace.AddressPrefixes?.ToList() ?? [];

                await foreach (SubnetResource? subnet in vnetResource.GetSubnets())
                {
                    // Case 1: Only has one IP scheme (either IPv4 or IPv6)
                    if (subnet.Data.AddressPrefix is not null)
                    {
                        if (IsIpv4AddressPrefix(subnet.Data.AddressPrefix))
                        {
                            // Check if this subnet's prefix exactly matches any VNet address prefix
                            bool fullyEncompassesVNetPrefix = vnetAddressPrefixes.Any(prefix =>
                                string.Equals(prefix, subnet.Data.AddressPrefix, StringComparison.OrdinalIgnoreCase));

                            // If subnet fully encompasses a VNet prefix AND matches the parent subnet's network and CIDR,
                            // add it to results regardless of containment validation
                            if (fullyEncompassesVNetPrefix &&
                                string.Equals(GetNetworkAddressFromCidr(subnet.Data.AddressPrefix), networkAddress, StringComparison.OrdinalIgnoreCase) &&
                                GetCidrFromAddressPrefix(subnet.Data.AddressPrefix) == cidr)
                            {
                                result.Add(new AzureSubnetViewModel
                                {
                                    Name = subnet.Data.Name,
                                    AddressPrefix = subnet.Data.AddressPrefix,
                                    HasMultipleAddressSchemes = false,
                                    FullyEncompassesVNetPrefix = true
                                });
                            }
                            // Otherwise apply normal containment validation
                            else
                            {
                                TryAddCompatibleSubnet(
                                    result,
                                    subnet.Data.Name,
                                    subnet.Data.AddressPrefix,
                                    false,
                                    networkAddress,
                                    cidr);
                            }
                        }
                    }
                    // Case 2: Has address prefixes (could be IPv4 only or both IPv4 and IPv6)
                    else if (subnet.Data.AddressPrefixes?.Any() == true)
                    {
                        // Check if the subnet actually has both IPv4 and IPv6 prefixes
                        bool hasIpv4 = false;
                        bool hasIpv6 = false;

                        foreach (string? prefix in subnet.Data.AddressPrefixes)
                        {
                            if (IsIpv4AddressPrefix(prefix))
                            {
                                hasIpv4 = true;
                            }
                            else
                            {
                                hasIpv6 = true;
                            }

                            // If we found both types, we can stop checking
                            if (hasIpv4 && hasIpv6)
                            {
                                break;
                            }
                        }

                        bool hasMultipleAddressSchemes = hasIpv4 && hasIpv6;

                        // Per requirements: "If an Azure subnet in an Azure vnet is assigned to both 
                        // IPv4 and IPv6, we ignore IPv6 for that subnet here and in subsequent steps."
                        foreach (string? addressPrefix in subnet.Data.AddressPrefixes)
                        {
                            if (IsIpv4AddressPrefix(addressPrefix))
                            {
                                // Check if this subnet's prefix exactly matches any VNet address prefix
                                bool fullyEncompassesVNetPrefix = vnetAddressPrefixes.Any(prefix =>
                                    string.Equals(prefix, addressPrefix, StringComparison.OrdinalIgnoreCase));

                                // If subnet fully encompasses a VNet prefix AND matches the parent subnet's network and CIDR,
                                // add it to results regardless of containment validation
                                if (fullyEncompassesVNetPrefix &&
                                    string.Equals(GetNetworkAddressFromCidr(addressPrefix), networkAddress, StringComparison.OrdinalIgnoreCase) &&
                                    GetCidrFromAddressPrefix(addressPrefix) == cidr)
                                {
                                    result.Add(new AzureSubnetViewModel
                                    {
                                        Name = subnet.Data.Name,
                                        AddressPrefix = addressPrefix,
                                        HasMultipleAddressSchemes = hasMultipleAddressSchemes,
                                        FullyEncompassesVNetPrefix = true
                                    });
                                }
                                // Otherwise apply normal containment validation
                                else
                                {
                                    TryAddCompatibleSubnet(
                                        result,
                                        subnet.Data.Name,
                                        addressPrefix,
                                        hasMultipleAddressSchemes,
                                        networkAddress,
                                        cidr);
                                }

                                break; // Take only the first valid IPv4 address
                            }
                        }
                    }
                }

                return result;
            }
            catch (Exception)
            {
                return [];
            }
        }

        /// <summary>
        /// Tries to add a subnet to the result list if it's a valid child of the specified parent subnet
        /// </summary>
        private void TryAddCompatibleSubnet(
            List<AzureSubnetViewModel> result,
            string name,
            string addressPrefix,
            bool hasMultipleAddressSchemes,
            string parentNetworkAddress,
            int parentCidr)
        {
            string networkAddress = GetNetworkAddressFromCidr(addressPrefix);
            int subnetCidr = GetCidrFromAddressPrefix(addressPrefix);

            // Check if this subnet would be a valid child of our Bastet subnet
            if (_ipUtilityService.IsSubnetContainedInParent(
                networkAddress,
                subnetCidr,
                parentNetworkAddress,
                parentCidr))
            {
                result.Add(new AzureSubnetViewModel
                {
                    Name = name,
                    AddressPrefix = addressPrefix,
                    HasMultipleAddressSchemes = hasMultipleAddressSchemes,
                    FullyEncompassesVNetPrefix = false
                });
            }
        }

        /// <summary>
        /// Determines if an address prefix is IPv4
        /// </summary>
        private static bool IsIpv4AddressPrefix(string addressPrefix)
        {
            if (string.IsNullOrEmpty(addressPrefix))
            {
                return false;
            }

            // Basic validation - IPv4 addresses have 4 octets separated by dots
            string ipPart = addressPrefix.Split('/')[0];
            return ipPart.Split('.').Length == 4;
        }

        /// <summary>
        /// Extracts the network address from a CIDR notation string
        /// </summary>
        private static string GetNetworkAddressFromCidr(string addressPrefix)
        {
            if (string.IsNullOrEmpty(addressPrefix))
            {
                return string.Empty;
            }

            string[] parts = addressPrefix.Split('/');
            return parts.Length > 0 ? parts[0] : string.Empty;
        }

        /// <summary>
        /// Extracts the CIDR from a CIDR notation string
        /// </summary>
        private static int GetCidrFromAddressPrefix(string addressPrefix)
        {
            if (string.IsNullOrEmpty(addressPrefix))
            {
                return 0;
            }

            string[] parts = addressPrefix.Split('/');
            return parts.Length > 1 && int.TryParse(parts[1], out int cidr) ? cidr : 0;
        }
    }
}
