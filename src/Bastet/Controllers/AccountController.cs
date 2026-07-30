using Bastet.Services;
using Bastet.Services.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public class AccountController(IWebHostEnvironment environment, IUserContextService userContextService) : Controller
{
    /// <summary>
    /// Anonymous: this is the configured AccessDeniedPath, so it must be reachable by a user who
    /// has just failed an authorization check.
    /// </summary>
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    /// <summary>
    /// Anonymous so that signing out still works once the session is already gone.
    /// </summary>
    /// <remarks>
    /// Deliberately left as a GET without an antiforgery token. That allows logout CSRF, which is
    /// accepted here: the worst outcome is an unwanted sign-out, and consumers of this open-source
    /// project may have external logout links (IdP, reverse proxy, bookmarks) pointing at GET
    /// /Account/Logout that would break with a 405. The authorization test exempts it explicitly.
    /// </remarks>
    [AllowAnonymous]
    public async Task<IActionResult> Logout(string? returnUrl = null)
    {
        // Never trust a caller-supplied absolute URL: it becomes the OIDC post-logout redirect, and
        // how strictly that is validated varies by IdP product and deployment. Local paths only;
        // everything else lands on the anonymous SignedOut page instead of an auto re-challenge.
        //
        // IsLocalUrl is not enough on its own. Its character check is char.IsControl - category Cc
        // only - so every non-ASCII character passes, and this value is written straight into the
        // Location header. Kestrel refuses to write one ("Invalid non-ASCII or control character in
        // header"), and it throws *after* the auth-cookie deletion has been queued: the exception
        // handler clears the response, the Set-Cookie is discarded with it, and sign-out answers 500
        // with the session still alive. HttpHeaderValue.IsValid is the project's existing test for
        // exactly this Kestrel rule. Nothing the app itself generates is affected - every URL it
        // builds is ASCII.
        string target = !string.IsNullOrEmpty(returnUrl)
                && Url.IsLocalUrl(returnUrl)
                && HttpHeaderValue.IsValid(returnUrl)
            ? returnUrl
            : Url.Action(nameof(SignedOut), "Account") ?? "/Account/SignedOut";

        // Clear all cookies
        foreach (string cookie in Request.Cookies.Keys)
        {
            Response.Cookies.Delete(cookie);
        }

        // No unconditional SignOutAsync here. Development registers a single scheme, DevAuthScheme,
        // and DevAuthHandler is not an IAuthenticationSignOutHandler - so signing out of "Cookies"
        // threw "No sign-out authentication handlers are registered", which the developer exception
        // page returned as the response body, and made the Development branch below unreachable.
        // Production is unaffected: the SignOutResult returned there already lists the cookie
        // scheme, so the sign-out still happens, once instead of twice.

        // If we're in production, also sign out from the identity provider
        if (!environment.IsDevelopment())
        {
            // Redirect to OIDC provider for logout
            return SignOut(
                new AuthenticationProperties { RedirectUri = target },
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme);
        }

        // In development, just redirect to the specified URL or the signed-out page
        return Redirect(target);
    }

    /// <summary>
    /// Anonymous: this is the post-logout landing page, shown precisely when the user has no
    /// session. Without it, logout would bounce straight into a fresh OIDC challenge - a login
    /// prompt at best, a silent re-login under SSO at worst.
    /// </summary>
    [AllowAnonymous]
    public IActionResult SignedOut() =>
        User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Home") : View();

    [Authorize]
    public IActionResult Roles()
    {
        // Get the current username
        ViewData["Username"] = userContextService.GetCurrentUsername();

        // Get the user's Bastet roles
        ViewData["BastetRoles"] = userContextService.GetUserBastetRoles();

        // Get the user's token roles
        ViewData["TokenRoles"] = userContextService.GetUserTokenRoles();

        return View();
    }
}
