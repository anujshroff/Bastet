using Bastet.Services.Data;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Tests.Services;

public class SqlSaveOutcomeTests
{
    [Theory]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(20)]
    [InlineData(64)]
    [InlineData(121)]
    [InlineData(233)]
    [InlineData(10053)]
    [InlineData(10054)]
    [InlineData(10060)]
    public void ATimeoutOrTransportErrorNumber_LeavesTheOutcomeUnknown(int errorNumber) =>
        Assert.True(SqlSaveOutcome.IsIndeterminateErrorNumber(errorNumber));

    [Theory]
    [InlineData(2627)]
    [InlineData(2601)]
    [InlineData(547)]
    [InlineData(1205)]
    [InlineData(229)]
    public void ADeterminateFailure_IsNotTreatedAsUnknown(int errorNumber) =>
        Assert.False(SqlSaveOutcome.IsIndeterminateErrorNumber(errorNumber));

    [Fact]
    public void AZeroErrorNumber_IsNotDecidedByTheNumberAlone() =>
        Assert.False(SqlSaveOutcome.IsIndeterminateErrorNumber(0));

    [Fact]
    public void AnExceptionThatIsNotASaveFailure_IsNeverUnknown()
    {
        Assert.False(SqlSaveOutcome.IsIndeterminate(null));
        Assert.False(SqlSaveOutcome.IsIndeterminate(new TimeoutException()));
        Assert.False(SqlSaveOutcome.IsIndeterminate(new InvalidOperationException()));
    }

    [Fact]
    public void ASaveFailureCarryingNoSqlException_IsNotUnknown() =>
        Assert.False(SqlSaveOutcome.IsIndeterminate(
            new DbUpdateException("save failed", new InvalidOperationException("no sql error under this"))));

    [Fact]
    public void TheCommandTimeoutConstant_IsSqlServersOwnNumber() =>
        Assert.Equal(-2, SqlSaveOutcome.CommandTimeout);
}
