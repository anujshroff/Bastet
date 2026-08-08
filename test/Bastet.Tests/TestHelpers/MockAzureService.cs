using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Azure;

namespace Bastet.Tests.TestHelpers;

public class MockAzureService : IAzureService
{
    private readonly bool _credentialValid;
    private readonly List<AzureSubscriptionViewModel> _subscriptions;
    private readonly List<AzureVNetViewModel> _vnets;
    private readonly List<AzureSubnetViewModel> _subnets;
    private readonly IpUtilityService _ipUtilityService;

    public MockAzureService()
    {
        _credentialValid = true;
        _subscriptions = [];
        _vnets = [];
        _subnets = [];
        _ipUtilityService = new IpUtilityService();
    }

    public MockAzureService(bool credentialValid)
    {
        _credentialValid = credentialValid;
        _subscriptions = [];
        _vnets = [];
        _subnets = [];
        _ipUtilityService = new IpUtilityService();
    }

    public MockAzureService(
        bool credentialValid,
        List<AzureSubscriptionViewModel>? subscriptions = null,
        List<AzureVNetViewModel>? vnets = null,
        List<AzureSubnetViewModel>? subnets = null)
    {
        _credentialValid = credentialValid;
        _subscriptions = subscriptions ?? [];
        _vnets = vnets ?? [];
        _subnets = subnets ?? [];
        _ipUtilityService = new IpUtilityService();
    }

    public Task<bool> IsCredentialValid() => Task.FromResult(_credentialValid);

    public Task<List<AzureSubscriptionViewModel>> GetSubscriptions() => Task.FromResult(_subscriptions);

    public Task<List<AzureVNetViewModel>> GetCompatibleVNets(
        string subscriptionId,
        string networkAddress,
        int cidr)
    {

        List<AzureVNetViewModel> filteredVnets = [.. _vnets.Where(v => v.AddressPrefixes.Any(p => IsAddressCompatible(p, networkAddress, cidr)))];

        return Task.FromResult(filteredVnets);
    }

    public Task<List<AzureSubnetViewModel>> GetCompatibleSubnets(
        string vnetResourceId,
        string networkAddress,
        int cidr)
    {

        AzureVNetViewModel? vnet = _vnets.FirstOrDefault(v => v.ResourceId == vnetResourceId);
        List<string> vnetAddressPrefixes = vnet?.AddressPrefixes ?? [];

        List<AzureSubnetViewModel> filteredSubnets = [];

        foreach (AzureSubnetViewModel subnet in _subnets)
        {

            bool fullyEncompassesVNetPrefix = vnetAddressPrefixes.Any(prefix =>
                string.Equals(prefix, subnet.AddressPrefix, StringComparison.OrdinalIgnoreCase));

            string[] subnetParts = subnet.AddressPrefix.Split('/');
            string subnetNetworkAddress = subnetParts.Length > 0 ? subnetParts[0] : string.Empty;
            int subnetCidr = subnetParts.Length > 1 && int.TryParse(subnetParts[1], out int cidrValue) ? cidrValue : 0;

            if (fullyEncompassesVNetPrefix &&
                string.Equals(subnetNetworkAddress, networkAddress, StringComparison.OrdinalIgnoreCase) &&
                subnetCidr == cidr)
            {

                filteredSubnets.Add(new AzureSubnetViewModel
                {
                    ResourceId = subnet.ResourceId,
                    Name = subnet.Name,
                    AddressPrefix = subnet.AddressPrefix,
                    HasMultipleAddressSchemes = subnet.HasMultipleAddressSchemes,
                    FullyEncompassesVNetPrefix = true
                });
            }
            else
            {

                if (IsSubnetWithinParent(subnet.AddressPrefix, networkAddress, cidr))
                {

                    filteredSubnets.Add(new AzureSubnetViewModel
                    {
                        ResourceId = subnet.ResourceId,
                        Name = subnet.Name,
                        AddressPrefix = subnet.AddressPrefix,
                        HasMultipleAddressSchemes = subnet.HasMultipleAddressSchemes,
                        FullyEncompassesVNetPrefix = false
                    });
                }
            }
        }

        return Task.FromResult(filteredSubnets);
    }

    public Task<AzureVNetInventory> GetVNetInventory(string subscriptionId)
    {
        if (!_credentialValid)
        {
            return Task.FromResult(new AzureVNetInventory
            {
                Success = false,
                ErrorMessage = "Mock credential is not valid."
            });
        }

        if (string.IsNullOrEmpty(subscriptionId))
        {
            return Task.FromResult(new AzureVNetInventory
            {
                Success = false,
                ErrorMessage = "No subscription was specified."
            });
        }

        List<BulkAzureVNetViewModel> result = [];
        foreach (AzureVNetViewModel vnet in _vnets)
        {
            BulkAzureVNetViewModel bulkVnet = new()
            {
                ResourceId = vnet.ResourceId,
                Name = vnet.Name,
                Ipv4AddressPrefixes = [.. vnet.AddressPrefixes.Where(p => !string.IsNullOrEmpty(p) && p.Split('/')[0].Split('.').Length == 4)]
            };

            if (bulkVnet.Ipv4AddressPrefixes.Count == 0)
            {
                continue;
            }

            foreach (AzureSubnetViewModel sub in _subnets)
            {
                if (string.IsNullOrEmpty(sub.AddressPrefix))
                {
                    continue;
                }
                bool contained = bulkVnet.Ipv4AddressPrefixes.Any(p =>
                {
                    string[] parts = p.Split('/');
                    return parts.Length == 2
                        && int.TryParse(parts[1], out int pCidr)
                        && IsSubnetWithinParent(sub.AddressPrefix, parts[0], pCidr);
                });
                if (contained || bulkVnet.Ipv4AddressPrefixes.Any(p => string.Equals(p, sub.AddressPrefix, StringComparison.OrdinalIgnoreCase)))
                {
                    bulkVnet.Subnets.Add(new BulkAzureSubnetViewModel
                    {
                        ResourceId = sub.ResourceId,
                        Name = sub.Name,
                        AddressPrefix = sub.AddressPrefix
                    });
                }
            }

            result.Add(bulkVnet);
        }

        return Task.FromResult(new AzureVNetInventory { Success = true, VNets = result });
    }

    public Dictionary<string, AzureResourceConfirmation> Confirmations { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AzureResourceConfirmation DefaultConfirmation { get; set; } = AzureResourceConfirmation.Deleted;

    public Task<IReadOnlyDictionary<string, AzureResourceConfirmation>> ConfirmResourcesAsync(
        IEnumerable<string> resourceIds)
    {
        Dictionary<string, AzureResourceConfirmation> result = new(StringComparer.OrdinalIgnoreCase);

        foreach (string id in resourceIds.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            result[id] = !_credentialValid
                ? AzureResourceConfirmation.Unknown
                : Confirmations.TryGetValue(id, out AzureResourceConfirmation c) ? c : DefaultConfirmation;
        }

        return Task.FromResult<IReadOnlyDictionary<string, AzureResourceConfirmation>>(result);
    }

    private bool IsAddressCompatible(string addressPrefix, string parentAddress, int parentCidr)

    {
        if (string.IsNullOrEmpty(addressPrefix))
        {
            return false;
        }

        string[] parts = addressPrefix.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int addressCidr))
        {
            return false;
        }

        string vnetAddress = parts[0];

        if (vnetAddress == "10.0.0.0" && addressCidr == 16 &&
            parentAddress == "10.0.0.0" && parentCidr == 16)
        {
            return true;
        }

        if (addressCidr < parentCidr)
        {

            return _ipUtilityService.IsSubnetContainedInParent(
                parentAddress, parentCidr, vnetAddress, addressCidr);
        }
        else
        {

            return _ipUtilityService.IsSubnetContainedInParent(
                vnetAddress, addressCidr, parentAddress, parentCidr);
        }
    }

    private bool IsSubnetWithinParent(string subnetPrefix, string parentAddress, int parentCidr)
    {
        if (string.IsNullOrEmpty(subnetPrefix))
        {
            return false;
        }

        string[] parts = subnetPrefix.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int subnetCidr))
        {
            return false;
        }

        string subnetAddress = parts[0];

        return subnetCidr > parentCidr && _ipUtilityService.IsSubnetContainedInParent(
            subnetAddress, subnetCidr, parentAddress, parentCidr);
    }
}
