using System.Runtime.CompilerServices;
using NWebDav.Server.Stores;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.WebDav.Base;

namespace RdtClient.Service.Services.Usenet.WebDav;

public class UsenetStoreCollection(UsenetJob job, INntpClient usenetClient) : BaseStoreReadonlyCollection
{
    public override String Name => job.JobName;
    public override String UniqueKey => job.UsenetJobId.ToString();
    public override DateTime CreatedAt => job.Added.DateTime;

    public override Task<IStoreItem?> GetItemAsync(String name, CancellationToken cancellationToken)
    {
        var file = job.Files.FirstOrDefault(f => Path.GetFileName(f.Path) == name);
        return Task.FromResult<IStoreItem?>(file != null ? new UsenetStoreFile(file, usenetClient) : null);
    }

    public override async IAsyncEnumerable<IStoreItem> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var file in job.Files)
        {
            yield return new UsenetStoreFile(file, usenetClient);
        }
        await Task.CompletedTask;
    }
}
