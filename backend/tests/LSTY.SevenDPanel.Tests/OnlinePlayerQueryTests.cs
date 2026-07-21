using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class OnlinePlayerQueryTests
    {
        [Fact]
        public async Task GetOnlinePlayersUseCase_forwards_the_query_once_and_returns_the_same_result()
        {
            var expected = new OnlinePlayersSnapshot(
                DateTimeOffset.UtcNow,
                new[]
                {
                    new PlayerSnapshot(
                        1,
                        "Alice",
                        new PlayerPlatformIdentity("steam:alice", "steam"),
                        null,
                        42,
                        10,
                        100)
                });
            var query = new RecordingOnlinePlayerQuery(expected);
            var useCase = new GetOnlinePlayersUseCase(query);

            var result = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

            Assert.Same(expected, result);
            Assert.Equal(1, query.CallCount);
        }

        [Fact]
        public void OnlinePlayersSnapshot_copies_the_source_collection()
        {
            var source = new List<PlayerSnapshot>
            {
                new PlayerSnapshot(
                    1,
                    "Alice",
                    new PlayerPlatformIdentity("steam:alice", "steam"),
                    null,
                    42,
                    10,
                    100)
            };

            var snapshot = new OnlinePlayersSnapshot(DateTimeOffset.UtcNow, source);
            source.Clear();

            Assert.Single(snapshot.Players);
            Assert.Equal("Alice", snapshot.Players[0].Name);
        }

        [Fact]
        public void Empty_player_collection_is_non_null()
        {
            var snapshot = new OnlinePlayersSnapshot(DateTimeOffset.UtcNow, Array.Empty<PlayerSnapshot>());

            Assert.NotNull(snapshot.Players);
            Assert.Empty(snapshot.Players);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void PlayerSnapshot_rejects_an_empty_player_name(string name)
        {
            Assert.Throws<ArgumentException>(() =>
                new PlayerSnapshot(
                    1,
                    name,
                    new PlayerPlatformIdentity("steam:alice", "steam"),
                    null,
                    42,
                    10,
                    100));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void PlayerPlatformIdentity_rejects_empty_identity_strings(string combinedId)
        {
            Assert.Throws<ArgumentException>(() =>
                new PlayerPlatformIdentity(combinedId, "steam"));
        }

        private sealed class RecordingOnlinePlayerQuery : IOnlinePlayerQuery
        {
            private readonly OnlinePlayersSnapshot result;

            public RecordingOnlinePlayerQuery(OnlinePlayersSnapshot result)
            {
                this.result = result;
            }

            public int CallCount { get; private set; }

            public Task<OnlinePlayersSnapshot> GetOnlineAsync(CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult(result);
            }
        }
    }
}
