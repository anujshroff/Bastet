using Bastet.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bastet.Tests.TestHelpers;

public static class TestDbContextFactory
{

    public static BastetDbContext CreateDbContext()
    {

        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();

        ServiceCollection services = new();

        services.AddDbContext<BastetDbContext>(options =>
            options.UseSqlite(connection));

        ServiceProvider serviceProvider = services.BuildServiceProvider();

        BastetDbContext context = serviceProvider.GetRequiredService<BastetDbContext>();

        context.Database.EnsureCreated();

        return context;
    }
}
