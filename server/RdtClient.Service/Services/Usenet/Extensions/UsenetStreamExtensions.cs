using System.Buffers;

namespace RdtClient.Service.Services.Usenet.Extensions;

public static class UsenetStreamExtensions
{
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
}
