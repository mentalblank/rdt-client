using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;
using RdtClient.Service.Services;

namespace RdtClient.Service.Middleware;

public class WebDavAuthorizeMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context)
    {
        Serilog.Log.Debug("WebDavAuthorizeMiddleware: Entry for {Path}", context.Request.Path.Value);
        if (context.Request.Path.Value != null && 
            context.Request.Path.Value.StartsWith("/dav/usenet", StringComparison.OrdinalIgnoreCase))
        {
            var settings = Settings.Get.WebDav;

            if (!settings.Enabled)
            {
                Serilog.Log.Warning("WebDAV request received but WebDAV is disabled in settings.");
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!String.IsNullOrWhiteSpace(settings.Username) && !String.IsNullOrWhiteSpace(settings.Password))
            {
                var authHeader = context.Request.Headers.Authorization.ToString();

                if (String.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                {
                    Serilog.Log.Debug("WebDAV request missing or invalid Basic Auth header.");
                    context.Response.Headers.Append("WWW-Authenticate", "Basic realm=\"rdt-client\"");
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                try
                {
                    var authHeaderValue = AuthenticationHeaderValue.Parse(authHeader);
                    var credentialBytes = Convert.FromBase64String(authHeaderValue.Parameter ?? "");
                    var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);
                    var username = credentials[0];
                    var password = credentials[1];

                    if (username != settings.Username || password != settings.Password)
                    {
                        Serilog.Log.Warning("WebDAV login failed for user {username}.", username);
                        context.Response.Headers.Append("WWW-Authenticate", "Basic realm=\"rdt-client\"");
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "Error parsing WebDAV auth header.");
                    context.Response.Headers.Append("WWW-Authenticate", "Basic realm=\"rdt-client\"");
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
            }
        }

        await next(context);
    }
}
