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

/// <summary>
/// Tests for the logout flow: the caller-supplied returnUrl feeds the OIDC post-logout redirect,
/// and Bastet deployments use arbitrary IdPs (Auth0, Entra, Duende, ...) whose own validation of
/// that redirect varies - so the app itself must reject non-local URLs.
/// </summary>
public class AccountControllerLogoutTests
{
    private const string SignedOutPath = "/Account/SignedOut";

    /// <summary>
    /// A controller under test together with what its sign-out actually did.
    /// </summary>
    /// <remarks>
    /// Production sign-out no longer returns a <c>SignOutResult</c> for the framework to execute
    /// later; it awaits <c>HttpContext.SignOutAsync</c> for each scheme itself. Round 9's I6 pins
    /// read <c>SignOutResult.Properties.RedirectUri</c>, which was the only place the resolved target
    /// was observable - so they now read it off the properties handed to the mocked
    /// <see cref="IAuthenticationService"/> instead. Same assertion, same guarantee, one layer down.
    /// </remarks>
    private sealed class LogoutHarness
    {
        public required AccountController Controller { get; init; }

        /// <summary>Properties passed to the OpenID Connect sign-out, or null if it never ran.</summary>
        public AuthenticationProperties? OidcProperties { get; set; }

        /// <summary>Properties passed to the cookie sign-out, or null if it never ran.</summary>
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

        // SignOutAsync resolves IAuthenticationService from RequestServices.
        //
        // The default permissive mock is what the Production cases need - they legitimately call
        // SignOutAsync and it must be callable. It is also what hid this defect: with SignOutAsync
        // a no-op, the Development tests asserted a RedirectResult the running application could
        // not produce. Development registers only DevAuthScheme, and DevAuthHandler is not an
        // IAuthenticationSignOutHandler, so the real framework throws there. signOutRegistered:false
        // reproduces that, and is used only by the Development cases.
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
                // The IdP is unreachable: OpenIdConnectHandler.SignOutAsync throws while fetching the
                // discovery document.
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

        // Mirror the real IsLocalUrl semantics: a single leading '/', not '//' or '/\'
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

    /// <summary>
    /// A local returnUrl that cannot legally be written as a header value must be dropped, not
    /// carried into the response. Kestrel refuses any non-ASCII character when it writes
    /// Response.Headers.Location, and the exception lands after the auth-cookie deletion has been
    /// queued - so UseExceptionHandler clears the response, the Set-Cookie goes with it, and the user
    /// gets a 500 while staying signed in.
    /// </summary>
    /// <remarks>
    /// Regression for round 9's I6. Url.IsLocalUrl accepts all of these: its character check is
    /// char.IsControl, which is category Cc only, so every non-ASCII character passes. The escapes are
    /// spelled out rather than written literally so they survive diffs and tool round-trips.
    /// </remarks>
    [Theory]
    [InlineData("/caf\u00E9")]                             // e-acute
    [InlineData("/Subnet/Details/3?name=\u00DCbersicht")]   // U-umlaut
    [InlineData("/price\u20AC")]                           // euro sign
    [InlineData("/\u2028next")]                            // line separator; char.IsControl misses it
    public async Task Logout_Production_ReturnUrlKestrelCannotWrite_RedirectsToSignedOutPage(string returnUrl)
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: true);

        IActionResult result = await harness.Controller.Logout(returnUrl);

        _ = Assert.IsType<EmptyResult>(result);
        Assert.Equal(SignedOutPath, harness.OidcProperties?.RedirectUri);
    }

    /// <summary>
    /// An unreachable identity provider must not keep a privileged session alive.
    /// </summary>
    /// <remarks>
    /// Round-10 J6. Sign-out used to return <c>SignOut(...)</c>, so both schemes ran during *result*
    /// execution, after the action had returned. A Production process that started while the IdP was
    /// down has never fetched the discovery document, so OpenIdConnectHandler.SignOutAsync threw
    /// IDX20803 there - and because UseExceptionHandler calls Response.Clear(), every queued
    /// Set-Cookie went with it. The user got a 500, the browser kept its ticket, and the cookie
    /// handler went on validating it locally without ever contacting the IdP, for up to the one-hour
    /// sliding expiry. Every other 500 in that window fails closed; this one failed open.
    /// <para>
    /// The local sign-out is now awaited first and separately, so it survives the remote leg failing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Logout_Production_IdentityProviderUnreachable_StillEndsTheLocalSession()
    {
        LogoutHarness harness = CreateHarness(
            isDevelopment: false,
            oidcSignOutThrows: new InvalidOperationException(
                "IDX20803: Unable to obtain configuration from: '[PII is hidden]'."));

        IActionResult result = await harness.Controller.Logout(returnUrl: null);

        // The user lands on the signed-out page rather than a 500.
        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(SignedOutPath, redirect.Url);

        // And the local session was ended before the remote leg was attempted.
        Assert.True(harness.CookieSignOutRan);
        Assert.Equal(SignedOutPath, harness.CookieProperties?.RedirectUri);
    }

    /// <summary>
    /// The cookie scheme's own LogoutPath is /Account/Logout - this very path - so signing out of it
    /// without a RedirectUri makes HandleSignOutAsync take its redirect branch and write a
    /// self-redirect. Harmless only by accident today, because both exits overwrite Location.
    /// </summary>
    [Fact]
    public async Task Logout_Production_CookieSignOut_CarriesTheRedirectTarget()
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: true);

        _ = await harness.Controller.Logout("/Subnet/Details/5");

        Assert.Equal("/Subnet/Details/5", harness.CookieProperties?.RedirectUri);
    }

    /// <summary>The Development branch reads the same target, so it is fixed by the same guard.</summary>
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
        // Browsing to the signed-out page with a live session must not pretend the user is out.
        AccountController controller = CreateController(isDevelopment: false, authenticated: true);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(controller.SignedOut());
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
    }

    /// <summary>
    /// O13. Logout is [AllowAnonymous], carries no antiforgery token and is reachable by a
    /// cross-site top-level navigation. Running the remote OpenID Connect sign-out for a caller with
    /// no session therefore let an attacker's link end the SSO session for every relying party of
    /// the tenant, on behalf of someone who was not signed in to Bastet at all.
    /// </summary>
    [Fact]
    public async Task Logout_Production_AnonymousCaller_DoesNotEndTheIdentityProviderSession()
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: false);

        IActionResult result = await harness.Controller.Logout(null);

        // Local redirect, and crucially the OIDC leg never ran.
        RedirectResult redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(SignedOutPath, redirect.Url);
        Assert.Null(harness.OidcProperties);
    }

    /// <summary>
    /// The counterpart: a signed-in caller still gets federated sign-out, which is the whole point
    /// of the remote leg.
    /// </summary>
    [Fact]
    public async Task Logout_Production_AuthenticatedCaller_StillEndsTheIdentityProviderSession()
    {
        LogoutHarness harness = CreateHarness(isDevelopment: false, authenticated: true);

        IActionResult result = await harness.Controller.Logout(null);

        _ = Assert.IsType<EmptyResult>(result);
        Assert.NotNull(harness.OidcProperties);
    }

    /// <summary>
    /// O13's other half: the loop that expired every cookie the browser presented is gone, so a
    /// co-hosted application's session cookie is never touched. Only the framework's own sign-out
    /// may emit Set-Cookie.
    /// </summary>
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
