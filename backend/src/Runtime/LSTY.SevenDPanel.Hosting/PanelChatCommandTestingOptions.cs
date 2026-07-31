using System;

namespace LSTY.SevenDPanel.Hosting
{
    public sealed class PanelChatCommandTestingOptions
    {
        public static readonly PanelChatCommandTestingOptions Disabled =
            new PanelChatCommandTestingOptions(false, null, false, false);

        private PanelChatCommandTestingOptions(
            bool enabled,
            string? testPlayerId,
            bool allowTeleport,
            bool allowRewardDelivery)
        {
            Enabled = enabled;
            TestPlayerId = Normalize(testPlayerId);
            AllowTeleport = enabled && allowTeleport;
            AllowRewardDelivery = enabled && allowRewardDelivery;
        }

        public bool Enabled { get; }
        public string? TestPlayerId { get; }
        public bool AllowTeleport { get; }
        public bool AllowRewardDelivery { get; }

        public static PanelChatCommandTestingOptions FromBinding(
            bool enabled,
            string? testPlayerId,
            bool allowTeleport,
            bool allowRewardDelivery)
        {
            var normalizedId = Normalize(testPlayerId);
            if (enabled && normalizedId == null)
                throw new ArgumentException(
                    "An enabled chat-command boundary test requires a stable test player identifier.",
                    nameof(testPlayerId));

            return new PanelChatCommandTestingOptions(
                enabled,
                normalizedId,
                allowTeleport,
                allowRewardDelivery);
        }

        private static string? Normalize(string? value)
        {
            var normalized = (value ?? string.Empty).Trim();
            return normalized.Length == 0 ? null : normalized;
        }
    }
}
