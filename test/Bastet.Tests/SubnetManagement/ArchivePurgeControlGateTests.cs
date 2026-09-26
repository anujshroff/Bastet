using System.Text.RegularExpressions;

namespace Bastet.Tests.SubnetManagement;

public partial class ArchivePurgeControlGateTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string SubnetArchiveHeader = "src/Bastet/Views/Subnet/DeletedSubnets/_Header.cshtml";
    private const string HostIpArchiveView = "src/Bastet/Views/HostIp/AllDeletedHostIps.cshtml";
    private const string EditSidebar = "src/Bastet/Views/HostIp/Edit/_SubnetInfo.cshtml";

    [GeneratedRegex(@"asp-action=""PurgeAll\w+""")]
    private static partial Regex PurgeAllActionPattern();

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

    private static string GuardAbove(string view, string anchor)
    {
        int index = view.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(index >= 0, $"anchor not found: {anchor}");

        string before = view[..index];
        int guard = before.LastIndexOf("@if (", StringComparison.Ordinal);
        Assert.True(guard >= 0, $"no @if guard above: {anchor}");

        return before[guard..];
    }

    [Fact]
    public void SubnetArchive_HidesPurgeAll_WhenTheArchiveIsEmpty()
    {
        string guard = GuardAbove(ReadView(SubnetArchiveHeader), "PurgeAllDeletedSubnets");

        Assert.Contains("Model.TotalCount > 0", guard);
        Assert.Contains("ApplicationRoles.Admin", guard);
    }

    [Fact]
    public void HostIpArchive_KeepsTheSameGate_SoTheTwoArchivesDoNotDrift()
    {
        string guard = GuardAbove(ReadView(HostIpArchiveView), "PurgeAllDeletedHostIps");

        Assert.Contains("Model.TotalCount > 0", guard);
        Assert.Contains("ApplicationRoles.Admin", guard);
    }

    [Fact]
    public void SubnetArchiveHeader_DeclaresTheModelItGatesOn()
    {
        string view = ReadView(SubnetArchiveHeader);

        Assert.StartsWith("@model DeletedSubnetListViewModel", view);
    }

    [Fact]
    public void EveryPurgeAllEntryPoint_IsGatedOnACount()
    {
        string viewsRoot = Path.Combine(RepoRoot, "src", "Bastet", "Views");

        List<string> ungated =
        [
            .. Directory.EnumerateFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).StartsWith("PurgeAll", StringComparison.Ordinal))
                .Where(path =>
                {
                    string text = File.ReadAllText(path);
                    return PurgeAllActionPattern().IsMatch(text)
                        && !GuardAbove(text, "asp-action=\"PurgeAll").Contains("TotalCount > 0");
                })
                .Select(path => Path.GetRelativePath(RepoRoot, path))
        ];

        Assert.Empty(ungated);
    }

    [Fact]
    public void HostIpEditSidebar_GatesTheDeleteRemedy_OnTheDeleteRole()
    {
        string sidebar = ReadView(EditSidebar);

        Assert.Contains("UserContextService.UserHasRole(Bastet.Models.ApplicationRoles.Delete)", sidebar);

        string withDeleteRole = "Host IP addresses cannot be changed. If you need a different IP, delete this one and create a new host IP assignment.";
        string withoutDeleteRole = "Host IP addresses cannot be changed. If you need a different IP, ask someone with the Delete role to delete this one, then create a new host IP assignment.";

        Assert.Contains(withDeleteRole, sidebar);
        Assert.Contains(withoutDeleteRole, sidebar);

        int guard = sidebar.IndexOf("UserHasRole(Bastet.Models.ApplicationRoles.Delete)", StringComparison.Ordinal);
        int direct = sidebar.IndexOf(withDeleteRole, StringComparison.Ordinal);
        int indirect = sidebar.IndexOf(withoutDeleteRole, StringComparison.Ordinal);
        int elseBranch = sidebar.IndexOf("else", guard, StringComparison.Ordinal);

        Assert.True(guard < direct, "the direct remedy must sit inside the Delete-role branch");
        Assert.True(direct < elseBranch, "the direct remedy must precede the else branch");
        Assert.True(elseBranch < indirect, "the role-neutral remedy must sit in the else branch");
    }

    [Fact]
    public void NoViewSentence_NamesADeleteRemedyWithoutGatingIt()
    {
        string viewsRoot = Path.Combine(RepoRoot, "src", "Bastet", "Views");

        List<string> offenders =
        [
            .. Directory.EnumerateFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories)
                .Where(path =>
                {
                    string text = File.ReadAllText(path);
                    return text.Contains("delete this one and create a new host IP assignment")
                        && !text.Contains("UserHasRole(Bastet.Models.ApplicationRoles.Delete)");
                })
                .Select(path => Path.GetRelativePath(RepoRoot, path))
        ];

        Assert.Empty(offenders);
    }
}
