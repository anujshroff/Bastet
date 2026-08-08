using Bastet.Services.Security;

namespace Bastet.Tests.Security;

public class LogSanitizerTests
{
    [Fact]
    public void SanitizeForLog_RemovesInteriorLineBreaks()
    {

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

    private const char Esc = (char)0x1B;

    [Fact]
    public void SanitizeForLog_RemovesEscapeSequencesUsedToRewriteTheScreen()
    {
        string forged = $"subnet-a{Esc}[1A{Esc}[2Kinfo: All subnets verified OK";

        string result = LogSanitizer.SanitizeForLog(forged);

        Assert.DoesNotContain(Esc, result);
        Assert.Equal("subnet-a[1A[2Kinfo: All subnets verified OK", result);
    }

    [Theory]
    [InlineData((char)0x1B)]
    [InlineData((char)0x08)]
    [InlineData((char)0x07)]
    [InlineData((char)0x00)]
    [InlineData((char)0x7F)]
    public void SanitizeForLog_RemovesControlCharactersGenerally(char control) =>
        Assert.Equal("ab", LogSanitizer.SanitizeForLog($"a{control}b"));

    [Fact]
    public void SanitizeForLog_KeepsTab() =>
        Assert.Equal("name with\ttab", LogSanitizer.SanitizeForLog("name with\ttab"));
}
