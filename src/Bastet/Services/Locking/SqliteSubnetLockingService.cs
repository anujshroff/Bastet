using Bastet.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Bastet.Services.Locking;

/// <summary>
/// SQLite implementation of subnet locking using semaphores and serializable transactions
/// Provides closest possible behavior to SQL Server application locks for testing
/// </summary>
public class SqliteSubnetLockingService(BastetDbContext context) : ISubnetLockingService
{
    private const int DEFAULT_TIMEOUT_MS = 30000; // 30 seconds

    // Static semaphores to simulate cross-process locking in SQLite
    private static readonly SemaphoreSlim _globalSubnetLock = new(1, 1);
    private static readonly Lock _editLocksLock = new();
    private static readonly Dictionary<int, SemaphoreSlim> _subnetEditLocks = [];

    /// <inheritdoc />
    public async Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation, TimeSpan? timeout = null)
    {
        int timeoutMs = (int)(timeout?.TotalMilliseconds ?? DEFAULT_TIMEOUT_MS);

        // SQLite strategy: Combine in-memory semaphore + serializable transaction
        // This simulates cross-process locking as closely as possible

        if (!await _globalSubnetLock.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)))
        {
            throw new TimeoutException($"Could not acquire subnet operation lock within {timeoutMs}ms");
        }

        try
        {
            using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            // Force SQLite to acquire a reserved lock by touching the schema
            // This ensures other connections will see serialization conflicts
            await context.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM Subnets WHERE 1=0");

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
        }
        finally
        {
            _globalSubnetLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<T> ExecuteWithSubnetEditLockAsync<T>(int subnetId, Func<Task<T>> operation, TimeSpan? timeout = null)
    {
        int timeoutMs = (int)(timeout?.TotalMilliseconds ?? DEFAULT_TIMEOUT_MS);

        // Get or create a semaphore for this specific subnet
        SemaphoreSlim subnetLock;
        lock (_editLocksLock)
        {
            if (!_subnetEditLocks.TryGetValue(subnetId, out subnetLock!))
            {
                subnetLock = new SemaphoreSlim(1, 1);
                _subnetEditLocks[subnetId] = subnetLock;
            }
        }

        if (!await subnetLock.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)))
        {
            throw new TimeoutException($"Could not acquire subnet edit lock for subnet {subnetId} within {timeoutMs}ms");
        }

        try
        {
            using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            // Force SQLite to acquire a reserved lock by reading the specific subnet
            await context.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM Subnets WHERE Id = {0}", subnetId);

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
        }
        finally
        {
            subnetLock.Release();

            // Clean up semaphore if possible (optional optimization)
            lock (_editLocksLock)
            {
                // Only remove if no one is waiting and we can acquire immediately
                if (subnetLock.CurrentCount == 1 && subnetLock.Wait(0))
                {
                    try
                    {
                        _subnetEditLocks.Remove(subnetId);
                        subnetLock.Dispose();
                    }
                    finally
                    {
                        // Release the lock we just acquired for testing
                        subnetLock.Release();
                    }
                }
            }
        }
    }
}
