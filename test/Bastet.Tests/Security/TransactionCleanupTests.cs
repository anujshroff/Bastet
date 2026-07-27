using Bastet.Controllers;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Moq;

namespace Bastet.Tests.Security;

/// <summary>
/// A rollback that fails must not become the failure being reported. When a commit fails the
/// provider marks the transaction complete, so the rollback in the catch block throws
/// InvalidOperationException - and called before the logging line it replaces the real exception,
/// losing the incident and turning the action's intended response into an unhandled error page.
/// </summary>
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

        // The assertion is the absence of a throw: an exception escaping here is the defect.
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

        // The rollback failure is not silently dropped - it is recorded as secondary to the real one.
        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                rollbackFailure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// The ordinary case - a constraint violation, say - leaves the transaction usable, and must
    /// still roll back and log nothing.
    /// </summary>
    [Fact]
    public async Task RollbackQuietlyAsync_RollbackSucceeds_LogsNothing()
    {
        Mock<IDbContextTransaction> transaction = new();
        Mock<ILogger> logger = new();

        await TransactionCleanup.RollbackQuietlyAsync(transaction.Object, logger.Object);

        transaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Nothing is recorded on the way through - the logger is never touched at all.
        logger.VerifyNoOtherCalls();
    }
}
