using System;
using System.Linq;
using System.Web.Http;
using NSwag;
using NSwag.Generation.Processors.Contexts;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class PanelOpenApiAuthorizationRule
    {
        public static void Apply(OperationProcessorContext context)
        {
            if (!RequiresAuthorization(context)) return;

            context.OperationDescription.Operation.Security =
                new System.Collections.Generic.List<OpenApiSecurityRequirement>();
            context.OperationDescription.Operation.Security.Add(
                new OpenApiSecurityRequirement
                {
                    { "Bearer", Array.Empty<string>() }
                });
        }

        private static bool RequiresAuthorization(OperationProcessorContext context)
        {
            if (context.MethodInfo == null || context.ControllerType == null)
                return false;

            if (context.MethodInfo.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any())
                return false;

            return context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any()
                || context.ControllerType.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any();
        }
    }
}
