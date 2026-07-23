using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;
using System.Linq;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal sealed class PanelOpenApiDocumentProcessor : IDocumentProcessor
    {
        public void Process(DocumentProcessorContext context)
        {
            context.Document.SecurityDefinitions["Bearer"] = new OpenApiSecurityScheme
            {
                Type = OpenApiSecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "opaque",
                Description = "Opaque Bearer access token."
            };

            AddOAuthTokenEndpoint(context.Document);
        }

        internal static void AddOAuthTokenEndpoint(OpenApiDocument document)
        {
            var existingEntry = document.Paths.FirstOrDefault(entry =>
                string.Equals(entry.Key, HttpRoutes.TokenEndpoint, StringComparison.OrdinalIgnoreCase));
            var existingPath = existingEntry.Value;
            if (existingPath != null && existingPath.ContainsKey(OpenApiOperationMethod.Post))
            {
                throw new InvalidOperationException(
                    "The OpenAPI document already contains POST " +
                    HttpRoutes.TokenEndpoint + ".");
            }

            var operation = new OpenApiOperation
            {
                OperationId = "issueAccessToken",
                Summary = "Issues an access token.",
                Description =
                    "Authenticates the owner with the OAuth password grant and returns an opaque Bearer token. " +
                    "Refresh tokens are not supported.",
                RequestBody = new OpenApiRequestBody
                {
                    IsRequired = true,
                    Description = "OAuth password-grant form data containing the owner credentials."
                }
            };
            operation.Tags.Add("Authentication");
            operation.RequestBody.Content["application/x-www-form-urlencoded"] =
                new OpenApiMediaType { Schema = CreateTokenRequestSchema() };
            operation.Responses["200"] = OpenApiResponses.Json(
                "An opaque Bearer access token.",
                CreateTokenResponseSchema());
            operation.Responses["400"] = OpenApiResponses.Json(
                "An OAuth protocol error.",
                CreateOAuthErrorSchema());
            operation.Responses["429"] = OpenApiResponses.Problem(
                "Problem Details rate-limit response.",
                ApiProblemDetailsOpenApiSchema.GetOrCreate(document));
            operation.Responses["500"] = OpenApiResponses.Problem(
                "Problem Details internal-server-error response.",
                ApiProblemDetailsOpenApiSchema.GetOrCreate(document));

            if (existingPath == null)
            {
                existingPath = new OpenApiPathItem();
                document.Paths.Add(HttpRoutes.TokenEndpoint, existingPath);
            }
            existingPath[OpenApiOperationMethod.Post] = operation;
        }

        private static JsonSchema CreateTokenRequestSchema()
        {
            var schema = CreateObjectSchema();
            var grantType = CreateStringProperty("OAuth grant type. Must be password.");
            grantType.Enumeration.Add("password");
            schema.Properties["grant_type"] = grantType;
            schema.Properties["username"] = CreateStringProperty("Owner username.");
            schema.Properties["password"] = CreateStringProperty("Owner password.");
            schema.RequiredProperties.Add("grant_type");
            schema.RequiredProperties.Add("username");
            schema.RequiredProperties.Add("password");
            return schema;
        }

        private static JsonSchema CreateTokenResponseSchema()
        {
            var schema = CreateObjectSchema();
            schema.Properties["access_token"] = CreateStringProperty("Opaque access token.");
            schema.Properties["token_type"] = CreateStringProperty("Bearer token type.");
            schema.Properties["expires_in"] = new JsonSchemaProperty
            {
                Type = JsonObjectType.Integer,
                Description = "Lifetime in seconds."
            };
            schema.Properties["username"] = CreateStringProperty("Authenticated user name.");
            var role = CreateStringProperty("Current panel role.");
            role.Enumeration.Add("Owner");
            role.Enumeration.Add("Admin");
            role.Enumeration.Add("Viewer");
            schema.Properties["role"] = role;
            schema.RequiredProperties.Add("access_token");
            schema.RequiredProperties.Add("token_type");
            schema.RequiredProperties.Add("expires_in");
            schema.RequiredProperties.Add("username");
            schema.RequiredProperties.Add("role");
            return schema;
        }

        private static JsonSchema CreateOAuthErrorSchema()
        {
            var schema = CreateObjectSchema();
            schema.Properties["error"] = CreateStringProperty("OAuth error code.");
            schema.Properties["error_description"] = CreateStringProperty("OAuth error description.");
            schema.RequiredProperties.Add("error");
            return schema;
        }

        private static JsonSchema CreateObjectSchema() =>
            new JsonSchema { Type = JsonObjectType.Object };

        private static JsonSchemaProperty CreateStringProperty(string description) =>
            new JsonSchemaProperty
            {
                Type = JsonObjectType.String,
                Description = description
            };

    }
}