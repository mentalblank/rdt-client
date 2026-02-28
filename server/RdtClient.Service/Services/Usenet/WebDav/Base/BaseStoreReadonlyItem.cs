using System.Xml.Linq;
using NWebDav.Server;
using NWebDav.Server.Props;
using NWebDav.Server.Stores;

namespace RdtClient.Service.Services.Usenet.WebDav.Base;

public abstract class BaseStoreReadonlyItem : IStoreItem, IPropertyManager
{
    public abstract String Name { get; }
    public abstract String UniqueKey { get; }
    public abstract Int64 FileSize { get; }
    public abstract DateTime CreatedAt { get; }
    
    public abstract Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken);

    public IPropertyManager PropertyManager => this;

    public Task<Object?> GetPropertyAsync(IStoreItem item, XName name, Boolean skipExpensive, CancellationToken cancellationToken)
    {
        if (name == "{DAV:}displayname") return Task.FromResult<Object?>(Name);
        if (name == "{DAV:}getcontentlength") return Task.FromResult<Object?>(FileSize);
        if (name == "{DAV:}getlastmodified") return Task.FromResult<Object?>(CreatedAt.ToString("R"));
        if (name == "{DAV:}creationdate") return Task.FromResult<Object?>(CreatedAt);
        if (name == "{DAV:}resourcetype") return Task.FromResult<Object?>(null);
        
        return Task.FromResult<Object?>(null);
    }

    public Task<DavStatusCode> SetPropertyAsync(IStoreItem item, XName name, Object? value, CancellationToken cancellationToken)
    {
        return Task.FromResult(DavStatusCode.Forbidden);
    }

    public IList<PropertyInfo> Properties => new List<PropertyInfo>
    {
        new PropertyInfo("{DAV:}displayname", false),
        new PropertyInfo("{DAV:}getcontentlength", false),
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
}
