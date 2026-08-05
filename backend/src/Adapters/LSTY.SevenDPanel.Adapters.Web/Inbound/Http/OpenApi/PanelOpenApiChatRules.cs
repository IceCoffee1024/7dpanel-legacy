using System;
using NSwag;
using NSwag.Generation.Processors.Contexts;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class PanelOpenApiChatRules
    {
        public static void Apply(OperationProcessorContext context)
        {
            var path = context.OperationDescription.Path;
            if (!path.StartsWith("/api/v1/chat/", StringComparison.Ordinal)) return;

            var method = context.OperationDescription.Method;
            var operation = context.OperationDescription.Operation;
            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/chat/messages/recent", StringComparison.Ordinal))
            {
                operation.OperationId = "Chat_GetRecentMessages";
                operation.Description = "Returns Owner-only recent chat messages from the current process event window.";
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "limit", "Number of recent chat messages from 1 through 500; defaults to 200.");
                return;
            }

            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/chat/messages", StringComparison.Ordinal))
            {
                operation.OperationId = "Chat_GetMessages";
                operation.Description = "Returns Owner-only persisted chat history using a filter-bound opaque cursor.";
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "cursor", "Opaque URL-safe cursor returned by the preceding page and bound to the active filters.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "limit", "Page size from 1 through 200; defaults to 100.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "crossplatformId", "Exact sender cross-platform identity filter.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "senderName", "Sender name search text.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "chatType", "Exact chat channel name.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "sourceKind", "Exact sender source kind.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "startUtc", "Optional inclusive UTC start time in round-trip format.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "endUtc", "Optional inclusive UTC end time in round-trip format.");
                return;
            }

            if (method == OpenApiOperationMethod.Post &&
                (string.Equals(path, "/api/v1/chat/messages/global", StringComparison.Ordinal) ||
                 string.Equals(path, "/api/v1/chat/messages/private", StringComparison.Ordinal)))
            {
                operation.OperationId = path.EndsWith("/global", StringComparison.Ordinal)
                    ? "Chat_SendGlobalMessage"
                    : "Chat_SendPrivateMessage";
                operation.Description = path.EndsWith("/global", StringComparison.Ordinal)
                    ? "Queues an Owner-only global chat message for execution on the game thread."
                    : "Queues an Owner-only private chat message for an online cross-platform identity.";
                PanelOpenApiOperationRuleHelpers.RemoveParameter(operation, "cancellationToken");
                PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "202", "The chat message was accepted for execution.");
                return;
            }

            if (string.Equals(path, "/api/v1/chat/settings", StringComparison.Ordinal))
            {
                operation.OperationId = method == OpenApiOperationMethod.Get
                    ? "Chat_GetSettings"
                    : method == OpenApiOperationMethod.Put
                        ? "Chat_UpdateSettings"
                        : "Chat_ResetSettings";
                operation.Description = method == OpenApiOperationMethod.Get
                    ? "Returns Owner-only chat settings."
                    : method == OpenApiOperationMethod.Put
                        ? "Validates, persists, and applies Owner-only chat settings."
                        : "Restores and applies the default Owner-only chat settings.";
                return;
            }

            if (string.Equals(path, "/api/v1/chat/colored/settings", StringComparison.Ordinal))
            {
                operation.OperationId = method == OpenApiOperationMethod.Get
                    ? "Chat_GetColoredSettings"
                    : method == OpenApiOperationMethod.Put
                        ? "Chat_UpdateColoredSettings"
                        : "Chat_ResetColoredSettings";
                operation.Description = method == OpenApiOperationMethod.Get
                    ? "Returns Owner-only colored chat settings."
                    : method == OpenApiOperationMethod.Put
                        ? "Validates, persists, and applies Owner-only colored chat settings."
                        : "Restores and applies the default Owner-only colored chat settings.";
                return;
            }

            if (string.Equals(path, "/api/v1/chat/colored/profiles", StringComparison.Ordinal))
            {
                if (method == OpenApiOperationMethod.Get)
                {
                    operation.OperationId = "Chat_GetColoredProfiles";
                    operation.Description = "Returns Owner-only colored chat profiles using a filter-bound opaque cursor.";
                    PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "cursor", "Opaque URL-safe cursor returned by the preceding page and bound to the active filters.");
                    PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "limit", "Page size from 1 through 100; defaults to 50.");
                    PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "crossplatformId", "Cross-platform identity search text.");
                    PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "customName", "Custom display name search text.");
                    PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "nameColor", "Exact normalized name color filter.");
                    PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "textColor", "Exact normalized text color filter.");
                    PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "createdAfterUtc", "Optional inclusive UTC creation start time in round-trip format.");
                    PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(operation, "createdBeforeUtc", "Optional inclusive UTC creation end time in round-trip format.");
                }
                else
                {
                    operation.OperationId = "Chat_CreateColoredProfile";
                    operation.Description = "Creates an Owner-only colored chat profile for a unique cross-platform identity.";
                    PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "201", "The colored chat profile was created.");
                }
                return;
            }

            if (string.Equals(path, "/api/v1/chat/colored/profiles/{crossplatformId}", StringComparison.Ordinal))
            {
                if (method == OpenApiOperationMethod.Put)
                {
                    operation.OperationId = "Chat_UpdateColoredProfile";
                    operation.Description = "Updates and applies an existing Owner-only colored chat profile.";
                }
                else
                {
                    operation.OperationId = "Chat_DeleteColoredProfile";
                    operation.Description = "Deletes an existing Owner-only colored chat profile.";
                    operation.Responses.Remove("200");
                    operation.Responses["204"] = new OpenApiResponse
                    {
                        Description = "The colored chat profile was deleted."
                    };
                }
            }
        }
    }
}
