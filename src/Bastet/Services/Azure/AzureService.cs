using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Resources;
using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{
    /// <summary>
    /// Implementation of the Azure service for interacting with Azure APIs
    /// </summary>
    /// <remarks>
    /// Creates a new instance of the AzureService
    /// </remarks>
    /// <param name="ipUtilityService">The IP utility service for subnet calculations</param>
    /// <param name="armClientProvider">Provides the shared ArmClient</param>
    /// <param name="logger">Logger for reporting Azure access failures</param>
    public class AzureService(
        IIpUtilityService ipUtilityService,
        AzureArmClientProvider armClientProvider,
        ILogger<AzureService> logger) : IAzureService
    {
        private readonly ArmClient? _armClient = armClientProvider.Client;
        private readonly IIpUtilityService _ipUtilityService = ipUtilityService;
        private readonly ILogger<AzureService> _logger = logger;

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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Azure credential validation failed");
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

            try
            {
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
            catch (Exception ex)
            {
                // Rethrown rather than collapsed to an empty list. Every caller renders an empty
                // result as "Azure has none of these", which is a different fact from "Azure could
                // not be asked" - and the difference decides whether an admin retries or rebuilds
                // the hierarchy by hand, permanently unlinked from Azure. The controllers already
                // have catch blocks that report success = false; swallowing here made them
                // unreachable. Same rule GetVNetInventory applies with its Success flag.
                _logger.LogError(ex, "Failed to retrieve Azure subscriptions");
                throw;
            }
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
            catch (Exception ex)
            {
                // See GetSubscriptions: a failed read must reach the caller as a failure, not as an
                // empty subscription. The wizard already renders #vnet-error for it.
                _logger.LogError(ex, "Failed to retrieve compatible Azure VNets for subscription {SubscriptionId}", SanitizeForLog(subscriptionId));
                throw;
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
                vnetResource = vnetResource.Get();
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
                                    ResourceId = subnet.Id.ToString(),
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
                                    subnet.Id.ToString(),
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
                                        ResourceId = subnet.Id.ToString(),
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
                                        subnet.Id.ToString(),
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
            catch (Exception ex)
            {
                // See GetSubscriptions. This one also covers vnetResource.Get() 404ing because the
                // VNet was deleted between step 2 and step 3 of the wizard, which is a real failure
                // rather than a VNet that happens to contain no compatible subnets.
                _logger.LogError(ex, "Failed to retrieve compatible Azure subnets for VNet {VNetResourceId}", SanitizeForLog(vnetResourceId));
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<AzureVNetInventory> GetVNetInventory(string subscriptionId)
        {
            if (_armClient == null)
            {
                return new AzureVNetInventory
                {
                    Success = false,
                    ErrorMessage = "No Azure credential is available. Check the application's Azure authentication configuration."
                };
            }

            if (string.IsNullOrEmpty(subscriptionId))
            {
                return new AzureVNetInventory { Success = false, ErrorMessage = "No subscription was specified." };
            }

            List<BulkAzureVNetViewModel> result = [];

            try
            {
                ResourceIdentifier resourceIdentifier = SubscriptionResource.CreateResourceIdentifier(subscriptionId);
                SubscriptionResource selectedSubscription = _armClient.GetSubscriptionResource(resourceIdentifier);

                await foreach (VirtualNetworkResource vnet in selectedSubscription.GetVirtualNetworksAsync())
                {
                    BulkAzureVNetViewModel vnetVm = new()
                    {
                        ResourceId = vnet.Id.ToString(),
                        Name = vnet.Data.Name
                    };

                    // Collect IPv4 prefixes only
                    if (vnet.Data.AddressSpace?.AddressPrefixes != null)
                    {
                        foreach (string? prefix in vnet.Data.AddressSpace.AddressPrefixes)
                        {
                            if (!string.IsNullOrEmpty(prefix) && IsIpv4AddressPrefix(prefix))
                            {
                                vnetVm.Ipv4AddressPrefixes.Add(prefix);
                            }
                        }
                    }

                    // Skip VNets that have no IPv4 prefixes (IPv6-only is out of scope)
                    if (vnetVm.Ipv4AddressPrefixes.Count == 0)
                    {
                        continue;
                    }

                    // Enumerate subnets in the VNet (IPv4 only)
                    await foreach (SubnetResource? subnet in vnet.GetSubnets())
                    {
                        string? ipv4Prefix = ExtractIpv4Prefix(subnet);
                        if (string.IsNullOrEmpty(ipv4Prefix))
                        {
                            continue;
                        }

                        vnetVm.Subnets.Add(new BulkAzureSubnetViewModel
                        {
                            ResourceId = subnet.Id.ToString(),
                            Name = subnet.Data.Name ?? string.Empty,
                            AddressPrefix = ipv4Prefix
                        });
                    }

                    result.Add(vnetVm);
                }

                return new AzureVNetInventory { Success = true, VNets = result };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve Azure VNets with subnets for subscription {SubscriptionId}", SanitizeForLog(subscriptionId));

                // Report the failure rather than an empty inventory: callers must be able to tell
                // "this subscription has no VNets" apart from "Azure could not be reached". The
                // message can end up in the UI, so keep the exception text in the log only.
                return new AzureVNetInventory { Success = false, ErrorMessage = "Azure could not be read for this subscription. Details have been logged." };
            }
        }

        /// <summary>
        /// Returns the first IPv4 prefix associated with the given Azure subnet, or null if none exists.
        /// Handles both single-prefix subnets and dual-stack subnets.
        /// </summary>
        private static string? ExtractIpv4Prefix(SubnetResource subnet)
        {
            // Case 1: Single address prefix
            if (subnet.Data.AddressPrefix is not null && IsIpv4AddressPrefix(subnet.Data.AddressPrefix))
            {
                return subnet.Data.AddressPrefix;
            }

            // Case 2: Multiple address prefixes (dual-stack)
            if (subnet.Data.AddressPrefixes?.Any() == true)
            {
                foreach (string? prefix in subnet.Data.AddressPrefixes)
                {
                    if (!string.IsNullOrEmpty(prefix) && IsIpv4AddressPrefix(prefix))
                    {
                        return prefix;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Tries to add a subnet to the result list if it's a valid child of the specified parent subnet
        /// </summary>
        private void TryAddCompatibleSubnet(
            List<AzureSubnetViewModel> result,
            string resourceId,
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
                    ResourceId = resourceId,
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

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<string, AzureResourceConfirmation>> ConfirmResourcesAsync(
            IEnumerable<string> resourceIds)
        {
            ArgumentNullException.ThrowIfNull(resourceIds);

            List<string> distinct = [.. resourceIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)];

            if (distinct.Count == 0)
            {
                return new Dictionary<string, AzureResourceConfirmation>(StringComparer.OrdinalIgnoreCase);
            }

            if (_armClient == null)
            {
                // No credential means no answers, and an unanswered question must never read as a
                // deletion. Report every ID as unknown rather than leaving it absent from the map.
                return distinct.ToDictionary(
                    id => id, _ => AzureResourceConfirmation.Unknown, StringComparer.OrdinalIgnoreCase);
            }

            // Bounded concurrency: reconcile can be asked about a lot of subnets at once, and a
            // serial pass would make the delete path's latency the sum of every check. The cap keeps
            // us well clear of ARM throttling, which would come back as Unknown and block deletions
            // the operator legitimately wants.
            using SemaphoreSlim gate = new(MaxConcurrentResourceChecks);

            IEnumerable<Task<KeyValuePair<string, AzureResourceConfirmation>>> checks = distinct.Select(async id =>
            {
                await gate.WaitAsync();
                try
                {
                    return new KeyValuePair<string, AzureResourceConfirmation>(id, await ConfirmOneAsync(id));
                }
                finally
                {
                    gate.Release();
                }
            });

            KeyValuePair<string, AzureResourceConfirmation>[] results = await Task.WhenAll(checks);
            return new Dictionary<string, AzureResourceConfirmation>(results, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>How many resource checks to have in flight at once.</summary>
        private const int MaxConcurrentResourceChecks = 8;

        /// <summary>
        /// Reads one resource and maps Azure's answer onto <see cref="AzureResourceConfirmation"/>.
        /// </summary>
        /// <remarks>
        /// The status code carries the meaning, not the error code string: a missing VNet reports
        /// "ResourceNotFound" while a missing subnet reports "NotFound", and both are 404. Subnets
        /// are child resources, so they need the subnet accessor - the generic resource API rejects
        /// their IDs outright.
        /// </remarks>
        private async Task<AzureResourceConfirmation> ConfirmOneAsync(string resourceId)
        {
            ResourceIdentifier identifier;
            try
            {
                identifier = new ResourceIdentifier(resourceId);
            }
            catch (Exception ex)
            {
                // Free text on the entity, so a malformed value is possible. Unknown, never Deleted.
                _logger.LogWarning(ex, "Could not parse the Azure resource ID {ResourceId}", SanitizeForLog(resourceId));
                return AzureResourceConfirmation.Unknown;
            }

            try
            {
                if (AzureResourceIdentity.IsAzureSubnet(resourceId))
                {
                    await _armClient.GetSubnetResource(identifier).GetAsync();
                }
                else
                {
                    await _armClient.GetVirtualNetworkResource(identifier).GetAsync();
                }

                return AzureResourceConfirmation.Live;
            }
            catch (global::Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                return AzureResourceConfirmation.Deleted;
            }
            catch (global::Azure.RequestFailedException ex) when (ex.Status is 401 or 403)
            {
                _logger.LogWarning(
                    "Azure denied access to {ResourceId} ({Status}), so it cannot be reported as deleted",
                    SanitizeForLog(resourceId), ex.Status);
                return AzureResourceConfirmation.NotVisible;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not confirm the Azure resource {ResourceId}", SanitizeForLog(resourceId));
                return AzureResourceConfirmation.Unknown;
            }
        }

        /// <summary>
        /// Strips line breaks from request-supplied values before logging, so crafted input can't
        /// forge additional log entries (CodeQL: log entries created from user input).
        /// </summary>
        private static string SanitizeForLog(string? value) =>
            Bastet.Services.Security.LogSanitizer.SanitizeForLog(value);
    }
}
