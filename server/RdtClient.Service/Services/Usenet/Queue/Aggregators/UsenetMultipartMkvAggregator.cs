using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.Queue.FileProcessors;

namespace RdtClient.Service.Services.Usenet.Queue.Aggregators;

public class UsenetMultipartMkvAggregator(
    DataContext dataContext,
    UsenetDavItem mountDirectory,
    Boolean checkedFullHealth
) : UsenetBaseAggregator
{
    protected override DataContext DataContext => dataContext;
    protected override UsenetDavItem MountDirectory => mountDirectory;

    public void UpdateDatabase(List<UsenetBaseProcessor.Result> processorResults)
    {
        var mkvFiles = processorResults
            .OfType<UsenetMultipartMkvProcessor.Result>()
            .ToList();

        ProcessMkvFiles(mkvFiles);
    }

    private void ProcessMkvFiles(List<UsenetMultipartMkvProcessor.Result> mkvFiles)
    {
        foreach (var mkvFile in mkvFiles)
        {
            var parentDirectory = EnsureParentDirectory(mkvFile.Filename);
            var name = Path.GetFileName(mkvFile.Filename);

            var davItem = new UsenetDavItem
            {
                Id = Guid.NewGuid(),
                ParentId = parentDirectory.Id,
                Name = name,
                FileSize = mkvFile.Parts.Sum(x => x.FilePartByteRange.Count),
                Type = UsenetDavItem.UsenetItemType.MultipartFile,
                Path = Path.Join(parentDirectory.Path, name),
                CreatedAt = DateTime.Now,
                ReleaseDate = mkvFile.ReleaseDate,
                LastHealthCheck = checkedFullHealth ? DateTimeOffset.UtcNow : null
            };

            var usenetMultipartFile = new UsenetMultipartFile
            {
                Id = davItem.Id,
                MetadataObject = new UsenetMultipartFile.Meta
                {
                    FileParts = mkvFile.Parts.ToArray()
                }
            };

            DataContext.UsenetDavItems.Add(davItem);
            DataContext.UsenetMultipartFiles.Add(usenetMultipartFile);
        }
    }
}
