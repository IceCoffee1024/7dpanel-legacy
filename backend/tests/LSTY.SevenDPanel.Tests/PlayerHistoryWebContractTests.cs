using System.Linq;
using System.Reflection;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerHistoryWebContractTests
    {
        [Fact]
        public void Players_controller_exposes_the_three_owner_history_routes()
        {
            AssertRoute("GetHistoricalPlayers", "history");
            AssertRoute("GetHistoricalPlayer", "history/{crossplatformId}");
            AssertRoute("GetHistoricalPlayerSnapshots", "history/{crossplatformId}/snapshots");
        }

        private static void AssertRoute(string methodName, string expectedTemplate)
        {
            var method = typeof(PlayersController).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public);
            var route = method == null
                ? null
                : method.GetCustomAttributes<RouteAttribute>(true).SingleOrDefault();

            Assert.NotNull(method);
            Assert.NotNull(route);
            Assert.Equal(expectedTemplate, route!.Template);
        }
    }
}
