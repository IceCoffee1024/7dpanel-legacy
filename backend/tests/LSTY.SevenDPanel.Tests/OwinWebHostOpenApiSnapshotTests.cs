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
