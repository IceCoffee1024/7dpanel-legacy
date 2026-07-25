using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class HistoricalPlayersResponse
    {
        public HistoricalPlayersResponse(
            IReadOnlyList<HistoricalPlayerSummaryResponse> players,
            string? nextCursor)
        {
            Players = players ?? throw new ArgumentNullException(nameof(players));
            NextCursor = nextCursor;
        }

        public IReadOnlyList<HistoricalPlayerSummaryResponse> Players { get; }

        public string? NextCursor { get; }
    }

    public sealed class HistoricalPlayerDetailsResponse
    {
        public HistoricalPlayerDetailsResponse(
            HistoricalPlayerSummaryResponse player,
            HistoricalPlayerGapSummaryResponse gapSummary)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            GapSummary = gapSummary ?? throw new ArgumentNullException(nameof(gapSummary));
        }

        public HistoricalPlayerSummaryResponse Player { get; }

        public HistoricalPlayerGapSummaryResponse GapSummary { get; }
    }

    public sealed class HistoricalPlayerSnapshotsResponse
    {
        public HistoricalPlayerSnapshotsResponse(
            IReadOnlyList<HistoricalPlayerSnapshotResponse> snapshots,
            long? nextBeforeSnapshotId,
            IReadOnlyList<PlayerHistoryGapResponse> gaps)
        {
            Snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            NextBeforeSnapshotId = nextBeforeSnapshotId;
            Gaps = gaps ?? throw new ArgumentNullException(nameof(gaps));
        }

        public IReadOnlyList<HistoricalPlayerSnapshotResponse> Snapshots { get; }

        public long? NextBeforeSnapshotId { get; }

        public IReadOnlyList<PlayerHistoryGapResponse> Gaps { get; }
    }

    public sealed class HistoricalPlayerSummaryResponse
    {
        public HistoricalPlayerSummaryResponse(HistoricalPlayerSummary player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            CrossplatformId = player.CrossplatformId;
            LatestName = player.LatestName;
            FirstObservedAtUtc = ToUtcString(player.FirstObservedAtUtc);
            LastObservedAtUtc = ToUtcString(player.LastObservedAtUtc);
            TotalObservationCount = player.TotalObservationCount;
            RetainedSnapshotCount = player.RetainedSnapshotCount;
            CompactedSnapshotCount = player.CompactedSnapshotCount;
            HasGaps = player.HasGaps;
        }

        public string CrossplatformId { get; }

        public string LatestName { get; }

        public string FirstObservedAtUtc { get; }

        public string LastObservedAtUtc { get; }

        public long TotalObservationCount { get; }

        public long RetainedSnapshotCount { get; }

        public long CompactedSnapshotCount { get; }

        public bool HasGaps { get; }

        private static string ToUtcString(DateTimeOffset value) =>
            value.ToString("O", CultureInfo.InvariantCulture);
    }

    public sealed class HistoricalPlayerGapSummaryResponse
    {
        public HistoricalPlayerGapSummaryResponse(PlayerHistoryGapSummary gapSummary)
        {
            if (gapSummary == null) throw new ArgumentNullException(nameof(gapSummary));

            GapCount = gapSummary.GapCount;
            DroppedObservationCount = gapSummary.DroppedObservationCount;
        }

        public long GapCount { get; }

        public long DroppedObservationCount { get; }
    }

    public sealed class HistoricalPlayerSnapshotResponse
    {
        public HistoricalPlayerSnapshotResponse(HistoricalPlayerSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var player = snapshot.Player;

            SnapshotId = snapshot.SnapshotId;
            EntityId = player.EntityId;
            Name = player.Name;
            PlatformIdentity = new PlayerHistoryPlatformIdentityResponse(player.PlatformIdentity);
            CrossplatformIdentity = player.CrossplatformIdentity == null
                ? null
                : new PlayerHistoryPlatformIdentityResponse(player.CrossplatformIdentity);
            DeviceType = ToDeviceType(player.DeviceType);
            Ip = player.Ip;
            Ping = player.Ping;
            CompatibilityVersion = player.CompatibilityVersion;
            DiscordUserId = player.DiscordUserId;
            PermissionLevel = player.PermissionLevel;
            Position = new PlayerHistoryPositionResponse(player.Position);
            IsDead = player.IsDead;
            Health = player.Health;
            MaxHealth = player.MaxHealth;
            Level = player.Level;
            PlayGroup = player.PlayGroup;
            LastLoginUtc = player.LastLoginUtc == null ? null : ToUtcString(player.LastLoginUtc.Value);
            GameStage = player.GameStage;
            ExpToNextLevel = player.ExpToNextLevel;
            SkillPoints = player.SkillPoints;
            Bedroll = player.Bedroll == null ? null : new PlayerHistoryPositionResponse(player.Bedroll.Value);
            Score = player.Score;
            ZombieKills = player.ZombieKills;
            PlayerKills = player.PlayerKills;
            Deaths = player.Deaths;
            TotalTimePlayedMinutes = player.TotalTimePlayedMinutes;
            DistanceWalkedMeters = player.DistanceWalkedMeters;
            TotalItemsCrafted = player.TotalItemsCrafted;
            LongestLifeMinutes = player.LongestLifeMinutes;
            CurrentLifeMinutes = player.CurrentLifeMinutes;
            ObservedAtUtc = ToUtcString(player.ObservedAtUtc);
        }

        public long SnapshotId { get; }
        public int EntityId { get; }
        public string Name { get; }
        public PlayerHistoryPlatformIdentityResponse PlatformIdentity { get; }
        public PlayerHistoryPlatformIdentityResponse? CrossplatformIdentity { get; }
        public string DeviceType { get; }
        public string? Ip { get; }
        public int Ping { get; }
        public string? CompatibilityVersion { get; }
        public string? DiscordUserId { get; }
        public int PermissionLevel { get; }
        public PlayerHistoryPositionResponse Position { get; }
        public bool IsDead { get; }
        public int Health { get; }
        public int MaxHealth { get; }
        public int Level { get; }
        public string? PlayGroup { get; }
        public string? LastLoginUtc { get; }
        public int? GameStage { get; }
        public int? ExpToNextLevel { get; }
        public int? SkillPoints { get; }
        public PlayerHistoryPositionResponse? Bedroll { get; }
        public int Score { get; }
        public int ZombieKills { get; }
        public int PlayerKills { get; }
        public int Deaths { get; }
        public float TotalTimePlayedMinutes { get; }
        public float DistanceWalkedMeters { get; }
        public uint TotalItemsCrafted { get; }
        public float LongestLifeMinutes { get; }
        public float CurrentLifeMinutes { get; }
        public string ObservedAtUtc { get; }

        private static string ToUtcString(DateTimeOffset value) =>
            value.ToString("O", CultureInfo.InvariantCulture);

        private static string ToDeviceType(PlayerDeviceType deviceType)
        {
            switch (deviceType)
            {
                case PlayerDeviceType.Linux: return "linux";
                case PlayerDeviceType.Mac: return "mac";
                case PlayerDeviceType.Windows: return "windows";
                case PlayerDeviceType.PlayStation: return "playStation";
                case PlayerDeviceType.Xbox: return "xbox";
                default: return "unknown";
            }
        }
    }

    public sealed class PlayerHistoryGapResponse
    {
        public PlayerHistoryGapResponse(PlayerHistoryGap gap)
        {
            if (gap == null) throw new ArgumentNullException(nameof(gap));

            GapId = gap.GapId;
            CrossplatformId = gap.CrossplatformId;
            StartedAtUtc = ToUtcString(gap.StartedAtUtc);
            CompletedAtUtc = ToUtcString(gap.CompletedAtUtc);
            DroppedCount = gap.DroppedCount;
            Reason = ToReason(gap.Reason);
            RecordedAtUtc = ToUtcString(gap.RecordedAtUtc);
        }

        public string GapId { get; }
        public string CrossplatformId { get; }
        public string StartedAtUtc { get; }
        public string CompletedAtUtc { get; }
        public long DroppedCount { get; }
        public string Reason { get; }
        public string RecordedAtUtc { get; }

        private static string ToUtcString(DateTimeOffset value) =>
            value.ToString("O", CultureInfo.InvariantCulture);

        private static string ToReason(PlayerHistoryGapReason reason)
        {
            switch (reason)
            {
                case PlayerHistoryGapReason.QueueFull: return "queue_full";
                case PlayerHistoryGapReason.StoreFailure: return "store_failure";
                case PlayerHistoryGapReason.ShutdownTimeout: return "shutdown_timeout";
                default: throw new ArgumentOutOfRangeException(nameof(reason));
            }
        }
    }

    public sealed class PlayerHistoryPlatformIdentityResponse
    {
        public PlayerHistoryPlatformIdentityResponse(PlayerPlatformIdentity identity)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));

            CombinedId = identity.CombinedId;
            Platform = identity.Platform;
        }

        public string CombinedId { get; }
        public string Platform { get; }
    }

    public sealed class PlayerHistoryPositionResponse
    {
        public PlayerHistoryPositionResponse(PlayerPosition position)
        {
            X = position.X;
            Y = position.Y;
            Z = position.Z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
    }
}
