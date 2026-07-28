using System;

namespace LSTY.SevenDPanel.Application.Discord
{
    public enum DiscordHealthState
    {
        Disabled,
        Connecting,
        Connected,
        Healthy,
        Degraded,
        Unavailable
    }

    public sealed record DiscordHealthSection(
        DiscordHealthState State,
        string? ErrorCode,
        DateTimeOffset? ObservedAtUtc);

    public sealed record DiscordHealthSnapshot(
        DiscordHealthSection Gateway,
        DiscordHealthSection Inbound);

    public interface IDiscordIntegrationHealthSource
    {
        DiscordHealthSnapshot GetHealth();
    }

    public interface IDiscordGatewayHealthSink
    {
        void ObserveGatewayHealth(
            DiscordHealthState state,
            string? errorCode,
            DateTimeOffset observedAtUtc);
    }

    public sealed class GetDiscordHealthUseCase
    {
        private readonly IDiscordIntegrationStore store;
        private readonly IDiscordIntegrationHealthSource source;

        public GetDiscordHealthUseCase(
            IDiscordIntegrationStore store,
            IDiscordIntegrationHealthSource source)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public DiscordHealthSnapshot Execute()
        {
            var health = source.GetHealth() ??
                throw new InvalidOperationException("discord_health_snapshot_missing");
            var settings = store.GetSettings();
            if (settings == null || !settings.IsEnabled)
            {
                var disabled = new DiscordHealthSection(
                    DiscordHealthState.Disabled,
                    null,
                    settings?.UpdatedAtUtc);
                return new DiscordHealthSnapshot(disabled, disabled);
            }

            var gateway = settings.Mode != DiscordIntegrationMode.Bot ||
                          !settings.BridgeDiscordToGame
                ? new DiscordHealthSection(
                    DiscordHealthState.Disabled,
                    null,
                    settings.UpdatedAtUtc)
                : GatewayHealth(settings, health.Gateway);
            return new DiscordHealthSnapshot(gateway, health.Inbound);
        }

        private DiscordHealthSection GatewayHealth(
            DiscordIntegrationSettings settings,
            DiscordHealthSection runtimeHealth)
        {
            if (store.GetSecret(DiscordSecretKeys.BotToken) == null ||
                string.IsNullOrWhiteSpace(settings.GuildId) ||
                string.IsNullOrWhiteSpace(settings.PublicChannelId))
            {
                return new DiscordHealthSection(
                    DiscordHealthState.Unavailable,
                    "discord_gateway_configuration_incomplete",
                    settings.UpdatedAtUtc);
            }

            return runtimeHealth;
        }
    }
}
