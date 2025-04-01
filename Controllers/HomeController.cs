using Bastet.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public class HomeController(IUserContextService userContextService) : Controller
{
    [Authorize(Policy = "RequireViewRole")]
    public IActionResult Index() => View();
}
