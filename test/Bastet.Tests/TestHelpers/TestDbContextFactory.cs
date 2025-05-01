using Bastet.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bastet.Tests.TestHelpers;

/// <summary>
/// Factory for creating test database contexts using an SQLite in-memory database
/// </summary>
public static class TestDbContextFactory
{
    /// <summary>
    /// Creates a new SQLite in-memory database context
    /// </summary>
    /// <returns>A configured DbContext for testing</returns>
    public static BastetDbContext CreateDbContext()
    {
        // Create SQLite connection open to in-memory database
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();

        // Set up the service collection
        ServiceCollection services = new();

        // Add an SQLite database using the open connection
        services.AddDbContext<BastetDbContext>(options =>
            options.UseSqlite(connection));

        // Build the service provider
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Get the context from the service provider
        BastetDbContext context = serviceProvider.GetRequiredService<BastetDbContext>();

        // Create the schema in the in-memory database
        context.Database.EnsureCreated();

        return context;
    }
}
