using Bastet.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Bastet.Tests.TestHelpers;

/// <summary>
/// Factory for creating test database contexts using an in-memory database
/// </summary>
public static class TestDbContextFactory
{
    /// <summary>
    /// Creates a new in-memory database context with a unique database name
    /// </summary>
    /// <returns>A configured DbContext for testing</returns>
    public static BastetDbContext CreateDbContext()
    {
        // Create a unique name for the in-memory database
        string dbName = $"BastetTestDb_{Guid.NewGuid()}";

        // Set up the service collection
        ServiceCollection services = new();

        // Add an in-memory database with transaction warnings suppressed
        services.AddDbContext<BastetDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                   .ConfigureWarnings(warnings =>
                       warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        // Build the service provider
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Get the context from the service provider
        return serviceProvider.GetRequiredService<BastetDbContext>();
    }
}
