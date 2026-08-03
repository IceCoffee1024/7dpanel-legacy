using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Application")]
    public sealed class MapLayerUseCaseTests
    {
        [Fact]
        public void Map_layer_query_requires_bounded_extent_zoom_and_limit()
        {
            var extent = new MapExtent(-100, -100, 100, 100);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MapLayerQuery(MapLayerKind.Vehicles, extent, -1, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MapLayerQuery(MapLayerKind.Vehicles, extent, 3, 0));
            Assert.Throws<MapLayerLimitExceededException>(() =>
                new MapLayerQuery(
                    MapLayerKind.Vehicles,
                    extent,
                    3,
                    MapLayerQuery.MaximumResultLimit + 1));
        }

        [Fact]
        public void Get_map_layer_delegates_the_bounded_query()
        {
            var projection = new RecordingMapLayerProjection();
            var useCase = new GetMapLayerUseCase(projection);
            var query = new MapLayerQuery(
                MapLayerKind.LandClaims,
                new MapExtent(-10, -20, 30, 40),
                2,
                25);

            var result = useCase.Execute(query);

            Assert.Same(query, projection.QueryValue);
            Assert.Equal(AvailabilityState.Unavailable, result.Availability);
        }

        [Fact]
        public async Task Historical_last_retained_locations_exclude_current_online_canonical_identities()
        {
            var store = new RecordingHistoryStore(
                Location(1, "EOS_online", "Online history", Utc(1), 1, 2, 3),
                Location(2, "EOS_retained", "Retained", Utc(2), 4, 5, 6));
            var online = new StubOnlinePlayerQuery(new OnlinePlayersSnapshot(new[]
            {
                Player("EOS_online"),
                Player(null)
            }));
            var useCase = new GetHistoricalPlayerLastLocationsUseCase(store, online);
            var request = new HistoricalPlayerLastLocationsRequest(
                new MapExtent(-100, -100, 100, 100),
                zoom: 2,
                limit: 10);

            var result = await useCase.ExecuteAsync(request, CancellationToken.None);

            Assert.Equal(AvailabilityState.Available, result.Availability);
            var retained = Assert.Single(result.Locations);
            Assert.Equal("EOS_retained", retained.CrossplatformId);
            Assert.Equal(Utc(2), retained.ObservedAtUtc);
            Assert.Equal(12, store.QueryValue!.CandidateLimit);
            Assert.DoesNotContain(
                typeof(HistoricalPlayerLastRetainedLocation).GetProperties(),
                property => property.Name.IndexOf("Current", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            property.Name.IndexOf("Offline", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public async Task Historical_locations_are_unavailable_without_assertions_when_online_projection_fails()
        {
            var store = new RecordingHistoryStore(
                Location(1, "EOS_retained", "Retained", Utc(1), 1, 2, 3));
            var useCase = new GetHistoricalPlayerLastLocationsUseCase(
                store,
                new ThrowingOnlinePlayerQuery());

            var result = await useCase.ExecuteAsync(
                new HistoricalPlayerLastLocationsRequest(
                    new MapExtent(-100, -100, 100, 100),
                    zoom: 2,
                    limit: 10),
                CancellationToken.None);

            Assert.Equal(AvailabilityState.Unavailable, result.Availability);
            Assert.Empty(result.Locations);
            Assert.Null(store.QueryValue);
        }

        [Fact]
        public async Task Historical_locations_reject_matching_results_above_the_requested_limit()
        {
            var store = new RecordingHistoryStore(
                Location(1, "EOS_1", "One", Utc(1), 1, 2, 3),
                Location(2, "EOS_2", "Two", Utc(2), 4, 5, 6));
            var useCase = new GetHistoricalPlayerLastLocationsUseCase(
                store,
                new StubOnlinePlayerQuery(new OnlinePlayersSnapshot(Array.Empty<PlayerSnapshot>())));

            await Assert.ThrowsAsync<MapLayerLimitExceededException>(() => useCase.ExecuteAsync(
                new HistoricalPlayerLastLocationsRequest(
                    new MapExtent(-100, -100, 100, 100),
                    zoom: 2,
                    limit: 1),
                CancellationToken.None));
        }

        private static readonly string NativeId = "Steam_76561198000000000";

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 1, minute, 0, TimeSpan.Zero);

        private static HistoricalPlayerLastRetainedLocation Location(
            long snapshotId,
            string crossplatformId,
            string name,
            DateTimeOffset observedAtUtc,
            double x,
            double y,
            double z) =>
            new HistoricalPlayerLastRetainedLocation(
                snapshotId,
                crossplatformId,
                name,
                new MapLayerPosition(x, y, z),
                observedAtUtc);

        private static PlayerSnapshot Player(string? crossplatformId) =>
            new PlayerSnapshot(
                1,
                "Online",
                new PlayerPlatformIdentity(NativeId, "Steam"),
                crossplatformId == null ? null : new PlayerPlatformIdentity(crossplatformId, "EOS"),
                PlayerDeviceType.Windows,
                null,
                1,
                null,
                null,
                0,
                new PlayerPosition(0, 0, 0),
                false,
                100,
                100,
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                Utc(0));

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingMapLayerProjection : IMapLayerProjection
        {
            public MapLayerQuery? QueryValue { get; private set; }

            public MapLayerProjectionSnapshot Query(MapLayerQuery query)
            {
                QueryValue = query;
                return MapLayerProjectionSnapshot.Unavailable(query.Layer);
            }
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class StubOnlinePlayerQuery : IOnlinePlayerQuery
        {
            private readonly OnlinePlayersSnapshot snapshot;

            public StubOnlinePlayerQuery(OnlinePlayersSnapshot snapshot) => this.snapshot = snapshot;

            public Task<OnlinePlayersSnapshot> GetOnlineAsync(CancellationToken cancellationToken) =>
                Task.FromResult(snapshot);
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class ThrowingOnlinePlayerQuery : IOnlinePlayerQuery
        {
            public Task<OnlinePlayersSnapshot> GetOnlineAsync(CancellationToken cancellationToken) =>
                Task.FromException<OnlinePlayersSnapshot>(new InvalidOperationException("unavailable"));
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingHistoryStore : IPlayerHistoryStore
        {
            private readonly IReadOnlyList<HistoricalPlayerLastRetainedLocation> locations;

            public RecordingHistoryStore(params HistoricalPlayerLastRetainedLocation[] locations) =>
                this.locations = locations;

            public HistoricalPlayerLastLocationsStoreQuery? QueryValue { get; private set; }

            public IReadOnlyList<HistoricalPlayerLastRetainedLocation> GetHistoricalPlayerLastRetainedLocations(
                HistoricalPlayerLastLocationsStoreQuery query)
            {
                QueryValue = query;
                return locations;
            }

            public void Append(PlayerSnapshot snapshot) => throw new NotSupportedException();
            public void AppendGap(PlayerHistoryGap gap) => throw new NotSupportedException();
            public HistoricalPlayersPage GetPlayers(HistoricalPlayersQuery query) => throw new NotSupportedException();
            public HistoricalPlayerDetails? GetPlayer(string crossplatformId) => throw new NotSupportedException();
            public PlayerHistorySnapshotsPage GetSnapshots(PlayerHistorySnapshotsQuery query) => throw new NotSupportedException();
            public PlayerTrackHistory? GetPlayerTrack(GetPlayerTrackQuery query) => throw new NotSupportedException();
            public int Compact(DateTimeOffset utcNow, int maximumDeletes) => throw new NotSupportedException();
        }
    }
}
