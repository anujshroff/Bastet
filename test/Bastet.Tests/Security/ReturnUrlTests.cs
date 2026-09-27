using Bastet.Services.Security;

namespace Bastet.Tests.Security;

public class ReturnUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Subnet")]
    [InlineData("/Subnet/Details/5?tab=hosts#top")]
    [InlineData("/%2F%2Fevil.example/phish")]
    [InlineData("~/")]
    [InlineData("~/Subnet")]
    public void OneOfBastetsOwnPages_IsLocal(string url) => Assert.True(ReturnUrl.IsLocal(url));

    [Theory]
    [InlineData("//evil.example/phish")]
    [InlineData("/\\evil.example/phish")]
    [InlineData("~//evil.example")]
    [InlineData("~/\\evil.example")]
    [InlineData("https://evil.example/")]
    [InlineData("evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/\tevil.example")]
    [InlineData("/Subnet\r\nSet-Cookie: a=b")]
    [InlineData("~")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingThatCouldLeaveBastet_IsNotLocal(string? url) => Assert.False(ReturnUrl.IsLocal(url));

    [Fact]
    public void ALineSeparatorInsideThePath_IsNotLocal()
        => Assert.False(ReturnUrl.IsLocal("/" + (char)0x2028 + "/evil.example"));
}
