using System.Text.RegularExpressions;

namespace Bastet.Tests.HostIpManagement;

public partial class HostIpViewSourceTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [GeneratedRegex(@"asp-validation-summary=|Html\.ValidationSummary\(|ModelState")]
    private static partial Regex ModelLevelRenderingPattern();

    [GeneratedRegex("alert-danger")]
    private static partial Regex AlertDangerPattern();

    [GeneratedRegex("<form[^>]*>")]
    private static partial Regex FormOpeningTagPattern();

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
        ModelLevelRenderingPattern().Count(view);

    private static IEnumerable<string> ViewsAHostIpPageCanComposeFrom() =>
        Directory.GetFiles(ViewPath("src/Bastet/Views"), "*.cshtml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(ViewPath("src/Bastet/Views/Shared"), "*.cshtml", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(ViewPath("src/Bastet/Views/HostIp"), "*.cshtml", SearchOption.AllDirectories));

    [Fact]
    public void HostIpPages_RenderModelLevelErrorsExactlyOnceEach_AcrossEveryViewTheyCanComposeFrom()
    {
        Assert.Equal(2, ViewsAHostIpPageCanComposeFrom().Sum(view => ModelLevelRenderings(File.ReadAllText(view))));
    }

    [Fact]
    public void HostIpEditSidebar_NamesAStepThatLoadsTheCurrentValues()
    {
        string sidebar = ReadView("src/Bastet/Views/HostIp/Edit/_SubnetInfo.cshtml");

        Assert.Contains(
            "Concurrent edits to this host IP by other users will be detected. If another user has modified this record, "
            + "use Cancel, then Edit on this host IP, and try again.",
            sidebar);
        Assert.DoesNotContain("reload", sidebar, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Create/_HostIpForm.cshtml", "<div asp-validation-summary=\"ModelOnly\" class=\"alert alert-danger\" role=\"alert\"></div>")]
    [InlineData("Edit/_Header.cshtml", "<div asp-validation-summary=\"ModelOnly\" class=\"alert alert-danger mb-4\" role=\"alert\"></div>")]
    public void HostIpPage_RendersItsModelLevelErrors_ThroughOneUnwrappedSummary(string owner, string summaryLine)
    {
        string ownerView = ReadView("src/Bastet/Views/HostIp/" + owner);
        Assert.Contains(summaryLine, ownerView);
        Assert.Single(AlertDangerPattern().Matches(ownerView));
    }

    [Theory]
    [InlineData("Delete/_DeleteConfirmationForm.cshtml", "Delete", "asp-route-ip=\"@Model.IP\"")]
    [InlineData("Edit/_EditForm.cshtml", "Edit", "asp-route-ip=\"@Model.IP\"")]
    [InlineData("Create/_HostIpForm.cshtml", "Create", "asp-route-subnetId=\"@Model.SubnetId\"")]
    public void HostIpForm_CarriesItsIdentifierInTheUrl_SoARepeatedGetAfterSignInLandsOnTheForm(string view, string action, string routeAttribute)
    {
        string form = ReadView("src/Bastet/Views/HostIp/" + view);

        MatchCollection forms = FormOpeningTagPattern().Matches(form);
        Match postForm = Assert.Single(forms, m => m.Value.Contains("method=\"post\""));
        Assert.Contains($"asp-action=\"{action}\"", postForm.Value);
        Assert.Contains(routeAttribute, postForm.Value);
    }
}
