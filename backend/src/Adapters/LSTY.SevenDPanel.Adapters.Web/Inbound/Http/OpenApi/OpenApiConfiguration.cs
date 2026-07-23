using LSTY.SevenDPanel.Hosting;
using NJsonSchema;
using NJsonSchema.NewtonsoftJson.Generation;
using NSwag.AspNet.Owin;
using Newtonsoft.Json;
using Owin;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class OpenApiConfiguration
    {
        public static void Configure(
            IAppBuilder app,
            JsonSerializerSettings jsonSerializerSettings)
        {
            app.UseOpenApi(typeof(OwinStartup).Assembly, settings =>
            {
                settings.DocumentPath = OpenApiRoutes.Document;
                settings.GeneratorSettings.SchemaSettings =
                    new NewtonsoftJsonSchemaGeneratorSettings
                    {
                        SchemaType = SchemaType.OpenApi3,
                        SerializerSettings = jsonSerializerSettings
                    };
                settings.GeneratorSettings.Title = "7DPanel API";
                settings.GeneratorSettings.Version = "v1";
                settings.GeneratorSettings.Description =
                    "Runtime API for 7DPanel " + ProductInfo.Version + ".";
                settings.GeneratorSettings.DocumentProcessors.Add(
                    new PanelOpenApiDocumentProcessor());
                settings.GeneratorSettings.OperationProcessors.Add(
                    new PanelOpenApiOperationProcessor());
            });
            app.UseSwaggerUi(settings =>
            {
                settings.Path = OpenApiRoutes.Ui;
                settings.SwaggerRoutes.Add(new SwaggerUiRoute(
                    "v1",
                    OpenApiRoutes.Document));
            });
        }
    }
}