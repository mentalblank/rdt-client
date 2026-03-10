using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NWebDav.Server.Stores;
using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.WebDav.Base;
using RdtClient.Service.Services.Usenet.Extensions;

namespace RdtClient.Service.Services.Usenet.WebDav;

public class UsenetStoreIdsCollection(
    String name,
    String currentPath,
    DataContext dataContext,
    INntpClient usenetClient
) : BaseStoreReadonlyCollection
{
    public override String Name => name;
    public override String UniqueKey => currentPath;
    public override DateTime CreatedAt => DateTime.UnixEpoch;

    private const String Alphabet = "0123456789abcdef";

    private readonly String[] _currentPathParts = currentPath.Split(
        ['/', '\\'],
        StringSplitOptions.RemoveEmptyEntries
    );

    public override async Task<IStoreItem?> GetItemAsync(String name, CancellationToken cancellationToken)
    {
        if (_currentPathParts.Length < 5) // Matches DavItem.IdPrefixLength
        {
            if (name.Length != 1) return null;
            if (!Alphabet.Contains(name[0])) return null;
            return new UsenetStoreIdsCollection(name, Path.Combine(currentPath, name), dataContext, usenetClient);
        }

        if (Guid.TryParse(name, out var id))
        {
            var item = await dataContext.UsenetDavItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            return item == null ? null : new UsenetStoreIdFile(item, dataContext, usenetClient);
        }

        return null;
    }

    public override async IAsyncEnumerable<IStoreItem> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_currentPathParts.Length < 5)
        {
            foreach (var c in Alphabet)
            {
                var s = c.ToString();
                yield return new UsenetStoreIdsCollection(s, Path.Combine(currentPath, s), dataContext, usenetClient);
            }
        }
        else
        {
            var idPrefix = String.Join("", _currentPathParts);
            // In a real database, we'd want an index or a specific column for this.
            // For now, we'll do a simple filtering if it's not too slow, 
            // but ideally UsenetDavItem should have an IdPrefix column.
            
            var items = await dataContext.UsenetDavItems
                .Where(x => x.Type != UsenetDavItem.UsenetItemType.Directory)
                .ToListAsync(cancellationToken);

            foreach (var item in items.Where(x => x.Id.GetFiveLengthPrefix() == idPrefix))
            {
                yield return new UsenetStoreIdFile(item, dataContext, usenetClient);
            }
        }
    }
}
