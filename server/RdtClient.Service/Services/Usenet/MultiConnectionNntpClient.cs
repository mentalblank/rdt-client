using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RdtClient.Service.Services.Usenet.Concurrency;
using RdtClient.Service.Services.Usenet.Connections;
using RdtClient.Service.Services.Usenet.Exceptions;
using RdtClient.Service.Services.Usenet.Models;
using UsenetSharp.Models;

namespace RdtClient.Service.Services.Usenet;

/// <summary>
/// This client is responsible for delegating NNTP commands to a connection pool.
/// </summary>
[SuppressMessage("ReSharper", "AccessToDisposedClosure")]
public class MultiConnectionNntpClient : NntpClient
{
    private readonly ConnectionPool<INntpClient> _connectionPool;
    private readonly ILogger _logger;
    public Boolean IsPooled { get; }
    public String Host { get; }
    public Int32 Port { get; }
    public String? Username { get; }

    public Int32 LiveConnections => _connectionPool.LiveConnections;
    public Int32 IdleConnections => _connectionPool.IdleConnections;
    public Int32 ActiveConnections => _connectionPool.ActiveConnections;
    public Int32 AvailableConnections => _connectionPool.AvailableConnections;

    public MultiConnectionNntpClient(ConnectionPool<INntpClient> connectionPool, Boolean isPooled, String host, Int32 port, String? username, ILogger logger)
    {
        _connectionPool = connectionPool;
        IsPooled = isPooled;
        Host = host;
        Port = port;
        Username = username;
        _logger = logger;
    }

    public override Task ConnectAsync(String host, Int32 port, Boolean useSsl, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please connect within the connectionFactory");
    }

    public override Task<UsenetResponse> AuthenticateAsync(String user, String pass,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please authenticate within the connectionFactory");
    }

    public override Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken ct)
    {
        return RunWithConnection(
            "STAT",
            SemaphorePriority.Low,
            (connection, _) => connection.StatAsync(segmentId, ct),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken ct)
    {
        return RunWithConnection(
            "HEAD",
            SemaphorePriority.Low,
            (connection, _) => connection.HeadAsync(segmentId, ct),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, CancellationToken ct)
    {
        return RunWithConnection(
            "BODY",
            SemaphorePriority.High,
            (connection, onDone) => connection.DecodedBodyAsync(segmentId, onDone, ct),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        CancellationToken ct
    )
    {
        return RunWithConnection(
            "ARTICLE",
            SemaphorePriority.High,
            (connection, onDone) => connection.DecodedArticleAsync(segmentId, onDone, ct),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        return RunWithConnection(
            "ARTICLE",
            SemaphorePriority.High,
            (connection, onDone) => connection.DecodedArticleAsync(segmentId, onDone, ct),
            onConnectionReadyAgain,
            ct
        );
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        return RunWithConnection(
            "BODY",
            SemaphorePriority.High,
            (connection, onDone) => connection.DecodedBodyAsync(segmentId, onDone, ct),
            onConnectionReadyAgain,
            ct
        );
    }

    public override Task<UsenetDateResponse> DateAsync(CancellationToken ct)
    {
        return RunWithConnection(
            "DATE",
            SemaphorePriority.Low,
            (connection, _) => connection.DateAsync(ct),
            onConnectionReadyAgain: null,
            ct
        );
    }

    private async Task<T> RunWithConnection<T>
    (
        String name,
        SemaphorePriority priority,
        Func<INntpClient, Action<ArticleBodyResult>, Task<T>> command,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct,
        Int32 retryCount = 1
    ) where T : UsenetResponse
    {
        while (retryCount >= 0)
        {
            ConnectionLock<INntpClient>? connectionLock = null;
            try
            {
                connectionLock = await _connectionPool.GetConnectionLockAsync(priority, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is TaskCanceledException || e is OperationCanceledException)
            {
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e)
            {
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                if (retryCount > 0)
                {
                    _logger.LogDebug(e, "Error getting connection-lock. Retrying with a new connection.");
                    retryCount--;
                    continue;
                }

                _logger.LogWarning(e, "Error getting connection-lock.");
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }

            T? result;
            try
            {
                result = await command(connectionLock.Connection, OnConnectionReadyAgain).ConfigureAwait(false);
            }
            catch (Exception e) when (e is TaskCanceledException || e is OperationCanceledException)
            {
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e) when (IsCausedBy<UsenetArticleNotFoundException>(e))
            {
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e)
            {
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                if (retryCount > 0)
                {
                    _logger.LogDebug(e, $"Error executing nntp {name} command. Retrying with a new connection.");
                    retryCount--;
                    continue;
                }

                _logger.LogWarning(e, $"Error executing nntp {name} command.");
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }

            if (name is "STAT" or "HEAD" or "DATE")
            {
                LogException(() => connectionLock?.Dispose());
            }
            else if ((result?.Success ?? false) == false)
            {
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
            }

            return result!;

            void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
            {
                if (articleBodyResult != ArticleBodyResult.Retrieved) return;

                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(articleBodyResult));
            }
        }

        _logger.LogError("Unreachable code reached");
        throw new InvalidOperationException("Unreachable code ");
    }

    private static Boolean IsCausedBy<T>(Exception? ex) where T : Exception
    {
        while (ex != null)
        {
            if (ex is T) return true;
            ex = ex.InnerException;
        }
        return false;
    }

    private void LogException(Action? action)
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unhandled exception");
        }
    }

    public override void Dispose()
    {
        _connectionPool.Dispose();
        GC.SuppressFinalize(this);
    }
}
