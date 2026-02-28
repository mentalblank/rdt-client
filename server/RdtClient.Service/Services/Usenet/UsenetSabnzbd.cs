using Microsoft.Extensions.Logging;
using RdtClient.Data.Models.Internal;
using RdtClient.Data.Models.Sabnzbd;
using RdtClient.Service.Helpers;

namespace RdtClient.Service.Services.Usenet;

public class UsenetSabnzbd(ILogger<UsenetSabnzbd> logger, UsenetQueueManager queueManager, AppSettings appSettings)
{
    public virtual async Task<SabnzbdQueue> GetQueue()
    {
        var allJobs = await queueManager.GetJobs();
        var activeJobs = allJobs.Where(j => j.Completed == null).ToList();

        var queue = new SabnzbdQueue
        {
            NoOfSlots = activeJobs.Count,
            Slots = activeJobs.Select((j, index) => new SabnzbdQueueSlot
            {
                Index = index,
                NzoId = j.Hash,
                Filename = j.JobName,
                Size = FileSizeHelper.FormatSize(j.TotalSize),
                SizeLeft = FileSizeHelper.FormatSize(j.TotalSize), 
                Percentage = "0",
                Status = "Downloading",
                Category = j.Category ?? "*",
                Priority = "Normal",
                TimeLeft = "0:00:00"
            }).ToList()
        };

        return queue;
    }

    public virtual async Task<SabnzbdHistory> GetHistory()
    {
        var allJobs = await queueManager.GetJobs();
        var completedJobs = allJobs.Where(j => j.Completed != null || j.Status == RdtClient.Data.Enums.TorrentStatus.Finished).ToList();
        
        var settings = Settings.Get.Usenet;
        var mappedPath = settings.MappedPath;

        var history = new SabnzbdHistory
        {
            NoOfSlots = completedJobs.Count,
            TotalSlots = completedJobs.Count,
            Slots = completedJobs.Select(j => {
                var storagePath = "";
                if (!String.IsNullOrWhiteSpace(mappedPath))
                {
                    storagePath = Path.Combine(mappedPath, j.Category ?? "", j.JobName);
                }

                return new SabnzbdHistorySlot
                {
                    NzoId = j.Hash,
                    Name = j.JobName,
                    NzbName = j.NzbFileName,
                    Size = FileSizeHelper.FormatSize(j.TotalSize),
                    Status = String.IsNullOrWhiteSpace(j.Error) ? "Completed" : "Failed",
                    Category = j.Category ?? "Default",
                    Path = storagePath,
                    Bytes = j.TotalSize
                };
            }).ToList()
        };

        return history;
    }

    public virtual async Task<String> AddFile(Byte[] fileBytes, String? fileName, String? category, Int32? priority)
    {
        logger.LogDebug($"Usenet: Add file {category}");
        return await queueManager.AddNzbFile(fileBytes, fileName ?? "unknown.nzb", category, priority ?? 0);
    }

    public virtual async Task Delete(String hash)
    {
        logger.LogDebug($"Usenet: Delete job {hash}");
        await queueManager.DeleteJob(hash);
    }

    public virtual List<String> GetCategories()
    {
        var categories = (Settings.Get.General.Categories ?? "radarr,sonarr,manual")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .ToList();
        categories.Insert(0, "*");
        return categories;
    }

    public virtual SabnzbdConfig GetConfig()
    {
        var savePath = Settings.Get.Usenet.MappedPath;
        var categoryList = GetCategories();

        return new SabnzbdConfig
        {
            Misc = new()
            {
                CompleteDir = savePath,
                DownloadDir = savePath,
                Port = appSettings.Port.ToString(),
                Version = "4.4.0",
                ApiKey = Settings.Get.Usenet.ApiKey
            },
            Categories = categoryList.Select((c, i) => new SabnzbdCategory
            {
                Name = c,
                Order = i,
                Dir = c == "*" ? "" : Path.Combine(savePath, c)
            }).ToList()
        };
    }
}
