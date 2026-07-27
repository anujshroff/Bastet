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

    // -------------------------------------------------------------------------
    // Line breaks are not the only way to forge an entry
    // -------------------------------------------------------------------------

    /// <summary>Escape, as a named constant - never a literal control character in source.</summary>
    private const char Esc = (char)0x1B;

    /// <summary>
    /// The console sink applies no control-character escaping, so an ESC byte reaches the operator's
    /// terminal intact and a VT100-compatible terminal reads ESC[1A as "cursor up" and ESC[2K as
    /// "erase line" - forging an entry over the top of a real one without any line break involved.
    /// </summary>
    [Fact]
    public void SanitizeForLog_RemovesEscapeSequencesUsedToRewriteTheScreen()
    {
        string forged = $"subnet-a{Esc}[1A{Esc}[2Kinfo: All subnets verified OK";

        string result = LogSanitizer.SanitizeForLog(forged);

        Assert.DoesNotContain(Esc, result);
        Assert.Equal("subnet-a[1A[2Kinfo: All subnets verified OK", result);
    }

    [Theory]
    [InlineData((char)0x1B)]   // ESC  - cursor control
    [InlineData((char)0x08)]   // BS   - backspace over what was already written
    [InlineData((char)0x07)]   // BEL
    [InlineData((char)0x00)]   // NUL
    [InlineData((char)0x7F)]   // DEL
    public void SanitizeForLog_RemovesControlCharactersGenerally(char control) =>
        Assert.Equal("ab", LogSanitizer.SanitizeForLog($"a{control}b"));

    /// <summary>
    /// Tab is deliberately exempt - it moves no cursor and appears in real pasted values. Guarded
    /// here as well as in the ordinary-values theory, because a naive !char.IsControl(c) would
    /// silently start eating it.
    /// </summary>
    [Fact]
    public void SanitizeForLog_KeepsTab() =>
        Assert.Equal("name with\ttab", LogSanitizer.SanitizeForLog("name with\ttab"));
}
