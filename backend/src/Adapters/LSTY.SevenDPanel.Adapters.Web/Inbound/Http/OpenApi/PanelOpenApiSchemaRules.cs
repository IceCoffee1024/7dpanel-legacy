using System;
using NJsonSchema;
using NSwag;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class PanelOpenApiSchemaRules
    {
        public static void RequireSchemaProperties(
            OpenApiDocument document,
            string schemaName,
            params string[] propertyNames)
        {
            if (!document.Components.Schemas.TryGetValue(schemaName, out var schema))
                return;
            foreach (var propertyName in propertyNames)
            {
                if (!schema.RequiredProperties.Contains(propertyName))
                    schema.RequiredProperties.Add(propertyName);
            }
        }
    }
}
