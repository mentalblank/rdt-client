using RdtClient.Service.Services.Usenet;

namespace RdtClient.Service.Services.Usenet.Streams;

public class UnbufferedMultiSegmentStream : FastReadOnlyNonSeekableStream
{
    private readonly Memory<String> _segmentIds;
    private readonly INntpClient _usenetClient;
    private Stream? _stream;
    private Int32 _currentIndex;
    private Boolean _disposed;

    public UnbufferedMultiSegmentStream(Memory<String> segmentIds, INntpClient usenetClient)
    {
        _segmentIds = segmentIds;
        _usenetClient = usenetClient;
    }

    public override async ValueTask<int> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        while (!cancellationToken.IsCancellationRequested)
        {
            // if the stream is null, get the next stream.
            if (_stream == null)
            {
                if (_currentIndex >= _segmentIds.Length) return 0;
                var body = await _usenetClient.DecodedBodyAsync(_segmentIds.Span[_currentIndex++], cancellationToken);
                _stream = body.Stream;
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
        _stream?.Dispose();
        base.Dispose(disposing);
    }
}
