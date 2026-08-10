using Microsoft.Data.SqlClient;

namespace Bastet.Data;

public static class MigrationLockConnectionString
{

    public const string BootstrapCatalog = "master";

    public static string? Configured(string? connectionString) => connectionString;

    public static string LockResource(string? connectionString)
    {
        string catalog = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        return $"Bastet:Migration:{catalog}".ToLowerInvariant();
    }

    public static string MasterBootstrap(string? connectionString) =>
        new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = BootstrapCatalog
        }.ConnectionString;
}
