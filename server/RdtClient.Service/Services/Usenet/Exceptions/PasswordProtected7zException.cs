namespace RdtClient.Service.Services.Usenet.Exceptions;

public class PasswordProtected7zException(String message) : NonRetryableDownloadException(message);
