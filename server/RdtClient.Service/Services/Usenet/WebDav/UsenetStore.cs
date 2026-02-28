using NWebDav.Server.Stores;
using RdtClient.Data.Data;

namespace RdtClient.Service.Services.Usenet.WebDav;

public class UsenetStore : IStore, IDisposable
{
    private readonly INntpClient _cachingClient;
    private readonly UsenetStoreRoot _root;

    public UsenetStore(DataContext dataContext, INntpClient usenetClient)
    {
        // Wrap the singleton streaming client in a request-scoped cache
        _cachingClient = new ArticleCachingNntpClient(usenetClient, true);
        _root = new UsenetStoreRoot(dataContext, _cachingClient);
    }

    public async Task<IStoreItem?> GetItemAsync(String path, CancellationToken cancellationToken)
    {
        if (path.StartsWith("/dav/usenet", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring("/dav/usenet".Length);
        }
        
        path = path.Trim('/');
        if (path == "") return _root;
        
        var segments = path.Split('/');
        IStoreItem? current = _root;
        
        foreach (var segment in segments)
        {
            if (current is not IStoreCollection collection) return null;
            current = await collection.GetItemAsync(segment, cancellationToken).ConfigureAwait(false);
            if (current == null) return null;
        }
        
        return current;
    }

    public Task<IStoreItem?> GetItemAsync(Uri uri, CancellationToken cancellationToken)
    {
        return GetItemAsync(Uri.UnescapeDataString(uri.AbsolutePath), cancellationToken);
    }

    public async Task<IStoreCollection?> GetCollectionAsync(Uri uri, CancellationToken cancellationToken)
    {
        return await GetItemAsync(uri, cancellationToken).ConfigureAwait(false) as IStoreCollection;
    }

    public void Dispose()
    {
        _cachingClient.Dispose();
    }
}
