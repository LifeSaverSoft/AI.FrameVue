using Microsoft.EntityFrameworkCore;
using AI.FrameVue.Models;

namespace AI.FrameVue.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DesignSession> DesignSessions => Set<DesignSession>();
    public DbSet<DesignOption> DesignOptions => Set<DesignOption>();
    public DbSet<DesignFeedback> Feedback => Set<DesignFeedback>();

    // Catalog tables (imported from SQL Server)
    public DbSet<CatalogVendor> CatalogVendors => Set<CatalogVendor>();
    public DbSet<CatalogMoulding> CatalogMouldings => Set<CatalogMoulding>();
    public DbSet<CatalogMat> CatalogMats => Set<CatalogMat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DesignSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasMany(e => e.Options)
                .WithOne()
                .HasForeignKey(e => e.DesignSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DesignOption>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<DesignFeedback>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Ignore(e => e.DominantColors);
        });

        // Catalog tables
        modelBuilder.Entity<CatalogVendor>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Prefix);
        });

        modelBuilder.Entity<CatalogMoulding>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.VendorId);
            entity.HasIndex(e => e.ItemName);
            entity.HasIndex(e => e.ColorCategory);
            entity.HasIndex(e => e.Material);
            entity.HasIndex(e => e.Style);
            entity.HasIndex(e => e.Profile);
            entity.HasIndex(e => e.PrimaryColorHex);
        });

        modelBuilder.Entity<CatalogMat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.VendorId);
            entity.HasIndex(e => e.ItemName);
            entity.HasIndex(e => e.ColorCategory);
            entity.HasIndex(e => e.Material);
            entity.HasIndex(e => e.MatClass);
            entity.HasIndex(e => e.PrimaryColorHex);
        });
    }
}
