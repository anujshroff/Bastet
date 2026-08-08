using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Resources;
using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{

    public class AzureService(
        IIpUtilityService ipUtilityService,
        AzureArmClientProvider armClientProvider,
        ILogger<AzureService> logger) : IAzureService
    {
        private readonly ArmClient? _armClient = armClientProvider.Client;
        private readonly IIpUtilityService _ipUtilityService = ipUtilityService;
        private readonly ILogger<AzureService> _logger = logger;

        public async Task<bool> IsCredentialValid()
        {
            if (_armClient == null)
            {
                return false;
            }

            try
            {

                SubscriptionCollection subscriptions = _armClient.GetSubscriptions();

                await foreach (SubscriptionResource? _ in subscriptions)
                {

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Azure credential validation failed");
                return false;
            }
        }

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

                _logger.LogError(ex, "Failed to retrieve Azure subscriptions");
                throw;
            }
        }

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

                    foreach (string? addressPrefix in vnet.Data.AddressSpace.AddressPrefixes)
                    {
                        if (string.IsNullOrEmpty(addressPrefix))
                        {
                            continue;
                        }

                        string vnetNetworkAddress = GetNetworkAddressFromCidr(addressPrefix);
                        int vnetCidr = GetCidrFromAddressPrefix(addressPrefix);

                        if (vnetNetworkAddress == networkAddress && vnetCidr == cidr)
                        {
                            result.Add(new AzureVNetViewModel
                            {
                                ResourceId = vnet.Id.ToString(),
                                Name = vnet.Data.Name,
                                AddressPrefixes = [.. vnet.Data.AddressSpace.AddressPrefixes]
                            });
                            break;
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex, "Failed to retrieve compatible Azure VNets for subscription {SubscriptionId}", SanitizeForLog(subscriptionId));
                throw;
            }
        }

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

                VirtualNetworkResource vnetResource = _armClient.GetVirtualNetworkResource(new ResourceIdentifier(vnetResourceId));
                vnetResource = vnetResource.Get();
                List<string> vnetAddressPrefixes = vnetResource.Data.AddressSpace.AddressPrefixes?.ToList() ?? [];

                await foreach (SubnetResource? subnet in vnetResource.GetSubnets())
                {

                    if (subnet.Data.AddressPrefix is not null)
                    {
                        if (IsIpv4AddressPrefix(subnet.Data.AddressPrefix))
                        {

                            bool fullyEncompassesVNetPrefix = vnetAddressPrefixes.Any(prefix =>
                                string.Equals(prefix, subnet.Data.AddressPrefix, StringComparison.OrdinalIgnoreCase));

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

                    else if (subnet.Data.AddressPrefixes?.Any() == true)
                    {

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

                            if (hasIpv4 && hasIpv6)
                            {
                                break;
                            }
                        }

                        bool hasMultipleAddressSchemes = hasIpv4 && hasIpv6;

                        foreach (string? addressPrefix in subnet.Data.AddressPrefixes)
                        {
                            if (IsIpv4AddressPrefix(addressPrefix))
                            {

                                bool fullyEncompassesVNetPrefix = vnetAddressPrefixes.Any(prefix =>
                                    string.Equals(prefix, addressPrefix, StringComparison.OrdinalIgnoreCase));

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

                            }
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {

                _logger.LogError(ex, "Failed to retrieve compatible Azure subnets for VNet {VNetResourceId}", SanitizeForLog(vnetResourceId));
                throw;
            }
        }

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

                    if (vnetVm.Ipv4AddressPrefixes.Count == 0)
                    {
                        continue;
                    }

                    foreach (SubnetData subnet in vnet.Data.Subnets ?? [])
                    {
                        vnetVm.Subnets.AddRange(BuildInventorySubnetRows(
                            subnet.Id?.ToString() ?? string.Empty,
                            subnet.Name ?? string.Empty,
                            [.. ExtractIpv4Prefixes(subnet)]));
                    }

                    result.Add(vnetVm);
                }

                return new AzureVNetInventory { Success = true, VNets = result };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve Azure VNets with subnets for subscription {SubscriptionId}", SanitizeForLog(subscriptionId));

                return new AzureVNetInventory { Success = false, ErrorMessage = "Azure could not be read for this subscription. Details have been logged." };
            }
        }

        private static IEnumerable<string> ExtractIpv4Prefixes(SubnetData subnet)
        {
            if (subnet.AddressPrefix is not null && IsIpv4AddressPrefix(subnet.AddressPrefix))
            {
                yield return subnet.AddressPrefix;
            }

            if (subnet.AddressPrefixes is null)
            {
                yield break;
            }

            foreach (string? prefix in subnet.AddressPrefixes)
            {
                if (!string.IsNullOrEmpty(prefix) && IsIpv4AddressPrefix(prefix))
                {
                    yield return prefix;
                }
            }
        }

        private static string? ExtractIpv4Prefix(SubnetData subnet)
        {

            if (subnet.AddressPrefix is not null && IsIpv4AddressPrefix(subnet.AddressPrefix))
            {
                return subnet.AddressPrefix;
            }

            if (subnet.AddressPrefixes?.Any() == true)
            {
                foreach (string? prefix in subnet.AddressPrefixes)
                {
                    if (!string.IsNullOrEmpty(prefix) && IsIpv4AddressPrefix(prefix))
                    {
                        return prefix;
                    }
                }
            }

            return null;
        }

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

        public static List<BulkAzureSubnetViewModel> BuildInventorySubnetRows(
            string resourceId, string name, IReadOnlyList<string> ipv4Prefixes)
        {
            ArgumentNullException.ThrowIfNull(ipv4Prefixes);

            List<string> prefixes = [.. ipv4Prefixes
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)];

            return [.. prefixes.Select(prefix => new BulkAzureSubnetViewModel
            {
                ResourceId = resourceId,
                Name = name,
                AddressPrefix = prefix,
                Ipv4AddressPrefixes = [.. prefixes]
            })];
        }

        private static bool IsIpv4AddressPrefix(string addressPrefix)
        {
            if (string.IsNullOrEmpty(addressPrefix))
            {
                return false;
            }

            string ipPart = addressPrefix.Split('/')[0];
            return ipPart.Split('.').Length == 4;
        }

        private static string GetNetworkAddressFromCidr(string addressPrefix)
        {
            if (string.IsNullOrEmpty(addressPrefix))
            {
                return string.Empty;
            }

            string[] parts = addressPrefix.Split('/');
            return parts.Length > 0 ? parts[0] : string.Empty;
        }

        private static int GetCidrFromAddressPrefix(string addressPrefix)
        {
            if (string.IsNullOrEmpty(addressPrefix))
            {
                return 0;
            }

            string[] parts = addressPrefix.Split('/');
            return parts.Length > 1 && int.TryParse(parts[1], out int cidr) ? cidr : 0;
        }

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

                return distinct.ToDictionary(
                    id => id, _ => AzureResourceConfirmation.Unknown, StringComparer.OrdinalIgnoreCase);
            }

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

        private const int MaxConcurrentResourceChecks = 8;

        private async Task<AzureResourceConfirmation> ConfirmOneAsync(string resourceId)
        {

            if (!ResourceIdentifier.TryParse(resourceId, out ResourceIdentifier? identifier) || identifier is null)
            {
                _logger.LogWarning("Could not parse the Azure resource ID {ResourceId}", SanitizeForLog(resourceId));
                return AzureResourceConfirmation.Unknown;
            }

            bool isSubnet = AzureResourceIdentity.IsAzureSubnet(resourceId);
            if (!isSubnet && !AzureResourceIdentity.IsAzureVNet(resourceId))
            {
                _logger.LogWarning(
                    "The stored Azure resource ID {ResourceId} names neither a VNet nor a subnet, so it cannot be confirmed",
                    SanitizeForLog(resourceId));
                return AzureResourceConfirmation.Unknown;
            }

            try
            {
                if (isSubnet)
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

        private static string SanitizeForLog(string? value) =>
            Bastet.Services.Security.LogSanitizer.SanitizeForLog(value);
    }
}
