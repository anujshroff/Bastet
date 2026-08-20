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

    public Task<CredentialCheckResult> CheckCredential() => Task.FromResult(
        !_credentialValid
            ? CredentialCheckResult.Failed
            : _subscriptions.Count > 0 ? CredentialCheckResult.Valid : CredentialCheckResult.NoVisibleSubscriptions);

    public Task<List<AzureSubscriptionViewModel>> GetSubscriptions() => Task.FromResult(_subscriptions);

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
