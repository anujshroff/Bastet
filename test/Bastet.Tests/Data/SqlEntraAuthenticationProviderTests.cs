using Microsoft.Data.SqlClient;

namespace Bastet.Tests.Data;

public class SqlEntraAuthenticationProviderTests
{
    [Theory]
    [InlineData(SqlAuthenticationMethod.ActiveDirectoryDefault)]
    [InlineData(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity)]
    public void EntraAuthenticationMethod_HasARegisteredProvider(SqlAuthenticationMethod method)
    {
        SqlAuthenticationProvider? provider = SqlAuthenticationProvider.GetProvider(method);

        Assert.True(
            provider is not null,
            $"No SQL authentication provider is registered for {method}. Microsoft.Data.SqlClient 7 and later "
            + "ship Entra ID authentication in the separate Microsoft.Data.SqlClient.Extensions.Azure package; "
            + "without a reference to it in Bastet.csproj, connection strings using "
            + "'Authentication=Active Directory ...' fail at startup.");
    }
}
