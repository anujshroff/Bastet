using Bastet.Controllers;
using Bastet.Tests.TestHelpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using System.Security.Claims;

namespace Bastet.Tests.Security;

/// <summary>
/// Tests for the logout flow: the caller-supplied returnUrl feeds the OIDC post-logout redirect,
/// and Bastet deployments use arbitrary IdPs (Auth0, Entra, Duende, ...) whose own validation of
/// that redirect varies - so the app itself must reject non-local URLs.
/// </summary>
public class AccountControllerLogoutTests
{
    private const string SignedOutPath = "/Account/SignedOut";

    private static AccountController CreateController(
        bool isDevelopment, bool authenticated = false, bool signOutRegistered = true)
    {
        Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment> environment = new();
        environment.Setup(e => e.EnvironmentName).Returns(isDevelopment ? "Development" : "Production");

        AccountController controller = new(environment.Object, ControllerTestHelper.CreateMockUserContextService());
        ControllerTestHelper.SetupController(controller);

        // SignOutAsync resolves IAuthenticationService from RequestServices.
        //
        // The default permissive mock is what the Production cases need - they legitimately call
        // SignOutAsync and it must be callable. It is also what hid this defect: with SignOutAsync
        // a no-op, the Development tests asserted a RedirectResult the running application could
        // not produce. Development registers only DevAuthScheme, and DevAuthHandler is not an
        // IAuthenticationSignOutHandler, so the real framework throws there. signOutRegistered:false
        // reproduces that, and is used only by the Development cases.
        Mock<IAuthenticationService> authService = new();
        if (!signOutRegistered)
        {
            authService
                .Setup(a => a.SignOutAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()))
                .ThrowsAsync(new InvalidOperationException(
                    "No sign-out authentication handlers are registered."));
        }

        Mock<IServiceProvider> services = new();
        services.Setup(s => s.GetService(typeof(IAuthenticationService))).Returns(authService.Object);
        controller.HttpContext.RequestServices = services.Object;

        controller.HttpContext.User = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], "test"))
            : new ClaimsPrincipal(new ClaimsIdentity());

        // Mirror the real IsLocalUrl semantics: a single leading '/', not '//' or '/\'
        Mock<IUrlHelper> urlHelper = new();
        urlHelper.Setup(u => u.IsLocalUrl(It.IsAny<string?>()))
            .Returns((string? u) => !string.IsNullOrEmpty(u)
                && u[0] == '/'
                && (u.Length == 1 || (u[1] != '/' && u[1] != '\\')));
        urlHelper.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns(SignedOutPath);
        controller.Url = urlHelper.Object;

        return controller;
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData(@"/\evil.example")]
    [InlineData(null)]
    public async Task Logout_Production_NonLocalOrMissingReturnUrl_RedirectsToSignedOutPage(string? returnUrl)
    {
        AccountController controller = CreateController(isDevelopment: false);

        IActionResult result = await controller.Logout(returnUrl);

        SignOutResult signOut = Assert.IsType<SignOutResult>(result);
        Assert.Equal(SignedOutPath, signOut.Properties?.RedirectUri);
    }

    [Fact]
    public async Task Logout_Production_LocalReturnUrl_IsPreserved()
    {
        AccountController controller = CreateController(isDevelopment: false);

        IActionResult result = await controller.Logout("/Subnet/Details/5");

        SignOutResult signOut = Assert.IsType<SignOutResult>(result);
        Assert.Equal("/Subnet/Details/5", signOut.Properties?.RedirectUri);
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
        // Browsing to the signed-out page with a live session must not pretend the user is out.
        AccountController controller = CreateController(isDevelopment: false, authenticated: true);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(controller.SignedOut());
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
    }
}
