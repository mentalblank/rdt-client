using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.Exceptions;
using RdtClient.Service.Services.Usenet.Extensions;
using RdtClient.Service.Services.Usenet.Utils;
using RdtClient.Service.Services.Usenet.Clients.RadarrSonarr;
using RdtClient.Service.Services.Usenet;
using RdtClient.Service.Services;

namespace RdtClient.Service.BackgroundServices;

public class UsenetHealthCheckService(
    IServiceProvider serviceProvider,
    INntpClient usenetClient,
    ILogger<UsenetHealthCheckService> logger
) : BackgroundService
{
    private static readonly HashSet<String> MissingSegmentIds = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = Settings.Get.Usenet;
                if (!settings.EnableBackgroundRepairs)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                using var scope = serviceProvider.CreateScope();
                var dataContext = scope.ServiceProvider.GetRequiredService<DataContext>();

                var currentDateTime = DateTimeOffset.UtcNow;
                var davItem = await dataContext.UsenetDavItems
                    .Where(x => x.Type == UsenetDavItem.UsenetItemType.NzbFile
                                || x.Type == UsenetDavItem.UsenetItemType.RarFile
                                || x.Type == UsenetDavItem.UsenetItemType.MultipartFile)
                    .Where(x => x.NextHealthCheck == null || x.NextHealthCheck < currentDateTime)
                    .OrderBy(x => x.NextHealthCheck)
                    .ThenByDescending(x => x.ReleaseDate)
                    .FirstOrDefaultAsync(stoppingToken).ConfigureAwait(false);

                if (davItem == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await PerformHealthCheck(davItem, dataContext, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Error in background health check: {e.Message}");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task PerformHealthCheck(UsenetDavItem davItem, DataContext dataContext, CancellationToken ct)
    {
        try
        {
            var segments = await GetAllSegments(davItem, dataContext, ct).ConfigureAwait(false);
            if (davItem.ReleaseDate == null && segments.Count > 0)
            {
                var firstSegmentId = segments[0];
                var head = await usenetClient.HeadAsync(firstSegmentId, ct).ConfigureAwait(false);
                davItem.ReleaseDate = head.ArticleHeaders!.Date;
            }

            var concurrency = Settings.Get.Usenet.MaxDownloadConnections;
            await usenetClient.CheckAllSegmentsAsync(segments, concurrency, null, ct).ConfigureAwait(false);

            davItem.LastHealthCheck = DateTimeOffset.UtcNow;
            davItem.NextHealthCheck = davItem.ReleaseDate + 2 * (davItem.LastHealthCheck - davItem.ReleaseDate);
            
            dataContext.UsenetHealthCheckResults.Add(new UsenetHealthCheckResult
            {
                Id = Guid.NewGuid(),
                DavItemId = davItem.Id,
                Path = davItem.Path,
                CreatedAt = DateTimeOffset.UtcNow,
                Result = UsenetHealthCheckResult.HealthResult.Healthy,
                RepairStatus = UsenetHealthCheckResult.RepairAction.None,
                Message = "File is healthy."
            });
            
            await dataContext.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (UsenetArticleNotFoundException e)
        {
            if (FilenameUtil.IsImportantFileType(davItem.Name))
            {
                lock (MissingSegmentIds) MissingSegmentIds.Add(e.SegmentId);
            }

            await Repair(davItem, dataContext, ct).ConfigureAwait(false);
        }
    }

    private async Task<List<String>> GetAllSegments(UsenetDavItem davItem, DataContext dataContext, CancellationToken ct)
    {
        if (davItem.Type == UsenetDavItem.UsenetItemType.NzbFile)
        {
            var nzbFile = await dataContext.UsenetNzbFiles.FirstOrDefaultAsync(x => x.Id == davItem.Id, ct).ConfigureAwait(false);
            return nzbFile?.SegmentIdList?.ToList() ?? [];
        }

        if (davItem.Type == UsenetDavItem.UsenetItemType.RarFile)
        {
            var rarFile = await dataContext.UsenetRarFiles.FirstOrDefaultAsync(x => x.Id == davItem.Id, ct).ConfigureAwait(false);
            return rarFile?.RarPartList?.SelectMany(x => x.SegmentIds)?.ToList() ?? [];
        }

        if (davItem.Type == UsenetDavItem.UsenetItemType.MultipartFile)
        {
            var multipartFile = await dataContext.UsenetMultipartFiles.FirstOrDefaultAsync(x => x.Id == davItem.Id, ct).ConfigureAwait(false);
            return multipartFile?.MetadataObject?.FileParts?.SelectMany(x => x.SegmentIds)?.ToList() ?? [];
        }

        return [];
    }

    private async Task Repair(UsenetDavItem davItem, DataContext dataContext, CancellationToken ct)
    {
        try
        {
            var symlinkOrStrmPath = UsenetOrganizedLinksUtil.GetLink(davItem);
            if (String.IsNullOrWhiteSpace(symlinkOrStrmPath))
            {
                dataContext.UsenetDavItems.Remove(davItem);
                dataContext.UsenetHealthCheckResults.Add(new UsenetHealthCheckResult
                {
                    Id = Guid.NewGuid(),
                    DavItemId = davItem.Id,
                    Path = davItem.Path,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Result = UsenetHealthCheckResult.HealthResult.Unhealthy,
                    RepairStatus = UsenetHealthCheckResult.RepairAction.Deleted,
                    Message = "File was unhealthy and orphaned (no link found in library). Deleted."
                });
                await dataContext.SaveChangesAsync(ct).ConfigureAwait(false);
                return;
            }

            var linkType = symlinkOrStrmPath.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) ? "strm-file" : "symlink";
            var arrClients = GetArrClients();

            foreach (var arrClient in arrClients)
            {
                var rootFolders = await arrClient.GetRootFolders().ConfigureAwait(false);
                if (!rootFolders.Any(x => symlinkOrStrmPath.StartsWith(x.Path!, StringComparison.OrdinalIgnoreCase))) continue;

                if (await arrClient.RemoveAndSearch(symlinkOrStrmPath).ConfigureAwait(false))
                {
                    dataContext.UsenetDavItems.Remove(davItem);
                    dataContext.UsenetHealthCheckResults.Add(new UsenetHealthCheckResult
                    {
                        Id = Guid.NewGuid(),
                        DavItemId = davItem.Id,
                        Path = davItem.Path,
                        CreatedAt = DateTimeOffset.UtcNow,
                        Result = UsenetHealthCheckResult.HealthResult.Unhealthy,
                        RepairStatus = UsenetHealthCheckResult.RepairAction.Repaired,
                        Message = $"Triggered new {arrClient.GetType().Name} search for unhealthy {linkType}."
                    });
                    await dataContext.SaveChangesAsync(ct).ConfigureAwait(false);
                    return;
                }
                break;
            }

            // Fallback: delete item and link if no Arr client handled it
            if (File.Exists(symlinkOrStrmPath)) File.Delete(symlinkOrStrmPath);
            dataContext.UsenetDavItems.Remove(davItem);
            dataContext.UsenetHealthCheckResults.Add(new UsenetHealthCheckResult
            {
                Id = Guid.NewGuid(),
                DavItemId = davItem.Id,
                Path = davItem.Path,
                CreatedAt = DateTimeOffset.UtcNow,
                Result = UsenetHealthCheckResult.HealthResult.Unhealthy,
                RepairStatus = UsenetHealthCheckResult.RepairAction.Deleted,
                Message = $"Unhealthy {linkType} deleted. Could not trigger Arr search."
            });
            await dataContext.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            var utcNow = DateTimeOffset.UtcNow;
            davItem.LastHealthCheck = utcNow;
            davItem.NextHealthCheck = utcNow + TimeSpan.FromDays(1);
            dataContext.UsenetHealthCheckResults.Add(new UsenetHealthCheckResult
            {
                Id = Guid.NewGuid(),
                DavItemId = davItem.Id,
                Path = davItem.Path,
                CreatedAt = utcNow,
                Result = UsenetHealthCheckResult.HealthResult.Unhealthy,
                RepairStatus = UsenetHealthCheckResult.RepairAction.ActionNeeded,
                Message = $"Error performing file repair: {e.Message}"
            });
            await dataContext.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private IEnumerable<UsenetArrClient> GetArrClients()
    {
        var settings = Settings.Get.Usenet;
        var clients = new List<UsenetArrClient>();
        if (!String.IsNullOrWhiteSpace(settings.RadarrHost) && !String.IsNullOrWhiteSpace(settings.RadarrApiKey))
            clients.Add(new UsenetRadarrClient(settings.RadarrHost, settings.RadarrApiKey));
        if (!String.IsNullOrWhiteSpace(settings.SonarrHost) && !String.IsNullOrWhiteSpace(settings.SonarrApiKey))
            clients.Add(new UsenetSonarrClient(settings.SonarrHost, settings.SonarrApiKey));
        return clients;
    }

    public static void CheckCachedMissingSegmentIds(IEnumerable<String> segmentIds)
    {
        lock (MissingSegmentIds)
        {
            foreach (var segmentId in segmentIds)
                if (MissingSegmentIds.Contains(segmentId))
                    throw new UsenetArticleNotFoundException(segmentId);
        }
    }
}
