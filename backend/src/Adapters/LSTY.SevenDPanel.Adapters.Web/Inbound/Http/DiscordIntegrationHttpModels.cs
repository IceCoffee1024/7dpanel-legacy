using System;
using System.Linq;
using LSTY.SevenDPanel.Application.Discord;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class DiscordConfigurationUpdateHttpRequest
    {
        public long? ExpectedVersion { get; set; }
        public bool? IsEnabled { get; set; }
        public string? Mode { get; set; }
        public string? ApplicationId { get; set; }
        public string? GuildId { get; set; }
        public string? PublicChannelId { get; set; }
        public bool? BridgeGameToDiscord { get; set; }
        public bool? BridgeDiscordToGame { get; set; }
        public bool? ProxyEnabled { get; set; }
        public string? ProxyEndpoint { get; set; }
        public DiscordTargetUpdateHttpRequest[]? Targets { get; set; }
    }

    public sealed class DiscordTargetUpdateHttpRequest
    {
        public string? TargetKey { get; set; }
        public string? DeliveryMode { get; set; }
        public string? ChannelId { get; set; }
        public bool? IsEnabled { get; set; }
    }

    public sealed class DiscordTestHttpRequest
    {
        public string? TargetKey { get; set; }
    }

    public sealed class DiscordBindingCodeCreateHttpRequest
    {
        public string? CrossplatformId { get; set; }
    }

    public sealed class DiscordConfigurationHttpResponse
    {
        public DiscordConfigurationHttpResponse(DiscordConfigurationSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            Version = summary.Version;
            IsEnabled = summary.IsEnabled;
            Mode = summary.Mode.ToString();
            ApplicationId = summary.ApplicationId;
            GuildId = summary.GuildId;
            PublicChannelId = summary.PublicChannelId;
            BridgeGameToDiscord = summary.BridgeGameToDiscord;
            BridgeDiscordToGame = summary.BridgeDiscordToGame;
            Proxy = new DiscordProxyHttpResponse(
                summary.Proxy.IsEnabled,
                summary.Proxy.Endpoint,
                summary.Proxy.HasCredentials);
            HasBotToken = HasSecret(summary, DiscordSecretKeys.BotToken);
            Targets = summary.Targets
                .Select(target => new DiscordTargetHttpResponse(
                    target.TargetKey,
                    target.DeliveryMode,
                    target.ChannelId,
                    target.IsEnabled,
                    string.Equals(
                        target.DeliveryMode,
                        DiscordIntegrationMode.Webhook.ToString(),
                        StringComparison.Ordinal) &&
                    HasSecret(summary, DiscordSecretKeys.WebhookUrl(target.TargetKey))))
                .ToArray();
            UpdatedAtUtc = summary.UpdatedAtUtc;
        }

        public long Version { get; }
        public bool IsEnabled { get; }
        public string Mode { get; }
        public string? ApplicationId { get; }
        public string? GuildId { get; }
        public string? PublicChannelId { get; }
        public bool BridgeGameToDiscord { get; }
        public bool BridgeDiscordToGame { get; }
        public DiscordProxyHttpResponse Proxy { get; }
        public bool HasBotToken { get; }
        public DiscordTargetHttpResponse[] Targets { get; }
        public DateTimeOffset? UpdatedAtUtc { get; }

        private static bool HasSecret(
            DiscordConfigurationSummary summary,
            string secretKey) =>
            summary.Secrets.Any(secret =>
                secret.IsSet &&
                string.Equals(secret.SecretKey, secretKey, StringComparison.Ordinal));
    }

    public sealed class DiscordProxyHttpResponse
    {
        public DiscordProxyHttpResponse(
            bool isEnabled,
            string? endpoint,
            bool hasCredentials)
        {
            IsEnabled = isEnabled;
            Endpoint = endpoint;
            HasCredentials = hasCredentials;
        }

        public bool IsEnabled { get; }
        public string? Endpoint { get; }
        public bool HasCredentials { get; }
    }

    public sealed class DiscordHealthHttpResponse
    {
        public DiscordHealthHttpResponse(DiscordHealthSnapshot health)
        {
            if (health == null) throw new ArgumentNullException(nameof(health));
            Gateway = new DiscordHealthSectionHttpResponse(health.Gateway);
            Inbound = new DiscordHealthSectionHttpResponse(health.Inbound);
        }

        public DiscordHealthSectionHttpResponse Gateway { get; }
        public DiscordHealthSectionHttpResponse Inbound { get; }
    }

    public sealed class DiscordHealthSectionHttpResponse
    {
        public DiscordHealthSectionHttpResponse(DiscordHealthSection health)
        {
            if (health == null) throw new ArgumentNullException(nameof(health));
            State = health.State.ToString();
            ErrorCode = health.ErrorCode;
            ObservedAtUtc = health.ObservedAtUtc;
        }

        public string State { get; }
        public string? ErrorCode { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
    }

    public sealed class DiscordTargetHttpResponse
    {
        public DiscordTargetHttpResponse(
            string targetKey,
            string deliveryMode,
            string? channelId,
            bool isEnabled,
            bool hasCredential)
        {
            TargetKey = targetKey;
            DeliveryMode = deliveryMode;
            ChannelId = channelId;
            IsEnabled = isEnabled;
            HasCredential = hasCredential;
        }

        public string TargetKey { get; }
        public string DeliveryMode { get; }
        public string? ChannelId { get; }
        public bool IsEnabled { get; }
        public bool HasCredential { get; }
    }

    public sealed class DiscordDeliveryHttpResponse
    {
        public DiscordDeliveryHttpResponse(DiscordDeliverySummary delivery)
        {
            if (delivery == null) throw new ArgumentNullException(nameof(delivery));
            DeliveryId = delivery.DeliveryId;
            BusinessKey = delivery.BusinessKey;
            TargetKey = delivery.TargetKey;
            Status = delivery.Status.ToString();
            NextAttemptAtUtc = delivery.NextAttemptAtUtc;
            RetryCount = delivery.RetryCount;
            CreatedAtUtc = delivery.CreatedAtUtc;
            CompletedAtUtc = delivery.CompletedAtUtc;
        }

        public string DeliveryId { get; }
        public string BusinessKey { get; }
        public string TargetKey { get; }
        public string Status { get; }
        public DateTimeOffset? NextAttemptAtUtc { get; }
        public int RetryCount { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
    }

    public sealed class DiscordBindingCodeHttpResponse
    {
        public DiscordBindingCodeHttpResponse(
            string code,
            string codePrefix,
            DateTimeOffset expiresAtUtc)
        {
            Code = code;
            CodePrefix = codePrefix;
            ExpiresAtUtc = expiresAtUtc;
        }

        public string Code { get; }
        public string CodePrefix { get; }
        public DateTimeOffset ExpiresAtUtc { get; }
    }

    public sealed class DiscordBindingHttpResponse
    {
        public DiscordBindingHttpResponse(DiscordBinding binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            DiscordSubject = binding.DiscordSubject;
            CrossplatformId = binding.CrossplatformId;
            IsActive = binding.IsActive;
            CreatedAtUtc = binding.CreatedAtUtc;
            UpdatedAtUtc = binding.UpdatedAtUtc;
        }

        public string DiscordSubject { get; }
        public string CrossplatformId { get; }
        public bool IsActive { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
    }

    public sealed class DiscordCommandHttpResponse
    {
        public DiscordCommandHttpResponse(DiscordCommandSetting command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            CommandKey = command.CommandKey;
            IsEnabled = command.IsEnabled;
            RemoteAllowed = command.RemoteAllowed;
        }

        public string CommandKey { get; }
        public bool IsEnabled { get; }
        public bool RemoteAllowed { get; }
    }
}
