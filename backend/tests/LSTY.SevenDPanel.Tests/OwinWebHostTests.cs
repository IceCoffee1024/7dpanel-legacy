using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
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
                Assert.Contains("\"status\":\"ok\"", body, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("\"product\":\"7DPanel\"", body, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("\"version\":\"0.1.0\"", body, StringComparison.OrdinalIgnoreCase);
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
