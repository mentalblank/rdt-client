using NWebDav.Server.Stores;
using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace RdtClient.Service.Services.Usenet.WebDav;

public class UsenetStore(DataContext dataContext, INntpClient usenetClient) : IStore
{
    private readonly UsenetStoreCollection _root = new(UsenetDavItemConstants.Root, dataContext, usenetClient);

    public async Task<IStoreItem?> GetItemAsync(String path, CancellationToken cancellationToken)
    {
        if (path.StartsWith("/dav/usenet", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring("/dav/usenet".Length);
        }
        
        path = path.Trim('/');
        if (path == "") return _root;

        var normalizedPath = "/" + path.Replace('\\', '/');

        // Handle the virtual /.ids folder
        if (normalizedPath.StartsWith("/.ids", StringComparison.OrdinalIgnoreCase))
        {
            if (normalizedPath.Length == 5) // Exactly "/.ids"
            {
                return new UsenetStoreIdsCollection(".ids", "/.ids", dataContext, usenetClient);
            }

            var subPath = normalizedPath.Substring(5).TrimStart('/');
            var segments = subPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            
            IStoreItem? current = new UsenetStoreIdsCollection(".ids", "/.ids", dataContext, usenetClient);
            foreach (var segment in segments)
            {
                if (current is not IStoreCollection collection) return null;
                current = await collection.GetItemAsync(segment, cancellationToken).ConfigureAwait(false);
                if (current == null) return null;
            }
            return current;
        }
        
        // Resolve the item from the database by its path
        var davItem = await dataContext.UsenetDavItems
            .FirstOrDefaultAsync(x => x.Path == normalizedPath, cancellationToken);
            
        if (davItem == null) return null;
        
        if (davItem.Type == UsenetDavItem.UsenetItemType.Directory || 
            davItem.Type == UsenetDavItem.UsenetItemType.SymlinkRoot || 
            davItem.Type == UsenetDavItem.UsenetItemType.IdsRoot)
        {
            return new UsenetStoreCollection(davItem, dataContext, usenetClient);
        }

        return new UsenetStoreItem(davItem, dataContext, usenetClient);
    }

    public Task<IStoreItem?> GetItemAsync(Uri uri, CancellationToken cancellationToken)
    {
        return GetItemAsync(Uri.UnescapeDataString(uri.AbsolutePath), cancellationToken);
    }

    public async Task<IStoreCollection?> GetCollectionAsync(Uri uri, CancellationToken cancellationToken)
    {
        return await GetItemAsync(uri, cancellationToken).ConfigureAwait(false) as IStoreCollection;
    }
}
