using RdtClient.Service.Services.Usenet.Exceptions;

namespace RdtClient.Service.Services.Usenet.Extensions;

public static class UsenetExceptionExtensions
{
    public static Boolean IsRetryableDownloadException(this Exception exception)
    {
        return exception is RetryableDownloadException;
    }

    public static Boolean IsNonRetryableDownloadException(this Exception exception)
    {
        return exception is NonRetryableDownloadException
            or SharpCompress.Common.InvalidFormatException;
    }

    public static Boolean IsCancellationException(this Exception exception)
    {
        return exception is TaskCanceledException or OperationCanceledException;
    }

    public static Boolean TryGetCausingException<T>(this Exception exception, out T? exceptionType) where T : Exception
    {
        ArgumentNullException.ThrowIfNull(exception);
        var current = exception;

        while (current != null)
        {
            if (current is T matching)
            {
                exceptionType = matching;
                return true;
            }

            current = current.InnerException;
        }

        exceptionType = null;
        return false;
    }
}
