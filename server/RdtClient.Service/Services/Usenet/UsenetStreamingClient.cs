using Microsoft.Extensions.Logging;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.Connections;
using RdtClient.Service.Services.Usenet.Models;
using UsenetSharp.Models;

namespace RdtClient.Service.Services.Usenet;

public class UsenetStreamingClient : WrappingNntpClient
{
    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<MultiConnectionNntpClient> _providers = [];
    private Boolean _providerInitialized;
    private readonly Object _initLock = new();

    public UsenetStreamingClient(ILoggerFactory loggerFactory)
        : base(CreateDownloadingNntpClient(loggerFactory, out var providers))
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<UsenetStreamingClient>();
        _providers = providers;
    }

    private static DownloadingNntpClient CreateDownloadingNntpClient(ILoggerFactory loggerFactory, out List<MultiConnectionNntpClient> providers)
    {
        var multiProviderClient = CreateMultiProviderClient(loggerFactory, out providers);
        return new DownloadingNntpClient(multiProviderClient);
    }

    private static MultiProviderNntpClient CreateMultiProviderClient(ILoggerFactory loggerFactory, out List<MultiConnectionNntpClient> providers)
    {
        providers = [];
        return new MultiProviderNntpClient(providers, loggerFactory.CreateLogger<MultiProviderNntpClient>());
    }

    private void EnsureProvider()
    {
        if (_providerInitialized) return;

        lock (_initLock)
        {
            if (_providerInitialized) return;

            var settings = Settings.Get.Usenet;
            if (settings.Enabled && !String.IsNullOrWhiteSpace(settings.Host))
            {
                AddProvider(new UsenetProvider
                {
                    Host = settings.Host,
                    Port = settings.Port,
                    UseSsl = settings.UseSsl,
                    Username = settings.Username,
                    Password = settings.Password,
                    MaxConnections = settings.MaxDownloadConnections,
                    Priority = 0,
                    Enabled = true
                });
                _providerInitialized = true;
                _logger.LogInformation($"Usenet provider {settings.Host} initialized automatically.");
            }
        }
    }

    public void AddProvider(UsenetProvider provider)
    {
        lock (_providers)
        {
            if (_providers.Any(p => p.Host == provider.Host && p.Port == provider.Port && p.Username == provider.Username))
            {
                return;
            }

            _logger.LogInformation($"Adding Usenet provider {provider.Host}:{provider.Port}");
            
            var connectionPool = new ConnectionPool<INntpClient>(
                provider.MaxConnections > 0 ? provider.MaxConnections : 10,
                async (ct) => await CreateNewConnection(provider, ct)
            );

            var logger = _loggerFactory.CreateLogger<MultiConnectionNntpClient>();
            var multiConnectionClient = new MultiConnectionNntpClient(connectionPool, true, provider.Host, provider.Port, provider.Username, logger);
            _providers.Add(multiConnectionClient);
        }
    }

    public static async ValueTask<INntpClient> CreateNewConnection
    (
        UsenetProvider provider,
        CancellationToken ct
    )
    {
        var connection = new BaseNntpClient();
        await connection.ConnectAsync(provider.Host, provider.Port, provider.UseSsl, ct).ConfigureAwait(false);
        if (!String.IsNullOrEmpty(provider.Username))
        {
            await connection.AuthenticateAsync(provider.Username, provider.Password ?? "", ct).ConfigureAwait(false);
        }
        return connection;
    }

    public void ClearProviders()
    {
        lock (_providers)
        {
            foreach (var provider in _providers)
            {
                provider.Dispose();
            }
            _providers.Clear();
            _providerInitialized = false;
        }
    }

    public override Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        EnsureProvider();
        return base.StatAsync(segmentId, cancellationToken);
    }

    public override Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        EnsureProvider();
        return base.HeadAsync(segmentId, cancellationToken);
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        EnsureProvider();
        return base.DecodedBodyAsync(segmentId, cancellationToken);
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        EnsureProvider();
        return base.DecodedBodyAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        EnsureProvider();
        return base.DecodedArticleAsync(segmentId, cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken)
    {
        EnsureProvider();
        return base.DecodedArticleAsync(segmentId, onConnectionReadyAgain, cancellationToken);
    }

    public override Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken)
    {
        EnsureProvider();
        return base.DateAsync(cancellationToken);
    }

    public override Task<UsenetYencHeader> GetYencHeadersAsync(String segmentId, CancellationToken ct)
    {
        EnsureProvider();
        return base.GetYencHeadersAsync(segmentId, ct);
    }

    public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(String segmentId, CancellationToken cancellationToken)
    {
        EnsureProvider();
        return base.AcquireExclusiveConnectionAsync(segmentId, cancellationToken);
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, UsenetExclusiveConnection exclusiveConnection, CancellationToken cancellationToken)
    {
        EnsureProvider();
        return base.DecodedBodyAsync(segmentId, exclusiveConnection, cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId, UsenetExclusiveConnection exclusiveConnection, CancellationToken cancellationToken)
    {
        EnsureProvider();
        return base.DecodedArticleAsync(segmentId, exclusiveConnection, cancellationToken);
    }
}
