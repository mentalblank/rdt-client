using RdtClient.Service.Services.Usenet.Models;
using UsenetSharp.Models;

namespace RdtClient.Service.Services.Usenet;

public interface INntpClient : IDisposable
{
    // core methods
    Task ConnectAsync(
        String host, Int32 port, Boolean useSsl, CancellationToken cancellationToken);

    Task<UsenetResponse> AuthenticateAsync(
        String user, String pass, CancellationToken cancellationToken);

    Task<UsenetStatResponse> StatAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    Task<UsenetHeadResponse> HeadAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken);

    Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken);

    Task<UsenetDateResponse> DateAsync(
        CancellationToken cancellationToken);

    // optimized for concurrency
    Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
        String segmentId, CancellationToken cancellationToken);

    Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, UsenetExclusiveConnection connection, CancellationToken cancellationToken);

    Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, UsenetExclusiveConnection connection, CancellationToken cancellationToken);

    // helpers
    Task<UsenetYencHeader> GetYencHeadersAsync(
        String segmentId, CancellationToken ct);

    Task CheckAllSegmentsAsync(
        IEnumerable<String> segmentIds, Int32 concurrency, IProgress<Int32>? progress, CancellationToken cancellationToken);
}
