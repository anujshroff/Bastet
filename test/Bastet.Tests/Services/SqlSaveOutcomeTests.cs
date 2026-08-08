using Bastet.Services.Data;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Tests.Services;

/// <summary>
/// P4. Every lock-guarded write saves with no explicit transaction, so SaveChangesAsync
/// auto-commits. When the connection breaks between the UPDATE reaching SQL Server and the reply
/// being read, the server has already committed and the client never finds out - and the operator
/// was told the change did not happen while the row carried it.
///
/// The predicate has to be "the outcome is unknown", not "it timed out": a transport failure after
/// the commit is exactly as ambiguous and arrives through the same catch.
/// </summary>
public class SqlSaveOutcomeTests
{
    [Theory]
    [InlineData(-2)]     // client-side command timeout - the reproducible case
    [InlineData(-1)]     // connection failed
    [InlineData(20)]     // instance did not return a response
    [InlineData(64)]     // network name no longer available
    [InlineData(121)]    // semaphore timeout expired
    [InlineData(233)]    // no process on the other end of the pipe
    [InlineData(10053)]  // established connection aborted by the host
    [InlineData(10054)]  // connection forcibly closed by the remote host
    [InlineData(10060)]  // connection attempt timed out
    public void ATimeoutOrTransportErrorNumber_LeavesTheOutcomeUnknown(int errorNumber) =>
        Assert.True(SqlSaveOutcome.IsIndeterminateErrorNumber(errorNumber));

    /// <summary>
    /// The counter-test that stops this swallowing real, determinate failures. A constraint
    /// violation, a deadlock victim and a permission error all mean the write did NOT happen, and
    /// reporting them as "could not confirm" would be its own false statement.
    /// </summary>
    [Theory]
    [InlineData(2627)]   // primary key violation
    [InlineData(2601)]   // duplicate key on a unique index
    [InlineData(547)]    // foreign key constraint violation
    [InlineData(1205)]   // chosen as the deadlock victim, rolled back
    [InlineData(229)]    // permission denied
    [InlineData(0)]
    public void ADeterminateFailure_IsNotTreatedAsUnknown(int errorNumber) =>
        Assert.False(SqlSaveOutcome.IsIndeterminateErrorNumber(errorNumber));

    /// <summary>
    /// Only a save can be indeterminate. A read that fails wrote nothing, so claiming otherwise
    /// would put a false "we cannot tell" in front of an operator whose data is untouched.
    /// </summary>
    [Fact]
    public void AnExceptionThatIsNotASaveFailure_IsNeverUnknown()
    {
        Assert.False(SqlSaveOutcome.IsIndeterminate(null));
        Assert.False(SqlSaveOutcome.IsIndeterminate(new TimeoutException()));
        Assert.False(SqlSaveOutcome.IsIndeterminate(new InvalidOperationException()));
    }

    /// <summary>
    /// A save failure with no SqlException under it - EF wrapping something else - stays
    /// determinate rather than defaulting to uncertainty.
    /// </summary>
    [Fact]
    public void ASaveFailureCarryingNoSqlException_IsNotUnknown() =>
        Assert.False(SqlSaveOutcome.IsIndeterminate(
            new DbUpdateException("save failed", new InvalidOperationException("no sql error under this"))));

    /// <summary>
    /// The constant the locking service now shares rather than keeping its own copy to drift.
    /// </summary>
    [Fact]
    public void TheCommandTimeoutConstant_IsSqlServersOwnNumber() =>
        Assert.Equal(-2, SqlSaveOutcome.CommandTimeout);
}
