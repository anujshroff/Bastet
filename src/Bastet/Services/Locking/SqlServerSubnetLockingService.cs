using Bastet.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Bastet.Services.Locking;

/// <summary>
/// SQL Server implementation of subnet locking using application locks (sp_getapplock)
/// Works with SQL Server, SQL LocalDB, and Azure SQL Database
/// </summary>
public class SqlServerSubnetLockingService(BastetDbContext context) : ISubnetLockingService
{
    private const int DEFAULT_TIMEOUT_MS = 30000; // 30 seconds
    private const string SUBNET_OPERATIONS_LOCK = "Bastet:SubnetOperations";
    private const string SUBNET_EDIT_LOCK_TEMPLATE = "Bastet:SubnetEdit:{0}";

    /// <inheritdoc />
    public async Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation, TimeSpan? timeout = null)
    {
        int timeoutMs = (int)(timeout?.TotalMilliseconds ?? DEFAULT_TIMEOUT_MS);

        using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        // Acquire exclusive application lock for all subnet operations
        int lockResult = await AcquireAppLockAsync(SUBNET_OPERATIONS_LOCK, timeoutMs);
        if (lockResult < 0)
        {
            throw new TimeoutException($"Could not acquire subnet operation lock within {timeoutMs}ms (result code: {lockResult})");
        }

        try
        {
            T? result = await operation();
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
        // Lock is automatically released when transaction ends
    }

    /// <inheritdoc />
    public async Task<T> ExecuteWithSubnetEditLockAsync<T>(int subnetId, Func<Task<T>> operation, TimeSpan? timeout = null)
    {
        int timeoutMs = (int)(timeout?.TotalMilliseconds ?? DEFAULT_TIMEOUT_MS);
        string lockResource = string.Format(SUBNET_EDIT_LOCK_TEMPLATE, subnetId);

        using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        // Acquire exclusive application lock for specific subnet edit
        int lockResult = await AcquireAppLockAsync(lockResource, timeoutMs);
        if (lockResult < 0)
        {
            throw new TimeoutException($"Could not acquire subnet edit lock for subnet {subnetId} within {timeoutMs}ms (result code: {lockResult})");
        }

        try
        {
            T? result = await operation();
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
        // Lock is automatically released when transaction ends
    }

    /// <summary>
    /// Acquires an application lock using sp_getapplock
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
            new SqlParameter("@LockOwner", SqlDbType.VarChar, 32) { Value = "Transaction" },
            new SqlParameter("@LockTimeout", SqlDbType.Int) { Value = timeoutMs },
            new SqlParameter("@Result", SqlDbType.Int) { Direction = ParameterDirection.Output }
        ];

        await context.Database.ExecuteSqlRawAsync(
            "EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = @LockMode, @LockOwner = @LockOwner, @LockTimeout = @LockTimeout",
            parameters);

        return (int)parameters[4].Value;
    }
}
