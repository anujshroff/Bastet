using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Text.Json;

namespace Bastet.Controllers;

public static class ErrorPageMessages
{
    private const string TempDataKey = "ErrorPageMessages";

    public const string TokenQueryKey = "m";

    private const int MaxPending = 5;

    private sealed record PendingMessage(string Token, string Message);

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

    public static string? Take(ITempDataDictionary tempData, string? token)
    {
        if (string.IsNullOrEmpty(token))
        {

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
