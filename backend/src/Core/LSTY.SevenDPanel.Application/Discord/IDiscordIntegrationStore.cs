using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application.Discord
{
    public enum DiscordIntegrationMode
    {
        Webhook,
        Bot
    }

    public enum DiscordDeliveryStatus
    {
        Pending,
        Sending,
        RetryScheduled,
        Succeeded,
        Failed,
        ResultUnknown,
        Cancelled
    }

    public sealed record DiscordIntegrationSettings(
        long Version,
        bool IsEnabled,
        DiscordIntegrationMode Mode,
        string? ApplicationId,
        string? GuildId,
        string? PublicChannelId,
        bool BridgeGameToDiscord,
        bool BridgeDiscordToGame,
        bool ProxyEnabled,
        string? ProxyUri,
        DateTimeOffset UpdatedAtUtc);

    public sealed record DiscordSecretMetadata(
        string SecretKey,
        string Fingerprint,
        DateTimeOffset UpdatedAtUtc);

    public sealed class DiscordSecretValue
    {
        public DiscordSecretValue(
            string secretKey,
            string secretValue,
            string fingerprint,
            DateTimeOffset updatedAtUtc)
        {
            SecretKey = secretKey;
            SecretValue = secretValue;
            Fingerprint = fingerprint;
            UpdatedAtUtc = updatedAtUtc;
        }

        public string SecretKey { get; }
        public string SecretValue { get; }
        public string Fingerprint { get; }
        public DateTimeOffset UpdatedAtUtc { get; }

        public override string ToString() =>
            $"DiscordSecretValue {{ SecretKey = {SecretKey}, SecretValue = [REDACTED], Fingerprint = {Fingerprint}, UpdatedAtUtc = {UpdatedAtUtc:O} }}";
    }

    public sealed record DiscordTarget(
        string TargetKey,
        string DeliveryMode,
        string? ChannelId,
        bool IsEnabled);

    public sealed record DiscordCommandSetting(
        string CommandKey,
        bool IsEnabled,
        bool RemoteAllowed);

    public sealed record DiscordDelivery(
        string DeliveryId,
        string BusinessKey,
        string TargetKey,
        DiscordDeliveryStatus Status,
        string? ContentText,
        string ContentSummary,
        DateTimeOffset? NextAttemptAtUtc,
        int RetryCount,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? CompletedAtUtc);

    public sealed record DiscordDeliveryAttempt(
        string DeliveryId,
        int AttemptNumber,
        DiscordDeliveryStatus Status,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        string? ErrorCode);

    public sealed record DiscordDeliveryEnqueueResult(
        DiscordDelivery Delivery,
        bool WasCreated);

    public sealed record DiscordBindingCode(
        string CodeId,
        string CrossplatformId,
        string CodePrefix,
        byte[] CodeHash,
        DateTimeOffset ExpiresAtUtc);

    public sealed record DiscordBinding(
        string DiscordSubject,
        string CrossplatformId,
        bool IsActive,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    public sealed record DiscordInteraction(
        string InteractionId,
        string CommandKey,
        string Status,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? CompletedAtUtc,
        string? GuildId = null,
        string? ChannelId = null,
        string? DiscordSubject = null,
        byte[]? BindingCodeHash = null);

    public sealed record DiscordInteractionToken(
        string InteractionId,
        string TokenValue,
        DateTimeOffset ExpiresAtUtc);

    public interface IDiscordIntegrationStore
    {
        DiscordIntegrationSettings? GetSettings();

        void SaveSettings(DiscordIntegrationSettings settings, long expectedVersion);

        void SetSecret(DiscordSecretValue secret);

        void DeleteSecret(string secretKey);

        DiscordSecretValue? GetSecret(string secretKey);

        IReadOnlyList<DiscordSecretMetadata> ListSecretMetadata();

        void SaveTarget(DiscordTarget target);

        IReadOnlyList<DiscordTarget> ListTargets();

        DiscordTarget? FindTarget(string targetKey);

        void SaveCommandSetting(DiscordCommandSetting command);

        IReadOnlyList<DiscordCommandSetting> ListCommandSettings();

        DiscordDeliveryEnqueueResult EnqueueDelivery(DiscordDelivery delivery);

        void BeginDeliveryAttempt(
            string deliveryId,
            int attemptNumber,
            DateTimeOffset startedAtUtc);

        DiscordDeliveryWorkItem? TryClaimNextDeliveryAttempt(DateTimeOffset claimedAtUtc);

        void CompleteDeliveryAttempt(
            string deliveryId,
            int attemptNumber,
            DiscordDeliveryStatus finalStatus,
            DateTimeOffset completedAtUtc,
            string? errorCode,
            DateTimeOffset? nextAttemptAtUtc);

        int RecoverSendingAsResultUnknown(DateTimeOffset recoveredAtUtc);

        DiscordDelivery? FindDelivery(string deliveryId);

        IReadOnlyList<DiscordDeliveryAttempt> ListDeliveryAttempts(string deliveryId);

        DiscordDelivery ScheduleManualRetry(
            string deliveryId,
            string contentText,
            DateTimeOffset scheduledAtUtc);

        bool CancelDelivery(string deliveryId, DateTimeOffset cancelledAtUtc);

        void SaveBindingCode(DiscordBindingCode code);

        DiscordBinding? TryConsumeBindingCode(
            byte[] codeHash,
            string discordSubject,
            DateTimeOffset consumedAtUtc);

        DiscordBinding? FindBinding(string discordSubject);

        bool TryRegisterInteraction(DiscordInteraction interaction);

        void CompleteInteraction(
            string interactionId,
            string status,
            DateTimeOffset completedAtUtc);

        void SaveInteractionWithToken(
            DiscordInteraction interaction,
            string tokenValue);

        DiscordInteractionToken? GetInteractionToken(
            string interactionId,
            DateTimeOffset observedAtUtc);

        int ClearExpiredInteractionTokens(DateTimeOffset observedAtUtc);

        bool TryRegisterBridgeMessage(
            string bridgeMessageId,
            string source,
            string sourceMessageId,
            DateTimeOffset expiresAtUtc);
    }

    public interface IDiscordInteractionPersistenceStore
    {
        bool TrySaveInteractionWithToken(
            DiscordInteraction interaction,
            string tokenValue);

        DiscordInteraction? TryClaimNextInteraction(DateTimeOffset claimedAtUtc);

        int RecoverRunningInteractions(DateTimeOffset recoveredAtUtc);
    }

    public interface IDiscordIntegrationAdministrationStore
    {
        IReadOnlyList<DiscordDelivery> ListDeliveries(int take);

        IReadOnlyList<DiscordBinding> ListBindings();

        bool DisableBinding(string discordSubject, DateTimeOffset disabledAtUtc);
    }

    public sealed class DiscordIntegrationVersionConflictException : InvalidOperationException
    {
        public DiscordIntegrationVersionConflictException()
            : base("discord_settings_version_conflict")
        {
        }
    }
}
