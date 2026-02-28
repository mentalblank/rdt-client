using System.Globalization;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using RdtClient.Data.Data;
using RdtClient.Data.Enums;
using RdtClient.Data.Models.Data;
using RdtClient.Data.Models.DebridClient;
using RdtClient.Data.Models.Internal;
using RdtClient.Service.BackgroundServices;
using RdtClient.Service.Helpers;
using RdtClient.Service.Services.DebridClients;
using RdtClient.Service.Services.Usenet;
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
    AllDebridDebridClient allDebridDebridClient,
    PremiumizeDebridClient premiumizeDebridClient,
    RealDebridDebridClient realDebridDebridClient,
    DebridLinkClient debridLinkClient,
    TorBoxDebridClient torBoxDebridClient,
    UsenetQueueManager usenetQueueManager)
{
    private static readonly SemaphoreSlim RealDebridUpdateLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    private static readonly SemaphoreSlim TorrentResetLock = new(1, 1);

    private IDebridClient GetDebridClient(Provider? provider)
    {
        return provider switch
        {
            Provider.Premiumize => premiumizeDebridClient,
            Provider.RealDebrid => realDebridDebridClient,
            Provider.AllDebrid => allDebridDebridClient,
            Provider.DebridLink => debridLinkClient,
            Provider.TorBox => torBoxDebridClient,
            _ => throw new($"Invalid Provider {provider}")
        };
    }

    private List<Provider> GetDefaultProviders()
    {
        var providers = new List<Provider>();
        
        var fallbackProvider = Settings.Get.Provider.FallbackProvider;
        var fallbackEnabled = fallbackProvider switch
        {
            Provider.RealDebrid => Settings.Get.Provider.RealDebridEnabled,
            Provider.AllDebrid => Settings.Get.Provider.AllDebridEnabled,
            Provider.Premiumize => Settings.Get.Provider.PremiumizeEnabled,
            Provider.DebridLink => Settings.Get.Provider.DebridLinkEnabled,
            Provider.TorBox => Settings.Get.Provider.TorBoxEnabled,
            _ => false
        };

        if (fallbackEnabled)
        {
            providers.Add(fallbackProvider);
            return providers;
        }

        if (Settings.Get.Provider.RealDebridEnabled) providers.Add(Provider.RealDebrid);
        if (Settings.Get.Provider.AllDebridEnabled) providers.Add(Provider.AllDebrid);
        if (Settings.Get.Provider.PremiumizeEnabled) providers.Add(Provider.Premiumize);
        if (Settings.Get.Provider.DebridLinkEnabled) providers.Add(Provider.DebridLink);
        if (Settings.Get.Provider.TorBoxEnabled) providers.Add(Provider.TorBox);

        if (providers.Count == 0)
        {
            if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.RealDebridApiKey)) providers.Add(Provider.RealDebrid);
            else if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.AllDebridApiKey)) providers.Add(Provider.AllDebrid);
            else if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.PremiumizeApiKey)) providers.Add(Provider.Premiumize);
            else if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.DebridLinkApiKey)) providers.Add(Provider.DebridLink);
            else if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.TorBoxApiKey)) providers.Add(Provider.TorBox);
        }

        if (providers.Count == 0)
        {
            providers.Add(fallbackProvider);
        }

        return providers;
    }

    private List<Provider> GetProvidersForCategory(String? category)
    {
        var providers = new List<Provider>();
        var mapping = Settings.Get.Provider.CategoryMapping;

        if (String.IsNullOrWhiteSpace(mapping))
        {
            return GetDefaultProviders();
        }

        var mappings = mapping.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var categories = (category ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim().ToLower()).ToList();

        if (categories.Count == 0)
        {
            categories.Add("");
        }

        foreach (var cat in categories)
        {
            var matched = false;
            foreach (var m in mappings)
            {
                var parts = m.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;

                var mappingCat = parts[0].Trim().ToLower();
                var mappingProviders = parts[1].Trim();

                if (mappingCat == cat || (mappingCat == "*" && !matched))
                {
                    var providerNames = mappingProviders.Split('+', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var providerName in providerNames)
                    {
                        if (Enum.TryParse<Provider>(providerName.Trim(), true, out var provider))
                        {
                            providers.Add(provider);
                            matched = true;
                        }
                    }
                }
            }
        }

        if (providers.Count == 0)
        {
            return GetDefaultProviders();
        }

        return providers.Distinct().ToList();
    }

    public virtual (Int64 Speed, Int64 BytesTotal, Int64 BytesDone) GetDownloadStats(Guid downloadId)
    {
        return TorrentRunner.GetStats(downloadId);
    }

    public virtual async Task<IList<Torrent>> Get()
    {
        var torrents = await torrentData.Get();

        return torrents;
    }

    public virtual async Task<Torrent?> GetByHash(String hash, Provider? clientKind = null)
    {
        clientKind ??= (Provider?)GetDefaultProviders().FirstOrDefault() ?? Provider.RealDebrid;

        var torrent = await torrentData.GetByHash(hash, clientKind.Value);

        if (torrent != null)
        {
            await UpdateTorrentClientData(torrent);
        }

        return torrent;
    }

    public async Task UpdateCategory(String hash, String? category)
    {
        var providers = GetProvidersForCategory(category);
        
        foreach (var provider in providers)
        {
            var torrent = await torrentData.GetByHash(hash, provider);

            if (torrent == null)
            {
                continue;
            }

            Log($"Update category to {category}", torrent);

            await torrentData.UpdateCategory(torrent.TorrentId, category);
        }
    }

    public virtual async Task<IList<Torrent>> AddNzbLinkToDebridQueue(String nzbLink, Torrent torrent)
    {
        throw new NotSupportedException("NZB Links are not supported in the internal Usenet engine. Please upload the .nzb file instead.");
    }

    public virtual async Task<IList<Torrent>> AddNzbFileToDebridQueue(Byte[] bytes, String? fileName, Torrent torrent)
    {
        var hash = await usenetQueueManager.AddNzbFile(bytes, fileName ?? "unknown.nzb", torrent.Category, torrent.Priority ?? 0);
        
        // Return a dummy torrent list to satisfy the UI, although the job is actually in UsenetJobs table
        return new List<Torrent>
        {
            new Torrent
            {
                Hash = hash,
                RdName = fileName,
                RdStatus = TorrentStatus.Processing,
                Type = DownloadType.Nzb
            }
        };
    }

    public virtual async Task<IList<Torrent>> AddMagnetToDebridQueue(String magnetLink, Torrent torrent)
    {
        var enriched = await enricher.EnrichMagnetLink(magnetLink);
        MagnetLink magnet;

        try
        {
            magnet = MagnetLink.Parse(magnetLink);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{ex.Message}, trying to parse {magnetLink}", ex.Message, magnetLink);

            throw new($"{ex.Message}, trying to parse {magnetLink}");
        }

        if (!String.IsNullOrWhiteSpace(Settings.Get.General.BannedTrackers))
        {
            var bannedTrackers = Settings.Get.General.BannedTrackers.Split(',');

            foreach (var bannedTracker in bannedTrackers)
            {
                var bannedTrackerCompare = bannedTracker.Trim().ToLower();

                if (String.IsNullOrWhiteSpace(bannedTrackerCompare))
                {
                    continue;
                }

                if (magnet.AnnounceUrls != null)
                {
                    var bannedUrls = magnet.AnnounceUrls.Where(m => m.Trim().ToLower().Contains(bannedTrackerCompare)).ToList();

                    if (bannedUrls.Count > 0)
                    {
                        var bannedUrlsString = String.Join(", ", bannedUrls);

                        throw new($"Cannot add torrent, the torrent contains banned trackers: {bannedUrlsString}.");
                    }
                }
            }
        }

        torrent.RdStatus = TorrentStatus.Queued;
        torrent.RdName = magnet.Name;

        var hash = magnet.InfoHashes.V1OrV2.ToHex();
        var providers = GetProvidersForCategory(torrent.Category);
        var results = new List<Torrent>();

        foreach (var provider in providers)
        {
            var newTorrent = await AddQueued(hash, enriched, false, DownloadType.Torrent, torrent, provider);
            Log($"Adding {hash} (magnet link) to queue for {provider}", newTorrent);
            await CopyAddedTorrent(newTorrent);
            results.Add(newTorrent);
        }

        return results;
    }

    public virtual async Task<IList<Torrent>> AddFileToDebridQueue(Byte[] bytes, Torrent torrent)
    {
        var enriched = await enricher.EnrichTorrentBytes(bytes);

        String fileAsBase64;

        MonoTorrent.Torrent monoTorrent;

        if (enriched.SequenceEqual(bytes))
        {
            fileAsBase64 = Convert.ToBase64String(bytes);
            logger.LogDebug($"bytes {bytes}");
        }
        else
        {
            fileAsBase64 = Convert.ToBase64String(enriched);
            logger.LogDebug($"enriched bytes {enriched}");
        }

        try
        {
            monoTorrent = await MonoTorrent.Torrent.LoadAsync(bytes);
        }
        catch (Exception ex)
        {
            throw new($"{ex.Message}, trying to parse {fileAsBase64}");
        }

        if (!String.IsNullOrWhiteSpace(Settings.Get.General.BannedTrackers))
        {
            var bannedTrackers = Settings.Get.General.BannedTrackers.Split(',');

            foreach (var bannedTracker in bannedTrackers)
            {
                var bannedTrackerCompare = bannedTracker.Trim().ToLower();

                if (String.IsNullOrWhiteSpace(bannedTrackerCompare))
                {
                    continue;
                }

                if (!String.IsNullOrWhiteSpace(monoTorrent.Source) && monoTorrent.Source.Contains(bannedTracker))
                {
                    throw new($"Cannot add torrent, the torrent source '{monoTorrent.Source}' is a banned tracker.");
                }

                if (monoTorrent.AnnounceUrls != null)
                {
                    var bannedUrls = monoTorrent.AnnounceUrls.SelectMany(m => m).Where(m => m.Trim().ToLower().Contains(bannedTrackerCompare)).ToList();

                    if (bannedUrls.Count > 0)
                    {
                        var bannedUrlsString = String.Join(", ", bannedUrls);

                        throw new($"Cannot add torrent, the torrent contains banned trackers: {bannedUrlsString}.");
                    }
                }
            }
        }

        torrent.RdStatus = TorrentStatus.Queued;
        torrent.RdName = monoTorrent.Name;

        var hash = monoTorrent.InfoHashes.V1OrV2.ToHex();
        var providers = GetProvidersForCategory(torrent.Category);
        var results = new List<Torrent>();

        foreach (var provider in providers)
        {
            var newTorrent = await AddQueued(hash, fileAsBase64, true, DownloadType.Torrent, torrent, provider);
            Log($"Adding {hash} (torrent file) to queue for {provider}", newTorrent);
            await CopyAddedTorrent(newTorrent);
            results.Add(newTorrent);
        }

        return results;
    }

    private async Task CopyAddedTorrent(Torrent torrent)
    {
        if (torrent.Type == DownloadType.Nzb || String.IsNullOrWhiteSpace(Settings.Get.General.CopyAddedTorrents) || String.IsNullOrWhiteSpace(torrent.FileOrMagnet) || String.IsNullOrWhiteSpace(torrent.RdName))
        {
            return;
        }

        try
        {
            if (!fileSystem.Directory.Exists(Settings.Get.General.CopyAddedTorrents))
            {
                fileSystem.Directory.CreateDirectory(Settings.Get.General.CopyAddedTorrents);
            }

            var extension = torrent.Type switch
            {
                DownloadType.Nzb => ".nzb",
                DownloadType.Torrent => torrent.IsFile ? ".torrent" : ".magnet",
                _ => throw new ArgumentException("Unexpected DownloadType")
            };

            var copyFileName = Path.Combine(Settings.Get.General.CopyAddedTorrents, FileHelper.RemoveInvalidFileNameChars(torrent.RdName));

            if (!copyFileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                copyFileName += extension;
            }

            if (fileSystem.File.Exists(copyFileName))
            {
                fileSystem.File.Delete(copyFileName);
            }

            if (torrent.IsFile)
            {
                var bytes = Convert.FromBase64String(torrent.FileOrMagnet);
                await fileSystem.File.WriteAllBytesAsync(copyFileName, bytes);
            }
            else
            {
                await fileSystem.File.WriteAllTextAsync(copyFileName, torrent.FileOrMagnet);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Unable to create torrent blackhole directory: {Settings.Get.General.CopyAddedTorrents}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Adds torrent in database to debrid provider and updates database accordingly.
    /// </summary>
    /// <param name="torrent">The torrent from the database to upload to the debrid provider</param>
    /// <returns>Updated torrent</returns>
    /// <exception cref="Exception">When RdId is not null or FileOrMagnet is null.</exception>
    public async Task DequeueFromDebridQueue(Torrent torrent)
    {
        if (torrent.RdId != null)
        {
            throw new("Torrent already added to debrid provider, cannot dequeue");
        }

        if (torrent.FileOrMagnet == null)
        {
            throw new("Torrent has no torrent file or magnet link");
        }

        logger.LogDebug("Adding {hash} to debrid provider {torrentInfo}", torrent.Hash, torrent.ToLog());

        await RealDebridUpdateLock.WaitAsync();

        try
        {
            String id;

            if (torrent.Type == DownloadType.Nzb)
            {
                if (torrent.IsFile)
                {
                    id = await usenetQueueManager.AddNzbFile(Convert.FromBase64String(torrent.FileOrMagnet!), torrent.RdName ?? "unknown.nzb", torrent.Category, torrent.Priority ?? 0);
                }
                else
                {
                    throw new NotSupportedException("NZB Links are not supported in the internal Usenet engine yet.");
                }
            }
            else
            {
                id = torrent.IsFile
                    ? await GetDebridClient(torrent.ClientKind).AddTorrentFile(Convert.FromBase64String(torrent.FileOrMagnet!))
                    : await GetDebridClient(torrent.ClientKind).AddTorrentMagnet(torrent.FileOrMagnet!);
            }

            await torrentData.UpdateRdId(torrent, id);

            await UpdateTorrentClientData(torrent);
        }
        finally
        {
            RealDebridUpdateLock.Release();
        }
    }

    public async Task<IList<DebridClientAvailableFile>> GetAvailableFiles(String hash)
    {
        var provider = (Provider?)GetDefaultProviders().FirstOrDefault() ?? Provider.RealDebrid;
        var result = await GetDebridClient(provider).GetAvailableFiles(hash);

        return result;
    }

    public async Task SelectFiles(Guid torrentId)
    {
        var torrent = await GetById(torrentId);

        if (torrent == null)
        {
            return;
        }

        var selected = await GetDebridClient(torrent.ClientKind).SelectFiles(torrent);

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

        var downloadInfos = await GetDebridClient(torrent.ClientKind).GetDownloadInfos(torrent);

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
    ///     Logs a message to the console, sets the error on the torrent and ensures it is not retried.
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

    public virtual async Task Delete(Guid torrentId, Boolean deleteData, Boolean deleteRdTorrent, Boolean deleteLocalFiles)
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
                await GetDebridClient(torrent.ClientKind).Delete(torrent);
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
        var download = await downloads.GetById(downloadId) ?? throw new($"Download with ID {downloadId} not found");

        Log("Unrestricting link", download, download.Torrent);

        var unrestrictedLink = await GetDebridClient(download.Torrent!.ClientKind).Unrestrict(download.Torrent!, download.Path);

        await downloads.UpdateUnrestrictedLink(downloadId, unrestrictedLink);

        return unrestrictedLink;
    }

    /// <inheritdoc />
    public async Task<String> RetrieveFileName(Guid downloadId)
    {
        var download = await downloads.GetById(downloadId) ?? throw new($"Download with ID {downloadId} not found");

        Log($"Retrieving filename for", download, download.Torrent!);

        var fileName = await GetDebridClient(download.Torrent!.ClientKind).GetFileName(download);

        await downloads.UpdateFileName(downloadId, fileName);

        return fileName;
    }

    public async Task<Profile> GetProfile()
    {
        Provider? provider = null;
        
        if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.RealDebridApiKey)) provider = Provider.RealDebrid;
        else if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.AllDebridApiKey)) provider = Provider.AllDebrid;
        else if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.PremiumizeApiKey)) provider = Provider.Premiumize;
        else if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.DebridLinkApiKey)) provider = Provider.DebridLink;
        else if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.TorBoxApiKey)) provider = Provider.TorBox;

        if (provider == null)
        {
            return new Profile
            {
                Provider = "None",
                UserName = "Internal",
                Expiration = null,
                CurrentVersion = UpdateChecker.CurrentVersion,
                LatestVersion = UpdateChecker.LatestVersion,
                IsInsecure = UpdateChecker.IsInsecure,
                DisableUpdateNotification = Settings.Get.General.DisableUpdateNotifications
            };
        }

        var user = await GetDebridClient(provider.Value).GetUser();

        var profile = new Profile
        {
            Provider = Enum.GetName(provider.Value),
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

        try
        {
            var torrents = await Get();
            torrents = torrents.Where(m => m.Type != DownloadType.Nzb).ToList();

            var providers = new List<Provider>();
            if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.RealDebridApiKey)) providers.Add(Provider.RealDebrid);
            if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.AllDebridApiKey)) providers.Add(Provider.AllDebrid);
            if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.PremiumizeApiKey)) providers.Add(Provider.Premiumize);
            if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.DebridLinkApiKey)) providers.Add(Provider.DebridLink);
            if (!String.IsNullOrWhiteSpace(Settings.Get.Provider.TorBoxApiKey)) providers.Add(Provider.TorBox);

            foreach (var provider in providers.Distinct())
            {
                try
                {
                    var debridClient = GetDebridClient(provider);
                    var rdTorrents = await debridClient.GetDownloads();

                    foreach (var rdTorrent in rdTorrents)
                    {
                        var torrent = torrents.FirstOrDefault(m => m.RdId == rdTorrent.Id && m.ClientKind == provider);

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
                                ClientKind = provider
                            };

                            if (newTorrent.RdStatus == TorrentStatus.WaitingForFileSelection)
                            {
                                continue;
                            }

                            torrent = await torrentData.Add(rdTorrent.Id, rdTorrent.Hash, null, false, DownloadType.Torrent, Settings.Get.DownloadClient.Client, newTorrent, provider);

                            await UpdateTorrentClientData(torrent, rdTorrent);
                        }
                        else if (torrent != null)
                        {
                            await UpdateTorrentClientData(torrent, rdTorrent);
                        }
                    }

                    foreach (var torrent in torrents.Where(m => m.ClientKind == provider))
                    {
                        var rdTorrent = rdTorrents.FirstOrDefault(m => m.Id == torrent.RdId);

                        if (rdTorrent == null && Settings.Get.Provider.AutoDelete && torrent.RdStatus != TorrentStatus.Queued)
                        {
                            await Delete(torrent.TorrentId, true, false, true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Failed to update data for provider {provider}: {ex.Message}");
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
                throw new($"Cannot re-add this torrent, original magnet or file not found");
            }

            IList<Torrent> newTorrents;

            if (torrent.Type == DownloadType.Nzb)
            {
                if (torrent.IsFile)
                {
                    var bytes = Convert.FromBase64String(torrent.FileOrMagnet!);

                    newTorrents = await AddNzbFileToDebridQueue(bytes, torrent.RdName, torrent);
                }
                else
                {
                    newTorrents = await AddNzbLinkToDebridQueue(torrent.FileOrMagnet!, torrent);
                }
            }
            else
            {
                if (torrent.IsFile)
                {
                    var bytes = Convert.FromBase64String(torrent.FileOrMagnet!);

                    newTorrents = await AddFileToDebridQueue(bytes, torrent);
                }
                else
                {
                    newTorrents = await AddMagnetToDebridQueue(torrent.FileOrMagnet!, torrent);
                }
            }

            foreach (var newTorrent in newTorrents)
            {
                await torrentData.UpdateRetry(newTorrent.TorrentId, null, retryCount);
            }
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
        var providers = GetProvidersForCategory(null);
        
        foreach (var provider in providers)
        {
            var torrent = await torrentData.GetByHash(hash, provider);

            if (torrent == null)
            {
                continue;
            }

            await torrentData.UpdatePriority(torrent.TorrentId, priority);
        }
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

        return torrent;
    }

    private static String DownloadPath(Torrent torrent)
    {
        var settingDownloadPath = Settings.GetDownloadPath(torrent.ClientKind);

        if (!String.IsNullOrWhiteSpace(torrent.Category))
        {
            settingDownloadPath = Path.Combine(settingDownloadPath, torrent.Category);
        }

        return settingDownloadPath;
    }

    private async Task<Torrent> AddQueued(String infoHash,
                                          String fileOrMagnetContents,
                                          Boolean isFile,
                                          DownloadType downloadType,
                                          Torrent torrent,
                                          Provider? clientKind)
    {
        var existingTorrent = await torrentData.GetByHash(infoHash, clientKind);

        if (existingTorrent != null)
        {
            return existingTorrent;
        }

        var newTorrent = await torrentData.Add(null,
                                               infoHash,
                                               fileOrMagnetContents,
                                               isFile,
                                               downloadType,
                                               torrent.DownloadClient,
                                               torrent,
                                               clientKind);

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

        var torrent = await torrentData.GetById(torrentId) ?? throw new($"Cannot find Torrent with ID {torrentId}");

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

    private async Task UpdateTorrentClientData(Torrent torrent, DebridClientTorrent? torrentClientTorrent = null)
    {
        try
        {
            var originalTorrent = JsonSerializer.Serialize(torrent, JsonSerializerOptions);

            await GetDebridClient(torrent.ClientKind).UpdateData(torrent, torrentClientTorrent);

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

    private static String ComputeSha1Hash(String input)
    {
        using var sha1 = SHA1.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = sha1.ComputeHash(bytes);

        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private static String ComputeMd5Hash(String input)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = md5.ComputeHash(bytes);

        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private static String ComputeMd5HashFromBytes(Byte[] bytes)
    {
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(bytes);

        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}
