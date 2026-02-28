namespace RdtClient.Service.Services.Usenet.Exceptions;

public class RetryableDownloadException(String message, Exception? innerException = null)
    : Exception(message, innerException)
{
}
