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
                AssertMapContractSemantics(document);
                AssertGameResourceContractSemantics(document);
                AssertChatOperations(document);
                AssertEvidenceFoundationOperations(document);
                AssertPlayerEvidenceActionOperations(document);
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

        private static void AssertMapContractSemantics(JObject document)
        {
            var metadata = GetOperation(document, "/api/v1/map/metadata");
            AssertOperationResponseCodes(metadata, "200", "401", "403", "500");
            AssertRequiredProperties(
                GetSchema(document, "MapMetadataHttpResponse"),
                "availability",
                "observedAtUtc",
                "worldId",
                "worldName",
                "extent",
                "axes",
                "availableZoomLevels",
                "tileSize",
                "mapResourceVersion");
            AssertNullableProperties(
                GetSchema(document, "MapMetadataHttpResponse"),
                "observedAtUtc",
                "worldId",
                "worldName",
                "extent",
                "axes",
                "availableZoomLevels",
                "tileSize",
                "mapResourceVersion");
            AssertRequiredProperties(
                GetSchema(document, "MapExtentHttpResponse"),
                "minimumX",
                "minimumZ",
                "maximumX",
                "maximumZ");
            AssertRequiredProperties(
                GetSchema(document, "MapAxesHttpResponse"),
                "xAxisDirection",
                "zAxisDirection");

            var gameTime = GetOperation(document, "/api/v1/map/game-time");
            AssertOperationResponseCodes(gameTime, "200", "401", "403", "500");
            AssertRequiredProperties(
                GetSchema(document, "MapGameTimeHttpResponse"),
                "availability",
                "day",
                "hour",
                "minute",
                "observedAtUtc");
            AssertNullableProperties(
                GetSchema(document, "MapGameTimeHttpResponse"),
                "day",
                "hour",
                "minute",
                "observedAtUtc");

            var track = GetOperation(document, "/api/v1/map/players/{crossplatformId}/track");
            AssertOperationResponseCodes(track, "200", "400", "401", "403", "404", "500");
            foreach (var name in new[] { "fromUtc", "toUtc" })
            {
                var parameter = Assert.Single(
                    track["parameters"]!.Children<JObject>(),
                    candidate => string.Equals((string?)candidate["name"], name, StringComparison.Ordinal));
                Assert.True((bool?)parameter["required"]);
                Assert.NotEqual(true, (bool?)parameter["schema"]?["nullable"]);
            }

            AssertRequiredProperties(
                GetSchema(document, "PlayerTrackHttpResponse"),
                "crossplatformId",
                "segments");
            AssertRequiredProperties(
                GetSchema(document, "PlayerTrackSegmentHttpResponse"),
                "points");
            AssertRequiredProperties(
                GetSchema(document, "PlayerTrackPointHttpResponse"),
                "snapshotId",
                "name",
                "x",
                "y",
                "z",
                "observedAtUtc");

            var tile = GetOperation(
                document,
                "/api/v1/map/tiles/{worldId}/{z}/{x}/{y}");
            AssertOperationResponseCodes(
                tile,
                "200",
                "304",
                "400",
                "401",
                "403",
                "404",
                "500",
                "503");
            Assert.Equal(
                new[] { "worldId", "z", "x", "y" }.OrderBy(name => name),
                tile["parameters"]!
                    .Children<JObject>()
                    .Where(parameter => string.Equals((string?)parameter["in"], "path", StringComparison.Ordinal))
                    .Select(parameter => (string)parameter["name"]!)
                    .OrderBy(name => name));
            Assert.DoesNotContain(
                tile["parameters"]!.Children<JObject>(),
                parameter => string.Equals((string?)parameter["in"], "query", StringComparison.Ordinal));
            var tileContent = tile["responses"]!["200"]!["content"]!;
            Assert.Equal(
                new[] { "image/png", "image/webp" },
                tileContent.Children<JProperty>().Select(property => property.Name).OrderBy(name => name));
            foreach (var mediaType in new[] { "image/png", "image/webp" })
            {
                Assert.Equal("string", (string?)tileContent[mediaType]?["schema"]?["type"]);
                Assert.Equal("binary", (string?)tileContent[mediaType]?["schema"]?["format"]);
            }
            foreach (var status in new[] { "200", "304" })
            {
                Assert.NotNull(tile["responses"]![status]?["headers"]?["ETag"]);
                Assert.NotNull(tile["responses"]![status]?["headers"]?["Cache-Control"]);
            }

            var layers = GetOperation(document, "/api/v1/map/layers/{layerId}");
            AssertOperationResponseCodes(layers, "200", "400", "401", "403", "500");
            foreach (var name in new[]
            {
                "layerId", "worldId", "minimumX", "minimumZ", "maximumX", "maximumZ", "zoom", "limit"
            })
            {
                var parameter = Assert.Single(
                    layers["parameters"]!.Children<JObject>(),
                    candidate => string.Equals((string?)candidate["name"], name, StringComparison.Ordinal));
                Assert.True((bool?)parameter["required"]);
            }
            AssertRequiredProperties(
                GetSchema(document, "MapLayerHttpResponse"),
                "layerId",
                "availability",
                "observedAtUtc",
                "isZoomSufficient",
                "items");
            AssertRequiredProperties(
                GetSchema(document, "MapLayerPositionHttpResponse"),
                "x",
                "y",
                "z");

            var area = GetOperation(document, "/api/v1/map/players/area");
            AssertOperationResponseCodes(area, "200", "400", "401", "403", "500");
            foreach (var name in new[] { "shape", "fromUtc", "toUtc", "limit" })
            {
                var parameter = Assert.Single(
                    area["parameters"]!.Children<JObject>(),
                    candidate => string.Equals((string?)candidate["name"], name, StringComparison.Ordinal));
                Assert.True((bool?)parameter["required"]);
            }
            AssertRequiredProperties(
                GetSchema(document, "PlayerAreaSearchHttpResponse"),
                "hits",
                "candidateObservationCount",
                "matchingObservationCount",
                "candidateObservationLimitReached",
                "playerResultLimitReached");
            AssertRequiredProperties(
                GetSchema(document, "PlayerAreaHitHttpResponse"),
                "crossplatformId",
                "displayName",
                "firstHitUtc",
                "lastHitUtc",
                "hitObservationCount",
                "lastPosition");
        }

        private static void AssertGameResourceContractSemantics(JObject document)
        {
            var list = document["paths"]?["/api/v1/game-resources"]?["get"] as JObject;
            Assert.NotNull(list);
            Assert.Equal("GameResources_Get", (string?)list!["operationId"]);
            AssertOperationResponseCodes(list, "200", "400", "401", "403", "500", "503");
            Assert.Contains(list["security"]!.Children(), requirement =>
                requirement["Bearer"] is JArray);
            Assert.Equal(
                new[] { "includeHidden", "kind", "language", "page", "pageSize", "search" },
                list["parameters"]!
                    .Children<JObject>()
                    .Where(parameter => string.Equals(
                        (string?)parameter["in"],
                        "query",
                        StringComparison.Ordinal))
                    .Select(parameter => (string)parameter["name"]!)
                    .OrderBy(name => name, StringComparer.Ordinal));
            AssertRequiredProperties(
                GetSchema(document, "GameResourcePageHttpResponse"),
                "catalogVersion",
                "gameVersion",
                "observedAtUtc",
                "total",
                "page",
                "pageSize",
                "warnings",
                "items");
            AssertNullableProperties(
                GetSchema(document, "GameResourcePageHttpResponse"),
                "gameVersion");
            AssertRequiredProperties(
                GetSchema(document, "GameResourceItemHttpResponse"),
                "resourceId",
                "numericId",
                "internalName",
                "localizedName",
                "kind",
                "visibility",
                "maxStack",
                "hasQuality",
                "iconStatus",
                "iconTintHex");
            AssertNullableProperties(
                GetSchema(document, "GameResourceItemHttpResponse"),
                "localizedName",
                "maxStack",
                "hasQuality",
                "iconTintHex");

            var icon = document["paths"]?["/api/v1/game-resources/{resourceId}/icon"]?["get"]
                as JObject;
            Assert.NotNull(icon);
            Assert.Equal("GameResources_GetIcon", (string?)icon!["operationId"]);
            AssertOperationResponseCodes(icon, "200", "304", "401", "404", "500", "503");
            Assert.Contains(icon["security"]!.Children(), requirement =>
                requirement["Bearer"] is JArray);
            Assert.DoesNotContain(
                icon["parameters"]!.Children<JObject>(),
                parameter => string.Equals(
                    (string?)parameter["in"],
                    "query",
                    StringComparison.Ordinal));
            var resourceId = Assert.Single(
                icon["parameters"]!.Children<JObject>(),
                parameter => string.Equals(
                    (string?)parameter["name"],
                    "resourceId",
                    StringComparison.Ordinal));
            Assert.Equal("path", (string?)resourceId["in"]);
            Assert.True((bool?)resourceId["required"]);
            var content = icon["responses"]!["200"]!["content"]!;
            var png = Assert.Single(content.Children<JProperty>());
            Assert.Equal("image/png", png.Name);
            Assert.Equal("string", (string?)png.Value["schema"]?["type"]);
            Assert.Equal("binary", (string?)png.Value["schema"]?["format"]);
            foreach (var status in new[] { "200", "304" })
            {
                var headers = icon["responses"]![status]!["headers"]!;
                Assert.NotNull(headers["ETag"]);
                Assert.NotNull(headers["Cache-Control"]);
                Assert.NotNull(headers["X-Content-Type-Options"]);
            }
        }

        private static JObject GetOperation(JObject document, string path) =>
            (JObject)document["paths"]![path]!["get"]!;

        private static JObject GetSchema(JObject document, string name) =>
            (JObject)document["components"]!["schemas"]![name]!;

        private static void AssertOperationResponseCodes(JObject operation, params string[] expected)
        {
            var actual = operation["responses"]!
                .Children<JProperty>()
                .Select(response => response.Name)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expected.OrderBy(code => code, StringComparer.Ordinal), actual);
        }

        private static void AssertRequiredProperties(JObject schema, params string[] expected)
        {
            var actual = schema["required"]?.Values<string>()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>();
            Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
        }

        private static void AssertNullableProperties(JObject schema, params string[] names)
        {
            foreach (var name in names)
                Assert.True((bool?)schema["properties"]![name]!["nullable"], name + " must be nullable.");
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

        private static void AssertEvidenceFoundationOperations(JObject document)
        {
            var expectations = new[]
            {
                new ChatOpenApiExpectation("/api/v1/audit", "get", "listAuditEntries", "200", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/game-events", "get", "listGameEvents", "200", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/mutes", "get", "listChatMutes", "200", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/mutes", "post", "createChatMute", "201", "400", "401", "403", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/mutes/{crossplatformId}", "put", "updateChatMute", "200", "400", "401", "403", "404", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/chat/mutes/{crossplatformId}", "delete", "releaseChatMute", "204", "400", "401", "403", "404", "500", "503")
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

        private static void AssertPlayerEvidenceActionOperations(JObject document)
        {
            var reads = new[]
            {
                new ChatOpenApiExpectation("/api/v1/players/{crossplatformId}/profile", "get", "PlayerEvidence_GetProfile", "200", "400", "401", "403", "404", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/players/{crossplatformId}/inventory-snapshots", "get", "PlayerEvidence_GetInventorySnapshots", "200", "400", "401", "403", "404", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/players/{crossplatformId}/inventory-diffs", "get", "PlayerEvidence_GetInventoryDiffs", "200", "400", "401", "403", "404", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/players/{crossplatformId}/skills", "get", "PlayerEvidence_GetSkills", "200", "400", "401", "403", "404", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/player-actions/{operationId}", "get", "PlayerActions_Get", "200", "400", "401", "403", "404", "500", "503")
            };
            var writes = new[]
            {
                new ChatOpenApiExpectation("/api/v1/player-actions/grant-item", "post", "PlayerActions_GrantItem", "202", "200", "400", "401", "403", "404", "409", "422", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/player-actions/remove-item", "post", "PlayerActions_RemoveItem", "202", "200", "400", "401", "403", "404", "409", "422", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/player-actions/reset-skills", "post", "PlayerActions_ResetSkills", "202", "200", "400", "401", "403", "404", "409", "422", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/player-actions/clear-inventory", "post", "PlayerActions_ClearInventory", "202", "200", "400", "401", "403", "404", "409", "422", "500", "503"),
                new ChatOpenApiExpectation("/api/v1/player-actions/reset-player-data", "post", "PlayerActions_ResetPlayerData", "202", "200", "400", "401", "403", "404", "409", "422", "500", "503")
            };

            foreach (var expectation in reads.Concat(writes))
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
                    if (statusCode != "200")
                        Assert.NotNull(response!["content"]?["application/problem+json"]);
                }
            }

            foreach (var path in new[]
            {
                "/api/v1/players/{crossplatformId}/inventory-snapshots",
                "/api/v1/players/{crossplatformId}/inventory-diffs",
                "/api/v1/players/{crossplatformId}/skills"
            })
            {
                var operation = (JObject)document["paths"]![path]!["get"]!;
                Assert.Contains(operation["parameters"]!.Children<JObject>(), parameter =>
                    string.Equals((string?)parameter["name"], "pageSize", StringComparison.Ordinal));
                Assert.Contains(operation["parameters"]!.Children<JObject>(), parameter =>
                    string.Equals((string?)parameter["name"], "cursor", StringComparison.Ordinal));
            }

            AssertActionBodySchema(document, "/api/v1/player-actions/grant-item", "GrantItemHttpRequest");
            AssertActionBodySchema(document, "/api/v1/player-actions/remove-item", "RemoveItemHttpRequest");
            AssertActionBodySchema(document, "/api/v1/player-actions/reset-skills", "ResetSkillsHttpRequest");
            AssertActionBodySchema(document, "/api/v1/player-actions/clear-inventory", "ClearInventoryHttpRequest");
            AssertActionBodySchema(document, "/api/v1/player-actions/reset-player-data", "ResetPlayerDataHttpRequest");

            foreach (var schemaName in new[] { "GrantItemHttpRequest", "RemoveItemHttpRequest" })
            {
                var properties = GetSchema(document, schemaName)["properties"]!
                    .Children<JProperty>()
                    .Select(property => property.Name)
                    .ToArray();
                Assert.DoesNotContain(properties, name =>
                    string.Equals(name, "internalName", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "itemKind", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "operatorId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "correlationId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "actionType", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "payload", StringComparison.OrdinalIgnoreCase));
                Assert.Contains("catalogVersion", properties);
                Assert.Contains("resourceId", properties);
            }
        }

        private static void AssertActionBodySchema(
            JObject document,
            string path,
            string expectedSchema)
        {
            var bodySchema = document["paths"]![path]!["post"]!["requestBody"]!["content"]!
                ["application/json"]!["schema"]!;
            Assert.Equal(
                "#/components/schemas/" + expectedSchema,
                (string?)bodySchema["$ref"]);
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
