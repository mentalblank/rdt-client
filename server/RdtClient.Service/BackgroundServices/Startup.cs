using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RdtClient.Data.Data;
using RdtClient.Service.Services;

namespace RdtClient.Service.BackgroundServices;

public class Startup(IServiceProvider serviceProvider) : IHostedService
{
    public static Boolean Ready { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;

        using var scope = serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Startup>>();

        logger.LogWarning("Starting host on version {version}", version);

        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);

        // Manual fix for UsenetFiles.UsenetJobId nullability if needed
        try
        {
            logger.LogInformation("Checking UsenetFiles table structure...");
            // Force recreation of the table with nullable UsenetJobId if it's currently NOT NULL
            // We use a transaction to be safe
            await dbContext.Database.ExecuteSqlRawAsync(@"
                PRAGMA foreign_keys=OFF;
                BEGIN TRANSACTION;
                
                CREATE TABLE IF NOT EXISTS UsenetFiles_new (
                    UsenetFileId TEXT NOT NULL PRIMARY KEY,
                    UsenetJobId TEXT NULL,
                    Path TEXT NOT NULL,
                    Size INTEGER NOT NULL,
                    SegmentIds TEXT NOT NULL,
                    CONSTRAINT FK_UsenetFiles_UsenetJobs_UsenetJobId FOREIGN KEY (UsenetJobId) REFERENCES UsenetJobs (UsenetJobId) ON DELETE RESTRICT
                );

                INSERT INTO UsenetFiles_new (UsenetFileId, UsenetJobId, Path, Size, SegmentIds)
                SELECT UsenetFileId, UsenetJobId, Path, Size, SegmentIds FROM UsenetFiles;

                DROP TABLE UsenetFiles;
                ALTER TABLE UsenetFiles_new RENAME TO UsenetFiles;
                
                CREATE INDEX IF NOT EXISTS IX_UsenetFiles_UsenetJobId ON UsenetFiles (UsenetJobId);

                COMMIT;
                PRAGMA foreign_keys=ON;
            ", cancellationToken);
            logger.LogInformation("UsenetFiles table structure updated successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking/fixing UsenetFiles table structure");
            // If it fails, we might already have the correct structure or it's a different error
            // We try to commit just in case we are in a broken transaction state
            try { await dbContext.Database.ExecuteSqlRawAsync("ROLLBACK;", cancellationToken); } catch { /* ignore */ }
        }

        // Configure SQLite for better concurrency and performance
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA cache_size=-64000;", cancellationToken);

        var settings = scope.ServiceProvider.GetRequiredService<Settings>();
        await settings.Seed();
        await settings.ResetCache();

        Ready = true;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
