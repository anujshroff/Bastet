namespace Bastet.Services.Locking;

public interface ISubnetLockingService
{

    Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation, TimeSpan? timeout = null);
}
