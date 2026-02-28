using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NWebDav.Server.Stores;
using RdtClient.Data.Data;
using RdtClient.Service.Services.Usenet.WebDav.Base;

namespace RdtClient.Service.Services.Usenet.WebDav;

public class UsenetStoreRoot(DataContext dataContext, INntpClient usenetClient) : BaseStoreReadonlyCollection
{
    public override String Name => "Usenet";
    public override String UniqueKey => "UsenetRoot";
    public override DateTime CreatedAt => DateTime.UnixEpoch;

    public override async Task<IStoreItem?> GetItemAsync(String name, CancellationToken cancellationToken)
    {
        var job = await dataContext.UsenetJobs
            .Include(j => j.Files)
            .FirstOrDefaultAsync(j => j.JobName == name, cancellationToken);
            
        if (job == null) return null;

        // Force every job to be a collection (folder) even if it has only one file
        return new UsenetStoreCollection(job, usenetClient);
    }

    public override async IAsyncEnumerable<IStoreItem> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var jobs = await dataContext.UsenetJobs
            .Include(j => j.Files)
            .ToListAsync(cancellationToken);
            
        foreach (var job in jobs)
        {
            // Force every job to be a collection (folder) even if it has only one file
            yield return new UsenetStoreCollection(job, usenetClient);
        }
    }
}
