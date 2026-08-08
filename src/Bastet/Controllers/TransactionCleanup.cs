using Microsoft.EntityFrameworkCore.Storage;

namespace Bastet.Controllers;

public static class TransactionCleanup
{

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
