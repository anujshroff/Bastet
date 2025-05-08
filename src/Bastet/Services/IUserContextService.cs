namespace Bastet.Services;

public interface IUserContextService
{
    string? GetCurrentUsername();
    bool UserHasRole(string role);
    IEnumerable<string> GetUserBastetRoles();
    IEnumerable<string> GetUserTokenRoles();
}
