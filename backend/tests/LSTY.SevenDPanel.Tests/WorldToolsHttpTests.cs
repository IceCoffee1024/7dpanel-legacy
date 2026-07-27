using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.WorldOperations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class WorldToolsHttpTests
    {
        private static readonly string[] WorldGetRoutes =
        {
            "summary",
            "land-claims",
            "vehicles",
            "drones",
            "containers",
            "catalogs/blocks",
            "catalogs/prefabs",
            "catalogs/entity-types"
        };

        private static readonly string[] WorldOperationPostRoutes =
        {
            "land-claims/delete",
            "players/move",
            "entities/move",
            "regions/copy",
            "regions/fill",
            "regions/clear",
            "regions/paste",
            "blocks/set",
            "prefabs/place",
            "prefabs/remove",
            "entities/spawn",
            "entities/delete",
            "entities/cleanup",
            "xml/reload",
            "gc",
            "undo"
        };

        private static readonly string[] MapJobPostRoutes =
        {
            "refresh-resources",
            "render-explored",
            "render-full"
        };

        [Fact]
        public void Controllers_expose_only_the_approved_fixed_routes()
        {
            Assert.Equal(WorldGetRoutes.OrderBy(value => value), Routes<WorldController, HttpGetAttribute>());
            Assert.Equal(WorldOperationPostRoutes.OrderBy(value => value), Routes<WorldOperationsController, HttpPostAttribute>());
            Assert.Equal(new[] { "{operationId}" }, Routes<WorldOperationsController, HttpGetAttribute>());
            Assert.Equal(MapJobPostRoutes.OrderBy(value => value), Routes<MapJobsController, HttpPostAttribute>());
            Assert.Equal(new[] { "resource-version" }, Routes<MapJobsController, HttpGetAttribute>());

            Assert.Equal("api/v1/world", Prefix<WorldController>());
            Assert.Equal("api/v1/world-operations", Prefix<WorldOperationsController>());
            Assert.Equal("api/v1/map-jobs", Prefix<MapJobsController>());
        }

        [Fact]
        public void Every_write_route_has_its_own_strict_web_request_dto_without_forbidden_members()
        {
            var methods = PostMethods(typeof(WorldOperationsController))
                .Concat(PostMethods(typeof(MapJobsController)))
                .ToArray();
            var requestTypes = methods
                .Select(method => method.GetParameters().Single(parameter =>
                    parameter.ParameterType != typeof(CancellationToken)).ParameterType)
                .ToArray();

            Assert.Equal(WorldOperationPostRoutes.Length + MapJobPostRoutes.Length, requestTypes.Length);
            Assert.Equal(requestTypes.Length, requestTypes.Distinct().Count());
            Assert.All(requestTypes, requestType =>
            {
                Assert.Equal(typeof(WorldController).Assembly, requestType.Assembly);
                AssertStrictRequestGraph(requestType, new HashSet<Type>());
            });
        }

        [Fact]
        public async Task World_reads_require_authentication_and_allow_viewers()
        {
            using var anonymous = CreateHost(null);
            using var unauthorized = await anonymous.Client.GetAsync("api/v1/world/summary");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

            using var viewer = CreateHost("Viewer");
            using var response = await viewer.Client.GetAsync("api/v1/world/summary");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [MemberData(nameof(AllWritePaths))]
        public async Task Every_write_route_is_owner_only(string path)
        {
            using var host = CreateHost("Viewer");

            using var response = await PostAsync(host.Client, path, "{}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task World_reads_return_fixed_scalar_dtos_and_catalog_partitions()
        {
            using var host = CreateHost("Viewer");

            using var summaryResponse = await host.Client.GetAsync("api/v1/world/summary");
            var summary = JObject.Parse(await summaryResponse.Content.ReadAsStringAsync());
            using var claimsResponse = await host.Client.GetAsync("api/v1/world/land-claims");
            var claims = JObject.Parse(await claimsResponse.Content.ReadAsStringAsync());
            using var blocksResponse = await host.Client.GetAsync("api/v1/world/catalogs/blocks");
            var blocks = JObject.Parse(await blocksResponse.Content.ReadAsStringAsync());

            Assert.Equal("world-1", (string?)summary["worldId"]);
            Assert.Equal("world-version-1", (string?)summary["worldVersion"]);
            Assert.Equal("map-version-1", (string?)summary["mapResourceVersion"]);
            Assert.Equal("Available", (string?)summary["sourceState"]);
            Assert.Equal("claim-1", (string?)claims["items"]?[0]?["serverId"]);
            Assert.Equal("block-stone", (string?)blocks["items"]?[0]);
            Assert.DoesNotContain("path", summary.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Owner_submission_returns_only_a_202_receipt_and_server_owned_identity_fields()
        {
            var bridge = new RecordingBridge();
            using var host = CreateHost("Owner", bridge: bridge);

            using var response = await PostAsync(
                host.Client,
                "api/v1/world-operations/gc",
                "{\"worldId\":\"world-1\",\"worldVersion\":\"world-version-1\",\"mapResourceVersion\":\"map-version-1\",\"confirmed\":true}");
            var receipt = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("operation-1", (string?)receipt["operationId"]);
            Assert.Equal("Queued", (string?)receipt["status"]);
            Assert.Equal("subject-1", bridge.LastIntent?.ActorSubject);
            Assert.Equal(WorldOperationKind.CollectGarbage, bridge.LastIntent?.Kind);
            Assert.Equal("world-1", bridge.LastIntent?.WorldId);
            Assert.DoesNotContain("actorSubject", receipt.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Full_map_render_requires_strong_confirmation_before_returning_202()
        {
            var bridge = new RecordingBridge();
            using var host = CreateHost("Owner", bridge: bridge);
            const string prefix = "{\"worldId\":\"world-1\",\"worldVersion\":\"world-version-1\",\"confirmed\":true,";

            using var rejected = await PostAsync(
                host.Client,
                "api/v1/map-jobs/render-full",
                prefix + "\"strongConfirmed\":false}");
            var problem = JObject.Parse(await rejected.Content.ReadAsStringAsync());

            Assert.Equal((HttpStatusCode)422, rejected.StatusCode);
            Assert.Equal("strong_confirmation_required", (string?)problem["code"]);

            using var accepted = await PostAsync(
                host.Client,
                "api/v1/map-jobs/render-full",
                prefix + "\"strongConfirmed\":true}");
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            Assert.Equal(WorldOperationKind.RenderFullMap, bridge.LastIntent?.Kind);
        }

        [Fact]
        public async Task Forbidden_unknown_request_members_are_rejected_without_echoing_values()
        {
            using var host = CreateHost("Owner");

            using var response = await PostAsync(
                host.Client,
                "api/v1/world-operations/gc",
                "{\"worldId\":\"world-1\",\"worldVersion\":\"world-version-1\",\"confirmed\":true,\"path\":\"C:\\\\private\\\\world.xml\"}");
            var body = await response.Content.ReadAsStringAsync();
            var problem = JObject.Parse(body);

            Assert.Equal((HttpStatusCode)422, response.StatusCode);
            Assert.Equal("invalid_world_operation_request", (string?)problem["code"]);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("private", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("world.xml", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Operation_queries_publish_all_eight_authoritative_statuses()
        {
            var bridge = new RecordingBridge();
            using var host = CreateHost("Owner", bridge: bridge);

            foreach (var status in Enum.GetValues(typeof(WorldOperationStatus)).Cast<WorldOperationStatus>())
            {
                bridge.Status = status;
                using var response = await host.Client.GetAsync("api/v1/world-operations/operation-1");
                var operation = JObject.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(status.ToString(), (string?)operation["status"]);
            }
        }

        [Fact]
        public async Task Operation_not_found_is_a_stable_404_problem()
        {
            using var host = CreateHost("Owner");

            using var response = await host.Client.GetAsync("api/v1/world-operations/missing");
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("world_operation_not_found", (string?)problem["code"]);
        }

        [Fact]
        public async Task Domain_conflicts_are_stable_409_problems_without_exception_details()
        {
            var bridge = new RecordingBridge
            {
                EnqueueException = new WorldOperationConflictException("full_map_render_already_active")
            };
            using var host = CreateHost("Owner", bridge: bridge);

            using var response = await PostAsync(
                host.Client,
                "api/v1/world-operations/gc",
                "{\"worldId\":\"world-1\",\"worldVersion\":\"world-version-1\",\"confirmed\":true}");
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("full_map_render_already_active", (string?)problem["code"]);
        }

        [Fact]
        public async Task Invalid_coordinates_and_missing_confirmation_are_422_problems()
        {
            using var host = CreateHost("Owner");

            using var invalidCoordinate = await PostAsync(
                host.Client,
                "api/v1/world-operations/players/move",
                "{\"crossplatformId\":\"player-1\",\"entityId\":1,\"onlineObservedAtUtc\":\"2026-07-27T00:00:00Z\",\"destination\":{\"x\":1,\"y\":1},\"worldId\":\"world-1\",\"worldVersion\":\"world-version-1\",\"confirmed\":true}");
            var invalidProblem = JObject.Parse(await invalidCoordinate.Content.ReadAsStringAsync());
            Assert.Equal((HttpStatusCode)422, invalidCoordinate.StatusCode);
            Assert.Equal("invalid_world_operation_request", (string?)invalidProblem["code"]);

            using var missingConfirmation = await PostAsync(
                host.Client,
                "api/v1/world-operations/gc",
                "{\"worldId\":\"world-1\",\"worldVersion\":\"world-version-1\",\"confirmed\":false}");
            var confirmationProblem = JObject.Parse(await missingConfirmation.Content.ReadAsStringAsync());
            Assert.Equal((HttpStatusCode)422, missingConfirmation.StatusCode);
            Assert.Equal("confirmation_required", (string?)confirmationProblem["code"]);
        }

        [Fact]
        public async Task Persistence_or_projection_failure_is_a_sanitized_503_problem()
        {
            var bridge = new RecordingBridge
            {
                EnqueueException = new InvalidOperationException("database=C:\\private\\jobs.db user=root")
            };
            using var writeHost = CreateHost("Owner", bridge: bridge);
            using var writeResponse = await PostAsync(
                writeHost.Client,
                "api/v1/world-operations/gc",
                "{\"worldId\":\"world-1\",\"worldVersion\":\"world-version-1\",\"confirmed\":true}");
            var writeBody = await writeResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, writeResponse.StatusCode);
            Assert.Equal("world_operation_unavailable", (string?)JObject.Parse(writeBody)["code"]);
            Assert.DoesNotContain("private", writeBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("root", writeBody, StringComparison.OrdinalIgnoreCase);

            using var readHost = CreateHost(
                "Viewer",
                projection: new StubProjection(new InvalidOperationException("save path /private/world")));
            using var readResponse = await readHost.Client.GetAsync("api/v1/world/summary");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, readResponse.StatusCode);
            Assert.Equal(
                "world_read_unavailable",
                (string?)JObject.Parse(await readResponse.Content.ReadAsStringAsync())["code"]);
        }

        [Fact]
        public async Task Map_resource_version_is_queried_without_starting_a_job()
        {
            var bridge = new RecordingBridge();
            using var host = CreateHost("Owner", bridge: bridge);

            using var response = await host.Client.GetAsync("api/v1/map-jobs/resource-version");
            var body = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("world-1", (string?)body["worldId"]);
            Assert.Equal("map-version-1", (string?)body["mapResourceVersion"]);
            Assert.Null(bridge.LastIntent);
        }

        public static IEnumerable<object[]> AllWritePaths()
        {
            foreach (var route in WorldOperationPostRoutes)
                yield return new object[] { "api/v1/world-operations/" + route };
            foreach (var route in MapJobPostRoutes)
                yield return new object[] { "api/v1/map-jobs/" + route };
        }

        private static IEnumerable<string> Routes<TController, TMethodAttribute>()
            where TMethodAttribute : Attribute =>
            typeof(TController)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.GetCustomAttribute<TMethodAttribute>() != null)
                .Select(method => method.GetCustomAttribute<RouteAttribute>()?.Template)
                .Where(template => template != null)
                .Select(template => template!)
                .OrderBy(template => template);

        private static string Prefix<TController>() =>
            typeof(TController).GetCustomAttribute<RoutePrefixAttribute>()?.Prefix ?? string.Empty;

        private static IEnumerable<MethodInfo> PostMethods(Type controllerType) =>
            controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.GetCustomAttribute<HttpPostAttribute>() != null);

        private static void AssertStrictRequestGraph(Type type, ISet<Type> visited)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) ||
                type == typeof(decimal) || type == typeof(DateTimeOffset) ||
                !visited.Add(type))
            {
                return;
            }

            Assert.NotNull(type.GetCustomAttribute<JsonConverterAttribute>());
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var normalized = property.Name.ToLowerInvariant();
                Assert.DoesNotContain("path", normalized, StringComparison.Ordinal);
                Assert.DoesNotContain("xml", normalized, StringComparison.Ordinal);
                Assert.DoesNotContain("script", normalized, StringComparison.Ordinal);
                Assert.DoesNotContain("command", normalized, StringComparison.Ordinal);
                Assert.DoesNotContain("typename", normalized, StringComparison.Ordinal);
                Assert.DoesNotContain("payload", normalized, StringComparison.Ordinal);
                AssertStrictRequestGraph(property.PropertyType, visited);
            }
        }

        private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string json) =>
            client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

        private static HttpTestHost CreateHost(
            string? role,
            RecordingBridge? bridge = null,
            IWorldSnapshotProjection? projection = null)
        {
            bridge ??= new RecordingBridge();
            projection ??= new StubProjection(CreateWorldSnapshot());
            var catalog = new StubCatalog();
            var changeSets = new StubChangeSetMetadataStore();
            var blobs = new StubChangeSetBlobStore();
            var services = new ServiceCollection();

            services.AddSingleton(projection);
            services.AddSingleton<IWorldSnapshotProjection>(projection);
            services.AddSingleton<IWorldToolCatalog>(catalog);
            services.AddSingleton<IWorldOperationJobBridge>(bridge);
            services.AddSingleton<IWorldChangeSetMetadataStore>(changeSets);
            services.AddSingleton<IWorldChangeSetBlobStore>(blobs);
            services.AddSingleton<QueryWorldUseCase>();
            services.AddSingleton<QueryWorldToolCatalogUseCase>();
            services.AddSingleton<DeleteLandClaimUseCase>();
            services.AddSingleton<MoveOnlinePlayerUseCase>();
            services.AddSingleton<MoveWorldEntityUseCase>();
            services.AddSingleton<CopyRegionUseCase>();
            services.AddSingleton<FillRegionUseCase>();
            services.AddSingleton<ClearRegionUseCase>();
            services.AddSingleton<PasteRegionUseCase>();
            services.AddSingleton<SetBlockUseCase>();
            services.AddSingleton<PlacePrefabUseCase>();
            services.AddSingleton<RemovePrefabUseCase>();
            services.AddSingleton<SpawnWorldEntityUseCase>();
            services.AddSingleton<DeleteWorldEntityUseCase>();
            services.AddSingleton<CleanupWorldEntitiesUseCase>();
            services.AddSingleton<ReloadGameResourceUseCase>();
            services.AddSingleton<CollectGameGarbageUseCase>();
            services.AddSingleton<UndoWorldChangeSetUseCase>();
            services.AddSingleton<SubmitMapJobUseCase>();
            services.AddTransient<WorldController>();
            services.AddTransient<WorldOperationsController>();
            services.AddTransient<MapJobsController>();

            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            var configuration = new HttpConfiguration
            {
                DependencyResolver = new MicrosoftDependencyResolver(provider)
            };
            configuration.MapHttpAttributeRoutes();
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            configuration.MessageHandlers.Add(new PrincipalHandler(role));
            configuration.MessageHandlers.Add(new ApiProblemDetailsHandler());
            configuration.EnsureInitialized();
            return new HttpTestHost(provider, configuration);
        }

        private static WorldSnapshot CreateWorldSnapshot()
        {
            var observedAtUtc = new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);
            var container = new ContainerSummary(
                "container-1",
                "container-stable-1",
                "vehicle-stable-1",
                new MapLayerPosition(1, 2, 3),
                MapEntityLoadState.Loaded,
                true,
                10,
                1,
                new[] { new ApprovedWorldItemSummary("item-water", 1, null) });
            return new WorldSnapshot(
                new WorldSummary(
                    AvailabilityState.Available,
                    "world-1",
                    "world-version-1",
                    "seed-1",
                    8192,
                    8192,
                    "v3.0.1-b4",
                    "map-version-1",
                    new MapExtent(-4096, -4096, 4096, 4096),
                    observedAtUtc),
                WorldCollectionSnapshot<LandClaimSummary>.Available(
                    observedAtUtc,
                    new[]
                    {
                        new LandClaimSummary(
                            "claim-1",
                            "claim-stable-1",
                            new MapLayerPosition(10, 20, 30),
                            "owner-1",
                            41,
                            true,
                            observedAtUtc)
                    }),
                WorldCollectionSnapshot<VehicleSummary>.Available(
                    observedAtUtc,
                    new[]
                    {
                        new VehicleSummary(
                            "vehicle-1",
                            "vehicle-stable-1",
                            "entity-vehicle",
                            "owner-1",
                            new MapLayerPosition(1, 2, 3),
                            MapEntityLoadState.Loaded,
                            true,
                            50,
                            3,
                            container)
                    }),
                WorldCollectionSnapshot<DroneSummary>.Available(
                    observedAtUtc,
                    new[]
                    {
                        new DroneSummary(
                            "drone-1",
                            "drone-stable-1",
                            "entity-drone",
                            "owner-1",
                            new MapLayerPosition(4, 5, 6),
                            MapEntityLoadState.Loaded,
                            false,
                            2,
                            null)
                    }),
                WorldCollectionSnapshot<ContainerSummary>.Available(
                    observedAtUtc,
                    new[] { container }));
        }

        private sealed class StubProjection : IWorldSnapshotProjection
        {
            private readonly WorldSnapshot? snapshot;
            private readonly Exception? exception;

            public StubProjection(WorldSnapshot snapshot) => this.snapshot = snapshot;
            public StubProjection(Exception exception) => this.exception = exception;

            public WorldSnapshot Query()
            {
                if (exception != null) throw exception;
                return snapshot!;
            }
        }

        private sealed class StubCatalog : IWorldToolCatalog
        {
            public WorldToolCatalogSnapshot Read() => WorldToolCatalogSnapshot.Available(
                "catalog-1",
                new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
                new[] { "block-stone" },
                new[] { "prefab-1" },
                new[] { "entity-zombie" });
        }

        private sealed class RecordingBridge : IWorldOperationJobBridge
        {
            private static readonly Guid JobId = new Guid("11111111-1111-1111-1111-111111111111");

            public Exception? EnqueueException { get; set; }
            public WorldOperationIntent? LastIntent { get; private set; }
            public WorldOperationStatus Status { get; set; } = WorldOperationStatus.Queued;

            public WorldOperationReceipt Enqueue(WorldOperationIntent intent)
            {
                if (EnqueueException != null) throw EnqueueException;
                LastIntent = intent;
                return new WorldOperationReceipt(
                    "operation-1",
                    JobId,
                    WorldOperationStatus.Queued,
                    intent.CorrelationId,
                    intent.CreatedAtUtc);
            }

            public WorldOperationRecord Get(string operationId)
            {
                if (string.Equals(operationId, "missing", StringComparison.Ordinal))
                    throw new KeyNotFoundException("database=/private/world.db");
                return new WorldOperationRecord(
                    operationId,
                    JobId,
                    "subject-1",
                    WorldOperationKind.CollectGarbage,
                    "world-1",
                    "world-version-1",
                    "map-version-1",
                    "correlation-1",
                    "Collect game garbage",
                    false,
                    null,
                    Status,
                    new WorldOperationProgress(1, 2),
                    Status == WorldOperationStatus.Failed ? "world_operation_failed" : null,
                    new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
                    null,
                    null);
            }

            public WorldOperationPage Query(WorldOperationQuery query) =>
                new WorldOperationPage(Array.Empty<WorldOperationRecord>(), null);

            public bool RequestCancellation(string operationId, string actorSubject) => false;
        }

        private sealed class StubChangeSetMetadataStore : IWorldChangeSetMetadataStore
        {
            public WorldChangeSetDescriptor Create(WorldChangeSetDraft draft) =>
                throw new NotSupportedException();
            public WorldChangeSetDescriptor Read(string changeSetId) =>
                throw new KeyNotFoundException();
            public void MarkApplied(string changeSetId, string afterHash) =>
                throw new NotSupportedException();
        }

        private sealed class StubChangeSetBlobStore : IWorldChangeSetBlobStore
        {
            public WorldChangeSetBlobReceipt Write(WorldChangeSetBlobDraft draft) =>
                throw new NotSupportedException();
            public WorldChangeSetBlobReadResult Read(string storageResourceId, string expectedHash) =>
                throw new KeyNotFoundException();
        }

        private sealed class PrincipalHandler : DelegatingHandler
        {
            private readonly string? role;

            public PrincipalHandler(string? role) => this.role = role;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var identity = role == null
                    ? new ClaimsIdentity()
                    : new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, "subject-1"),
                            new Claim(ClaimTypes.Role, role)
                        },
                        "Test");
                var principal = new ClaimsPrincipal(identity);
                var owin = new OwinContext();
                owin.Authentication.User = principal;
                request.SetOwinContext(owin);
                request.GetRequestContext().Principal = principal;
                return base.SendAsync(request, cancellationToken);
            }
        }

        private sealed class HttpTestHost : IDisposable
        {
            private readonly ServiceProvider provider;
            private readonly HttpServer server;

            public HttpTestHost(ServiceProvider provider, HttpConfiguration configuration)
            {
                this.provider = provider;
                server = new HttpServer(configuration);
                Client = new HttpClient(server)
                {
                    BaseAddress = new Uri("http://localhost/")
                };
            }

            public HttpClient Client { get; }

            public void Dispose()
            {
                Client.Dispose();
                server.Dispose();
                provider.Dispose();
            }
        }
    }
}
