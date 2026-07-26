using Bastet.Services.Security;

namespace Bastet.Tests.Security;

/// <summary>
/// Values that came from a request must not be able to add lines to the log. Sanitization trims and
/// strips HTML but leaves interior line breaks, so anything logged raw has to be stripped here first.
/// </summary>
public class LogSanitizerTests
{
    [Fact]
    public void SanitizeForLog_RemovesInteriorLineBreaks()
    {
        // The forging shape: a value that ends a line and starts what reads as a second log entry.
        string forged = "subnet-a\nwarn: Bastet: admin login from 1.2.3.4";

        string result = LogSanitizer.SanitizeForLog(forged);

        Assert.DoesNotContain("\n", result);
        Assert.Equal("subnet-awarn: Bastet: admin login from 1.2.3.4", result);
    }

    [Theory]
    [InlineData("a\rb", "ab")]
    [InlineData("a\nb", "ab")]
    [InlineData("a\r\nb", "ab")]
    [InlineData("\r\nleading", "leading")]
    [InlineData("trailing\r\n", "trailing")]
    public void SanitizeForLog_RemovesLineBreaksWhereverTheyAppear(string value, string expected) =>
        Assert.Equal(expected, LogSanitizer.SanitizeForLog(value));

    [Theory]
    [InlineData("ordinary subnet name")]
    [InlineData("10.0.0.0/24")]
    [InlineData("name with\ttab")]
    public void SanitizeForLog_LeavesOrdinaryValuesAlone(string value) =>
        Assert.Equal(value, LogSanitizer.SanitizeForLog(value));

    [Fact]
    public void SanitizeForLog_TreatsNullAsEmpty() =>
        Assert.Equal(string.Empty, LogSanitizer.SanitizeForLog(null));
}
