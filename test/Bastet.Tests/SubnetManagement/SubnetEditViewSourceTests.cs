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

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    [Fact]
    public void EditInformationSidebar_DescribesCidrAsModifiableOnlyWhenTheFormAllowsIt()
    {
        string sidebar = ReadView("src/Bastet/Views/Subnet/Edit/_InformationSidebar.cshtml");
        string form = ReadView("src/Bastet/Views/Subnet/Edit/_EditForm.cshtml");

        Assert.StartsWith("@model EditSubnetViewModel", sidebar);
        Assert.Matches(@"@if \(Model\.IsAzureLinked\)\s*\{[^}]*<input[^>]*readonly[^>]*>", form);

        Assert.Matches(@"@if \(!Model\.IsAzureLinked\)\s*\{\s*<li><strong>CIDR</strong>[^}]*\}", sidebar);
        Assert.Matches(@"@if \(Model\.IsAzureLinked\)\s*\{[^}]*Network address and CIDR cannot be changed here\.[^}]*\}\s*else\s*\{[^}]*Only the CIDR value can be modified\.[^}]*CIDR Modification Rules[^}]*\}", sidebar);
        Assert.Equal(1, Occurrences(sidebar, "<li><strong>CIDR</strong>"));
        Assert.Equal(1, Occurrences(sidebar, "Network address and CIDR cannot be changed here."));
        Assert.Equal(1, Occurrences(sidebar, "Only the CIDR value can be modified."));
        Assert.Equal(1, Occurrences(sidebar, "CIDR Modification Rules"));
    }
}
