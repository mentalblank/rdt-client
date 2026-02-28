using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NWebDav.Server.Stores;
using RdtClient.Data.Data;
using RdtClient.Service.Services.Usenet.WebDav.Base;

namespace RdtClient.Service.Services.Usenet.WebDav;

public class UsenetStoreCategory(String categoryName, DataContext dataContext, INntpClient usenetClient) : BaseStoreReadonlyCollection
{
    public override String Name => categoryName;
    public override String UniqueKey => $"Category_{categoryName}";
    public override DateTime CreatedAt => DateTime.UnixEpoch;

    public override async Task<IStoreItem?> GetItemAsync(String name, CancellationToken cancellationToken)
    {
        var job = await dataContext.UsenetJobs
            .Include(j => j.Files)
            .FirstOrDefaultAsync(j => j.Category == categoryName && j.JobName == name, cancellationToken);
            
        if (job != null)
        {
            return new UsenetStoreCollection(job, usenetClient);
        }

        return null;
    }

    public override async IAsyncEnumerable<IStoreItem> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var jobs = await dataContext.UsenetJobs
            .Include(j => j.Files)
            .Where(j => j.Category == categoryName)
            .ToListAsync(cancellationToken);
            
        foreach (var job in jobs)
        {
            yield return new UsenetStoreCollection(job, usenetClient);
        }
    }
}
