using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using RdtClient.Data.Enums;
using RdtClient.Service.Services;

namespace RdtClient.Service.Middleware;

public class AuthSettingRequirement : IAuthorizationRequirement
{
}

public class AuthSettingHandler : AuthorizationHandler<AuthSettingRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AuthSettingRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
        }

        if (Settings.Get.General.AuthenticationType == AuthenticationType.None)
        {
            context.Succeed(requirement);
        }

        if ((Settings.Get.General.AuthenticationType == AuthenticationType.UserNamePasswordClientApiKey || Settings.Get.General.AuthenticationType == AuthenticationType.UserNamePassword) &&
            context.Resource is HttpContext httpContext)
        {
            var queryApiKey = httpContext.Request.Query["apikey"].FirstOrDefault();
            var headerApiKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
            var providedKey = queryApiKey ?? headerApiKey;

            if (!String.IsNullOrWhiteSpace(providedKey) && providedKey == Settings.Get.General.ClientApiKey)
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}
