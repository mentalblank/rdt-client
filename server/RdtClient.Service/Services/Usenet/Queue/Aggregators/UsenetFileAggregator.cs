using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.Queue.FileProcessors;

namespace RdtClient.Service.Services.Usenet.Queue.Aggregators;

public class UsenetFileAggregator(DataContext dataContext, UsenetDavItem mountDirectory, Boolean checkedFullHealth) : UsenetBaseAggregator
{
    protected override DataContext DataContext => dataContext;
    protected override UsenetDavItem MountDirectory => mountDirectory;

    public void UpdateDatabase(List<UsenetBaseProcessor.Result> processorResults)
    {
        foreach (var processorResult in processorResults)
        {
            if (processorResult is not UsenetFileProcessor.Result result) continue;
            if (String.IsNullOrEmpty(result.FileName)) continue; // skip files whose name we can't determine
            
            var parentDirectory = EnsureParentDirectory(result.FileName);
            var name = Path.GetFileName(result.FileName);

            var davItem = new UsenetDavItem
            {
                Id = Guid.NewGuid(),
                ParentId = parentDirectory.Id,
                Name = name,
                FileSize = result.FileSize,
                Type = UsenetDavItem.UsenetItemType.NzbFile,
                Path = Path.Join(parentDirectory.Path, name),
                CreatedAt = DateTime.Now,
                ReleaseDate = result.ReleaseDate,
                LastHealthCheck = checkedFullHealth ? DateTimeOffset.UtcNow : null
            };

            var usenetNzbFile = new UsenetNzbFile
            {
                Id = davItem.Id,
                SegmentIdList = result.NzbFile.GetSegmentIds(),
            };

            DataContext.UsenetDavItems.Add(davItem);
            DataContext.UsenetNzbFiles.Add(usenetNzbFile);
        }
    }
}
