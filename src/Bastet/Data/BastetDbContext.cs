using Bastet.Models;
using Bastet.Services;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Data;

/// <summary>
/// Entity Framework DbContext for the BASTET application
/// </summary>
public class BastetDbContext(DbContextOptions<BastetDbContext> options, IUserContextService? userContextService = null) : DbContext(options)
{
    public DbSet<Subnet> Subnets { get; set; } = null!;
    public DbSet<DeletedSubnet> DeletedSubnets { get; set; } = null!;
    public DbSet<HostIpAssignment> HostIpAssignments { get; set; } = null!;
    public DbSet<DeletedHostIpAssignment> DeletedHostIpAssignments { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Subnet entity
        modelBuilder.Entity<Subnet>(entity =>
        {
            // Configure the self-referencing relationship
            entity.HasOne(s => s.ParentSubnet)
                .WithMany(s => s.ChildSubnets)
                .HasForeignKey(s => s.ParentSubnetId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete

            // Create indexes
            entity.HasIndex(s => new { s.NetworkAddress, s.Cidr })
                .IsUnique();

            entity.HasIndex(s => s.ParentSubnetId);

            entity.HasIndex(s => s.Name);

            // Configure properties
            entity.Property(s => s.NetworkAddress)
                .IsRequired()
                .HasMaxLength(45);

            entity.Property(s => s.Name)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(s => s.Cidr)
                .IsRequired();

            entity.Property(s => s.Description)
                .HasMaxLength(500);

            entity.Property(s => s.Tags)
                .HasMaxLength(255);

            // Add check constraints using the new API
            entity.ToTable(t => t.HasCheckConstraint("CK_Subnet_ValidCidr", "Cidr >= 0 AND Cidr <= 32"));
        });

        // Configure HostIpAssignment entity
        modelBuilder.Entity<HostIpAssignment>(entity =>
        {
            // Configure the relationship with Subnet
            entity.HasOne(h => h.Subnet)
                .WithMany(s => s.HostIpAssignments)
                .HasForeignKey(h => h.SubnetId)
                .OnDelete(DeleteBehavior.Cascade); // When subnet is deleted, cascade to host IPs

            // Configure properties
            entity.Property(h => h.IP)
                .IsRequired()
                .HasMaxLength(15);

            entity.Property(h => h.Name)
                .HasMaxLength(100);

            // Create indexes
            entity.HasIndex(h => h.IP).IsUnique();
            entity.HasIndex(h => h.SubnetId);
        });

        // Configure DeletedHostIpAssignment entity
        modelBuilder.Entity<DeletedHostIpAssignment>(entity =>
        {
            entity.Property(h => h.OriginalIP)
                .IsRequired()
                .HasMaxLength(15);

            entity.Property(h => h.Name)
                .HasMaxLength(100);

            entity.HasIndex(h => h.OriginalSubnetId);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        UpdateAuditFields();
        return base.SaveChanges();
    }

    private void UpdateAuditFields()
    {
        string? currentUsername = userContextService?.GetCurrentUsername();

        IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> entries = ChangeTracker.Entries()
            .Where(e => e.Entity is BaseEntity && (e.State == EntityState.Added || e.State == EntityState.Modified));

        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry? entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                ((BaseEntity)entry.Entity).CreatedAt = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(currentUsername))
                {
                    ((BaseEntity)entry.Entity).CreatedBy = currentUsername;
                }
            }

            if (entry.State == EntityState.Modified)
            {
                ((BaseEntity)entry.Entity).LastModifiedAt = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(currentUsername))
                {
                    ((BaseEntity)entry.Entity).ModifiedBy = currentUsername;
                }
            }
        }
    }
}
