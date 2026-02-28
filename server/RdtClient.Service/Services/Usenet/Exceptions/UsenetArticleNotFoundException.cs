namespace RdtClient.Service.Services.Usenet.Exceptions;

public class UsenetArticleNotFoundException(String segmentId)
    : NonRetryableDownloadException($"Article with message-id {segmentId} not found.")
{
    public String SegmentId => segmentId;
}
