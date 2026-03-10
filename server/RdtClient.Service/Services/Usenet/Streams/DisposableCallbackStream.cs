namespace RdtClient.Service.Services.Usenet.Streams;

/// <summary>
/// A wrapper stream that delegates all operations to an inner stream and
/// invokes optional callbacks when the stream is disposed synchronously or asynchronously.
///
/// Use this class to hook into the disposal lifecycle of a stream without modifying its implementation.
/// </summary>
public class DisposableCallbackStream : Stream
{
    private readonly Stream _inner;
    private readonly Action? _onDispose;
    private readonly Func<ValueTask>? _onDisposeAsync;
    private Boolean _disposed;

    public DisposableCallbackStream(Stream inner, Action? onDispose = null, Func<ValueTask>? onDisposeAsync = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onDispose = onDispose;
        _onDisposeAsync = onDisposeAsync;
    }

    protected override void Dispose(Boolean disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _inner.Dispose();
            _onDispose?.Invoke();
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await _inner.DisposeAsync().ConfigureAwait(false);

        if (_onDisposeAsync != null)
            await _onDisposeAsync().ConfigureAwait(false);
        else
            _onDispose?.Invoke();

        _disposed = true;
        await base.DisposeAsync().ConfigureAwait(false);
    }

    // Core stream overrides
    public override Boolean CanRead => _inner.CanRead;
    public override Boolean CanSeek => _inner.CanSeek;
    public override Boolean CanWrite => _inner.CanWrite;
    public override Int64 Length => _inner.Length;

    public override Int64 Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override Int64 Seek(Int64 offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(Int64 value) => _inner.SetLength(value);

    // Read/Write (sync)
    public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) => _inner.Read(buffer, offset, count);
    public override void Write(Byte[] buffer, Int32 offset, Int32 count) => _inner.Write(buffer, offset, count);

    // Read/Write (async)
    public override Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken)
        => _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override Task WriteAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken)
        => _inner.WriteAsync(buffer, offset, count, cancellationToken);

    // Modern Span/Memory overloads
    public override Int32 Read(Span<Byte> buffer) => _inner.Read(buffer);

    public override ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        => _inner.ReadAsync(buffer, cancellationToken);

    public override void Write(ReadOnlySpan<Byte> buffer) => _inner.Write(buffer);

    public override ValueTask WriteAsync(ReadOnlyMemory<Byte> buffer, CancellationToken cancellationToken = default)
        => _inner.WriteAsync(buffer, cancellationToken);
}
