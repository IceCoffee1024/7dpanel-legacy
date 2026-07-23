using NJsonSchema;
using NSwag;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class ApiProblemDetailsOpenApiSchema
    {
        public static JsonSchema GetOrCreate(OpenApiDocument document)
        {
            if (document.Definitions.TryGetValue("ApiProblemDetails", out var existing))
                return existing;

            var schema = new JsonSchema { Type = JsonObjectType.Object };
            schema.Properties["type"] = CreateStringProperty();
            schema.Properties["title"] = CreateStringProperty();
            schema.Properties["status"] = new JsonSchemaProperty { Type = JsonObjectType.Integer };
            schema.Properties["detail"] = CreateStringProperty();
            schema.Properties["instance"] = CreateStringProperty();
            schema.Properties["code"] = CreateStringProperty();
            schema.Properties["traceId"] = CreateStringProperty();
            foreach (var propertyName in schema.Properties.Keys)
                schema.RequiredProperties.Add(propertyName);
            document.Definitions["ApiProblemDetails"] = schema;
            return schema;
        }

        private static JsonSchemaProperty CreateStringProperty() =>
            new JsonSchemaProperty { Type = JsonObjectType.String };
    }
}