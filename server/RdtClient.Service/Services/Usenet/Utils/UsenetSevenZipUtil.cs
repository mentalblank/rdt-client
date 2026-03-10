using RdtClient.Service.Services.Usenet.Extensions;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.Streams;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace RdtClient.Service.Services.Usenet.Utils;

public static class UsenetSevenZipUtil
{
    public static async Task<List<SevenZipEntry>> GetSevenZipEntriesAsync
    (
        Stream stream,
        String? password,
        CancellationToken ct
    )
    {
        await using var cancellableStream = new CancellableStream(stream, ct);
        return await Task.Run(() => GetSevenZipEntries(cancellableStream, password), ct).ConfigureAwait(false);
    }

    public static List<SevenZipEntry> GetSevenZipEntries(Stream stream, String? password = null)
    {
        using var archive = SevenZipArchive.Open(stream, new ReaderOptions { Password = password });
        return archive.Entries
            .Where(x => !x.IsDirectory)
            .Select((entry, index) => new SevenZipEntry(entry, archive, index, password))
            .ToList();
    }

    public class SevenZipEntry(SevenZipArchiveEntry entry, SevenZipArchive archive, Int32 index, String? password)
    {
        public SevenZipArchiveEntry Entry => entry;
        public String PathWithinArchive { get; } = entry.Key!;
        public CompressionType CompressionType { get; } = entry.GetCompressionType();
        public Boolean IsEncrypted { get; } = entry.IsEncrypted;
        public Boolean IsSolid { get; } = entry.IsSolid;

        public AesParams? AesParams { get; } =
            AesParams.FromCoderInfo(entry.GetAesCoderInfoProps(), password, entry.Size);

        public Int64 FolderStartByteOffset { get; } = entry.GetFolderStartByteOffset();

        public LongRange ByteRangeWithinArchive { get; } =
            LongRange.FromStartAndSize(archive.GetEntryStartByteOffset(index), archive.GetPackSize(index));
    }
}
