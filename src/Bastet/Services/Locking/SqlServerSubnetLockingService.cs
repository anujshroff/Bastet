using Bastet.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Bastet.Services.Locking;

/// <summary>
/// SQL Server implementation of subnet locking using application locks (sp_getapplock).
/// Works with SQL Server, SQL LocalDB, and Azure SQL Database.
/// </summary>
/// <remarks>
/// The lock is session-owned rather than transaction-owned so that callers keep full control of
/// their own transactions: several guarded paths open a transaction, mutate multiple tables, and
/// roll back on validation failures - a lock that owned the transaction would either nest or force
/// every failure path through an exception. The connection is explicitly opened for the duration
/// so EF cannot return it to the pool (which would silently drop a session lock), and the lock is
/// released in a finally; if the process dies, the lock dies with the connection.
/// </remarks>
public class SqlServerSubnetLockingService(BastetDbContext context, ILogger<SqlServerSubnetLockingService> logger) : ISubnetLockingService
{
    private const int DEFAULT_TIMEOUT_MS = 30000; // 30 seconds
    private const string SUBNET_OPERATIONS_LOCK = "Bastet:SubnetOperations";

    /// <summary>SQL Server's error number for a client-side command timeout.</summary>
    private const int SQL_TIMEOUT_ERROR_NUMBER = -2;

    /// <inheritdoc />
    public async Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation, TimeSpan? timeout = null)
    {
        int timeoutMs = (int)(timeout?.TotalMilliseconds ?? DEFAULT_TIMEOUT_MS);

        // Keep the session (and with it the session-owned lock) alive across the operation.
        await context.Database.OpenConnectionAsync();
        try
        {
            int lockResult = await AcquireAppLockAsync(SUBNET_OPERATIONS_LOCK, timeoutMs);
            if (lockResult < 0)
            {
                throw new TimeoutException($"Could not acquire subnet operation lock within {timeoutMs}ms (result code: {lockResult})");
            }

            try
            {
                return await operation();
            }
            finally
            {
                // A failed release must not become the caller's error. Every guarded path commits
                // inside operation(), so by now the work is durable: rethrowing here would report a
                // completed operation as failed, and - because an exception raised in a finally
                // replaces the one in flight - would also destroy the original error when the
                // operation itself was what failed. Swallowing does not change the lock's fate: if
                // the connection died the session died with it and SQL Server has already dropped
                // the session-scoped lock, and if it is alive the outer finally closes it anyway.
                try
                {
                    await ReleaseAppLockAsync(SUBNET_OPERATIONS_LOCK);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to release the subnet operation lock after the operation completed");
                }
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Acquires a session-owned application lock using sp_getapplock
    /// </summary>
    /// <param name="resource">The lock resource name</param>
    /// <param name="timeoutMs">Timeout in milliseconds</param>
    /// <returns>Lock result code: 0=success, 1=granted after wait, negative=failure</returns>
    private async Task<int> AcquireAppLockAsync(string resource, int timeoutMs)
    {
        SqlParameter[] parameters =
        [
            new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = resource },
            new SqlParameter("@LockMode", SqlDbType.VarChar, 32) { Value = "Exclusive" },
            new SqlParameter("@LockOwner", SqlDbType.VarChar, 32) { Value = "Session" },
            new SqlParameter("@LockTimeout", SqlDbType.Int) { Value = timeoutMs },
            new SqlParameter("@Result", SqlDbType.Int) { Direction = ParameterDirection.Output }
        ];

        // The command has to outlive the wait it is asking the server to perform. SqlClient's default
        // command timeout is 30s, exactly the default @LockTimeout, so the two race: when the client
        // wins it throws SqlException instead of returning a result code, and every caller here
        // catches TimeoutException, so a contended operation surfaces as a generic 500 rather than
        // "another operation is in progress". Program.cs applies the same rule to the migration lock.
        // The context is request-scoped and shared with the caller's own queries, so restore it after.
        int? originalCommandTimeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout((timeoutMs / 1000) + 30);

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                "EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = @LockMode, @LockOwner = @LockOwner, @LockTimeout = @LockTimeout",
                parameters);
        }
        catch (SqlException ex) when (ex.Number == SQL_TIMEOUT_ERROR_NUMBER)
        {
            // Unreachable while the command timeout above exceeds the lock timeout, but a timeout
            // imposed elsewhere (connection settings, a proxy) must still reach callers as the
            // failure this method documents rather than as an unhandled provider exception.
            throw new TimeoutException($"Could not acquire subnet operation lock within {timeoutMs}ms (the command timed out).", ex);
        }
        finally
        {
            context.Database.SetCommandTimeout(originalCommandTimeout);
        }

        return (int)parameters[4].Value;
    }

    private async Task ReleaseAppLockAsync(string resource)
    {
        SqlParameter[] parameters =
        [
            new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = resource },
            new SqlParameter("@LockOwner", SqlDbType.VarChar, 32) { Value = "Session" }
        ];

        await context.Database.ExecuteSqlRawAsync(
            "EXEC sp_releaseapplock @Resource = @Resource, @LockOwner = @LockOwner",
            parameters);
    }
}
