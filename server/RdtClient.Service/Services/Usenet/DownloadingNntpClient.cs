using RdtClient.Service.Services.Usenet.Concurrency;
using RdtClient.Service.Services.Usenet.Contexts;
using RdtClient.Service.Services.Usenet.Extensions;
using RdtClient.Service.Services.Usenet.Models;
using UsenetSharp.Models;

namespace RdtClient.Service.Services.Usenet;

/// <summary>
/// This client is only responsible for limiting download operations (BODY/ARTICLE)
/// to the configured number of maximum download connections.
/// </summary>
public class DownloadingNntpClient : WrappingNntpClient
{
    private readonly PrioritizedSemaphore _semaphore;

    public DownloadingNntpClient(INntpClient usenetClient) : base(usenetClient)
    {
        var settings = Settings.Get.Usenet;
        var maxDownloadConnections = settings.MaxDownloadConnections;
        var highPriorityOdds = settings.ArticleBufferSize; // Using ArticleBufferSize as proxy for streaming priority odds for now or we can add a new setting
        var streamingPriority = new SemaphorePriorityOdds { HighPriorityOdds = 80 }; 
        
        _semaphore = new PrioritizedSemaphore(maxDownloadConnections, maxDownloadConnections, streamingPriority);
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId,
        CancellationToken cancellationToken)
    {
        return DecodedBodyAsync(segmentId, onConnectionReadyAgain: null, cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId,
        CancellationToken cancellationToken)
    {
        return DecodedArticleAsync(segmentId, onConnectionReadyAgain: null, cancellationToken);
    }

    public override async Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        await AcquireExclusiveConnectionAsync(onConnectionReadyAgain, cancellationToken).ConfigureAwait(false);
        return await base.DecodedBodyAsync(segmentId, OnConnectionReadyAgain, cancellationToken).ConfigureAwait(false);

        void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
        {
            _semaphore.Release();
            onConnectionReadyAgain?.Invoke(articleBodyResult);
        }
    }

    public override async Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        await AcquireExclusiveConnectionAsync(onConnectionReadyAgain, cancellationToken).ConfigureAwait(false);
        return await base.DecodedArticleAsync(segmentId, OnConnectionReadyAgain, cancellationToken)
            .ConfigureAwait(false);

        void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
        {
            _semaphore.Release();
            onConnectionReadyAgain?.Invoke(articleBodyResult);
        }
    }

    private async Task AcquireExclusiveConnectionAsync(Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken)
    {
        try
        {
            await AcquireExclusiveConnectionAsync(cancellationToken);
        }
        catch
        {
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);
            throw;
        }
    }

    private Task AcquireExclusiveConnectionAsync(CancellationToken cancellationToken)
    {
        var downloadPriorityContext = cancellationToken.GetContext<DownloadPriorityContext>();
        var semaphorePriority = downloadPriorityContext?.Priority ?? SemaphorePriority.Low;
        return _semaphore.WaitAsync(semaphorePriority, cancellationToken);
    }

    public override async Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync
    (
        String segmentId,
        CancellationToken cancellationToken
    )
    {
        await AcquireExclusiveConnectionAsync(cancellationToken).ConfigureAwait(false);
        return new UsenetExclusiveConnection(_ => _semaphore.Release());
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection, CancellationToken cancellationToken)
    {
        var onConnectionReadyAgain = exclusiveConnection.OnConnectionReadyAgain;
        return base.DecodedBodyAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection, CancellationToken cancellationToken)
    {
        var onConnectionReadyAgain = exclusiveConnection.OnConnectionReadyAgain;
        return base.DecodedArticleAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    public override void Dispose()
    {
        base.Dispose();
        _semaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
