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

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    [AllowAnonymous]
    public async Task<IActionResult> Logout(string? returnUrl = null)
    {

        string target = !string.IsNullOrEmpty(returnUrl)
                && Url.IsLocalUrl(returnUrl)
                && HttpHeaderValue.IsValid(returnUrl)
            ? returnUrl
            : Url.Action(nameof(SignedOut), "Account") ?? "/Account/SignedOut";

        TempData.Clear();

        if (!environment.IsDevelopment())
        {

            AuthenticationProperties properties = new() { RedirectUri = target };

            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, properties);

            if (User.Identity?.IsAuthenticated != true)
            {
                return Redirect(target);
            }

            try
            {
                await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);

                return new EmptyResult();
            }
            catch (Exception ex)
            {

                logger.LogWarning(ex,
                    "Signing out of the identity provider failed; the local session was still ended.");
                return Redirect(target);
            }
        }

        return Redirect(target);
    }

    [AllowAnonymous]
    public IActionResult SignedOut() =>
        User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Home") : View();

    [AllowAnonymous]
    public IActionResult SignInFailed() =>
        User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Home") : View();

    [Authorize]
    public IActionResult Roles()
    {

        ViewData["Username"] = userContextService.GetCurrentUsername();

        ViewData["BastetRoles"] = userContextService.GetUserBastetRoles();

        ViewData["TokenRoles"] = userContextService.GetUserTokenRoles();

        return View();
    }
}
