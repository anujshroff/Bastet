namespace Bastet.Services.Locking;

/// <summary>
/// SQLite implementation of subnet locking using a process-wide semaphore. SQLite only appears in
/// tests (in-memory databases), where all "concurrent" callers live in one process, so a static
/// semaphore gives the same serialization guarantee sp_getapplock gives across SQL Server sessions.
/// Like the SQL Server implementation, it does NOT open a transaction - callers own theirs.
/// </summary>
public class SqliteSubnetLockingService : ISubnetLockingService
{
    private const int DEFAULT_TIMEOUT_MS = 30000; // 30 seconds

    // Static to simulate cross-connection locking within the test process
    private static readonly SemaphoreSlim _globalSubnetLock = new(1, 1);

    /// <inheritdoc />
    public async Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation, TimeSpan? timeout = null)
    {
        int timeoutMs = (int)(timeout?.TotalMilliseconds ?? DEFAULT_TIMEOUT_MS);

        if (!await _globalSubnetLock.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)))
        {
            throw new TimeoutException($"Could not acquire subnet operation lock within {timeoutMs}ms");
        }

        try
        {
            return await operation();
        }
        finally
        {
            _globalSubnetLock.Release();
        }
    }
}
