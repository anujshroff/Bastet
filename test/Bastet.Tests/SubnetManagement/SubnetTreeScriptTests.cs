using System.Text.RegularExpressions;

namespace Bastet.Tests.SubnetManagement;

public partial class SubnetTreeScriptTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string SiteScript = "src/Bastet/wwwroot/js/site.js";

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunPattern();

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bastet.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Bastet.sln not found above test base directory");
    }

    private static string ReadScript() =>
        File.ReadAllText(Path.Combine(RepoRoot, SiteScript.Replace('/', Path.DirectorySeparatorChar)));

    private static string CollapseAllHandler() => ClickHandlerBody("collapse-all");

    private static string ClickHandlerBody(string buttonId)
    {
        string script = ReadScript();
        Match match = Regex.Match(
            script,
            @"\$\('#" + buttonId + @"'\)\.on\('click',\s*function\s*\(\)\s*\{(?<body>.*?)\n\s*\}\);",
            RegexOptions.Singleline);

        Assert.True(match.Success, $"The #{buttonId} click handler was not found in site.js");
        return match.Groups["body"].Value;
    }

    [Fact]
    public void CollapseAll_IsOneStatement_SlidingUpEveryChildContainerAtEveryDepth()
    {
        string body = WhitespaceRunPattern().Replace(CollapseAllHandler(), " ").Trim();

        Assert.Equal("$('.subnet-children').slideUp(200).promise().done(updateToggleIcons);", body);
    }

    [Fact]
    public void ExpandAll_RepaintsTheIconsOnce_AfterEveryContainerHasFinished()
    {
        string body = WhitespaceRunPattern().Replace(ClickHandlerBody("expand-all"), " ").Trim();

        Assert.Equal("$('.subnet-children').slideDown(200).promise().done(updateToggleIcons);", body);
    }

    [Fact]
    public void CollapseAll_NeverRevealsAContainer_SoAHandCollapsedRootStaysCollapsed()
    {
        string body = CollapseAllHandler();

        Assert.DoesNotContain(".show(", body);
        Assert.DoesNotContain("slideDown", body);
        Assert.DoesNotContain("fadeIn", body);
        Assert.DoesNotContain("display", body);
    }

    [Fact]
    public void CollapseAll_SelectsContainersAtEveryDepth_SoATwoLevelTreeIsNotANoOp()
    {
        string body = CollapseAllHandler();

        Assert.DoesNotContain(".subnet-tree", body);
        Assert.DoesNotContain(".subnet-item", body);
        Assert.DoesNotContain("children(", body);
    }

    [Fact]
    public void CollapseAll_MirrorsExpandAll_OverTheSameUnqualifiedSelector()
    {
        Assert.Contains("$('.subnet-children').slideDown(200", ClickHandlerBody("expand-all"));
        Assert.Contains("$('.subnet-children').slideUp(200", CollapseAllHandler());
    }
}
