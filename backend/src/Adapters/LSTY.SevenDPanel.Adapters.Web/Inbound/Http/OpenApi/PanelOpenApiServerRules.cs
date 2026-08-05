using System;
using System.Linq;
using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors.Contexts;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class PanelOpenApiServerRules
    {
        public static void DescribeServerEventStream(OperationProcessorContext context)
        {
            if (!string.Equals(
                    context.OperationDescription.Path,
                    "/api/v1/events/stream",
                    StringComparison.Ordinal) ||
                context.OperationDescription.Method != OpenApiOperationMethod.Get)
            {
                return;
            }

            var operation = context.OperationDescription.Operation;
            var cancellationToken = operation.Parameters.FirstOrDefault(parameter =>
                string.Equals(parameter.Name, "cancellationToken", StringComparison.OrdinalIgnoreCase));
            if (cancellationToken != null)
                operation.Parameters.Remove(cancellationToken);

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "Last-Event-ID",
                Kind = OpenApiParameterKind.Header,
                IsRequired = false,
                Description = "Last processed server event sequence.",
                CustomSchema = new JsonSchema { Type = JsonObjectType.String }
            });
            operation.Description =
                "Opens a long-lived named event stream. Errors before the response starts use Problem Details; " +
                "after the response starts they cannot be rewritten as JSON.";

            if (!operation.Responses.TryGetValue("200", out var response))
            {
                response = new OpenApiResponse { Description = "Server-sent event stream." };
                operation.Responses["200"] = response;
            }
            response.Content.Clear();
            response.Content["text/event-stream"] = new OpenApiMediaType
            {
                Schema = new JsonSchema { Type = JsonObjectType.String }
            };
        }

        public static void DescribeApiKeyManagement(OperationProcessorContext context)
        {
            var operation = context.OperationDescription.Operation;
            var path = context.OperationDescription.Path;
            var method = context.OperationDescription.Method;
            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/api-keys", StringComparison.Ordinal))
            {
                operation.Responses["200"] = OpenApiResponses.Json(
                    "API Key metadata for the authenticated subject.",
                    CreateApiKeyMetadataListSchema());
                return;
            }

            if (method == OpenApiOperationMethod.Post &&
                string.Equals(path, "/api/v1/api-keys", StringComparison.Ordinal))
            {
                operation.Description =
                    "Creates an API Key for the authenticated subject. " +
                    "Only a website Access Token can create API Keys. " +
                    "The complete API Key is returned only in this response.";
                operation.Responses.Remove("200");
                var response = OpenApiResponses.Json(
                    "A newly created API Key, including its one-time complete value.",
                    CreateCreatedApiKeySchema());
                response.Headers["Cache-Control"] = new OpenApiHeader
                {
                    Description = "Prevents storage of the one-time complete API Key.",
                    IsRequired = true,
                    Schema = new JsonSchema { Type = JsonObjectType.String },
                    Example = "no-store"
                };
                operation.Responses["201"] = response;
                return;
            }

            if (method == OpenApiOperationMethod.Delete &&
                string.Equals(path, "/api/v1/api-keys/{keyId}", StringComparison.Ordinal))
            {
                operation.Description =
                    "Revokes an API Key owned by the authenticated subject. " +
                    "Only a website Access Token can revoke API Keys.";
                operation.Responses.Remove("200");
                operation.Responses["204"] = new OpenApiResponse
                {
                    Description = "The API Key is revoked or was already revoked."
                };
            }
        }

        public static void DescribePlayerHistory(OperationProcessorContext context)
        {
            var operation = context.OperationDescription.Operation;
            var path = context.OperationDescription.Path;
            var method = context.OperationDescription.Method;
            if (method != OpenApiOperationMethod.Get ||
                !path.StartsWith("/api/v1/players/history", StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(path, "/api/v1/players/history", StringComparison.Ordinal))
            {
                operation.Description =
                    "Returns Owner-only historical player summaries from SQLite. " +
                    "The endpoint remains readable while the game is not ready.";
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "query",
                    "Optional player name or cross-platform identity search text.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "pageSize",
                    "Page size from 1 through 100; defaults to 50.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "cursor",
                    "Opaque URL-safe cursor returned by the preceding page.");
                return;
            }

            if (string.Equals(path, "/api/v1/players/history/{crossplatformId}", StringComparison.Ordinal))
            {
                operation.Description =
                    "Returns an Owner-only historical player summary and recorded gap totals.";
                return;
            }

            if (string.Equals(
                    path,
                    "/api/v1/players/history/{crossplatformId}/snapshots",
                    StringComparison.Ordinal))
            {
                operation.Description =
                    "Returns Owner-only retained historical snapshots in descending snapshot ID order.";
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "pageSize",
                    "Page size from 1 through 200; defaults to 100.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "beforeSnapshotId",
                    "Exclusive snapshot ID cursor returned by the preceding page.");
            }
        }

        public static void DescribeServerOperations(OperationProcessorContext context)
        {
            var path = context.OperationDescription.Path;
            if (context.OperationDescription.Method != OpenApiOperationMethod.Post ||
                (!string.Equals(
                     path,
                     "/api/v1/server-operations/restart",
                     StringComparison.Ordinal) &&
                 !string.Equals(
                     path,
                     "/api/v1/server-operations/shutdown",
                     StringComparison.Ordinal)))
            {
                return;
            }

            var operation = context.OperationDescription.Operation;
            operation.Responses.Remove("200");
            operation.Responses["202"] = OpenApiResponses.Json(
                "The server operation request was accepted.",
                CreateServerOperationResponseSchema(
                    string.Equals(path, "/api/v1/server-operations/restart", StringComparison.Ordinal)));
        }

        private static JsonSchema CreateServerOperationResponseSchema(bool restart)
        {
            var schema = new JsonSchema { Type = JsonObjectType.Object };
            schema.Properties["operationId"] = CreateStringProperty("Accepted operation identifier.");
            schema.Properties["code"] = CreateStringProperty("Accepted operation status.");
            schema.Properties["requestedAtUtc"] = CreateStringProperty("Request time in UTC.");
            schema.Properties[restart ? "scriptStartedAtUtc" : "acceptedAtUtc"] =
                CreateStringProperty("Acceptance time in UTC.");
            schema.Properties["auditStatus"] = CreateStringProperty("Audit persistence status.");
            foreach (var property in schema.Properties.Keys)
                schema.RequiredProperties.Add(property);
            return schema;
        }

        private static JsonSchema CreateApiKeyMetadataListSchema()
        {
            return new JsonSchema
            {
                Type = JsonObjectType.Array,
                Item = CreateApiKeyMetadataSchema()
            };
        }

        private static JsonSchema CreateCreatedApiKeySchema()
        {
            var schema = new JsonSchema { Type = JsonObjectType.Object };
            schema.Properties["id"] = CreateStringProperty("Public API Key identifier.");
            schema.Properties["name"] = CreateStringProperty("User-provided API Key name.");
            schema.Properties["apiKey"] = CreateStringProperty(
                "Complete API Key. This value is returned only once.");
            schema.Properties["createdAtUtc"] = CreateStringProperty("Creation time in UTC.");
            schema.Properties["expiresAtUtc"] = CreateNullableStringProperty(
                "Expiration time in UTC, when configured.");
            schema.RequiredProperties.Add("id");
            schema.RequiredProperties.Add("name");
            schema.RequiredProperties.Add("apiKey");
            schema.RequiredProperties.Add("createdAtUtc");
            schema.RequiredProperties.Add("expiresAtUtc");
            return schema;
        }

        private static JsonSchema CreateApiKeyMetadataSchema()
        {
            var schema = new JsonSchema { Type = JsonObjectType.Object };
            schema.Properties["id"] = CreateStringProperty("Public API Key identifier.");
            schema.Properties["displayPrefix"] = CreateStringProperty(
                "Safe API Key prefix for display.");
            schema.Properties["name"] = CreateStringProperty("User-provided API Key name.");
            schema.Properties["createdAtUtc"] = CreateStringProperty("Creation time in UTC.");
            schema.Properties["lastUsedAtUtc"] = CreateNullableStringProperty(
                "Most recent accepted use time in UTC, when available.");
            schema.Properties["expiresAtUtc"] = CreateNullableStringProperty(
                "Expiration time in UTC, when configured.");
            var status = CreateStringProperty("Current API Key status.");
            status.Enumeration.Add("active");
            status.Enumeration.Add("expired");
            status.Enumeration.Add("revoked");
            schema.Properties["status"] = status;
            schema.RequiredProperties.Add("id");
            schema.RequiredProperties.Add("displayPrefix");
            schema.RequiredProperties.Add("name");
            schema.RequiredProperties.Add("createdAtUtc");
            schema.RequiredProperties.Add("lastUsedAtUtc");
            schema.RequiredProperties.Add("expiresAtUtc");
            schema.RequiredProperties.Add("status");
            return schema;
        }

        private static JsonSchemaProperty CreateStringProperty(string description) =>
            new JsonSchemaProperty
            {
                Type = JsonObjectType.String,
                Description = description
            };

        private static JsonSchemaProperty CreateNullableStringProperty(string description) =>
            new JsonSchemaProperty
            {
                Type = JsonObjectType.String,
                Description = description,
                IsNullableRaw = true
            };
    }
}
