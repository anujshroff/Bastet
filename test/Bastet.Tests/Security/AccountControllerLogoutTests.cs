using Bastet.Controllers;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
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
        urlHelper.Setup(u => u.IsLocalUrl(It.IsAny<string?>()))
            .Returns((string? u) => !string.IsNullOrEmpty(u)
                && u[0] == '/'
                && (u.Length == 1 || (u[1] != '/' && u[1] != '\\')));
        urlHelper.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns(SignedOutPath);
        controller.Url = urlHelper.Object;

        return harness;
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData(@"/\evil.example")]
    [InlineData(null)]
    public async Task Logout_Production_NonLocalOrMissingReturnUrl_RedirectsToSignedOutPage(string? returnUrl)
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: true);

        IActionResult result = await harness.Controller.Logout(returnUrl);

        _ = Assert.IsType<EmptyResult>(result);
        Assert.Equal(SignedOutPath, harness.OidcProperties?.RedirectUri);
    }

    [Fact]
    public async Task Logout_Production_LocalReturnUrl_IsPreserved()
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: true);

        IActionResult result = await harness.Controller.Logout("/Subnet/Details/5");

        _ = Assert.IsType<EmptyResult>(result);
        Assert.Equal("/Subnet/Details/5", harness.OidcProperties?.RedirectUri);
    }

    [Theory]
    [InlineData("/caf\u00E9")]
    [InlineData("/Subnet/Details/3?name=\u00DCbersicht")]
    [InlineData("/price\u20AC")]
    [InlineData("/\u2028next")]
    public async Task Logout_Production_ReturnUrlKestrelCannotWrite_RedirectsToSignedOutPage(string returnUrl)
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: true);

        IActionResult result = await harness.Controller.Logout(returnUrl);

        _ = Assert.IsType<EmptyResult>(result);
        Assert.Equal(SignedOutPath, harness.OidcProperties?.RedirectUri);
    }

    [Fact]
    public async Task Logout_Production_IdentityProviderUnreachable_StillEndsTheLocalSession()
    {
        LogoutHarness harness = CreateHarness(
            isDevelopment: false,
            oidcSignOutThrows: new InvalidOperationException(
                "IDX20803: Unable to obtain configuration from: '[PII is hidden]'."));

        IActionResult result = await harness.Controller.Logout(returnUrl: null);

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(SignedOutPath, redirect.Url);

        Assert.True(harness.CookieSignOutRan);
        Assert.Equal(SignedOutPath, harness.CookieProperties?.RedirectUri);
    }

    [Fact]
    public async Task Logout_Production_CookieSignOut_CarriesTheRedirectTarget()
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: true);

        _ = await harness.Controller.Logout("/Subnet/Details/5");

        Assert.Equal("/Subnet/Details/5", harness.CookieProperties?.RedirectUri);
    }

    [Fact]
    public async Task Logout_Development_ReturnUrlKestrelCannotWrite_RedirectsToSignedOutPage()
    {
        AccountController controller = CreateController(isDevelopment: true, signOutRegistered: false);

        IActionResult result = await controller.Logout("/caf\u00E9");

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(SignedOutPath, redirect.Url);
    }

    [Fact]
    public async Task Logout_Development_NonLocalReturnUrl_RedirectsToSignedOutPage()
    {
        AccountController controller = CreateController(isDevelopment: true, signOutRegistered: false);

        IActionResult result = await controller.Logout("https://evil.example");

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(SignedOutPath, redirect.Url);
    }

    [Fact]
    public async Task Logout_Development_LocalReturnUrl_IsPreserved()
    {
        AccountController controller = CreateController(isDevelopment: true, signOutRegistered: false);

        IActionResult result = await controller.Logout("/Subnet/Details/5");

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Subnet/Details/5", redirect.Url);
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
    public async Task Logout_Production_AnonymousCaller_DoesNotEndTheIdentityProviderSession()
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: false);

        IActionResult result = await harness.Controller.Logout(null);

        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(SignedOutPath, redirect.Url);
        Assert.Null(harness.OidcProperties);
    }

    [Fact]
    public async Task Logout_Production_AuthenticatedCaller_StillEndsTheIdentityProviderSession()
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: true);

        IActionResult result = await harness.Controller.Logout(null);

        _ = Assert.IsType<EmptyResult>(result);
        Assert.NotNull(harness.OidcProperties);
    }

    [Fact]
    public async Task Logout_DoesNotExpireCookiesBastetDidNotIssue()
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: true);
        harness.Controller.HttpContext.Request.Headers.Cookie =
            "coapp_session=abc; grafana_session=def";

        _ = await harness.Controller.Logout(null);

        string setCookie = string.Join("\n", harness.Controller.HttpContext.Response.Headers.SetCookie!);
        Assert.DoesNotContain("coapp_session", setCookie);
        Assert.DoesNotContain("grafana_session", setCookie);
    }
}
