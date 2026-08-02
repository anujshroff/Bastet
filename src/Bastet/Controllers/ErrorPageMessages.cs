using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Text.Json;

namespace Bastet.Controllers;

/// <summary>
/// Carries a diagnostic from an action that redirects to <c>/Error/{statusCode}</c> across to the
/// error page, giving every redirect its own slot.
/// </summary>
/// <remarks>
/// Round-10 J4. There used to be one <c>TempData["ErrorPageMessage"]</c> slot for the whole session,
/// and <c>ErrorController</c> read it unconditionally - so whichever request reached the error page
/// first took whatever was pending, and a TempData read consumes it. Three ways that went wrong,
/// all reproduced:
/// <list type="bullet">
/// <item>an unrelated 4xx (a missing stylesheet, a stale antiforgery token) landing between the
/// redirect and the browser following it printed a diagnostic about a request that browser never
/// made, while the page it belonged to fell back to the generic text;</item>
/// <item>two stale rows in two tabs crossed their messages - the most reachable path, and the one a
/// re-execute-only guard does not close, because neither request is a re-execute;</item>
/// <item>a 4xx re-execute with nothing pending still flushed the key, silently deleting a message
/// rather than stealing it.</item>
/// </list>
/// A token in the redirect URL fixes all three: the error page reads only the entry the redirect that
/// reached it created. The token is an opaque lookup key that is never rendered, so this does not
/// reintroduce round 3's B6 - the message text still comes from TempData, set server-side, and is
/// never taken from the URL.
/// </remarks>
public static class ErrorPageMessages
{
    private const string TempDataKey = "ErrorPageMessages";

    /// <summary>Query-string key holding the token that identifies one pending message.</summary>
    public const string TokenQueryKey = "m";

    /// <summary>
    /// How many undelivered messages to keep. A redirect the browser never follows - the user hits
    /// Stop, closes the tab, the follow-up fails - leaves its entry behind, and TempData persists
    /// anything unread. Without a cap those would accumulate in the cookie for the whole session.
    /// The oldest is dropped first; losing an undelivered diagnostic costs nothing, because nothing
    /// is going to ask for it.
    /// </summary>
    private const int MaxPending = 5;

    private sealed record PendingMessage(string Token, string Message);

    /// <summary>
    /// Stashes <paramref name="message"/> and redirects to the error page for
    /// <paramref name="statusCode"/>, tagging the redirect so only it can read the message back.
    /// </summary>
    public static RedirectToActionResult RedirectToErrorPage(
        this Controller controller, int statusCode, string message)
    {
        List<PendingMessage> pending = Read(controller.TempData);
        string token = Guid.NewGuid().ToString("N")[..12];

        pending.Add(new PendingMessage(token, message));
        if (pending.Count > MaxPending)
        {
            pending.RemoveRange(0, pending.Count - MaxPending);
        }

        Write(controller.TempData, pending);

        return controller.RedirectToAction(
            "HttpStatusCodeHandler", "Error", new { statusCode, m = token });
    }

    /// <summary>
    /// Returns the message this request's token refers to, removing it so it is shown once, and
    /// leaves every other pending message untouched.
    /// </summary>
    public static string? Take(ITempDataDictionary tempData, string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            // A re-executed 4xx or a direct visit to /Error/{code} carries no token. It must not
            // read, and must not clear, a message queued for some other request.
            return null;
        }

        List<PendingMessage> pending = Read(tempData);
        PendingMessage? match = pending.Find(p => p.Token == token);
        if (match is null)
        {
            return null;
        }

        pending.Remove(match);
        Write(tempData, pending);
        return match.Message;
    }

    private static List<PendingMessage> Read(ITempDataDictionary tempData)
    {
        // Peek rather than index: reading TempData consumes it, and the entries not being taken here
        // still have a request to survive to.
        if (tempData.Peek(TempDataKey) is not string serialized || serialized.Length == 0)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<PendingMessage>>(serialized) ?? [];
        }
        catch (JsonException)
        {
            // Written by a previous version of this application, or truncated. A diagnostic is not
            // worth failing a request that is already reporting an error.
            return [];
        }
    }

    private static void Write(ITempDataDictionary tempData, List<PendingMessage> pending)
    {
        if (pending.Count == 0)
        {
            tempData.Remove(TempDataKey);
            return;
        }

        tempData[TempDataKey] = JsonSerializer.Serialize(pending);
    }
}
