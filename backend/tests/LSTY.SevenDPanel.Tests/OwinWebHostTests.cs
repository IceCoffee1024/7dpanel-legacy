using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Category", "Integration")]
    [Trait("Host", "InProcessKatana")]
    public sealed class OwinWebHostTests
    {
        [Theory]
        [InlineData("health")]
        [InlineData("api/v1/health")]
        public async Task Health_endpoint_runs_in_real_katana_host(string route)
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";

            using (var host = new OwinWebHost(url, OwinStartup.Configure))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                var response = await client.GetAsync(url + route, TestContext.Current.CancellationToken);
                var body = await response.Content.ReadAsStringAsync();

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                AssertHealthContract(body);
            }

            var rebound = new TcpListener(IPAddress.Loopback, port);
            try
            {
                rebound.Start();
            }
            finally
            {
                rebound.Stop();
            }
        }

        [Fact]
        public async Task Admin_assets_spa_routes_and_api_precedence_run_in_real_katana_host()
        {
            var assetRoot = Path.Combine(Path.GetTempPath(), "7dpanel-admin-" + Guid.NewGuid().ToString("N"));
            var assetsDirectory = Path.Combine(assetRoot, "assets");
            var conflictingApiDirectory = Path.Combine(assetRoot, "api", "v1");
            Directory.CreateDirectory(assetsDirectory);
            Directory.CreateDirectory(conflictingApiDirectory);
            File.WriteAllText(Path.Combine(assetRoot, "index.html"), "<html><body>7DPanel Admin</body></html>");
            File.WriteAllText(Path.Combine(assetsDirectory, "app.js"), "window.panelLoaded = true;");
            File.WriteAllText(Path.Combine(conflictingApiDirectory, "health"), "static content must not win");

            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";

            try
            {
                using (var host = new OwinWebHost(url, app => OwinStartup.Configure(app, assetRoot)))
                using (var handler = new HttpClientHandler { UseProxy = false })
                using (var client = new HttpClient(handler))
                {
                    host.Start();

                    var rootResponse = await client.GetAsync(url, TestContext.Current.CancellationToken);
                    var rootBody = await rootResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
                    Assert.Contains("7DPanel Admin", rootBody);

                    var spaResponse = await client.GetAsync(url + "overview", TestContext.Current.CancellationToken);
                    var spaBody = await spaResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, spaResponse.StatusCode);
                    Assert.Contains("7DPanel Admin", spaBody);

                    var assetResponse = await client.GetAsync(url + "assets/app.js", TestContext.Current.CancellationToken);
                    var assetBody = await assetResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
                    Assert.Contains("panelLoaded", assetBody);

                    var missingAssetResponse = await client.GetAsync(url + "assets/missing.js", TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.NotFound, missingAssetResponse.StatusCode);

                    var missingExtensionAssetResponse = await client.GetAsync(url + "assets/missing", TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.NotFound, missingExtensionAssetResponse.StatusCode);

                    var assetsDirectoryResponse = await client.GetAsync(url + "assets/", TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.NotFound, assetsDirectoryResponse.StatusCode);

                    var apiResponse = await client.GetAsync(url + "api/v1/health", TestContext.Current.CancellationToken);
                    var apiBody = await apiResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
                    AssertHealthContract(apiBody);
                    Assert.DoesNotContain("static content must not win", apiBody);

                    var missingApiResponse = await client.GetAsync(url + "api/v1/missing", TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.NotFound, missingApiResponse.StatusCode);
                }
            }
            finally
            {
                Directory.Delete(assetRoot, true);
            }
        }

        [Fact]
        public async Task Health_endpoint_remains_available_when_admin_assets_are_missing()
        {
            var missingAssetRoot = Path.Combine(Path.GetTempPath(), "7dpanel-missing-admin-" + Guid.NewGuid().ToString("N"));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";

            using (var host = new OwinWebHost(url, app => OwinStartup.Configure(app, missingAssetRoot)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();

                var apiResponse = await client.GetAsync(url + "api/v1/health", TestContext.Current.CancellationToken);
                var apiBody = await apiResponse.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
                AssertHealthContract(apiBody);

                var rootResponse = await client.GetAsync(url, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.NotFound, rootResponse.StatusCode);
            }
        }

        private static void AssertHealthContract(string body)
        {
            var payload = JObject.Parse(body);
            var propertyNames = payload.Properties().Select(property => property.Name).ToArray();

            Assert.Equal(3, propertyNames.Length);
            Assert.Contains("status", propertyNames);
            Assert.Contains("product", propertyNames);
            Assert.Contains("version", propertyNames);
            Assert.DoesNotContain("Status", propertyNames);
            Assert.DoesNotContain("Product", propertyNames);
            Assert.DoesNotContain("Version", propertyNames);
            Assert.Equal("ok", (string)payload["status"]);
            Assert.Equal("7DPanel", (string)payload["product"]);
            Assert.Equal("0.1.0", (string)payload["version"]);
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
