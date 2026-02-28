using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RdtClient.Data.Models.Internal;
using RdtClient.Service.Helpers;
using RdtClient.Service.Services;
using DownloadClient = RdtClient.Data.Enums.DownloadClient;

namespace RdtClient.Service.BackgroundServices;

public class DiskSpaceMonitor(ILogger<DiskSpaceMonitor> logger, IServiceProvider serviceProvider) : BackgroundService
{
    private static DiskSpaceStatus? _lastStatus;
    private Boolean _isPausedForLowDiskSpace;

    public static DiskSpaceStatus? GetCurrentStatus()
    {
        return _lastStatus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!Startup.Ready)
        {
            await Task.Delay(1000, stoppingToken);
        }

        using var scope = serviceProvider.CreateScope();
        var remoteService = scope.ServiceProvider.GetRequiredService<RemoteService>();

        logger.LogInformation("DiskSpaceMonitor started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var minimumFreeSpaceGB = Settings.Get.DownloadClient.MinimumFreeSpaceGB;

                if (minimumFreeSpaceGB <= 0)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                    continue;
                }

                var intervalMinutes = Settings.Get.DownloadClient.DiskSpaceCheckIntervalMinutes;

                if (intervalMinutes < 1)
                {
                    intervalMinutes = 1;
                }

                var downloadPaths = new List<String>
                                    {
                                        Settings.Get.DownloadClient.DownloadPath,
                                        Settings.Get.DownloadClient.DownloadPathRealDebrid!,
                                        Settings.Get.DownloadClient.DownloadPathAllDebrid!,
                                        Settings.Get.DownloadClient.DownloadPathPremiumize!,
                                        Settings.Get.DownloadClient.DownloadPathDebridLink!,
                                        Settings.Get.DownloadClient.DownloadPathTorBox!
                                    }
                                    .Where(p => !String.IsNullOrWhiteSpace(p))
                                    .Distinct()
                                    .ToList();

                Double availableSpaceGB = -1;
                String? limitingPath = null;

                foreach (var path in downloadPaths)
                {
                    logger.LogDebug($"Checking disk space for path: {path}");

                    if (!Directory.Exists(path))
                    {
                        logger.LogWarning($"Download path does not exist: {path}");

                        continue;
                    }

                    var space = FileHelper.GetAvailableFreeSpaceGB(path);

                    if (availableSpaceGB < 0 || space < availableSpaceGB)
                    {
                        availableSpaceGB = space;
                        limitingPath = path;
                    }
                }

                if (availableSpaceGB < 0)
                {
                    logger.LogWarning($"No valid download paths found to check disk space.");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                    continue;
                }

                logger.LogDebug($"Disk space check: {availableSpaceGB} GB available on {limitingPath} (threshold: {minimumFreeSpaceGB} GB, resume: {minimumFreeSpaceGB * 2} GB, isPaused: {_isPausedForLowDiskSpace})");

                if (availableSpaceGB == 0)
                {
                    logger.LogWarning($"Failed to get disk space for path: {limitingPath}");
                }

                var shouldPause = availableSpaceGB > 0 && availableSpaceGB < minimumFreeSpaceGB;
                var shouldResume = availableSpaceGB >= minimumFreeSpaceGB * 2;

                if (shouldPause && !_isPausedForLowDiskSpace)
                {
                    logger.LogWarning($"Pausing Bezzad downloads: {availableSpaceGB} GB available, threshold is {minimumFreeSpaceGB} GB");

                    var pausedCount = 0;

                    foreach (var download in TorrentRunner.ActiveDownloadClients)
                    {
                        if (download.Value.Type == DownloadClient.Bezzad)
                        {
                            logger.LogDebug($"Pausing Bezzad download: {download.Key}");
                            await download.Value.Pause();
                            pausedCount++;
                        }
                    }

                    logger.LogInformation($"Paused {pausedCount} active Bezzad downloads");

                    TorrentRunner.IsPausedForLowDiskSpace = true;
                    _isPausedForLowDiskSpace = true;

                    var status = new DiskSpaceStatus
                    {
                        IsPaused = true,
                        AvailableSpaceGB = availableSpaceGB,
                        ThresholdGB = minimumFreeSpaceGB,
                        LastCheckTime = DateTimeOffset.UtcNow
                    };

                    _lastStatus = status;
                    await remoteService.UpdateDiskSpaceStatus(status);
                }
                else if (shouldPause && _isPausedForLowDiskSpace)
                {
                    logger.LogDebug($"Still paused: {availableSpaceGB} GB available (need {minimumFreeSpaceGB * 2} GB to resume)");

                    var status = new DiskSpaceStatus
                    {
                        IsPaused = true,
                        AvailableSpaceGB = availableSpaceGB,
                        ThresholdGB = minimumFreeSpaceGB,
                        LastCheckTime = DateTimeOffset.UtcNow
                    };

                    _lastStatus = status;
                    await remoteService.UpdateDiskSpaceStatus(status);
                }
                else if (shouldResume && _isPausedForLowDiskSpace)
                {
                    logger.LogInformation($"Resuming Bezzad downloads: {availableSpaceGB} GB available, resume threshold is {minimumFreeSpaceGB * 2} GB");

                    var resumedCount = 0;

                    foreach (var download in TorrentRunner.ActiveDownloadClients)
                    {
                        if (download.Value.Type == DownloadClient.Bezzad)
                        {
                            logger.LogDebug($"Resuming Bezzad download: {download.Key}");
                            await download.Value.Resume();
                            resumedCount++;
                        }
                    }

                    logger.LogInformation($"Resumed {resumedCount} Bezzad downloads");

                    TorrentRunner.IsPausedForLowDiskSpace = false;
                    _isPausedForLowDiskSpace = false;

                    var status = new DiskSpaceStatus
                    {
                        IsPaused = false,
                        AvailableSpaceGB = availableSpaceGB,
                        ThresholdGB = minimumFreeSpaceGB,
                        LastCheckTime = DateTimeOffset.UtcNow
                    };

                    _lastStatus = status;
                    await remoteService.UpdateDiskSpaceStatus(status);
                }

                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Unexpected error in DiskSpaceMonitor: {ex.Message}");
            }
        }

        logger.LogInformation("DiskSpaceMonitor stopped.");
    }
}
