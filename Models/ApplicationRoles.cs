namespace Bastet.Models;

public static class ApplicationRoles
{
    public const string View = "View";
    public const string Edit = "Edit";
    public const string Delete = "Delete";

    // Helper collection of all roles
    public static readonly IReadOnlyCollection<string> AllRoles = [View, Edit, Delete];
}
