using System.Threading.Channels;
using RdtClient.Service.Services.Usenet;
using RdtClient.Service.Services.Usenet.Concurrency;
using RdtClient.Service.Services.Usenet.Contexts;
using RdtClient.Service.Services.Usenet.Models;
using RdtClient.Service.Services.Usenet.Exceptions;

namespace RdtClient.Service.Services.Usenet.Streams;

public class MultiSegmentStream : FastReadOnlyNonSeekableStream
{
    private readonly Memory<String> _segmentIds;
    private readonly INntpClient _usenetClient;
    private readonly Channel<Task<Stream>> _streamTasks;
    private readonly ContextualCancellationTokenSource _cts;
    private Stream? _stream;
    private Boolean _disposed;

    public static Stream Create
    (
        Memory<String> segmentIds,
        INntpClient usenetClient,
        Int32 articleBufferSize,
        CancellationToken cancellationToken
    )
    {
        return articleBufferSize == 0
            ? new UnbufferedMultiSegmentStream(segmentIds, usenetClient)
            : new MultiSegmentStream(segmentIds, usenetClient, articleBufferSize, cancellationToken);
    }

    private MultiSegmentStream
    (
        Memory<String> segmentIds,
        INntpClient usenetClient,
        Int32 articleBufferSize,
        CancellationToken cancellationToken
    )
    {
        _segmentIds = segmentIds;
        _usenetClient = usenetClient;
        _streamTasks = Channel.CreateBounded<Task<Stream>>(articleBufferSize);
        _cts = ContextualCancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = DownloadSegments(_cts.Token);
    }

    private async Task DownloadSegments(CancellationToken cancellationToken)
    {
        try
        {
            for (var i = 0; i < _segmentIds.Length; i++)
            {
                var segmentId = _segmentIds.Span[i];

                await _streamTasks.Writer.WaitToWriteAsync(cancellationToken);
                
                try 
                {
                    var connection = await _usenetClient.AcquireExclusiveConnectionAsync(segmentId, cancellationToken);
                    var streamTask = DownloadSegment(segmentId, connection, cancellationToken);
                    if (_streamTasks.Writer.TryWrite(streamTask)) continue;

                    // if we never get a chance to write the stream to the writer
                    // then make sure the stream gets disposed.
                    _ = Task.Run(async () => await (await streamTask).DisposeAsync(), CancellationToken.None);
                }
                catch (UsenetArticleNotFoundException)
                {
                    Serilog.Log.Warning("Article {segmentId} not found, skipping and providing empty stream.", segmentId);
                    // Provide an empty stream for missing articles to avoid crashing playback
                    if (_streamTasks.Writer.TryWrite(Task.FromResult<Stream>(Stream.Null))) continue;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Serilog.Log.Error(ex, "Error downloading segment {segmentId}", segmentId);
                    if (_streamTasks.Writer.TryWrite(Task.FromException<Stream>(ex))) continue;
                }
                break;
            }
        }
        catch (OperationCanceledException)
        {
            // Normal on disposal
        }
        finally
        {
            _streamTasks.Writer.TryComplete();
        }

        return;
    }

    private async Task<Stream> DownloadSegment
    (
        String segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken
    )
    {
        var bodyResponse = await _usenetClient
            .DecodedBodyAsync(segmentId, exclusiveConnection, cancellationToken)
            .ConfigureAwait(false);
        return bodyResponse.Stream;
    }

    public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count)
    {
        return ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    public override async ValueTask<int> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        while (!cancellationToken.IsCancellationRequested)
        {
            // if the stream is null, get the next stream.
            if (_stream == null)
            {
                if (!await _streamTasks.Reader.WaitToReadAsync(cancellationToken)) return 0;
                if (!_streamTasks.Reader.TryRead(out var streamTask)) return 0;
                
                try 
                {
                    _stream = await streamTask;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "Error reading from segment stream task.");
                    return 0;
                }
            }

            // read from the stream
            var read = await _stream.ReadAsync(buffer, cancellationToken);
            if (read > 0) return read;

            // if the stream ended, continue to the next stream.
            await _stream.DisposeAsync();
            _stream = null;
        }

        return 0;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    protected override void Dispose(Boolean disposing)
    {
        if (_disposed) return;
        if (!disposing) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _stream?.Dispose();
        _streamTasks.Writer.TryComplete();

        // ensure that streams that were never read from the channel get disposed
        while (_streamTasks.Reader.TryRead(out var streamTask))
            _ = Task.Run(async () => await (await streamTask).DisposeAsync(), CancellationToken.None);

        base.Dispose(disposing);
    }
}
