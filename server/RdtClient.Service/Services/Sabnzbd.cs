using Microsoft.Extensions.Logging;
using RdtClient.Data.Enums;
using RdtClient.Data.Models.Data;
using RdtClient.Data.Models.Internal;
using RdtClient.Data.Models.Sabnzbd;
using RdtClient.Service.Helpers;

using RdtClient.Service.Services.Usenet;

namespace RdtClient.Service.Services;

public class Sabnzbd(ILogger<Sabnzbd> logger, Torrents torrents, AppSettings appSettings, UsenetQueueManager usenetQueueManager)
{
    public virtual async Task<SabnzbdQueue> GetQueue()
    {
        var allTorrents = await torrents.Get();
        var activeTorrents = allTorrents.Where(t => t.Type == DownloadType.Nzb && t.Completed == null).ToList();
        
        var usenetJobs = await usenetQueueManager.GetJobs();
        var activeUsenetJobs = usenetJobs.Where(j => j.Completed == null).ToList();

        var slots = activeTorrents.Select((t, index) =>
                                  {
                                      var rdProgress = Math.Clamp(t.RdProgress ?? 0.0, 0.0, 100.0) / 100.0;
                                      Double progress;

                                      var dlStats = t.Downloads.Select(m => torrents.GetDownloadStats(m.DownloadId)).ToList();

                                      if (dlStats.Count > 0)
                                      {
                                          var bytesDone = dlStats.Sum(m => m.BytesDone);
                                          var bytesTotal = dlStats.Sum(m => m.BytesTotal);
                                          var downloadProgress = bytesTotal > 0 ? Math.Clamp((Double)bytesDone / bytesTotal, 0.0, 1.0) : 0;
                                          progress = (rdProgress + downloadProgress) / 2.0;
                                      }
                                      else
                                      {
                                          progress = rdProgress;
                                      }

                                      var timeLeft = "0:00:00";
                                      var startTime = t.Retry > t.Added ? t.Retry.Value : t.Added;
                                      var elapsed = DateTimeOffset.UtcNow - startTime;

                                      if (progress is > 0 and < 1.0)
                                      {
                                          var totalEstimatedTime = TimeSpan.FromTicks((Int64)(elapsed.Ticks / progress));
                                          var remaining = totalEstimatedTime - elapsed;

                                          if (remaining.TotalSeconds > 0)
                                          {
                                              timeLeft = $"{(Int32)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
                                          }
                                      }

                                      return new SabnzbdQueueSlot
                                      {
                                          Index = index,
                                          NzoId = t.Hash,
                                          Filename = t.RdName ?? t.Hash,
                                          Size = FileSizeHelper.FormatSize(dlStats.Sum(d => d.BytesTotal)),
                                          SizeLeft = FileSizeHelper.FormatSize(dlStats.Sum(d => d.BytesTotal - d.BytesDone)),
                                          Percentage = (progress * 100.0).ToString("0"),

                                          Status = t.RdStatus switch
                                          {
                                              TorrentStatus.Queued => "Queued",
                                              TorrentStatus.Processing => "Downloading",
                                              TorrentStatus.WaitingForFileSelection => "Downloading",
                                              TorrentStatus.Downloading => "Downloading",
                                              TorrentStatus.Uploading => "Downloading",
                                              TorrentStatus.Finished => "Completed",
                                              TorrentStatus.Error => "Failed",
                                              _ => "Downloading"
                                          },
                                          Category = t.Category ?? "*",
                                          Priority = "Normal",
                                          TimeLeft = timeLeft
                                      };
                                  })
                                  .ToList();

        var usenetSlots = activeUsenetJobs.Select((j, index) => new SabnzbdQueueSlot
        {
            Index = slots.Count + index,
            NzoId = j.Hash,
            Filename = j.JobName,
            Size = FileSizeHelper.FormatSize(j.TotalSize),
            SizeLeft = "0", // In streaming mode, we don't really have "size left" in the traditional sense
            Percentage = "100", // Internal Usenet jobs are effectively "finished" immediately as they are available for streaming
            Status = "Completed", 
            Category = j.Category ?? "*",
            Priority = j.Priority > 0 ? "High" : "Normal",
            TimeLeft = "0:00:00"
        });

        slots.AddRange(usenetSlots);

        return new SabnzbdQueue
        {
            NoOfSlots = slots.Count,
            Slots = slots
        };
    }

    public virtual async Task<SabnzbdHistory> GetHistory()
    {
        var allTorrents = await torrents.Get();
        var completedTorrents = allTorrents.Where(t => t.Type == DownloadType.Nzb && t.Completed != null).ToList();
        
        var usenetJobs = await usenetQueueManager.GetJobs();
        var completedUsenetJobs = usenetJobs.Where(j => j.Completed != null).ToList();

        var slots = completedTorrents.Select(t =>
                                     {
                                         var path = Settings.GetAppDefaultSavePath(t.ClientKind);

                                         if (!String.IsNullOrWhiteSpace(t.Category))
                                         {
                                             path = Path.Combine(path, t.Category);
                                         }

                                         if (!String.IsNullOrWhiteSpace(t.RdName))
                                         {
                                             path = Path.Combine(path, t.RdName);
                                         }

                                         return new SabnzbdHistorySlot
                                         {
                                             NzoId = t.Hash,
                                             Name = t.RdName ?? t.Hash,
                                             Size = FileSizeHelper.FormatSize(t.Downloads.Sum(d => d.BytesTotal)),
                                             Status = String.IsNullOrWhiteSpace(t.Error) ? "Completed" : "Failed",
                                             Category = t.Category ?? "Default",
                                             Path = path
                                         };
                                     })
                                     .ToList();

        var usenetSlots = completedUsenetJobs.Select(j =>
        {
            var path = Settings.Get.Usenet.SymlinkPath ?? "";
            if (!String.IsNullOrWhiteSpace(j.Category))
            {
                path = Path.Combine(path, j.Category);
            }
            path = Path.Combine(path, j.JobName);

            return new SabnzbdHistorySlot
            {
                NzoId = j.Hash,
                Name = j.JobName,
                Size = FileSizeHelper.FormatSize(j.TotalSize),
                Status = String.IsNullOrWhiteSpace(j.Error) ? "Completed" : "Failed",
                Category = j.Category ?? "Default",
                Path = path
            };
        });

        slots.AddRange(usenetSlots);

        return new SabnzbdHistory
        {
            NoOfSlots = slots.Count,
            TotalSlots = slots.Count,
            Slots = slots
        };
    }

    public virtual async Task<String> AddFile(Byte[] fileBytes, String? fileName, String? category, Int32? priority)
    {
        logger.LogDebug($"Add file {category}");

        var torrent = new Torrent
        {
            Category = category,
            DownloadClient = Settings.Get.DownloadClient.Client,
            HostDownloadAction = Settings.Get.Integrations.Default.HostDownloadAction,
            FinishedActionDelay = Settings.Get.Integrations.Default.FinishedActionDelay,
            DownloadAction = Settings.Get.Integrations.Default.OnlyDownloadAvailableFiles ? TorrentDownloadAction.DownloadAvailableFiles : TorrentDownloadAction.DownloadAll,
            FinishedAction = TorrentFinishedAction.None,
            DownloadMinSize = Settings.Get.Integrations.Default.MinFileSize,
            IncludeRegex = Settings.Get.Integrations.Default.IncludeRegex,
            ExcludeRegex = Settings.Get.Integrations.Default.ExcludeRegex,
            TorrentRetryAttempts = Settings.Get.Integrations.Default.TorrentRetryAttempts,
            DownloadRetryAttempts = Settings.Get.Integrations.Default.DownloadRetryAttempts,
            DeleteOnError = Settings.Get.Integrations.Default.DeleteOnError,
            Lifetime = Settings.Get.Integrations.Default.TorrentLifetime,
            Priority = (priority ?? Settings.Get.Integrations.Default.Priority) > 0 ? 1 : null
        };

        var result = await torrents.AddNzbFileToDebridQueue(fileBytes, fileName, torrent);

        return result.FirstOrDefault()?.Hash ?? "";
    }

    public virtual async Task<String> AddUrl(String url, String? category, Int32? priority)
    {
        logger.LogDebug($"Add url {category}");

        var torrent = new Torrent
        {
            Category = category,
            DownloadClient = Settings.Get.DownloadClient.Client,
            HostDownloadAction = Settings.Get.Integrations.Default.HostDownloadAction,
            FinishedActionDelay = Settings.Get.Integrations.Default.FinishedActionDelay,
            DownloadAction = Settings.Get.Integrations.Default.OnlyDownloadAvailableFiles ? TorrentDownloadAction.DownloadAvailableFiles : TorrentDownloadAction.DownloadAll,
            FinishedAction = TorrentFinishedAction.None,
            DownloadMinSize = Settings.Get.Integrations.Default.MinFileSize,
            IncludeRegex = Settings.Get.Integrations.Default.IncludeRegex,
            ExcludeRegex = Settings.Get.Integrations.Default.ExcludeRegex,
            TorrentRetryAttempts = Settings.Get.Integrations.Default.TorrentRetryAttempts,
            DownloadRetryAttempts = Settings.Get.Integrations.Default.DownloadRetryAttempts,
            DeleteOnError = Settings.Get.Integrations.Default.DeleteOnError,
            Lifetime = Settings.Get.Integrations.Default.TorrentLifetime,
            Priority = priority ?? (Settings.Get.Integrations.Default.Priority > 0 ? Settings.Get.Integrations.Default.Priority : null)
        };

        var result = await torrents.AddNzbLinkToDebridQueue(url, torrent);

        return result.FirstOrDefault()?.Hash ?? "";
    }

    public virtual async Task Delete(String hash)
    {
        var torrent = await torrents.GetByHash(hash);

        if (torrent == null || torrent.Type != DownloadType.Nzb)
        {
            return;
        }

        switch (Settings.Get.Integrations.Default.FinishedAction)
        {
            case TorrentFinishedAction.RemoveAllTorrents:
                logger.LogDebug("Removing nzb from debrid provider and RDT-Client, no files");
                await torrents.Delete(torrent.TorrentId, true, true, true);

                break;
            case TorrentFinishedAction.RemoveRealDebrid:
                logger.LogDebug("Removing nzb from debrid provider, no files");
                await torrents.Delete(torrent.TorrentId, false, true, true);

                break;
            case TorrentFinishedAction.RemoveClient:
                logger.LogDebug("Removing nzb from client, no files");
                await torrents.Delete(torrent.TorrentId, true, false, true);

                break;
            case TorrentFinishedAction.None:
                logger.LogDebug("Not removing nzb files");

                break;
            default:
                logger.LogDebug($"Invalid nzb FinishedAction {torrent.FinishedAction}", torrent);

                break;
        }
    }

    public virtual List<String> GetCategories()
    {
        var categoryList = (Settings.Get.General.Categories ?? "")
                           .Split(",", StringSplitOptions.RemoveEmptyEntries)
                           .Select(m => m.Trim())
                           .Where(m => m != "*")
                           .Distinct(StringComparer.CurrentCultureIgnoreCase)
                           .ToList();

        categoryList.Insert(0, "*");

        return categoryList;
    }

    public virtual SabnzbdConfig GetConfig()
    {
        var savePath = Settings.AppDefaultSavePath;

        var categoryList = GetCategories();

        var categories = categoryList.Select((c, i) => new SabnzbdCategory
                                     {
                                         Name = c,
                                         Order = i,
                                         Dir = c == "*" ? "" : Path.Combine(savePath, c)
                                     })
                                     .ToList();

        var config = new SabnzbdConfig
        {
            Misc = new()
            {
                CompleteDir = savePath,
                DownloadDir = savePath,
                Port = appSettings.Port.ToString(),
                Version = "4.4.0"
            },
            Categories = categories
        };

        return config;
    }
}
