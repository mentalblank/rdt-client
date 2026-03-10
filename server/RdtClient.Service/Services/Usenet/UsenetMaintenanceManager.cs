using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.Utils;
using RdtClient.Service.Services;

namespace RdtClient.Service.Services.Usenet;

public class UsenetMaintenanceManager(ILogger<UsenetMaintenanceManager> logger, DataContext dataContext)
{
    public async Task<Int32> RemoveOrphanedFiles(CancellationToken ct)
    {
        var allDavItems = await dataContext.UsenetDavItems.ToListAsync(ct).ConfigureAwait(false);
        var linkedIds = UsenetOrganizedLinksUtil.GetLibraryDavItemLinks()
            .Select(x => x.DavItemId)
            .ToHashSet();

        if (linkedIds.Count < 5)
        {
            logger.LogWarning("Usenet maintenance aborted: Less than 5 linked files found in library. Skipping cleanup to prevent accidental data loss.");
            return 0;
        }

        var removedItems = new HashSet<Guid>();
        var dateThreshold = DateTime.Now.Subtract(TimeSpan.FromDays(1));

        var unlinkedFiles = allDavItems
            .Where(x => x.Type is UsenetDavItem.UsenetItemType.NzbFile 
                                or UsenetDavItem.UsenetItemType.RarFile 
                                or UsenetDavItem.UsenetItemType.MultipartFile)
            .Where(x => x.CreatedAt < dateThreshold)
            .Where(x => !linkedIds.Contains(x.Id))
            .ToList();

        foreach (var file in unlinkedFiles)
        {
            RemoveItem(file, allDavItems, removedItems);
        }

        if (removedItems.Count > 0)
        {
            await dataContext.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation($"Removed {removedItems.Count} orphaned Usenet items from database.");
        }

        return removedItems.Count;
    }

    private void RemoveItem(UsenetDavItem item, List<UsenetDavItem> allItems, HashSet<Guid> removedItems)
    {
        if (removedItems.Contains(item.Id)) return;

        // Check if it's a protected root folder
        if (item.Id == UsenetDavItemConstants.Root.Id || 
            item.Id == UsenetDavItemConstants.NzbFolder.Id || 
            item.Id == UsenetDavItemConstants.ContentFolder.Id || 
            item.Id == UsenetDavItemConstants.IdsFolder.Id)
        {
            return;
        }

        dataContext.UsenetDavItems.Remove(item);
        removedItems.Add(item.Id);

        // Recursively remove empty parent directories
        if (item.ParentId.HasValue)
        {
            var parent = allItems.FirstOrDefault(x => x.Id == item.ParentId.Value);
            if (parent != null)
            {
                var otherChildren = allItems.Where(x => x.ParentId == parent.Id && !removedItems.Contains(x.Id)).ToList();
                if (otherChildren.Count == 0)
                {
                    RemoveItem(parent, allItems, removedItems);
                }
            }
        }
    }
}
