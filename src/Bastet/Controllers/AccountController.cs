using Bastet.Services;
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
    public IActionResult AccessDenied(string returnUrl = "/")
    {
        // Store the return URL in ViewData to potentially use it in the view
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

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
        string target = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Action(nameof(SignedOut), "Account") ?? "/Account/SignedOut";

        // Clear all cookies
        foreach (string cookie in Request.Cookies.Keys)
        {
            Response.Cookies.Delete(cookie);
        }

        // Sign out of the local authentication
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

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
