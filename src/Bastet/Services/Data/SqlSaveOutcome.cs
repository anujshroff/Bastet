using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Services.Data;

public static class SqlSaveOutcome
{

    public const int CommandTimeout = -2;

    private static readonly int[] IndeterminateErrorNumbers =
    [
        CommandTimeout,
        -1,
        20,
        64,
        121,
        233,
        10053,
        10054,
        10060,
    ];

    public static bool IsIndeterminateErrorNumber(int errorNumber) =>
        IndeterminateErrorNumbers.Contains(errorNumber);

    public static bool IsIndeterminate(Exception? exception) =>
        exception is DbUpdateException && CarriesIndeterminateSqlError(exception);

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
