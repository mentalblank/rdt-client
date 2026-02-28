using RdtClient.Service.Services.Usenet.Contexts;

namespace RdtClient.Service.Services.Usenet.Extensions;

public static class UsenetCancellationTokenExtensions
{
    public static CancellationTokenContext SetContext<T>(this CancellationToken ct, T? value)
    {
        return CancellationTokenContext.SetContext(ct, value);
    }

    public static T? GetContext<T>(this CancellationToken ct)
    {
        return CancellationTokenContext.GetContext<T>(ct);
    }
}
