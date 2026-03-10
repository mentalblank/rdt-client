using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RdtClient.Data.Models.Data;

namespace RdtClient.Data.Data;

#nullable disable

public class DataContext(DbContextOptions options) : IdentityDbContext(options)
{
    public DbSet<Download> Downloads { get; set; }
    public DbSet<Setting> Settings { get; set; }
    public DbSet<Torrent> Torrents { get; set; }

    public DbSet<UsenetJob> UsenetJobs { get; set; }
    public DbSet<UsenetFile> UsenetFiles { get; set; }
    public DbSet<UsenetProvider> UsenetProviders { get; set; }

    public DbSet<UsenetDavItem> UsenetDavItems { get; set; }
    public DbSet<UsenetNzbFile> UsenetNzbFiles { get; set; }
    public DbSet<UsenetRarFile> UsenetRarFiles { get; set; }
    public DbSet<UsenetMultipartFile> UsenetMultipartFiles { get; set; }
    public DbSet<UsenetHealthCheckResult> UsenetHealthCheckResults { get; set; }
    public DbSet<UsenetHealthCheckStat> UsenetHealthCheckStats { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UsenetHealthCheckStat>(e =>
        {
            e.HasKey(i => new { i.DateStartInclusive, i.DateEndExclusive, i.Result, i.RepairStatus });
        });

        builder.Entity<UsenetDavItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).ValueGeneratedNever();
            e.Property(i => i.CreatedAt).ValueGeneratedNever().IsRequired();
            e.Property(i => i.Name).IsRequired().HasMaxLength(255);
            e.Property(i => i.Path).IsRequired();
            e.HasOne(i => i.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(i => i.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(i => new { i.ParentId, i.Name }).IsUnique();
        });

        builder.Entity<UsenetNzbFile>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(i => i.Id).ValueGeneratedNever();
            e.HasOne(f => f.DavItem)
                .WithOne()
                .HasForeignKey<UsenetNzbFile>(f => f.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UsenetRarFile>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(i => i.Id).ValueGeneratedNever();
            e.HasOne(f => f.DavItem)
                .WithOne()
                .HasForeignKey<UsenetRarFile>(f => f.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UsenetMultipartFile>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(i => i.Id).ValueGeneratedNever();
            e.HasOne(f => f.DavItem)
                .WithOne()
                .HasForeignKey<UsenetMultipartFile>(f => f.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        var cascadeFKs = builder.Model.GetEntityTypes()
                                .Where(t => !t.ClrType.Name.Contains("Usenet"))
                                .SelectMany(t => t.GetForeignKeys())
                                .Where(fk => !fk.IsOwnership && fk.DeleteBehavior == DeleteBehavior.Cascade);

        foreach (var fk in cascadeFKs)
        {
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        }

        builder.Entity<UsenetFile>()
               .Property(f => f.UsenetJobId)
               .IsRequired(false);
    }
}
