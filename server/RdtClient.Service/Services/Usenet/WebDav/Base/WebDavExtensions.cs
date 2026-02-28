using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using NWebDav.Server.Helpers;

namespace RdtClient.Service.Services.Usenet.WebDav.Base;

public static class WebDavExtensions
{
    public record Range(Int64? Start, Int64? End);

    public static Range? GetRange(this HttpRequest request)
    {
        var rangeHeader = request.Headers[HeaderNames.Range].ToString();
        if (String.IsNullOrEmpty(rangeHeader)) return null;

        var headerValue = RangeHeaderValue.Parse(rangeHeader);
        var range = headerValue.Ranges.FirstOrDefault();
        if (range == null) return null;

        return new Range(range.From, range.To);
    }

    public static Uri GetUri(this HttpRequest request)
    {
        return new Uri($"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}");
    }
}
