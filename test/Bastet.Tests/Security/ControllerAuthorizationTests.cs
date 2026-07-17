using Bastet.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace Bastet.Tests.Security;

/// <summary>
/// Guards the authorization surface of every controller action, including ones that don't exist yet.
/// Program.cs sets a fallback policy so an action that forgets its attribute is denied rather than
/// served anonymously, but the fallback only requires *authentication* - it would happily let a
/// View-role user reach an action that meant to be Admin-only. These tests make the omission fail
/// at build time instead of in production.
/// </summary>
public class ControllerAuthorizationTests
{
    /// <summary>
    /// Actions that are deliberately reachable without authentication, with the reason they must be.
    /// Anything added here is a decision, not an oversight.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedAnonymousActions = new()
    {
        ["ErrorController.HttpStatusCodeHandler"] = "Target of UseStatusCodePagesWithReExecute; challenging it would recurse.",
        ["ErrorController.Error"] = "Target of UseExceptionHandler; challenging it would recurse.",
        ["AccountController.AccessDenied"] = "Configured AccessDeniedPath; must be reachable after a failed authorization check.",
        ["AccountController.Logout"] = "Must work once the session is already gone.",
        ["AccountController.SignedOut"] = "Post-logout landing page; shown precisely when the user has no session."
    };

    /// <summary>
    /// State-changing actions that intentionally ship without an antiforgery token.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedMissingAntiForgery = new()
    {
        // Kept as a GET on purpose: consumers of this open-source project may have external logout
        // links (IdP, reverse proxy, bookmarks) pointing at it. Logout CSRF is accepted - the worst
        // outcome is an unwanted sign-out, with nothing read or written.
        ["AccountController.Logout"] = "Deliberately a GET for backwards compatibility; logout CSRF accepted."
    };

    public static TheoryData<string> AllActions() => [.. ActionsById.Keys];

    /// <summary>
    /// Actions keyed by a serializable id (name plus parameter types, to disambiguate GET/POST
    /// overloads) so Test Explorer can enumerate individual data rows. Each test resolves the id
    /// back to its MethodInfo, since MethodInfo itself cannot cross the serialization boundary.
    /// </summary>
    private static readonly SortedDictionary<string, MethodInfo> ActionsById =
        new(typeof(SubnetController).Assembly
            .GetTypes()
            .Where(IsController)
            .SelectMany(GetActions)
            .ToDictionary(Id, a => a));

    private static string Id(MethodInfo action) =>
        $"{Key(action)}({string.Join(", ", action.GetParameters().Select(p => p.ParameterType.Name))})";

    // -------------------------------------------------------------------------
    // Every action is guarded
    // -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllActions))]
    public void EveryAction_IsAuthorizedOrExplicitlyAnonymous(string actionId)
    {
        MethodInfo action = ActionsById[actionId];
        string key = Key(action);

        bool anonymous = HasAttribute<AllowAnonymousAttribute>(action);
        bool authorized = HasAttribute<AuthorizeAttribute>(action);

        if (AllowedAnonymousActions.ContainsKey(key))
        {
            Assert.True(anonymous, $"{key} is in the anonymous allow-list but is missing [AllowAnonymous]. The allow-list documents intent; the attribute is what the framework enforces.");
            return;
        }

        Assert.False(anonymous, $"{key} is marked [AllowAnonymous] but is not in the allow-list. If exposing it anonymously is intended, add it to {nameof(AllowedAnonymousActions)} with a reason.");
        Assert.True(authorized, $"{key} has no [Authorize] on the action or its controller. Add a policy, or add it to {nameof(AllowedAnonymousActions)} with a reason if it must be public.");
    }

    /// <summary>
    /// The fallback policy only requires an authenticated user, so an action that omits a policy is
    /// reachable by any signed-in user regardless of role. Require an explicit role policy.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllActions))]
    public void EveryAuthorizedAction_NamesARolePolicy(string actionId)
    {
        MethodInfo action = ActionsById[actionId];
        string key = Key(action);

        if (AllowedAnonymousActions.ContainsKey(key))
        {
            return;
        }

        // AccountController.Roles only reports the caller's own roles, so any authenticated user
        // may see it; a role policy would be wrong here.
        if (key == "AccountController.Roles")
        {
            return;
        }

        bool namesPolicy = GetAttributes<AuthorizeAttribute>(action).Any(a => !string.IsNullOrEmpty(a.Policy));

        Assert.True(namesPolicy, $"{key} is authorized but names no policy, so any authenticated user can reach it regardless of role. Specify one of the Require*Role policies.");
    }

    // -------------------------------------------------------------------------
    // State-changing actions are antiforgery protected
    // -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllActions))]
    public void EveryStateChangingAction_ValidatesAntiForgeryToken(string actionId)
    {
        MethodInfo action = ActionsById[actionId];
        string key = Key(action);

        if (!IsStateChanging(action) || AllowedMissingAntiForgery.ContainsKey(key))
        {
            return;
        }

        Assert.True(
            HasAttribute<ValidateAntiForgeryTokenAttribute>(action),
            $"{key} changes state but has no [ValidateAntiForgeryToken]. Add it, or add the action to {nameof(AllowedMissingAntiForgery)} with a reason.");
    }

    // -------------------------------------------------------------------------
    // The audit itself is wired up correctly
    // -------------------------------------------------------------------------

    [Fact]
    public void ActionDiscovery_FindsControllersAndActions()
    {
        List<string> keys = [.. ActionsById.Values.Select(Key)];

        // A refactor that breaks discovery would make every test above vacuously pass.
        Assert.Contains("SubnetController.BulkCreateFromAzurePlan", keys);
        Assert.Contains("AzureController.BulkImport", keys);
        Assert.Contains("ErrorController.Error", keys);
        Assert.True(keys.Count > 25, $"Only discovered {keys.Count} actions, which is fewer than this app has. Discovery is probably broken.");
    }

    [Fact]
    public void AnonymousAllowList_HasNoStaleEntries()
    {
        List<string> keys = [.. ActionsById.Values.Select(Key)];

        foreach (string allowed in AllowedAnonymousActions.Keys.Concat(AllowedMissingAntiForgery.Keys))
        {
            Assert.Contains(allowed, keys);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool IsController(Type type) =>
        typeof(Controller).IsAssignableFrom(type) && type is { IsAbstract: false, IsPublic: true };

    /// <summary>
    /// Public instance methods declared on the controller itself. Mirrors how MVC discovers actions:
    /// inherited framework members and [NonAction] methods are not routable.
    /// </summary>
    private static IEnumerable<MethodInfo> GetActions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m => m.GetCustomAttribute<NonActionAttribute>() is null);

    private static string Key(MethodInfo action) => $"{action.DeclaringType!.Name}.{action.Name}";

    /// <summary>
    /// Attributes on the action or on its controller. Partial controllers carry class-level
    /// attributes on the type, so this sees them from any partial file.
    /// </summary>
    private static IEnumerable<T> GetAttributes<T>(MethodInfo action) where T : Attribute =>
        action.GetCustomAttributes<T>(inherit: true)
            .Concat(action.DeclaringType!.GetCustomAttributes<T>(inherit: true));

    private static bool HasAttribute<T>(MethodInfo action) where T : Attribute =>
        GetAttributes<T>(action).Any();

    private static bool IsStateChanging(MethodInfo action) =>
        HasAttribute<HttpPostAttribute>(action)
        || HasAttribute<HttpPutAttribute>(action)
        || HasAttribute<HttpDeleteAttribute>(action)
        || HasAttribute<HttpPatchAttribute>(action);
}
