namespace RdtClient.Service.Services.Usenet.Exceptions;

public class NonRetryableDownloadException(String message) : Exception(message)
{
}
