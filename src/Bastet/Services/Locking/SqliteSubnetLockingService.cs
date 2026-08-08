namespace Bastet.Services.Locking;

public class SqliteSubnetLockingService : ISubnetLockingService
{
    private const int DEFAULT_TIMEOUT_MS = 30000;

    private static readonly SemaphoreSlim _globalSubnetLock = new(1, 1);

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
