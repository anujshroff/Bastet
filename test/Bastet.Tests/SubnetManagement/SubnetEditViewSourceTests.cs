namespace Bastet.Tests.SubnetManagement;

public class SubnetEditViewSourceTests
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
    public void EditInformationSidebar_DescribesCidrAsModifiableOnlyWhenTheFormAllowsIt()
    {
        string sidebar = ReadView("src/Bastet/Views/Subnet/Edit/_InformationSidebar.cshtml");
        string form = ReadView("src/Bastet/Views/Subnet/Edit/_EditForm.cshtml");

        Assert.StartsWith("@model EditSubnetViewModel", sidebar);
        Assert.Contains("Model.IsAzureLinked", form);

        int linkedBranch = sidebar.IndexOf("@if (Model.IsAzureLinked)", StringComparison.Ordinal);
        int linkedText = sidebar.IndexOf("Network address and CIDR cannot be changed here.", StringComparison.Ordinal);
        int elseBranch = sidebar.IndexOf("else", linkedBranch, StringComparison.Ordinal);
        int unlinkedText = sidebar.IndexOf("Only the CIDR value can be modified.", StringComparison.Ordinal);
        int rules = sidebar.IndexOf("CIDR Modification Rules", StringComparison.Ordinal);

        Assert.True(linkedBranch >= 0 && linkedText > linkedBranch && elseBranch > linkedText && unlinkedText > elseBranch && rules > elseBranch);
        Assert.Equal(1, sidebar.Split("Only the CIDR value can be modified.").Length - 1);
        Assert.Contains("@if (!Model.IsAzureLinked)", sidebar);
    }
}
