using System;
using NSwag;
using NSwag.Generation.Processors.Contexts;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class PanelOpenApiConsoleRules
    {
        public static void Apply(OperationProcessorContext context)
        {
            if (context.OperationDescription.Method != OpenApiOperationMethod.Get) return;

            var path = context.OperationDescription.Path;
            var operation = context.OperationDescription.Operation;
            if (string.Equals(path, "/api/v1/console/logs/recent", StringComparison.Ordinal))
            {
                operation.OperationId = "ConsoleLogs_GetRecent";
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "limit",
                    "Number of recent console logs from 1 through 5000; defaults to 1000.");
                return;
            }

            if (string.Equals(path, "/api/v1/console/commands/catalog", StringComparison.Ordinal))
                operation.OperationId = "ConsoleCommands_GetCatalog";
        }
    }
}
