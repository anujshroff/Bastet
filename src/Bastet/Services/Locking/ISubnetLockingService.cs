namespace Bastet.Services.Locking;

/// <summary>
/// Service for providing distributed locking for subnet operations to prevent race conditions
/// </summary>
public interface ISubnetLockingService
{
    /// <summary>
    /// Executes an operation within an exclusive lock for all subnet operations
    /// </summary>
    /// <typeparam name="T">Return type of the operation</typeparam>
    /// <param name="operation">The operation to execute within the lock</param>
    /// <param name="timeout">Optional timeout for acquiring the lock</param>
    /// <returns>The result of the operation</returns>
    /// <exception cref="TimeoutException">Thrown when the lock cannot be acquired within the timeout period</exception>
    Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation, TimeSpan? timeout = null);

    /// <summary>
    /// Executes an operation within an exclusive lock for a specific subnet edit operation
    /// </summary>
    /// <typeparam name="T">Return type of the operation</typeparam>
    /// <param name="subnetId">The ID of the subnet being edited</param>
    /// <param name="operation">The operation to execute within the lock</param>
    /// <param name="timeout">Optional timeout for acquiring the lock</param>
    /// <returns>The result of the operation</returns>
    /// <exception cref="TimeoutException">Thrown when the lock cannot be acquired within the timeout period</exception>
    Task<T> ExecuteWithSubnetEditLockAsync<T>(int subnetId, Func<Task<T>> operation, TimeSpan? timeout = null);
}
