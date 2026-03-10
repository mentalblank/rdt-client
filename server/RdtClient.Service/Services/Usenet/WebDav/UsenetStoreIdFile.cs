using NWebDav.Server.Stores;
using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.WebDav.Base;

namespace RdtClient.Service.Services.Usenet.WebDav;

public class UsenetStoreIdFile(
    UsenetDavItem davItem,
    DataContext dataContext,
    INntpClient usenetClient
) : BaseStoreReadonlyItem
{
    public override String Name => davItem.Id.ToString();
    public override String UniqueKey => davItem.Id.ToString();
    public override Int64 FileSize => davItem.FileSize ?? 0;
    public override DateTime CreatedAt => davItem.CreatedAt;

    public override Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        return new UsenetStoreItem(davItem, dataContext, usenetClient).GetReadableStreamAsync(cancellationToken);
    }
}
