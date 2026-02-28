using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RDNET;
using RdtClient.Data.Enums;
using RdtClient.Data.Models.Data;
using RdtClient.Data.Models.DebridClient;
using RdtClient.Data.Models.Internal;
using RdtClient.Service.Helpers;
using Torrent = RdtClient.Data.Models.Data.Torrent;

namespace RdtClient.Service.Services.DebridClients;

public class RealDebridDebridClient(ILogger<RealDebridDebridClient> logger, IHttpClientFactory httpClientFactory, IDownloadableFileFilter fileFilter) : IDebridClient
{
    public async Task<IList<DebridClientTorrent>> GetDownloads()
    {
        var results = await GetClient().Torrents.GetAsync(0, 100);

        return results.Select(Map).ToList();
    }

    public async Task<DebridClientUser> GetUser()
    {
        var user = await GetClient().User.GetAsync() ?? throw new("Unable to get user");

        return new()
        {
            Username = user.Username,
            Expiration = user.Premium > 0 ? DateTime.UtcNow.AddSeconds(user.Premium) : null
        };
    }

    public async Task<String> AddTorrentMagnet(String magnetLink)
    {
        try
        {
            var result = await GetClient().Torrents.AddMagnetAsync(magnetLink);

            if (result?.Id == null)
            {
                throw new("Unable to add magnet link");
            }

            return result.Id;
        }
        catch (Exception ex) when (ex.Message.Contains("slow_down", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("rate limit exceeded", StringComparison.OrdinalIgnoreCase))
        {
            throw new RateLimitException(ex.Message, TimeSpan.FromMinutes(2));
        }
    }

    public async Task<String> AddTorrentFile(Byte[] bytes)
    {
        try
        {
            var result = await GetClient().Torrents.AddFileAsync(bytes);

            if (result?.Id == null)
            {
                throw new("Unable to add torrent file");
            }

            return result.Id;
        }
        catch (Exception ex) when (ex.Message.Contains("slow_down", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("rate limit exceeded", StringComparison.OrdinalIgnoreCase))
        {
            throw new RateLimitException(ex.Message, TimeSpan.FromMinutes(2));
        }
    }

    public Task<IList<DebridClientAvailableFile>> GetAvailableFiles(String hash)
    {
        return Task.FromResult<IList<DebridClientAvailableFile>>([]);
    }

    /// <inheritdoc />
    public async Task<Int32?> SelectFiles(Torrent torrent)
    {
        if (torrent.RdId == null)
        {
            return null;
        }

        var info = await GetClient().Torrents.GetInfoAsync(torrent.RdId);

        var fileIds = (info.Files ?? [])
                          .Where(m => m != null && fileFilter.IsDownloadable(torrent, m.Path, m.Bytes))
                          .Select(m => m.Id.ToString())
                          .ToList();

        if (fileIds.Count > 0)
        {
            await GetClient().Torrents.SelectFilesAsync(torrent.RdId, [.. fileIds]);
        }

        return fileIds.Count;
    }

    public async Task Delete(Torrent torrent)
    {
        if (torrent.RdId == null)
        {
            return;
        }

        await GetClient().Torrents.DeleteAsync(torrent.RdId);
    }

    public async Task<String> Unrestrict(Torrent torrent, String link)
    {
        var result = await GetClient().Unrestrict.LinkAsync(link);

        if (result?.Download == null)
        {
            throw new("Invalid result link");
        }

        return result.Download;
    }

    public async Task<Torrent> UpdateData(Torrent torrent, DebridClientTorrent? torrentClientTorrent)
    {
        try
        {
            if (torrent.RdId == null)
            {
                return torrent;
            }

            torrentClientTorrent ??= await GetInfo(torrent.RdId);

            if (!String.IsNullOrWhiteSpace(torrentClientTorrent.Filename))
            {
                torrent.RdName = torrentClientTorrent.Filename;
            }

            if (torrentClientTorrent.Bytes > 0)
            {
                torrent.RdSize = torrentClientTorrent.Bytes;
            }

            if (torrentClientTorrent.Files != null)
            {
                torrent.RdFiles = JsonConvert.SerializeObject(torrentClientTorrent.Files);
            }

            torrent.ClientKind = Provider.RealDebrid;
            torrent.RdHost = torrentClientTorrent.Host;
            torrent.RdSplit = torrentClientTorrent.Split;
            torrent.RdProgress = torrentClientTorrent.Progress;
            torrent.RdAdded = torrentClientTorrent.Added;
            torrent.RdEnded = torrentClientTorrent.Ended;
            torrent.RdSpeed = torrentClientTorrent.Speed;
            torrent.RdSeeders = torrentClientTorrent.Seeders;
            torrent.RdStatusRaw = torrentClientTorrent.Status;

            torrent.RdStatus = torrentClientTorrent.Status switch
            {
                "magnet_error" => TorrentStatus.Error,
                "error" => TorrentStatus.Error,
                "virus" => TorrentStatus.Error,
                "dead" => TorrentStatus.Error,
                "waiting_files_selection" => TorrentStatus.WaitingForFileSelection,
                "queued" => TorrentStatus.Processing,
                "downloading" => TorrentStatus.Downloading,
                "downloaded" => TorrentStatus.Finished,
                "compressing" => TorrentStatus.Downloading,
                "uploading" => TorrentStatus.Uploading,
                _ => TorrentStatus.Error
            };
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("MAGNET_INVALID_ID"))
            {
                torrent.RdStatusRaw = "deleted";
            }
            else
            {
                throw;
            }
        }

        return torrent;
    }

    public async Task<IList<DownloadInfo>?> GetDownloadInfos(Torrent torrent)
    {
        if (torrent.RdId == null)
        {
            return null;
        }

        var info = await GetClient().Torrents.GetInfoAsync(torrent.RdId);

        if (info.Links == null) return null;

        return info.Links.Select(m => new DownloadInfo
                   {
                       RestrictedLink = m,
                       FileName = "" 
                   })
                   .ToList();
    }

    /// <inheritdoc />
    public async Task<String> GetFileName(RdtClient.Data.Models.Data.Download download)
    {
        if (download.Link == null) throw new ArgumentNullException(nameof(download.Link));
        var result = await GetClient().Unrestrict.LinkAsync(download.Link);

        if (result?.Filename == null)
        {
            throw new("Invalid result filename");
        }

        return result.Filename;
    }

    private RdNetClient GetClient()
    {
        try
        {
            var apiKey = Settings.Get.Provider.RealDebridApiKey;

            if (String.IsNullOrWhiteSpace(apiKey))
            {
                throw new("Real-Debrid API Key not set in the settings");
            }

            var httpClient = httpClientFactory.CreateClient(DiConfig.RD_CLIENT);
            var rdNetClient = new RdNetClient(null, httpClient);
            rdNetClient.UseApiAuthentication(apiKey);

            return rdNetClient;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            logger.LogError(ex, $"The connection to Real-Debrid has timed out: {ex.Message}");

            throw;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, $"The connection to Real-Debrid has timed out: {ex.Message}");

            throw;
        }
    }

    private static DebridClientTorrent Map(RDNET.Torrent torrent)
    {
        return new()
        {
            Id = torrent.Id,
            Filename = torrent.Filename,
            OriginalFilename = torrent.OriginalFilename,
            Hash = torrent.Hash,
            Bytes = torrent.Bytes,
            OriginalBytes = torrent.OriginalBytes,
            Host = torrent.Host,
            Split = torrent.Split,
            Progress = (Int64)torrent.Progress,
            Status = torrent.Status,
            Added = torrent.Added,
            Files = [],
            Links = (torrent.Links ?? []).ToList(),
            Ended = torrent.Ended,
            Speed = 0, 
            Seeders = torrent.Seeders
        };
    }

    private async Task<DebridClientTorrent> GetInfo(String id)
    {
        var result = await GetClient().Torrents.GetInfoAsync(id);

        var torrent = Map(result);

        torrent.Files = (result.Files ?? []).Select(m => new DebridClientFile
                              {
                                  Id = m.Id,
                                  Path = m.Path,
                                  Bytes = m.Bytes,
                                  Selected = m.Selected
                              })
                              .ToList();

        return torrent;
    }
}
