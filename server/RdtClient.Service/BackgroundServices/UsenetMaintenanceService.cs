using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RdtClient.Service.Services;

namespace RdtClient.Service.BackgroundServices;

public class UsenetMaintenanceService(
    IServiceProvider serviceProvider,
    ILogger<UsenetMaintenanceService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Maintenance runs once every 24 hours
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = Settings.Get.Usenet;
                if (String.IsNullOrWhiteSpace(settings.LibraryDirectory))
                {
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                using var scope = serviceProvider.CreateScope();
                var maintenanceManager = scope.ServiceProvider.GetRequiredService<Services.Usenet.UsenetMaintenanceManager>();

                logger.LogInformation("Starting Usenet orphan cleanup task...");
                var removedCount = await maintenanceManager.RemoveOrphanedFiles(stoppingToken).ConfigureAwait(false);
                logger.LogInformation($"Usenet orphan cleanup task completed. Removed {removedCount} items.");
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Error in background maintenance: {e.Message}");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken).ConfigureAwait(false);
        }
    }
}
