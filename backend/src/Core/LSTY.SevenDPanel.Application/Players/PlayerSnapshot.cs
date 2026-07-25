using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class PlayerSnapshot
    {
        public PlayerSnapshot(
            int entityId,
            string name,
            PlayerPlatformIdentity platformIdentity,
            PlayerPlatformIdentity? crossplatformIdentity,
            PlayerDeviceType deviceType,
            string? ip,
            int ping,
            string? compatibilityVersion,
            string? discordUserId,
            int permissionLevel,
            PlayerPosition position,
            bool isDead,
            int health,
            int maxHealth,
            int level,
            int score,
            int zombieKills,
            int playerKills,
            int deaths,
            float totalTimePlayedMinutes,
            float distanceWalkedMeters,
            uint totalItemsCrafted,
            float longestLifeMinutes,
            float currentLifeMinutes,
            DateTimeOffset observedAtUtc)
            : this(
                entityId,
                name,
                platformIdentity,
                crossplatformIdentity,
                deviceType,
                ip,
                ping,
                compatibilityVersion,
                discordUserId,
                permissionLevel,
                position,
                isDead,
                health,
                maxHealth,
                level,
                null,
                null,
                null,
                null,
                null,
                null,
                score,
                zombieKills,
                playerKills,
                deaths,
                totalTimePlayedMinutes,
                distanceWalkedMeters,
                totalItemsCrafted,
                longestLifeMinutes,
                currentLifeMinutes,
                observedAtUtc)
        {
        }

        public PlayerSnapshot(
            int entityId,
            string name,
            PlayerPlatformIdentity platformIdentity,
            PlayerPlatformIdentity? crossplatformIdentity,
            PlayerDeviceType deviceType,
            string? ip,
            int ping,
            string? compatibilityVersion,
            string? discordUserId,
            int permissionLevel,
            PlayerPosition position,
            bool isDead,
            int health,
            int maxHealth,
            int level,
            string? playGroup,
            DateTimeOffset? lastLoginUtc,
            int? gameStage,
            int? expToNextLevel,
            int? skillPoints,
            PlayerPosition? bedroll,
            int score,
            int zombieKills,
            int playerKills,
            int deaths,
            float totalTimePlayedMinutes,
            float distanceWalkedMeters,
            uint totalItemsCrafted,
            float longestLifeMinutes,
            float currentLifeMinutes,
            DateTimeOffset observedAtUtc)
        {
            if (entityId < 0)
                throw new ArgumentOutOfRangeException(nameof(entityId));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A player name is required.", nameof(name));
            if (platformIdentity == null)
                throw new ArgumentNullException(nameof(platformIdentity));

            ValidateOptionalString(ip, nameof(ip));
            ValidateOptionalString(compatibilityVersion, nameof(compatibilityVersion));
            ValidateOptionalString(discordUserId, nameof(discordUserId));
            ValidateOptionalString(playGroup, nameof(playGroup));
            ValidateNonNegativeFinite(totalTimePlayedMinutes, nameof(totalTimePlayedMinutes));
            ValidateNonNegativeFinite(distanceWalkedMeters, nameof(distanceWalkedMeters));
            ValidateNonNegativeFinite(longestLifeMinutes, nameof(longestLifeMinutes));
            ValidateNonNegativeFinite(currentLifeMinutes, nameof(currentLifeMinutes));

            EntityId = entityId;
            Name = name;
            PlatformIdentity = platformIdentity;
            CrossplatformIdentity = crossplatformIdentity;
            DeviceType = deviceType;
            Ip = ip;
            Ping = ping;
            CompatibilityVersion = compatibilityVersion;
            DiscordUserId = discordUserId;
            PermissionLevel = permissionLevel;
            Position = position;
            IsDead = isDead;
            Health = health;
            MaxHealth = maxHealth;
            Level = level;
            PlayGroup = playGroup;
            LastLoginUtc = lastLoginUtc;
            GameStage = gameStage;
            ExpToNextLevel = expToNextLevel;
            SkillPoints = skillPoints;
            Bedroll = bedroll;
            Score = score;
            ZombieKills = zombieKills;
            PlayerKills = playerKills;
            Deaths = deaths;
            TotalTimePlayedMinutes = totalTimePlayedMinutes;
            DistanceWalkedMeters = distanceWalkedMeters;
            TotalItemsCrafted = totalItemsCrafted;
            LongestLifeMinutes = longestLifeMinutes;
            CurrentLifeMinutes = currentLifeMinutes;
            ObservedAtUtc = observedAtUtc;
        }

        public int EntityId { get; }

        public string Name { get; }

        public PlayerPlatformIdentity PlatformIdentity { get; }

        public PlayerPlatformIdentity? CrossplatformIdentity { get; }

        public PlayerDeviceType DeviceType { get; }

        public string? Ip { get; }

        public int Ping { get; }

        public string? CompatibilityVersion { get; }

        public string? DiscordUserId { get; }

        public int PermissionLevel { get; }

        public PlayerPosition Position { get; }

        public bool IsDead { get; }

        public int Level { get; }

        public string? PlayGroup { get; }

        public DateTimeOffset? LastLoginUtc { get; }

        public int? GameStage { get; }

        public int? ExpToNextLevel { get; }

        public int? SkillPoints { get; }

        public PlayerPosition? Bedroll { get; }

        public int Health { get; }

        public int MaxHealth { get; }

        public int Score { get; }

        public int ZombieKills { get; }

        public int PlayerKills { get; }

        public int Deaths { get; }

        public float TotalTimePlayedMinutes { get; }

        public float DistanceWalkedMeters { get; }

        public uint TotalItemsCrafted { get; }

        public float LongestLifeMinutes { get; }

        public float CurrentLifeMinutes { get; }

        public DateTimeOffset ObservedAtUtc { get; }

        private static void ValidateOptionalString(string? value, string parameterName)
        {
            if (value != null && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("An optional player value cannot be blank.", parameterName);
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
