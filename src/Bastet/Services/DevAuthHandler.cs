using Bastet.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Bastet.Services;

public class DevAuthOptions : AuthenticationSchemeOptions
{
    public string AccessDeniedPath { get; set; } = "/Account/AccessDenied";
}

public class DevAuthHandler(
    IOptionsMonitor<DevAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<DevAuthOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Create identity with development user
        Claim[] claims =
        [
            new Claim(ClaimTypes.Name, "development-user"),
            new Claim(ClaimTypes.Email, "dev@example.com"),
            // Add all defined roles
            new Claim(ClaimTypes.Role, ApplicationRoles.Admin)
        ];

        ClaimsIdentity identity = new(claims, Scheme.Name);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, Scheme.Name);

        // Return success with the ticket
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
