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
                AssertChatOperations(document);
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

        private static void AssertChatOperations(JObject document)
        {
            var expectations = new[]
            {
                new ChatOpenApiExpectation("/api/v1/chat/messages/recent", "get", "Chat_GetRecentMessages", "200", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/messages", "get", "Chat_GetMessages", "200", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/messages/global", "post", "Chat_SendGlobalMessage", "202", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/messages/private", "post", "Chat_SendPrivateMessage", "202", "400", "401", "403", "409", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/settings", "get", "Chat_GetSettings", "200", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/settings", "put", "Chat_UpdateSettings", "200", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/settings", "delete", "Chat_ResetSettings", "200", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/colored/settings", "get", "Chat_GetColoredSettings", "200", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/colored/settings", "put", "Chat_UpdateColoredSettings", "200", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/colored/settings", "delete", "Chat_ResetColoredSettings", "200", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/colored/profiles", "get", "Chat_GetColoredProfiles", "200", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/colored/profiles", "post", "Chat_CreateColoredProfile", "201", "400", "401", "403", "409", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/colored/profiles/{crossplatformId}", "put", "Chat_UpdateColoredProfile", "200", "400", "401", "403", "404", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/colored/profiles/{crossplatformId}", "delete", "Chat_DeleteColoredProfile", "204", "400", "401", "403", "404", "500", "503")
            };

            foreach (var expectation in expectations)
            {
                var operation = (JObject?)document["paths"]?[expectation.Path]?[expectation.Method];
                Assert.NotNull(operation);
                Assert.Equal(expectation.OperationId, (string?)operation!["operationId"]);
                Assert.NotNull(operation["responses"]?[expectation.SuccessStatusCode]);
                Assert.Contains(operation["security"]!.Children(), requirement =>
                    requirement["Bearer"] is JArray);
                foreach (var statusCode in expectation.ProblemStatusCodes)
                {
                    var response = operation["responses"]?[statusCode];
                    Assert.NotNull(response);
                    Assert.NotNull(response!["content"]?["application/problem+json"]);
                }
            }
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

        private sealed class ChatOpenApiExpectation
        {
            public ChatOpenApiExpectation(
                string path,
                string method,
                string operationId,
                string successStatusCode,
                params string[] problemStatusCodes)
            {
                Path = path;
                Method = method;
                OperationId = operationId;
                SuccessStatusCode = successStatusCode;
                ProblemStatusCodes = problemStatusCodes;
            }

            public string Path { get; }
            public string Method { get; }
            public string OperationId { get; }
            public string SuccessStatusCode { get; }
            public IReadOnlyList<string> ProblemStatusCodes { get; }
        }
    }
}
