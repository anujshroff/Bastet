using Bastet.Data;

namespace Bastet.Tests.Data;

public class MigrationLockResourceTests
{
    private static string Conn(string catalog) =>
        $"Server=db;Database={catalog};User Id=sa;Password=x;TrustServerCertificate=True;";

    [Fact]
    public void TheResourceNamesTheCatalog_SoUnrelatedDeploymentsDoNotShareIt()
    {
        Assert.Equal("bastet:migration:bastet_teama",
            MigrationLockConnectionString.LockResource(Conn("bastet_teamA")));

        Assert.NotEqual(
            MigrationLockConnectionString.LockResource(Conn("bastet_teamA")),
            MigrationLockConnectionString.LockResource(Conn("bastet_teamB")));
    }

    [Fact]
    public void TwoInstancesOfTheSameDeployment_ShareTheResource_EvenWhenTheCatalogIsCasedDifferently()
    {
        Assert.Equal(
            MigrationLockConnectionString.LockResource(Conn("Bastet_Prod")),
            MigrationLockConnectionString.LockResource(Conn("bastet_prod")));
    }

    [Fact]
    public void TheResourceFitsSqlServersLimit_ForTheLongestLegalCatalogName()
    {
        string longest = new('n', 128);
        Assert.True(MigrationLockConnectionString.LockResource(Conn(longest)).Length <= 255);
    }

    [Fact]
    public void AConnectionStringWithNoCatalog_StillYieldsAStableResource()
    {
        string resource = MigrationLockConnectionString.LockResource("Server=db;User Id=sa;Password=x;");
        Assert.Equal("bastet:migration:", resource);
    }
}
