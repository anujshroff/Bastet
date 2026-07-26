namespace Bastet.Services.Security;

/// <summary>
/// Prepares request-supplied values for logging.
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Strips line breaks from a value before it is logged, so crafted input cannot forge additional
    /// log entries (CodeQL: log entries created from user input). A log line the operator is reading
    /// to diagnose a problem must not be able to describe events that never happened.
    /// </summary>
    public static string SanitizeForLog(string? value) =>
        (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
}
