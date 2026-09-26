using System.Text.RegularExpressions;

namespace Bastet.Tests.Security;

public class SignInFailedViewSourceTests
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

    private static string ReadView(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void SignInFailed_NamesWhoCanFixAPersistentFailure_AfterTheRoutineCauses()
    {
        string view = ReadView("src/Bastet/Views/Account/SignInFailed.cshtml");

        Assert.Matches(
            @"Nothing is wrong with your account, and nothing was changed\.</p>\s*<p>If this happens every time you try, contact your administrator\.</p>",
            view);
        Assert.Single(Regex.Matches(view, "contact your administrator"));
        Assert.Contains("Try signing in again", view);
        Assert.DoesNotContain("log", view, StringComparison.OrdinalIgnoreCase);
    }
}
