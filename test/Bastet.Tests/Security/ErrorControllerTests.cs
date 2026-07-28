using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.Security;

/// <summary>
/// The error page is anonymous, so it must not reflect attacker-supplied query text (B6). The
/// message now comes only from TempData set by the redirecting action; the action no longer has an
/// errorMessage/errorCode parameter to bind, so a crafted /Error/400?errorMessage=... shows the
/// per-status default instead.
/// </summary>
public class ErrorControllerTests
{
    private static ErrorController CreateController()
    {
        ErrorController controller = new();
        ControllerTestHelper.SetupController(controller);
        return controller;
    }

    [Fact]
    public void HttpStatusCodeHandler_NoTempData_UsesPerStatusDefault()
    {
        ErrorController controller = CreateController();

        IActionResult result = controller.HttpStatusCodeHandler(404);

        ViewResult view = Assert.IsType<ViewResult>(result);
        Assert.Equal("NotFound", view.ViewName);
        ErrorViewModel model = Assert.IsType<ErrorViewModel>(view.Model);
        Assert.Equal("The resource you requested could not be found.", model.ErrorMessage);
    }

    [Fact]
    public void HttpStatusCodeHandler_TempDataMessage_IsShown()
    {
        ErrorController controller = CreateController();
        controller.TempData["ErrorPageMessage"] = "Subnet with ID 5 could not be found.";

        IActionResult result = controller.HttpStatusCodeHandler(404);

        ViewResult view = Assert.IsType<ViewResult>(result);
        ErrorViewModel model = Assert.IsType<ErrorViewModel>(view.Model);
        Assert.Equal("Subnet with ID 5 could not be found.", model.ErrorMessage);
    }

    [Fact]
    public async Task Caller_PutsItsMessageInTempData_NotTheQueryString()
    {
        // Representative of the 11 redirecting sites: the custom message travels via TempData, so
        // it survives the move while no longer being forgeable through the URL.
        using BastetDbContext context = TestDbContextFactory.CreateDbContext();
        IIpUtilityService ip = new IpUtilityService();
        SubnetController controller = new(
            context, ip, new SubnetValidationService(ip), new HostIpValidationService(ip, context),
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(), NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(controller);

        IActionResult result = await controller.Details(999); // no such subnet

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("HttpStatusCodeHandler", redirect.ActionName);
        Assert.Equal("Error", redirect.ControllerName);
        Assert.Equal(404, redirect.RouteValues?["statusCode"]);
        Assert.Contains("999", controller.TempData["ErrorPageMessage"]?.ToString() ?? "");
    }

    /// <summary>
    /// The status-code-pages middleware sets the response status itself before re-executing, which
    /// is why a route that matched nothing really did answer 404. A controller that *redirects*
    /// here does not: the browser issues a fresh GET, this action returns a view, and the response
    /// went out as HTTP 200 with "Resource Not Found" rendered in it. Eleven redirect sites across
    /// SubnetController.Read/Edit/Delete and AzureController reached the page that way, so every
    /// missing subnet and every feature-flag refusal reported success to anything reading the
    /// status rather than the page.
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(500)]
    public void HttpStatusCodeHandler_SetsTheResponseStatus(int statusCode)
    {
        ErrorController controller = CreateController();

        _ = controller.HttpStatusCodeHandler(statusCode);

        Assert.Equal(statusCode, controller.Response.StatusCode);
    }

    /// <summary>
    /// The route segment is caller-controlled, so a nonsense or success-range value must not become
    /// the response status - /Error/200 answering 200 would be the same defect wearing a hat.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(0)]
    [InlineData(99999)]
    public void HttpStatusCodeHandler_OutOfRangeStatus_FallsBackTo500(int statusCode)
    {
        ErrorController controller = CreateController();

        _ = controller.HttpStatusCodeHandler(statusCode);

        Assert.Equal(500, controller.Response.StatusCode);
    }

    [Fact]
    public void Error_SetsA500Status()
    {
        ErrorController controller = CreateController();

        _ = controller.Error();

        Assert.Equal(500, controller.Response.StatusCode);
    }
}
