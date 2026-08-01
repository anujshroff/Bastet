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
    public IActionResult HttpStatusCodeHandler()
    {
        // Read straight off the route rather than bound as a parameter. UseStatusCodePagesWithReExecute
        // re-executes the pipeline for the SAME request - same method, same body - having rewritten
        // the path, and MVC's default composite value provider puts FormValueProviderFactory ahead of
        // RouteValueProviderFactory. A form field named statusCode in the original POST body therefore
        // outranked the route segment the middleware had just set, letting a caller relabel their own
        // failed request: posting statusCode=404 to a request the framework answered 400 with put 404
        // on the wire and in the request log.
        //
        // Binding also failed in the other direction. When the body cannot be read at all - a NUL byte
        // in any form value, a malformed multipart boundary - FormPipeReader throws and binding is
        // abandoned for every source, so the parameter arrived as 0 and the guard below turned an
        // ordinary client mistake into 500 "Status Code: 0". The route value survives both, because it
        // is what the middleware set and it does not depend on parsing the body.
        int statusCode = RouteData.Values.TryGetValue("statusCode", out object? routeValue)
                         && int.TryParse(routeValue as string, out int statusFromRoute)
            ? statusFromRoute
            : Response.StatusCode;

        // The message is read from TempData (set server-side by the redirecting action), never from
        // the query string - otherwise anyone could craft /Error/400?errorMessage=... and show
        // arbitrary text under this origin. Falls back to the per-status default below.
        string? errorMessage = TempData["ErrorPageMessage"] as string;

        // UseStatusCodePagesWithReExecute sets the status itself before re-executing, so a route
        // that matched nothing really did answer 404. A controller that *redirects* here does not:
        // the browser issues a fresh GET, this action returns a view, and the response went out as
        // HTTP 200 with "Resource Not Found" rendered in it. Eleven redirect sites reach the page
        // that way, so a missing subnet reported success to anything reading the status rather than
        // the page. Setting it here is a no-op on the re-execute path, which already carries the
        // same value. The route segment is caller-supplied, so anything outside the error range
        // becomes 500 rather than letting /Error/200 answer 200.
        Response.StatusCode = statusCode is >= 400 and <= 599 ? statusCode : 500;

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
        // Same reason as above. UseExceptionHandler already sets 500 before re-executing, so this
        // only changes the case where the page is reached directly.
        Response.StatusCode = 500;

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
