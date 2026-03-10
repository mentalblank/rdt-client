namespace RdtClient.Service.Services.Usenet.Extensions;

public static class UsenetStringExtensions
{
    public static String RemovePrefix(this String text, String prefix)
    {
        return text.StartsWith(prefix) ? text.Substring(prefix.Length) : text;
    }
}
