namespace RdtClient.Service.Services.Usenet.Streams;

/// <summary>
/// This class wraps an underlying stream and exposes a method to
/// probe whether the stream is empty or not, without changing the
/// position of the stream. It does this by reading a single byte
/// and buffering it in memory to relay during future Read requests.
/// </summary>
public class ProbingStream(Stream stream) : Stream
{
    private Boolean? _isEmpty;
    private Byte? _probeByte;
    private Boolean _disposed;

    public async Task<Boolean> IsEmptyAsync()
    {
        if (_isEmpty.HasValue)
            return _isEmpty.Value;

        var buffer = new Byte[1];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 1)).ConfigureAwait(false);

        if (bytesRead == 0)
        {
            _isEmpty = true;
        }
        else
        {
            _isEmpty = false;
            _probeByte = buffer[0];
        }

        return _isEmpty.Value;
    }

    public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count)
    {
        if (!_isEmpty.HasValue)
        {
            var read = stream.Read(buffer, offset, count);
            _isEmpty = read == 0;
            return read;
        }

        if (_probeByte.HasValue)
        {
            buffer[offset] = _probeByte.Value;
            _probeByte = null;
            var read = stream.Read(buffer, offset + 1, count - 1);
            return 1 + read;
        }

        return stream.Read(buffer, offset, count);
    }

    public override async Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken)
    {
        if (!_isEmpty.HasValue)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            _isEmpty = read == 0;
            return read;
        }

        if (_probeByte.HasValue)
        {
            buffer[offset] = _probeByte.Value;
            _probeByte = null;
            var read = await stream.ReadAsync(buffer.AsMemory(offset + 1, count - 1), cancellationToken).ConfigureAwait(false);
            return 1 + read;
        }

        return await stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_isEmpty.HasValue)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _isEmpty = read == 0;
            return read;
        }

        if (_probeByte.HasValue)
        {
            var span = buffer.Span;
            if (span.Length == 0)
                return 0;

            span[0] = (Byte)_probeByte.Value;
            _probeByte = null;

            var read = await stream.ReadAsync(buffer[1..], cancellationToken).ConfigureAwait(false);
            return 1 + read;
        }

        return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override void Flush() => stream.Flush();
    public override Int64 Seek(Int64 offset, SeekOrigin origin) => stream.Seek(offset, origin);
    public override void SetLength(Int64 value) => stream.SetLength(value);
    public override void Write(Byte[] buffer, Int32 offset, Int32 count) => stream.Write(buffer, offset, count);

    public override Boolean CanRead => stream.CanRead;
    public override Boolean CanSeek => stream.CanSeek;
    public override Boolean CanWrite => stream.CanWrite;
    public override Int64 Length => stream.Length;

    public override Int64 Position
    {
        get => stream.Position;
        set => stream.Position = value;
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
