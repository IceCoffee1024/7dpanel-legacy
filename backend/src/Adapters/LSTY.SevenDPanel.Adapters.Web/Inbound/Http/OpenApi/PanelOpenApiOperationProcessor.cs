using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal sealed class PanelOpenApiOperationProcessor : IOperationProcessor
    {
        public bool Process(OperationProcessorContext context)
        {
            if (context.ControllerType == typeof(PlayerAuthenticationController) ||
                context.ControllerType == typeof(PlayerSessionController))
            {
                return false;
            }

            PanelOpenApiServerRules.DescribeServerEventStream(context);
            PanelOpenApiServerRules.DescribeApiKeyManagement(context);
            PanelOpenApiServerRules.DescribePlayerHistory(context);
            PanelOpenApiMapRules.Apply(context);
            PanelOpenApiGameResourceRules.Apply(context);
            PanelOpenApiServerRules.DescribeServerOperations(context);
            PanelOpenApiConsoleRules.Apply(context);
            PanelOpenApiChatRules.Apply(context);
            PanelOpenApiEvidenceRules.DescribeEvidenceFoundation(context);
            PanelOpenApiEvidenceRules.DescribePlayerEvidenceActions(context);
            PanelOpenApiJobsRules.Apply(context);
            PanelOpenApiResponseRules.Apply(context);
            PanelOpenApiAuthorizationRule.Apply(context);
            return true;
        }
    }
}
