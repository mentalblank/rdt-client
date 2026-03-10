using System.Text.RegularExpressions;
using RdtClient.Service.Services.Usenet.Models;
using RdtClient.Service.Services.Usenet.Queue.Steps;
using RdtClient.Data.Models.Data;

namespace RdtClient.Service.Services.Usenet.Queue.FileProcessors;

public class UsenetMultipartMkvProcessor : UsenetBaseProcessor
{
    private readonly List<UsenetGetFileInfosStep.FileInfo> _fileInfos;
    private readonly INntpClient _usenetClient;
    private readonly CancellationToken _ct;

    public UsenetMultipartMkvProcessor
    (
        List<UsenetGetFileInfosStep.FileInfo> fileInfos,
        INntpClient usenetClient,
        CancellationToken ct
    )
    {
        _fileInfos = fileInfos;
        _usenetClient = usenetClient;
        _ct = ct;
    }

    public override async Task<UsenetBaseProcessor.Result?> ProcessAsync()
    {
        var sortedFileInfos = _fileInfos.OrderBy(f => GetPartNumber(f.FileName)).ToList();
        var fileParts = new List<UsenetMultipartFile.FilePart>();
        foreach (var fileInfo in sortedFileInfos)
        {
            var partSize = fileInfo.FileSize ?? await _usenetClient
                .GetFileSizeAsync(fileInfo.NzbFile, _ct)
                .ConfigureAwait(false);

            fileParts.Add(new UsenetMultipartFile.FilePart
            {
                SegmentIds = fileInfo.NzbFile.GetSegmentIds(),
                SegmentIdByteRange = LongRange.FromStartAndSize(0, partSize),
                FilePartByteRange = LongRange.FromStartAndSize(0, partSize),
            });
        }

        return new Result
        {
            Filename = GetBaseName(sortedFileInfos.First().FileName),
            Parts = fileParts,
            ReleaseDate = sortedFileInfos.First().ReleaseDate,
        };
    }

    private static String GetBaseName(String filename)
    {
        var extensionIndex = filename.LastIndexOf(".mkv", StringComparison.Ordinal);
        return filename[..(extensionIndex + 4)];
    }

    private static Int32 GetPartNumber(String filename)
    {
        var match = Regex.Match(filename, @"\.mkv\.(\d+)?$", RegexOptions.IgnoreCase);
        return String.IsNullOrEmpty(match.Groups[1].Value) ? -1 : Int32.Parse(match.Groups[1].Value);
    }

    public new class Result : UsenetBaseProcessor.Result
    {
        public required String Filename { get; init; }
        public required List<UsenetMultipartFile.FilePart> Parts { get; init; } = [];
        public required DateTimeOffset ReleaseDate { get; init; }
    }
}
