using Bastet.Controllers;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Moq;

namespace Bastet.Tests.Security;

public class TransactionCleanupTests
{
    [Fact]
    public async Task RollbackQuietlyAsync_RollbackThrows_DoesNotPropagate()
    {
        Mock<IDbContextTransaction> transaction = new();
        transaction
            .Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "This SqlTransaction has completed; it is no longer usable."));

        Mock<ILogger> logger = new();

        await TransactionCleanup.RollbackQuietlyAsync(transaction.Object, logger.Object);

        transaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RollbackQuietlyAsync_RollbackThrows_LogsTheSecondaryFailure()
    {
        Mock<IDbContextTransaction> transaction = new();
        InvalidOperationException rollbackFailure = new("This SqlTransaction has completed.");
        transaction
            .Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(rollbackFailure);

        Mock<ILogger> logger = new();

        await TransactionCleanup.RollbackQuietlyAsync(transaction.Object, logger.Object);

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                rollbackFailure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RollbackQuietlyAsync_RollbackSucceeds_LogsNothing()
    {
        Mock<IDbContextTransaction> transaction = new();
        Mock<ILogger> logger = new();

        await TransactionCleanup.RollbackQuietlyAsync(transaction.Object, logger.Object);

        transaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);

        logger.VerifyNoOtherCalls();
    }
}
