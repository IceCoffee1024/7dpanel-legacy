using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.GameEvents;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.GameEvents;
using LSTY.SevenDPanel.Application.GameEvents;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Community")]
    [Trait("Boundary", "SevenDays")]
    public sealed class SevenDaysGameEventRuntimeTests
    {
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(3);

        [Fact]
        public void Queue_full_is_nonblocking_and_is_persisted_as_a_gap()
        {
            using var store = new BlockingEventStore();
            using var writer = new GameEventWriteService(store, 1, TimeSpan.FromSeconds(1));
            writer.Start();
            Assert.True(writer.TryRecord(Event(GameEventType.PlayerJoined)));
            Assert.True(store.AppendEntered.Wait(WaitTimeout));
            Assert.True(writer.TryRecord(Event(GameEventType.PlayerLeft)));

            var stopwatch = Stopwatch.StartNew();
            var accepted = writer.TryRecord(Event(GameEventType.PlayerDied));
            stopwatch.Stop();

            Assert.False(accepted);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
            store.ReleaseAppend.Set();
            writer.Stop();
            Assert.Equal(1, writer.QueueFullCount);
            Assert.Equal(1, Assert.Single(store.Gaps).AffectedCount);
            Assert.Equal(GameEventGapReason.QueueFull, Assert.Single(store.Gaps).Reason);
        }

        [Fact]
        public void Gap_flush_does_not_erase_the_same_reason_added_during_persistence()
        {
            using var store = new BlockingGapStore();
            using var writer = new GameEventWriteService(store, 1, TimeSpan.FromSeconds(1));
            writer.Start();
            Assert.True(writer.TryRecord(Event(GameEventType.PlayerJoined)));
            Assert.True(store.AppendEntered.Wait(WaitTimeout));
            Assert.True(writer.TryRecord(Event(GameEventType.PlayerLeft)));
            Assert.False(writer.TryRecord(Event(GameEventType.PlayerDied)));
            store.ReleaseAppend.Set();
            Assert.True(store.GapEntered.Wait(WaitTimeout));

            Assert.True(writer.TryRecord(Event(GameEventType.PlayerJoined)));
            Assert.False(writer.TryRecord(Event(GameEventType.PlayerLeft)));
            store.ReleaseGap.Set();
            writer.Stop();

            Assert.Equal(2, store.Gaps.Where(gap => gap.Reason == GameEventGapReason.QueueFull).Sum(gap => gap.AffectedCount));
        }

        [Fact]
        public void Stop_is_bounded_when_gap_persistence_blocks_and_never_uses_store_concurrently()
        {
            using var store = new BlockingGapStore();
            using var writer = new GameEventWriteService(store, 1, TimeSpan.FromMilliseconds(100));
            writer.Start();
            Assert.True(writer.TryRecord(Event(GameEventType.PlayerJoined)));
            Assert.True(store.AppendEntered.Wait(WaitTimeout));
            Assert.True(writer.TryRecord(Event(GameEventType.PlayerLeft)));
            Assert.False(writer.TryRecord(Event(GameEventType.PlayerDied)));
            store.ReleaseAppend.Set();
            Assert.True(store.GapEntered.Wait(WaitTimeout));

            var stopwatch = Stopwatch.StartNew();
            var stopTask = Task.Factory.StartNew(
                writer.Stop,
                CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
            var completedWithinBound = stopTask.Wait(TimeSpan.FromMilliseconds(500));
            stopwatch.Stop();
            var maximumConcurrentCalls = store.MaximumConcurrentCalls;
            try
            {
                Assert.True(completedWithinBound);
                Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500));
                Assert.Equal(1, maximumConcurrentCalls);
            }
            finally
            {
                store.ReleaseGap.Set();
                Assert.True(stopTask.Wait(WaitTimeout));
            }

            Assert.True(SpinWait.SpinUntil(
                () => store.Gaps.Any(gap => gap.Reason == GameEventGapReason.DrainTimeout),
                WaitTimeout));
        }

        [Fact]
        public void Store_failure_gap_is_flushed_before_the_recovery_event()
        {
            var store = new FailingStore { FailAppends = 1 };
            using var writer = new GameEventWriteService(store, 1, TimeSpan.FromSeconds(1));
            writer.Start();
            Assert.True(writer.TryRecord(Event(GameEventType.PlayerJoined)));
            Assert.True(SpinWait.SpinUntil(() => store.AppendAttempts > 0, WaitTimeout));
            Assert.True(writer.TryRecord(Event(GameEventType.PlayerLeft)));
            Assert.True(SpinWait.SpinUntil(() => store.Events.Count == 1, WaitTimeout));
            writer.Stop();

            Assert.Equal(new[] { "gap-StoreFailure", "event-PlayerLeft" }, store.Trace);
        }

        [Fact]
        public void Event_callback_remains_nonblocking_when_writer_queue_is_full()
        {
            using var store = new BlockingEventStore();
            using var writer = new GameEventWriteService(store, 1, TimeSpan.FromSeconds(1));
            var sources = new FakeGameEventSources();
            var adapter = Adapter(writer, sources);
            writer.Start();
            using var subscription = adapter.Subscribe();
            sources.RaiseJoined(Subject(1, "cross-1", "platform-1", "First"));
            Assert.True(store.AppendEntered.Wait(WaitTimeout));
            sources.RaiseJoined(Subject(2, "cross-2", "platform-2", "Second"));

            var stopwatch = Stopwatch.StartNew();
            sources.RaiseJoined(Subject(3, "cross-3", "platform-3", "Third"));
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
            Assert.Equal(1, writer.QueueFullCount);
            store.ReleaseAppend.Set();
            writer.Stop();
        }

        [Theory]
        [InlineData(2, new[] { "subscribe-joined", "subscribe-left", "unsubscribe-joined" })]
        [InlineData(3, new[] { "subscribe-joined", "subscribe-left", "subscribe-killed", "unsubscribe-left", "unsubscribe-joined" })]
        public void Partial_subscription_failure_cleans_up_prior_static_event_sources(
            int failureIndex,
            string[] expectedTrace)
        {
            var sources = new FakeGameEventSources { FailureIndex = failureIndex };
            using var writer = new GameEventWriteService(new FailingStore(), 4, TimeSpan.FromSeconds(1));
            var adapter = Adapter(writer, sources);

            Assert.Throws<InvalidOperationException>(() => adapter.Subscribe());

            Assert.Equal(expectedTrace, sources.Trace);
            Assert.Equal(0, sources.ActiveSubscriptionCount);
        }

        [Fact]
        public void Three_static_event_source_callbacks_map_to_all_four_event_types()
        {
            var store = new FailingStore();
            using var writer = new GameEventWriteService(store, 8, TimeSpan.FromSeconds(1));
            var sources = new FakeGameEventSources();
            var adapter = new SevenDaysGameEventAdapter(writer, sources, UtcClock);
            writer.Start();
            using var subscription = adapter.Subscribe();

            sources.RaiseJoined(Subject(1, "join-cross", "join-platform", "Joined"));
            sources.RaiseLeft(Subject(2, "left-cross", "left-platform", "Left"), true);
            sources.RaiseKilled(
                Snapshot(Subject(11, "killer-cross", "killer-platform", "Killer"), true),
                Snapshot(new GameEventSubject(null, null, 20, "Zombie"), false));
            sources.RaiseKilled(
                Snapshot(new GameEventSubject(null, null, 21, "Zombie"), false),
                Snapshot(Subject(12, "victim-cross", "victim-platform", "Victim"), true));

            Assert.True(SpinWait.SpinUntil(() => store.Events.Count == 4, WaitTimeout));
            writer.Stop();
            Assert.Equal(
                new[] { GameEventType.PlayerJoined, GameEventType.PlayerLeft, GameEventType.PlayerKilledEntity, GameEventType.PlayerDied },
                store.Events.Select(record => record.EventType));
            Assert.Equal("killer-cross", store.Events[2].Actor!.CrossplatformId);
            Assert.Equal("killer-platform", store.Events[2].Actor!.PlatformId);
            Assert.Equal("Killer", store.Events[2].Actor!.DisplayName);
            Assert.Equal("victim-cross", store.Events[3].Target!.CrossplatformId);
            Assert.True(store.Events[1].GameShuttingDown);
        }

        [Fact]
        public void Entity_snapshot_prefers_current_client_identity_without_retaining_engine_objects()
        {
            var current = Subject(11, "current-cross", "current-platform", "Current Name");

            var snapshot = SevenDaysModGameEventSources.CreateEntitySnapshot(
                11,
                "stale entity name",
                true,
                entityId => entityId == 11 ? current : null);

            Assert.True(snapshot.IsPlayer);
            Assert.Equal("current-cross", snapshot.Subject!.CrossplatformId);
            Assert.Equal("current-platform", snapshot.Subject.PlatformId);
            Assert.Equal(11, snapshot.Subject.EntityId);
            Assert.Equal("Current Name", snapshot.Subject.DisplayName);
        }

        [Fact]
        public void Player_versus_player_kill_emits_distinct_kill_and_death_records()
        {
            var store = new FailingStore();
            using var writer = new GameEventWriteService(store, 4, TimeSpan.FromSeconds(1));
            var sources = new FakeGameEventSources();
            var adapter = new SevenDaysGameEventAdapter(writer, sources, UtcClock);
            writer.Start();
            using var subscription = adapter.Subscribe();

            sources.RaiseKilled(
                Snapshot(Subject(31, "cross-a", "platform-a", "Same Name"), true),
                Snapshot(Subject(32, "cross-b", "platform-b", "Same Name"), true));

            Assert.True(SpinWait.SpinUntil(() => store.Events.Count == 2, WaitTimeout));
            writer.Stop();
            Assert.Equal(new[] { GameEventType.PlayerKilledEntity, GameEventType.PlayerDied }, store.Events.Select(record => record.EventType));
            Assert.Equal("cross-a", store.Events[0].Actor!.CrossplatformId);
            Assert.Equal("cross-b", store.Events[0].Target!.CrossplatformId);
            Assert.Equal(store.Events[0].Actor!.CrossplatformId, store.Events[1].Actor!.CrossplatformId);
            Assert.Equal(store.Events[0].Target!.CrossplatformId, store.Events[1].Target!.CrossplatformId);
        }

        [Fact]
        public void Runtime_starts_writer_subscribes_then_stops_producers_before_inner_runtime()
        {
            var trace = new List<string>();
            using var writer = new GameEventWriteService(new FailingStore(trace), 4, TimeSpan.FromSeconds(1));
            var runtime = new SevenDaysGameEventRuntime(
                writer,
                () => { trace.Add("subscribe"); return new CallbackDisposable(() => trace.Add("unsubscribe")); },
                new RecordingRuntime(trace));

            runtime.Start();
            runtime.Stop();
            runtime.Stop();

            Assert.Equal(new[] { "subscribe", "inner-start", "unsubscribe", "inner-stop" }, trace);
        }

        private static SevenDaysGameEventAdapter Adapter(
            GameEventWriteService writer,
            ISevenDaysGameEventSources sources) =>
            new SevenDaysGameEventAdapter(writer, sources, UtcClock);

        private static DateTimeOffset UtcClock() =>
            new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);

        private static GameEventRecord Event(GameEventType type) => new GameEventRecord(
            Guid.NewGuid().ToString("D"), type, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            new GameEventSubject("EOS_1", "Steam_1", 1, "player"), null, null);

        private static GameEventSubject Subject(
            int entityId,
            string crossplatformId,
            string platformId,
            string name) =>
            new GameEventSubject(crossplatformId, platformId, entityId, name);

        private static GameEventEntitySnapshot Snapshot(
            GameEventSubject? subject,
            bool isPlayer) =>
            new GameEventEntitySnapshot(subject, isPlayer);

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class FailingStore : IGameEventStore
        {
            private readonly object gate = new object();
            private readonly ICollection<string>? externalTrace;
            private readonly List<GameEventRecord> events = new List<GameEventRecord>();
            private readonly List<GameEventGap> gaps = new List<GameEventGap>();
            private readonly List<string> trace = new List<string>();
            private int appendAttempts;
            public FailingStore(ICollection<string>? trace = null) => externalTrace = trace;
            public int FailAppends { get; set; }
            public int AppendAttempts => Volatile.Read(ref appendAttempts);
            public IReadOnlyList<GameEventRecord> Events { get { lock (gate) return events.ToArray(); } }
            public IReadOnlyList<GameEventGap> Gaps { get { lock (gate) return gaps.ToArray(); } }
            public IReadOnlyList<string> Trace { get { lock (gate) return trace.ToArray(); } }
            public void Append(GameEventRecord record)
            {
                Interlocked.Increment(ref appendAttempts);
                lock (gate)
                {
                    if (FailAppends-- > 0) throw new InvalidOperationException("store failed");
                    events.Add(record);
                    trace.Add("event-" + record.EventType);
                }
            }
            public void AppendGap(GameEventGap gap)
            {
                lock (gate)
                {
                    externalTrace?.Add("gap");
                    gaps.Add(gap);
                    trace.Add("gap-" + gap.Reason);
                }
            }
            public GameEventPage Query(GameEventQuery query) => throw new NotSupportedException();
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class BlockingEventStore : IGameEventStore, IDisposable
        {
            private readonly object gate = new object();
            private readonly List<GameEventGap> gaps = new List<GameEventGap>();
            private int appendCount;
            public ManualResetEventSlim AppendEntered { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ReleaseAppend { get; } = new ManualResetEventSlim(false);
            public IReadOnlyList<GameEventGap> Gaps { get { lock (gate) return gaps.ToArray(); } }
            public void Append(GameEventRecord record)
            {
                if (Interlocked.Increment(ref appendCount) != 1) return;
                AppendEntered.Set();
                ReleaseAppend.Wait();
            }
            public void AppendGap(GameEventGap gap) { lock (gate) gaps.Add(gap); }
            public GameEventPage Query(GameEventQuery query) => throw new NotSupportedException();
            public void Dispose() { ReleaseAppend.Set(); AppendEntered.Dispose(); ReleaseAppend.Dispose(); }
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class BlockingGapStore : IGameEventStore, IDisposable
        {
            private readonly object gate = new object();
            private readonly List<GameEventGap> gaps = new List<GameEventGap>();
            private int appendCount;
            private int gapCount;
            private int activeCalls;
            private int maximumConcurrentCalls;
            public ManualResetEventSlim AppendEntered { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ReleaseAppend { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim GapEntered { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ReleaseGap { get; } = new ManualResetEventSlim(false);
            public int MaximumConcurrentCalls => Volatile.Read(ref maximumConcurrentCalls);
            public IReadOnlyList<GameEventGap> Gaps { get { lock (gate) return gaps.ToArray(); } }
            public void Append(GameEventRecord record)
            {
                EnterStore();
                try
                {
                    if (Interlocked.Increment(ref appendCount) != 1) return;
                    AppendEntered.Set();
                    ReleaseAppend.Wait();
                }
                finally { Interlocked.Decrement(ref activeCalls); }
            }
            public void AppendGap(GameEventGap gap)
            {
                EnterStore();
                try
                {
                    if (Interlocked.Increment(ref gapCount) == 1)
                    {
                        GapEntered.Set();
                        ReleaseGap.Wait();
                    }
                    lock (gate) gaps.Add(gap);
                }
                finally { Interlocked.Decrement(ref activeCalls); }
            }
            public GameEventPage Query(GameEventQuery query) => throw new NotSupportedException();
            public void Dispose()
            {
                ReleaseAppend.Set();
                ReleaseGap.Set();
                AppendEntered.Dispose();
                ReleaseAppend.Dispose();
                GapEntered.Dispose();
                ReleaseGap.Dispose();
            }
            private void EnterStore()
            {
                var active = Interlocked.Increment(ref activeCalls);
                while (true)
                {
                    var maximum = Volatile.Read(ref maximumConcurrentCalls);
                    if (active <= maximum || Interlocked.CompareExchange(ref maximumConcurrentCalls, active, maximum) == maximum) return;
                }
            }
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class FakeGameEventSources : ISevenDaysGameEventSources
        {
            private Action<GameEventSubject?>? joined;
            private Action<GameEventSubject?, bool>? left;
            private Action<GameEventEntitySnapshot, GameEventEntitySnapshot>? killed;
            private int registrations;
            public int FailureIndex { get; set; }
            public int ActiveSubscriptionCount { get; private set; }
            public List<string> Trace { get; } = new List<string>();
            public IDisposable SubscribePlayerJoined(Action<GameEventSubject?> handler) => Subscribe(
                "joined", () => joined = handler, () => joined = null);
            public IDisposable SubscribePlayerDisconnected(Action<GameEventSubject?, bool> handler) => Subscribe(
                "left", () => left = handler, () => left = null);
            public IDisposable SubscribeEntityKilled(Action<GameEventEntitySnapshot, GameEventEntitySnapshot> handler) => Subscribe(
                "killed", () => killed = handler, () => killed = null);
            public void RaiseJoined(GameEventSubject? subject) => joined?.Invoke(subject);
            public void RaiseLeft(GameEventSubject? subject, bool shuttingDown) => left?.Invoke(subject, shuttingDown);
            public void RaiseKilled(GameEventEntitySnapshot killer, GameEventEntitySnapshot victim) => killed?.Invoke(killer, victim);
            private IDisposable Subscribe(string name, Action activate, Action deactivate)
            {
                registrations++;
                Trace.Add("subscribe-" + name);
                if (registrations == FailureIndex) throw new InvalidOperationException("registration failed");
                activate();
                ActiveSubscriptionCount++;
                return new CallbackDisposable(() =>
                {
                    deactivate();
                    ActiveSubscriptionCount--;
                    Trace.Add("unsubscribe-" + name);
                });
            }
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly ICollection<string> trace;
            public RecordingRuntime(ICollection<string> trace) => this.trace = trace;
            public void Start() => trace.Add("inner-start");
            public void MarkGameReady() { }
            public void Stop() => trace.Add("inner-stop");
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class CallbackDisposable : IDisposable
        {
            private Action? callback;
            public CallbackDisposable(Action callback) => this.callback = callback;
            public void Dispose() => Interlocked.Exchange(ref callback, null)?.Invoke();
        }
    }
}
