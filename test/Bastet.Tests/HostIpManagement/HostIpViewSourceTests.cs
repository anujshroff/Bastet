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

    private static int ModelLevelRenderings(string view) =>
        Regex.Matches(view, @"asp-validation-summary=|Html\.ValidationSummary\(|ModelState").Count;

    private static IEnumerable<string> ViewsAHostIpPageCanComposeFrom() =>
        Directory.GetFiles(ViewPath("src/Bastet/Views"), "*.cshtml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(ViewPath("src/Bastet/Views/Shared"), "*.cshtml", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(ViewPath("src/Bastet/Views/HostIp"), "*.cshtml", SearchOption.AllDirectories));

    [Fact]
    public void HostIpPages_RenderModelLevelErrorsExactlyOnceEach_AcrossEveryViewTheyCanComposeFrom()
    {
        Assert.Equal(2, ViewsAHostIpPageCanComposeFrom().Sum(view => ModelLevelRenderings(File.ReadAllText(view))));
    }

    [Theory]
    [InlineData("Create/_HostIpForm.cshtml", "<div asp-validation-summary=\"ModelOnly\" class=\"alert alert-danger\" role=\"alert\"></div>")]
    [InlineData("Edit/_Header.cshtml", "<div asp-validation-summary=\"ModelOnly\" class=\"alert alert-danger mb-4\" role=\"alert\"></div>")]
    public void HostIpPage_RendersItsModelLevelErrors_ThroughOneUnwrappedSummary(string owner, string summaryLine)
    {
        string ownerView = ReadView("src/Bastet/Views/HostIp/" + owner);
        Assert.Contains(summaryLine, ownerView);
        Assert.Single(Regex.Matches(ownerView, "alert-danger"));
    }
}
