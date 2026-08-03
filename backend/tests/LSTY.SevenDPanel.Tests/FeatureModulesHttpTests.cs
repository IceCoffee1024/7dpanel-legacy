using System.Linq;
using System.Reflection;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Platform")]
    [Trait("Boundary", "Web")]
    public sealed class FeatureModulesHttpTests
    {
        [Fact]
        public void Owner_only_controller_exposes_fixed_list_enable_and_disable_routes()
        {
            var type = typeof(ModulesController);
            Assert.Equal("Owner", type.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
            Assert.Equal(
                "api/v1/modules",
                type.GetCustomAttribute<RoutePrefixAttribute>()?.Prefix);

            Assert.Equal("", Route(type, "Get"));
            Assert.Equal("{moduleId}/enable", Route(type, "Enable"));
            Assert.Equal("{moduleId}/disable", Route(type, "Disable"));
            Assert.Equal(
                new[] { "disable", "enable" },
                type.GetMethods()
                    .Where(method => method.GetCustomAttribute<HttpPostAttribute>() != null)
                    .Select(method => method.Name.ToLowerInvariant())
                    .OrderBy(value => value));
        }

        private static string? Route(System.Type type, string method) =>
            type.GetMethod(method)?.GetCustomAttribute<RouteAttribute>()?.Template;
    }
}
