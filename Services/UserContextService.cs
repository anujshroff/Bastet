using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Bastet.Services;

public class UserContextService(IHttpContextAccessor httpContextAccessor) : IUserContextService
{
    public string? GetCurrentUsername()
    {
        // Check if we're in development mode first
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
        {
            return "development-user";
        }

        // For production, check authentication
        if (httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // Try to get preferred_username claim (common in OIDC)
        string? username = httpContextAccessor.HttpContext.User.FindFirst("preferred_username")?.Value;

        // Fall back to name claim if preferred_username is not available
        if (string.IsNullOrEmpty(username))
        {
            username = httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.Name)?.Value;
        }

        // Fall back to email if name is not available
        if (string.IsNullOrEmpty(username))
        {
            username = httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.Email)?.Value;
        }

        return username;
    }
}
