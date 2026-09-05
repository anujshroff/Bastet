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

        Assert.Matches(
            @"if \(!payload\)\s*\{\s*payload\s*=\s*xhr\.status\s*===\s*0\s*\?\s*\{\s*error:\s*""The server could not be reached, so it is unknown whether the change was applied\. Check the subnet list before retrying\.""\s*\}\s*:\s*\{\s*error:\s*""The server returned status ""\s*\+\s*xhr\.status\s*\+\s*""\.""\s*\}\s*;\s*\}",
            view);
        Assert.DoesNotContain("Server error: ", view);
    }

    [Fact]
    public void WizardPages_RenderOneSharedErrorAlert_ThatCarriesNoConnectivityHeadline()
    {
        string bulkImportPage = ReadView("src/Bastet/Views/Azure/BulkImport.cshtml");
        string reconcilePage = ReadView("src/Bastet/Views/Azure/Reconcile.cshtml");
        string azureViews = Path.Combine(RepoRoot, "src", "Bastet", "Views", "Azure");
        string[] errorAlertPartials = Directory.GetFiles(azureViews, "_ErrorAlert.cshtml", SearchOption.AllDirectories);

        string sharedPartial = Assert.Single(errorAlertPartials);
        Assert.Equal(Path.Combine(azureViews, "_ErrorAlert.cshtml"), sharedPartial);
        Assert.Contains("Html.PartialAsync(\"_ErrorAlert\")", bulkImportPage);
        Assert.Contains("Html.PartialAsync(\"_ErrorAlert\")", reconcilePage);
        Assert.DoesNotContain("Could not connect to Azure.", File.ReadAllText(sharedPartial));
        Assert.DoesNotContain("Could not connect to Azure.", bulkImportPage);
        Assert.DoesNotContain("Could not connect to Azure.", reconcilePage);
    }
}
