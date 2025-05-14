using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using RdtClient.Data.Enums;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services;
using RdtClient.Service.Helpers;

namespace RdtClient.Web.Controllers;

[Authorize(Policy = "AuthSetting")]
[Route("api")]
public class SabnzbdCompatController(ILogger<QBittorrent> logger, Torrents torrents) : ControllerBase
{
    [HttpGet("queue")]
    public async Task<IActionResult> GetQueue(string? cat)
    {
        logger.LogInformation("GetQueue called.");
        var all = await torrents.Get();

        var slots = all
            .Where(t => t.Completed == null || t.Downloads.Count == 0 || t.Downloads.Any(d => d.Completed == null))
            .Select(t =>
            {
                double rdProgress = t.RdProgress ?? 0.0;
                double downloadProgress = 0.0;

                if (t.Downloads.Count > 0)
                {
                    long bytesDone = t.Downloads.Sum(d => d.BytesDone);
                    long bytesTotal = t.Downloads.Sum(d => d.BytesTotal);
                    downloadProgress = bytesTotal > 0 ? (double)bytesDone / bytesTotal * 100 : 0.0;
                }

                double averageProgress = (rdProgress + downloadProgress) / 2.0;
                double finalProgress = (t.Completed == null && averageProgress >= 100 && t.Downloads.All(d => d.Completed == null))
                    ? 98.0 : averageProgress;
                int percent = (int)Math.Round(finalProgress);

                long sizeBytes = t.RdSize ?? 0;
                double sizeMb = sizeBytes / 1024.0 / 1024.0;
                double sizeLeftMb = sizeMb * (1 - finalProgress / 100.0);

                string status = "Downloading";
                if (t.Completed != null && t.Downloads.All(d => d.Completed != null))
                {
                    status = "Completed";
                }
                else if (!string.IsNullOrWhiteSpace(t.Error))
                {
                    status = "Failed";
                }
                else if (t.RdStatus == TorrentStatus.Downloading && t.RdSeeders < 1)
                {
                    status = "Stalled";
                }

                return new
                {
                    nzo_id = $"{t.Hash}",
                    filename = t.RdName ?? "unknown",
                    status,
                    percentage = percent,
                    mb = Math.Round(sizeMb, 2),
                    mbleft = Math.Round(sizeLeftMb, 2),
                    size = $"{Math.Round(sizeMb, 2)} MB",
                    sizeleft = $"{Math.Round(sizeLeftMb, 2)} MB",
                    timeleft = "00:05:00", // Static placeholder or calculate
                    eta = DateTime.UtcNow.AddMinutes(5).ToString("r"),
                    priority = 0,
                    cat = t.Category ?? cat ?? "tv",
                };
            }).ToList();

        var response = new
        {
            queue = new
            {
                status = slots.Any(s => s.status == "Downloading") ? "Downloading" : "Paused",
                paused = false,
                slots
            }
        };

        logger.LogInformation("Stripped queue response: {Response}", JsonConvert.SerializeObject(response));
        return Ok(response);
    }
    private static string MapStatus(Torrent t)
    {
        if (t.Completed != null)
        {
            return "Completed";
        }

        if ((t.RdProgress ?? 0) >= 100)
        {
            return "Downloading"; // keep visible until file download completes
        }

        return t.RdStatus switch
        {
            TorrentStatus.Downloading => "Downloading",
            TorrentStatus.Processing => "Fetching",
            TorrentStatus.WaitingForFileSelection => "Fetching",
            TorrentStatus.Finished => "Downloading",
            TorrentStatus.Uploading => "Extracting",
            TorrentStatus.Queued => "Queued",
            TorrentStatus.Error => "Failed",
            _ => "Queued"
        };
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(string? cat)
    {
        logger.LogInformation("GetHistory called.");
        var all = await torrents.Get();

        var completed = all.Where(t => t.Completed != null).ToList();

        var slots = completed.Select((t, i) =>
        {
            var name = t.RdName ?? $"unknown_{i}";
            var completedOn = t.Completed?.ToUnixTimeSeconds() ?? 0;
            var sizeBytes = t.RdSize ?? 0;
            var sizeGb = sizeBytes / 1024.0 / 1024.0 / 1024.0;
            var category = t.Category ?? cat ?? "tv";
            var nzoId = $"{t.Hash}";
            var fakePath = Settings.Get.DownloadClient.MappedPath;
            var fakeStorage = $"{Settings.Get.DownloadClient.MappedPath}/{category}/{name}";

            return new
            {
                nzo_id = nzoId,
                name = name,
                status = "Completed",
                category,
                size = $"{sizeGb:F1} GB",
                completed = completedOn,
                bytes = sizeBytes,
                storage = fakeStorage,
                path = fakePath,
                nzb_name = $"{name}.nzb"
            };
        }).ToList();

        var historyData = new
        {
            status = true,
            history = new
            {
                slots
            }
        };

        return Ok(historyData);
    }

    [HttpPost("addfile")]
    public async Task<IActionResult> AddFile(IFormFile uploadedFile, [FromQuery] string? cat, [FromQuery] string? apikey, [FromQuery] string? priority)
    {
        if (uploadedFile == null || uploadedFile.Length == 0)
        {
            return BadRequest(new { status = false, error = "Missing NZB upload" });
        }

        using var memoryStream = new MemoryStream();
        await uploadedFile.CopyToAsync(memoryStream);
        var bytes = memoryStream.ToArray();

        var hash = DownloadHelper.ComputeMd5Hash(bytes);

        var torrent = new Torrent
        {
            Category = cat,
            RdName = Path.GetFileNameWithoutExtension(uploadedFile.FileName),
            DebridContentKind = 2,
            Hash = hash
        };

        await torrents.AddFileToDebridQueue(bytes, torrent);

        return Ok(new
        {
            status = true,
            nzo_ids = new[] { hash }
        });
    }

    [HttpGet("get_config")]
    public IActionResult GetConfig()
    {
        var config = new
        {
            version = "3.7.0",
            apikey = Settings.Get.General.ClientApiKey,
            misc = new
            {
                complete_dir = Settings.Get.DownloadClient.MappedPath,
                pre_check = false,
                enable_tv_sorting = false,
                enable_movie_sorting = false,
                enable_date_sorting = false,
                tv_categories = Array.Empty<string>(),
                movie_categories = Array.Empty<string>(),
                date_categories = Array.Empty<string>(),
                history_retention = "0",
                history_retention_option = "all",
                history_retention_number = 0
            },
            categories = (Settings.Get.General.Categories ?? "tv,movies")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(tok =>
                {
                    tok = tok.Trim();
                    return new
                    {
                        name = tok,
                        dir = tok + "/",
                        pp = "3",
                        script = "",
                        newzbin = tok,
                        priority = "0"
                    };
                }).ToArray(),
            sorters = Array.Empty<object>()
        };

        return Ok(new { config });
    }

    [HttpGet("version")]
    public IActionResult GetVersion()
    {
        var version = new { version = "3.7.0" };
        return Ok(version);
    }


    [HttpGet("fullstatus")]
    public async Task<IActionResult> GetFullStatus()
    {
        var all = await torrents.Get();

        var slots = all
            .Where(t => t.Completed == null || t.Downloads.Count == 0 || t.Downloads.Any(d => d.Completed == null))
            .Select(t =>
            {
                double rdProgress = t.RdProgress ?? 0.0;
                double downloadProgress = 0.0;

                if (t.Downloads.Count > 0)
                {
                    long bytesDone = t.Downloads.Sum(d => d.BytesDone);
                    long bytesTotal = t.Downloads.Sum(d => d.BytesTotal);
                    downloadProgress = bytesTotal > 0 ? (double)bytesDone / bytesTotal * 100 : 0.0;
                }

                double averageProgress = (rdProgress + downloadProgress) / 2.0;
                var finalProgress = (t.Completed == null && averageProgress >= 100 && t.Downloads.All(d => d.Completed == null)) ? 98.0 : averageProgress;
                var percent = (int)Math.Round(finalProgress);

                long sizeBytes = t.RdSize ?? 0;
                double sizeMb = sizeBytes / 1024.0 / 1024.0;
                double sizeLeftMb = sizeMb * (1 - finalProgress / 100.0);

                return new
                {
                    nzo_id = $"{t.Hash}",
                    filename = t.RdName ?? "unknown",
                    status = "Downloading",
                    percentage = percent,
                    mb = Math.Round(sizeMb, 2),
                    mbleft = Math.Round(sizeLeftMb, 2),
                    size = $"{Math.Round(sizeMb, 2)} MB",
                    sizeleft = $"{Math.Round(sizeLeftMb, 2)} MB",
                    timeleft = "00:05:00",
                    eta = DateTime.UtcNow.AddMinutes(5).ToString("r"),
                    priority = 0
                };
            }).ToList();

        var minimalStatus = new
        {
            complete_dir = Settings.Get.DownloadClient.MappedPath,
            categories = (Settings.Get.General.Categories ?? "tv,movies")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(tok =>
                {
                    tok = tok.Trim();
                    return new
                    {
                        name = tok,
                        dir = tok + "/",
                        pp = "3",
                        script = "",
                        newzbin = tok,
                        priority = "0"
                    };
                }).ToArray(),
            sorters = Array.Empty<object>(), // Optional; harmless to keep
            queue = new
            {
                paused = false,
                default_root_folder = Settings.Get.DownloadClient.MappedPath,
                slots
            }
        };

        return Ok(new { status = minimalStatus });
    }

    private async Task<IActionResult> DeleteFromQueue(string? nzoId, string? apikey, string? delFiles)
    {
        if (string.IsNullOrWhiteSpace(nzoId))
        {
            return BadRequest(new { status = false, error = "Missing NZO ID (value)" });
        }
        var torrent = await torrents.GetByHash(nzoId);
        if (torrent == null)
        {
            return NotFound(new { status = false, error = "Torrent not found" });
        }
        var shouldDeleteFiles = delFiles == "1";
        try
        {
            switch (Settings.Get.Integrations.Default.FinishedAction)
            {
                case TorrentFinishedAction.RemoveAllTorrents:
                    await torrents.Delete(torrent.TorrentId, true, true, shouldDeleteFiles);
                    break;
                case TorrentFinishedAction.RemoveRealDebrid:
                    await torrents.Delete(torrent.TorrentId, false, true, shouldDeleteFiles);
                    break;
                case TorrentFinishedAction.RemoveClient:
                    await torrents.Delete(torrent.TorrentId, true, false, shouldDeleteFiles);
                    break;
                case TorrentFinishedAction.None:
                    break;
                default:
                    break;
            }
            return Ok(new { status = true });
        }
        catch (Exception)
        {
            return StatusCode(500, new { status = false, error = "Internal error during deletion" });
        }
    }

    [HttpGet("")]
    public async Task<IActionResult> LegacyApi(
        [FromQuery] string mode,
        [FromQuery] string? apikey,
        [FromQuery] string? name,
        [FromQuery] string? value,
        [FromQuery] string? cat,
        [FromQuery] string? priority,
        [FromQuery] string? del_files)
    {
        logger.LogInformation("LegacyApi called with mode: {Mode}", mode);

        return mode.ToLowerInvariant() switch
        {
            "get_config" => GetConfig(),
            "queue" when name == "delete" => await DeleteFromQueue(value, apikey, del_files),
            "queue" => await GetQueue(cat),
            "history" when name == "delete" => await DeleteFromQueue(value, apikey, del_files),
            "history" => await GetHistory(cat),
            "fullstatus" => await GetFullStatus(),
            "version" => GetVersion(),
            _ => NotFound(new { status = false, error = $"Unknown mode '{mode}'" })
        };
    }

    [HttpPost("")]
    public async Task<IActionResult> LegacyApiPost([FromQuery] string mode, [FromQuery] string? apikey, [FromQuery] string? cat, [FromQuery] string? priority)
    {
        logger.LogInformation("LegacyApiPost called with mode: {Mode}", mode);

        if (!Request.HasFormContentType)
        {
            return BadRequest(new { status = false, error = "Expected form data" });
        }

        var form = await Request.ReadFormAsync();
        var uploadedNzb = form.Files.GetFile("name");

        return mode.ToLowerInvariant() switch
        {
            "addfile" when uploadedNzb != null => await AddFile(uploadedNzb, cat, apikey, priority),
            "addfile" => BadRequest(new { status = false, error = "Missing NZB upload" }),
            _ => BadRequest(new { status = false, error = $"Unsupported POST mode '{mode}'" })
        };
    }
}