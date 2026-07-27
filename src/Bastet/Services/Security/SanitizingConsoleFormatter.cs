using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Bastet.Services.Security;

/// <summary>
/// A console formatter that runs every line it writes through <see cref="LogSanitizer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LogSanitizer"/> protects values passed as template arguments, but the sink writes the
/// exception itself and that never went through it. An ARM identifier that fails the Azure SDK's own
/// local validation comes back inside <c>ex.Message</c> verbatim, so a request-supplied value reached
/// the terminal with its control characters intact - enough to erase a genuine log line and print a
/// fabricated one in its place. Sanitizing at the sink covers every call site at once, including the
/// ones no static analyser flags because their template arguments are integers.
/// </para>
/// <para>
/// Lines are split before they are sanitized, never after. A newline is a control character, so
/// sanitizing an exception as one string would collapse its stack trace onto a single line; splitting
/// first means legitimate structure comes from the formatter and only the content is scrubbed.
/// </para>
/// <para>
/// No ANSI colour is emitted, unlike the default simple formatter. A formatter whose purpose is to
/// keep escape sequences out of the log has no business writing its own.
/// </para>
/// </remarks>
public sealed class SanitizingConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "bastet-sanitizing";

    /// <summary>Matches the default console formatter's continuation indent.</summary>
    private const string Indent = "      ";

    public override void Write<TState>(
        in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        ArgumentNullException.ThrowIfNull(textWriter);

        string message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception) ?? string.Empty;

        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
        {
            return;
        }

        textWriter.Write(LevelPrefix(logEntry.LogLevel));
        textWriter.Write(": ");
        textWriter.Write(logEntry.Category);
        textWriter.Write('[');
        textWriter.Write(logEntry.EventId.Id);
        textWriter.Write(']');
        textWriter.Write(Environment.NewLine);

        WriteIndentedLines(textWriter, message);

        if (logEntry.Exception is not null)
        {
            WriteIndentedLines(textWriter, logEntry.Exception.ToString());
        }
    }

    private static void WriteIndentedLines(TextWriter textWriter, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (string line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            textWriter.Write(Indent);
            textWriter.Write(LogSanitizer.SanitizeForLog(line));
            textWriter.Write(Environment.NewLine);
        }
    }

    /// <summary>The four-character prefixes the default console formatter uses.</summary>
    private static string LevelPrefix(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        LogLevel.Critical => "crit",
        _ => "none"
    };
}
