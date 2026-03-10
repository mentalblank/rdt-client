using UsenetSharp.Streams;

namespace RdtClient.Service.Services.Usenet.Streams;

public class LimitedLengthStream(Stream stream, Int64 length) : FastReadOnlyNonSeekableStream
{
    private Int64 _position;
    private Boolean _disposed;

    public override Int64 Length => length;
    public override void Flush() => stream.Flush();

    public override Int64 Position
    {
        get => stream.Position;
        set => throw new NotSupportedException();
    }

    public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
    {
        // If we've already read the specified length, return 0 (end of stream)
        if (_position >= length)
            return 0;

        // Calculate how many bytes we can still read
        var remainingBytes = length - _position;
        var bytesToRead = (Int32)Math.Min(remainingBytes, buffer.Length);

        // Read from the underlying stream
        var bytesRead = await stream.ReadAsync(buffer[..bytesToRead], cancellationToken).ConfigureAwait(false);

        // Update the position by the number of bytes read
        _position += bytesRead;

        // Return the number of bytes read
        return bytesRead;
    }

    protected override void Dispose(Boolean disposing)
    {
        if (_disposed) return;
        stream.Dispose();
        _disposed = true;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await stream.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
