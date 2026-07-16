namespace Bastet.Tests.Azure;

/// <summary>
/// Groups the test classes that read or write the BASTET_AZURE_IMPORT environment variable so they
/// never run at the same time.
/// </summary>
/// <remarks>
/// Environment variables are process-global, and xUnit runs test collections in parallel by default.
/// Two classes flipping this flag concurrently makes unrelated tests fail depending on timing - one
/// class's Dispose clearing the flag mid-run of another's test. Sharing a collection serialises them.
/// Any future test that touches this flag belongs here too.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public class AzureFeatureFlagCollection
{
    public const string Name = "AzureFeatureFlag";
}
