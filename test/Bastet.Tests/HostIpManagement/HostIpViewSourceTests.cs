using System.Text.RegularExpressions;

namespace Bastet.Tests.HostIpManagement;

public class HostIpViewSourceTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bastet.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Bastet.sln not found above test base directory");
    }

    private static string ViewPath(string relativePath) =>
        Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ReadView(string relativePath) => File.ReadAllText(ViewPath(relativePath));

    private static int ModelOnlySummaries(string view) =>
        Regex.Matches(view, @"asp-validation-summary=""ModelOnly""").Count;

    [Fact]
    public void HostIpCreateAndEdit_RenderModelLevelErrorsExactlyOnce()
    {
        string hostIpViews = ViewPath("src/Bastet/Views/HostIp");

        Assert.Empty(Directory.GetFiles(hostIpViews, "_ErrorAlert.cshtml", SearchOption.AllDirectories));
        Assert.Equal(0, ModelOnlySummaries(ReadView("src/Bastet/Views/HostIp/Create.cshtml")));
        Assert.DoesNotContain("_ErrorAlert", ReadView("src/Bastet/Views/HostIp/Create.cshtml"));
        Assert.Equal(1, ModelOnlySummaries(ReadView("src/Bastet/Views/HostIp/Create/_HostIpForm.cshtml")));

        string editHeader = ReadView("src/Bastet/Views/HostIp/Edit/_Header.cshtml");
        Assert.Equal(1, ModelOnlySummaries(editHeader));
        Assert.DoesNotContain("ModelState.ErrorCount", editHeader);
        Assert.DoesNotContain("alert-heading", editHeader);
    }
}
