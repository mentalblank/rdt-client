using System.Diagnostics.CodeAnalysis;
using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.Extensions;
using RdtClient.Service.Services.Usenet.Queue.FileProcessors;
using RdtClient.Service.Services.Usenet.Utils;

namespace RdtClient.Service.Services.Usenet.Queue.Aggregators;

public class UsenetRarAggregator(DataContext dataContext, UsenetDavItem mountDirectory, Boolean checkedFullHealth) : UsenetBaseAggregator
{
    protected override DataContext DataContext => dataContext;
    protected override UsenetDavItem MountDirectory => mountDirectory;

    public void UpdateDatabase(List<UsenetBaseProcessor.Result> processorResults)
    {
        var fileSegments = processorResults
            .OfType<UsenetRarProcessor.Result>()
            .SelectMany(x => x.StoredFileSegments)
            .ToList();

        ProcessArchive(fileSegments);
    }

    private void ProcessArchive(List<UsenetRarProcessor.StoredFileSegment> fileSegments)
    {
        var archiveFiles = new Dictionary<String, List<UsenetRarProcessor.StoredFileSegment>>();
        foreach (var fileSegment in fileSegments)
        {
            if (!archiveFiles.ContainsKey(fileSegment.PathWithinArchive))
                archiveFiles.Add(fileSegment.PathWithinArchive, []);

            archiveFiles[fileSegment.PathWithinArchive].Add(fileSegment);
        }

        foreach (var archiveFile in archiveFiles)
        {
            // Ensure we have all volumes necessary for this file.
            ValidateVolumes(archiveFile.Value);

            // Initialize dav-item fields
            var pathWithinArchive = archiveFile.Key;
            var fileParts = SortByPartNumber(archiveFile.Value);
            var aesParams = fileParts.Select(x => x.AesParams).FirstOrDefault(x => x != null);
            var fileSize = aesParams?.DecodedSize ?? fileParts.Sum(x => x.ByteRangeWithinPart.Count);
            var parentDirectory = EnsureParentDirectory(pathWithinArchive);
            var name = Path.GetFileName(pathWithinArchive);

            // If there is only one file in the archive and the file-name is obfuscated,
            // then rename the file to the same name as the containing mount directory.
            if (archiveFiles.Count == 1 && ObfuscationUtil.IsProbablyObfuscated(name))
                name = mountDirectory.Name + Path.GetExtension(name);

            var davItem = new UsenetDavItem
            {
                Id = Guid.NewGuid(),
                ParentId = parentDirectory.Id,
                Name = name,
                FileSize = fileSize,
                Type = UsenetDavItem.UsenetItemType.MultipartFile,
                Path = Path.Join(parentDirectory.Path, name),
                CreatedAt = DateTime.Now,
                ReleaseDate = fileParts.First().ReleaseDate,
                LastHealthCheck = checkedFullHealth ? DateTimeOffset.UtcNow : null
            };

            var usenetMultipartFile = new UsenetMultipartFile
            {
                Id = davItem.Id,
                MetadataObject = new UsenetMultipartFile.Meta
                {
                    AesParams = aesParams,
                    FileParts = fileParts.Select(x => new UsenetMultipartFile.FilePart
                    {
                        SegmentIds = x.NzbFile.GetSegmentIds(),
                        SegmentIdByteRange = LongRange.FromStartAndSize(0, x.PartSize),
                        FilePartByteRange = x.ByteRangeWithinPart
                    }).ToArray(),
                }
            };

            DataContext.UsenetDavItems.Add(davItem);
            DataContext.UsenetMultipartFiles.Add(usenetMultipartFile);
        }
    }

    private static UsenetRarProcessor.StoredFileSegment[] SortByPartNumber(
        List<UsenetRarProcessor.StoredFileSegment> storedFileSegments)
    {
        // Find delta between part number from headers and filenames.
        var delta = storedFileSegments
            .Select(x => x.PartNumber)
            .Where(x => x is { PartNumberFromHeader: >= 0, PartNumberFromFilename: >= 0 })
            .Select(x => x.PartNumberFromHeader - x.PartNumberFromFilename)
            .GroupBy(x => x)
            .MaxBy(x => x.Count())?.Key;

        // Ensure there are no duplicate part numbers.
        var allPartNumbers = storedFileSegments.Select(x => GetNormalizedPartNumber(x.PartNumber, delta));
        ValidatePartNumbers(allPartNumbers);

        // Sort by part numbers and return.
        return storedFileSegments
            .OrderBy(x => GetNormalizedPartNumber(x.PartNumber, delta))
            .ToArray();
    }

    private static void ValidateVolumes(List<UsenetRarProcessor.StoredFileSegment> storedFileSegments)
    {
        if (storedFileSegments.Count == 0) return;
        var distinctUncompressedSizes = storedFileSegments.Select(x => x.FileUncompressedSize).Distinct().ToList();
        if (distinctUncompressedSizes.Count != 1)
            throw new InvalidDataException("Inconsistent rar file size detected.");
        var expected = distinctUncompressedSizes[0];
        var actual = storedFileSegments.Sum(x => x.ByteRangeWithinPart.Count);
        if (Math.Abs(actual - expected) > 16)
            throw new InvalidDataException("Missing rar volumes detected.");
    }

    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    private static void ValidatePartNumbers(IEnumerable<Int32> partNumbers)
    {
        var count = partNumbers.Count();
        var uniqueCount = partNumbers.Distinct().Count();
        if (count != uniqueCount)
            throw new InvalidDataException("Rar archive has duplicate volume numbers.");
    }

    private static Int32 GetNormalizedPartNumber(UsenetRarProcessor.PartNumber partNumber, Int32? delta)
    {
        if (partNumber.PartNumberFromHeader >= 0) return partNumber.PartNumberFromHeader!.Value;
        if (partNumber.PartNumberFromFilename >= 0) return partNumber.PartNumberFromFilename!.Value + (delta ?? 0);
        return -1;
    }
}
