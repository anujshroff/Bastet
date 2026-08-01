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

    /// <summary>
    /// Invokes the action the way routing does, with the status as a route value.
    /// </summary>
    /// <remarks>
    /// It is deliberately not an action parameter. UseStatusCodePagesWithReExecute re-executes the
    /// same request, body included, and the default composite value provider reads form values before
    /// route values - so a form field named statusCode used to outrank the segment the middleware had
    /// just set, and a caller could relabel their own failed request's status.
    /// </remarks>
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

    /// <summary>
    /// Queues a message the way a redirecting action does, returning the token that identifies it.
    /// </summary>
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

    /// <summary>
    /// A message queued by some other request must not be shown here, and must not be consumed.
    /// </summary>
    /// <remarks>
    /// Round-10 J4. Every request reaching this page used to read the one shared slot, so an
    /// unrelated 4xx - a missing stylesheet, a stale antiforgery token, a second tab's stale row -
    /// printed a diagnostic about a request the browser never made, and the page it belonged to fell
    /// back to the generic text. A re-executed status page carries no token at all.
    /// </remarks>
    [Fact]
    public void HttpStatusCodeHandler_MessageForAnotherRedirect_IsNeitherShownNorConsumed()
    {
        ErrorController controller = CreateController();
        string otherToken = StashMessage(controller, "The subnet with ID 999 could not be found.");

        IActionResult result = Invoke(controller, 404);

        ViewResult view = Assert.IsType<ViewResult>(result);
        ErrorViewModel model = Assert.IsType<ErrorViewModel>(view.Model);
        Assert.Equal("The resource you requested could not be found.", model.ErrorMessage);

        // Still there for the request it was meant for.
        Assert.Equal("The subnet with ID 999 could not be found.",
            ErrorPageMessages.Take(controller.TempData, otherToken));
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

        // Keyed to this redirect rather than dropped in a single shared slot, so a concurrent 4xx
        // elsewhere in the session can neither show it nor consume it.
        string? token = redirect.RouteValues?[ErrorPageMessages.TokenQueryKey]?.ToString();
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Contains("999", ErrorPageMessages.Take(controller.TempData, token) ?? "");
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

        _ = Invoke(controller, statusCode);

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

        _ = Invoke(controller, statusCode);

        Assert.Equal(500, controller.Response.StatusCode);
    }

    /// <summary>
    /// When the route value is missing the status must come from the response the middleware already
    /// set, not from a defaulted zero.
    /// </summary>
    /// <remarks>
    /// Round-10 J3, the leg that <c>[FromRoute]</c> alone would not have closed. A NUL byte in any
    /// form value - or a malformed multipart boundary - makes FormPipeReader throw, and MVC then
    /// abandons binding for *every* source rather than just the form. A bound parameter arrived as 0,
    /// which the out-of-range guard turned into 500 "Status Code: 0": an ordinary client mistake
    /// reported as a server fault. Reading the route directly and falling back to the response status
    /// keeps a 400 a 400.
    /// </remarks>
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
