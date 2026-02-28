using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using RdtClient.Service.Services.Usenet.Models;
using RdtClient.Service.Services.Usenet.Streams;
using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace RdtClient.Service.Services.Usenet;

/// <summary>
/// This client is responsible for caching Article/Body commands to disk.
/// It uses a stable directory to persist across application restarts.
/// </summary>
public class ArticleCachingNntpClient(
    INntpClient usenetClient,
    Boolean leaveOpen = true
) : WrappingNntpClient(usenetClient)
{
    private static readonly String CacheDir = Path.Combine("/data", "rdt-usenet-cache");
    private readonly ConcurrentDictionary<String, SemaphoreSlim> _pendingRequests = new();
    private readonly ConcurrentDictionary<String, CacheEntry> _cachedSegments = new();

    static ArticleCachingNntpClient()
    {
        if (!Directory.Exists(CacheDir))
        {
            Directory.CreateDirectory(CacheDir);
        }
    }

    private record CacheEntry(
        UsenetYencHeader YencHeaders,
        Boolean HasArticleHeaders,
        UsenetArticleHeader? ArticleHeaders);

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, CancellationToken cancellationToken)
    {
        return DecodedBodyAsync(segmentId, onConnectionReadyAgain: null, cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, CancellationToken cancellationToken)
    {
        return DecodedArticleAsync(segmentId, onConnectionReadyAgain: null, cancellationToken);
    }

    public override async Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        var semaphore = _pendingRequests.GetOrAdd(segmentId, _ => new SemaphoreSlim(1, 1));

        try
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);
            throw;
        }

        try
        {
            if (_cachedSegments.TryGetValue(segmentId, out var existingEntry))
            {
                onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
                return ReadCachedBodyAsync(segmentId, existingEntry.YencHeaders);
            }

            // Check disk even if not in memory map
            var cachePath = GetCachePath(segmentId);
            if (File.Exists(cachePath))
            {
                // We need headers to properly read it. For now, if it's on disk but not in memory, 
                // we'll just re-download to be safe or implement header persistence later.
                // However, most streaming happens in one session.
            }

            var response = await base.DecodedBodyAsync(segmentId, onConnectionReadyAgain, cancellationToken)
                .ConfigureAwait(false);

            await using var stream = response.Stream;

            var yencHeaders = await stream.GetYencHeadersAsync(cancellationToken).ConfigureAwait(false);
            if (yencHeaders == null)
            {
                throw new InvalidOperationException($"Failed to read yenc headers for segment {segmentId}");
            }

            await CacheDecodedStreamAsync(segmentId, stream, cancellationToken).ConfigureAwait(false);

            _cachedSegments.TryAdd(segmentId, new CacheEntry(
                YencHeaders: yencHeaders,
                HasArticleHeaders: false,
                ArticleHeaders: null));

            return ReadCachedBodyAsync(segmentId, yencHeaders);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public override async Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        var semaphore = _pendingRequests.GetOrAdd(segmentId, _ => new SemaphoreSlim(1, 1));

        try
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);
            throw;
        }

        try
        {
            if (_cachedSegments.TryGetValue(segmentId, out var cacheEntry))
            {
                if (cacheEntry.HasArticleHeaders)
                {
                    onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
                    return ReadCachedArticleAsync(segmentId, cacheEntry.YencHeaders, cacheEntry.ArticleHeaders!);
                }
                else
                {
                    UsenetHeadResponse? headResponse = null;
                    try
                    {
                        headResponse = await base.HeadAsync(segmentId, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
                    }

                    var updatedEntry = new CacheEntry(
                        YencHeaders: cacheEntry.YencHeaders,
                        HasArticleHeaders: true,
                        ArticleHeaders: headResponse.ArticleHeaders);

                    _cachedSegments.TryUpdate(segmentId, updatedEntry, cacheEntry);

                    return ReadCachedArticleAsync(segmentId, cacheEntry.YencHeaders, headResponse.ArticleHeaders!);
                }
            }

            var response = await base.DecodedArticleAsync(segmentId, onConnectionReadyAgain, cancellationToken)
                .ConfigureAwait(false);

            await using var stream = response.Stream;

            var yencHeaders = await stream.GetYencHeadersAsync(cancellationToken).ConfigureAwait(false);
            if (yencHeaders == null)
            {
                throw new InvalidOperationException($"Failed to read yenc headers for segment {segmentId}");
            }

            await CacheDecodedStreamAsync(segmentId, stream, cancellationToken).ConfigureAwait(false);

            _cachedSegments.TryAdd(segmentId, new CacheEntry(
                YencHeaders: yencHeaders,
                HasArticleHeaders: true,
                ArticleHeaders: response.ArticleHeaders));

            return ReadCachedArticleAsync(segmentId, yencHeaders, response.ArticleHeaders);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public override async Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync
    (
        String segmentId,
        CancellationToken cancellationToken
    )
    {
        var semaphore = _pendingRequests.GetOrAdd(segmentId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _cachedSegments.ContainsKey(segmentId)
                ? new UsenetExclusiveConnection(onConnectionReadyAgain: null)
                : await base.AcquireExclusiveConnectionAsync(segmentId, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken
    )
    {
        var onConnectionReadyAgain = exclusiveConnection.OnConnectionReadyAgain;
        return DecodedBodyAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken
    )
    {
        var onConnectionReadyAgain = exclusiveConnection.OnConnectionReadyAgain;
        return DecodedArticleAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    public override Task<UsenetYencHeader> GetYencHeadersAsync(String segmentId, CancellationToken ct)
    {
        return _cachedSegments.TryGetValue(segmentId, out var existingEntry)
            ? Task.FromResult(existingEntry.YencHeaders)
            : base.GetYencHeadersAsync(segmentId, ct);
    }

    private async Task CacheDecodedStreamAsync(String segmentId, YencStream stream, CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath(segmentId);
        await using var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    private UsenetDecodedBodyResponse ReadCachedBodyAsync(String segmentId, UsenetYencHeader yencHeaders)
    {
        var cachePath = GetCachePath(segmentId);
        var fileStream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        return new UsenetDecodedBodyResponse
        {
            SegmentId = segmentId,
            ResponseCode = (Int32)UsenetResponseType.ArticleRetrievedBodyFollows,
            ResponseMessage = "222 - Article retrieved from file cache",
            Stream = new CachedYencStream(yencHeaders, fileStream)
        };
    }

    private UsenetDecodedArticleResponse ReadCachedArticleAsync(
        String segmentId, UsenetYencHeader yencHeaders, UsenetArticleHeader articleHeaders)
    {
        var cachePath = GetCachePath(segmentId);
        var fileStream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        return new UsenetDecodedArticleResponse
        {
            SegmentId = segmentId,
            ResponseCode = (Int32)UsenetResponseType.ArticleRetrievedHeadAndBodyFollow,
            ResponseMessage = "220 - Article retrieved from cache",
            ArticleHeaders = articleHeaders,
            Stream = new CachedYencStream(yencHeaders, fileStream)
        };
    }

    private String GetCachePath(String segmentId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(segmentId));
        var filename = Convert.ToHexString(hash);
        return Path.Combine(CacheDir, filename);
    }

    public override void Dispose()
    {
        if (!leaveOpen)
            base.Dispose();

        foreach (var semaphore in _pendingRequests.Values)
            semaphore.Dispose();

        _pendingRequests.Clear();
        _cachedSegments.Clear();
        
        // We no longer delete the cache directory on disposal to allow persistence
        
        GC.SuppressFinalize(this);
    }
}
