using RdtClient.Service.Services.Usenet;
using RdtClient.Service.Services.Usenet.Exceptions;
using RdtClient.Service.Services.Usenet.Extensions;
using RdtClient.Data.Models.Data;

namespace RdtClient.Service.Services.Usenet.Streams;

public class UsenetMultipartFileStream(
    UsenetMultipartFile.FilePart[] fileParts,
    INntpClient usenetClient,
    Int32 articleBufferSize
) : Stream
{
    private Int64 _position = 0;
    private CombinedStream? _innerStream;
    private Boolean _disposed;


    public override void Flush()
    {
        _innerStream?.Flush();
    }

    public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count)
    {
        return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    public override async Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken)
    {
        if (_innerStream == null) _innerStream = GetFileStream(_position);
        var read = await _innerStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        _position += read;
        return read;
    }

    public override Int64 Seek(Int64 offset, SeekOrigin origin)
    {
        var absoluteOffset = origin == SeekOrigin.Begin ? offset
            : origin == SeekOrigin.Current ? _position + offset
            : throw new InvalidOperationException("SeekOrigin must be Begin or Current.");
        if (_position == absoluteOffset) return _position;
        _position = absoluteOffset;
        _innerStream?.Dispose();
        _innerStream = null;
        return _position;
    }

    public override void SetLength(Int64 value)
    {
        throw new InvalidOperationException();
    }

    public override void Write(Byte[] buffer, Int32 offset, Int32 count)
    {
        throw new InvalidOperationException();
    }

    public override Boolean CanRead => true;
    public override Boolean CanSeek => true;
    public override Boolean CanWrite => false;
    public override Int64 Length { get; } = fileParts.Select(x => x.FilePartByteRange.Count).Sum();

    public override Int64 Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }


    private (Int32 filePartIndex, Int64 filePartOffset) SeekFilePart(Int64 byteOffset)
    {
        Int64 offset = 0;
        for (var i = 0; i < fileParts.Length; i++)
        {
            var filePart = fileParts[i];
            var nextOffset = offset + filePart.FilePartByteRange.Count;
            if (byteOffset < nextOffset)
                return (i, offset);
            offset = nextOffset;
        }

        throw new SeekPositionNotFoundException($"Corrupt file. Cannot seek to byte position {byteOffset}.");
    }

    private CombinedStream GetFileStream(Int64 rangeStart)
    {
        if (rangeStart == 0) return GetCombinedStream(0, 0);
        var (filePartIndex, filePartOffset) = SeekFilePart(rangeStart);
        var stream = GetCombinedStream(filePartIndex, rangeStart - filePartOffset);
        return stream;
    }

    private CombinedStream GetCombinedStream(Int32 firstFilePartIndex, Int64 additionalOffset)
    {
        var streams = fileParts[firstFilePartIndex..]
            .Select((x, i) =>
            {
                var offset = (i == 0) ? additionalOffset : 0;
                var stream = usenetClient.GetFileStream(x.SegmentIds, x.SegmentIdByteRange.Count, articleBufferSize);
                stream.Seek(x.FilePartByteRange.StartInclusive + offset, SeekOrigin.Begin);
                return Task.FromResult(stream.LimitLength(x.FilePartByteRange.Count - offset));
            });
        return new CombinedStream(streams);
    }

    protected override void Dispose(Boolean disposing)
    {
        if (_disposed) return;
        _innerStream?.Dispose();
        _disposed = true;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (_innerStream != null) await _innerStream.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
