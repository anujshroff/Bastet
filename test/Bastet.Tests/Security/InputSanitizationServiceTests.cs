using Bastet.Services.Security;

namespace Bastet.Tests.Security;

public class InputSanitizationServiceTests
{
    private readonly InputSanitizationService _sanitizationService;

    public InputSanitizationServiceTests() => _sanitizationService = new InputSanitizationService();

    [Theory]
    [InlineData("<script>alert('xss')</script>", "alert('xss')")]
    [InlineData("Hello <b>World</b>", "Hello World")]
    [InlineData("<img src=x onerror=alert('xss')>", "")]
    [InlineData("Normal text", "Normal text")]
    [InlineData("Text with & symbols", "Text with & symbols")]
    public void StripHtml_RemovesHtmlTags(string input, string expected)
    {
        // Act
        string result = _sanitizationService.StripHtml(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Test Name", "Test Name")]
    [InlineData("<script>alert('xss')</script>", "alert('xss')")]
    [InlineData("Name with <b>bold</b>", "Name with bold")]
    public void SanitizeName_SanitizesCorrectly(string input, string expected)
    {
        // Act
        string result = _sanitizationService.SanitizeName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeName_TruncatesLongInput()
    {
        // Arrange
        string longInput = "A" + new string('B', 200); // 201 characters
        string expected = "A" + new string('B', 99);   // 100 characters max

        // Act
        string result = _sanitizationService.SanitizeName(longInput);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("192.168.1.1", "192.168.1.1")]
    [InlineData("192.168.1.1<script>", "192.168.1.1script")]
    [InlineData("192.168.1.1&test=1", "192.168.1.1test1")]
    [InlineData("hostname.domain.com", "hostname.domain.com")]
    [InlineData("host-name_test:8080", "host-name_test:8080")]
    [InlineData("invalid@chars#", "invalidchars")]
    public void SanitizeNetworkInput_SanitizesCorrectly(string input, string expected)
    {
        // Act
        string result = _sanitizationService.SanitizeNetworkInput(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("256.256.256.256", false)]
    [InlineData("192.168.1", false)]
    [InlineData("not.an.ip", false)]
    [InlineData("192.168.1.1<script>", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidIpAddress_ValidatesCorrectly(string? input, bool expected)
    {
        // Act
        bool result = _sanitizationService.IsValidIpAddress(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Hello World", true)]
    [InlineData("Test123", true)]
    [InlineData("Test-Name_123", true)]
    [InlineData("Test@Example.com", true)]
    [InlineData("<script>", false)]
    [InlineData("javascript:", false)]
    [InlineData("Test\x00", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void IsSafeText_ValidatesCorrectly(string? input, bool expected)
    {
        // Act
        bool result = _sanitizationService.IsSafeText(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("tag1,tag2,tag3", "tag1, tag2, tag3")]
    [InlineData("tag1, tag2, tag3", "tag1, tag2, tag3")]
    [InlineData("<script>evil</script>,goodtag", "evil, goodtag")]
    [InlineData("a,b,c,d,e,f,g,h,i,j,k,l", "a, b, c, d, e, f, g, h, i, j")] // Max 10 tags
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizeTags_SanitizesCorrectly(string? input, string expected)
    {
        // Act
        string result = _sanitizationService.SanitizeTags(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Normal description", "Normal description")]
    [InlineData("<p>HTML description</p>", "HTML description")]
    public void SanitizeDescription_SanitizesCorrectly(string input, string expected)
    {
        // Act
        string result = _sanitizationService.SanitizeDescription(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeDescription_TruncatesLongInput()
    {
        // Arrange
        string prefix = "Very long description that exceeds the maximum length limit";
        string longInput = prefix + new string('X', 1000);
        // The service truncates at 1000 characters total
        string expected = longInput[..1000];

        // Act
        string result = _sanitizationService.SanitizeDescription(longInput);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Normal text", false, "Normal text")]
    [InlineData("<script>alert('xss')</script>", false, "alert('xss')")]
    [InlineData("<p>Paragraph</p>", true, "Paragraph")]
    [InlineData("Text with javascript:alert('xss')", true, "Text with alert('xss')")]
    [InlineData("Text with onload='alert(1)'", true, "Text with ='alert(1)'")]
    public void SanitizeString_SanitizesCorrectly(string input, bool allowHtml, string expected)
    {
        // Act
        string result = _sanitizationService.SanitizeString(input, allowHtml);

        // Assert
        Assert.Contains(expected, result.Replace("&amp;", "&").Replace("&#39;", "'"));
    }

    [Theory]
    [InlineData("Test & Company", "Test &amp; Company")]
    [InlineData("<test>", "&lt;test&gt;")]
    [InlineData("\"quoted\"", "&quot;quoted&quot;")]
    [InlineData("'single'", "&#39;single&#39;")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void EncodeHtml_EncodesCorrectly(string? input, string expected)
    {
        // Act
        string result = _sanitizationService.EncodeHtml(input);

        // Assert
        Assert.Equal(expected, result);
    }
}
