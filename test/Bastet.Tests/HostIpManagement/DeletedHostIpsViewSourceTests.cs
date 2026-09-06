namespace Bastet.Tests.HostIpManagement;

public class DeletedHostIpsViewSourceTests
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

    [Fact]
    public void DeletedHostIps_BackLinkGoesToSubnetDetails_NeverToTheGuardedIndex()
    {
        string view = ReadView("src/Bastet/Views/HostIp/DeletedHostIps.cshtml");

        Assert.Contains("asp-controller=\"Subnet\" asp-action=\"Details\" asp-route-id=\"@Model.SubnetId\"", view);
        Assert.DoesNotContain("asp-action=\"Index\"", view);
        Assert.DoesNotContain("Back to Host IPs", view);
        Assert.DoesNotContain("@inject", view);
    }
}
