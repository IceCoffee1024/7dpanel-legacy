using System;
using System.Linq;

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
        DiscordHealthSection Inbound)
    {
        private readonly string? loadedGatewayBotTokenFingerprint;

        public DiscordHealthSnapshot(
            DiscordHealthSection gateway,
            DiscordHealthSection inbound,
            string? loadedGatewayBotTokenFingerprint)
            : this(gateway, inbound) =>
            this.loadedGatewayBotTokenFingerprint = loadedGatewayBotTokenFingerprint;

        public bool HasLoadedGatewayBotToken =>
            loadedGatewayBotTokenFingerprint != null;

        public bool IsGatewayBotTokenLoaded(string fingerprint) =>
            string.Equals(
                fingerprint,
                loadedGatewayBotTokenFingerprint,
                StringComparison.Ordinal);

        public override string ToString() =>
            $"DiscordHealthSnapshot {{ Gateway = {Gateway}, Inbound = {Inbound} }}";
    }

    public interface IDiscordIntegrationHealthSource
    {
        DiscordHealthSnapshot GetHealth();
    }

    public interface IDiscordGatewayHealthSink
    {
        void ObserveLoadedGatewayBotTokenFingerprint(string? fingerprint);

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
                : GatewayHealth(
                    settings,
                    health.Gateway,
                    health);
            return new DiscordHealthSnapshot(gateway, health.Inbound);
        }

        private DiscordHealthSection GatewayHealth(
            DiscordIntegrationSettings settings,
            DiscordHealthSection runtimeHealth,
            DiscordHealthSnapshot runtimeHealthSnapshot)
        {
            var botToken = store.ListSecretMetadata().FirstOrDefault(secret =>
                string.Equals(
                    secret.SecretKey,
                    DiscordSecretKeys.BotToken,
                    StringComparison.Ordinal));
            if (botToken == null ||
                string.IsNullOrWhiteSpace(settings.GuildId) ||
                string.IsNullOrWhiteSpace(settings.PublicChannelId))
            {
                return new DiscordHealthSection(
                    DiscordHealthState.Unavailable,
                    "discord_gateway_configuration_incomplete",
                    settings.UpdatedAtUtc);
            }

            if (runtimeHealthSnapshot.HasLoadedGatewayBotToken &&
                !runtimeHealthSnapshot.IsGatewayBotTokenLoaded(botToken.Fingerprint))
            {
                return new DiscordHealthSection(
                    DiscordHealthState.Degraded,
                    "discord_gateway_restart_required",
                    botToken.UpdatedAtUtc);
            }

            return runtimeHealth;
        }
    }
}
