using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.Queue.FileProcessors;
using RdtClient.Service.Services.Usenet.Utils;

namespace RdtClient.Service.Services.Usenet.Queue.Aggregators;

public class UsenetSevenZipAggregator(
    DataContext dataContext,
    UsenetDavItem mountDirectory,
    Boolean checkedFullHealth
) : UsenetBaseAggregator
{
    protected override DataContext DataContext => dataContext;
    protected override UsenetDavItem MountDirectory => mountDirectory;

    public void UpdateDatabase(List<UsenetBaseProcessor.Result> processorResults)
    {
        var sevenZipFiles = processorResults
            .OfType<UsenetSevenZipProcessor.Result>()
            .SelectMany(x => x.SevenZipFiles)
            .ToList();

        ProcessSevenZipFile(sevenZipFiles);
    }

    private void ProcessSevenZipFile(List<UsenetSevenZipProcessor.SevenZipFile> sevenZipFiles)
    {
        foreach (var sevenZipFile in sevenZipFiles)
        {
            var pathWithinArchive = sevenZipFile.PathWithinArchive;
            var davMultipartFileMeta = sevenZipFile.DavMultipartFileMeta;
            var parentDirectory = EnsureParentDirectory(pathWithinArchive);
            var name = Path.GetFileName(pathWithinArchive);

            // If there is only one file in the archive and the file-name is obfuscated,
            // then rename the file to the same name as the containing mount directory.
            if (sevenZipFiles.Count == 1 && ObfuscationUtil.IsProbablyObfuscated(name))
                name = mountDirectory.Name + Path.GetExtension(name);

            var davItem = new UsenetDavItem
            {
                Id = Guid.NewGuid(),
                ParentId = parentDirectory.Id,
                Name = name,
                FileSize = davMultipartFileMeta.AesParams?.DecodedSize
                    ?? davMultipartFileMeta.FileParts.Sum(x => x.FilePartByteRange.Count),
                Type = UsenetDavItem.UsenetItemType.MultipartFile,
                Path = Path.Join(parentDirectory.Path, name),
                CreatedAt = DateTime.Now,
                ReleaseDate = sevenZipFile.ReleaseDate,
                LastHealthCheck = checkedFullHealth ? DateTimeOffset.UtcNow : null
            };

            var usenetMultipartFile = new UsenetMultipartFile
            {
                Id = davItem.Id,
                MetadataObject = davMultipartFileMeta
            };

            DataContext.UsenetDavItems.Add(davItem);
            DataContext.UsenetMultipartFiles.Add(usenetMultipartFile);
        }
    }
}
