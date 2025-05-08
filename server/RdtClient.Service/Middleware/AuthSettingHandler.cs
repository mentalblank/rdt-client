using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using RdtClient.Data.Enums;
using RdtClient.Service.Services;

namespace RdtClient.Service.Middleware;

public class AuthSettingRequirement : IAuthorizationRequirement { }

public class AuthSettingHandler : AuthorizationHandler<AuthSettingRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AuthSettingRequirement requirement)
    {
        var settings = Settings.Get.General;

        if (settings.AuthenticationType == AuthenticationType.None)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (settings.AuthenticationType == AuthenticationType.UserNamePassword || settings.AuthenticationType == AuthenticationType.UserNamePasswordClientApiKey)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }

        if ((settings.AuthenticationType == AuthenticationType.UserNamePasswordClientApiKey || settings.AuthenticationType == AuthenticationType.UserNamePassword) && context.Resource is HttpContext httpContext)
        {
            var queryApiKey = httpContext.Request.Query["apikey"].FirstOrDefault();
            var headerApiKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
            var providedKey = queryApiKey ?? headerApiKey;
            if (!string.IsNullOrWhiteSpace(providedKey) && providedKey == settings.ClientApiKey)
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }
        return Task.CompletedTask;
    }
}
