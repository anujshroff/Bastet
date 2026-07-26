using Bastet.Services.Security;

namespace Bastet.Tests.Security;

/// <summary>
/// The frame-ancestors value from BASTET_FRAME_ANCESTORS is written to a header on every response, so
/// a character Kestrel refuses would fail every request rather than one. These cases mirror what a
/// minimal Kestrel app was observed to accept and reject.
/// </summary>
public class HttpHeaderValueTests
{
    [Theory]
    [InlineData("'none'")]
    [InlineData("'self'")]
    [InlineData("https://portal.example.com")]
    [InlineData("'self' https://a.example.com https://b.example.com")]
    // Tab is a control character but is legal in a header value, and Kestrel accepts it.
    [InlineData("'self'\thttps://a.example.com")]
    public void IsValid_AcceptsValuesKestrelWillWrite(string value) =>
        Assert.True(HttpHeaderValue.IsValid(value));

    [Theory]
    // The realistic accident: an env file edited on Windows leaves a carriage return on the value.
    [InlineData("'self'\r")]
    [InlineData("'self'\n")]
    [InlineData("'self'\r\n")]
    // A smart quote pasted from a document.
    [InlineData("‘self’")]
    // Any other character above the ASCII range.
    [InlineData("'self' https://café.example.com")]
    public void IsValid_RejectsValuesKestrelWillRefuse(string value) =>
        Assert.False(HttpHeaderValue.IsValid(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsValid_TreatsAbsentValueAsValid(string? value) =>
        // Nothing is written for an empty value, so there is nothing to reject.
        Assert.True(HttpHeaderValue.IsValid(value));
}
