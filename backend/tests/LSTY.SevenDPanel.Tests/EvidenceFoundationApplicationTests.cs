using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.GameEvents;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class EvidenceFoundationApplicationTests
    {
        [Fact]
        public void Metrics_distinguish_real_zero_from_null_warning_and_support_nullable_value_types()
        {
            var zero = new ObservedMetric<int?>(0, "World.Players.Count", "count", Utc(1), null);
            var failed = new ObservedMetric<int?>(
                null,
                "World.Players.Count",
                "count",
                Utc(1),
                RuntimeMetricWarningCode.ReadFailed);
            var unsupported = new ObservedMetric<bool?>(
                null,
                "World.aiDirector.BloodMoonComponent.BloodMoonActive",
                "boolean",
                Utc(1),
                RuntimeMetricWarningCode.Unsupported);

            Assert.Equal(0, zero.Value);
            Assert.Null(zero.Warning);
            Assert.Null(failed.Value);
            Assert.Equal(RuntimeMetricWarningCode.ReadFailed, failed.Warning);
            Assert.Null(unsupported.Value);
            Assert.Equal(RuntimeMetricWarningCode.Unsupported, unsupported.Warning);
        }

        [Fact]
        public void Metrics_reject_inconsistent_warning_semantics()
        {
            Assert.Throws<ArgumentException>(() =>
                new ObservedMetric<int?>(null, "source", "count", Utc(1), null));
            Assert.Throws<ArgumentException>(() =>
                new ObservedMetric<int?>(1, "source", "count", Utc(1), RuntimeMetricWarningCode.ReadFailed));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ObservedMetric<int?>(null, "source", "count", Utc(1), (RuntimeMetricWarningCode)99));
        }

        [Fact]
        public void Game_runtime_metrics_exposes_exactly_the_fixed_typed_properties()
        {
            var metrics = new GameRuntimeMetrics(
                Available("12:00", "World.worldTime", "game-clock"),
                Available<bool?>(false, "BloodMoonActive", "boolean"),
                Available<double?>(60d, "GameManager.frameTime", "frames/second"),
                Available<int?>(0, "World.Players.Count", "count"),
                Available<int?>(12, "GameManager.persistentPlayerCount", "count"),
                Available<int?>(3, "EntityAnimal", "count"),
                Available<int?>(4, "EntityZombie", "count"),
                Available<int?>(7, "World.Entities", "count"),
                Available<int?>(8, "Chunk.InstanceCount", "count"),
                Available<int?>(2, "EntityItem", "count"),
                Available<long?>(1024L, "GC.GetTotalMemory(false)", "bytes"));

            Assert.Equal(
                new[]
                {
                    "ActiveEntityCount",
                    "AnimalCount",
                    "ChunkCount",
                    "DroppedItemCount",
                    "FramesPerSecond",
                    "GameDayTime",
                    "GameMemoryBytes",
                    "HistoricalPlayerCount",
                    "HostileEntityCount",
                    "IsBloodMoon",
                    "OnlinePlayerCount"
                },
                typeof(GameRuntimeMetrics).GetProperties().Select(property => property.Name).OrderBy(name => name));
            Assert.IsType<ObservedMetric<int?>>(metrics.OnlinePlayerCount);
            Assert.IsType<ObservedMetric<long?>>(metrics.GameMemoryBytes);
        }

        [Fact]
        public void Every_evidence_timestamp_requires_utc()
        {
            var local = new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.FromHours(8));
            var subject = new GameEventSubject("EOS_1", "Steam_1", 7, "Alice");
            var mute = Mute("EOS_1", null, Utc(1));

            Action[] constructions =
            {
                () => new ObservedMetric<int?>(1, "source", "count", local, null),
                () => new GameEventRecord(GuidString(), GameEventType.PlayerJoined, local, Utc(1), subject, null, null),
                () => new GameEventRecord(GuidString(), GameEventType.PlayerJoined, Utc(1), local, subject, null, null),
                () => new GameEventGap(GuidString(), GameEventGapReason.QueueFull, local, null, 1),
                () => new GameEventGap(GuidString(), GameEventGapReason.QueueFull, Utc(1), local, 1),
                () => new GameEventCursor(local, GuidString()),
                () => new UnifiedAuditEntry("chat", "1", "owner", "EOS_1", "mute", local, "Succeeded", null, false),
                () => new AuditSourceGap("chat", local, null, 1, "QueueFull"),
                () => new UnifiedAuditCursor(local, "chat", "1"),
                () => new UnifiedAuditFilter(20, local, null, null, null, null, null, null, null),
                () => new ChatMuteRecord("EOS_1", "Alice", "reason", local, "owner", Utc(1), "owner", Utc(1)),
                () => new ChatMuteRecord("EOS_1", "Alice", "reason", null, "owner", local, "owner", Utc(1)),
                () => new ChatMuteOperation(GuidString(), ChatMuteOperationKind.Create, "EOS_1", "owner", local, "Succeeded", null, null, "reason"),
                () => new ChatMuteCursor(local, "EOS_1"),
                () => mute.IsActiveAt(local)
            };

            foreach (var construction in constructions)
                Assert.Throws<ArgumentException>(construction);
        }

        [Fact]
        public void Event_types_gap_reasons_and_server_generated_ids_are_fixed()
        {
            Assert.Equal(
                new[] { "PlayerJoined", "PlayerLeft", "PlayerKilledEntity", "PlayerDied" },
                Enum.GetNames(typeof(GameEventType)));
            Assert.Equal(
                new[] { "QueueFull", "StoreFailure", "DrainTimeout" },
                Enum.GetNames(typeof(GameEventGapReason)));

            var record = GameEventRecord.Create(
                GameEventType.PlayerJoined,
                Utc(1),
                Utc(1),
                new GameEventSubject("EOS_1", "Steam_1", 7, "Alice"),
                null,
                null);

            Assert.True(Guid.TryParseExact(record.EventId, "D", out _));
            Assert.Throws<ArgumentException>(() =>
                new GameEventRecord("client-id", GameEventType.PlayerJoined, Utc(1), Utc(1), record.Actor, null, null));
        }

        [Fact]
        public void Event_subject_never_promotes_display_name_or_entity_id_to_stable_identity()
        {
            var first = new GameEventSubject(null, "Steam_1", 7, "Same Name");
            var second = new GameEventSubject(null, "Steam_2", 7, "Same Name");
            var sameMetadata = new GameEventSubject(null, "Steam_1", 7, "Same Name");
            var stable = new GameEventSubject("EOS_1", "Steam_1", 7, "Same Name");

            Assert.Null(first.StableIdentity);
            Assert.Null(second.StableIdentity);
            Assert.NotEqual(first, second);
            Assert.NotEqual(first, sameMetadata);
            Assert.Equal("EOS_1", stable.StableIdentity);
            Assert.DoesNotContain(
                typeof(GameEventQuery).GetProperties(),
                property => property.Name.Contains("DisplayName") || property.Name.Contains("EntityId"));
        }

        [Fact]
        public void Event_cursor_orders_occurred_time_then_event_id_descending()
        {
            var newest = new GameEventCursor(Utc(3), "ffffffff-ffff-ffff-ffff-ffffffffffff");
            var sameTimeHigherId = new GameEventCursor(Utc(2), "ffffffff-ffff-ffff-ffff-ffffffffffff");
            var sameTimeLowerId = new GameEventCursor(Utc(2), "00000000-0000-0000-0000-000000000001");

            var ordered = new[] { sameTimeLowerId, newest, sameTimeHigherId }.OrderBy(cursor => cursor).ToArray();

            Assert.Equal(new[] { newest, sameTimeHigherId, sameTimeLowerId }, ordered);
        }

        [Fact]
        public void Game_event_page_keeps_gaps_separate_from_events()
        {
            var record = GameEventRecord.Create(
                GameEventType.PlayerLeft,
                Utc(2),
                Utc(2),
                new GameEventSubject(null, null, 7, "Alice"),
                null,
                false);
            var cursor = new GameEventCursor(record.OccurredAtUtc, record.EventId);
            var gap = new GameEventGap(GuidString(), GameEventGapReason.QueueFull, Utc(1), Utc(2), 3);
            var page = new GameEventPage(new[] { record }, cursor, new[] { gap });

            Assert.Same(record, Assert.Single(page.Events));
            Assert.Same(gap, Assert.Single(page.Gaps));
            Assert.Same(cursor, page.NextCursor);
            Assert.False(typeof(GameEventRecord).IsAssignableFrom(typeof(GameEventGap)));
        }

        [Fact]
        public void Audit_cursor_contains_only_the_fixed_descending_sort_tuple()
        {
            Assert.Equal(
                new[] { "OccurredAtUtc", "SourceId", "SourceKind" },
                typeof(UnifiedAuditCursor).GetProperties().Select(property => property.Name).OrderBy(name => name));

            var newest = new UnifiedAuditCursor(Utc(3), "chat", "1");
            var higherSource = new UnifiedAuditCursor(Utc(2), "server", "1");
            var higherId = new UnifiedAuditCursor(Utc(2), "chat", "z");
            var lowerId = new UnifiedAuditCursor(Utc(2), "chat", "a");
            var ordered = new[] { lowerId, higherId, newest, higherSource }.OrderBy(cursor => cursor).ToArray();

            Assert.Equal(new[] { newest, higherSource, higherId, lowerId }, ordered);
            Assert.DoesNotContain(
                typeof(UnifiedAuditCursor).GetProperties(),
                property => property.Name.IndexOf("Sql", StringComparison.OrdinalIgnoreCase) >= 0
                    || property.Name.IndexOf("Filter", StringComparison.OrdinalIgnoreCase) >= 0
                    || property.PropertyType == typeof(object));
        }

        [Fact]
        public void Unified_audit_has_only_stable_projection_fields_and_separate_gap_metadata()
        {
            var entry = new UnifiedAuditEntry(
                "chatMuteOperation",
                "operation-1",
                "owner",
                "EOS_1",
                "Create",
                Utc(2),
                "Succeeded",
                "correlation-1",
                false);
            var gap = new AuditSourceGap("consoleCommand", Utc(1), Utc(2), 2, "QueueFull");
            var page = new UnifiedAuditPage(
                new[] { entry },
                new UnifiedAuditCursor(entry.OccurredAtUtc, entry.SourceKind, entry.SourceId),
                new[] { gap });

            Assert.Equal(
                new[]
                {
                    "Action",
                    "ActorSubject",
                    "CorrelationId",
                    "HasDetails",
                    "OccurredAtUtc",
                    "SourceId",
                    "SourceKind",
                    "Status",
                    "TargetRef"
                },
                typeof(UnifiedAuditEntry).GetProperties().Select(property => property.Name).OrderBy(name => name));
            Assert.Same(entry, Assert.Single(page.Entries));
            Assert.Same(gap, Assert.Single(page.Gaps));
        }

        [Fact]
        public void Permanent_temporary_and_exactly_expired_mutes_have_fixed_semantics()
        {
            var now = Utc(2);
            var permanent = Mute("EOS_PERMANENT", null, Utc(1));
            var temporary = Mute("EOS_TEMPORARY", Utc(3), Utc(1));
            var exactlyExpired = Mute("EOS_EXPIRED", now, Utc(1));

            Assert.True(permanent.IsActiveAt(now));
            Assert.True(temporary.IsActiveAt(now));
            Assert.False(exactlyExpired.IsActiveAt(now));
        }

        [Fact]
        public void Mute_mutations_store_state_and_dedicated_operation_before_replacing_snapshot()
        {
            var order = new List<string>();
            var times = new Queue<DateTimeOffset>(new[] { Utc(1), Utc(2), Utc(3) });
            var store = new RecordingMuteStore(order);
            var runtime = new RecordingMuteRuntime(order);
            var useCases = new ChatMuteUseCases(store, runtime, () => times.Dequeue());

            var created = useCases.Create("owner", "EOS_1", "Alice", "first reason", null, "create-correlation");
            var updated = useCases.Update("owner", "EOS_1", "Alice 2", "second reason", Utc(5), "update-correlation");
            useCases.Release("owner", "EOS_1", "release-correlation");

            Assert.Equal(
                new[]
                {
                    "store:create", "runtime:replace",
                    "store:update", "runtime:replace",
                    "store:release", "runtime:replace"
                },
                order);
            Assert.Null(created.MutedUntilUtc);
            Assert.Equal(Utc(5), updated.MutedUntilUtc);
            Assert.Equal(
                new[] { ChatMuteOperationKind.Create, ChatMuteOperationKind.Update, ChatMuteOperationKind.Release },
                store.Mutations.Select(mutation => mutation.Operation.Kind));
            Assert.All(store.Mutations, mutation => Assert.Equal(mutation.State?.CrossplatformId ?? "EOS_1", mutation.Operation.TargetCrossplatformId));
            Assert.Empty(runtime.Snapshot);
        }

        [Fact]
        public void Mute_store_failure_preserves_the_prior_runtime_snapshot()
        {
            var prior = Mute("EOS_PRIOR", null, Utc(1));
            var runtime = new RecordingMuteRuntime(initial: new[] { prior });
            var priorSnapshot = runtime.Snapshot;
            var store = new RecordingMuteStore { Failure = new InvalidOperationException("store unavailable") };
            var useCases = new ChatMuteUseCases(store, runtime, () => Utc(2));

            Assert.Throws<InvalidOperationException>(() =>
                useCases.Create("owner", "EOS_NEW", "New", "reason", null, null));

            Assert.Same(priorSnapshot, runtime.Snapshot);
            Assert.Equal(0, runtime.ReplaceCount);
        }

        [Fact]
        public void Mute_runtime_replace_failure_preserves_the_prior_snapshot()
        {
            var prior = Mute("EOS_PRIOR", null, Utc(1));
            var runtime = new RecordingMuteRuntime(initial: new[] { prior })
            {
                Failure = new InvalidOperationException("runtime unavailable")
            };
            var priorSnapshot = runtime.Snapshot;
            var useCases = new ChatMuteUseCases(new RecordingMuteStore(), runtime, () => Utc(2));

            Assert.Throws<InvalidOperationException>(() =>
                useCases.Create("owner", "EOS_NEW", "New", "reason", null, null));

            Assert.Same(priorSnapshot, runtime.Snapshot);
        }

        [Fact]
        public void Mute_operations_are_fixed_and_cannot_store_blocked_chat_bodies()
        {
            Assert.Equal(
                new[] { "Create", "Update", "Release", "Expire" },
                Enum.GetNames(typeof(ChatMuteOperationKind)));
            Assert.DoesNotContain(
                typeof(ChatMuteOperation).GetProperties(),
                property => property.Name.IndexOf("Message", StringComparison.OrdinalIgnoreCase) >= 0
                    || property.Name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Help_is_the_only_registered_command_and_succeeds_without_arguments()
        {
            var catalog = new GameChatCommandCatalog(new IGameChatCommandHandler[]
            {
                new HelpGameChatCommandHandler(() => true)
            });

            var result = catalog.Handle("HELP", CommandContext());

            var descriptor = Assert.Single(catalog.Commands);
            Assert.Equal("help", descriptor.Name);
            Assert.Empty(descriptor.Aliases);
            Assert.True(result.IsHandled);
            Assert.Equal("chat.command.help.succeeded", result.Code);
            Assert.Contains("help", result.Messages);
        }

        [Fact]
        public void Help_rejects_arguments_and_reports_unavailable_without_throwing()
        {
            var available = new GameChatCommandCatalog(new IGameChatCommandHandler[]
            {
                new HelpGameChatCommandHandler(() => true)
            });
            var unavailable = new GameChatCommandCatalog(new IGameChatCommandHandler[]
            {
                new HelpGameChatCommandHandler(() => false)
            });

            Assert.Equal(
                "chat.command.invalid_arguments",
                available.Handle("help", CommandContext("unexpected")).Code);
            Assert.Equal(
                "chat.command.unavailable",
                unavailable.Handle("help", CommandContext()).Code);
        }

        [Fact]
        public void Handler_failure_maps_to_the_fixed_failed_result()
        {
            var catalog = new GameChatCommandCatalog(new[]
            {
                new StubCommandHandler("broken", Array.Empty<string>(), _ => throw new InvalidOperationException("secret"))
            });

            var result = catalog.Handle("broken", CommandContext());

            Assert.True(result.IsHandled);
            Assert.Equal("chat.command.failed", result.Code);
            Assert.DoesNotContain("secret", result.ToString()!);
        }

        [Fact]
        public void Command_names_and_aliases_cannot_conflict_ignoring_case()
        {
            Assert.Throws<ArgumentException>(() => new GameChatCommandCatalog(new IGameChatCommandHandler[]
            {
                new StubCommandHandler("help", Array.Empty<string>()),
                new StubCommandHandler("HELP", Array.Empty<string>())
            }));
            Assert.Throws<ArgumentException>(() => new GameChatCommandCatalog(new IGameChatCommandHandler[]
            {
                new StubCommandHandler("help", new[] { "assist" }),
                new StubCommandHandler("ASSIST", Array.Empty<string>())
            }));
        }

        [Fact]
        public void Unknown_commands_remain_unhandled_and_catalog_has_no_runtime_registration_api()
        {
            var catalog = new GameChatCommandCatalog(new IGameChatCommandHandler[]
            {
                new HelpGameChatCommandHandler(() => true)
            });

            var result = catalog.Handle("unknown", CommandContext());

            Assert.False(result.IsHandled);
            Assert.Null(result.Code);
            Assert.Empty(result.Messages);
            Assert.DoesNotContain(
                typeof(GameChatCommandCatalog).GetMethods().Where(method => method.IsPublic),
                method => method.Name.IndexOf("Register", StringComparison.OrdinalIgnoreCase) >= 0
                    || method.Name.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0
                    || method.Name.IndexOf("Script", StringComparison.OrdinalIgnoreCase) >= 0
                    || method.Name.IndexOf("Console", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static ObservedMetric<T> Available<T>(T value, string source, string unit) =>
            new ObservedMetric<T>(value, source, unit, Utc(1), null);

        private static DateTimeOffset Utc(int hour) =>
            new DateTimeOffset(2026, 7, 26, hour, 0, 0, TimeSpan.Zero);

        private static string GuidString() => Guid.NewGuid().ToString("D");

        private static ChatMuteRecord Mute(
            string crossplatformId,
            DateTimeOffset? mutedUntilUtc,
            DateTimeOffset changedAtUtc) =>
            new ChatMuteRecord(
                crossplatformId,
                "Alice",
                "reason",
                mutedUntilUtc,
                "owner",
                changedAtUtc,
                "owner",
                changedAtUtc);

        private static GameChatCommandContext CommandContext(params string[] arguments) =>
            new GameChatCommandContext("EOS_1", "Alice", arguments);

        private sealed class RecordingMuteStore : IChatMuteStore
        {
            private readonly List<string>? order;
            private readonly Dictionary<string, ChatMuteRecord> records =
                new Dictionary<string, ChatMuteRecord>(StringComparer.Ordinal);

            public RecordingMuteStore(List<string>? order = null) => this.order = order;

            public Exception? Failure { get; set; }

            public List<(ChatMuteRecord? State, ChatMuteOperation Operation)> Mutations { get; } =
                new List<(ChatMuteRecord?, ChatMuteOperation)>();

            public ChatMutePage GetPage(int pageSize, ChatMuteCursor? cursor) =>
                new ChatMutePage(records.Values, null);

            public ChatMuteRecord? Find(string crossplatformId) =>
                records.TryGetValue(crossplatformId, out var record) ? record : null;

            public IReadOnlyList<ChatMuteRecord> Create(ChatMuteRecord record, ChatMuteOperation operation)
            {
                ThrowIfFailed();
                order?.Add("store:create");
                records.Add(record.CrossplatformId, record);
                Mutations.Add((record, operation));
                return records.Values.ToArray();
            }

            public IReadOnlyList<ChatMuteRecord> Update(ChatMuteRecord record, ChatMuteOperation operation)
            {
                ThrowIfFailed();
                order?.Add("store:update");
                records[record.CrossplatformId] = record;
                Mutations.Add((record, operation));
                return records.Values.ToArray();
            }

            public IReadOnlyList<ChatMuteRecord> Release(string crossplatformId, ChatMuteOperation operation)
            {
                ThrowIfFailed();
                order?.Add("store:release");
                records.Remove(crossplatformId);
                Mutations.Add((null, operation));
                return records.Values.ToArray();
            }

            private void ThrowIfFailed()
            {
                if (Failure != null) throw Failure;
            }
        }

        private sealed class RecordingMuteRuntime : IChatMuteRuntimeConfiguration
        {
            private readonly List<string>? order;

            public RecordingMuteRuntime(
                List<string>? order = null,
                IEnumerable<ChatMuteRecord>? initial = null)
            {
                this.order = order;
                Snapshot = ReadOnlySnapshot(initial ?? Array.Empty<ChatMuteRecord>());
            }

            public Exception? Failure { get; set; }

            public IReadOnlyDictionary<string, ChatMuteRecord> Snapshot { get; private set; }

            public int ReplaceCount { get; private set; }

            public void ApplyChatSettings(ChatSettings settings) { }

            public void ApplyColoredChatSettings(ColoredChatSettings settings) { }

            public void UpsertProfile(ColoredChatProfile profile) { }

            public void RemoveProfile(string crossplatformId) { }

            public void ReplaceMuteSnapshot(IReadOnlyDictionary<string, ChatMuteRecord> snapshot)
            {
                if (Failure != null) throw Failure;
                order?.Add("runtime:replace");
                Snapshot = snapshot;
                ReplaceCount++;
            }

            private static IReadOnlyDictionary<string, ChatMuteRecord> ReadOnlySnapshot(
                IEnumerable<ChatMuteRecord> records) =>
                new ReadOnlyDictionary<string, ChatMuteRecord>(
                    records.ToDictionary(record => record.CrossplatformId, StringComparer.Ordinal));
        }

        private sealed class StubCommandHandler : IGameChatCommandHandler
        {
            private readonly Func<GameChatCommandContext, GameChatCommandResult> handle;

            public StubCommandHandler(
                string name,
                IReadOnlyList<string> aliases,
                Func<GameChatCommandContext, GameChatCommandResult>? handle = null)
            {
                Descriptor = new GameChatCommandDescriptor(name, aliases);
                this.handle = handle ?? (_ => GameChatCommandResult.HelpSucceeded(new[] { name }));
            }

            public GameChatCommandDescriptor Descriptor { get; }

            public GameChatCommandResult Handle(GameChatCommandContext context) => handle(context);
        }
    }
}
