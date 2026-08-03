using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World;
using LSTY.SevenDPanel.Application.WorldOperations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "SevenDays")]
    public sealed class RuntimeMaintenanceOperationTests
    {
        private static readonly DateTimeOffset RequestedAtUtc =
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);

        [Theory]
        [InlineData(WorldReloadResourceKind.Blocks, WorldOperationKind.ReloadBlocks)]
        [InlineData(WorldReloadResourceKind.Items, WorldOperationKind.ReloadItems)]
        [InlineData(WorldReloadResourceKind.EntityClasses, WorldOperationKind.ReloadEntityClasses)]
        [InlineData(WorldReloadResourceKind.Prefabs, WorldOperationKind.ReloadPrefabs)]
        public void Reload_submission_maps_only_the_closed_resource_kind(
            WorldReloadResourceKind resourceKind,
            WorldOperationKind expectedKind)
        {
            var bridge = new RecordingBridge();

            new ReloadGameResourceUseCase(bridge).Execute(new ReloadGameResourceRequest(
                "operator-1", "world-1", "world-v1", "map-v1", resourceKind,
                "reload-1", true, true, RequestedAtUtc));

            Assert.Equal(expectedKind, bridge.Intent!.Kind);
            var target = Assert.IsType<WorldMaintenanceOperationTarget>(bridge.Intent.Target);
            Assert.Null(target.EntityTypeResourceId);
        }

        [Fact]
        public void Garbage_collection_submission_has_no_command_text_or_parameters()
        {
            var bridge = new RecordingBridge();
            var useCase = new CollectGameGarbageUseCase(bridge);

            Assert.Throws<WorldOperationConfirmationRequiredException>(() =>
                useCase.Execute(new CollectGameGarbageRequest(
                    "operator-1", "world-1", "world-v1", "map-v1",
                    "gc-1", false, RequestedAtUtc)));

            useCase.Execute(new CollectGameGarbageRequest(
                "operator-1", "world-1", "world-v1", "map-v1",
                "gc-1", true, RequestedAtUtc));

            Assert.Equal(WorldOperationKind.CollectGarbage, bridge.Intent!.Kind);
            var target = Assert.IsType<WorldMaintenanceOperationTarget>(bridge.Intent.Target);
            Assert.Null(target.EntityTypeResourceId);
        }

        [Theory]
        [InlineData(WorldOperationKind.ReloadBlocks, "blocks")]
        [InlineData(WorldOperationKind.ReloadItems, "items")]
        [InlineData(WorldOperationKind.ReloadEntityClasses, "entity-classes")]
        [InlineData(WorldOperationKind.ReloadPrefabs, "prefabs")]
        public async Task Reload_uses_an_explicit_exhaustive_dispatch_branch(
            WorldOperationKind kind,
            string expectedAction)
        {
            var trace = new List<string>();
            var handler = Handler(
                _ =>
                {
                    trace.Add("context");
                    return Context(trace);
                },
                trace);

            var result = await handler.HandleAsync(Intent(kind), CancellationToken.None);

            Assert.Equal(SevenDaysRuntimeMaintenanceOutcome.Succeeded, result.Outcome);
            Assert.Null(result.ErrorCode);
            Assert.Equal(new[] { "dispatch", "context", expectedAction }, trace);
        }

        [Fact]
        public async Task World_version_is_revalidated_inside_dispatch_before_reload()
        {
            var trace = new List<string>();
            var handler = Handler(
                _ =>
                {
                    trace.Add("context");
                    return Context(trace, worldVersion: "world-v2");
                },
                trace);

            var result = await handler.HandleAsync(
                Intent(WorldOperationKind.ReloadBlocks),
                CancellationToken.None);

            Assert.Equal(SevenDaysRuntimeMaintenanceOutcome.Rejected, result.Outcome);
            Assert.Equal(SevenDaysRuntimeMaintenanceResult.WorldVersionChanged, result.ErrorCode);
            Assert.Equal(new[] { "dispatch", "context" }, trace);
        }

        [Fact]
        public async Task Garbage_collection_timeout_is_sanitized_result_unknown()
        {
            var handler = new SevenDaysRuntimeMaintenanceHandler(
                (_, _, _, _) => Task.FromException<SevenDaysRuntimeMaintenanceResult>(
                    new TimeoutException("C:\\private\\runtime.xml")),
                _ => throw new InvalidOperationException("must not capture context"));

            var result = await handler.HandleAsync(
                Intent(WorldOperationKind.CollectGarbage),
                CancellationToken.None);

            Assert.Equal(SevenDaysRuntimeMaintenanceOutcome.ResultUnknown, result.Outcome);
            Assert.Equal(SevenDaysRuntimeMaintenanceResult.ResultUnknown, result.ErrorCode);
            Assert.DoesNotContain("private", result.ErrorCode!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Exception_after_reload_side_effect_start_is_result_unknown()
        {
            var handler = Handler(
                _ => SevenDaysRuntimeMaintenanceContext.Available(
                    "world-1", "world-v1", "map-v1",
                    reloadBlocks: () => throw new InvalidOperationException(
                        "C:\\private\\blocks.xml secret"),
                    reloadItems: () => true,
                    reloadEntityClasses: () => true,
                    reloadPrefabs: () => true,
                    collectGarbage: () => true),
                new List<string>());

            var result = await handler.HandleAsync(
                Intent(WorldOperationKind.ReloadBlocks),
                CancellationToken.None);

            Assert.Equal(SevenDaysRuntimeMaintenanceOutcome.ResultUnknown, result.Outcome);
            Assert.Equal(SevenDaysRuntimeMaintenanceResult.ResultUnknown, result.ErrorCode);
            Assert.DoesNotContain("private", result.ErrorCode!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Runtime_target_cannot_smuggle_a_path_xml_or_console_command()
        {
            var dispatched = false;
            var handler = new SevenDaysRuntimeMaintenanceHandler(
                (_, _, _, _) =>
                {
                    dispatched = true;
                    throw new InvalidOperationException("must not dispatch");
                },
                _ => throw new InvalidOperationException("must not capture context"));
            var intent = new WorldOperationIntent(
                "operator-1", WorldOperationKind.ReloadBlocks,
                "world-1", "world-v1", "map-v1", "correlation-1",
                "Approved runtime maintenance", false,
                new WorldMaintenanceOperationTarget("C:\\server\\blocks.xml reloadxml"),
                RequestedAtUtc);

            var result = await handler.HandleAsync(intent, CancellationToken.None);

            Assert.Equal(SevenDaysRuntimeMaintenanceOutcome.Rejected, result.Outcome);
            Assert.Equal(SevenDaysRuntimeMaintenanceResult.TargetInvalid, result.ErrorCode);
            Assert.False(dispatched);
        }

        private static SevenDaysRuntimeMaintenanceHandler Handler(
            Func<WorldOperationIntent, SevenDaysRuntimeMaintenanceContext?> captureContext,
            ICollection<string> trace) =>
            new SevenDaysRuntimeMaintenanceHandler(
                (name, action, timeout, _) =>
                {
                    Assert.Equal("7DPanel.World.RuntimeMaintenance", name);
                    Assert.Equal(TimeSpan.FromSeconds(5), timeout);
                    trace.Add("dispatch");
                    return Task.FromResult(action());
                },
                captureContext);

        private static SevenDaysRuntimeMaintenanceContext Context(
            ICollection<string> trace,
            string worldVersion = "world-v1") =>
            SevenDaysRuntimeMaintenanceContext.Available(
                "world-1",
                worldVersion,
                "map-v1",
                reloadBlocks: () => Add(trace, "blocks"),
                reloadItems: () => Add(trace, "items"),
                reloadEntityClasses: () => Add(trace, "entity-classes"),
                reloadPrefabs: () => Add(trace, "prefabs"),
                collectGarbage: () => Add(trace, "gc"));

        private static bool Add(ICollection<string> trace, string value)
        {
            trace.Add(value);
            return true;
        }

        private static WorldOperationIntent Intent(WorldOperationKind kind) =>
            new WorldOperationIntent(
                "operator-1", kind, "world-1", "world-v1", "map-v1",
                "correlation-1", "Approved runtime maintenance", false,
                new WorldMaintenanceOperationTarget(null), RequestedAtUtc);

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "SevenDays")]

        private sealed class RecordingBridge : IWorldOperationJobBridge
        {
            public WorldOperationIntent? Intent { get; private set; }

            public WorldOperationReceipt Enqueue(WorldOperationIntent intent)
            {
                Intent = intent;
                return new WorldOperationReceipt(
                    "operation-1",
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    WorldOperationStatus.Queued,
                    intent.CorrelationId,
                    intent.CreatedAtUtc);
            }

            public WorldOperationRecord Get(string operationId) => throw new NotSupportedException();
            public WorldOperationPage Query(WorldOperationQuery query) => throw new NotSupportedException();
            public bool RequestCancellation(string operationId, string actorSubject) => false;
        }
    }
}
