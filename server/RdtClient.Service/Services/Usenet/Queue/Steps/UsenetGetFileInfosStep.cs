using System.Security.Cryptography;
using RdtClient.Service.Services.Usenet.Models;
using RdtClient.Service.Services.Usenet.Par2.Packets;
using RdtClient.Service.Services.Usenet.Utils;
using RdtClient.Data.Models.Data;

namespace RdtClient.Service.Services.Usenet.Queue.Steps;

public static class UsenetGetFileInfosStep
{
    public static List<FileInfo> GetFileInfos
    (
        List<UsenetFetchFirstSegmentsStep.NzbFileWithFirstSegment> files,
        List<FileDesc> par2FileDescriptors
    )
    {
        using var md5 = MD5.Create();
        var hashToFileDescMap = GetHashToFileDescMap(par2FileDescriptors);
        var filesInfos = files
            .Select(x => GetFileInfo(x, hashToFileDescMap, md5))
            .ToList();

        return filesInfos;
    }

    private static Dictionary<String, FileDesc> GetHashToFileDescMap(List<FileDesc> par2FileDescriptors)
    {
        var hashToFileDescMap = new Dictionary<String, FileDesc>();
        foreach (var descriptor in par2FileDescriptors)
        {
            if (descriptor.File16kHash == null) continue;
            var hash = BitConverter.ToString(descriptor.File16kHash);
            hashToFileDescMap[hash] = descriptor;
        }

        return hashToFileDescMap;
    }

    private static FileInfo GetFileInfo(
        UsenetFetchFirstSegmentsStep.NzbFileWithFirstSegment file,
        Dictionary<String, FileDesc> hashToFiledescMap,
        MD5 md5
    )
    {
        var fileDesc = GetMatchingFileDescriptor(file, hashToFiledescMap, md5);
        var subjectFileName = file.NzbFile.GetSubjectFileName();
        var headerFileName = file.Header?.FileName ?? "";
        var par2FileName = fileDesc?.FileName ?? "";
        
        var filename = new List<(String? FileName, Int32 Priority)>
        {
            (FileName: par2FileName, Priority: GetFilenamePriority(par2FileName, 3)),
            (FileName: subjectFileName, Priority: GetFilenamePriority(subjectFileName, 2)),
            (FileName: headerFileName, Priority: GetFilenamePriority(headerFileName, 1)),
        }.Where(x => x.FileName is not null).MaxBy(x => x.Priority).FileName ?? "";

        return new FileInfo()
        {
            NzbFile = file.NzbFile,
            FileName = filename,
            ReleaseDate = file.ReleaseDate,
            FileSize = (Int64?)fileDesc?.FileLength,
            IsRar = file.HasRar4Magic() || file.HasRar5Magic(),
        };
    }

    private static Int32 GetFilenamePriority(String? filename, Int32 startingPriority)
    {
        var priority = startingPriority;
        if (String.IsNullOrWhiteSpace(filename)) return priority - 5000;
        if (ObfuscationUtil.IsProbablyObfuscated(filename)) priority -= 1000;
        if (FilenameUtil.IsImportantFileType(filename)) priority += 50;
        if (Path.GetExtension(filename).TrimStart('.').Length is >= 2 and <= 4) priority += 10;
        return priority;
    }

    private static FileDesc? GetMatchingFileDescriptor
    (
        UsenetFetchFirstSegmentsStep.NzbFileWithFirstSegment file,
        Dictionary<String, FileDesc> hashToFiledescMap,
        MD5 md5
    )
    {
        var hash = !file.MissingFirstSegment && file.First16KB != null ? BitConverter.ToString(md5.ComputeHash(file.First16KB)) : "";
        var fileDesc = hashToFiledescMap.GetValueOrDefault(hash);
        if (fileDesc is null) return null;
        
        return IsCloseToYencodedSize((Int64)fileDesc.FileLength, file.NzbFile.GetTotalYencodedSize())
            ? fileDesc
            : null;
    }

    private static Boolean IsCloseToYencodedSize(Int64 fileSize, Int64 totalYencodedSize)
    {
        var range = new LongRange(95 * totalYencodedSize / 100, totalYencodedSize);
        return range.Contains(fileSize);
    }

    public record FileInfo
    {
        public required NzbFile NzbFile { get; init; }
        public required String FileName { get; init; }
        public required DateTimeOffset ReleaseDate { get; init; }
        public required Int64? FileSize { get; init; }
        public required Boolean IsRar { get; init; }
    }
}
