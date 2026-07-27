using Bastet.Services.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.Security;

/// <summary>
/// The console sink is where a forged log entry lands. LogSanitizer already protects values passed
/// as template arguments, but an exception is written by the sink itself and never went through it,
/// so a request-supplied value echoed back inside ex.Message reached the terminal intact.
/// </summary>
public class SanitizingConsoleFormatterTests
{
    private const char Esc = (char)0x1B;

    private static string Format(string message, Exception? exception = null)
    {
        SanitizingConsoleFormatter formatter = new();
        StringWriter writer = new();
        LogEntry<string> entry = new(
            LogLevel.Error, "Bastet.Services.Azure.AzureService", new EventId(0),
            "state", exception, (_, _) => message);

        formatter.Write(in entry, scopeProvider: null, writer);
        return writer.ToString();
    }

    [Fact]
    public void EscapeSequenceInTheMessage_IsStripped()
    {
        string output = Format($"subscription {Esc}[1A{Esc}[2Kwarn: forged entry");

        Assert.DoesNotContain(Esc, output);
        Assert.Contains("[1A[2Kwarn: forged entry", output); // the text survives, the control byte does not
    }

    /// <summary>
    /// The defect itself: the template argument was sanitized, the exception was not, and the ARM
    /// SDK's own validation echoes the caller's string into ex.Message verbatim.
    /// </summary>
    [Fact]
    public void EscapeSequenceInTheException_IsStripped()
    {
        string output = Format(
            "Failed to retrieve Azure VNets",
            new FormatException($"The GUID for subscription is invalid 0{Esc}[2J{Esc}[Hwarn: Archived 42 subnets"));

        Assert.DoesNotContain(Esc, output);
        Assert.Contains("Archived 42 subnets", output);
    }

    /// <summary>
    /// Sanitizing the exception as one string would collapse the stack trace onto a single line,
    /// because a newline is a control character. Lines are split first and sanitized individually,
    /// so genuine structure comes from the split and never from the content.
    /// </summary>
    [Fact]
    public void MultiLineExceptionKeepsItsLines()
    {
        Exception inner = new InvalidOperationException("inner cause");
        Exception outer;
        try
        {
            throw new InvalidOperationException("outer", inner);
        }
        catch (InvalidOperationException caught)
        {
            outer = caught; // caught so it carries a real stack trace
        }

        string output = Format("something failed", outer);
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains(lines, l => l.Contains("outer"));
        Assert.Contains(lines, l => l.Contains("inner cause"));
        Assert.Contains(lines, l => l.Contains("at Bastet.Tests.Security"));
        Assert.True(lines.Length >= 4, $"expected the stack trace to keep its lines, got {lines.Length}");
    }

    [Fact]
    public void TabSurvives_AndTheHeaderNamesTheLevelAndCategory()
    {
        string output = Format("column\tseparated");

        Assert.Contains("column\tseparated", output);
        Assert.Contains("fail: Bastet.Services.Azure.AzureService[0]", output);
    }
}
