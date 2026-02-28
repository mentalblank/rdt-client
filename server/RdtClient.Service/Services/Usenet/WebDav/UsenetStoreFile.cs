using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.Streams;
using RdtClient.Service.Services.Usenet.WebDav.Base;

namespace RdtClient.Service.Services.Usenet.WebDav;

public class UsenetStoreFile(UsenetFile file, INntpClient usenetClient, String? nameOverride = null) : BaseStoreReadonlyItem
{
    public override String Name => nameOverride ?? Path.GetFileName(file.Path);
    public override String UniqueKey => file.UsenetFileId.ToString();
    public override Int64 FileSize => file.Size;
    public override DateTime CreatedAt => file.UsenetJob.Added.DateTime;

    public override Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        var settings = Settings.Get.Usenet;
        var articleBufferSize = settings.ArticleBufferSize;
        var stream = new NzbFileStream(file.SegmentIdList, file.Size, usenetClient, articleBufferSize);
        return Task.FromResult<Stream>(stream);
    }
}
