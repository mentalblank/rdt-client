using RdtClient.Service.Services.Usenet.Models;
using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace RdtClient.Service.Services.Usenet.Streams;

/// <summary>
/// A YencStream that reads from already-decoded cached data instead of decoding yenc-encoded data.
/// This stream bypasses the yenc decoding process by directly serving cached decoded bytes
/// and returning pre-parsed yenc headers.
/// </summary>
public class CachedYencStream(UsenetYencHeader cachedHeaders, Stream cachedDecodedStream) : YencStream(Null)
{
    public override ValueTask<UsenetYencHeader?> GetYencHeadersAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<UsenetYencHeader?>(cachedHeaders);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return cachedDecodedStream.ReadAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            cachedDecodedStream?.Dispose();
        }

        base.Dispose(disposing);
    }
}
