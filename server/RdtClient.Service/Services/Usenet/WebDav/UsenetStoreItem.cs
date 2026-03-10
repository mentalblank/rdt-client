using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.Streams;
using RdtClient.Service.Services.Usenet.WebDav.Base;
using RdtClient.Service.Services.Usenet.Extensions;
using Microsoft.EntityFrameworkCore;
using RdtClient.Data.Data;

namespace RdtClient.Service.Services.Usenet.WebDav;

public class UsenetStoreItem(UsenetDavItem davItem, DataContext dataContext, INntpClient usenetClient) : BaseStoreReadonlyItem
{
    public override String Name => davItem.Name;
    public override String UniqueKey => davItem.Id.ToString();
    public override Int64 FileSize => davItem.FileSize ?? 0;
    public override DateTime CreatedAt => davItem.CreatedAt;

    public override async Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        var settings = Settings.Get.Usenet;
        var articleBufferSize = settings.ArticleBufferSize;

        if (Path.GetExtension(davItem.Name).ToLower() == ".par2" && Settings.Get.WebDav.PreviewPar2Files)
        {
            var baseStream = await GetBaseStream(cancellationToken);
            var fileDescriptors = await Par2.Par2.ReadFileDescriptions(baseStream, cancellationToken).GetAllAsync(cancellationToken);
            var json = RdtClient.Service.Services.Usenet.Extensions.UsenetObjectExtensions.ToIndentedJson(fileDescriptors);
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        }

        return await GetBaseStream(cancellationToken);
    }

    private async Task<Stream> GetBaseStream(CancellationToken cancellationToken)
    {
        var settings = Settings.Get.Usenet;
        var articleBufferSize = settings.ArticleBufferSize;

        switch (davItem.Type)
        {
            case UsenetDavItem.UsenetItemType.NzbFile:
            {
                var file = await dataContext.UsenetNzbFiles.FirstOrDefaultAsync(x => x.Id == davItem.Id, cancellationToken);
                if (file == null) throw new FileNotFoundException($"Could not find nzb file metadata for {davItem.Name}");
                return usenetClient.GetFileStream(file.SegmentIdList, FileSize, articleBufferSize);
            }
            case UsenetDavItem.UsenetItemType.RarFile:
            {
                var file = await dataContext.UsenetRarFiles.FirstOrDefaultAsync(x => x.Id == davItem.Id, cancellationToken);
                if (file == null) throw new FileNotFoundException($"Could not find rar file metadata for {davItem.Name}");
                return new UsenetMultipartFileStream(
                    file.ToUsenetMultipartFileMeta().FileParts,
                    usenetClient,
                    articleBufferSize
                );
            }
            case UsenetDavItem.UsenetItemType.MultipartFile:
            {
                var file = await dataContext.UsenetMultipartFiles.FirstOrDefaultAsync(x => x.Id == davItem.Id, cancellationToken);
                if (file == null) throw new FileNotFoundException($"Could not find multipart file metadata for {davItem.Name}");
                var metadata = file.MetadataObject;
                if (metadata == null) throw new InvalidDataException($"Invalid metadata for multipart file {davItem.Name}");
                
                var packedStream = new UsenetMultipartFileStream(
                    metadata.FileParts,
                    usenetClient,
                    articleBufferSize
                );
                
                return metadata.AesParams != null
                    ? new AesDecoderStream(packedStream, metadata.AesParams)
                    : packedStream;
            }
            default:
                throw new NotSupportedException($"Streaming not supported for item type {davItem.Type}");
        }
    }
}
