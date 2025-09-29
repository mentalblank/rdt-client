﻿using System.Globalization;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using RdtClient.Data.Data;
using RdtClient.Data.Enums;
using RdtClient.Data.Models.Data;
using RdtClient.Data.Models.Internal;
using RdtClient.Data.Models.TorrentClient;
using RdtClient.Service.BackgroundServices;
using RdtClient.Service.Helpers;
using RdtClient.Service.Services.TorrentClients;
using RdtClient.Service.Wrappers;
using Torrent = RdtClient.Data.Models.Data.Torrent;

namespace RdtClient.Service.Services;

public class Torrents(
    ILogger<Torrents> logger,
    ITorrentData torrentData,
    IDownloads downloads,
    IProcessFactory processFactory,
    IFileSystem fileSystem,
    IEnricher enricher,
    AllDebridTorrentClient allDebridTorrentClient,
    PremiumizeTorrentClient premiumizeTorrentClient,
    RealDebridTorrentClient realDebridTorrentClient,
    DebridLinkClient debridLinkClient,
    TorBoxMultiClient torBoxMultiClient)
{
    private static readonly SemaphoreSlim RealDebridUpdateLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    private ITorrentClient TorrentClient
    {
        get
        {
            return Settings.Get.Provider.Provider switch
            {
                Provider.Premiumize => premiumizeTorrentClient,
                Provider.RealDebrid => realDebridTorrentClient,
                Provider.AllDebrid => allDebridTorrentClient,
                Provider.DebridLink => debridLinkClient,
                Provider.TorBox => torBoxMultiClient,
                _ => throw new Exception("Invalid Provider")
            };
        }
    }

    private static readonly SemaphoreSlim TorrentResetLock = new(1, 1);

    public async Task<IList<Torrent>> Get()
    {
        var torrents = await torrentData.Get();

        foreach (var torrent in torrents)
        {
            foreach (var download in torrent.Downloads)
            {
                if (TorrentRunner.ActiveDownloadClients.TryGetValue(download.DownloadId, out var downloadClient))
                {
                    download.Speed = downloadClient.Speed;
                    download.BytesTotal = downloadClient.BytesTotal;
                    download.BytesDone = downloadClient.BytesDone;
                }

                if (TorrentRunner.ActiveUnpackClients.TryGetValue(download.DownloadId, out var unpackClient))
                {
                    download.BytesTotal = 100;
                    download.BytesDone = unpackClient.Progess;
                }
            }
        }

        return torrents;
    }

    public async Task<Torrent?> GetByHash(String hash)
    {
        var torrent = await torrentData.GetByHash(hash);

        if (torrent != null)
        {
            await UpdateTorrentClientData(torrent);
        }

        return torrent;
    }

    public async Task UpdateCategory(String hash, String? category)
    {
        var torrent = await torrentData.GetByHash(hash);

        if (torrent == null)
        {
            return;
        }

        Log($"Update category to {category}", torrent);

        await torrentData.UpdateCategory(torrent.TorrentId, category);
    }

    public async Task<Torrent> AddMagnetToDebridQueue(String magnetLink, Torrent torrent)
    {
        if (torrent.ContentKind == 1)
        {
            if (TorrentClient is not IMultiClient)
            {
                throw new Exception("NZB support is not available for the current provider.");
            }

            var bytes = await DownloadHelper.DownloadBytesFromUrl(magnetLink);
            var hash = DownloadHelper.ComputeMd5Hash(bytes);
            torrent.RdStatus = TorrentStatus.Queued;
            Log($"Adding {hash} (NZB) to queue", torrent);
            await CopyAddedTorrent(torrent.RdName!, bytes, torrent.ContentKind);

            return await AddQueued(hash, magnetLink, false, torrent);
        }
        try
        {
            var enriched = await enricher.EnrichMagnetLink(magnetLink);
            MagnetLink magnet;

            try
            {
                magnet = MagnetLink.Parse(magnetLink);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{ex.Message}, trying to parse {enriched}", ex.Message, enriched);

                throw new Exception($"{ex.Message}, trying to parse {enriched}");
            }

            torrent.RdStatus = TorrentStatus.Queued;
            torrent.RdName = magnet.Name;

            var hash = magnet.InfoHashes.V1OrV2.ToHex();

            Log($"Adding {hash} (magnet link) to queue", torrent);
            await CopyAddedTorrent(magnet.Name!, magnetLink, torrent.ContentKind);

            return await AddQueued(hash, enriched, false, torrent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Message}, trying to parse {MagnetLink}", ex.Message, magnetLink);

            throw new Exception($"{ex.Message}, trying to parse {magnetLink}");
        }
    }

    public async Task<Torrent> AddFileToDebridQueue(Byte[] bytes, Torrent torrent)
    {
        String hash;

        if (torrent.ContentKind == 1)
        {
            if (TorrentClient is not IMultiClient multiClient)
            {
                throw new Exception("NZB support is not available for the current provider.");
            }

            hash = await multiClient.AddFile(bytes, torrent.ContentKind, torrent.RdName);
            torrent.RdStatus = TorrentStatus.Queued;
            await CopyAddedTorrent(torrent.RdName ?? hash, bytes, torrent.ContentKind);
        }
        else
        {
            var enriched = await enricher.EnrichTorrentBytes(bytes);

            hash = await TorrentClient.AddFile(enriched);

            if (enriched.SequenceEqual(bytes))
            {
                logger.LogDebug("bytes {Bytes}", bytes);
            }
            else
            {
                logger.LogDebug("enriched bytes {Enriched}", enriched);
            }

            try
            {
                var monoTorrent = await MonoTorrent.Torrent.LoadAsync(bytes);
                torrent.RdName = monoTorrent.Name;
                await CopyAddedTorrent(monoTorrent.Name, bytes, torrent.ContentKind);
            }
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message}, trying to parse {Convert.ToBase64String(bytes)}");
            }

            torrent.RdStatus = TorrentStatus.Queued;
        }

        return await AddQueued(hash, Convert.ToBase64String(bytes), true, torrent);
    }

    private async Task CopyAddedTorrent(String downloadName, Object fileOrMagnet, Int64 contentKind)
    {
        if (!String.IsNullOrWhiteSpace(Settings.Get.General.CopyAddedTorrents))
        {
            try
            {
                var targetDir = Settings.Get.General.CopyAddedTorrents;

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                var safeName = FileHelper.RemoveInvalidFileNameChars(downloadName);

                var extension = fileOrMagnet switch
                {
                    String _ => ".magnet",
                    Byte[] _ when contentKind == 0 => ".torrent",
                    Byte[] _ when contentKind == 1 => ".nzb",
                    _ => throw new ArgumentException($"Unsupported download type: kind={contentKind}")
                };

                var outPath = Path.Combine(targetDir, safeName + extension);

                if (File.Exists(outPath))
                {
                    File.Delete(outPath);
                }

                switch (fileOrMagnet)
                {
                    case String magnet:
                        await File.WriteAllTextAsync(outPath, magnet);

                        break;
                    case Byte[] data:
                        await File.WriteAllBytesAsync(outPath, data);

                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to copy “{downloadName}” to “{Settings.Get.General.CopyAddedTorrents}”: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Adds torrent in database to debrid provider and updates database accordingly.
    /// </summary>
    /// <param name="torrent">The torrent from the database to upload to the debrid provider</param>
    /// <returns>Updated torrent</returns>
    /// <exception cref="Exception">When RdId is not null or FileOrMagnet is null.</exception>
    public async Task DequeueFromDebridQueue(Torrent torrent)
    {
        if (torrent.RdId != null)
        {
            throw new InvalidOperationException("Torrent already added to debrid provider, cannot dequeue.");
        }

        if (String.IsNullOrEmpty(torrent.FileOrMagnet))
        {
            throw new InvalidOperationException("Torrent has no torrent file or magnet link.");
        }

        logger.LogDebug("Adding {hash} to debrid provider {torrentInfo}", torrent.Hash, torrent.ToLog());

        await RealDebridUpdateLock.WaitAsync();

        try
        {
            var id = TorrentClient switch
            {
                IMultiClient multiClient => torrent.IsFile
                    ? await multiClient.AddFile(Convert.FromBase64String(torrent.FileOrMagnet), torrent.ContentKind, torrent.RdName)
                    : await multiClient.AddMagnet(torrent.FileOrMagnet, torrent.ContentKind),
                _ => torrent.IsFile
                    ? await TorrentClient.AddFile(Convert.FromBase64String(torrent.FileOrMagnet))
                    : await TorrentClient.AddMagnet(torrent.FileOrMagnet)
            };

            await torrentData.UpdateRdId(torrent, id);
            await UpdateTorrentClientData(torrent);
        }
        finally
        {
            RealDebridUpdateLock.Release();
        }
    }

    public async Task<IList<TorrentClientAvailableFile>> GetAvailableFiles(String hash, Int32? type = null)
    {
        if (String.IsNullOrWhiteSpace(hash))
        {
            throw new ArgumentNullException(nameof(hash));
        }

        IList<TorrentClientAvailableFile> result;

        if (TorrentClient is IMultiClient multiClient)
        {
            result = await multiClient.GetAvailableFiles(hash, type);
        }
        else
        {
            result = await TorrentClient.GetAvailableFiles(hash);
        }

        return result;
    }

    public async Task SelectFiles(Guid torrentId)
    {
        var torrent = await GetById(torrentId);

        if (torrent == null)
        {
            return;
        }

        var selected = await TorrentClient.SelectFiles(torrent);

        if (selected == 0)
        {
            await MarkAllFilesExcluded(torrent);
        }
    }

    public async Task CreateDownloads(Guid torrentId)
    {
        var torrent = await GetById(torrentId);

        if (torrent == null)
        {
            return;
        }

        var downloadInfos = await TorrentClient.GetDownloadInfos(torrent);

        if (downloadInfos == null)
        {
            return;
        }

        if (downloadInfos.Count == 0)
        {
            await MarkAllFilesExcluded(torrent);

            return;
        }

        foreach (var downloadInfo in downloadInfos)
        {
            // Make sure downloads don't get added multiple times
            var downloadExists = await downloads.Get(torrent.TorrentId, downloadInfo.RestrictedLink);

            if (downloadExists == null && !String.IsNullOrWhiteSpace(downloadInfo.RestrictedLink))
            {
                await downloads.Add(torrent.TorrentId, downloadInfo);
            }
        }
    }

    /// <summary>
    /// Logs a message to the console, sets the error on the torrent and ensures it is not retried.
    /// </summary>
    /// <param name="torrent">The torrent to mark as "All files excluded"</param>
    private async Task MarkAllFilesExcluded(Torrent torrent)
    {
        logger.LogInformation("All files excluded by filters (IncludeRegex: {includeRegex}, ExcludeRegex: {excludeRegex}, DownloadMinSize: {downloadMinSize}) {torrentInfo}",
                              torrent.IncludeRegex,
                              torrent.ExcludeRegex,
                              torrent.DownloadMinSize,
                              torrent.ToLog());

        await torrentData.UpdateRetry(torrent.TorrentId, null, torrent.TorrentRetryAttempts);
        await torrentData.UpdateComplete(torrent.TorrentId, "All files excluded", DateTimeOffset.Now, false);
    }

    public async Task Delete(Guid torrentId, Boolean deleteData, Boolean deleteRdTorrent, Boolean deleteLocalFiles)
    {
        var torrent = await GetById(torrentId);

        if (torrent == null)
        {
            return;
        }

        Log($"Deleting", torrent);

        await UpdateComplete(torrentId, "Torrent deleted", DateTimeOffset.UtcNow, false);

        foreach (var download in torrent.Downloads)
        {
            var retry = 10;

            while (TorrentRunner.ActiveDownloadClients.TryGetValue(download.DownloadId, out var downloadClient))
            {
                Log($"Cancelling download", download, torrent);

                await downloadClient.Cancel();

                await Task.Delay(500);

                retry++;

                if (retry > 5)
                {
                    break;
                }
            }

            retry = 10;

            while (TorrentRunner.ActiveUnpackClients.TryGetValue(download.DownloadId, out var unpackClient))
            {
                Log($"Cancelling unpack", download, torrent);

                unpackClient.Cancel();

                await Task.Delay(500);

                retry++;

                if (retry > 10)
                {
                    break;
                }
            }
        }

        if (deleteData)
        {
            Log($"Deleting data", torrent);

            await downloads.DeleteForTorrent(torrent.TorrentId);
            await torrentData.Delete(torrentId);
        }

        if (deleteRdTorrent && torrent.RdId != null)
        {
            Log($"Deleting torrent", torrent);

            try
            {
                await TorrentClient.Delete(torrent.RdId);
            }
            catch
            {
                // ignored
            }
        }

        if (deleteLocalFiles && !String.IsNullOrWhiteSpace(torrent.RdName))
        {
            var downloadPath = DownloadPath(torrent);
            downloadPath = Path.Combine(downloadPath, torrent.RdName);

            Log($"Deleting local files in {downloadPath}", torrent);

            if (Directory.Exists(downloadPath))
            {
                var retry = 0;

                while (true)
                {
                    try
                    {
                        Directory.Delete(downloadPath, true);

                        break;
                    }
                    catch
                    {
                        retry++;

                        if (retry >= 3)
                        {
                            throw;
                        }

                        await Task.Delay(1000);
                    }
                }
            }
        }
    }

    public async Task<String> UnrestrictLink(Guid downloadId)
    {
        var download = await downloads.GetById(downloadId) ?? throw new Exception($"Download with ID {downloadId} not found");

        Log("Unrestricting link", download, download.Torrent);

        var unrestrictedLink = TorrentClient switch
        {
            IMultiClient multiClient => await multiClient.Unrestrict(download.Path, download.Torrent!.ContentKind),
            _ => await TorrentClient.Unrestrict(download.Path)
        };

        await downloads.UpdateUnrestrictedLink(downloadId, unrestrictedLink);

        return unrestrictedLink;
    }

    /// <summary>
    /// To be called only when <see cref="Data.Models.Data.Download" />.<see cref="Data.Models.Data.Download.FileName" /> is not set by
    /// <see cref="ITorrentClient.GetDownloadInfos" />
    /// </summary>
    public async Task<String> RetrieveFileName(Guid downloadId)
    {
        var download = await downloads.GetById(downloadId) ?? throw new Exception($"Download with ID {downloadId} not found");

        Log($"Retrieving filename for", download, download.Torrent);

        var fileName = await TorrentClient.GetFileName(download);

        await downloads.UpdateFileName(downloadId, fileName);

        return fileName;
    }

    public async Task<Profile> GetProfile()
    {
        var user = await TorrentClient.GetUser();

        var profile = new Profile
        {
            Provider = Enum.GetName(Settings.Get.Provider.Provider),
            UserName = user.Username,
            Expiration = user.Expiration,
            CurrentVersion = UpdateChecker.CurrentVersion,
            LatestVersion = UpdateChecker.LatestVersion,
            IsInsecure = UpdateChecker.IsInsecure,
            DisableUpdateNotification = Settings.Get.General.DisableUpdateNotifications
        };

        return profile;
    }

    public async Task UpdateRdData()
    {
        await RealDebridUpdateLock.WaitAsync();

        var torrents = await Get();

        try
        {
            var rdTorrents = await TorrentClient.GetTorrents();

            foreach (var rdTorrent in rdTorrents)
            {
                var torrent = torrents.FirstOrDefault(m => m.RdId == rdTorrent.Id);

                // Auto import torrents only torrents that have their files selected
                if (torrent == null && Settings.Get.Provider.AutoImport)
                {
                    var newTorrent = new Torrent
                    {
                        Category = Settings.Get.Provider.Default.Category,
                        DownloadClient = Settings.Get.DownloadClient.Client,
                        DownloadAction =
                            Settings.Get.Provider.Default.OnlyDownloadAvailableFiles ? TorrentDownloadAction.DownloadAvailableFiles : TorrentDownloadAction.DownloadAll,
                        HostDownloadAction = Settings.Get.Provider.Default.HostDownloadAction,
                        FinishedActionDelay = Settings.Get.Provider.Default.FinishedActionDelay,
                        FinishedAction = Settings.Get.Provider.Default.FinishedAction,
                        DownloadMinSize = Settings.Get.Provider.Default.MinFileSize,
                        IncludeRegex = Settings.Get.Provider.Default.IncludeRegex,
                        ExcludeRegex = Settings.Get.Provider.Default.ExcludeRegex,
                        TorrentRetryAttempts = Settings.Get.Provider.Default.TorrentRetryAttempts,
                        DownloadRetryAttempts = Settings.Get.Provider.Default.DownloadRetryAttempts,
                        DeleteOnError = Settings.Get.Provider.Default.DeleteOnError,
                        Lifetime = Settings.Get.Provider.Default.TorrentLifetime,
                        Priority = Settings.Get.Provider.Default.Priority > 0 ? Settings.Get.Provider.Default.Priority : null,
                        RdId = rdTorrent.Id,
                        ContentKind = rdTorrent.ContentKind,
                    };

                    if (newTorrent is { ContentKind: 0, RdStatus: TorrentStatus.WaitingForFileSelection })
                    {
                        continue;
                    }

                    torrent = await torrentData.Add(rdTorrent.Id, rdTorrent.Hash, null, false, Settings.Get.DownloadClient.Client, newTorrent);

                    await UpdateTorrentClientData(torrent, rdTorrent);
                }
                else if (torrent != null)
                {
                    await UpdateTorrentClientData(torrent, rdTorrent);
                }
            }

            foreach (var torrent in torrents)
            {
                var rdTorrent = rdTorrents.FirstOrDefault(m => m.Id == torrent.RdId);

                if (rdTorrent == null && Settings.Get.Provider.AutoDelete && torrent.RdStatus != TorrentStatus.Queued)
                {
                    await Delete(torrent.TorrentId, true, false, true);
                }
            }
        }
        finally
        {
            RealDebridUpdateLock.Release();
        }
    }

    public async Task RetryTorrent(Guid torrentId, Int32 retryCount)
    {
        await TorrentResetLock.WaitAsync();

        try
        {
            var torrent = await torrentData.GetById(torrentId);

            if (torrent?.Retry == null)
            {
                return;
            }

            Log($"Retrying Torrent", torrent);

            await UpdateComplete(torrent.TorrentId, "Retrying Torrent", DateTimeOffset.UtcNow, false);
            await UpdateRetry(torrent.TorrentId, null, 0);

            foreach (var download in torrent.Downloads)
            {
                await downloads.UpdateError(download.DownloadId, null);
                await downloads.UpdateCompleted(download.DownloadId, DateTimeOffset.UtcNow);
            }

            foreach (var download in torrent.Downloads)
            {
                while (TorrentRunner.ActiveDownloadClients.TryRemove(download.DownloadId, out var downloadClient))
                {
                    await downloadClient.Cancel();

                    await Task.Delay(100);
                }

                while (TorrentRunner.ActiveUnpackClients.TryRemove(download.DownloadId, out var unpackClient))
                {
                    unpackClient.Cancel();

                    await Task.Delay(100);
                }
            }

            await Delete(torrentId, true, true, true);

            if (String.IsNullOrWhiteSpace(torrent.FileOrMagnet))
            {
                throw new Exception($"Cannot re-add this torrent, original magnet or file not found");
            }

            Torrent newTorrent;

            if (torrent.IsFile)
            {
                var bytes = Convert.FromBase64String(torrent.FileOrMagnet);

                newTorrent = await AddFileToDebridQueue(bytes, torrent);
            }
            else
            {
                newTorrent = await AddMagnetToDebridQueue(torrent.FileOrMagnet, torrent);
            }

            await torrentData.UpdateRetry(newTorrent.TorrentId, null, retryCount);
        }
        finally
        {
            TorrentResetLock.Release();
        }
    }

    public async Task RetryDownload(Guid downloadId)
    {
        var download = await downloads.GetById(downloadId);

        if (download == null)
        {
            return;
        }

        Log($"Retrying Download", download, download.Torrent);

        while (TorrentRunner.ActiveDownloadClients.TryRemove(download.DownloadId, out var downloadClient))
        {
            await downloadClient.Cancel();

            await Task.Delay(100);
        }

        while (TorrentRunner.ActiveUnpackClients.TryRemove(download.DownloadId, out var unpackClient))
        {
            unpackClient.Cancel();

            await Task.Delay(100);
        }

        var downloadPath = DownloadPath(download.Torrent!);

        var filePath = DownloadHelper.GetDownloadPath(downloadPath, download.Torrent!, download);

        if (filePath != null)
        {
            Log($"Deleting {filePath}", download, download.Torrent);

            await FileHelper.Delete(filePath);
        }

        Log($"Resetting", download, download.Torrent);

        await downloads.Reset(downloadId);

        await torrentData.UpdateComplete(download.TorrentId, null, null, false);
    }

    public async Task UpdateComplete(Guid torrentId, String? error, DateTimeOffset datetime, Boolean retry)
    {
        await torrentData.UpdateComplete(torrentId, error, datetime, retry);
    }

    public async Task UpdateFilesSelected(Guid torrentId, DateTimeOffset datetime)
    {
        await torrentData.UpdateFilesSelected(torrentId, datetime);
    }

    public async Task UpdatePriority(String hash, Int32 priority)
    {
        var torrent = await torrentData.GetByHash(hash);

        if (torrent == null)
        {
            return;
        }

        await torrentData.UpdatePriority(torrent.TorrentId, priority);
    }

    public async Task UpdateRetry(Guid torrentId, DateTimeOffset? datetime, Int32 retry)
    {
        await torrentData.UpdateRetry(torrentId, datetime, retry);
    }

    public async Task UpdateError(Guid torrentId, String error)
    {
        await torrentData.UpdateError(torrentId, error);
    }

    public async Task<Torrent?> GetById(Guid torrentId)
    {
        var torrent = await torrentData.GetById(torrentId);

        if (torrent == null)
        {
            return null;
        }

        await UpdateTorrentClientData(torrent);

        foreach (var download in torrent.Downloads)
        {
            if (TorrentRunner.ActiveDownloadClients.TryGetValue(download.DownloadId, out var downloadClient))
            {
                download.Speed = downloadClient.Speed;
                download.BytesTotal = downloadClient.BytesTotal;
                download.BytesDone = downloadClient.BytesDone;
            }

            if (TorrentRunner.ActiveUnpackClients.TryGetValue(download.DownloadId, out var unpackClient))
            {
                download.BytesTotal = 100;
                download.BytesDone = unpackClient.Progess;
            }
        }

        return torrent;
    }

    private static String DownloadPath(Torrent torrent)
    {
        var settingDownloadPath = Settings.Get.DownloadClient.DownloadPath;

        if (!String.IsNullOrWhiteSpace(torrent.Category))
        {
            settingDownloadPath = Path.Combine(settingDownloadPath, torrent.Category);
        }

        return settingDownloadPath;
    }

    private async Task<Torrent> AddQueued(String infoHash,
                                          String fileOrMagnetContents,
                                          Boolean isFile,
                                          Torrent torrent)
    {
        var existingTorrent = await torrentData.GetByHash(infoHash);

        if (existingTorrent != null)
        {
            return existingTorrent;
        }

        var newTorrent = await torrentData.Add(null,
                                               infoHash,
                                               fileOrMagnetContents,
                                               isFile,
                                               torrent.DownloadClient,
                                               torrent);

        return newTorrent;
    }

    public async Task Update(Torrent torrent)
    {
        await torrentData.Update(torrent);
    }

    public async Task RunTorrentComplete(Guid torrentId, DbSettings? settings = null)
    {
        settings ??= Settings.Get;

        if (String.IsNullOrWhiteSpace(settings.General.RunOnTorrentCompleteFileName))
        {
            return;
        }

        var torrent = await torrentData.GetById(torrentId) ?? throw new Exception($"Cannot find Torrent with ID {torrentId}");

        var downloadsForTorrent = await downloads.GetForTorrent(torrentId);

        var fileName = settings.General.RunOnTorrentCompleteFileName;
        var arguments = settings.General.RunOnTorrentCompleteArguments ?? "";

        Log($"Parsing external program {fileName} with arguments {arguments}", torrent);

        var downloadPath = DownloadPath(torrent);
        var torrentPath = Path.Combine(downloadPath, torrent.RdName ?? "Unknown");

        var filePath = torrentPath;

        var files = fileSystem.Directory.GetFiles(filePath);

        if (files.Length == 1)
        {
            filePath = Path.Combine(torrentPath, files[0]);
        }

        arguments = arguments.Replace("%N", $"\"{torrent.RdName}\"");
        arguments = arguments.Replace("%L", $"\"{torrent.Category}\"");
        arguments = arguments.Replace("%F", $"\"{filePath}\"");
        arguments = arguments.Replace("%R", $"\"{downloadPath}\"");
        arguments = arguments.Replace("%D", $"\"{torrentPath}\"");
        arguments = arguments.Replace("%C", downloadsForTorrent.Count.ToString(CultureInfo.InvariantCulture).Replace(",", "").Replace(".", ""));
        arguments = arguments.Replace("%Z", torrent.RdSize?.ToString(CultureInfo.InvariantCulture).Replace(",", "").Replace(".", ""));
        arguments = arguments.Replace("%I", torrent.Hash);

        Log($"Executing external program {fileName} with arguments {arguments}", torrent);

        var errorSb = new StringBuilder();
        var outputSb = new StringBuilder();

        using var process = processFactory.NewProcess();

        process.StartInfo.FileName = fileName;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        process.OutputDataReceived += (_, data) =>
        {
            if (data == null)
            {
                return;
            }

            outputSb.AppendLine(data.Trim());
        };

        process.ErrorDataReceived += (_, data) =>
        {
            if (data == null)
            {
                return;
            }

            errorSb.AppendLine(data.Trim());
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = process.WaitForExit(60000 * 10);

        var errors = errorSb.ToString();
        var output = outputSb.ToString();

        if (errors.Length > 0)
        {
            Log($"External application exited with errors: {errors}", torrent);
        }

        if (output.Length > 0)
        {
            Log($"External application exited with output: {output}", torrent);
        }

        if (!exited)
        {
            Log("External application after a 60 second timeout", torrent);
        }
    }

    private async Task UpdateTorrentClientData(Torrent torrent, TorrentClientTorrent? torrentClientTorrent = null)
    {
        try
        {
            var originalTorrent = JsonSerializer.Serialize(torrent, JsonSerializerOptions);

            await TorrentClient.UpdateData(torrent, torrentClientTorrent);

            var newTorrent = JsonSerializer.Serialize(torrent, JsonSerializerOptions);

            if (originalTorrent != newTorrent)
            {
                await torrentData.UpdateRdData(torrent);
            }
        }
        catch
        {
            // ignored
        }
    }

    private void Log(String message, Download? download, Torrent? torrent)
    {
        if (download != null)
        {
            message = $"{message} {download.ToLog()}";
        }

        if (torrent != null)
        {
            message = $"{message} {torrent.ToLog()}";
        }

        logger.LogDebug(message);
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