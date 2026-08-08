using Bastet.Services;
using Bastet.Services.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public class AccountController(
    IWebHostEnvironment environment,
    IUserContextService userContextService,
    ILogger<AccountController> logger) : Controller
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

        // Drop any queued TempData before the environment branch, so it happens on all three exits
        // - including the Production OIDC branch that returns EmptyResult after the handler
        // redirects. Clear() forces the lazy Load(), so the globally registered SaveTempDataFilter
        // reaches the provider with an empty dictionary and the provider deletes its own cookie
        // using its own name, path and SameSite options. Response.Cookies.Delete with a hard-coded
        // name would emit a Set-Cookie the browser may not match.
        //
        // ASP.NET Core removes a TempData entry only on READ. An operator who posts an edit and does
        // not follow the redirect - closes the tab, hits Stop, or lets the 2000ms setTimeout on the
        // reconcile screen navigate late - leaves the entry unread, and it then rendered to whoever
        // signed in next on that browser: a green banner naming a subnet they never touched, in the
        // worst case asserting a completed destructive reconcile delete.
        TempData.Clear();

        // No cookie loop here. It walked Request.Cookies.Keys - every cookie the BROWSER sent,
        // including ones Bastet never issued - and expired each one. Since cookies ignore port
        // (RFC 6265 gives no port isolation), any other application sharing this hostname had its
        // session cookie destroyed by an anonymous, tokenless, cross-site-navigable GET. That is a
        // write, which is exactly what this endpoint's own remarks claim it does not perform.
        //
        // Removing the loop was right, but its justification was wrong about one cookie and that is
        // what P7 measured: the TempData cookie is NOT "re-minted on the next request", it persists
        // until read, and the loop had been the only thing deleting it. Hence the explicit
        // TempData.Clear() above, which names only the cookie Bastet's own provider issued and never
        // iterates Request.Cookies. SignOutAsync on the cookie scheme below removes the auth ticket
        // cookie AND its C1..Cn chunks through ChunkingCookieManager. The antiforgery cookie holds
        // no session state and is re-minted on the next request. In Development there is no cookie
        // scheme registered at all, so the loop was dead weight there too.

        // No unconditional SignOutAsync here. Development registers a single scheme, DevAuthScheme,
        // and DevAuthHandler is not an IAuthenticationSignOutHandler - so signing out of "Cookies"
        // threw "No sign-out authentication handlers are registered", which the developer exception
        // page returned as the response body, and made the Development branch below unreachable.
        // Production is unaffected: the SignOutResult returned there already lists the cookie
        // scheme, so the sign-out still happens, once instead of twice.

        // If we're in production, also sign out from the identity provider
        if (!environment.IsDevelopment())
        {
            // The local session is ended first, and separately, so it does not depend on a remote
            // round trip succeeding. Returning SignOut(...) performed both legs during *result*
            // execution, after the action had returned: when the OIDC handler could not fetch the
            // discovery document it threw IDX20803 there, UseExceptionHandler called Response.Clear()
            // - taking every queued Set-Cookie with it - and sign-out answered 500 with the session
            // still alive. Every other failure in that window fails closed; that one failed open.
            //
            // RedirectUri is passed to the cookie leg too. CookieAuthenticationDefaults.LogoutPath is
            // /Account/Logout, which is this very path, so without it HandleSignOutAsync takes its
            // redirect branch with a null RedirectUri and writes a self-redirect - harmless today
            // only because both exits below overwrite Location.
            AuthenticationProperties properties = new() { RedirectUri = target };

            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, properties);

            // Only end the IdP's session for a caller who actually has one here. This action is
            // [AllowAnonymous], carries no antiforgery token and is reachable by a cross-site
            // top-level navigation, so without this an attacker's link ended the SSO session for
            // every relying party of the tenant on behalf of a visitor who was not even signed in
            // to Bastet. Signed-in callers are unaffected: the remote leg still runs for them, which
            // is the whole point of federated sign-out.
            if (User.Identity?.IsAuthenticated != true)
            {
                return Redirect(target);
            }

            try
            {
                await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);

                // The handler has written the redirect to the IdP's end-session endpoint itself.
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                // The IdP being unreachable must not keep a privileged session alive. The local
                // sign-out above has already happened and its Set-Cookie is not at risk, because
                // nothing here throws out of result execution.
                logger.LogWarning(ex,
                    "Signing out of the identity provider failed; the local session was still ended.");
                return Redirect(target);
            }
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

    /// <summary>
    /// Where a failed OIDC sign-in lands. Declining the consent prompt, an expired or already-used
    /// correlation cookie and a reloaded callback are all routine events, not server faults.
    /// </summary>
    /// <remarks>
    /// Anonymous, and it must stay that way: it is reached from the OpenIdConnect handler's
    /// OnRemoteFailure, i.e. precisely when authentication did not happen. Challenging here would
    /// bounce the user straight back into the flow that just failed, which for a declined consent
    /// prompt is a loop. Deliberately not <c>AccessDenied</c>: that page tells the user their account
    /// lacks the necessary roles, which is untrue for every cause but one.
    /// </remarks>
    [AllowAnonymous]
    public IActionResult SignInFailed() =>
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
