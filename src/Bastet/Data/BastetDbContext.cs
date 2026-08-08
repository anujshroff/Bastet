using Bastet.Models;
using Bastet.Services;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Data;

public class BastetDbContext(DbContextOptions<BastetDbContext> options, IUserContextService? userContextService = null) : DbContext(options)
{
    public DbSet<Subnet> Subnets { get; set; } = null!;
    public DbSet<DeletedSubnet> DeletedSubnets { get; set; } = null!;
    public DbSet<HostIpAssignment> HostIpAssignments { get; set; } = null!;
    public DbSet<DeletedHostIpAssignment> DeletedHostIpAssignments { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Subnet>(entity =>
        {

            entity.HasOne(s => s.ParentSubnet)
                .WithMany(s => s.ChildSubnets)
                .HasForeignKey(s => s.ParentSubnetId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(s => new { s.NetworkAddress, s.Cidr })
                .IsUnique();

            entity.HasIndex(s => s.ParentSubnetId);

            entity.HasIndex(s => s.Name);

            entity.Property(s => s.NetworkAddress)
                .IsRequired()
                .HasMaxLength(45);

            entity.Property(s => s.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(s => s.Cidr)
                .IsRequired();

            entity.Property(s => s.Description)
                .HasMaxLength(1000);

            entity.Property(s => s.Tags)
                .HasMaxLength(255);

            entity.Property(s => s.AzureResourceId)
                .HasMaxLength(500);

            entity.ToTable(t => t.HasCheckConstraint("CK_Subnet_ValidCidr", "Cidr >= 0 AND Cidr <= 32"));
        });

        modelBuilder.Entity<HostIpAssignment>(entity =>
        {

            entity.HasOne(h => h.Subnet)
                .WithMany(s => s.HostIpAssignments)
                .HasForeignKey(h => h.SubnetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(h => h.IP)
                .IsRequired()
                .HasMaxLength(15);

            entity.Property(h => h.Name)
                .HasMaxLength(100);

            entity.HasIndex(h => h.IP).IsUnique();
            entity.HasIndex(h => h.SubnetId);
        });

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
