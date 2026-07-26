using System;
using System.Linq;
using System.Web.Http;
using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal sealed class PanelOpenApiOperationProcessor : IOperationProcessor
    {
        public bool Process(OperationProcessorContext context)
        {
            DescribeServerEventStream(context);
            DescribeApiKeyManagement(context);
            DescribePlayerHistory(context);
            DescribeServerOperations(context);
            DescribeConsoleReads(context);
            DescribeProblemResponses(context);
            if (!RequiresAuthorization(context)) return true;

            context.OperationDescription.Operation.Security =
                new System.Collections.Generic.List<OpenApiSecurityRequirement>();
            context.OperationDescription.Operation.Security.Add(
                new OpenApiSecurityRequirement
                {
                    { "Bearer", Array.Empty<string>() }
                });
            return true;
        }

        private static void DescribeServerEventStream(OperationProcessorContext context)
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

        private static void DescribeApiKeyManagement(OperationProcessorContext context)
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

        private static void DescribePlayerHistory(OperationProcessorContext context)
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
                DescribeQueryParameter(
                    operation,
                    "query",
                    "Optional player name or cross-platform identity search text.");
                DescribeQueryParameter(
                    operation,
                    "pageSize",
                    "Page size from 1 through 100; defaults to 50.");
                DescribeQueryParameter(
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
                DescribeQueryParameter(
                    operation,
                    "pageSize",
                    "Page size from 1 through 200; defaults to 100.");
                DescribeQueryParameter(
                    operation,
                    "beforeSnapshotId",
                    "Exclusive snapshot ID cursor returned by the preceding page.");
            }
        }

        private static void DescribeServerOperations(OperationProcessorContext context)
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
            if (!operation.Responses.TryGetValue("200", out var response)) return;

            operation.Responses.Remove("200");
            response.Description = "The server operation request was accepted.";
            operation.Responses["202"] = response;
        }

        private static void DescribeProblemResponses(OperationProcessorContext context)
        {
            var statusCodes = GetProblemStatusCodes(
                context.OperationDescription.Path,
                context.OperationDescription.Method);
            if (statusCodes.Length == 0) return;

            var problemSchema = ApiProblemDetailsOpenApiSchema.GetOrCreate(context.Document);
            foreach (var statusCode in statusCodes)
            {
                context.OperationDescription.Operation.Responses[statusCode] =
                    OpenApiResponses.Problem("Problem Details error response.", problemSchema);
            }
        }

        private static void DescribeConsoleReads(OperationProcessorContext context)
        {
            if (context.OperationDescription.Method != OpenApiOperationMethod.Get) return;

            var path = context.OperationDescription.Path;
            var operation = context.OperationDescription.Operation;
            if (string.Equals(path, "/api/v1/console/logs/recent", StringComparison.Ordinal))
            {
                operation.OperationId = "ConsoleLogs_GetRecent";
                DescribeQueryParameter(
                    operation,
                    "limit",
                    "Number of recent console logs from 1 through 5000; defaults to 1000.");
                return;
            }

            if (string.Equals(path, "/api/v1/console/commands/catalog", StringComparison.Ordinal))
                operation.OperationId = "ConsoleCommands_GetCatalog";
        }

        private static string[] GetProblemStatusCodes(
            string path,
            string method)
        {
            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/events/stream", StringComparison.Ordinal))
            {
                return new[] { "400", "401", "429", "500", "503" };
            }

            if (method == OpenApiOperationMethod.Post &&
                string.Equals(path, "/api/v1/console/commands", StringComparison.Ordinal))
            {
                return new[] { "400", "401", "403", "500", "503" };
            }

            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/players/online", StringComparison.Ordinal))
            {
                return new[] { "401", "403", "500", "503" };
            }

            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/players/history", StringComparison.Ordinal))
            {
                return new[] { "400", "401", "403", "500" };
            }

            if (method == OpenApiOperationMethod.Get &&
                (string.Equals(path, "/api/v1/players/history/{crossplatformId}", StringComparison.Ordinal) ||
                 string.Equals(
                     path,
                     "/api/v1/players/history/{crossplatformId}/snapshots",
                     StringComparison.Ordinal)))
            {
                return new[] { "400", "401", "403", "404", "500" };
            }

            if (method == OpenApiOperationMethod.Post &&
                string.Equals(path, "/api/v1/players/{entityId}/kick", StringComparison.Ordinal))
            {
                return new[] { "400", "401", "403", "409", "500", "503" };
            }

            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/console/logs/recent", StringComparison.Ordinal))
            {
                return new[] { "400", "401", "403", "500", "503" };
            }

            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/console/commands/catalog", StringComparison.Ordinal))
            {
                return new[] { "401", "403", "500", "503" };
            }

            if (method == OpenApiOperationMethod.Post &&
                (string.Equals(
                     path,
                     "/api/v1/server-operations/restart",
                     StringComparison.Ordinal) ||
                 string.Equals(
                     path,
                     "/api/v1/server-operations/shutdown",
                     StringComparison.Ordinal)))
            {
                return new[] { "400", "401", "403", "409", "500", "503" };
            }

            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/api-keys", StringComparison.Ordinal))
            {
                return new[] { "401", "403", "500" };
            }

            if (method == OpenApiOperationMethod.Post &&
                string.Equals(path, "/api/v1/api-keys", StringComparison.Ordinal))
            {
                return new[] { "400", "401", "403", "409", "415", "500" };
            }

            if (method == OpenApiOperationMethod.Delete &&
                string.Equals(path, "/api/v1/api-keys/{keyId}", StringComparison.Ordinal))
            {
                return new[] { "401", "403", "404", "500" };
            }

            return Array.Empty<string>();
        }

        private static void DescribeQueryParameter(
            OpenApiOperation operation,
            string name,
            string description)
        {
            var parameter = operation.Parameters.FirstOrDefault(candidate =>
                candidate.Kind == OpenApiParameterKind.Query &&
                string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (parameter != null)
                parameter.Description = description;
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
