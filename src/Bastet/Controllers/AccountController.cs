using Bastet.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public class AccountController(IWebHostEnvironment environment, IUserContextService userContextService) : Controller
{
    public IActionResult AccessDenied(string returnUrl = "/")
    {
        // Store the return URL in ViewData to potentially use it in the view
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    public async Task<IActionResult> Logout(string returnUrl = "/")
    {
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
                new AuthenticationProperties { RedirectUri = returnUrl },
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme);
        }

        // In development, just redirect to the specified URL or home
        return Redirect("/");
    }

    public IActionResult Roles()
    {
        // Only accessible to authenticated users
        if (!User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        // Get the current username
        ViewData["Username"] = userContextService.GetCurrentUsername();

        // Get the user's Bastet roles
        ViewData["BastetRoles"] = userContextService.GetUserBastetRoles();

        // Get the user's token roles
        ViewData["TokenRoles"] = userContextService.GetUserTokenRoles();

        return View();
    }
}
