using NJsonSchema;
using NSwag;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class OpenApiResponses
    {
        public static OpenApiResponse Json(
            string description,
            JsonSchema schema)
        {
            var response = new OpenApiResponse { Description = description };
            response.Content["application/json"] = new OpenApiMediaType { Schema = schema };
            return response;
        }

        public static OpenApiResponse Problem(
            string description,
            JsonSchema schema)
        {
            var response = new OpenApiResponse { Description = description };
            response.Content["application/problem+json"] = new OpenApiMediaType
            {
                Schema = new JsonSchema { Reference = schema }
            };
            return response;
        }
    }
}