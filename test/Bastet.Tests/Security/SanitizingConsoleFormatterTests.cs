using Bastet.Services.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bastet.Tests.Security;

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
        Assert.Contains("[1A[2Kwarn: forged entry", output);
    }

    [Fact]
    public void EscapeSequenceInTheException_IsStripped()
    {
        string output = Format(
            "Failed to retrieve Azure VNets",
            new FormatException($"The GUID for subscription is invalid 0{Esc}[2J{Esc}[Hwarn: Archived 42 subnets"));

        Assert.DoesNotContain(Esc, output);
        Assert.Contains("Archived 42 subnets", output);
    }

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
            outer = caught;
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
