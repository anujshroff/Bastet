using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public class AccountController : Controller
{
    public IActionResult AccessDenied(string returnUrl = "/")
    {
        // Store the return URL in ViewData to potentially use it in the view
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }
}
