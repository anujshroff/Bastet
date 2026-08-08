using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Services.Data;

/// <summary>
/// Whether a failed save left the database in a state BASTET can describe, or one it cannot.
/// </summary>
/// <remarks>
/// Every lock-guarded write here saves with no explicit transaction, so <c>SaveChangesAsync</c>
/// auto-commits. If the connection breaks between the UPDATE reaching SQL Server and the reply
/// being read, the server has already committed and the client never learns it. The operator is
/// then told the change did not happen while the row carries it - measured on a /8 resized to /9
/// under a paused process, with the page reading "Error updating subnet".
///
/// THE PREDICATE IS "THE OUTCOME IS UNKNOWN", NOT "IT TIMED OUT". A command timeout (-2) is the
/// case that is easiest to reproduce, but a transport-level failure after the commit - a reset
/// connection, a KILLed SPID, an Azure SQL failover - is exactly as ambiguous and arrives through
/// the same catch. Testing only -2 closes one door of several.
///
/// THIS NEVER MEANS THE WRITE LANDED. More often -2 is a genuine cancel-and-rollback. Callers must
/// report that they cannot tell, and must not assert either outcome.
///
/// Only a save can be indeterminate, so the outer exception must be a <see cref="DbUpdateException"/>:
/// a read that times out wrote nothing, and saying otherwise would be its own false statement.
/// </remarks>
public static class SqlSaveOutcome
{
    /// <summary>SQL Server's error number for a client-side command timeout.</summary>
    public const int CommandTimeout = -2;

    /// <summary>
    /// Error numbers that leave a save's outcome unknown: the command timed out, or the connection
    /// failed at the transport level. In both cases the server may already have committed.
    /// </summary>
    private static readonly int[] IndeterminateErrorNumbers =
    [
        CommandTimeout, // client-side command timeout
        -1,             // connection failed / general network error
        20,             // the instance did not return a response
        64,             // the specified network name is no longer available
        121,            // the semaphore timeout period has expired
        233,            // no process is on the other end of the pipe
        10053,          // an established connection was aborted by the host
        10054,          // an existing connection was forcibly closed by the remote host
        10060,          // connection attempt timed out
    ];

    /// <summary>
    /// True when a SQL Server error number leaves a save's outcome unknown. Split out because
    /// <see cref="SqlException"/> has no public constructor, so this is the part a unit test can
    /// reach; the exception-shape half is covered against a real server.
    /// </summary>
    public static bool IsIndeterminateErrorNumber(int errorNumber) =>
        IndeterminateErrorNumbers.Contains(errorNumber);

    /// <summary>
    /// True when this failure leaves BASTET unable to tell whether the write was applied.
    /// </summary>
    public static bool IsIndeterminate(Exception? exception) =>
        exception is DbUpdateException && CarriesIndeterminateSqlError(exception);

    /// <summary>
    /// The same question for a path that wraps its work in an explicit transaction, where the
    /// ambiguous moment is <c>CommitAsync</c> rather than <c>SaveChangesAsync</c>.
    /// </summary>
    /// <remarks>
    /// A commit that times out throws a bare <see cref="SqlException"/>, not a
    /// <see cref="DbUpdateException"/>, so <see cref="IsIndeterminate"/> does not see it - and these
    /// callers then told the operator "no changes were saved" about a transaction the server may
    /// have committed. A transaction does not remove the ambiguity; it only moves it to the commit.
    ///
    /// This is deliberately wider than <see cref="IsIndeterminate"/>: these call sites wrap reads as
    /// well, so a read that times out also lands here. That direction is safe - nothing was written,
    /// and "BASTET could not confirm" is merely cautious. The direction that is NOT safe is the one
    /// being fixed: asserting nothing was saved when it was.
    /// </remarks>
    public static bool IsIndeterminateTransaction(Exception? exception) =>
        exception is DbUpdateException or SqlException && CarriesIndeterminateSqlError(exception);

    private static bool CarriesIndeterminateSqlError(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql && IsIndeterminateErrorNumber(sql.Number))
            {
                return true;
            }
        }

        return false;
    }
}
