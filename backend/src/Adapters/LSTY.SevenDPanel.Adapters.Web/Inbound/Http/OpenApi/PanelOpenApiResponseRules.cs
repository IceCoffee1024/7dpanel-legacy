using System;
using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors.Contexts;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class PanelOpenApiResponseRules
    {
        public static void Apply(OperationProcessorContext context)
        {
            NormalizeNoContentResponse(context);
            DescribeProblemResponses(context);
        }

        private static void NormalizeNoContentResponse(OperationProcessorContext context)
        {
            var responses = context.OperationDescription.Operation.Responses;
            if (!responses.TryGetValue("204", out var response)) return;
            responses["204"] = new OpenApiResponse
            {
                Description = string.IsNullOrWhiteSpace(response.Description)
                    ? "The operation completed successfully."
                    : response.Description
            };
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

        private static string[] GetProblemStatusCodes(
            string path,
            string method)
        {
            if (method == OpenApiOperationMethod.Get &&
                (path.StartsWith("/api/v1/players/{crossplatformId}/", StringComparison.Ordinal) ||
                 string.Equals(path, "/api/v1/player-actions/{operationId}", StringComparison.Ordinal)))
            {
                return new[] { "400", "401", "403", "404", "500", "503" };
            }

            if (method == OpenApiOperationMethod.Post &&
                path.StartsWith("/api/v1/player-actions/", StringComparison.Ordinal))
            {
                return new[] { "400", "401", "403", "404", "409", "422", "500", "503" };
            }

            if (path.StartsWith("/api/v1/jobs", StringComparison.Ordinal))
            {
                if (method == OpenApiOperationMethod.Get &&
                    string.Equals(path, "/api/v1/jobs", StringComparison.Ordinal))
                    return new[] { "400", "401", "403", "500", "503" };
                if (method == OpenApiOperationMethod.Get)
                    return new[] { "401", "403", "404", "500", "503" };
                return new[] { "401", "403", "404", "409", "500", "503" };
            }

            if (path.StartsWith("/api/v1/schedules", StringComparison.Ordinal))
                return new[] { "400", "401", "403", "404", "409", "500", "503" };

            if (string.Equals(path, "/api/v1/announcements", StringComparison.Ordinal))
                return new[] { "400", "401", "403", "500", "503" };

            if (method == OpenApiOperationMethod.Get &&
                (string.Equals(path, "/api/v1/audit", StringComparison.Ordinal) ||
                 string.Equals(path, "/api/v1/game-events", StringComparison.Ordinal)))
            {
                return new[] { "400", "401", "403", "500", "503" };
            }

            if ((method == OpenApiOperationMethod.Put || method == OpenApiOperationMethod.Delete) &&
                string.Equals(path, "/api/v1/chat/mutes/{crossplatformId}", StringComparison.Ordinal))
            {
                return new[] { "400", "401", "403", "404", "500", "503" };
            }

            if (method == OpenApiOperationMethod.Get &&
                string.Equals(path, "/api/v1/game-resources", StringComparison.Ordinal))
            {
                return new[] { "400", "401", "403", "500", "503" };
            }

            if (method == OpenApiOperationMethod.Get &&
                string.Equals(
                    path,
                    "/api/v1/game-resources/{resourceId}/icon",
                    StringComparison.Ordinal))
            {
                return new[] { "401", "404", "500", "503" };
            }

            if (path.StartsWith("/api/v1/chat/", StringComparison.Ordinal))
            {
                if (method == OpenApiOperationMethod.Get)
                    return new[] { "400", "401", "403", "500", "503" };

                if (method == OpenApiOperationMethod.Post &&
                    string.Equals(path, "/api/v1/chat/messages/global", StringComparison.Ordinal))
                    return new[] { "400", "401", "403", "500", "503" };

                if (method == OpenApiOperationMethod.Post &&
                    string.Equals(path, "/api/v1/chat/messages/private", StringComparison.Ordinal))
                    return new[] { "400", "401", "403", "409", "500", "503" };

                if (method == OpenApiOperationMethod.Post &&
                    string.Equals(path, "/api/v1/chat/colored/profiles", StringComparison.Ordinal))
                    return new[] { "400", "401", "403", "409", "500", "503" };

                if ((method == OpenApiOperationMethod.Put || method == OpenApiOperationMethod.Delete) &&
                    string.Equals(path, "/api/v1/chat/colored/profiles/{crossplatformId}", StringComparison.Ordinal))
                    return new[] { "400", "401", "403", "404", "500", "503" };

                return new[] { "400", "401", "403", "500", "503" };
            }

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
                (string.Equals(path, "/api/v1/map/metadata", StringComparison.Ordinal) ||
                 string.Equals(path, "/api/v1/map/game-time", StringComparison.Ordinal)))
            {
                return new[] { "401", "403", "500" };
            }

            if (method == OpenApiOperationMethod.Get &&
                string.Equals(
                    path,
                    "/api/v1/map/players/{crossplatformId}/track",
                    StringComparison.Ordinal))
            {
                return new[] { "400", "401", "403", "404", "500" };
            }

            if (method == OpenApiOperationMethod.Get &&
                string.Equals(
                    path,
                    "/api/v1/map/tiles/{worldId}/{z}/{x}/{y}",
                    StringComparison.Ordinal))
            {
                return new[] { "400", "401", "403", "404", "500", "503" };
            }

            if (method == OpenApiOperationMethod.Get &&
                (string.Equals(path, "/api/v1/map/layers/{layerId}", StringComparison.Ordinal) ||
                 string.Equals(path, "/api/v1/map/players/area", StringComparison.Ordinal)))
            {
                return new[] { "400", "401", "403", "500" };
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
    }
}
