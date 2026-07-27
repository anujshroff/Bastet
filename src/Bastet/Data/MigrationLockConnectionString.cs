using Microsoft.Data.SqlClient;

namespace Bastet.Data;

/// <summary>
/// Builds the connection strings for the dedicated connection that holds the
/// <c>Bastet:Migration</c> application lock while BASTET_AUTO_MIGRATE applies migrations.
///
/// Split out of Program.cs so the catalog choice is unit-testable. It is the only part of the
/// startup migration path that can be exercised without a real SQL Server, and it is the part
/// that has regressed: v3.3.0 pointed this connection unconditionally at master, which no
/// managed-identity deployment can open, and nothing in the suite noticed.
/// </summary>
public static class MigrationLockConnectionString
{
    /// <summary>
    /// The catalog the lock connection falls back to when the configured one does not exist yet.
    /// </summary>
    public const string BootstrapCatalog = "master";

    /// <summary>
    /// The connection string exactly as configured, catalog untouched. This is the connection the
    /// lock is taken on in every deployment where the database already exists, which is every
    /// healthy one.
    ///
    /// Do not redirect this to <see cref="BootstrapCatalog"/>. The documented deployment model
    /// (README, "Database Setup") is a user inside the application database - on Azure SQL that
    /// is a contained user with no login in master at all - so a lock connection that always
    /// opens master fails at startup with SQL 18456 before a single request is served.
    /// </summary>
    /// <remarks>
    /// Nullable in and out, matching <see cref="SqlConnection(string)"/> and
    /// <see cref="SqlConnectionStringBuilder(string)"/>, which both accept a null connection
    /// string. BASTET_CONNECTION_STRING is only guaranteed set outside Development, so a caller
    /// handing this a null is a pre-existing configuration error and should fail where it always
    /// has - on Open - rather than differently here.
    /// </remarks>
    public static string? Configured(string? connectionString) => connectionString;

    /// <summary>
    /// The same connection redirected to <see cref="BootstrapCatalog"/>, for the one case that
    /// needs it: the configured catalog does not exist, so <c>Migrate()</c> is about to create it
    /// and the lock has nowhere else to live. Holding the lock here means CREATE DATABASE happens
    /// inside it, which EF Core's own __EFMigrationsLock does not cover - two simultaneous cold
    /// starts against a missing catalog otherwise race and one dies with SQL 1801.
    ///
    /// Built with <see cref="SqlConnectionStringBuilder"/> rather than by editing the string, so
    /// authentication, encryption and server settings carry over verbatim.
    /// </summary>
    /// <remarks>Accepts a null connection string for the same reason <see cref="Configured"/> does.</remarks>
    public static string MasterBootstrap(string? connectionString) =>
        new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = BootstrapCatalog
        }.ConnectionString;
}
