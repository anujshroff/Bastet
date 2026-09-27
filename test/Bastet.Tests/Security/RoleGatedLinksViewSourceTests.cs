using System.Text.RegularExpressions;

namespace Bastet.Tests.Security;

public partial class RoleGatedLinksViewSourceTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string Gate = @"@if \(!UserContextService\.IsSignedInWithoutRole\(ApplicationRoles\.View\)\)\s*\{";

    [GeneratedRegex(Gate)]
    private static partial Regex GatePattern();

    [GeneratedRegex(Gate + @"\s*<a href=""/"" class=""btn btn-primary"">Return to Home</a>\s*\}")]
    private static partial Regex GatedReturnToHomePattern();

    [GeneratedRegex("Return to Home")]
    private static partial Regex ReturnToHomeTextPattern();

    [GeneratedRegex(@"href=""/""")]
    private static partial Regex RootHrefPattern();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bastet.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Bastet.sln not found above test base directory");
    }

    private static string ReadView(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void AccessDenied_OffersReturnToHome_OnlyToAReaderWhoCanOpenHome()
    {
        string view = ReadView("src/Bastet/Views/Account/AccessDenied.cshtml");

        Assert.Contains("@inject IUserContextService UserContextService", view);
        Match gated = GatedReturnToHomePattern().Match(view);
        Assert.True(gated.Success, "Return to Home must sit inside the View-role gate");
        Assert.Single(ReturnToHomeTextPattern().Matches(view));
        Assert.True(view.IndexOf("asp-action=\"Logout\"", StringComparison.Ordinal) > gated.Index + gated.Length,
            "Logout must stay outside the gate: it is the one link a role-less reader can use");
        Assert.Contains("Please contact your administrator", view);
    }

    [Fact]
    public void ErrorLayout_OffersReturnToHome_OnlyToAReaderWhoCanOpenHome()
    {
        string view = ReadView("src/Bastet/Views/Shared/_ErrorLayout.cshtml");

        Assert.Contains("@inject IUserContextService UserContextService", view);
        Match gated = GatedReturnToHomePattern().Match(view);
        Assert.True(gated.Success, "Return to Home must sit inside the View-role gate");
        Assert.Single(ReturnToHomeTextPattern().Matches(view));
        Assert.True(view.IndexOf("history.back()", StringComparison.Ordinal) > gated.Index + gated.Length,
            "Go Back must stay outside the gate");
    }

    [Fact]
    public void Layout_LinksTheBrandAndTheSubnetAndHostIpMenus_OnlyForAReaderWhoCanOpenThem()
    {
        string view = ReadView("src/Bastet/Views/Shared/_Layout.cshtml");

        Assert.Matches(
            @"@if \(UserContextService\.IsSignedInWithoutRole\(ApplicationRoles\.View\)\)\s*\{\s*<span class=""navbar-brand"">BASTET</span>\s*\}\s*else\s*\{\s*<a class=""navbar-brand"" href=""/"">BASTET</a>\s*\}",
            view);
        Assert.Single(RootHrefPattern().Matches(view));

        Match menuGate = GatePattern().Match(view);
        Assert.True(menuGate.Success, "the Subnets and Host IPs menus must sit inside the View-role gate");
        int azureBlock = view.IndexOf("bulkAzureImportEnabled", StringComparison.Ordinal);
        foreach (string action in new[] { "asp-action=\"Index\"", "asp-action=\"DeletedSubnets\"", "asp-action=\"AllHostIps\"", "asp-action=\"AllDeletedHostIps\"" })
        {
            int link = view.IndexOf(action, StringComparison.Ordinal);
            Assert.True(link > menuGate.Index && link < azureBlock, $"{action} must be inside the View-role gate that precedes the Azure links");
        }

        Assert.Contains("asp-action=\"Roles\"", view);
        Assert.Contains("asp-action=\"Logout\"", view);
    }
}
