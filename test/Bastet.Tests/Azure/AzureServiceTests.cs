using Bastet.Services.Azure;
using Bastet.Models.ViewModels;
using Bastet.Tests.TestHelpers;

namespace Bastet.Tests.Azure;

public class AzureServiceTests
{
    private readonly MockAzureService _mockAzureService;

    public AzureServiceTests()
    {

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
            new() { Name = "subnet1", AddressPrefix = "10.0.0.0/24" },
            new() { Name = "subnet2", AddressPrefix = "10.0.1.0/24" },
            new() { Name = "subnet3", AddressPrefix = "172.16.1.0/24" }
        ];

        _mockAzureService = new MockAzureService(true, subscriptions, vnets, subnets);
    }

    [Fact]
    public async Task CheckCredential_WithValidCredentialAndSubscriptions_ReturnsValid()
    {

        CredentialCheckResult result = await _mockAzureService.CheckCredential();

        Assert.Equal(CredentialCheckResult.Valid, result);
    }

    [Fact]
    public async Task CheckCredential_WithInvalidCredential_ReturnsFailed()
    {

        MockAzureService service = new(false);

        CredentialCheckResult result = await service.CheckCredential();

        Assert.Equal(CredentialCheckResult.Failed, result);
    }

    [Fact]
    public async Task CheckCredential_AuthenticatedButNoVisibleSubscriptions_IsNotReportedAsAFailure()
    {

        MockAzureService service = new(true);

        CredentialCheckResult result = await service.CheckCredential();

        Assert.Equal(CredentialCheckResult.NoVisibleSubscriptions, result);
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsAllSubscriptions()
    {

        List<AzureSubscriptionViewModel> subscriptions = await _mockAzureService.GetSubscriptions();

        Assert.Equal(2, subscriptions.Count);
        Assert.Contains(subscriptions, s => s.SubscriptionId == "sub-1");
        Assert.Contains(subscriptions, s => s.SubscriptionId == "sub-2");
    }




}
