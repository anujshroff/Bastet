using Bastet.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Bastet.Controllers;

/// <summary>
/// Error pages. These are the targets of UseStatusCodePagesWithReExecute and UseExceptionHandler,
/// so they must stay anonymous - if the authorization fallback policy challenged them, handling a
/// 401 would produce another 401 and the error pipeline would recurse.
/// </summary>
[AllowAnonymous]
public class ErrorController : Controller
{
    [Route("/Error/{statusCode}")]
    public IActionResult HttpStatusCodeHandler(int statusCode)
    {
        // The message is read from TempData (set server-side by the redirecting action), never from
        // the query string - otherwise anyone could craft /Error/400?errorMessage=... and show
        // arbitrary text under this origin. Falls back to the per-status default below.
        string? errorMessage = TempData["ErrorPageMessage"] as string;

        ErrorViewModel viewModel = new()
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            StatusCode = statusCode,
            ErrorMessage = errorMessage
        };

        // Determine which view to use based on status code
        switch (statusCode)
        {
            case 400:
                viewModel.Title = "Bad Request";
                viewModel.ErrorMessage ??= "The request could not be understood by the server.";
                return View("BadRequest", viewModel);

            case 404:
                viewModel.Title = "Resource Not Found";
                viewModel.ErrorMessage ??= "The resource you requested could not be found.";
                return View("NotFound", viewModel);

            case 409:
                viewModel.Title = "Conflict";
                viewModel.ErrorMessage ??= "The resource you're trying to modify conflicts with existing data.";
                return View("ConflictError", viewModel);

            case 500:
                viewModel.Title = "Server Error";
                viewModel.ErrorMessage ??= "An unexpected error occurred on the server.";
                return View("ServerError", viewModel);

            default:
                viewModel.Title = "Error";
                viewModel.ErrorMessage ??= "An error occurred while processing your request.";
                return View("Error", viewModel);
        }
    }

    [Route("Error")]
    public IActionResult Error()
    {
        ErrorViewModel viewModel = new()
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            StatusCode = 500,
            Title = "Server Error",
            ErrorMessage = "An unexpected error occurred on the server."
        };

        return View("ServerError", viewModel);
    }
}
