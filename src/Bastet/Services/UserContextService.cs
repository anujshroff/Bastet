using System.Security.Claims;

namespace Bastet.Services;

public class UserContextService(IHttpContextAccessor httpContextAccessor) : IUserContextService
{
    public string? GetCurrentUsername()
    {
        // Check if the user is authenticated
        if (httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // Try to get preferred_username claim (common in OIDC)
        string? username = httpContextAccessor.HttpContext.User.FindFirst("preferred_username")?.Value;

        // Fall back to email if name is not available
        if (string.IsNullOrEmpty(username))
        {
            username = httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.Email)?.Value;
        }

        // Fall back to name claim if preferred_username is not available
        if (string.IsNullOrEmpty(username))
        {
            username = httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.Name)?.Value;
        }

        return username;
    }

    public bool UserHasRole(string role) =>
        // Simply check if the user is in the specified role
        // DevAuthHandler will automatically add all roles in development
        httpContextAccessor.HttpContext?.User?.IsInRole(role) == true;
}
