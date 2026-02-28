namespace RdtClient.Service.Services.Usenet.Exceptions;

public class SeekPositionNotFoundException(String message) : NonRetryableDownloadException(message)
{
}
