using System;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps
{
    public sealed class SevenDaysMapSample
    {
        public SevenDaysMapSample(
            SevenDaysMapMetadataSample? metadata,
            SevenDaysMapGameTimeSample? gameTime,
            bool metadataCaptureFailed = false,
            bool gameTimeCaptureFailed = false,
            bool worldAvailable = true)
        {
            if (!worldAvailable &&
                (metadata != null || gameTime != null || metadataCaptureFailed || gameTimeCaptureFailed))
            {
                throw new ArgumentException("An unavailable world cannot contain captured fields.", nameof(worldAvailable));
            }
            if (metadataCaptureFailed && metadata != null)
                throw new ArgumentException("Failed metadata capture cannot contain metadata.", nameof(metadata));
            if (gameTimeCaptureFailed && gameTime != null)
                throw new ArgumentException("Failed game-time capture cannot contain game time.", nameof(gameTime));
            Metadata = metadata;
            GameTime = gameTime;
            WorldAvailable = worldAvailable;
            MetadataCaptureFailed = worldAvailable && (metadataCaptureFailed || metadata == null);
            GameTimeCaptureFailed = worldAvailable && (gameTimeCaptureFailed || gameTime == null);
        }

        public SevenDaysMapMetadataSample? Metadata { get; }
        public SevenDaysMapGameTimeSample? GameTime { get; }
        public bool WorldAvailable { get; }
        public bool MetadataCaptureFailed { get; }
        public bool GameTimeCaptureFailed { get; }
    }
}
