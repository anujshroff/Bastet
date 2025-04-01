using Bastet.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Bastet.Services;

public class DevAuthOptions : AuthenticationSchemeOptions
{
}

public class DevAuthHandler : AuthenticationHandler<DevAuthOptions>
{
    public DevAuthHandler(
        IOptionsMonitor<DevAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock) : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Create identity with development user
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "development-user"),
            new Claim(ClaimTypes.Email, "dev@example.com"),
            // Add all defined roles
            new Claim(ClaimTypes.Role, ApplicationRoles.View),
            new Claim(ClaimTypes.Role, ApplicationRoles.Edit),
            new Claim(ClaimTypes.Role, ApplicationRoles.Delete)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        // Return success with the ticket
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
