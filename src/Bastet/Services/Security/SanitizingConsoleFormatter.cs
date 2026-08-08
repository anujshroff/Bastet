using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Bastet.Services.Security;

public sealed class SanitizingConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "bastet-sanitizing";

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
