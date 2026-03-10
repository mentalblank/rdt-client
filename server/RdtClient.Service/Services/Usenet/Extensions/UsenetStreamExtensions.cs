using System.Buffers;
using RdtClient.Service.Services.Usenet.Streams;

namespace RdtClient.Service.Services.Usenet.Extensions;

public static class UsenetStreamExtensions
{
    public static Stream LimitLength(this Stream stream, Int64 length)
    {
        return new LimitedLengthStream(stream, length);
    }

    public static async Task DiscardBytesAsync(this Stream stream, Int64 count, CancellationToken ct = default)
    {
        if (count == 0) return;
        var remaining = count;
        var throwaway = ArrayPool<Byte>.Shared.Rent(4096);
        try
        {
            while (remaining > 0)
            {
                var toRead = (Int32)Math.Min(remaining, throwaway.Length);
                var read = await stream.ReadAsync(throwaway.AsMemory(0, toRead), ct).ConfigureAwait(false);
                if (read == 0) break;
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<Byte>.Shared.Return(throwaway);
        }
    }

    public static Stream OnDispose(this Stream stream, Action onDispose)
    {
        return new DisposableCallbackStream(stream, onDispose, () =>
        {
            onDispose?.Invoke();
            return ValueTask.CompletedTask;
        });
    }

    public static Stream OnDisposeAsync(this Stream stream, Func<ValueTask> onDisposeAsync)
    {
        return new DisposableCallbackStream(stream, onDisposeAsync: onDisposeAsync);
    }
}
