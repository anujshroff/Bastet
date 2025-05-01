namespace Bastet.Models;

public static class ApplicationRoles
{
    public const string View = "View";
    public const string Edit = "Edit";
    public const string Delete = "Delete";
    public const string Admin = "Admin";

    // Helper collection of all roles
    public static readonly IReadOnlyCollection<string> AllRoles = [View, Edit, Delete, Admin];
}
