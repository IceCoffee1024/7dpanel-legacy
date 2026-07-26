using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed partial class OwinWebHostTests
    {
        private const string UpdateAdminOpenApiSnapshotVariable =
            "SEVENDPANEL_UPDATE_ADMIN_OPENAPI_SNAPSHOT";

        [Fact]
        public async Task Openapi_document_matches_admin_codegen_snapshot()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(false, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                var document = await GetOpenApiDocumentAsync(client, url);
                AssertUniqueOperationIds(document);
                NormalizeForAdminCodegen(document);

                var snapshotPath = GetAdminOpenApiSnapshotPath();
                if (string.Equals(
                    Environment.GetEnvironmentVariable(UpdateAdminOpenApiSnapshotVariable),
                    "1",
                    StringComparison.Ordinal))
                {
                    var directory = Path.GetDirectoryName(snapshotPath)
                        ?? throw new InvalidOperationException("Snapshot path has no directory.");
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(
                        snapshotPath,
                        document.ToString(Formatting.Indented) + Environment.NewLine,
                        new UTF8Encoding(false));
                }

                Assert.True(
                    File.Exists(snapshotPath),
                    "Admin OpenAPI snapshot is missing. Set " +
                    UpdateAdminOpenApiSnapshotVariable + "=1 and rerun this test.");
                var expected = JObject.Parse(File.ReadAllText(snapshotPath));
                Assert.True(
                    JToken.DeepEquals(expected, document),
                    "Runtime OpenAPI differs from frontend/apps/admin/openapi/7dpanel.v1.json.");
            }
        }

        private static void AssertUniqueOperationIds(JObject document)
        {
            var operationIds = document["paths"]!
                .Children<JProperty>()
                .SelectMany(path => path.Value.Children<JProperty>())
                .Where(operation => operation.Value["responses"] != null)
                .Select(operation => (string?)operation.Value["operationId"])
                .ToArray();

            Assert.DoesNotContain(operationIds, string.IsNullOrWhiteSpace);
            Assert.Equal(
                operationIds.Length,
                operationIds.Distinct(StringComparer.Ordinal).Count());
        }

        private static void NormalizeForAdminCodegen(JObject document)
        {
            document["servers"] = new JArray(
                new JObject(new JProperty("url", "/")));
        }

        private static string GetAdminOpenApiSnapshotPath()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "backend", "7DPanel.sln")))
                {
                    return Path.Combine(
                        directory.FullName,
                        "frontend",
                        "apps",
                        "admin",
                        "openapi",
                        "7dpanel.v1.json");
                }
                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate the repository root.");
        }
    }
}
