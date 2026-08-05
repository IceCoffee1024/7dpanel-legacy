using System;
using System.Linq;
using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors.Contexts;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class PanelOpenApiGameResourceRules
    {
        public static void Apply(OperationProcessorContext context)
        {
            if (context.OperationDescription.Method != OpenApiOperationMethod.Get)
                return;

            var path = context.OperationDescription.Path;
            var operation = context.OperationDescription.Operation;
            if (string.Equals(path, "/api/v1/game-resources", StringComparison.Ordinal))
            {
                operation.OperationId = "GameResources_Get";
                operation.Description =
                    "Returns one authorization-aware page from the in-memory game-resource catalog.";
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "search",
                    "Case-insensitive internal or localized name search.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "kind",
                    "Resource kind: all, item, or block.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "includeHidden",
                    "Includes hidden resources for Owner callers only.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "language",
                    "Localization language used for display names.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "page",
                    "One-based page number.");
                PanelOpenApiOperationRuleHelpers.DescribeQueryParameter(
                    operation,
                    "pageSize",
                    "Requested page size.");
                PanelOpenApiSchemaRules.RequireSchemaProperties(
                    context.Document,
                    "GameResourcePageHttpResponse",
                    "catalogVersion",
                    "gameVersion",
                    "observedAtUtc",
                    "total",
                    "page",
                    "pageSize",
                    "warnings",
                    "items");
                PanelOpenApiSchemaRules.RequireSchemaProperties(
                    context.Document,
                    "GameResourceItemHttpResponse",
                    "resourceId",
                    "numericId",
                    "internalName",
                    "localizedName",
                    "kind",
                    "visibility",
                    "maxStack",
                    "hasQuality",
                    "iconStatus",
                    "iconTintHex");
                return;
            }

            if (!string.Equals(
                    path,
                    "/api/v1/game-resources/{resourceId}/icon",
                    StringComparison.Ordinal))
            {
                return;
            }

            operation.OperationId = "GameResources_GetIcon";
            operation.Description =
                "Returns one catalog-version-bound PNG icon using Bearer authentication.";
            RequireParameters(operation, "resourceId");
            var resourceId = operation.Parameters.First(parameter =>
                string.Equals(parameter.Name, "resourceId", StringComparison.Ordinal));
            resourceId.Description = "Opaque resource identifier returned by the catalog query.";

            var binarySchema = new JsonSchema
            {
                Type = JsonObjectType.String,
                Format = "binary"
            };
            var success = new OpenApiResponse
            {
                Description = "PNG icon bytes."
            };
            success.Content["image/png"] = new OpenApiMediaType { Schema = binarySchema };
            AddGameResourceIconHeaders(success);
            operation.Responses["200"] = success;

            var notModified = new OpenApiResponse
            {
                Description = "The current icon matches If-None-Match."
            };
            AddGameResourceIconHeaders(notModified);
            operation.Responses["304"] = notModified;
        }

        private static void RequireParameters(OpenApiOperation operation, params string[] names)
        {
            foreach (var name in names)
            {
                var parameter = operation.Parameters.First(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.Ordinal));
                parameter.IsRequired = true;
                parameter.IsNullableRaw = false;
                if (parameter.Schema != null)
                    parameter.Schema.IsNullableRaw = false;
            }
        }

        private static void AddGameResourceIconHeaders(OpenApiResponse response)
        {
            response.Headers["ETag"] = new OpenApiHeader
            {
                Description = "Strong entity tag for the icon content.",
                IsRequired = true,
                Schema = new JsonSchema { Type = JsonObjectType.String }
            };
            response.Headers["Cache-Control"] = new OpenApiHeader
            {
                Description = "Private cache policy for authenticated icon content.",
                IsRequired = true,
                Schema = new JsonSchema { Type = JsonObjectType.String }
            };
            response.Headers["X-Content-Type-Options"] = new OpenApiHeader
            {
                Description = "Prevents MIME type sniffing.",
                IsRequired = true,
                Schema = new JsonSchema { Type = JsonObjectType.String },
                Example = "nosniff"
            };
        }
    }
}
