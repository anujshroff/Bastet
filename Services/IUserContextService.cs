namespace Bastet.Services;

public interface IUserContextService
{
    string? GetCurrentUsername();
    bool UserHasRole(string role);
}
