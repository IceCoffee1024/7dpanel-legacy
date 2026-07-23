using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysOnlinePlayerQueryTests
    {
        [Fact]
        public async Task Query_sorts_observations_and_preserves_each_observed_time()
        {
            var older = Utc(1, 0, 0);
            var newer = older.AddSeconds(20);
            using var projection = CreateProjection();
            projection.UpsertForTest(CreateObservation(42, "Zed", newer));
            projection.UpsertForTest(CreateObservation(7, "Amy", older));

            var result = await projection.GetOnlineAsync(CancellationToken.None);

            Assert.Equal(new[] { 7, 42 }, result.Players.Select(player => player.EntityId));
            Assert.Equal(new[] { older, newer }, result.Players.Select(player => player.ObservedAtUtc));
        }

        [Fact]
        public async Task Empty_projection_returns_an_empty_collection()
        {
            using var projection = CreateProjection();

            var result = await projection.GetOnlineAsync(CancellationToken.None);

            Assert.Empty(result.Players);
        }

        [Fact]
        public async Task Query_honors_an_already_cancelled_token()
        {
            using var projection = CreateProjection();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                projection.GetOnlineAsync(new CancellationToken(true)));
        }

        [Fact]
        public async Task Later_observation_replaces_the_same_entity_and_prior_results_stay_stable()
        {
            var observedAt = Utc(1, 0, 0);
            using var projection = CreateProjection();
            projection.UpsertForTest(CreateObservation(7, "Before", observedAt));
            var before = await projection.GetOnlineAsync(CancellationToken.None);

            projection.UpsertForTest(CreateObservation(7, "After", observedAt.AddSeconds(1)));
            var after = await projection.GetOnlineAsync(CancellationToken.None);

            Assert.Equal("Before", Assert.Single(before.Players).Name);
            Assert.Equal("After", Assert.Single(after.Players).Name);
        }

        [Fact]
        public async Task Query_returns_an_old_observation_without_applying_an_age_policy()
        {
            var observedAt = Utc(1, 0, 0);
            using var projection = CreateProjection();
            projection.UpsertForTest(CreateObservation(7, "Amy", observedAt));

            var result = await projection.GetOnlineAsync(CancellationToken.None);

            Assert.Equal(observedAt, Assert.Single(result.Players).ObservedAtUtc);
        }

        [Fact]
        public async Task Membership_without_an_observation_remains_absent_regardless_of_age()
        {
            var joinedAt = Utc(1, 0, 0);
            using var projection = CreateProjection();
            projection.JoinForTest(7, "steam:amy");

            var result = await projection.GetOnlineAsync(CancellationToken.None);

            Assert.Empty(result.Players);
        }

        [Fact]
        public async Task Observation_for_a_reused_entity_is_hidden_until_the_current_identity_is_observed()
        {
            var observedAt = Utc(1, 0, 0);
            using var projection = CreateProjection();
            projection.UpsertForTest(CreateObservation(7, "Old", observedAt, "steam:old"));

            projection.JoinForTest(7, "steam:new");

            Assert.Empty((await projection.GetOnlineAsync(CancellationToken.None)).Players);
        }

        [Fact]
        public async Task Join_creates_membership_without_creating_an_observation()
        {
            var fixture = new ProjectionFixture();
            using var projection = fixture.CreateProjection();
            projection.Start();

            fixture.RaiseJoined(new OnlinePlayerIdentitySource(7, "steam:amy"));

            Assert.Empty((await projection.GetOnlineAsync(CancellationToken.None)).Players);
        }

        [Fact]
        public async Task Save_upserts_one_observation_and_later_save_replaces_it()
        {
            var fixture = new ProjectionFixture();
            using var projection = fixture.CreateProjection();
            projection.Start();

            fixture.RaiseSave(CreateObservation(7, "Amy", fixture.UtcNow, level: 10, health: 80));
            fixture.RaiseSave(CreateObservation(7, "Amy", fixture.UtcNow.AddSeconds(1), level: 11, health: 75));

            var player = Assert.Single((await projection.GetOnlineAsync(CancellationToken.None)).Players);
            Assert.Equal(11, player.Level);
            Assert.Equal(75, player.Health);
            Assert.Equal(2, fixture.CopyCount);
        }

        [Fact]
        public async Task Save_without_a_prior_join_creates_matching_membership()
        {
            var fixture = new ProjectionFixture();
            using var projection = fixture.CreateProjection();
            projection.Start();

            fixture.RaiseSave(CreateObservation(7, "Amy", fixture.UtcNow));

            Assert.Single((await projection.GetOnlineAsync(CancellationToken.None)).Players);
        }

        [Fact]
        public async Task Join_for_a_reused_entity_removes_the_old_identity_observation()
        {
            var fixture = new ProjectionFixture();
            using var projection = fixture.CreateProjection();
            projection.Start();
            fixture.RaiseSave(CreateObservation(7, "Old", fixture.UtcNow, "steam:old"));

            fixture.RaiseJoined(new OnlinePlayerIdentitySource(7, "steam:new"));

            Assert.Empty((await projection.GetOnlineAsync(CancellationToken.None)).Players);
        }

        [Fact]
        public async Task Disconnect_removes_only_the_matching_identity()
        {
            var fixture = new ProjectionFixture();
            using var projection = fixture.CreateProjection();
            projection.Start();
            fixture.RaiseSave(CreateObservation(7, "Amy", fixture.UtcNow));

            fixture.RaiseDisconnected(new OnlinePlayerIdentitySource(7, "steam:other"));
            Assert.Single((await projection.GetOnlineAsync(CancellationToken.None)).Players);

            fixture.RaiseDisconnected(new OnlinePlayerIdentitySource(7, "steam:amy"));
            Assert.Empty((await projection.GetOnlineAsync(CancellationToken.None)).Players);
        }

        [Fact]
        public async Task Copy_failure_keeps_the_previous_observation_and_does_not_escape()
        {
            var fixture = new ProjectionFixture();
            using var projection = fixture.CreateProjection();
            projection.Start();
            fixture.RaiseSave(CreateObservation(7, "Before", fixture.UtcNow));
            fixture.CopyException = new InvalidOperationException("copy failed");

            fixture.RaiseSave(CreateObservation(7, "After", fixture.UtcNow.AddSeconds(1)));

            Assert.Equal(
                "Before",
                Assert.Single((await projection.GetOnlineAsync(CancellationToken.None)).Players).Name);
            Assert.Contains("online player save rejected", fixture.LogMessages);
        }

        [Fact]
        public void Start_and_stop_use_exact_reverse_subscription_order_and_are_idempotent()
        {
            var fixture = new ProjectionFixture();
            using var projection = fixture.CreateProjection();

            projection.Start();
            projection.Start();
            projection.Stop();
            projection.Stop();

            Assert.Equal(
                new[]
                {
                    "subscribe-joined", "subscribe-save", "subscribe-disconnected",
                    "dispose-disconnected", "dispose-save", "dispose-joined"
                },
                fixture.Trace);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void Registration_failure_rolls_back_prior_subscriptions_in_reverse_order(int failureIndex)
        {
            var fixture = new ProjectionFixture { SubscriptionFailureIndex = failureIndex };
            using var projection = fixture.CreateProjection();

            Assert.Throws<InvalidOperationException>(() => projection.Start());

            Assert.Equal(0, fixture.ActiveSubscriptionCount);
            Assert.Equal(
                failureIndex == 2
                    ? new[] { "subscribe-joined", "subscribe-save", "dispose-joined" }
                    : new[]
                    {
                        "subscribe-joined", "subscribe-save", "subscribe-disconnected",
                        "dispose-save", "dispose-joined"
                    },
                fixture.Trace);
        }

        [Fact]
        public async Task Stop_clears_projection_and_rejects_later_callbacks()
        {
            var fixture = new ProjectionFixture();
            using var projection = fixture.CreateProjection();
            projection.Start();
            fixture.RaiseSave(CreateObservation(7, "Amy", fixture.UtcNow));

            projection.Stop();
            fixture.RaiseSave(CreateObservation(8, "Late", fixture.UtcNow));

            Assert.Empty((await projection.GetOnlineAsync(CancellationToken.None)).Players);
        }

        private static SevenDaysOnlinePlayerProjection CreateProjection() =>
            new SevenDaysOnlinePlayerProjection();

        private static OnlinePlayerObservation CreateObservation(
            int entityId,
            string name,
            DateTimeOffset observedAtUtc,
            string combinedId = "steam:amy",
            int level = 10,
            int health = 100) =>
            new OnlinePlayerObservation(
                new PlayerSnapshot(
                    entityId,
                    name,
                    new PlayerPlatformIdentity(combinedId, "Steam"),
                    null,
                    42,
                    level,
                    health,
                    observedAtUtc),
                observedAtUtc);

        private static DateTimeOffset Utc(int hour, int minute, int second) =>
            new DateTimeOffset(2026, 7, 22, hour, minute, second, TimeSpan.Zero);

        private sealed class ProjectionFixture
        {
            private Action<OnlinePlayerIdentitySource>? joined;
            private Action<OnlinePlayerObservation>? save;
            private Action<OnlinePlayerIdentitySource>? disconnected;

            public ProjectionFixture()
            {
                UtcNow = Utc(1, 0, 0);
            }

            public DateTimeOffset UtcNow { get; set; }

            public int CopyCount { get; private set; }

            public Exception? CopyException { get; set; }

            public List<string> LogMessages { get; } = new List<string>();

            public List<string> Trace { get; } = new List<string>();

            public int SubscriptionFailureIndex { get; set; }

            public int ActiveSubscriptionCount => activeSubscriptions;

            private int subscriptionCount;
            private int activeSubscriptions;

            public SevenDaysOnlinePlayerProjection CreateProjection() =>
                new SevenDaysOnlinePlayerProjection(
                    handler => Subscribe("joined", handler, value => joined = value),
                    handler => Subscribe("save", handler, value => save = value),
                    handler => Subscribe("disconnected", handler, value => disconnected = value),
                    observation =>
                    {
                        CopyCount++;
                        if (CopyException != null) throw CopyException;
                        return observation;
                    },
                    LogMessages.Add);

            public void RaiseJoined(OnlinePlayerIdentitySource source) => joined!(source);

            public void RaiseSave(OnlinePlayerObservation observation) => save!(observation);

            public void RaiseDisconnected(OnlinePlayerIdentitySource source) => disconnected!(source);

            private IDisposable Subscribe<T>(
                string name,
                Action<T> handler,
                Action<Action<T>> capture)
            {
                subscriptionCount++;
                Trace.Add("subscribe-" + name);
                if (subscriptionCount == SubscriptionFailureIndex)
                    throw new InvalidOperationException("subscription failed");

                capture(handler);
                activeSubscriptions++;
                return new Subscription(() =>
                {
                    activeSubscriptions--;
                    Trace.Add("dispose-" + name);
                });
            }

            private sealed class Subscription : IDisposable
            {
                private Action? dispose;

                public Subscription(Action dispose)
                {
                    this.dispose = dispose;
                }

                public void Dispose()
                {
                    Interlocked.Exchange(ref dispose, null)?.Invoke();
                }
            }
        }
    }
}
