namespace RdtClient.Service.Services.Usenet.Exceptions;

public class CouldNotLoginToUsenetException(String message, Exception? innerException = null)
    : RetryableDownloadException(message, innerException)
{
}
