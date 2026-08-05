using System;
using System.Linq;
using NSwag;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class PanelOpenApiOperationRuleHelpers
    {
        public static void DescribeQueryParameter(
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

        public static void RemoveParameter(OpenApiOperation operation, string name)
        {
            var parameter = operation.Parameters.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            if (parameter != null) operation.Parameters.Remove(parameter);
        }

        public static void MoveSuccessResponse(
            OpenApiOperation operation,
            string fromStatusCode,
            string toStatusCode,
            string description)
        {
            if (!operation.Responses.TryGetValue(fromStatusCode, out var response))
                response = new OpenApiResponse();
            operation.Responses.Remove(fromStatusCode);
            response.Description = description;
            operation.Responses[toStatusCode] = response;
        }
    }
}
