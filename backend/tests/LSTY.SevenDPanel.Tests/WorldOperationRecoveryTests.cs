using System;
using System.Collections.Generic;
using System.IO;
using Dapper;
using LSTY.SevenDPanel.Adapters.Local.MapTiles;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.WorldOperations;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World;
using LSTY.SevenDPanel.Application.WorldOperations;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class WorldOperationRecoveryTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 26, 16, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Running_world_operation_becomes_unknown_while_queued_work_is_preserved()
        {
            using var database = new TemporaryDatabase();
            var bridge = new SqliteWorldOperationJobBridge(
                database.ConnectionFactory,
                () => Now);
            var running = bridge.Enqueue(Intent("running"));
            var queued = bridge.Enqueue(Intent("queued"));
            using (var connection = database.ConnectionFactory.Open())
            {
                connection.Execute(
                    "UPDATE jobs SET status = 'Running', started_at_utc = @StartedAtUtc, row_version = 1 WHERE id = @Id;",
                    new
                    {
                        StartedAtUtc = Now.AddMinutes(-1).ToUnixTimeMilliseconds(),
                        Id = running.JobId.ToString("D")
                    });
            }

            var recovered = new SqliteWorldOperationStore(database.ConnectionFactory)
                .RecoverRunning(Now);

            Assert.Equal(1, recovered);
            Assert.Equal(WorldOperationStatus.ResultUnknown, bridge.Get(running.OperationId).Status);
            Assert.Equal("world_operation_restart_result_unknown", bridge.Get(running.OperationId).ErrorCode);
            Assert.Equal(WorldOperationStatus.Queued, bridge.Get(queued.OperationId).Status);
        }

        [Fact]
        public void Runtime_recovers_once_before_start_and_only_delegates_ready_and_stop()
        {
            var order = new List<string>();
            var runtime = new WorldOperationRuntime(
                new RecordingRecoveryStore(order),
                new RecordingRuntime(order),
                () => Now);

            runtime.Start();
            runtime.Start();
            runtime.MarkGameReady();
            runtime.Stop();

            Assert.Equal(
                new[] { "recover", "inner:start", "inner:ready", "inner:stop" },
                order);
        }

        [Fact]
        public async System.Threading.Tasks.Task Job_handler_uses_the_fixed_kind_branch_and_sanitizes_rejection()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-world-handler-" + Guid.NewGuid().ToString("N"));
            var staging = Path.Combine(root, "staging");
            var published = Path.Combine(root, "published");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(published);
            try
            {
                var jobId = Guid.NewGuid();
                var intent = new WorldOperationIntent(
                    "owner",
                    WorldOperationKind.MoveOnlinePlayer,
                    "world",
                    "world-v1",
                    null,
                    "invalid-player-target",
                    "move player",
                    false,
                    new WorldMaintenanceOperationTarget(null),
                    Now);
                var executionStore = new ExecutionStore(
                    new WorldOperationExecutionRecord("operation", jobId, intent));
                var changeSets = new UnusedChangeSetStore();
                var blobs = new UnusedBlobStore();
                var handler = new WorldOperationJobHandler(
                    executionStore,
                    new SevenDaysMapWorldOperationHandler(staging),
                    new LocalMapResourcePublisher(staging, published),
                    new SevenDaysRegionOperationHandler(changeSets, blobs),
                    new SevenDaysBlockPrefabOperationHandler(changeSets, blobs),
                    new SevenDaysEntityMaintenanceHandler(),
                    new SevenDaysRuntimeMaintenanceHandler(),
                    new SevenDaysUndoOperationHandler(changeSets, blobs),
                    () => Now);

                var result = await handler.ExecuteAsync(jobId, default);

                Assert.Equal(WorldOperationStatus.Failed, result.Status);
                Assert.Equal(SevenDaysMapWorldOperationResult.TargetInvalid, result.ErrorCode);
                Assert.Null(result.Progress);
                Assert.Equal(0, executionStore.RollbackFailureCount);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static WorldOperationIntent Intent(string correlationId) =>
            new WorldOperationIntent(
                "owner",
                WorldOperationKind.ReloadItems,
                "world",
                "world-v1",
                null,
                correlationId,
                "reload items",
                false,
                new WorldMaintenanceOperationTarget(null),
                Now.AddMinutes(-2));

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingRecoveryStore : IWorldOperationRecoveryStore
        {
            private readonly List<string> order;

            public RecordingRecoveryStore(List<string> order) => this.order = order;

            public int RecoverRunning(DateTimeOffset recoveredAtUtc)
            {
                Assert.Equal(Now, recoveredAtUtc);
                order.Add("recover");
                return 0;
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly List<string> order;

            public RecordingRuntime(List<string> order) => this.order = order;

            public void Start() => order.Add("inner:start");
            public void MarkGameReady() => order.Add("inner:ready");
            public void Stop() => order.Add("inner:stop");
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class ExecutionStore : IWorldOperationExecutionStore
        {
            private readonly WorldOperationExecutionRecord execution;

            public ExecutionStore(WorldOperationExecutionRecord execution) =>
                this.execution = execution;

            public int RollbackFailureCount { get; private set; }

            public WorldOperationExecutionRecord ReadForExecution(Guid jobId)
            {
                Assert.Equal(execution.JobId, jobId);
                return execution;
            }

            public void MarkRollbackFailed(
                Guid jobId,
                string errorCode,
                DateTimeOffset failedAtUtc) => RollbackFailureCount++;
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class UnusedChangeSetStore : IWorldChangeSetMetadataStore
        {
            public WorldChangeSetDescriptor Create(WorldChangeSetDraft draft) =>
                throw new InvalidOperationException("unexpected change-set create");

            public WorldChangeSetDescriptor Read(string changeSetId) =>
                throw new InvalidOperationException("unexpected change-set read");

            public void MarkApplied(string changeSetId, string afterHash) =>
                throw new InvalidOperationException("unexpected change-set update");
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class UnusedBlobStore : IWorldChangeSetBlobStore
        {
            public WorldChangeSetBlobReceipt Write(WorldChangeSetBlobDraft draft) =>
                throw new InvalidOperationException("unexpected blob write");

            public WorldChangeSetBlobReadResult Read(
                string storageResourceId,
                string expectedHash) =>
                throw new InvalidOperationException("unexpected blob read");
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string root;

            public TemporaryDatabase()
            {
                root = Path.Combine(
                    Path.GetTempPath(),
                    "7dpanel-world-recovery-" + Guid.NewGuid().ToString("N"));
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
