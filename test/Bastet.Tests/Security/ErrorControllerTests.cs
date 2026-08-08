using Bastet.Controllers;
using Bastet.Data;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Validation;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;

namespace Bastet.Tests.Security;

public class ErrorControllerTests
{
    private static ErrorController CreateController()
    {
        ErrorController controller = new();
        ControllerTestHelper.SetupController(controller);
        return controller;
    }

    private static IActionResult Invoke(ErrorController controller, int statusCode, string? token = null)
    {
        controller.ControllerContext.RouteData = new RouteData();
        controller.ControllerContext.RouteData.Values["statusCode"] =
            statusCode.ToString(CultureInfo.InvariantCulture);
        controller.Request.QueryString = token is null
            ? QueryString.Empty
            : QueryString.Create(ErrorPageMessages.TokenQueryKey, token);
        return controller.HttpStatusCodeHandler();
    }

    private static string StashMessage(ErrorController controller, string message) =>
        controller.RedirectToErrorPage(404, message)
            .RouteValues?[ErrorPageMessages.TokenQueryKey]?.ToString() ?? string.Empty;

    [Fact]
    public void HttpStatusCodeHandler_NoTempData_UsesPerStatusDefault()
    {
        ErrorController controller = CreateController();

        IActionResult result = Invoke(controller, 404);

        ViewResult view = Assert.IsType<ViewResult>(result);
        Assert.Equal("NotFound", view.ViewName);
        ErrorViewModel model = Assert.IsType<ErrorViewModel>(view.Model);
        Assert.Equal("The resource you requested could not be found.", model.ErrorMessage);
    }

    [Fact]
    public void HttpStatusCodeHandler_MessageForThisRedirect_IsShown()
    {
        ErrorController controller = CreateController();
        string token = StashMessage(controller, "Subnet with ID 5 could not be found.");

        IActionResult result = Invoke(controller, 404, token);

        ViewResult view = Assert.IsType<ViewResult>(result);
        ErrorViewModel model = Assert.IsType<ErrorViewModel>(view.Model);
        Assert.Equal("Subnet with ID 5 could not be found.", model.ErrorMessage);
    }

    [Fact]
    public void HttpStatusCodeHandler_MessageForAnotherRedirect_IsNeitherShownNorConsumed()
    {
        ErrorController controller = CreateController();
        string otherToken = StashMessage(controller, "The subnet with ID 999 could not be found.");

        IActionResult result = Invoke(controller, 404);

        ViewResult view = Assert.IsType<ViewResult>(result);
        ErrorViewModel model = Assert.IsType<ErrorViewModel>(view.Model);
        Assert.Equal("The resource you requested could not be found.", model.ErrorMessage);

        Assert.Equal("The subnet with ID 999 could not be found.",
            ErrorPageMessages.Take(controller.TempData, otherToken));
    }

    [Fact]
    public async Task Caller_PutsItsMessageInTempData_NotTheQueryString()
    {

        using BastetDbContext context = TestDbContextFactory.CreateDbContext();
        IIpUtilityService ip = new IpUtilityService();
        SubnetController controller = new(
            context, ip, new SubnetValidationService(ip), new HostIpValidationService(ip, context),
            ControllerTestHelper.CreateMockUserContextService(),
            ControllerTestHelper.CreateMockSubnetLockingService(), NullLogger<SubnetController>.Instance);
        ControllerTestHelper.SetupController(controller);

        IActionResult result = await controller.Details(999);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("HttpStatusCodeHandler", redirect.ActionName);
        Assert.Equal("Error", redirect.ControllerName);
        Assert.Equal(404, redirect.RouteValues?["statusCode"]);

        string? token = redirect.RouteValues?[ErrorPageMessages.TokenQueryKey]?.ToString();
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Contains("999", ErrorPageMessages.Take(controller.TempData, token) ?? "");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(500)]
    public void HttpStatusCodeHandler_SetsTheResponseStatus(int statusCode)
    {
        ErrorController controller = CreateController();

        _ = Invoke(controller, statusCode);

        Assert.Equal(statusCode, controller.Response.StatusCode);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(0)]
    [InlineData(99999)]
    public void HttpStatusCodeHandler_OutOfRangeStatus_FallsBackTo500(int statusCode)
    {
        ErrorController controller = CreateController();

        _ = Invoke(controller, statusCode);

        Assert.Equal(500, controller.Response.StatusCode);
    }

    [Fact]
    public void HttpStatusCodeHandler_NoRouteValue_UsesTheStatusAlreadyOnTheResponse()
    {
        ErrorController controller = CreateController();
        controller.ControllerContext.RouteData = new RouteData();
        controller.Response.StatusCode = 400;

        IActionResult result = controller.HttpStatusCodeHandler();

        ViewResult view = Assert.IsType<ViewResult>(result);
        Assert.Equal("BadRequest", view.ViewName);
        Assert.Equal(400, controller.Response.StatusCode);
        ErrorViewModel model = Assert.IsType<ErrorViewModel>(view.Model);
        Assert.Equal(400, model.StatusCode);
    }

    [Fact]
    public void Error_SetsA500Status()
    {
        ErrorController controller = CreateController();

        _ = controller.Error();

        Assert.Equal(500, controller.Response.StatusCode);
    }
}
