using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerHistoryUseCaseTests
    {
        [Fact]
        public void Historical_players_use_case_forwards_the_validated_query_once()
        {
            var expected = new HistoricalPlayersPage(
                new[] { CreateSummary() },
                new HistoricalPlayersCursor(Utc(1), "EOS_0002"));
            var store = new RecordingPlayerHistoryStore { PlayersPage = expected };
            var useCase = new GetHistoricalPlayersUseCase(store);
            var query = new HistoricalPlayersQuery(
                "Alice",
                50,
                new HistoricalPlayersCursor(Utc(2), "EOS_0001"));

            var result = useCase.Execute(query);

            Assert.Same(expected, result);
            Assert.Same(query, store.PlayersQuery);
            Assert.Equal(1, store.PlayersCallCount);
        }

        [Fact]
        public void Historical_player_use_case_validates_the_crossplatform_identity_before_querying()
        {
            var store = new RecordingPlayerHistoryStore();
            var useCase = new GetHistoricalPlayerUseCase(store);

            Assert.Throws<ArgumentException>(() => useCase.Execute(" "));
            Assert.Equal(0, store.PlayerCallCount);

            var expected = new HistoricalPlayerDetails(
                CreateSummary(),
                new PlayerHistoryGapSummary(1, 3));
            store.Player = expected;

            Assert.Same(expected, useCase.Execute("EOS_0002"));
            Assert.Equal("EOS_0002", store.CrossplatformId);
            Assert.Equal(1, store.PlayerCallCount);
        }

        [Fact]
        public void Historical_snapshots_use_case_forwards_keyset_request_once()
        {
            var expected = new PlayerHistorySnapshotsPage(
                Array.Empty<HistoricalPlayerSnapshot>(),
                41,
                Array.Empty<PlayerHistoryGap>());
            var store = new RecordingPlayerHistoryStore { SnapshotsPage = expected };
            var useCase = new GetPlayerHistorySnapshotsUseCase(store);
            var request = new PlayerHistorySnapshotsQuery("EOS_0002", 100, 42);

            var result = useCase.Execute(request);

            Assert.Same(expected, result);
            Assert.Same(request, store.SnapshotsQuery);
            Assert.Equal(1, store.SnapshotsCallCount);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        public void Historical_player_list_query_rejects_page_sizes_outside_the_contract(int pageSize)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new HistoricalPlayersQuery(null, pageSize, null));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(201)]
        public void Historical_snapshot_query_rejects_page_sizes_outside_the_contract(int pageSize)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PlayerHistorySnapshotsQuery("EOS_0002", pageSize, null));
        }

        private static HistoricalPlayerSummary CreateSummary() =>
            new HistoricalPlayerSummary(
                "EOS_0002",
                "Alice",
                Utc(1),
                Utc(3),
                17,
                12,
                5,
                true);

        private static DateTimeOffset Utc(int hour) =>
            new DateTimeOffset(2026, 7, 25, hour, 0, 0, TimeSpan.Zero);

        private sealed class RecordingPlayerHistoryStore : IPlayerHistoryStore
        {
            public HistoricalPlayersPage PlayersPage { get; set; } =
                new HistoricalPlayersPage(Array.Empty<HistoricalPlayerSummary>(), null);

            public HistoricalPlayerDetails? Player { get; set; }

            public PlayerHistorySnapshotsPage SnapshotsPage { get; set; } =
                new PlayerHistorySnapshotsPage(
                    Array.Empty<HistoricalPlayerSnapshot>(),
                    null,
                    Array.Empty<PlayerHistoryGap>());

            public HistoricalPlayersQuery? PlayersQuery { get; private set; }

            public string? CrossplatformId { get; private set; }

            public PlayerHistorySnapshotsQuery? SnapshotsQuery { get; private set; }

            public int PlayersCallCount { get; private set; }

            public int PlayerCallCount { get; private set; }

            public int SnapshotsCallCount { get; private set; }

            public void Append(PlayerSnapshot snapshot) => throw new NotSupportedException();

            public void AppendGap(PlayerHistoryGap gap) => throw new NotSupportedException();

            public HistoricalPlayersPage GetPlayers(HistoricalPlayersQuery query)
            {
                PlayersCallCount++;
                PlayersQuery = query;
                return PlayersPage;
            }

            public HistoricalPlayerDetails? GetPlayer(string crossplatformId)
            {
                PlayerCallCount++;
                CrossplatformId = crossplatformId;
                return Player;
            }

            public PlayerHistorySnapshotsPage GetSnapshots(PlayerHistorySnapshotsQuery query)
            {
                SnapshotsCallCount++;
                SnapshotsQuery = query;
                return SnapshotsPage;
            }

            public PlayerTrackHistory? GetPlayerTrack(GetPlayerTrackQuery query) =>
                throw new NotSupportedException();

            public IReadOnlyList<HistoricalPlayerLastRetainedLocation> GetHistoricalPlayerLastRetainedLocations(
                HistoricalPlayerLastLocationsStoreQuery query) =>
                Array.Empty<HistoricalPlayerLastRetainedLocation>();

            public int Compact(DateTimeOffset utcNow, int maximumDeletes) => 0;
        }
    }
}
