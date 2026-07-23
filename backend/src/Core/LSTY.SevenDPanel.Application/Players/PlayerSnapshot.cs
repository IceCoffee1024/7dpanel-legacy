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
            int ping,
            int level,
            int health,
            DateTimeOffset observedAtUtc)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A player name is required.", nameof(name));

            if (platformIdentity == null)
                throw new ArgumentNullException(nameof(platformIdentity));

            EntityId = entityId;
            Name = name;
            PlatformIdentity = platformIdentity;
            CrossplatformIdentity = crossplatformIdentity;
            Ping = ping;
            Level = level;
            Health = health;
            ObservedAtUtc = observedAtUtc;
        }

        public int EntityId { get; }

        public string Name { get; }

        public PlayerPlatformIdentity PlatformIdentity { get; }

        public PlayerPlatformIdentity? CrossplatformIdentity { get; }

        public int Ping { get; }

        public int Level { get; }

        public int Health { get; }

        public DateTimeOffset ObservedAtUtc { get; }
    }
}
