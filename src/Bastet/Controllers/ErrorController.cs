using Bastet.Models.ViewModels;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Bastet.Controllers;

public class ErrorController : Controller
{
    [Route("/Error/{statusCode}")]
    public IActionResult HttpStatusCodeHandler(int statusCode, string? errorCode = null, string? errorMessage = null)
    {
        IStatusCodeReExecuteFeature? statusCodeResult = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        ErrorViewModel viewModel = new()
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            StatusCode = statusCode,
            ErrorCode = errorCode,
            OriginalPath = statusCodeResult?.OriginalPath,
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
        IExceptionHandlerPathFeature? exceptionDetails = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        ErrorViewModel viewModel = new()
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            StatusCode = 500,
            Title = "Server Error",
            ErrorMessage = "An unexpected error occurred on the server.",
            OriginalPath = exceptionDetails?.Path
        };

        return View("ServerError", viewModel);
    }
}
