using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models.ViewModels;
using Bastet.Services.Azure;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.Azure;

[Collection(AzureFeatureFlagCollection.Name)]
public class AzureCredentialBannerTests : IDisposable
{
    private const string FailedMessage =
        "Could not authenticate with Azure. Check the credentials and that Azure is reachable from this host.";

    private const string NoSubscriptionsMessage =
        "Signed in to Azure, but this credential cannot see any subscriptions. Grant it access to a subscription and reload this page.";

    private readonly BastetDbContext _context;

    public AzureCredentialBannerTests()
    {
        DbContextOptions<BastetDbContext> options = new DbContextOptionsBuilder<BastetDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new BastetDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", "true");
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        Environment.SetEnvironmentVariable("BASTET_AZURE_IMPORT", null);
        GC.SuppressFinalize(this);
    }

    private AzureController CreateController(MockAzureService azureService) =>
        new(azureService, new AzureSubnetSnapshotService(_context), NullLogger<AzureController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static List<string> PageErrors(AzureController controller) =>
        [.. controller.ModelState[""]?.Errors.Select(e => e.ErrorMessage) ?? []];

    private static MockAzureService FailedCredentialService() => new(false);

    private static MockAzureService NoVisibleSubscriptionsService() => new(true);

    private static MockAzureService ValidCredentialService() => new(true,
        [new AzureSubscriptionViewModel { SubscriptionId = "00000000-0000-0000-0000-000000000001", DisplayName = "Test" }]);

    [Fact]
    public async Task BulkImport_WhenCredentialCheckFails_NamesBothPossibleCauses()
    {
        AzureController controller = CreateController(FailedCredentialService());

        IActionResult result = await controller.BulkImport();

        Assert.IsType<ViewResult>(result);
        string error = Assert.Single(PageErrors(controller));
        Assert.Equal(FailedMessage, error);
        Assert.DoesNotContain("Please check your credentials", error);
    }

    [Fact]
    public async Task BulkImport_WhenCredentialSeesNoSubscriptions_TellsTheOperatorToGrantAccess()
    {
        AzureController controller = CreateController(NoVisibleSubscriptionsService());

        IActionResult result = await controller.BulkImport();

        Assert.IsType<ViewResult>(result);
        string error = Assert.Single(PageErrors(controller));
        Assert.Equal(NoSubscriptionsMessage, error);
    }

    [Fact]
    public async Task BulkImport_WhenCredentialIsValid_ShowsNoCredentialBanner()
    {
        AzureController controller = CreateController(ValidCredentialService());

        IActionResult result = await controller.BulkImport();

        Assert.IsType<ViewResult>(result);
        Assert.Empty(PageErrors(controller));
    }

    [Fact]
    public async Task Reconcile_WhenCredentialCheckFails_NamesBothPossibleCauses()
    {
        AzureController controller = CreateController(FailedCredentialService());

        IActionResult result = await controller.Reconcile();

        Assert.IsType<ViewResult>(result);
        string error = Assert.Single(PageErrors(controller));
        Assert.Equal(FailedMessage, error);
        Assert.DoesNotContain("Please check your credentials", error);
    }

    [Fact]
    public async Task Reconcile_WhenCredentialSeesNoSubscriptions_TellsTheOperatorToGrantAccess()
    {
        AzureController controller = CreateController(NoVisibleSubscriptionsService());

        IActionResult result = await controller.Reconcile();

        Assert.IsType<ViewResult>(result);
        string error = Assert.Single(PageErrors(controller));
        Assert.Equal(NoSubscriptionsMessage, error);
    }

    [Fact]
    public async Task Reconcile_WhenCredentialIsValid_ShowsNoCredentialBanner()
    {
        AzureController controller = CreateController(ValidCredentialService());

        IActionResult result = await controller.Reconcile();

        Assert.IsType<ViewResult>(result);
        Assert.Empty(PageErrors(controller));
    }
}
