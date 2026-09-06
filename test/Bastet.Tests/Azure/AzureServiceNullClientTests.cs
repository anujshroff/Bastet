using Bastet.Services.Azure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.Azure;

[Collection(AzureFeatureFlagCollection.Name)]
public class AzureServiceNullClientTests
{
    [Fact]
    public async Task GetSubscriptions_WhenNoCredentialCouldBeConstructed_ThrowsInsteadOfReportingEmpty()
    {
        string? prior = Environment.GetEnvironmentVariable("AZURE_TOKEN_CREDENTIALS");
        Environment.SetEnvironmentVariable("AZURE_TOKEN_CREDENTIALS", "bastet-test-invalid-value");
        try
        {
            AzureArmClientProvider provider = new(NullLogger<AzureArmClientProvider>.Instance);
            Assert.Null(provider.Client);

            AzureService service = new(provider, NullLogger<AzureService>.Instance);

            InvalidOperationException ex =
                await Assert.ThrowsAsync<InvalidOperationException>(service.GetSubscriptions);
            Assert.Contains("No Azure credential is available", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZURE_TOKEN_CREDENTIALS", prior);
        }
    }
}
