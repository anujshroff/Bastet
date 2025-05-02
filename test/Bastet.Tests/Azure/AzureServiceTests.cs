using Bastet.Models.ViewModels;
using Bastet.Tests.TestHelpers;

namespace Bastet.Tests.Azure;

/// <summary>
/// Unit tests for the AzureService
/// </summary>
public class AzureServiceTests
{
    private readonly MockAzureService _mockAzureService;

    public AzureServiceTests()
    {
        // Initialize the mock service with test data
        List<AzureSubscriptionViewModel> subscriptions =
        [
            new() { SubscriptionId = "sub-1", DisplayName = "Test Subscription 1" },
            new() { SubscriptionId = "sub-2", DisplayName = "Test Subscription 2" }
        ];

        List<AzureVNetViewModel> vnets =
        [
            new()
            {
                ResourceId = "/subscriptions/sub-1/resourceGroups/test-rg/providers/Microsoft.Network/virtualNetworks/vnet1",
                Name = "vnet1",
                AddressPrefixes = ["10.0.0.0/16", "192.168.0.0/24"]
            },
            new()
            {
                ResourceId = "/subscriptions/sub-1/resourceGroups/test-rg/providers/Microsoft.Network/virtualNetworks/vnet2",
                Name = "vnet2",
                AddressPrefixes = ["172.16.0.0/12"]
            }
        ];

        List<AzureSubnetViewModel> subnets =
        [
            new() { Name = "subnet1", AddressPrefix = "10.0.0.0/24", HasMultipleAddressSchemes = false },
            new() { Name = "subnet2", AddressPrefix = "10.0.1.0/24", HasMultipleAddressSchemes = false },
            new() { Name = "subnet3", AddressPrefix = "172.16.1.0/24", HasMultipleAddressSchemes = false }
        ];

        _mockAzureService = new MockAzureService(true, subscriptions, vnets, subnets);
    }

    [Fact]
    public async Task IsCredentialValid_WithValidCredential_ReturnsTrue()
    {
        // Act
        bool result = await _mockAzureService.IsCredentialValid();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsCredentialValid_WithInvalidCredential_ReturnsFalse()
    {
        // Arrange
        MockAzureService service = new(false);

        // Act
        bool result = await service.IsCredentialValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsAllSubscriptions()
    {
        // Act
        List<AzureSubscriptionViewModel> subscriptions = await _mockAzureService.GetSubscriptions();

        // Assert
        Assert.Equal(2, subscriptions.Count);
        Assert.Contains(subscriptions, s => s.SubscriptionId == "sub-1");
        Assert.Contains(subscriptions, s => s.SubscriptionId == "sub-2");
    }

    [Fact]
    public async Task GetCompatibleVNets_WithMatchingCIDR_ReturnsFilteredVNets()
    {
        // Arrange
        string subscriptionId = "sub-1";
        string networkAddress = "10.0.0.0";
        int cidr = 16;

        // Act
        List<AzureVNetViewModel> vnets = await _mockAzureService.GetCompatibleVNets(subscriptionId, networkAddress, cidr);

        // Assert
        Assert.Single(vnets);
        Assert.Equal("vnet1", vnets[0].Name);
    }

    [Fact]
    public async Task GetCompatibleVNets_WithNoMatches_ReturnsEmptyList()
    {
        // Arrange
        string subscriptionId = "sub-1";
        string networkAddress = "192.168.1.0"; // No VNets match this exactly
        int cidr = 24;

        // Act
        List<AzureVNetViewModel> vnets = await _mockAzureService.GetCompatibleVNets(subscriptionId, networkAddress, cidr);

        // Assert
        Assert.Empty(vnets);
    }

    [Fact]
    public async Task GetCompatibleSubnets_WithParentSubnet_ReturnsFilteredSubnets()
    {
        // Arrange
        string vnetResourceId = "/subscriptions/sub-1/resourceGroups/test-rg/providers/Microsoft.Network/virtualNetworks/vnet1";
        string networkAddress = "10.0.0.0";
        int cidr = 16;

        // Act
        List<AzureSubnetViewModel> subnets = await _mockAzureService.GetCompatibleSubnets(vnetResourceId, networkAddress, cidr);

        // Assert - only subnet1 and subnet2 should be contained in 10.0.0.0/16, not subnet3
        Assert.Equal(2, subnets.Count);
        Assert.Contains(subnets, s => s.Name == "subnet1");
        Assert.Contains(subnets, s => s.Name == "subnet2");
        Assert.DoesNotContain(subnets, s => s.Name == "subnet3"); // subnet3 should be excluded (172.16.1.0/24)
    }

    [Fact]
    public async Task GetCompatibleSubnets_WithNoMatches_ReturnsEmptyList()
    {
        // Arrange
        string vnetResourceId = "/subscriptions/sub-1/resourceGroups/test-rg/providers/Microsoft.Network/virtualNetworks/vnet1";
        string networkAddress = "192.168.1.0";
        int cidr = 24;

        // Act
        List<AzureSubnetViewModel> subnets = await _mockAzureService.GetCompatibleSubnets(vnetResourceId, networkAddress, cidr);

        // Assert
        Assert.Empty(subnets);
    }
}
