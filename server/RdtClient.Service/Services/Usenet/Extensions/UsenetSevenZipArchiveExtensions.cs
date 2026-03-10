using SharpCompress.Archives.SevenZip;

namespace RdtClient.Service.Services.Usenet.Extensions;

public static class UsenetSevenZipArchiveExtensions
{
    public static Int64 GetEntryStartByteOffset(this SevenZipArchive archive, Int32 index)
    {
        var database = archive.GetReflectionField("_database");
        var dataStartPosition = (Int64?)database?.GetReflectionField("_dataStartPosition");
        var packStreamStartPositions = (List<Int64>?)database?.GetReflectionField("_packStreamStartPositions");
        return dataStartPosition!.Value + packStreamStartPositions![index];
    }

    public static Int64 GetPackSize(this SevenZipArchive archive, Int32 index)
    {
        var database = archive.GetReflectionField("_database");
        var packSizes = (List<Int64>?)database?.GetReflectionField("_packSizes");
        return packSizes![index];
    }
}
