using System.Text.RegularExpressions;
using RdtClient.Service.Services.Usenet.Extensions;
using RdtClient.Service.Services.Usenet.Models;
using RdtClient.Service.Services.Usenet.Queue.Steps;
using RdtClient.Service.Services.Usenet.Streams;
using RdtClient.Service.Services.Usenet.Utils;
using SharpCompress.Common;
using RdtClient.Data.Models.Data;

namespace RdtClient.Service.Services.Usenet.Queue.FileProcessors;

public class UsenetSevenZipProcessor : UsenetBaseProcessor
{
    private readonly List<UsenetGetFileInfosStep.FileInfo> _fileInfos;
    private readonly INntpClient _usenetClient;
    private readonly String? _archivePassword;
    private readonly CancellationToken _ct;

    public UsenetSevenZipProcessor
    (
        List<UsenetGetFileInfosStep.FileInfo> fileInfos,
        INntpClient usenetClient,
        String? archivePassword,
        CancellationToken ct
    )
    {
        _fileInfos = fileInfos;
        _usenetClient = usenetClient;
        _archivePassword = archivePassword;
        _ct = ct;
    }

    public override async Task<UsenetBaseProcessor.Result?> ProcessAsync(IProgress<Int32> progress)
    {
        try
        {
            // Simple scale logic for progress
            var multipartFile = await GetMultipartFile().ConfigureAwait(false);
            
            // We use the temporary UsenetFileStream instead of creating a full DB record first
            var segmentIds = multipartFile.FileParts.SelectMany(x => x.SegmentIds).ToArray();
            var totalSize = multipartFile.FileParts.Sum(x => x.FilePartByteRange.Count);
            
            await using var stream = _usenetClient.GetFileStream(segmentIds, totalSize, articleBufferSize: 0);
            
            var sevenZipEntries = await UsenetSevenZipUtil
                .GetSevenZipEntriesAsync(stream, _archivePassword, _ct)
                .ConfigureAwait(false);

            if (sevenZipEntries.Any(x => x.CompressionType != CompressionType.None))
            {
                const String message = "Only uncompressed 7z files are supported.";
                throw new Exceptions.Unsupported7zCompressionMethodException(message);
            }

            if (sevenZipEntries.Any(x => x.IsEncrypted && x.IsSolid))
            {
                const String message = "Password-protected 7z archives cannot be solid.";
                throw new Exceptions.NonRetryableDownloadException(message);
            }

            var results = new List<SevenZipFile>();
            foreach (var x in sevenZipEntries)
            {
                results.Add(new SevenZipFile
                {
                    PathWithinArchive = x.PathWithinArchive,
                    DavMultipartFileMeta = await GetUsenetMultipartFileMeta(x, multipartFile),
                    ReleaseDate = _fileInfos.First().ReleaseDate,
                });
            }

            return new Result
            {
                SevenZipFiles = results
            };
        }
        finally
        {
            progress.Report(100);
        }
    }

    private async Task<UsenetMultipartFile.Meta> GetMultipartFile()
    {
        var sortedFileInfos = _fileInfos.OrderBy(f => GetPartNumber(f.FileName)).ToList();
        var fileParts = new List<UsenetMultipartFile.FilePart>();
        Int64 startInclusive = 0;
        foreach (var fileInfo in sortedFileInfos)
        {
            var nzbFile = fileInfo.NzbFile;
            var fileSize = fileInfo.FileSize ?? await _usenetClient
                .GetFileSizeAsync(nzbFile, _ct)
                .ConfigureAwait(false);
            var endExclusive = startInclusive + fileSize;
            fileParts.Add(new UsenetMultipartFile.FilePart
            {
                SegmentIds = fileInfo.NzbFile.GetSegmentIds(),
                SegmentIdByteRange = LongRange.FromStartAndSize(0, fileSize),
                FilePartByteRange = new LongRange(startInclusive, endExclusive),
            });
            startInclusive = endExclusive;
        }

        return new UsenetMultipartFile.Meta { FileParts = [.. fileParts] };
    }

    private static Int32 GetPartNumber(String filename)
    {
        var match = Regex.Match(filename, @"\.7z(\.(\d+))?$", RegexOptions.IgnoreCase);
        return String.IsNullOrEmpty(match.Groups[2].Value) ? -1 : Int32.Parse(match.Groups[2].Value);
    }

    private async Task<UsenetMultipartFile.Meta> GetUsenetMultipartFileMeta
    (
        UsenetSevenZipUtil.SevenZipEntry sevenZipEntry,
        UsenetMultipartFile.Meta multipartFile
    )
    {
        var totalSize = multipartFile.FileParts.Sum(x => x.FilePartByteRange.Count);
        
        var startResult = await InterpolationSearch.Find(
            sevenZipEntry.ByteRangeWithinArchive.StartInclusive,
            new LongRange(0, multipartFile.FileParts.Length),
            new LongRange(0, totalSize),
            async guess => 
            {
                var part = multipartFile.FileParts[guess];
                return await ValueTask.FromResult(new LongRange(part.FilePartByteRange.StartInclusive, part.FilePartByteRange.EndExclusive));
            },
            _ct
        ).ConfigureAwait(false);

        var endResult = await InterpolationSearch.Find(
            sevenZipEntry.ByteRangeWithinArchive.EndExclusive - 1,
            new LongRange(0, multipartFile.FileParts.Length),
            new LongRange(0, totalSize),
            async guess => 
            {
                var part = multipartFile.FileParts[guess];
                return await ValueTask.FromResult(new LongRange(part.FilePartByteRange.StartInclusive, part.FilePartByteRange.EndExclusive));
            },
            _ct
        ).ConfigureAwait(false);

        var startIndexInclusive = startResult.FoundIndex;
        var startIndexByteRange = startResult.FoundByteRange;
        var endIndexInclusive = endResult.FoundIndex;
        var endIndexByteRange = endResult.FoundByteRange;

        var endIndexExclusive = endIndexInclusive + 1;
        var indexCount = endIndexExclusive - startIndexInclusive;
        var fileParts = Enumerable
            .Range(startIndexInclusive, indexCount)
            .Select(index =>
            {
                var part = multipartFile.FileParts[index];
                var partStartInclusive = index == startIndexInclusive
                    ? sevenZipEntry.ByteRangeWithinArchive.StartInclusive - part.FilePartByteRange.StartInclusive
                    : 0;
                var partEndExclusive = index == endIndexInclusive
                    ? sevenZipEntry.ByteRangeWithinArchive.EndExclusive - part.FilePartByteRange.StartInclusive
                    : part.SegmentIdByteRange.Count;
                var partByteCount = partEndExclusive - partStartInclusive;

                return new UsenetMultipartFile.FilePart
                {
                    SegmentIds = part.SegmentIds,
                    SegmentIdByteRange = part.SegmentIdByteRange,
                    FilePartByteRange = LongRange.FromStartAndSize(partStartInclusive, partByteCount),
                };
            })
            .ToArray();

        return new UsenetMultipartFile.Meta
        {
            AesParams = sevenZipEntry.AesParams,
            FileParts = fileParts,
        };
    }

    public new class Result : UsenetBaseProcessor.Result
    {
        public required List<SevenZipFile> SevenZipFiles { get; init; }
    }

    public class SevenZipFile
    {
        public required String PathWithinArchive { get; init; }
        public required UsenetMultipartFile.Meta DavMultipartFileMeta { get; init; }
        public required DateTimeOffset ReleaseDate { get; init; }
    }
}
