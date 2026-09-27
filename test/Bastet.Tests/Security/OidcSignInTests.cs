using Bastet.Services.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Bastet.Tests.Security;

public class OidcSignInTests
{
    private static TicketReceivedContext SignInReturningTo(string? returnUri, params AuthenticationToken[] tokens)
    {
        AuthenticationProperties properties = new();
        properties.StoreTokens(tokens);
        AuthenticationTicket ticket = new(new ClaimsPrincipal(new ClaimsIdentity("Test")), properties, "OpenIdConnect");

        return new TicketReceivedContext(
            new DefaultHttpContext(),
            new AuthenticationScheme("OpenIdConnect", null, typeof(OpenIdConnectHandler)),
            new OpenIdConnectOptions(),
            ticket)
        {
            ReturnUri = returnUri
        };
    }

    [Theory]
    [InlineData("//evil.example/phish")]
    [InlineData("/\\evil.example/phish")]
    [InlineData("https://evil.example/")]
    [InlineData("/\tevil.example")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AReturnTargetThatIsNotOneOfBastetsOwnPages_SendsTheBrowserHome(string? returnUri)
    {
        TicketReceivedContext context = SignInReturningTo(returnUri);

        await OidcSignIn.OnTicketReceived(context);

        Assert.Equal("/", context.ReturnUri);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Subnet")]
    [InlineData("/Subnet/Details/5?tab=hosts")]
    [InlineData("/%2F%2Fevil.example/phish")]
    public async Task AReturnTargetOnOneOfBastetsOwnPages_IsKept(string returnUri)
    {
        TicketReceivedContext context = SignInReturningTo(returnUri);

        await OidcSignIn.OnTicketReceived(context);

        Assert.Equal(returnUri, context.ReturnUri);
    }

    [Fact]
    public async Task OnlyTheIdTokenIsKeptInTheCookie()
    {
        TicketReceivedContext context = SignInReturningTo(
            "/Subnet",
            new AuthenticationToken { Name = "id_token", Value = "id" },
            new AuthenticationToken { Name = "access_token", Value = "access" },
            new AuthenticationToken { Name = "refresh_token", Value = "refresh" });

        await OidcSignIn.OnTicketReceived(context);

        AuthenticationToken kept = Assert.Single(context.Properties!.GetTokens());
        Assert.Equal("id_token", kept.Name);
        Assert.Equal("id", kept.Value);
    }

    [Fact]
    public void SignInAndSignOut_DecideWhereTheBrowserMayGo_WithTheSameRule()
    {
        string logout = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Bastet", "Controllers", "AccountController.cs"));
        string signIn = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Bastet", "Services", "Security", "OidcSignIn.cs"));
        string program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Bastet", "Program.cs"));

        Assert.Contains("ReturnUrl.IsLocal(returnUrl)", logout);
        Assert.DoesNotContain("IsLocalUrl", logout);
        Assert.Contains("ReturnUrl.IsLocal(context.ReturnUri)", signIn);
        Assert.Contains("options.Events.OnTicketReceived = OidcSignIn.OnTicketReceived;", program);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bastet.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Bastet.sln not found above test base directory");
    }
}
