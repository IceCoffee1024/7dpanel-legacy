using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Web")]
    public sealed class GameResourcesHttpTests
    {
        private const string HttpNamespace =
            "LSTY.SevenDPanel.Adapters.Web.Inbound.Http.";
        private const string ResourceUrl = "api/v1/game-resources";
        private const string ETag = "\"game-resource-etag\"";
        private static readonly DateTimeOffset ObservedAtUtc =
            new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);
        private static readonly byte[] PngContent = { 0x89, 0x50, 0x4e, 0x47, 1, 2, 3 };

        [Fact]
        public void Controller_exposes_authorized_path_free_routes_with_stable_action_names()
        {
            var controller = WebType("GameResourcesController");

            Assert.NotNull(controller);
            var authorize = Assert.Single(controller!.GetCustomAttributes<AuthorizeAttribute>(true));
            Assert.True(string.IsNullOrEmpty(authorize.Roles));
            Assert.Equal(
                "api/v1/game-resources",
                Assert.Single(controller.GetCustomAttributes<RoutePrefixAttribute>(true)).Prefix);

            var get = controller.GetMethod("Get", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(get);
            Assert.Equal("", Route(get!).Template);
            Assert.Equal(
                new[] { "search", "kind", "includeHidden", "language", "page", "pageSize" },
                get.GetParameters().Select(parameter => parameter.Name).ToArray());
            Assert.Equal(
                HttpNamespace + "GameResourcePageHttpResponse",
                ResponseTypeName(get));

            var getIcon = controller.GetMethod("GetIcon", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(getIcon);
            Assert.Equal("{resourceId}/icon", Route(getIcon!).Template);
            Assert.Equal(
                new[] { "resourceId" },
                getIcon.GetParameters().Select(parameter => parameter.Name).ToArray());
            Assert.Equal(typeof(byte[]).FullName, ResponseTypeName(getIcon));
            Assert.DoesNotContain(
                getIcon.GetParameters(),
                parameter => ContainsUnsafeIconParameterName(parameter.Name));
        }

        [Fact]
        public void Json_models_expose_only_the_approved_resource_fields()
        {
            Assert.Equal(
                new[]
                {
                    "CatalogVersion", "GameVersion", "Items", "ObservedAtUtc",
                    "Page", "PageSize", "Total", "Warnings"
                },
                PublicPropertyNames(WebType("GameResourcePageHttpResponse")));
            Assert.Equal(
                new[]
                {
                    "HasQuality", "IconStatus", "IconTintHex", "InternalName", "Kind",
                    "LocalizedName", "MaxStack", "NumericId", "ResourceId", "Visibility"
                },
                PublicPropertyNames(WebType("GameResourceItemHttpResponse")));
        }

        [Fact]
        public async Task Catalog_requires_authentication()
        {
            using var host = CreateHost(null, AvailableCatalog(Entry("public", 1, "public")));

            using var response = await host.Client.GetAsync(
                ResourceUrl,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData("Owner")]
        [InlineData("Admin")]
        [InlineData("Viewer")]
        public async Task Catalog_allows_each_authenticated_panel_role(string role)
        {
            using var host = CreateHost(role, AvailableCatalog(Entry("public", 1, "public")));

            using var response = await host.Client.GetAsync(
                ResourceUrl,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData("Owner", HttpStatusCode.OK)]
        [InlineData("Admin", HttpStatusCode.Forbidden)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        public async Task Only_owner_access_can_include_hidden_resources(
            string role,
            HttpStatusCode expectedStatus)
        {
            using var host = CreateHost(role, AvailableCatalog(
                Entry("public", 1, "public"),
                Entry("hidden", 2, "hidden", visibility: GameResourceVisibility.Hidden)));

            using var response = await host.Client.GetAsync(
                ResourceUrl + "?includeHidden=true",
                TestContext.Current.CancellationToken);

            Assert.Equal(expectedStatus, response.StatusCode);
            if (expectedStatus == HttpStatusCode.Forbidden)
                Assert.Equal("game-resource-hidden-forbidden", await ProblemCode(response));
        }

        [Theory]
        [InlineData("?kind=entity")]
        [InlineData("?language=EN")]
        [InlineData("?page=0")]
        [InlineData("?pageSize=101")]
        [InlineData("?search=%20%20")]
        public async Task Invalid_query_parameters_return_stable_problem_details(string query)
        {
            using var host = CreateHost("Owner", AvailableCatalog(Entry("public", 1, "public")));

            using var response = await host.Client.GetAsync(
                ResourceUrl + query,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid-game-resource-query", await ProblemCode(response));
        }

        [Theory]
        [InlineData(GameResourceCatalogReadStatus.Building, "game-resource-catalog-building", true)]
        [InlineData(GameResourceCatalogReadStatus.Unavailable, "game-resource-catalog-unavailable", false)]
        public async Task Non_available_catalog_states_return_stable_503(
            GameResourceCatalogReadStatus status,
            string expectedCode,
            bool expectsRetryAfter)
        {
            var read = status == GameResourceCatalogReadStatus.Building
                ? GameResourceCatalogReadResult.Building()
                : GameResourceCatalogReadResult.Unavailable();
            using var host = CreateHost("Viewer", new StubCatalog(read));

            using var response = await host.Client.GetAsync(
                ResourceUrl,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(expectedCode, await ProblemCode(response));
            if (expectsRetryAfter)
            {
                var delta = response.Headers.RetryAfter?.Delta;
                Assert.NotNull(delta);
                Assert.InRange(delta!.Value.TotalSeconds, 1, 30);
            }
            else
            {
                Assert.Null(response.Headers.RetryAfter);
            }
        }

        [Fact]
        public async Task Available_catalog_uses_camel_case_and_only_the_approved_json_shape()
        {
            using var host = CreateHost("Owner", AvailableCatalog(
                Entry(
                    "resource-1",
                    42,
                    "resourceInternal",
                    localizedNameEn: null,
                    maxStack: null,
                    hasQuality: null,
                    iconTintHex: null),
                gameVersion: null));

            using var response = await host.Client.GetAsync(
                ResourceUrl,
                TestContext.Current.CancellationToken);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            var item = Assert.IsType<JObject>(json["items"]?[0]);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[]
                {
                    "catalogVersion", "gameVersion", "items", "observedAtUtc",
                    "page", "pageSize", "total", "warnings"
                },
                JsonPropertyNames(json));
            Assert.Equal(
                new[]
                {
                    "hasQuality", "iconStatus", "iconTintHex", "internalName", "kind",
                    "localizedName", "maxStack", "numericId", "resourceId", "visibility"
                },
                JsonPropertyNames(item));
            Assert.Equal("catalog-7", (string?)json["catalogVersion"]);
            Assert.Null(json["gameVersion"]?.Value<string>());
            Assert.Equal(ObservedAtUtc, (DateTimeOffset?)json["observedAtUtc"]);
            Assert.Equal("missing-localization-en", (string?)json["warnings"]?[0]);
            Assert.Equal(1, (int?)json["total"]);
            Assert.Equal(1, (int?)json["page"]);
            Assert.Equal(50, (int?)json["pageSize"]);
            Assert.Equal("resource-1", (string?)item["resourceId"]);
            Assert.Equal(42, (int?)item["numericId"]);
            Assert.Equal("resourceInternal", (string?)item["internalName"]);
            Assert.Equal("item", (string?)item["kind"]);
            Assert.Equal("public", (string?)item["visibility"]);
            Assert.Equal("available", (string?)item["iconStatus"]);
            Assert.Equal(JTokenType.Null, item["localizedName"]?.Type);
            Assert.Equal(JTokenType.Null, item["maxStack"]?.Type);
            Assert.Equal(JTokenType.Null, item["hasQuality"]?.Type);
            Assert.Equal(JTokenType.Null, item["iconTintHex"]?.Type);
        }

        [Fact]
        public async Task Page_beyond_the_result_set_is_200_with_empty_items_and_real_total()
        {
            using var host = CreateHost("Viewer", AvailableCatalog(Entry("public", 1, "public")));

            using var response = await host.Client.GetAsync(
                ResourceUrl + "?page=100000&pageSize=100",
                TestContext.Current.CancellationToken);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, (int?)json["total"]);
            Assert.Empty(Assert.IsType<JArray>(json["items"]));
        }

        [Theory]
        [InlineData("Viewer", "hidden")]
        [InlineData("Owner", "does-not-exist")]
        public async Task Hidden_and_missing_icons_share_the_same_404_contract(
            string role,
            string resourceId)
        {
            using var host = CreateHost(role, AvailableCatalog(
                Entry("public", 1, "public"),
                Entry("hidden", 2, "hidden", visibility: GameResourceVisibility.Hidden)));

            using var response = await host.Client.GetAsync(
                ResourceUrl + "/" + resourceId + "/icon",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("game-resource-icon-not-found", await ProblemCode(response));
        }

        [Fact]
        public async Task Png_icon_returns_exact_bytes_private_cache_etag_and_nosniff()
        {
            var catalog = AvailableCatalog(Entry("public", 1, "public"));
            catalog.IconRead = GameResourceIconReadResult.Available(PngContent, ETag);
            using var host = CreateHost("Viewer", catalog);

            using var response = await host.Client.GetAsync(
                ResourceUrl + "/public/icon",
                TestContext.Current.CancellationToken);
            var content = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(PngContent, content);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(ETag, response.Headers.ETag?.Tag);
            Assert.True(response.Headers.CacheControl?.Private);
            Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var values));
            Assert.Equal("nosniff", Assert.Single(values));
        }

        [Theory]
        [InlineData(ETag)]
        [InlineData("*")]
        public async Task Matching_if_none_match_returns_304_without_content(string candidate)
        {
            var catalog = AvailableCatalog(Entry("public", 1, "public"));
            catalog.IconRead = GameResourceIconReadResult.Available(PngContent, ETag);
            using var host = CreateHost("Owner", catalog);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                ResourceUrl + "/public/icon");
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(candidate));

            using var response = await host.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            Assert.Null(response.Content);
            Assert.Equal(ETag, response.Headers.ETag?.Tag);
            Assert.True(response.Headers.CacheControl?.Private);
        }

        [Fact]
        public async Task Unavailable_icon_read_returns_stable_503()
        {
            var catalog = AvailableCatalog(Entry("public", 1, "public"));
            catalog.IconRead = GameResourceIconReadResult.Unavailable();
            using var host = CreateHost("Viewer", catalog);

            using var response = await host.Client.GetAsync(
                ResourceUrl + "/public/icon",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("game-resource-catalog-unavailable", await ProblemCode(response));
        }

        [Fact]
        public async Task Icon_read_failure_does_not_echo_paths_or_exception_details()
        {
            var catalog = AvailableCatalog(Entry("public", 1, "public"));
            catalog.IconException = new IOException("C:\\private\\save\\icons\\public.png");
            using var host = CreateHost("Owner", catalog);

            using var response = await host.Client.GetAsync(
                ResourceUrl + "/public/icon",
                TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("game-resource-icon-read-failed", (string?)JObject.Parse(body)["code"]);
            Assert.DoesNotContain("private", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("save", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Icon_request_passes_the_owin_cancellation_token_to_application()
        {
            var catalog = AvailableCatalog(Entry("public", 1, "public"));
            catalog.IconRead = GameResourceIconReadResult.Available(PngContent, ETag);
            var controllerType = WebType("GameResourcesController");
            Assert.NotNull(controllerType);
            using var controller = Assert.IsAssignableFrom<ApiController>(Activator.CreateInstance(
                controllerType!,
                new QueryGameResourcesUseCase(catalog),
                new GetGameResourceIconUseCase(catalog)));
            using var configuration = new HttpConfiguration();
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "http://localhost/api/v1/game-resources/public/icon");
            using var cancellation = new CancellationTokenSource();
            var principal = Principal("Owner");
            var owin = new OwinContext();
            owin.Authentication.User = principal;
            owin.Environment["owin.CallCancelled"] = cancellation.Token;
            request.SetOwinContext(owin);
            request.GetRequestContext().Principal = principal;
            controller.Configuration = configuration;
            controller.Request = request;
            var getIcon = controllerType.GetMethod("GetIcon", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(getIcon);

            using var response = await Assert.IsType<Task<HttpResponseMessage>>(
                getIcon!.Invoke(controller, new object[] { "public" }));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(cancellation.Token, catalog.LastCancellationToken);
        }

        private static Type? WebType(string name) =>
            typeof(OverviewController).Assembly.GetType(HttpNamespace + name, throwOnError: false);

        private static RouteAttribute Route(MethodInfo method) =>
            Assert.Single(method.GetCustomAttributes<RouteAttribute>(true));

        private static string? ResponseTypeName(MethodInfo method) =>
            Assert.Single(method.GetCustomAttributes<System.Web.Http.Description.ResponseTypeAttribute>(true))
                .ResponseType?.FullName;

        private static string[] PublicPropertyNames(Type? type)
        {
            Assert.NotNull(type);
            return type!.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] JsonPropertyNames(JObject value) =>
            value.Properties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

        private static async Task<string?> ProblemCode(HttpResponseMessage response) =>
            (string?)JObject.Parse(await response.Content.ReadAsStringAsync())["code"];

        private static StubCatalog AvailableCatalog(
            GameResourceCatalogEntry resource,
            string? gameVersion = "3.0.1-b4") =>
            AvailableCatalog(new[] { resource }, gameVersion);

        private static StubCatalog AvailableCatalog(
            GameResourceCatalogEntry first,
            GameResourceCatalogEntry second) =>
            AvailableCatalog(new[] { first, second }, "3.0.1-b4");

        private static StubCatalog AvailableCatalog(
            IReadOnlyList<GameResourceCatalogEntry> resources,
            string? gameVersion) =>
            new StubCatalog(GameResourceCatalogReadResult.Available(
                new GameResourceCatalogSnapshot(
                    "catalog-7",
                    gameVersion,
                    ObservedAtUtc,
                    resources,
                    new[] { "missing-localization-en" })));

        private static GameResourceCatalogEntry Entry(
            string resourceId,
            int numericId,
            string internalName,
            string? localizedNameEn = "English name",
            GameResourceVisibility visibility = GameResourceVisibility.Public,
            int? maxStack = 100,
            bool? hasQuality = false,
            string? iconTintHex = null) =>
            new GameResourceCatalogEntry(
                resourceId,
                numericId,
                internalName,
                "中文名",
                localizedNameEn,
                GameResourceKind.Item,
                visibility,
                maxStack,
                hasQuality,
                GameResourceIconStatus.Available,
                iconTintHex);

        private static HttpTestHost CreateHost(string? role, StubCatalog catalog)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IGameResourceCatalog>(catalog);
            services.AddSingleton<QueryGameResourcesUseCase>();
            services.AddSingleton<GetGameResourceIconUseCase>();
            var provider = services.BuildServiceProvider();
            var configuration = new HttpConfiguration
            {
                DependencyResolver = new MicrosoftDependencyResolver(provider)
            };
            configuration.MapHttpAttributeRoutes();
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            configuration.MessageHandlers.Add(new PrincipalHandler(role));
            configuration.EnsureInitialized();
            return new HttpTestHost(provider, configuration);
        }

        private static ClaimsPrincipal Principal(string role) =>
            new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "subject-1"),
                    new Claim(ClaimTypes.Role, role)
                },
                "Test"));

        private static bool ContainsUnsafeIconParameterName(string? name) =>
            name?.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name?.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name?.IndexOf("tint", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name?.IndexOf("iconName", StringComparison.OrdinalIgnoreCase) >= 0;

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Web")]

        private sealed class StubCatalog : IGameResourceCatalog
        {
            private readonly GameResourceCatalogReadResult read;

            public StubCatalog(GameResourceCatalogReadResult read)
            {
                this.read = read;
            }

            public GameResourceIconReadResult IconRead { get; set; } =
                GameResourceIconReadResult.Missing();

            public Exception? IconException { get; set; }

            public CancellationToken LastCancellationToken { get; private set; }

            public GameResourceCatalogReadResult Read() => read;

            public Task<GameResourceIconReadResult> ReadIconAsync(
                string catalogVersion,
                string resourceId,
                CancellationToken cancellationToken)
            {
                LastCancellationToken = cancellationToken;
                if (IconException != null) throw IconException;
                return Task.FromResult(IconRead);
            }
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Web")]

        private sealed class HttpTestHost : IDisposable
        {
            private readonly ServiceProvider provider;
            private readonly HttpConfiguration configuration;

            public HttpTestHost(ServiceProvider provider, HttpConfiguration configuration)
            {
                this.provider = provider;
                this.configuration = configuration;
                Client = new HttpClient(new HttpServer(configuration))
                {
                    BaseAddress = new Uri("http://localhost/")
                };
            }

            public HttpClient Client { get; }

            public void Dispose()
            {
                Client.Dispose();
                configuration.Dispose();
                provider.Dispose();
            }
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Web")]

        private sealed class PrincipalHandler : DelegatingHandler
        {
            private readonly string? role;

            public PrincipalHandler(string? role)
            {
                this.role = role;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var principal = role == null
                    ? new ClaimsPrincipal(new ClaimsIdentity())
                    : Principal(role);
                var owin = new OwinContext();
                owin.Authentication.User = principal;
                owin.Environment["owin.CallCancelled"] = cancellationToken;
                request.SetOwinContext(owin);
                request.GetRequestContext().Principal = principal;
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
