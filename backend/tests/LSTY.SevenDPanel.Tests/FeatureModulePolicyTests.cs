using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Modules;
using LSTY.SevenDPanel.Application.Modules;
using LSTY.SevenDPanel.Application.WorldOperations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Platform")]
    [Trait("Boundary", "Bootstrap")]
    public sealed class FeatureModulePolicyTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 26, 17, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Policy_is_a_closed_acyclic_set_of_seventeen_compile_time_modules()
        {
            var expected = Enum.GetValues(typeof(FeatureModuleId))
                .Cast<FeatureModuleId>()
                .OrderBy(value => value)
                .ToArray();
            var descriptors = FeatureModulePolicy.All;

            Assert.Equal(17, expected.Length);
            Assert.Equal(expected, descriptors.Select(item => item.Id).OrderBy(value => value));
            Assert.All(descriptors, descriptor =>
            {
                Assert.NotEmpty(descriptor.HealthSource);
                Assert.NotEmpty(descriptor.DataRetentionSummary);
                Assert.NotEmpty(descriptor.ConsumerIds);
                Assert.DoesNotContain(descriptor.Id, descriptor.Dependencies);
            });
            Assert.False(FeatureModulePolicy.Describe(
                FeatureModuleId.IdentityAndAuthorization).IsToggleable);
            Assert.False(FeatureModulePolicy.Describe(FeatureModuleId.Audit).IsToggleable);
            Assert.False(FeatureModulePolicy.Describe(FeatureModuleId.RuntimeHealth).IsToggleable);
            Assert.False(FeatureModulePolicy.Describe(FeatureModuleId.Overview).IsToggleable);

            Assert.All(expected, module => AssertNoCycle(module, new HashSet<FeatureModuleId>()));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FeatureModulePolicy.Describe((FeatureModuleId)999));
        }

        [Fact]
        public void Disabled_module_blocks_new_commands_but_keeps_state_available_for_history_queries()
        {
            var store = new MemoryStore();
            var gate = new FeatureModuleGate(store);
            store.Save(new FeatureModuleStateChange(
                FeatureModuleId.WorldTools,
                false,
                FeatureModuleLifecycleState.Disabled,
                "owner",
                "disable-world-tools",
                Now,
                0));

            var error = Assert.Throws<FeatureModuleDisabledException>(() =>
                gate.RequireEnabled(FeatureModuleId.WorldTools));

            Assert.Equal(FeatureModuleId.WorldTools, error.ModuleId);
            Assert.False(store.Get(FeatureModuleId.WorldTools).IsEnabled);
        }

        [Fact]
        public void Sqlite_store_persists_state_and_uses_row_version_compare_and_set()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteFeatureModuleStateStore(database.ConnectionFactory);

            var disabled = store.Save(new FeatureModuleStateChange(
                FeatureModuleId.Discord,
                false,
                FeatureModuleLifecycleState.Disabled,
                "owner",
                "disable-discord",
                Now,
                0));
            var enabled = store.Save(new FeatureModuleStateChange(
                FeatureModuleId.Discord,
                true,
                FeatureModuleLifecycleState.Enabled,
                "owner",
                "enable-discord",
                Now.AddMinutes(1),
                disabled.RowVersion));

            Assert.False(disabled.IsEnabled);
            Assert.True(enabled.IsEnabled);
            Assert.Equal(2, enabled.RowVersion);
            Assert.Equal(enabled, store.Get(FeatureModuleId.Discord));
            Assert.Throws<FeatureModuleStateConflictException>(() =>
                store.Save(new FeatureModuleStateChange(
                    FeatureModuleId.Discord,
                    false,
                    FeatureModuleLifecycleState.Disabled,
                    "owner",
                    "stale-disable",
                    Now.AddMinutes(2),
                    disabled.RowVersion)));
        }

        [Fact]
        public void Use_cases_protect_core_dependencies_and_drain_active_work_without_deleting_state()
        {
            var store = new MemoryStore();
            var activity = new ActivityQuery();
            var useCases = new FeatureModuleUseCases(store, activity, () => Now);

            Assert.Throws<FeatureModuleNotToggleableException>(() =>
                useCases.SetEnabled(new SetFeatureModuleStateRequest(
                    FeatureModuleId.Audit,
                    false,
                    "owner",
                    "disable-audit",
                    0)));
            Assert.Throws<FeatureModuleDependencyException>(() =>
                useCases.SetEnabled(new SetFeatureModuleStateRequest(
                    FeatureModuleId.GameResources,
                    false,
                    "owner",
                    "disable-resources",
                    0)));

            activity.Active.Add(FeatureModuleId.WorldTools);
            var draining = useCases.SetEnabled(new SetFeatureModuleStateRequest(
                FeatureModuleId.WorldTools,
                false,
                "owner",
                "disable-world-tools",
                0));

            Assert.False(draining.IsEnabled);
            Assert.Equal(FeatureModuleLifecycleState.Draining, draining.LifecycleState);
            Assert.Same(draining, store.Get(FeatureModuleId.WorldTools));
        }

        [Fact]
        public void World_operation_bridge_gates_every_new_submission_but_keeps_history_readable()
        {
            var states = new MemoryStore();
            states.Save(new FeatureModuleStateChange(
                FeatureModuleId.WorldTools,
                false,
                FeatureModuleLifecycleState.Disabled,
                "owner",
                "disable-world-tools-for-bridge",
                Now,
                0));
            var inner = new RecordingWorldOperationBridge();
            var bridge = new FeatureModuleWorldOperationJobBridge(
                inner,
                new FeatureModuleGate(states));

            Assert.Throws<FeatureModuleDisabledException>(() => bridge.Enqueue(
                new WorldOperationIntent(
                    "owner",
                    WorldOperationKind.ReloadItems,
                    "world",
                    "world-v1",
                    null,
                    "blocked-operation",
                    "reload items",
                    false,
                    new WorldMaintenanceOperationTarget(null),
                    Now)));

            Assert.Equal(0, inner.EnqueueCount);
            Assert.Equal("history", bridge.Get("history").OperationId);
        }

        private static void AssertNoCycle(
            FeatureModuleId module,
            ISet<FeatureModuleId> path)
        {
            Assert.True(path.Add(module), "Feature module dependency cycle: " + module);
            foreach (var dependency in FeatureModulePolicy.Describe(module).Dependencies)
                AssertNoCycle(dependency, path);
            path.Remove(module);
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Bootstrap")]

        private sealed class MemoryStore : IFeatureModuleStateStore
        {
            private readonly Dictionary<FeatureModuleId, FeatureModuleState> states = new();

            public FeatureModuleState Get(FeatureModuleId moduleId) =>
                states.TryGetValue(moduleId, out var state)
                    ? state
                    : FeatureModuleState.DefaultEnabled(moduleId);

            public IReadOnlyList<FeatureModuleState> List() =>
                Enum.GetValues(typeof(FeatureModuleId))
                    .Cast<FeatureModuleId>()
                    .Select(Get)
                    .ToArray();

            public FeatureModuleState Save(FeatureModuleStateChange change)
            {
                var current = Get(change.ModuleId);
                if (current.RowVersion != change.ExpectedRowVersion)
                    throw new FeatureModuleStateConflictException(change.ModuleId);
                var next = new FeatureModuleState(
                    change.ModuleId,
                    change.IsEnabled,
                    change.LifecycleState,
                    change.UpdatedBy,
                    change.CorrelationId,
                    change.UpdatedAtUtc,
                    current.RowVersion + 1);
                states[change.ModuleId] = next;
                return next;
            }
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Bootstrap")]

        private sealed class ActivityQuery : IFeatureModuleActivityQuery
        {
            public ISet<FeatureModuleId> Active { get; } = new HashSet<FeatureModuleId>();

            public bool HasActiveWork(FeatureModuleId moduleId) => Active.Contains(moduleId);
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Bootstrap")]

        private sealed class RecordingWorldOperationBridge : IWorldOperationJobBridge
        {
            public int EnqueueCount { get; private set; }

            public WorldOperationReceipt Enqueue(WorldOperationIntent intent)
            {
                EnqueueCount++;
                return new WorldOperationReceipt(
                    "new",
                    Guid.NewGuid(),
                    WorldOperationStatus.Queued,
                    intent.CorrelationId,
                    intent.CreatedAtUtc);
            }

            public WorldOperationRecord Get(string operationId) => new WorldOperationRecord(
                operationId,
                Guid.NewGuid(),
                "owner",
                WorldOperationKind.ReloadItems,
                "world",
                "world-v1",
                null,
                "history-correlation",
                "history",
                false,
                null,
                WorldOperationStatus.Succeeded,
                null,
                null,
                Now,
                Now,
                Now);

            public WorldOperationPage Query(WorldOperationQuery query) =>
                new WorldOperationPage(Array.Empty<WorldOperationRecord>(), null);

            public bool RequestCancellation(string operationId, string actorSubject) => false;
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Bootstrap")]

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string root;

            public TemporaryDatabase()
            {
                root = Path.Combine(
                    Path.GetTempPath(),
                    "7dpanel-feature-modules-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                ConnectionFactory = new SqliteConnectionFactory(
                    Path.Combine(root, "panel.db"));
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
