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
        Assert.Null(model.ErrorCode);
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
}
