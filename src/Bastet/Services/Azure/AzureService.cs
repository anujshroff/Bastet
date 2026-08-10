using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Resources;
using Bastet.Models.ViewModels;

namespace Bastet.Services.Azure
{

    public class AzureService(
        AzureArmClientProvider armClientProvider,
        ILogger<AzureService> logger) : IAzureService
    {
        private readonly ArmClient? _armClient = armClientProvider.Client;
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

        public static List<BulkAzureSubnetViewModel> BuildInventorySubnetRows(
            string resourceId, string name, IReadOnlyList<string> ipv4Prefixes)
        {
            ArgumentNullException.ThrowIfNull(ipv4Prefixes);

            List<string> prefixes = [.. ipv4Prefixes
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)];

            return prefixes.Count == 0
                ? [new BulkAzureSubnetViewModel { ResourceId = resourceId, Name = name }]
                : [.. prefixes.Select(prefix => new BulkAzureSubnetViewModel
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
