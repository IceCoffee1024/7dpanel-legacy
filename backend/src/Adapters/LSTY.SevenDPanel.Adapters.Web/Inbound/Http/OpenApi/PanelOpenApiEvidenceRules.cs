using System;
using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors.Contexts;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class PanelOpenApiEvidenceRules
    {
        public static void DescribeEvidenceFoundation(OperationProcessorContext context)
        {
            var operation = context.OperationDescription.Operation;
            var path = context.OperationDescription.Path;
            var method = context.OperationDescription.Method;

            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/audit", StringComparison.Ordinal))
            {
                operation.OperationId = "listAuditEntries";
                operation.Description =
                    "Returns the Owner-only unified audit summary projection without sensitive source payloads.";
                return;
            }

            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/game-events", StringComparison.Ordinal))
            {
                operation.OperationId = "listGameEvents";
                operation.Description =
                    "Returns Owner-only game lifecycle events and separate evidence-gap metadata.";
                return;
            }

            if (string.Equals(path, "/api/v1/chat/mutes", StringComparison.Ordinal))
            {
                if (method == OpenApiOperationMethod.Get)
                {
                    operation.OperationId = "listChatMutes";
                    operation.Description = "Returns Owner-only active chat mutes using bounded keyset pagination.";
                }
                else if (method == OpenApiOperationMethod.Post)
                {
                    operation.OperationId = "createChatMute";
                    operation.Description = "Creates and applies an Owner-only permanent or temporary chat mute.";
                    PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "201", "The chat mute was created.");
                }
                return;
            }

            if (!string.Equals(path, "/api/v1/chat/mutes/{crossplatformId}", StringComparison.Ordinal))
                return;

            if (method == OpenApiOperationMethod.Put)
            {
                operation.OperationId = "updateChatMute";
                operation.Description = "Updates and applies an existing Owner-only chat mute.";
            }
            else if (method == OpenApiOperationMethod.Delete)
            {
                operation.OperationId = "releaseChatMute";
                operation.Description = "Releases an existing Owner-only chat mute.";
                PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "204", "The chat mute was released.");
            }
        }

        public static void DescribePlayerEvidenceActions(OperationProcessorContext context)
        {
            var path = context.OperationDescription.Path;
            var method = context.OperationDescription.Method;
            var operation = context.OperationDescription.Operation;

            if (method == OpenApiOperationMethod.Get &&
                path.StartsWith("/api/v1/players/{crossplatformId}/", StringComparison.Ordinal))
            {
                if (path.EndsWith("/profile", StringComparison.Ordinal))
                {
                    operation.OperationId = "PlayerEvidence_GetProfile";
                    operation.Description =
                        "Returns the Owner-only sectioned player profile with independent observation and gap metadata.";
                    return;
                }

                if (path.EndsWith("/inventory-snapshots", StringComparison.Ordinal))
                    operation.OperationId = "PlayerEvidence_GetInventorySnapshots";
                else if (path.EndsWith("/inventory-diffs", StringComparison.Ordinal))
                    operation.OperationId = "PlayerEvidence_GetInventoryDiffs";
                else if (path.EndsWith("/skills", StringComparison.Ordinal))
                    operation.OperationId = "PlayerEvidence_GetSkills";
                else
                    return;

                operation.Description =
                    "Returns Owner-only bounded player evidence using a player-bound opaque keyset cursor and explicit gap metadata.";
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "pageSize",
                    "Page size from 1 through 200; defaults to 50.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "cursor",
                    "Opaque URL-safe cursor returned by the preceding page and bound to this cross-platform identity.");
                return;
            }

            if (!path.StartsWith("/api/v1/player-actions", StringComparison.Ordinal)) return;
            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/player-actions/{operationId}", StringComparison.Ordinal))
            {
                operation.OperationId = "PlayerActions_Get";
                operation.Description =
                    "Returns the fixed persisted result of one Owner-only player action operation.";
                return;
            }
            if (method != OpenApiOperationMethod.Post) return;

            string requestSchemaName;
            if (string.Equals(path, "/api/v1/player-actions/grant-item", StringComparison.Ordinal))
            {
                operation.OperationId = "PlayerActions_GrantItem";
                requestSchemaName = "GrantItemHttpRequest";
            }
            else if (string.Equals(path, "/api/v1/player-actions/remove-item", StringComparison.Ordinal))
            {
                operation.OperationId = "PlayerActions_RemoveItem";
                requestSchemaName = "RemoveItemHttpRequest";
            }
            else if (string.Equals(path, "/api/v1/player-actions/reset-skills", StringComparison.Ordinal))
            {
                operation.OperationId = "PlayerActions_ResetSkills";
                requestSchemaName = "ResetSkillsHttpRequest";
            }
            else if (string.Equals(path, "/api/v1/player-actions/clear-inventory", StringComparison.Ordinal))
            {
                operation.OperationId = "PlayerActions_ClearInventory";
                requestSchemaName = "ClearInventoryHttpRequest";
            }
            else if (string.Equals(path, "/api/v1/player-actions/reset-player-data", StringComparison.Ordinal))
            {
                operation.OperationId = "PlayerActions_ResetPlayerData";
                requestSchemaName = "ResetPlayerDataHttpRequest";
            }
            else
                return;

            operation.Description =
                "Submits one typed Owner-only player action. Operator and correlation identity are derived from the authenticated request; pending results use 202 and known terminal results use 200.";
            PromoteRequestBodySchema(context, requestSchemaName);
            PanelOpenApiOperationRuleHelpers.RemoveParameter(operation, "cancellationToken");
            AddAcceptedResponse(operation);
        }

        private static void PromoteRequestBodySchema(
            OperationProcessorContext context,
            string schemaName)
        {
            var requestBody = context.OperationDescription.Operation.RequestBody;
            if (requestBody == null ||
                !requestBody.Content.TryGetValue("application/json", out var mediaType) ||
                mediaType.Schema == null)
            {
                return;
            }

            if (!context.Document.Components.Schemas.TryGetValue(schemaName, out var schema))
            {
                schema = mediaType.Schema.ActualSchema;
                context.SchemaResolver.AppendSchema(schema, schemaName);
            }
            mediaType.Schema = new JsonSchema { Reference = schema };
        }

        private static void AddAcceptedResponse(OpenApiOperation operation)
        {
            if (!operation.Responses.TryGetValue("200", out var success)) return;
            var accepted = new OpenApiResponse
            {
                Description = "The player action is pending execution."
            };
            foreach (var content in success.Content)
                accepted.Content[content.Key] = content.Value;
            operation.Responses["202"] = accepted;
        }
    }
}
