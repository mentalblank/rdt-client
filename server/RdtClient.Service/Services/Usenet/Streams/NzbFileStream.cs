using RdtClient.Service.Services.Usenet;
using RdtClient.Service.Services.Usenet.Extensions;
using RdtClient.Service.Services.Usenet.Models;
using RdtClient.Service.Services.Usenet.Utils;

namespace RdtClient.Service.Services.Usenet.Streams;

public class NzbFileStream(
    String[] fileSegmentIds,
    Int64 fileSize,
    INntpClient usenetClient,
    Int32 articleBufferSize
) : FastReadOnlyStream
{
    private Int64 _position;
    private Boolean _disposed;
    private Stream? _innerStream;

    public override Boolean CanSeek => true;
    public override Int64 Length => fileSize;

    public override Int64 Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush()
    {
        _innerStream?.Flush();
    }

    public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count)
    {
        return ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= fileSize) return 0;
        
        while (_position < fileSize && !cancellationToken.IsCancellationRequested)
        {
            if (_innerStream == null)
            {
                _innerStream = await GetFileStream(_position, cancellationToken).ConfigureAwait(false);
            }
            
            var read = await _innerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _position += read;
            if (read > 0) return read;

            // If the stream ended but we haven't reached the end of the file, it means we need the next segment
            await _innerStream.DisposeAsync().ConfigureAwait(false);
            _innerStream = null;
        }
        
        return 0;
    }

    public override Int64 Seek(Int64 offset, SeekOrigin origin)
    {
        var absoluteOffset = origin == SeekOrigin.Begin ? offset
            : origin == SeekOrigin.Current ? _position + offset
            : throw new InvalidOperationException("SeekOrigin must be Begin or Current.");
        
        if (absoluteOffset < 0) absoluteOffset = 0;
        if (absoluteOffset > fileSize) absoluteOffset = fileSize;

        if (_position == absoluteOffset && _innerStream != null) return _position;
        
        _position = absoluteOffset;
        _innerStream?.Dispose();
        _innerStream = null;
        return _position;
    }

    private async Task<InterpolationSearch.Result> SeekSegment(Int64 byteOffset, CancellationToken ct)
    {
        return await InterpolationSearch.Find(
            byteOffset,
            new LongRange(0, fileSegmentIds.Length),
            new LongRange(0, fileSize),
            async (guess) =>
            {
                var header = await usenetClient.GetYencHeadersAsync(fileSegmentIds[guess], ct).ConfigureAwait(false);
                return new LongRange(header.PartOffset, header.PartOffset + header.PartSize);
            },
            ct
        ).ConfigureAwait(false);
    }

    private async Task<Stream> GetFileStream(Int64 rangeStart, CancellationToken cancellationToken)
    {
        if (rangeStart == 0) return GetMultiSegmentStream(0, cancellationToken);
        var foundSegment = await SeekSegment(rangeStart, cancellationToken).ConfigureAwait(false);
        var stream = GetMultiSegmentStream(foundSegment.FoundIndex, cancellationToken);
        await stream.DiscardBytesAsync(rangeStart - foundSegment.FoundByteRange.StartInclusive, cancellationToken)
            .ConfigureAwait(false);
        return stream;
    }

    private Stream GetMultiSegmentStream(Int32 firstSegmentIndex, CancellationToken cancellationToken)
    {
        var segmentIds = fileSegmentIds.AsMemory()[firstSegmentIndex..];
        return MultiSegmentStream.Create(segmentIds, usenetClient, articleBufferSize, cancellationToken);
    }

    protected override void Dispose(Boolean disposing)
    {
        if (_disposed) return;
        _innerStream?.Dispose();
        _disposed = true;
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (_innerStream != null) await _innerStream.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
