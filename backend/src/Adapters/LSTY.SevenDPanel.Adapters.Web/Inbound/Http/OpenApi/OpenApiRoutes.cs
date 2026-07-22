using System;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class OpenApiRoutes
    {
        public const string Document = "/swagger/v1/swagger.json";
        public const string Ui = "/swagger";

        public static bool OwnsPath(string path) =>
            string.Equals(path, Ui, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(Ui + "/", StringComparison.OrdinalIgnoreCase);
    }
}