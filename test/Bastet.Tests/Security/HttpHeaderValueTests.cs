using Bastet.Services.Security;

namespace Bastet.Tests.Security;

public class HttpHeaderValueTests
{
    [Theory]
    [InlineData("'none'")]
    [InlineData("'self'")]
    [InlineData("https://portal.example.com")]
    [InlineData("'self' https://a.example.com https://b.example.com")]

    [InlineData("'self'\thttps://a.example.com")]
    public void IsValid_AcceptsValuesKestrelWillWrite(string value) =>
        Assert.True(HttpHeaderValue.IsValid(value));

    [Theory]

    [InlineData("'self'\r")]
    [InlineData("'self'\n")]
    [InlineData("'self'\r\n")]

    [InlineData("‘self’")]

    [InlineData("'self' https://café.example.com")]
    public void IsValid_RejectsValuesKestrelWillRefuse(string value) =>
        Assert.False(HttpHeaderValue.IsValid(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsValid_TreatsAbsentValueAsValid(string? value) =>

        Assert.True(HttpHeaderValue.IsValid(value));
}
