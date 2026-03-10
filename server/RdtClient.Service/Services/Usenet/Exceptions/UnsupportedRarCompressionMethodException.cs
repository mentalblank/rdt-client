namespace RdtClient.Service.Services.Usenet.Exceptions;

public class UnsupportedRarCompressionMethodException(String message) : NonRetryableDownloadException(message);
