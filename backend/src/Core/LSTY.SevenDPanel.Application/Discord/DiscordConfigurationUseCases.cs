using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LSTY.SevenDPanel.Application.Discord
{
    public sealed class GetDiscordConfigurationUseCase
    {
        private readonly IDiscordIntegrationStore store;

        public GetDiscordConfigurationUseCase(IDiscordIntegrationStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public DiscordConfigurationSummary Execute()
        {
            var settings = store.GetSettings();
            var metadata = store.ListSecretMetadata();
            var targets = store.ListTargets();
            var hasProxyCredentials = metadata.Any(secret =>
                string.Equals(secret.SecretKey, DiscordSecretKeys.ProxyCredentials, StringComparison.Ordinal));
            var metadataByKey = metadata.ToDictionary(secret => secret.SecretKey, StringComparer.Ordinal);
            var secretKeys = new HashSet<string>(metadataByKey.Keys, StringComparer.Ordinal)
            {
                DiscordSecretKeys.BotToken,
                DiscordSecretKeys.ProxyCredentials
            };
            foreach (var target in targets.Where(target =>
                         string.Equals(target.DeliveryMode, "Webhook", StringComparison.Ordinal)))
                secretKeys.Add(DiscordSecretKeys.WebhookUrl(target.TargetKey));
            var secrets = secretKeys
                .OrderBy(key => key, StringComparer.Ordinal)
                .Select(key => metadataByKey.TryGetValue(key, out var secret)
                    ? new DiscordSecretSummary(
                        secret.SecretKey,
                        true,
                        secret.Fingerprint,
                        secret.UpdatedAtUtc)
                    : new DiscordSecretSummary(key, false, null, null))
                .ToArray();
            if (settings == null)
            {
                return new DiscordConfigurationSummary(
                    0,
                    false,
                    DiscordIntegrationMode.Webhook,
                    null,
                    null,
                    null,
                    false,
                    false,
                    new DiscordProxySummary(false, null, hasProxyCredentials),
                    targets,
                    secrets,
                    null);
            }

            return new DiscordConfigurationSummary(
                settings.Version,
                settings.IsEnabled,
                settings.Mode,
                settings.ApplicationId,
                settings.GuildId,
                settings.PublicChannelId,
                settings.BridgeGameToDiscord,
                settings.BridgeDiscordToGame,
                new DiscordProxySummary(
                    settings.ProxyEnabled,
                    SafeProxyEndpoint(settings.ProxyUri),
                    hasProxyCredentials),
                targets,
                secrets,
                settings.UpdatedAtUtc);
        }

        internal static string? SafeProxyEndpoint(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                throw new DiscordDeliveryValidationException();
            var builder = new UriBuilder(uri)
            {
                UserName = string.Empty,
                Password = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri.GetLeftPart(UriPartial.Authority);
        }
    }

    public sealed class SaveDiscordConfigurationUseCase
    {
        private readonly IDiscordIntegrationStore store;
        private readonly Func<DateTimeOffset> utcNow;

        public SaveDiscordConfigurationUseCase(
            IDiscordIntegrationStore store,
            Func<DateTimeOffset> utcNow)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public DiscordConfigurationSummary Execute(DiscordConfigurationUpdate update)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            if (update.ExpectedVersion < 0)
                throw new ArgumentOutOfRangeException(nameof(update));
            var currentVersion = store.GetSettings()?.Version ?? 0;
            if (currentVersion != update.ExpectedVersion)
                throw new DiscordIntegrationVersionConflictException();
            var now = RequireUtc(utcNow());
            var safeProxyUri = NormalizeProxy(update.ProxyEnabled, update.ProxyUri, now);
            ValidateTargets(update.Targets);
            ValidateEnabledMode(update);
            store.SaveSettings(new DiscordIntegrationSettings(
                update.ExpectedVersion + 1,
                update.IsEnabled,
                update.Mode,
                NormalizeOptional(update.ApplicationId),
                NormalizeOptional(update.GuildId),
                NormalizeOptional(update.PublicChannelId),
                update.BridgeGameToDiscord,
                update.BridgeDiscordToGame,
                update.ProxyEnabled,
                safeProxyUri,
                now), update.ExpectedVersion);
            foreach (var target in update.Targets) store.SaveTarget(target);
            return new GetDiscordConfigurationUseCase(store).Execute();
        }

        private string? NormalizeProxy(bool enabled, string? value, DateTimeOffset now)
        {
            if (!enabled) return null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var proxy) ||
                (proxy.Scheme != Uri.UriSchemeHttp && proxy.Scheme != Uri.UriSchemeHttps))
                throw new DiscordDeliveryValidationException();
            if (!string.IsNullOrEmpty(proxy.UserInfo))
            {
                new SetDiscordSecretUseCase(store, () => now)
                    .Execute(DiscordSecretKeys.ProxyCredentials, proxy.UserInfo);
            }
            var builder = new UriBuilder(proxy)
            {
                UserName = string.Empty,
                Password = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri.AbsoluteUri;
        }

        private void ValidateEnabledMode(DiscordConfigurationUpdate update)
        {
            if (!update.IsEnabled) return;
            if (update.Mode == DiscordIntegrationMode.Bot)
            {
                if (store.GetSecret(DiscordSecretKeys.BotToken) == null)
                    throw new DiscordDeliveryValidationException();
                var hasChannel = !string.IsNullOrWhiteSpace(update.PublicChannelId) ||
                    update.Targets.Any(target =>
                        target.IsEnabled &&
                        string.Equals(target.DeliveryMode, "Bot", StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(target.ChannelId));
                if (!hasChannel) throw new DiscordDeliveryValidationException();
                return;
            }

            var hasWebhook = update.Targets.Any(target =>
                target.IsEnabled &&
                string.Equals(target.DeliveryMode, "Webhook", StringComparison.Ordinal) &&
                store.GetSecret(DiscordSecretKeys.WebhookUrl(target.TargetKey)) != null);
            if (!hasWebhook) throw new DiscordDeliveryValidationException();
        }

        private static void ValidateTargets(IReadOnlyList<DiscordTarget> targets)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in targets)
            {
                if (target == null) throw new DiscordDeliveryValidationException();
                if (string.IsNullOrWhiteSpace(target.TargetKey) ||
                    !keys.Add(target.TargetKey) ||
                    (!string.Equals(target.DeliveryMode, "Webhook", StringComparison.Ordinal) &&
                     !string.Equals(target.DeliveryMode, "Bot", StringComparison.Ordinal)) ||
                    (string.Equals(target.DeliveryMode, "Bot", StringComparison.Ordinal) &&
                     string.IsNullOrWhiteSpace(target.ChannelId)))
                    throw new DiscordDeliveryValidationException();
            }
        }

        private static string? NormalizeOptional(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        private static DateTimeOffset RequireUtc(DateTimeOffset value) =>
            value.Offset == TimeSpan.Zero
                ? value
                : throw new InvalidOperationException("discord_clock_not_utc");
    }

    public sealed class SetDiscordSecretUseCase
    {
        private readonly IDiscordIntegrationStore store;
        private readonly Func<DateTimeOffset> utcNow;

        public SetDiscordSecretUseCase(
            IDiscordIntegrationStore store,
            Func<DateTimeOffset> utcNow)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public DiscordSecretSummary Execute(string secretKey, string? secretValue)
        {
            if (string.IsNullOrWhiteSpace(secretKey))
                throw new ArgumentException("A secret key is required.", nameof(secretKey));
            var key = secretKey.Trim();
            if (string.IsNullOrEmpty(secretValue))
            {
                store.DeleteSecret(key);
                return new DiscordSecretSummary(key, false, null, null);
            }

            var now = utcNow();
            if (now.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("discord_clock_not_utc");
            var fingerprint = DiscordSecretFingerprint.Compute(secretValue!);
            store.SetSecret(new DiscordSecretValue(key, secretValue!, fingerprint, now));
            return new DiscordSecretSummary(key, true, fingerprint, now);
        }
    }

    public static class DiscordSecretFingerprint
    {
        public static string Compute(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            return string.Concat(hash.Take(6).Select(part => part.ToString("x2")));
        }
    }
}
