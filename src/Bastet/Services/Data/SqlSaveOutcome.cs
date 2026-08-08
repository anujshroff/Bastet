using Microsoft.Data.SqlClient;
using System.Net.Sockets;
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

    public static bool IsIndeterminateSqlException(SqlException sql) =>
        sql is not null
        && (IsIndeterminateErrorNumber(sql.Number)
            || (sql.Number == 0 && (sql.Class >= 20 || HasTransportInnerException(sql))));

    private static bool HasTransportInnerException(Exception exception)
    {
        for (Exception? e = exception.InnerException; e is not null; e = e.InnerException)
        {
            if (e is IOException or SocketException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CarriesIndeterminateSqlError(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql && IsIndeterminateSqlException(sql))
            {
                return true;
            }
        }

        return false;
    }
}
