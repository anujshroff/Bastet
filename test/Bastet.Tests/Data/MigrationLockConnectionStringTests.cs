using Bastet.Data;
using Microsoft.Data.SqlClient;

namespace Bastet.Tests.Data;

/// <summary>
/// Guards the catalog the BASTET_AUTO_MIGRATE application lock connects to.
///
/// Scope, stated plainly: these cover the catalog choice only. The ordering that uses them - the
/// configured catalog is opened first and master is tried only after SQL 4060 - lives in Program.cs
/// and needs a real SQL Server to exercise, which the SQLite suite cannot reach. That gap is why
/// v3.3.0 could redirect this connection to master unconditionally and ship green.
/// </summary>
public class MigrationLockConnectionStringTests
{
    // The form README documents for BASTET_CONNECTION_STRING: Azure SQL, managed identity, and a
    // named catalog that is not master.
    private const string AzureManagedIdentityConnectionString =
        "Server=your-server.database.windows.net;Authentication=Active Directory Default;Encrypt=True;Database=bastet;";

    [Fact]
    public void Configured_PreservesTheCatalogTheOperatorAsked_For()
    {
        string? result = MigrationLockConnectionString.Configured(AzureManagedIdentityConnectionString);

        SqlConnectionStringBuilder builder = new(result);

        // The regression this file exists for. A managed identity is a contained user in 'bastet'
        // and has no login in master, so anything but 'bastet' here is a startup crash (SQL 18456).
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

        // Redirecting the catalog must not quietly drop the credential or the encryption setting
        // with it - the bootstrap connection has to authenticate the same way the configured one does.
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
