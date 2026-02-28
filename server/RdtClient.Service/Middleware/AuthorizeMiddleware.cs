using System.Net;
using Microsoft.AspNetCore.Http;

namespace RdtClient.Service.Middleware;

public class AuthorizeMiddleware(RequestDelegate next)
{
    /// <summary>
    ///     Return a 403 instead of a 401, it's quirk that QBittorrent has.
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task Invoke(HttpContext context)
    {
        if (context.Request.Path.Value != null && 
            context.Request.Path.Value.StartsWith("/dav/usenet", StringComparison.OrdinalIgnoreCase))
        {
            Serilog.Log.Debug("AuthorizeMiddleware: Skipping WebDAV path {Path}", context.Request.Path.Value);
            await next(context);
            return;
        }

        await next(context);

        if (context.Response.StatusCode == (Int32)HttpStatusCode.Unauthorized)
        {
            Serilog.Log.Debug("AuthorizeMiddleware: Converting 401 to 403 for {Path}", context.Request.Path.Value);
            context.Response.StatusCode = (Int32)HttpStatusCode.Forbidden;
        }
    }
}
