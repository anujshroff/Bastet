namespace Bastet.Tests.Azure;

public class AzureWizardClientWordingTests
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

    public static TheoryData<string> StepSubscriptionPartials => new()
    {
        "src/Bastet/Views/Azure/BulkImport/_StepSubscription.cshtml",
        "src/Bastet/Views/Azure/Reconcile/_StepSubscription.cshtml",
    };

    public static TheoryData<string> WizardScriptPartials => new()
    {
        "src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml",
        "src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml",
    };

    [Theory]
    [MemberData(nameof(StepSubscriptionPartials))]
    public void NoSubscriptionsPanel_NamesTheGrantAccessRemedy_AndNotTheCredentialRemedy(string viewPath)
    {
        string view = ReadView(viewPath);

        Assert.Contains(
            "This credential cannot see any subscriptions. Grant it access to a subscription and reload this page.",
            view);
        Assert.DoesNotContain("Please check your Azure credentials", view);
    }

    [Theory]
    [MemberData(nameof(WizardScriptPartials))]
    public void CommitErrorHandler_BranchesOnUnreachableServer_AndNeverAssertsServerErrorZero(string viewPath)
    {
        string view = ReadView(viewPath);

        Assert.Contains("xhr.status === 0", view);
        Assert.Contains(
            "The server could not be reached, so it is unknown whether the change was applied. Check the subnet list before retrying.",
            view);
        Assert.Contains("The server returned status \" + xhr.status", view);
        Assert.DoesNotContain("Server error: ", view);
    }
}
