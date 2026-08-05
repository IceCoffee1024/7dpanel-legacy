using System;
using System.Linq;
using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors.Contexts;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class PanelOpenApiMapRules
    {
        public static void Apply(OperationProcessorContext context)
        {
            if (context.OperationDescription.Method != OpenApiOperationMethod.Get ||
                !context.OperationDescription.Path.StartsWith("/api/v1/map/", StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(
                    context.OperationDescription.Path,
                    "/api/v1/map/tiles/{worldId}/{z}/{x}/{y}",
                    StringComparison.Ordinal))
            {
                DescribeMapTile(context.OperationDescription.Operation);
                return;
            }

            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "MapMetadataHttpResponse",
                "availability",
                "observedAtUtc",
                "worldId",
                "worldName",
                "extent",
                "axes",
                "availableZoomLevels",
                "tileSize",
                "mapResourceVersion");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "MapExtentHttpResponse",
                "minimumX",
                "minimumZ",
                "maximumX",
                "maximumZ");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "MapAxesHttpResponse",
                "xAxisDirection",
                "zAxisDirection");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "MapGameTimeHttpResponse",
                "availability",
                "day",
                "hour",
                "minute",
                "observedAtUtc");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "PlayerTrackHttpResponse",
                "crossplatformId",
                "segments");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "PlayerTrackSegmentHttpResponse",
                "points");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "PlayerTrackPointHttpResponse",
                "snapshotId",
                "name",
                "x",
                "y",
                "z",
                "observedAtUtc");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "MapLayerHttpResponse",
                "layerId",
                "availability",
                "observedAtUtc",
                "isZoomSufficient",
                "items");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "MapLayerPositionHttpResponse",
                "x",
                "y",
                "z");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "MapLayerBoundsHttpResponse",
                "minimumX",
                "minimumZ",
                "maximumX",
                "maximumZ");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "MapLayerItemHttpResponse",
                "id",
                "kind",
                "position",
                "observedAtUtc",
                "name",
                "playerCombinedId",
                "prefab",
                "prefabBounds",
                "protectionRadius",
                "isOpen",
                "ownerCrossplatformId",
                "isValid",
                "ownerLastLoginUtc",
                "vehicleType",
                "loadState",
                "fuelPercentage",
                "quality",
                "isLocked",
                "storageItemCount",
                "entityType");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "PlayerAreaSearchHttpResponse",
                "hits",
                "candidateObservationCount",
                "matchingObservationCount",
                "candidateObservationLimitReached",
                "playerResultLimitReached");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "PlayerAreaHitHttpResponse",
                "crossplatformId",
                "displayName",
                "firstHitUtc",
                "lastHitUtc",
                "hitObservationCount",
                "lastPosition");
            PanelOpenApiSchemaRules.RequireSchemaProperties(
                context.Document,
                "PlayerMapPositionHttpResponse",
                "x",
                "y",
                "z");

            if (string.Equals(
                    context.OperationDescription.Path,
                    "/api/v1/map/layers/{layerId}",
                    StringComparison.Ordinal))
            {
                DescribeMapLayer(context.OperationDescription.Operation);
                return;
            }

            if (string.Equals(
                    context.OperationDescription.Path,
                    "/api/v1/map/players/area",
                    StringComparison.Ordinal))
            {
                DescribePlayerArea(context.OperationDescription.Operation);
                return;
            }

            if (!string.Equals(
                    context.OperationDescription.Path,
                    "/api/v1/map/players/{crossplatformId}/track",
                    StringComparison.Ordinal))
            {
                return;
            }

            foreach (var name in new[] { "fromUtc", "toUtc" })
            {
                var parameter = context.OperationDescription.Operation.Parameters.First(candidate =>
                    candidate.Kind == OpenApiParameterKind.Query &&
                    string.Equals(candidate.Name, name, StringComparison.Ordinal));
                parameter.IsRequired = true;
                parameter.IsNullableRaw = false;
                if (parameter.Schema != null)
                    parameter.Schema.IsNullableRaw = false;
            }
        }

        private static void DescribeMapLayer(OpenApiOperation operation)
        {
            operation.Description =
                "Returns one Owner-only bounded map layer for the current world. " +
                "Historical and transient items are retained observations or the latest captured snapshot only.";
            RequireParameters(
                operation,
                "layerId",
                "worldId",
                "minimumX",
                "minimumZ",
                "maximumX",
                "maximumZ",
                "zoom",
                "limit");

            var layerId = operation.Parameters.First(parameter =>
                string.Equals(parameter.Name, "layerId", StringComparison.Ordinal));
            var schema = layerId.Schema ?? layerId.CustomSchema;
            if (schema != null)
            {
                schema.Enumeration.Clear();
                foreach (var value in new[]
                {
                    "historical-player-locations",
                    "traders",
                    "land-claims",
                    "vehicles",
                    "drones",
                    "animals",
                    "hostiles"
                })
                {
                    schema.Enumeration.Add(value);
                }
            }
        }

        private static void DescribePlayerArea(OpenApiOperation operation)
        {
            operation.Description =
                "Returns players with retained observations inside one rectangle or circle during the UTC range. " +
                "Results do not assert continuous presence or dwell time.";
            RequireParameters(operation, "shape", "fromUtc", "toUtc", "limit");
            var shape = operation.Parameters.First(parameter =>
                string.Equals(parameter.Name, "shape", StringComparison.Ordinal));
            var schema = shape.Schema ?? shape.CustomSchema;
            if (schema != null)
            {
                schema.Enumeration.Clear();
                schema.Enumeration.Add("rectangle");
                schema.Enumeration.Add("circle");
            }
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

        private static void DescribeMapTile(OpenApiOperation operation)
        {
            operation.Description =
                "Returns one Owner-only map tile. Authentication is accepted only from the Bearer header; " +
                "the URL contains typed coordinates and never a server filesystem path.";

            foreach (var parameter in operation.Parameters)
            {
                if (parameter.Kind != OpenApiParameterKind.Path) continue;
                parameter.IsRequired = true;
                parameter.Description = string.Equals(parameter.Name, "worldId", StringComparison.Ordinal)
                    ? "Current safe world identifier from map metadata."
                    : "Signed native map tile coordinate.";
            }

            var binarySchema = new JsonSchema
            {
                Type = JsonObjectType.String,
                Format = "binary"
            };
            var success = new OpenApiResponse
            {
                Description = "PNG or WebP map tile bytes."
            };
            success.Content["image/png"] = new OpenApiMediaType { Schema = binarySchema };
            success.Content["image/webp"] = new OpenApiMediaType { Schema = binarySchema };
            AddMapTileHeaders(success);
            operation.Responses["200"] = success;

            var notModified = new OpenApiResponse
            {
                Description = "The current tile matches If-None-Match."
            };
            AddMapTileHeaders(notModified);
            operation.Responses["304"] = notModified;
        }

        private static void AddMapTileHeaders(OpenApiResponse response)
        {
            response.Headers["ETag"] = new OpenApiHeader
            {
                Description = "Strong SHA-256 entity tag for the tile content.",
                IsRequired = true,
                Schema = new JsonSchema { Type = JsonObjectType.String }
            };
            response.Headers["Cache-Control"] = new OpenApiHeader
            {
                Description = "Private revalidation policy for authenticated tile content.",
                IsRequired = true,
                Schema = new JsonSchema { Type = JsonObjectType.String },
                Example = "private, max-age=0, must-revalidate"
            };
        }
    }
}
