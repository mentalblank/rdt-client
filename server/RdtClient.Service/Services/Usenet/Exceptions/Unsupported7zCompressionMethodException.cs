namespace RdtClient.Service.Services.Usenet.Exceptions;

public class Unsupported7zCompressionMethodException(String message) : NonRetryableDownloadException(message);
