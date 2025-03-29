using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
}
