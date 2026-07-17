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
public class SqlServerSubnetLockingService(BastetDbContext context) : ISubnetLockingService
{
    private const int DEFAULT_TIMEOUT_MS = 30000; // 30 seconds
    private const string SUBNET_OPERATIONS_LOCK = "Bastet:SubnetOperations";

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
                await ReleaseAppLockAsync(SUBNET_OPERATIONS_LOCK);
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

        await context.Database.ExecuteSqlRawAsync(
            "EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = @LockMode, @LockOwner = @LockOwner, @LockTimeout = @LockTimeout",
            parameters);

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
