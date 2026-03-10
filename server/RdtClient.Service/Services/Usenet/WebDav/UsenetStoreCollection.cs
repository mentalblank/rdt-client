using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NWebDav.Server.Stores;
using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.WebDav.Base;

namespace RdtClient.Service.Services.Usenet.WebDav;

public class UsenetStoreCollection(UsenetDavItem davItem, DataContext dataContext, INntpClient usenetClient) : BaseStoreReadonlyCollection
{
    public override String Name => davItem.Name;
    public override String UniqueKey => davItem.Id.ToString();
    public override DateTime CreatedAt => davItem.CreatedAt;

    public override async Task<IStoreItem?> GetItemAsync(String name, CancellationToken cancellationToken)
    {
        var child = await dataContext.UsenetDavItems
            .FirstOrDefaultAsync(x => x.ParentId == davItem.Id && x.Name == name, cancellationToken);
            
        return child != null ? GetItem(child) : null;
    }

    public override async IAsyncEnumerable<IStoreItem> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var children = await dataContext.UsenetDavItems
            .Where(x => x.ParentId == davItem.Id)
            .ToListAsync(cancellationToken);

        foreach (var child in children)
        {
            yield return GetItem(child);
        }
    }

    private IStoreItem GetItem(UsenetDavItem childItem)
    {
        if (childItem.Type == UsenetDavItem.UsenetItemType.IdsRoot)
        {
            return new UsenetStoreIdsCollection(childItem.Name, childItem.Path, dataContext, usenetClient);
        }

        if (childItem.Type == UsenetDavItem.UsenetItemType.Directory || 
            childItem.Type == UsenetDavItem.UsenetItemType.SymlinkRoot)
        {
            return new UsenetStoreCollection(childItem, dataContext, usenetClient);
        }

        return new UsenetStoreItem(childItem, dataContext, usenetClient);
    }
}
