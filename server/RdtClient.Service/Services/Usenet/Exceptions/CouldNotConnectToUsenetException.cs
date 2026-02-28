namespace RdtClient.Service.Services.Usenet.Exceptions;

public class CouldNotConnectToUsenetException(String message, Exception? innerException = null)
    : RetryableDownloadException(message, innerException)
{
}
