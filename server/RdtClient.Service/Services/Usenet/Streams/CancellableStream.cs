using System.Buffers;
using UsenetSharp.Streams;

namespace RdtClient.Service.Services.Usenet.Streams;

public class CancellableStream(Stream innerStream, CancellationToken token) : FastReadOnlyStream
{
    private readonly Stream _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
    private Boolean _disposed;

    public override Boolean CanSeek => _innerStream.CanSeek;
    public override Int64 Length => _innerStream.Length;

    public override Int64 Position
    {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override void Flush()
    {
        CheckDisposed();
        _innerStream.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        CheckDisposed();
        return _innerStream.FlushAsync(cancellationToken);
    }

    public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count)
    {
        CheckDisposed();
        return ReadAsync(buffer, offset, count, token)
            .GetAwaiter()
            .GetResult();
    }

    public override Int32 Read(Span<Byte> buffer)
    {
        CheckDisposed();
        var array = ArrayPool<Byte>.Shared.Rent(buffer.Length);
        try
        {
            var buffer1 = new Memory<Byte>(array, 0, buffer.Length);
            var result = this.ReadAsync(buffer1, token).AsTask().GetAwaiter().GetResult();
            buffer1.Span[..result].CopyTo(buffer);
            return result;
        }
        finally
        {
            ArrayPool<Byte>.Shared.Return(array);
        }
    }

    public override ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
    {
        CheckDisposed();
        return _innerStream.ReadAsync(buffer, cancellationToken);
    }

    public override void SetLength(Int64 value)
    {
        CheckDisposed();
        _innerStream.SetLength(value);
    }

    public override Int64 Seek(Int64 offset, SeekOrigin origin)
    {
        CheckDisposed();
        return _innerStream.Seek(offset, origin);
    }

    private void CheckDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CancellableStream));
        }
    }

    protected override void Dispose(Boolean disposing)
    {
        if (_disposed) return;
        if (!disposing) return;
        _disposed = true;
        _innerStream.Dispose();
        base.Dispose(disposing);
    }
}
