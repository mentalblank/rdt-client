using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace RdtClient.Service.Services;

public class TrackerListGrabber(IHttpClientFactory httpClientFactory, IMemoryCache memoryCache, ILogger<TrackerListGrabber> logger) : ITrackerListGrabber
{
    private const String EnrichmentCacheKey = "TrackerList_Enrichment";
    private const String BannedCacheKey = "TrackerList_Banned";

    private static readonly SemaphoreSlim Semaphore = new(1, 1);

    private Int32? _lastEnrichmentExpirationMinutes;

    public async Task<String[]> GetEnrichmentTrackers()
    {
        var trackerList = Settings.Get.General.TrackerEnrichmentList;
        var expiration = Settings.Get.General.TrackerEnrichmentCacheExpiration;

        return await GetTrackers(trackerList, EnrichmentCacheKey, expiration, true).ConfigureAwait(false);
    }

    public async Task<String[]> GetBannedTrackers()
    {
        var trackerList = Settings.Get.General.BannedTrackers;
        var expiration = Settings.Get.General.TrackerEnrichmentCacheExpiration;

        return await GetTrackers(trackerList, BannedCacheKey, expiration, false).ConfigureAwait(false);
    }

    private async Task<String[]> GetTrackers(String? trackerList, String cacheKey, Int32 expiration, Boolean isEnrichment)
    {
        if (String.IsNullOrWhiteSpace(trackerList))
        {
            return [];
        }

        // Check if it's a URL
        if (Uri.TryCreate(trackerList, UriKind.Absolute, out var trackerUri) &&
            (trackerUri.Scheme == Uri.UriSchemeHttp || trackerUri.Scheme == Uri.UriSchemeHttps))
        {
            var useCache = expiration > 0;

            if (!useCache)
            {
                memoryCache.Remove(cacheKey);
                if (isEnrichment)
                {
                    _lastEnrichmentExpirationMinutes = null;
                }
            }
            else
            {
                if (isEnrichment)
                {
                    if (_lastEnrichmentExpirationMinutes is not null && expiration != _lastEnrichmentExpirationMinutes)
                    {
                        logger.LogDebug("Tracker list cache timeout changed, invalidating cache.");
                        memoryCache.Remove(cacheKey);
                    }

                    _lastEnrichmentExpirationMinutes = expiration;
                }

                if (memoryCache.TryGetValue(cacheKey, out String[]? cachedTrackers) && cachedTrackers is { Length: > 0 })
                {
                    logger.LogDebug("Using cached tracker list for {CacheKey}.", cacheKey);

                    return cachedTrackers;
                }
            }

            await Semaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                if (useCache)
                {
                    if (memoryCache.TryGetValue(cacheKey, out String[]? cachedTrackers) && cachedTrackers is { Length: > 0 })
                    {
                        logger.LogDebug("Using cached tracker list (after lock) for {CacheKey}.", cacheKey);

                        return cachedTrackers;
                    }
                }

                logger.LogDebug("Tracker cache miss or cache disabled for {CacheKey}. Fetching tracker list.", cacheKey);

                var trackers = await FetchAndParseTrackersAsync(trackerUri).ConfigureAwait(false);

                if (useCache)
                {
                    memoryCache.Set(cacheKey,
                                    trackers,
                                    new MemoryCacheEntryOptions
                                    {
                                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(expiration)
                                    });
                }

                return trackers;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to fetch tracker list from {Url}.", trackerList);

                if (isEnrichment)
                {
                    throw new("Unable to fetch tracker list for enrichment.", ex);
                }

                return [];
            }
            finally
            {
                Semaphore.Release();
            }
        }

        // Not a URL, treat as comma-separated list
        return trackerList.Split([
                                     ',', '\r', '\n', ';'
                                 ],
                                 StringSplitOptions.RemoveEmptyEntries)
                          .Select(t => t.Trim())
                          .Where(t => !String.IsNullOrWhiteSpace(t))
                          .ToArray();
    }

    private async Task<String[]> FetchAndParseTrackersAsync(Uri trackerUri)
    {
        logger.LogDebug("Fetching tracker list from URL: {TrackerUrl}", trackerUri);

        var httpClient = httpClientFactory.CreateClient();

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();

        var currentVersion = version != null && version.LastIndexOf('.') > 0
            ? $"v{version[..version.LastIndexOf('.')]}"
            : "";

        httpClient.DefaultRequestHeaders.UserAgent.Add(new("RdtClient", currentVersion));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = cts.Token;
        var response = await httpClient.GetAsync(trackerUri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

        using (response)
        {
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var reader = new StreamReader(contentStream);
            var result = await reader.ReadToEndAsync(token).ConfigureAwait(false);

            String[] trackers;

            try
            {
                var trackerRejectionCount = 0;

                trackers = result
                           .Split([
                                      "\r\n", "\n", ","
                                  ],
                                  StringSplitOptions.RemoveEmptyEntries)
                           .Where(line => !String.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
                           .Select(t => t.EndsWith("/") ? t.TrimEnd('/') : t)
                           .Select(t => t.Trim())
                           .Where(t =>
                           {
                               if (!Uri.TryCreate(t, UriKind.Absolute, out var uri))
                               {
                                   logger.LogDebug("Rejected tracker: {TrackerUrl} - Reason: Invalid format or unsupported scheme.", t);
                                   trackerRejectionCount++;

                                   return false;
                               }

                               var isIpv6 = uri.Host.StartsWith('[') && uri.Host.Contains(']');

                               var valid = ((isIpv6 && uri.Host.All(c => Char.IsLetterOrDigit(c) || c == '.' || c == ':' || c == '[' || c == ']')) ||
                                            (!isIpv6 && uri.Host.All(c => Char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_'))) &&
                                           (uri.Scheme == Uri.UriSchemeHttp ||
                                            uri.Scheme == Uri.UriSchemeHttps ||
                                            uri.Scheme.Equals("udp", StringComparison.OrdinalIgnoreCase) ||
                                            uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase)) &&
                                           !t.Contains("..") &&
                                           !t.Contains("\\") &&
                                           !t.Any(Char.IsControl) &&
                                           uri.Host.Length > 0;

                               if (!valid)
                               {
                                   logger.LogDebug("Enrichment tracker rejected: {TrackerUrl} - Reason: Invalid format or unsupported scheme.", t);
                                   trackerRejectionCount++;
                               }

                               return valid;
                           })
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .ToArray();

                logger.LogInformation("{TrackerRejectionCount} trackers were rejected during enrichment.", trackerRejectionCount);
            }
            catch (Exception ex)

            {
                logger.LogError(ex, "Error parsing tracker list response.");

                throw new InvalidOperationException("Failed to parse tracker list response.", ex);
            }

            return trackers;
        }
    }
}
