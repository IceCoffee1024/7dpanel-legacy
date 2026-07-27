using System;
using System.IO;

namespace LSTY.SevenDPanel.Hosting
{
    public sealed class PanelPlayerEvidenceOptions
    {
        public const string DefaultServerId = "local";
        public const string DefaultTimeZoneId = "UTC";
        public const int DefaultQueueCapacity = 256;
        public static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);

        private PanelPlayerEvidenceOptions(string serverId, TimeZoneInfo timeZone)
        {
            ServerId = serverId;
            TimeZone = timeZone;
        }

        public string ServerId { get; }
        public TimeZoneInfo TimeZone { get; }
        public int QueueCapacity => DefaultQueueCapacity;
        public TimeSpan DrainTimeout => DefaultDrainTimeout;
        public TimeSpan Retention => DefaultRetention;

        public static PanelPlayerEvidenceOptions Default { get; } =
            new PanelPlayerEvidenceOptions(DefaultServerId, TimeZoneInfo.Utc);

        public static PanelPlayerEvidenceOptions FromBinding(
            string? serverId,
            string? timeZoneId)
        {
            var normalizedServerId = (serverId ?? string.Empty).Trim();
            if (normalizedServerId.Length == 0) normalizedServerId = DefaultServerId;
            var normalizedTimeZoneId = (timeZoneId ?? string.Empty).Trim();
            if (normalizedTimeZoneId.Length == 0) normalizedTimeZoneId = DefaultTimeZoneId;

            try
            {
                var timeZone = string.Equals(
                    normalizedTimeZoneId,
                    DefaultTimeZoneId,
                    StringComparison.OrdinalIgnoreCase)
                    ? TimeZoneInfo.Utc
                    : TimeZoneInfo.FindSystemTimeZoneById(normalizedTimeZoneId);
                return new PanelPlayerEvidenceOptions(normalizedServerId, timeZone);
            }
            catch (TimeZoneNotFoundException exception)
            {
                throw new InvalidDataException("Player evidence time zone is not installed.", exception);
            }
            catch (InvalidTimeZoneException exception)
            {
                throw new InvalidDataException("Player evidence time zone is invalid.", exception);
            }
        }
    }
}
