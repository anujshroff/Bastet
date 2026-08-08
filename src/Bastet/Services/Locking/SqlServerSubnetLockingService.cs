using Bastet.Data;
using Bastet.Services.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Diagnostics;

namespace Bastet.Services.Locking;

public class SqlServerSubnetLockingService(BastetDbContext context, ILogger<SqlServerSubnetLockingService> logger) : ISubnetLockingService
{
    private const int DEFAULT_TIMEOUT_MS = 30000;
    private const string SUBNET_OPERATIONS_LOCK = "Bastet:SubnetOperations";

    private void DiscardPooledConnection()
    {
        try
        {
            if (context.Database.GetDbConnection() is SqlConnection stranded)
            {
                SqlConnection.ClearPool(stranded);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discard the pooled connection after a failed lock release");
        }
    }

    private static readonly SemaphoreSlim _localGate = new(1, 1);

    public async Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation, TimeSpan? timeout = null)
    {
        int timeoutMs = (int)(timeout?.TotalMilliseconds ?? DEFAULT_TIMEOUT_MS);

        long waitStarted = Stopwatch.GetTimestamp();
        if (!await _localGate.WaitAsync(timeoutMs))
        {
            throw new TimeoutException(
                $"Could not acquire subnet operation lock within {timeoutMs}ms (another operation on this instance holds it)");
        }

        try
        {
            int remainingMs = timeoutMs - (int)Stopwatch.GetElapsedTime(waitStarted).TotalMilliseconds;
            if (remainingMs < 0)
            {
                remainingMs = 0;
            }

            await context.Database.OpenConnectionAsync();
            try
            {

                int lockResult;

                try
                {
                    lockResult = await AcquireAppLockAsync(SUBNET_OPERATIONS_LOCK, remainingMs);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "sp_getapplock did not return a result; discarding the pooled connection in case the "
                        + "lock was granted server-side and would otherwise be stranded on a pooled session");

                    DiscardPooledConnection();
                    throw;
                }

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

                    try
                    {
                        await ReleaseAppLockAsync(SUBNET_OPERATIONS_LOCK);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "Failed to release the subnet operation lock; discarding the pooled connection so the "
                            + "session-owned lock is dropped rather than stranded");

                        DiscardPooledConnection();
                    }
                }
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            _localGate.Release();
        }
    }

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

        int? originalCommandTimeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout((timeoutMs / 1000) + 30);

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                "EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = @LockMode, @LockOwner = @LockOwner, @LockTimeout = @LockTimeout",
                parameters);
        }
        catch (SqlException ex) when (ex.Number == SqlSaveOutcome.CommandTimeout)
        {

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
