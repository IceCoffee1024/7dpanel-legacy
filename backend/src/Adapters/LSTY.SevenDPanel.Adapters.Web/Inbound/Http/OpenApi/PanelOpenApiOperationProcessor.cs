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
            DescribeProblemResponses(context);
            if (!RequiresAuthorization(context)) return true;

            context.OperationDescription.Operation.Security =
                new System.Collections.Generic.List<OpenApiSecurityRequirement>();
            context.OperationDescription.Operation.Security.Add(
                new OpenApiSecurityRequirement
                {
                    { "Basic", Array.Empty<string>() }
                });
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

            if (method == OpenApiOperationMethod.Post &&
                string.Equals(path, "/api/v1/players/{entityId}/kick", StringComparison.Ordinal))
            {
                return new[] { "400", "401", "403", "409", "500", "503" };
            }

            return Array.Empty<string>();
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