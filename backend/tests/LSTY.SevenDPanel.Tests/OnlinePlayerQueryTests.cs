using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Application")]
    public sealed class OnlinePlayerQueryTests
    {
        [Fact]
        public async Task GetOnlinePlayersUseCase_forwards_the_query_once_and_returns_the_same_result()
        {
            var expected = new OnlinePlayersSnapshot(
                new[]
                {
                    CreatePlayer(
                        entityId: 1,
                        name: "Alice",
                        platformIdentity: new PlayerPlatformIdentity("steam:alice", "steam"),
                        crossplatformIdentity: null,
                        observedAtUtc: new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero))
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
                CreatePlayer(
                    entityId: 1,
                    name: "Alice",
                    platformIdentity: new PlayerPlatformIdentity("steam:alice", "steam"),
                    crossplatformIdentity: null,
                    observedAtUtc: new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero))
            };

            var snapshot = new OnlinePlayersSnapshot(source);
            source.Clear();

            Assert.Single(snapshot.Players);
            Assert.Equal("Alice", snapshot.Players[0].Name);
        }

        [Fact]
        public void Empty_player_collection_is_non_null()
        {
            var snapshot = new OnlinePlayersSnapshot(Array.Empty<PlayerSnapshot>());

            Assert.NotNull(snapshot.Players);
            Assert.Empty(snapshot.Players);
        }

        [Fact]
        public void PlayerSnapshot_preserves_the_observation_time()
        {
            var observedAtUtc = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero);
            var player = CreatePlayer(
                entityId: 1,
                name: "Alice",
                platformIdentity: new PlayerPlatformIdentity("steam:alice", "steam"),
                crossplatformIdentity: null,
                observedAtUtc: observedAtUtc);

            Assert.Equal(observedAtUtc, player.ObservedAtUtc);
        }

        [Fact]
        public void PlayerSnapshot_preserves_the_complete_observation()
        {
            var observedAtUtc = new DateTimeOffset(2026, 7, 24, 9, 30, 0, TimeSpan.Zero);
            var player = CreatePlayer(observedAtUtc);

            Assert.Equal(171, player.EntityId);
            Assert.Equal("Player", player.Name);
            Assert.Equal("Steam_76561198000000000", player.PlatformIdentity.CombinedId);
            Assert.Equal("EOS_0002", player.CrossplatformIdentity?.CombinedId);
            Assert.Equal(PlayerDeviceType.Windows, player.DeviceType);
            Assert.Equal("192.0.2.10", player.Ip);
            Assert.Equal(42, player.Ping);
            Assert.Equal("V 3.0.1", player.CompatibilityVersion);
            Assert.Equal("18446744073709551615", player.DiscordUserId);
            Assert.Equal(1000, player.PermissionLevel);
            Assert.Equal(100.5f, player.Position.X);
            Assert.Equal(51f, player.Position.Y);
            Assert.Equal(200.25f, player.Position.Z);
            Assert.False(player.IsDead);
            Assert.Equal(93, player.Health);
            Assert.Equal(100, player.MaxHealth);
            Assert.Equal(18, player.Level);
            Assert.Equal("Friends", player.PlayGroup);
            Assert.Equal(new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero), player.LastLoginUtc);
            Assert.Equal(123, player.GameStage);
            Assert.Equal(0, player.ExpToNextLevel);
            Assert.Equal(0, player.SkillPoints);
            Assert.Equal(12.5f, player.Bedroll?.X);
            Assert.Equal(64f, player.Bedroll?.Y);
            Assert.Equal(-8.25f, player.Bedroll?.Z);
            Assert.Equal(827, player.Score);
            Assert.Equal(317, player.ZombieKills);
            Assert.Equal(2, player.PlayerKills);
            Assert.Equal(4, player.Deaths);
            Assert.Equal(4823.5f, player.TotalTimePlayedMinutes);
            Assert.Equal(127540.75f, player.DistanceWalkedMeters);
            Assert.Equal(2360u, player.TotalItemsCrafted);
            Assert.Equal(920.25f, player.LongestLifeMinutes);
            Assert.Equal(134.5f, player.CurrentLifeMinutes);
            Assert.Equal(observedAtUtc, player.ObservedAtUtc);
        }

        [Fact]
        public void PlayerPosition_rejects_non_finite_axes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerPosition(float.NaN, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerPosition(0, float.PositiveInfinity, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerPosition(0, 0, float.NegativeInfinity));
        }

        [Fact]
        public void PlayerSnapshot_preserves_null_optional_values()
        {
            var player = CreatePlayer(
                includeCrossplatformIdentity: false,
                ip: null,
                compatibilityVersion: null,
                discordUserId: null,
                playGroup: null,
                includeLastLoginUtc: false,
                gameStage: null,
                expToNextLevel: null,
                skillPoints: null,
                includeBedroll: false);

            Assert.Null(player.CrossplatformIdentity);
            Assert.Null(player.Ip);
            Assert.Null(player.CompatibilityVersion);
            Assert.Null(player.DiscordUserId);
            Assert.Null(player.PlayGroup);
            Assert.Null(player.LastLoginUtc);
            Assert.Null(player.GameStage);
            Assert.Null(player.ExpToNextLevel);
            Assert.Null(player.SkillPoints);
            Assert.Null(player.Bedroll);
        }

        [Fact]
        public void PlayerSnapshot_rejects_invalid_required_values()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlayer(entityId: -1));
            Assert.Throws<ArgumentException>(() => CreatePlayer(name: "   "));
            Assert.Throws<ArgumentNullException>(() => CreatePlayer(includePlatformIdentity: false));
        }

        [Fact]
        public void PlayerSnapshot_rejects_empty_optional_strings()
        {
            Assert.Throws<ArgumentException>(() => CreatePlayer(ip: " "));
            Assert.Throws<ArgumentException>(() => CreatePlayer(compatibilityVersion: " "));
            Assert.Throws<ArgumentException>(() => CreatePlayer(discordUserId: " "));
        }

        [Fact]
        public void PlayerSnapshot_rejects_invalid_cumulative_values()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlayer(totalTimePlayedMinutes: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlayer(distanceWalkedMeters: float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlayer(longestLifeMinutes: float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlayer(currentLifeMinutes: -1));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void PlayerSnapshot_rejects_an_empty_player_name(string name)
        {
            Assert.Throws<ArgumentException>(() =>
                CreatePlayer(
                    name: name,
                    platformIdentity: new PlayerPlatformIdentity("steam:alice", "steam"),
                    crossplatformIdentity: null,
                    observedAtUtc: DateTimeOffset.UtcNow));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void PlayerPlatformIdentity_rejects_empty_identity_strings(string combinedId)
        {
            Assert.Throws<ArgumentException>(() =>
                new PlayerPlatformIdentity(combinedId, "steam"));
        }

        private static PlayerSnapshot CreatePlayer(
            DateTimeOffset? observedAtUtc = null,
            int entityId = 171,
            string name = "Player",
            bool includePlatformIdentity = true,
            bool includeCrossplatformIdentity = true,
            PlayerPlatformIdentity? platformIdentity = null,
            PlayerPlatformIdentity? crossplatformIdentity = null,
            string? ip = "192.0.2.10",
            string? compatibilityVersion = "V 3.0.1",
            string? discordUserId = "18446744073709551615",
            string? playGroup = "Friends",
            DateTimeOffset? lastLoginUtc = null,
            bool includeLastLoginUtc = true,
            int? gameStage = 123,
            int? expToNextLevel = 0,
            int? skillPoints = 0,
            PlayerPosition? bedroll = null,
            bool includeBedroll = true,
            float totalTimePlayedMinutes = 4823.5f,
            float distanceWalkedMeters = 127540.75f,
            float longestLifeMinutes = 920.25f,
            float currentLifeMinutes = 134.5f)
        {
            return new PlayerSnapshot(
                entityId,
                name,
                platformIdentity ?? (includePlatformIdentity
                    ? new PlayerPlatformIdentity("Steam_76561198000000000", "Steam")
                    : null!),
                crossplatformIdentity ?? (includeCrossplatformIdentity
                    ? new PlayerPlatformIdentity("EOS_0002", "EOS")
                    : null),
                PlayerDeviceType.Windows,
                ip,
                42,
                compatibilityVersion,
                discordUserId,
                1000,
                new PlayerPosition(100.5f, 51f, 200.25f),
                false,
                93,
                100,
                18,
                playGroup,
                includeLastLoginUtc
                    ? lastLoginUtc ?? new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero)
                    : null,
                gameStage,
                expToNextLevel,
                skillPoints,
                includeBedroll ? bedroll ?? new PlayerPosition(12.5f, 64f, -8.25f) : null,
                827,
                317,
                2,
                4,
                totalTimePlayedMinutes,
                distanceWalkedMeters,
                2360u,
                longestLifeMinutes,
                currentLifeMinutes,
                observedAtUtc ?? new DateTimeOffset(2026, 7, 24, 9, 30, 0, TimeSpan.Zero));
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

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
