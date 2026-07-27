using Microsoft.EntityFrameworkCore.Storage;

namespace Bastet.Controllers;

/// <summary>
/// Cleanup that must not itself become the failure being reported.
/// </summary>
public static class TransactionCleanup
{
    /// <summary>
    /// Rolls back, and logs rather than throws if the rollback fails.
    /// </summary>
    /// <remarks>
    /// When a commit fails - an Azure SQL failover, a gateway drop - the provider marks the
    /// transaction complete, so the <c>RollbackAsync</c> in the catch block throws
    /// <c>InvalidOperationException: This SqlTransaction has completed; it is no longer usable</c>.
    /// Called before the logging line, that exception replaces the real one and leaves the incident
    /// with no record of what actually went wrong, while the caller receives an unhandled error page
    /// instead of the response the action meant to return. Measured against SQL Server 2022 with
    /// Microsoft.Data.SqlClient 6.1.1 by killing the session mid-transaction.
    ///
    /// Nothing is stranded by swallowing it: the caller's <c>using</c> declaration disposes the
    /// transaction, which rolls back anything still live, and a broken connection is rolled back
    /// server-side regardless. An ordinary failure - a constraint violation, a bad conversion -
    /// leaves the transaction usable and rolls back here exactly as before.
    ///
    /// Same rule the migration lock already follows: an exception raised while cleaning up must not
    /// destroy the one already in flight.
    /// </remarks>
    public static async Task RollbackQuietlyAsync(IDbContextTransaction transaction, ILogger logger)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch (Exception rollbackException)
        {
            logger.LogError(rollbackException,
                "Rolling back after the error above also failed; the transaction had already completed");
        }
    }
}
