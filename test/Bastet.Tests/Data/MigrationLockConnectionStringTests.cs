using Bastet.Data;
using Microsoft.Data.SqlClient;

namespace Bastet.Tests.Data;

public class MigrationLockConnectionStringTests
{

    private const string AzureManagedIdentityConnectionString =
        "Server=your-server.database.windows.net;Authentication=Active Directory Default;Encrypt=True;Database=bastet;";

    [Fact]
    public void Configured_PreservesTheCatalogTheOperatorAsked_For()
    {
        string? result = MigrationLockConnectionString.Configured(AzureManagedIdentityConnectionString);

        SqlConnectionStringBuilder builder = new(result);

        Assert.Equal("bastet", builder.InitialCatalog);
        Assert.NotEqual(MigrationLockConnectionString.BootstrapCatalog, builder.InitialCatalog);
    }

    [Theory]
    [InlineData("Server=localhost;Database=bastet;Trusted_Connection=True;TrustServerCertificate=True;")]
    [InlineData("Server=tcp:sql.example.com,1433;Initial Catalog=ipam_prod;User ID=svc;Password=p@ss;Encrypt=True;")]
    [InlineData(AzureManagedIdentityConnectionString)]
    public void Configured_NeverRewritesAnything(string connectionString)
    {
        Assert.Equal(connectionString, MigrationLockConnectionString.Configured(connectionString));
    }

    [Fact]
    public void MasterBootstrap_SwitchesTheCatalogToMaster()
    {
        string result = MigrationLockConnectionString.MasterBootstrap(AzureManagedIdentityConnectionString);

        SqlConnectionStringBuilder builder = new(result);

        Assert.Equal(MigrationLockConnectionString.BootstrapCatalog, builder.InitialCatalog);
    }

    [Fact]
    public void MasterBootstrap_CarriesServerAndAuthenticationSettingsOver()
    {
        string result = MigrationLockConnectionString.MasterBootstrap(AzureManagedIdentityConnectionString);

        SqlConnectionStringBuilder builder = new(result);

        Assert.Equal("your-server.database.windows.net", builder.DataSource);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryDefault, builder.Authentication);
        Assert.True(builder.Encrypt);
    }

    [Fact]
    public void MasterBootstrap_PreservesSqlLoginCredentials()
    {
        string result = MigrationLockConnectionString.MasterBootstrap(
            "Server=tcp:sql.example.com,1433;Initial Catalog=ipam_prod;User ID=svc;Password=p@ss;Encrypt=True;");

        SqlConnectionStringBuilder builder = new(result);

        Assert.Equal(MigrationLockConnectionString.BootstrapCatalog, builder.InitialCatalog);
        Assert.Equal("tcp:sql.example.com,1433", builder.DataSource);
        Assert.Equal("svc", builder.UserID);
        Assert.Equal("p@ss", builder.Password);
    }

    [Fact]
    public void MasterBootstrap_IsIdempotentWhenMasterIsAlreadyTheCatalog()
    {
        string result = MigrationLockConnectionString.MasterBootstrap(
            "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;");

        SqlConnectionStringBuilder builder = new(result);

        Assert.Equal(MigrationLockConnectionString.BootstrapCatalog, builder.InitialCatalog);
    }
}
