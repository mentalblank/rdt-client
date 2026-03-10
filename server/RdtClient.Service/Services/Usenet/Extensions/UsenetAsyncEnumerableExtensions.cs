namespace RdtClient.Service.Services.Usenet.Extensions;

public static class UsenetAsyncEnumerableExtensions
{
    public static async Task<List<T>> GetAllAsync<T>
    (
        this IAsyncEnumerable<T> asyncEnumerable,
        CancellationToken ct = default,
        IProgress<Int32>? progress = null
    )
    {
        var done = 0;
        var results = new List<T>();
        await foreach (var result in asyncEnumerable.WithCancellation(ct))
        {
            results.Add(result);
            progress?.Report(++done);
        }

        return results;
    }
}
