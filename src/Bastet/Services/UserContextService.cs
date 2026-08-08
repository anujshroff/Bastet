using Bastet.Models;
using System.Security.Claims;

namespace Bastet.Services;

public class UserContextService(IHttpContextAccessor httpContextAccessor) : IUserContextService
{
    public string? GetCurrentUsername()
    {

        if (httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        string? username = httpContextAccessor.HttpContext.User.FindFirst("preferred_username")?.Value;

        if (string.IsNullOrEmpty(username))
        {
            username = httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.Email)?.Value;
        }

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

        if (httpContextAccessor.HttpContext.User.IsInRole(role))
        {
            return true;
        }

        return role switch
        {
            ApplicationRoles.View => httpContextAccessor.HttpContext.User.IsInRole(ApplicationRoles.Edit) ||
                                   httpContextAccessor.HttpContext.User.IsInRole(ApplicationRoles.Delete) ||
                                   httpContextAccessor.HttpContext.User.IsInRole(ApplicationRoles.Admin),
            ApplicationRoles.Edit => httpContextAccessor.HttpContext.User.IsInRole(ApplicationRoles.Delete) ||
                                   httpContextAccessor.HttpContext.User.IsInRole(ApplicationRoles.Admin),
            ApplicationRoles.Delete => httpContextAccessor.HttpContext.User.IsInRole(ApplicationRoles.Admin),
            _ => false,
        };
    }

    public IEnumerable<string> GetUserBastetRoles()
    {
        if (httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return [];
        }

        return ApplicationRoles.AllRoles.Where(role => UserHasRole(role));
    }

    public IEnumerable<string> GetUserTokenRoles()
    {
        if (httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return [];
        }

        List<string> allRoleClaims = [.. httpContextAccessor.HttpContext.User
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)];

        List<string> customRoleClaims = [.. httpContextAccessor.HttpContext.User
            .FindAll("roles")
            .SelectMany(c => c.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];

        allRoleClaims.AddRange(customRoleClaims);

        return allRoleClaims.Distinct().OrderBy(role => role);
    }
}
