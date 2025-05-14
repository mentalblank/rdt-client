using Microsoft.Extensions.Logging;
using RdtClient.Data.Models.Data;
using RdtClient.Data.Models.TorrentClient;
using RdtClient.Service.Helpers;

namespace RdtClient.Service.Services.TorrentClients;

public class TorBoxMultiClient(
    ILogger<TorBoxMultiClient> logger,
    TorBoxUsenetClient usenetClient,
    TorBoxTorrentClient torrentClient
) : IMultiClient
{

    private ITorrentClient GetClient(Torrent? torrent = null, Boolean? forceUsenet = null, Boolean? forceTorrent = null)
    {
        if (forceUsenet == true && forceTorrent == true)
        {
            throw new ArgumentException("Cannot force both Usenet and Torrent clients at the same time.");
        }

        if (forceUsenet == true)
        {
            return usenetClient;
        }
        if (forceTorrent == true)
        {
            return torrentClient;
        }

        if (torrent != null)
        {
            Log($"Determining client for ContentKind: {torrent.ContentKind}", torrent);
            return torrent.ContentKind switch
            {
                0 => torrentClient,
                1 => usenetClient,
                _ => throw new ArgumentOutOfRangeException(nameof(torrent.ContentKind), torrent.ContentKind, "Invalid debrid content type specified. Expected 0 (Torrent) or 1 (Usenet).")
            };
        }

        return torrentClient; // Default fallback
    }

    private ITorrentClient GetClient(Int64? type)
    {
        return type switch
        {
            0 => torrentClient,
            1 => usenetClient,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid debrid content type specified. Expected 0 (Torrent) or 1 (Usenet).")
        };
    }

    public async Task<IList<TorrentClientTorrent>> GetTorrents()
    {
        var usenet = await usenetClient.GetTorrents();
        var torrents = await torrentClient.GetTorrents();
        return usenet.Concat(torrents).ToList();
    }

    public Task<TorrentClientUser> GetUser()
    {
        return torrentClient.GetUser();
    }

    public Task<Int32?> SelectFiles(Torrent torrent)
    {
        return GetClient(torrent).SelectFiles(torrent);
    }

    public Task<Torrent> UpdateData(Torrent torrent, TorrentClientTorrent? torrentClientTorrent)
    {
        return GetClient(torrent).UpdateData(torrent, torrentClientTorrent);
    }

    public Task<IList<DownloadInfo>?> GetDownloadInfos(Torrent torrent)
    {
        return GetClient(torrent).GetDownloadInfos(torrent);
    }

    public Task<String> GetFileName(Download download)
    {
        return Task.FromResult(download.FileName!);
    }

    // --- IMultiDownloadClient overloads with type routing ---

    public Task<String> AddMagnet(String link, Int64? type)
    {
        return GetClient(type).AddMagnet(link);
    }

    public Task<String> AddFile(Byte[] bytes, Int64? type, String? name = "NZB Upload")
    {
        var client = GetClient(type);

        if (client is TorBoxUsenetClient usenet)
        {
            return usenet.AddFile(bytes, name ?? "NZB Upload");
        }

        return client.AddFile(bytes);
    }

    public Task<String> Unrestrict(String link, Int64? type)
    {
        return GetClient(type).Unrestrict(link);
    }

    public Task<IList<TorrentClientAvailableFile>> GetAvailableFiles(String hash, Int64? type)
    {
        return GetClient(type).GetAvailableFiles(hash);
    }

    public Task Delete(String torrentId, Int64? type)
    {
        return GetClient(type).Delete(torrentId);
    }

    // --- ITorrentClient base methods (default to Torrent) ---

    public Task<String> AddMagnet(String link)
    {
        return AddMagnet(link, 0);
    }

    public Task<String> AddFile(Byte[] bytes)
    {
        return AddFile(bytes, 0);
    }

    public Task<String> Unrestrict(String link)
    {
        return Unrestrict(link, 0);
    }

    public Task<IList<TorrentClientAvailableFile>> GetAvailableFiles(String hash)
    {
        return GetAvailableFiles(hash, 0);
    }

    public Task Delete(String torrentId)
    {
        return Delete(torrentId, 0);
    }

    public static void MoveHashDirContents(String extractPath, Torrent torrent)
    {
        var hashDir = Path.Combine(extractPath, torrent.Hash);

        if (!Directory.Exists(hashDir))
        {
            return;
        }

        var innerFolder = Directory.GetDirectories(hashDir)[0];
        var moveDir = extractPath.EndsWith(torrent.RdName!) ? extractPath : hashDir;

        foreach (var file in Directory.GetFiles(innerFolder))
        {
            var destFile = Path.Combine(moveDir, Path.GetFileName(file));
            File.Move(file, destFile);
        }

        foreach (var dir in Directory.GetDirectories(innerFolder))
        {
            var destDir = Path.Combine(moveDir, Path.GetFileName(dir));
            Directory.Move(dir, destDir);
        }

        if (!extractPath.Contains(torrent.RdName!))
        {
            Directory.Delete(innerFolder, true);
        }
        else
        {
            Directory.Delete(hashDir, true);
        }
    }

    private void Log(String message, Torrent? torrent = null)
    {
        if (torrent != null)
        {
            message = $"{message} {torrent.ToLog()}";
        }

        logger.LogDebug(message);
    }
}