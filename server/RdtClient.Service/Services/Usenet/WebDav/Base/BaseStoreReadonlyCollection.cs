using System.Xml.Linq;
using NWebDav.Server;
using NWebDav.Server.Props;
using NWebDav.Server.Stores;

namespace RdtClient.Service.Services.Usenet.WebDav.Base;

public abstract class BaseStoreReadonlyCollection : IStoreCollection, IPropertyManager
{
    public abstract String Name { get; }
    public abstract String UniqueKey { get; }
    public abstract DateTime CreatedAt { get; }
    
    public abstract Task<IStoreItem?> GetItemAsync(String name, CancellationToken cancellationToken);
    public abstract IAsyncEnumerable<IStoreItem> GetItemsAsync(CancellationToken cancellationToken);

    public IPropertyManager PropertyManager => this;

    public Task<Object?> GetPropertyAsync(IStoreItem item, XName name, Boolean skipExpensive, CancellationToken cancellationToken)
    {
        if (name == "{DAV:}displayname") return Task.FromResult<Object?>(Name);
        if (name == "{DAV:}getlastmodified") return Task.FromResult<Object?>(CreatedAt.ToString("R"));
        if (name == "{DAV:}creationdate") return Task.FromResult<Object?>(CreatedAt);
        if (name == "{DAV:}resourcetype") return Task.FromResult<Object?>(new XElement("{DAV:}collection"));
        
        return Task.FromResult<Object?>(null);
    }

    public Task<DavStatusCode> SetPropertyAsync(IStoreItem item, XName name, Object? value, CancellationToken cancellationToken)
    {
        return Task.FromResult(DavStatusCode.Forbidden);
    }

    public IList<PropertyInfo> Properties => new List<PropertyInfo>
    {
        new PropertyInfo("{DAV:}displayname", false),
        new PropertyInfo("{DAV:}getlastmodified", false),
        new PropertyInfo("{DAV:}creationdate", false),
        new PropertyInfo("{DAV:}resourcetype", false)
    };

    public Task<DavStatusCode> UploadFromStreamAsync(Stream source, CancellationToken cancellationToken)
    {
        return Task.FromResult(DavStatusCode.Forbidden);
    }

    public Task<StoreItemResult> CopyAsync(IStoreCollection destination, String name, Boolean overwrite, CancellationToken cancellationToken)
    {
        return Task.FromResult(new StoreItemResult(DavStatusCode.Forbidden));
    }

    public Task<StoreItemResult> MoveAsync(IStoreCollection destination, String name, Boolean overwrite, CancellationToken cancellationToken)
    {
        return Task.FromResult(new StoreItemResult(DavStatusCode.Forbidden));
    }

    public Task<StoreItemResult> CreateItemAsync(String name, Stream source, Boolean overwrite, CancellationToken cancellationToken)
    {
        return Task.FromResult(new StoreItemResult(DavStatusCode.Forbidden));
    }

    public Task<StoreCollectionResult> CreateCollectionAsync(String name, Boolean overwrite, CancellationToken cancellationToken)
    {
        return Task.FromResult(new StoreCollectionResult(DavStatusCode.Forbidden));
    }

    public Task<DavStatusCode> DeleteItemAsync(String name, CancellationToken cancellationToken)
    {
        return Task.FromResult(DavStatusCode.Forbidden);
    }

    public Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Boolean SupportsFastMove(IStoreCollection destination, String name, Boolean overwrite)
    {
        return false;
    }

    public Task<StoreItemResult> MoveItemAsync(String sourceName, IStoreCollection destination, String destinationName, Boolean overwrite, CancellationToken cancellationToken)
    {
        return Task.FromResult(new StoreItemResult(DavStatusCode.Forbidden));
    }

    public InfiniteDepthMode InfiniteDepthMode => InfiniteDepthMode.Rejected;
}
