using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public class HomeController() : Controller
{
    [Authorize(Policy = "RequireViewRole")]
    public IActionResult Index() => View();
}
