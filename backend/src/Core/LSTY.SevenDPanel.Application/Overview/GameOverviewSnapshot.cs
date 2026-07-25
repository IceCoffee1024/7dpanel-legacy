using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GameOverviewSnapshot
    {
        public GameOverviewSnapshot(AvailabilityState availability, DateTimeOffset? sampledAtUtc, string? gameTitle, string? saveGameName, string? worldName, long? worldSessionUptimeSeconds, string? version, string? gameMode, string? difficulty, string? region, string? language, string? connectionAddress, int? connectionPort, int? onlinePlayerCount, int? maximumPlayerCount, int? historicalPlayerCount, double? framesPerSecond, string? gameTime)
        {
            Availability = availability;
            SampledAtUtc = sampledAtUtc;
            GameTitle = gameTitle;
            SaveGameName = saveGameName;
            WorldName = worldName;
            WorldSessionUptimeSeconds = worldSessionUptimeSeconds;
            Version = version;
            GameMode = gameMode;
            Difficulty = difficulty;
            Region = region;
            Language = language;
            ConnectionAddress = connectionAddress;
            ConnectionPort = connectionPort;
            OnlinePlayerCount = onlinePlayerCount;
            MaximumPlayerCount = maximumPlayerCount;
            HistoricalPlayerCount = historicalPlayerCount;
            FramesPerSecond = framesPerSecond;
            GameTime = gameTime;
        }

        public AvailabilityState Availability { get; }
        public DateTimeOffset? SampledAtUtc { get; }
        public string? GameTitle { get; }
        public string? SaveGameName { get; }
        public string? WorldName { get; }
        public long? WorldSessionUptimeSeconds { get; }
        public string? Version { get; }
        public string? GameMode { get; }
        public string? Difficulty { get; }
        public string? Region { get; }
        public string? Language { get; }
        public string? ConnectionAddress { get; }
        public int? ConnectionPort { get; }
        public int? OnlinePlayerCount { get; }
        public int? MaximumPlayerCount { get; }
        public int? HistoricalPlayerCount { get; }
        public double? FramesPerSecond { get; }
        public string? GameTime { get; }

        public static GameOverviewSnapshot Unavailable() => new GameOverviewSnapshot(AvailabilityState.Unavailable, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
    }
}
