using RdtClient.Service.Services.Usenet.Models;
using UsenetSharp.Models;

namespace RdtClient.Service.Services.Usenet;

public class WrappingNntpClient(INntpClient usenetClient) : NntpClient
{
    private INntpClient _usenetClient = usenetClient;

    public override Task ConnectAsync(
        String host, Int32 port, Boolean useSsl, CancellationToken cancellationToken) =>
        _usenetClient.ConnectAsync(host, port, useSsl, cancellationToken);

    public override Task<UsenetResponse> AuthenticateAsync(
        String user, String pass, CancellationToken cancellationToken) =>
        _usenetClient.AuthenticateAsync(user, pass, cancellationToken);

    public override Task<UsenetStatResponse> StatAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        _usenetClient.StatAsync(segmentId, cancellationToken);

    public override Task<UsenetHeadResponse> HeadAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        _usenetClient.HeadAsync(segmentId, cancellationToken);

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        _usenetClient.DecodedBodyAsync(segmentId, cancellationToken);

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        _usenetClient.DecodedArticleAsync(segmentId, cancellationToken);

    public override Task<UsenetDateResponse> DateAsync(
        CancellationToken cancellationToken) =>
        _usenetClient.DateAsync(cancellationToken);

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken) =>
        _usenetClient.DecodedBodyAsync(segmentId, onConnectionReadyAgain, cancellationToken);

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken) =>
        _usenetClient.DecodedArticleAsync(segmentId, onConnectionReadyAgain, cancellationToken);

    public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
        String segmentId, CancellationToken cancellationToken) =>
        _usenetClient.AcquireExclusiveConnectionAsync(segmentId, cancellationToken);

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, UsenetExclusiveConnection exclusiveConnection, CancellationToken cancellationToken) =>
        _usenetClient.DecodedBodyAsync(segmentId, exclusiveConnection, cancellationToken);

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, UsenetExclusiveConnection exclusiveConnection, CancellationToken cancellationToken) =>
        _usenetClient.DecodedArticleAsync(segmentId, exclusiveConnection, cancellationToken);

    protected void ReplaceUnderlyingClient(INntpClient usenetClient)
    {
        var old = _usenetClient;
        _usenetClient = usenetClient;
        if (old is IDisposable disposable)
            disposable.Dispose();
    }

    public override void Dispose()
    {
        _usenetClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
