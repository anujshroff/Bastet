using Bastet.Controllers;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Reflection;
using System.Security.Claims;

namespace Bastet.Tests.Security;

public class AccountControllerLogoutTests
{
    private const string SignedOutPath = "/Account/SignedOut";

    private sealed class LogoutHarness
    {
        public required AccountController Controller { get; init; }

        public AuthenticationProperties? OidcProperties { get; set; }

        public AuthenticationProperties? CookieProperties { get; set; }

        public bool CookieSignOutRan => CookieProperties is not null;
    }

    private static AccountController CreateController(
        bool isDevelopment, bool authenticated = false, bool signOutRegistered = true) =>
        CreateHarness(isDevelopment, authenticated, signOutRegistered).Controller;

    private static LogoutHarness CreateHarness(
        bool isDevelopment,
        bool authenticated = false,
        bool signOutRegistered = true,
        Exception? oidcSignOutThrows = null)
    {
        Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment> environment = new();
        environment.Setup(e => e.EnvironmentName).Returns(isDevelopment ? "Development" : "Production");

        AccountController controller = new(
            environment.Object,
            ControllerTestHelper.CreateMockUserContextService(),
            NullLogger<AccountController>.Instance);
        ControllerTestHelper.SetupController(controller);

        LogoutHarness harness = new() { Controller = controller };

        Mock<IAuthenticationService> authService = new();
        if (!signOutRegistered)
        {
            authService
                .Setup(a => a.SignOutAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()))
                .ThrowsAsync(new InvalidOperationException(
                    "No sign-out authentication handlers are registered."));
        }
        else
        {
            authService
                .Setup(a => a.SignOutAsync(
                    It.IsAny<HttpContext>(),
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    It.IsAny<AuthenticationProperties?>()))
                .Callback<HttpContext, string?, AuthenticationProperties?>(
                    (_, _, properties) => harness.CookieProperties = properties)
                .Returns(Task.CompletedTask);

            Moq.Language.Flow.ISetup<IAuthenticationService, Task> oidc = authService
                .Setup(a => a.SignOutAsync(
                    It.IsAny<HttpContext>(),
                    OpenIdConnectDefaults.AuthenticationScheme,
                    It.IsAny<AuthenticationProperties?>()));

            if (oidcSignOutThrows is null)
            {
                oidc.Callback<HttpContext, string?, AuthenticationProperties?>(
                        (_, _, properties) => harness.OidcProperties = properties)
                    .Returns(Task.CompletedTask);
            }
            else
            {

                oidc.Callback<HttpContext, string?, AuthenticationProperties?>(
                        (_, _, properties) => harness.OidcProperties = properties)
                    .ThrowsAsync(oidcSignOutThrows);
            }
        }

        Mock<IServiceProvider> services = new();
        services.Setup(s => s.GetService(typeof(IAuthenticationService))).Returns(authService.Object);
        controller.HttpContext.RequestServices = services.Object;

        controller.HttpContext.User = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], "test"))
            : new ClaimsPrincipal(new ClaimsIdentity());

        Mock<IUrlHelper> urlHelper = new();
        urlHelper.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns(SignedOutPath);
        controller.Url = urlHelper.Object;

        return harness;
    }

    private static void AssertRedirectsToTheSignedOutPage(IActionResult result)
    {
        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.SignedOut), redirect.ActionName);
        Assert.Null(redirect.ControllerName);
    }

    [Fact]
    public void Logout_TakesNoReturnTarget()
    {
        MethodInfo logout = typeof(AccountController).GetMethod(nameof(AccountController.Logout))!;

        Assert.Empty(logout.GetParameters());
    }

    [Fact]
    public async Task Logout_Production_AuthenticatedCaller_EndsTheIdentityProviderSession_ReturningToTheSignedOutPage()
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: true);

        IActionResult result = await harness.Controller.Logout();

        _ = Assert.IsType<EmptyResult>(result);
        Assert.Equal(SignedOutPath, harness.OidcProperties?.RedirectUri);
    }

    [Fact]
    public async Task Logout_Production_CookieSignOut_ReturnsToTheSignedOutPage()
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: true);

        _ = await harness.Controller.Logout();

        Assert.Equal(SignedOutPath, harness.CookieProperties?.RedirectUri);
    }

    [Fact]
    public async Task Logout_Production_IdentityProviderUnreachable_StillEndsTheLocalSession()
    {
        LogoutHarness harness = CreateHarness(
            isDevelopment: false,
            authenticated: true,
            oidcSignOutThrows: new InvalidOperationException(
                "IDX20803: Unable to obtain configuration from: '[PII is hidden]'."));

        IActionResult result = await harness.Controller.Logout();

        AssertRedirectsToTheSignedOutPage(result);
        Assert.NotNull(harness.OidcProperties);
        Assert.True(harness.CookieSignOutRan);
        Assert.Equal(SignedOutPath, harness.CookieProperties?.RedirectUri);
    }

    [Fact]
    public async Task Logout_Production_AnonymousCaller_DoesNotEndTheIdentityProviderSession()
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: false);

        IActionResult result = await harness.Controller.Logout();

        AssertRedirectsToTheSignedOutPage(result);
        Assert.True(harness.CookieSignOutRan);
        Assert.Null(harness.OidcProperties);
    }

    [Fact]
    public async Task Logout_Development_RedirectsToTheSignedOutPage_WithoutSigningOut()
    {
        AccountController controller = CreateController(isDevelopment: true, signOutRegistered: false);

        IActionResult result = await controller.Logout();

        AssertRedirectsToTheSignedOutPage(result);
    }

    [Fact]
    public void SignedOut_Anonymous_ShowsThePage()
    {
        AccountController controller = CreateController(isDevelopment: false);

        Assert.IsType<ViewResult>(controller.SignedOut());
    }

    [Fact]
    public void SignedOut_StillAuthenticated_RedirectsHome()
    {

        AccountController controller = CreateController(isDevelopment: false, authenticated: true);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(controller.SignedOut());
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
    }

    [Fact]
    public async Task Logout_DoesNotExpireCookiesBastetDidNotIssue()
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: true);
        harness.Controller.HttpContext.Request.Headers.Cookie =
            "coapp_session=abc; grafana_session=def";

        _ = await harness.Controller.Logout();

        string setCookie = string.Join("\n", harness.Controller.HttpContext.Response.Headers.SetCookie!);
        Assert.DoesNotContain("coapp_session", setCookie);
        Assert.DoesNotContain("grafana_session", setCookie);
    }
}
