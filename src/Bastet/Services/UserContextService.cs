using Bastet.Models;
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

    public bool UserHasRole(string role)
    {
        if (httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        // Role inheritance logic:
        // Admin can do everything
        // Delete can also Edit and View
        // Edit can also View

        // Direct role check first
        if (httpContextAccessor.HttpContext.User.IsInRole(role))
        {
            return true;
        }

        // Check for higher roles that imply the requested role
        return role switch
        {
            ApplicationRoles.View => httpContextAccessor.HttpContext.User.IsInRole(ApplicationRoles.Edit) ||
                                   httpContextAccessor.HttpContext.User.IsInRole(ApplicationRoles.Delete) ||
                                   httpContextAccessor.HttpContext.User.IsInRole(ApplicationRoles.Admin),// If they have Edit or Delete or Admin, they automatically have View
            ApplicationRoles.Edit => httpContextAccessor.HttpContext.User.IsInRole(ApplicationRoles.Delete) ||
                                   httpContextAccessor.HttpContext.User.IsInRole(ApplicationRoles.Admin),// If they have Delete or Admin, they automatically have Edit
            ApplicationRoles.Delete => httpContextAccessor.HttpContext.User.IsInRole(ApplicationRoles.Admin),// If they have Admin, they automatically have Delete
            _ => false,
        };
    }

    public IEnumerable<string> GetUserBastetRoles()
    {
        if (httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return [];
        }

        // Return all Bastet application roles that the user has
        return ApplicationRoles.AllRoles.Where(role => UserHasRole(role));
    }

    public IEnumerable<string> GetUserTokenRoles()
    {
        if (httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return [];
        }

        // Get all role claims from the user's identity
        List<string> allRoleClaims = [.. httpContextAccessor.HttpContext.User
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)];

        // Add roles that might be in a custom claim (like "roles")
        List<string> customRoleClaims = [.. httpContextAccessor.HttpContext.User
            .FindAll("roles")
            .SelectMany(c => c.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];

        allRoleClaims.AddRange(customRoleClaims);

        // Return all token roles, including Bastet roles if they exist in the token
        return allRoleClaims.Distinct().OrderBy(role => role);
    }
}
