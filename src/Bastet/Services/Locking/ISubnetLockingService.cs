namespace Bastet.Services.Locking;

/// <summary>
/// Serializes every mutation of the subnet tree (creates, edits, deletes, imports, host IP writes)
/// behind one global exclusive lock. Overlap/containment validation reads committed rows and checks
/// them in memory, so two concurrent writers can each pass validation against a tree that the other
/// is changing (write-skew); the global lock is what makes those checks sound.
/// </summary>
public interface ISubnetLockingService
{
    /// <summary>
    /// Executes an operation while holding the global subnet-operations lock. The lock service does
    /// NOT open a transaction - callers own their transactions (or rely on a single SaveChanges).
    /// </summary>
    /// <typeparam name="T">Return type of the operation</typeparam>
    /// <param name="operation">The operation to execute within the lock</param>
    /// <param name="timeout">Optional timeout for acquiring the lock</param>
    /// <returns>The result of the operation</returns>
    /// <exception cref="TimeoutException">Thrown when the lock cannot be acquired within the timeout period</exception>
    Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation, TimeSpan? timeout = null);
}
