namespace RdtClient.Service.Services.Usenet.Extensions;

public static class UsenetGuidExtensions
{
    public static String GetFiveLengthPrefix(this Guid guid)
    {
        return guid.ToString()[..5];
    }
}
