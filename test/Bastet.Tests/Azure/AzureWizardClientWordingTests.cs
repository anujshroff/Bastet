using System.Text.RegularExpressions;

namespace Bastet.Tests.Azure;

public partial class AzureWizardClientWordingTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [GeneratedRegex(@"(?<!function )readErrorMessage\(xhr\)")]
    private static partial Regex ReadErrorMessageCallPattern();

    [GeneratedRegex(@"function showCommitError\(payload,\s*status\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline)]
    private static partial Regex ShowCommitErrorPattern();

    [GeneratedRegex(@"if\s*\(status\s*===\s*409\)\s*\{", RegexOptions.Singleline)]
    private static partial Regex Status409BranchPattern();

    [GeneratedRegex(@"function voidConfirmation\(\)\s*\{(?:(?!activateTab).)*?\n        \}", RegexOptions.Singleline)]
    private static partial Regex VoidConfirmationWithoutTabSwitchPattern();

    [GeneratedRegex(@"function invalidateConfirmation\(\)\s*\{\s*voidConfirmation\(\);", RegexOptions.Singleline)]
    private static partial Regex InvalidateConfirmationDelegatesPattern();

    [GeneratedRegex(@"function renderPlan\(plan\)\s*\{(?<body>.{0,400})", RegexOptions.Singleline)]
    private static partial Regex RenderPlanHeadPattern();

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

    public static TheoryData<string> StepSubscriptionPartials =>
    [
        "src/Bastet/Views/Azure/BulkImport/_StepSubscription.cshtml",
        "src/Bastet/Views/Azure/Reconcile/_StepSubscription.cshtml",
    ];

    public static TheoryData<string> WizardScriptPartials =>
    [
        "src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml",
        "src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml",
    ];

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
            @"if \(!payload\)\s*\{\s*payload\s*=\s*xhr\.status\s*===\s*0\s*\?\s*\{\s*error:\s*""The server could not be reached, so it is unknown whether the change was applied\. Check the subnet list before retrying; if you are asked to sign in, sign in and [^""]+\.""\s*\}\s*:\s*\{\s*error:\s*""The server returned status ""\s*\+\s*xhr\.status\s*\+\s*""\.""\s*\}\s*;\s*\}",
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

    [Theory]
    [MemberData(nameof(WizardScriptPartials))]
    public void ReadErrorHandlers_BranchOnUnreachableServer_AndNeverBlameTheConnectionForAnHttpAnswer(string viewPath)
    {
        string view = ReadView(viewPath);

        Assert.Matches(
            @"function readErrorMessage\(xhr\)\s*\{\s*return xhr\.status\s*===\s*0\s*\?\s*""The server could not be reached, or your sign-in has expired\. Reload this page and try again\.""\s*:\s*""The server returned status ""\s*\+\s*xhr\.status\s*\+\s*""\.""\s*;\s*\}",
            view);
        Assert.DoesNotContain("Error connecting to server", view);

        int readHandlers = viewPath.Contains("BulkImport") ? 3 : 2;
        Assert.Equal(readHandlers, ReadErrorMessageCallPattern().Count(view));
    }

    [Fact]
    public void ReconcileScanError_DoesNotBlameAzureForEveryFailure()
    {
        string stepReview = ReadView("src/Bastet/Views/Azure/Reconcile/_StepReview.cshtml");

        Assert.DoesNotContain("Because Azure could not be read", stepReview);
        Assert.DoesNotContain("Fix the connection", stepReview);
        Assert.Contains("Fix the problem shown", stepReview);
    }

    public static TheoryData<string> WizardScripts =>
    [
        "src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml",
        "src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml",
    ];

    [Theory]
    [MemberData(nameof(WizardScripts))]
    public void StatusZeroBanner_NamesTheSignInCause_AndARemedyTheReaderCanReach(string script)
    {
        string text = ReadView(script);

        Assert.Contains(
            "\"The server could not be reached, or your sign-in has expired. Reload this page and try again.\"",
            text);
        Assert.DoesNotContain("\"The server could not be reached.\"", text);
    }

    [Theory]
    [MemberData(nameof(WizardScripts))]
    public void OutcomeUnknownBanner_KeepsItsHedge_AndAddsTheSignInStep(string script)
    {
        string text = ReadView(script);

        Assert.Contains("so it is unknown whether the change was applied", text);
        Assert.Contains("if you are asked to sign in, sign in and", text);
        Assert.DoesNotContain("Check the subnet list before retrying.\"", text);
    }

    [Fact]
    public void EachWizard_NamesItsOwnNextStep_NeverTheOtherWizards()
    {
        string bulk = ReadView("src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml");
        string reconcile = ReadView("src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml");

        Assert.Contains("sign in and run the import again", bulk);
        Assert.DoesNotContain("sign in and scan again", bulk);

        Assert.Contains("sign in and scan again", reconcile);
        Assert.DoesNotContain("sign in and run the import again", reconcile);
    }

    [Theory]
    [MemberData(nameof(WizardScripts))]
    public void AStalePlan409_VoidsTheSnapshot_RatherThanReOfferingIt(string script)
    {
        string body = ShowCommitErrorBody(script);
        Assert.Matches(Status409BranchPattern(), body);

        int branch = body.IndexOf("status === 409", StringComparison.Ordinal);
        int reEnable = body.IndexOf("prop(\"disabled\", false)", StringComparison.Ordinal);
        if (reEnable >= 0)
        {
            Assert.True(branch < reEnable, "the 409 branch must return before anything is re-enabled");
        }
    }

    [Fact]
    public void ABulk409_InvalidatesThePlan_SoTheStaleSelectionCannotBeReposted()
    {
        string body = ShowCommitErrorBody("src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml");

        Assert.Contains("invalidatePlan();", body);
        Assert.Contains("return;", body);
    }

    [Fact]
    public void AReconcile409_VoidsTheConfirmationAndThePlan_WithoutBouncingOffTheBanner()
    {
        string body = ShowCommitErrorBody("src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml");

        Assert.Contains("lastPlan = null;", body);
        Assert.Contains("voidConfirmation();", body);
        Assert.DoesNotContain("invalidateConfirmation();", body);
    }

    [Fact]
    public void VoidConfirmation_IsTheStateHalf_AndInvalidateConfirmationAddsTheBounce()
    {
        string text = ReadView("src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml");

        Assert.Matches(VoidConfirmationWithoutTabSwitchPattern(), text);
        Assert.Matches(InvalidateConfirmationDelegatesPattern(), text);
    }

    [Theory]
    [InlineData("src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml", "#bulk-go-commit-btn", "#bulk-commit-error")]
    [InlineData("src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml", "#rec-go-confirm-btn", "#rec-commit-error")]
    public void ReEnteringTheCommitStep_NeverHidesARefusalThatStillStands(
        string script, string stepButton, string errorPanel)
    {
        string text = ReadView(script);

        Match handler = Regex.Match(
            text,
            Regex.Escape($"$(\"{stepButton}\").on(\"click\"") + @".*?\n        \}\);",
            RegexOptions.Singleline);
        Assert.True(handler.Success, $"the {stepButton} click handler was not found in {script}");

        Assert.DoesNotContain($"$(\"{errorPanel}\").addClass(\"d-none\")", handler.Value);
    }

    [Theory]
    [MemberData(nameof(WizardScripts))]
    public void TheRealStatusReachesTheErrorHandler_OrTheBranchIsInert(string script)
    {
        string text = ReadView(script);

        Assert.Contains("showCommitError(payload, xhr.status);", text);
        Assert.Contains("showCommitError(result, 0);", text);
        Assert.DoesNotContain("showCommitError(payload, 0)", text);
        Assert.DoesNotContain("showCommitError(result, 409)", text);
    }

    [Theory]
    [InlineData("src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml", "#bulk-commit-error")]
    [InlineData("src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml", "#rec-commit-error")]
    public void AFreshPlan_ClearsARefusalThatNoLongerStands(string script, string errorPanel)
    {
        string text = ReadView(script);

        Match render = RenderPlanHeadPattern().Match(text);
        Assert.True(render.Success, $"renderPlan was not found in {script}");

        Assert.Contains($"$(\"{errorPanel}\").addClass(\"d-none\")", render.Groups["body"].Value);
    }

    [Fact]
    public void AReconcile409_AlsoDisablesTheStepTwoForwardButton_AsBulkDoes()
    {
        string body = ShowCommitErrorBody("src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml");

        Assert.Contains("updateGoConfirmBtn();", body);

        Assert.True(
            body.IndexOf("lastPlan = null;", StringComparison.Ordinal)
                < body.IndexOf("updateGoConfirmBtn();", StringComparison.Ordinal),
            "the snapshot must be voided before the forward button is re-evaluated");
    }

    [Theory]
    [InlineData("src/Bastet/Views/Azure/BulkImport/_BulkScripts.cshtml",
        @"error:\s*function\s*\(xhr\)\s*\{\s*if\s*\(selection\s*!==\s*lastSelection\)\s*\{\s*\$\(""#bulk-confirm-commit-btn""\)\.prop\(""disabled"",\s*false\);\s*return;\s*\}")]
    [InlineData("src/Bastet/Views/Azure/Reconcile/_ReconcileScripts.cshtml",
        @"error:\s*function\s*\(xhr\)\s*\{\s*if\s*\(ids\s*!==\s*confirmedIds\)\s*\{\s*return;\s*\}")]
    public void AnAnswerToASupersededSnapshot_IsDroppedBeforeItCanVoidTheFreshOne(string script, string guard)
    {
        Assert.Matches(new Regex(guard, RegexOptions.Singleline), ReadView(script));
    }

    private static string ShowCommitErrorBody(string script)
    {
        Match handler = ShowCommitErrorPattern().Match(ReadView(script));
        Assert.True(handler.Success, $"showCommitError must take the status in {script}");
        return handler.Groups["body"].Value;
    }
}
