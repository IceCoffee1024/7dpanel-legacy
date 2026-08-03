using System;
using System.Linq;
using System.Reflection;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Web")]
    public sealed class PlayerActionsWebContractTests
    {
        private static readonly string[] ForbiddenBodyProperties =
        {
            "ActionType", "Payload", "OperatorId", "CorrelationId", "InternalName", "ItemKind"
        };

        [Fact]
        public void Actions_controller_exposes_independent_owner_only_routes()
        {
            Assert.Equal("api/v1/player-actions", typeof(PlayerActionsController)
                .GetCustomAttributes(typeof(RoutePrefixAttribute), true)
                .Cast<RoutePrefixAttribute>()
                .Single().Prefix);
            Assert.Contains(
                typeof(PlayerActionsController).GetCustomAttributes(true),
                attribute => attribute.GetType().Name == "OwnerAuthorizeAttribute");
            AssertRoute(nameof(PlayerActionsController.GrantItem), "grant-item");
            AssertRoute(nameof(PlayerActionsController.RemoveItem), "remove-item");
            AssertRoute(nameof(PlayerActionsController.ResetSkills), "reset-skills");
            AssertRoute(nameof(PlayerActionsController.ClearInventory), "clear-inventory");
            AssertRoute(nameof(PlayerActionsController.ResetPlayerData), "reset-player-data");
            AssertRoute(nameof(PlayerActionsController.Get), "{operationId}");
        }

        [Fact]
        public void Every_post_has_a_distinct_typed_body_without_trusted_identity_or_internal_item_fields()
        {
            var types = new[]
            {
                typeof(GrantItemHttpRequest),
                typeof(RemoveItemHttpRequest),
                typeof(ResetSkillsHttpRequest),
                typeof(ClearInventoryHttpRequest),
                typeof(ResetPlayerDataHttpRequest)
            };

            Assert.Equal(types.Length, types.Distinct().Count());
            foreach (var type in types)
            {
                var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(property => property.Name)
                    .ToArray();
                foreach (var forbidden in ForbiddenBodyProperties)
                    Assert.DoesNotContain(forbidden, properties, StringComparer.OrdinalIgnoreCase);
                Assert.Contains("Target", properties);
                Assert.Contains("ClientRequestKey", properties);
            }

            Assert.Contains("CatalogVersion", Properties<GrantItemHttpRequest>());
            Assert.Contains("ResourceId", Properties<GrantItemHttpRequest>());
            Assert.Contains("CatalogVersion", Properties<RemoveItemHttpRequest>());
            Assert.Contains("ResourceId", Properties<RemoveItemHttpRequest>());
        }

        [Fact]
        public void Action_responses_expose_fixed_operation_and_correlation_without_unsafe_fields()
        {
            var responseTypes = new[]
            {
                typeof(GrantItemHttpResponse),
                typeof(RemoveItemHttpResponse),
                typeof(ResetSkillsHttpResponse),
                typeof(ClearInventoryHttpResponse),
                typeof(ResetPlayerDataHttpResponse),
                typeof(PlayerActionOperationHttpResponse)
            };

            foreach (var type in responseTypes)
            {
                var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(property => property.Name)
                    .ToArray();
                Assert.Contains("OperationId", properties);
                Assert.Contains("CorrelationId", properties);
                Assert.DoesNotContain(properties, name =>
                    name.IndexOf("Object", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Token", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        private static string[] Properties<T>() => typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        private static void AssertRoute(string methodName, string expected)
        {
            var method = typeof(PlayerActionsController).GetMethod(methodName);
            Assert.NotNull(method);
            var route = method!.GetCustomAttributes(typeof(RouteAttribute), true)
                .Cast<RouteAttribute>()
                .Single();
            Assert.Equal(expected, route.Template);
        }
    }
}
