using System.Collections;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace RdtClient.Service.Services.Usenet.Extensions;

public static class UsenetSevenZipArchiveEntryExtensions
{
    public static CompressionType GetCompressionType(this SevenZipArchiveEntry entry)
    {
        try
        {
            return entry.CompressionType;
        }
        catch (NotImplementedException)
        {
            var coders = entry.GetCoders();
            var compressionMethodId = GetCoderMethodId(coders?.FirstOrDefault());
            return compressionMethodId == 0 ? CompressionType.None
                : compressionMethodId == 116459265 ? CompressionType.None
                : CompressionType.Unknown;
        }
    }

    public static Byte[]? GetAesCoderInfoProps(this SevenZipArchiveEntry entry)
    {
        const UInt64 aesMethodId = 0x06F10701;
        return (Byte[]?)entry
            .GetCoders()
            ?.FirstOrDefault(x => GetCoderMethodId(x) == aesMethodId)
            ?.GetReflectionField("_props");
    }

    public static Int64 GetFolderStartByteOffset(this SevenZipArchiveEntry entry)
    {
        var filePart = entry.GetReflectionProperty("FilePart");
        var folder = filePart?.GetReflectionProperty("Folder");
        var database = filePart?.GetReflectionField("_database");
        var folderFirstPackStreamId = (Int32)folder?.GetReflectionField("_firstPackStreamId")!;
        var databaseDataStartPosition = (Int64)database?.GetReflectionField("_dataStartPosition")!;
        var databasePackStreamStartPositions = (List<Int64>)database?.GetReflectionField("_packStreamStartPositions")!;
        return databaseDataStartPosition + databasePackStreamStartPositions[folderFirstPackStreamId];
    }

    private static IEnumerable<Object?>? GetCoders(this SevenZipArchiveEntry entry)
    {
        var coders = (IEnumerable?)entry
            .GetFolder()
            ?.GetReflectionField("_coders");
        return coders?.Cast<Object?>();
    }

    private static Object? GetFolder(this SevenZipArchiveEntry entry)
    {
        return entry
            .GetReflectionProperty("FilePart")
            ?.GetReflectionProperty("Folder");
    }

    private static UInt64? GetCoderMethodId(Object? coder)
    {
        return (UInt64?)coder
            ?.GetReflectionField("_methodId")
            ?.GetReflectionField("_id");
    }
}
