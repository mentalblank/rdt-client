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
        // Check if it's a category folder
        var category = await dataContext.UsenetJobs
            .Where(j => j.Category != null)
            .Select(j => j.Category)
            .Distinct()
            .FirstOrDefaultAsync(c => c == name, cancellationToken);

        if (category != null)
        {
            return new UsenetStoreCategory(category, dataContext, usenetClient);
        }

        // Check if it's a job with no category (at root)
        var job = await dataContext.UsenetJobs
            .Include(j => j.Files)
            .FirstOrDefaultAsync(j => j.Category == null && j.JobName == name, cancellationToken);
            
        if (job != null)
        {
            return new UsenetStoreCollection(job, usenetClient);
        }

        return null;
    }

    public override async IAsyncEnumerable<IStoreItem> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 1. List Category Folders
        var categories = await dataContext.UsenetJobs
            .Where(j => j.Category != null)
            .Select(j => j.Category)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var category in categories)
        {
            if (category != null)
            {
                yield return new UsenetStoreCategory(category, dataContext, usenetClient);
            }
        }

        // 2. List Jobs with No Category (at root)
        var jobsNoCategory = await dataContext.UsenetJobs
            .Include(j => j.Files)
            .Where(j => j.Category == null)
            .ToListAsync(cancellationToken);

        foreach (var job in jobsNoCategory)
        {
            yield return new UsenetStoreCollection(job, usenetClient);
        }
    }
}
