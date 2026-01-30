using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Data;

/// <summary>
/// Entity Framework DbContext for ASP.NET Core Data Protection keys.
/// This context is separate from the main application context to keep
/// infrastructure concerns isolated from domain concerns.
/// </summary>
/// <remarks>
/// Data Protection keys are used to encrypt/decrypt authentication cookies.
/// When running multiple replicas without session affinity, all replicas
/// must share the same Data Protection keys to avoid authentication issues.
/// </remarks>
public class DataProtectionDbContext(DbContextOptions<DataProtectionDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>
    /// Gets or sets the Data Protection keys.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
}
