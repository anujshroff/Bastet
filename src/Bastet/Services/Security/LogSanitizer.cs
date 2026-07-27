namespace Bastet.Services.Security;

/// <summary>
/// Prepares request-supplied values for logging.
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Tab is the one control character kept. It carries no cursor movement, appears in pasted
    /// values often enough to be worth preserving verbatim, and removing it would change what an
    /// operator sees without buying any safety.
    /// </summary>
    private const char Tab = '\t';

    /// <summary>
    /// Strips control characters from a value before it is logged, so crafted input cannot forge
    /// additional log entries (CodeQL: log entries created from user input). A log line the operator
    /// is reading to diagnose a problem must not be able to describe events that never happened.
    /// </summary>
    /// <remarks>
    /// Removing CR and LF alone is not enough. The console sink this app configures applies no
    /// control-character escaping of its own - its only transformation replaces newlines with
    /// padding - so an ESC byte reaches the operator's terminal verbatim, and a VT100-compatible
    /// terminal reads ESC[1A as "move up one line" and ESC[2K as "erase it". That forges an entry
    /// without needing a line break at all, which is exactly what this method exists to prevent.
    /// The value is dropped rather than escaped: these are identifiers and names being logged for
    /// diagnosis, not content that needs to survive round-tripping.
    /// </remarks>
    public static string SanitizeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return !value.Any(c => char.IsControl(c) && c != Tab)
            ? value
            : string.Concat(value.Where(c => !char.IsControl(c) || c == Tab));
    }
}
