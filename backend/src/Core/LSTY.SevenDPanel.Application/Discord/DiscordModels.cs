using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.Discord
{
    public static class DiscordSecretKeys
    {
        public const string BotToken = "botToken";
        public const string ProxyCredentials = "proxyCredentials";

        public static string WebhookUrl(string targetKey)
        {
            if (string.IsNullOrWhiteSpace(targetKey))
                throw new ArgumentException("A target key is required.", nameof(targetKey));
            return "webhook:" + targetKey.Trim();
        }
    }

    public sealed class DiscordConfigurationUpdate
    {
        public DiscordConfigurationUpdate(
            long expectedVersion,
            bool isEnabled,
            DiscordIntegrationMode mode,
            string? applicationId,
            string? guildId,
            string? publicChannelId,
            bool bridgeGameToDiscord,
            bool bridgeDiscordToGame,
            bool proxyEnabled,
            string? proxyUri,
            IReadOnlyList<DiscordTarget> targets)
        {
            ExpectedVersion = expectedVersion;
            IsEnabled = isEnabled;
            Mode = mode;
            ApplicationId = applicationId;
            GuildId = guildId;
            PublicChannelId = publicChannelId;
            BridgeGameToDiscord = bridgeGameToDiscord;
            BridgeDiscordToGame = bridgeDiscordToGame;
            ProxyEnabled = proxyEnabled;
            ProxyUri = proxyUri;
            Targets = targets ?? throw new ArgumentNullException(nameof(targets));
        }

        public long ExpectedVersion { get; }
        public bool IsEnabled { get; }
        public DiscordIntegrationMode Mode { get; }
        public string? ApplicationId { get; }
        public string? GuildId { get; }
        public string? PublicChannelId { get; }
        public bool BridgeGameToDiscord { get; }
        public bool BridgeDiscordToGame { get; }
        public bool ProxyEnabled { get; }
        public string? ProxyUri { get; }
        public IReadOnlyList<DiscordTarget> Targets { get; }

        public override string ToString() =>
            $"DiscordConfigurationUpdate {{ ExpectedVersion = {ExpectedVersion}, IsEnabled = {IsEnabled}, Mode = {Mode}, ProxyEnabled = {ProxyEnabled}, ProxyUri = [REDACTED], TargetCount = {Targets.Count} }}";
    }

    public sealed record DiscordSecretSummary(
        string SecretKey,
        bool IsSet,
        string? Fingerprint,
        DateTimeOffset? UpdatedAtUtc);

    public sealed record DiscordProxySummary(
        bool IsEnabled,
        string? Endpoint,
        bool HasCredentials);

    public sealed record DiscordConfigurationSummary(
        long Version,
        bool IsEnabled,
        DiscordIntegrationMode Mode,
        string? ApplicationId,
        string? GuildId,
        string? PublicChannelId,
        bool BridgeGameToDiscord,
        bool BridgeDiscordToGame,
        DiscordProxySummary Proxy,
        IReadOnlyList<DiscordTarget> Targets,
        IReadOnlyList<DiscordSecretSummary> Secrets,
        DateTimeOffset? UpdatedAtUtc);

    public sealed record DiscordDeliverySummary(
        string DeliveryId,
        string BusinessKey,
        string TargetKey,
        DiscordDeliveryStatus Status,
        string ContentSummary,
        DateTimeOffset? NextAttemptAtUtc,
        int RetryCount,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? CompletedAtUtc)
    {
        public static DiscordDeliverySummary FromDelivery(DiscordDelivery delivery)
        {
            if (delivery == null) throw new ArgumentNullException(nameof(delivery));
            return new DiscordDeliverySummary(
                delivery.DeliveryId,
                delivery.BusinessKey,
                delivery.TargetKey,
                delivery.Status,
                delivery.ContentSummary,
                delivery.NextAttemptAtUtc,
                delivery.RetryCount,
                delivery.CreatedAtUtc,
                delivery.CompletedAtUtc);
        }
    }

    public sealed record DiscordDeliveryWorkItem(
        DiscordDelivery Delivery,
        int AttemptNumber);

    public enum DiscordApiDeliveryDisposition
    {
        Succeeded,
        Retryable,
        Failed,
        ResultUnknown
    }

    public sealed record DiscordApiResult(
        DiscordApiDeliveryDisposition Disposition,
        string? ErrorCode,
        TimeSpan? RetryAfter)
    {
        public static DiscordApiResult Succeeded() =>
            new(DiscordApiDeliveryDisposition.Succeeded, null, null);

        public static DiscordApiResult Retryable(string errorCode, TimeSpan? retryAfter) =>
            new(DiscordApiDeliveryDisposition.Retryable, RequireErrorCode(errorCode), retryAfter);

        public static DiscordApiResult Failed(string errorCode) =>
            new(DiscordApiDeliveryDisposition.Failed, RequireErrorCode(errorCode), null);

        public static DiscordApiResult ResultUnknown() =>
            new(DiscordApiDeliveryDisposition.ResultUnknown, "discord_delivery_result_unknown", null);

        private static string RequireErrorCode(string errorCode) =>
            string.IsNullOrWhiteSpace(errorCode)
                ? throw new ArgumentException("An error code is required.", nameof(errorCode))
                : errorCode;
    }

    public sealed class DiscordProxyConfiguration
    {
        public DiscordProxyConfiguration(Uri endpoint, string? credentials)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (!endpoint.IsAbsoluteUri ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("discord_proxy_endpoint_invalid", nameof(endpoint));
            Endpoint = new UriBuilder(endpoint)
            {
                UserName = string.Empty,
                Password = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            }.Uri;
            Credentials = credentials;
        }

        public Uri Endpoint { get; }
        public string? Credentials { get; }

        public override string ToString() =>
            $"DiscordProxyConfiguration {{ Endpoint = {Endpoint.GetLeftPart(UriPartial.Authority)}, Credentials = [REDACTED] }}";
    }

    public sealed class DiscordApiRequest
    {
        private DiscordApiRequest(
            DiscordIntegrationMode mode,
            string? channelId,
            string credential,
            string content,
            string? nonce,
            DiscordProxyConfiguration? proxy)
        {
            Mode = mode;
            ChannelId = channelId;
            Credential = credential;
            Content = content;
            Nonce = nonce;
            Proxy = proxy;
        }

        public DiscordIntegrationMode Mode { get; }
        public string? ChannelId { get; }
        public string Credential { get; }
        public string Content { get; }
        public string? Nonce { get; }
        public DiscordProxyConfiguration? Proxy { get; }

        public static DiscordApiRequest Webhook(
            string webhookUrl,
            string content,
            DiscordProxyConfiguration? proxy) =>
            new(DiscordIntegrationMode.Webhook, null, webhookUrl, content, null, proxy);

        public static DiscordApiRequest Bot(
            string channelId,
            string token,
            string content,
            string nonce,
            DiscordProxyConfiguration? proxy) =>
            new(DiscordIntegrationMode.Bot, channelId, token, content, nonce, proxy);

        public override string ToString() =>
            $"DiscordApiRequest {{ Mode = {Mode}, ChannelId = {ChannelId}, Credential = [REDACTED], ContentLength = {Content?.Length ?? 0}, Nonce = {Nonce}, Proxy = {Proxy} }}";
    }

    public interface IDiscordApiClient
    {
        Task<DiscordApiResult> SendAsync(
            DiscordApiRequest request,
            CancellationToken cancellationToken);
    }

    public static class DiscordDeliveryPolicy
    {
        public const int MaximumAutomaticRetries = 5;
        public static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(5);

        public static TimeSpan ExponentialDelay(int automaticRetryNumber)
        {
            if (automaticRetryNumber <= 0)
                throw new ArgumentOutOfRangeException(nameof(automaticRetryNumber));
            if (automaticRetryNumber >= 9) return MaximumBackoff;
            var seconds = 2L << (automaticRetryNumber - 1);
            return TimeSpan.FromSeconds(Math.Min(seconds, (long)MaximumBackoff.TotalSeconds));
        }

        public static TimeSpan RetryDelay(int automaticRetryNumber, TimeSpan? officialRetryAfter)
        {
            if (officialRetryAfter.HasValue && officialRetryAfter.Value > TimeSpan.Zero)
                return officialRetryAfter.Value;
            return ExponentialDelay(automaticRetryNumber);
        }
    }

    public static class DiscordInteractionTypes
    {
        public const int ApplicationCommand = 2;
    }

    public static class DiscordSlashCommandNames
    {
        public const string Bind = "bind";
        public const string Status = "status";
        public const string Players = "players";

        public static bool IsAllowed(string commandName) =>
            string.Equals(commandName, Bind, StringComparison.Ordinal) ||
            string.Equals(commandName, Status, StringComparison.Ordinal) ||
            string.Equals(commandName, Players, StringComparison.Ordinal);
    }

    public static class DiscordInteractionStatuses
    {
        public const string Pending = "Pending";
        public const string Running = "Running";
        public const string Succeeded = "Succeeded";
        public const string Rejected = "Rejected";
        public const string Failed = "Failed";
        public const string Expired = "Expired";
        public const string ResultUnknown = "ResultUnknown";

        public static bool IsTerminal(string status) =>
            string.Equals(status, Succeeded, StringComparison.Ordinal) ||
            string.Equals(status, Rejected, StringComparison.Ordinal) ||
            string.Equals(status, Failed, StringComparison.Ordinal) ||
            string.Equals(status, Expired, StringComparison.Ordinal) ||
            string.Equals(status, ResultUnknown, StringComparison.Ordinal);
    }

    public static class DiscordBindingCodeHash
    {
        public static byte[] Compute(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("A binding code is required.", nameof(code));
            using var algorithm = SHA256.Create();
            return algorithm.ComputeHash(Encoding.UTF8.GetBytes(code.Trim()));
        }
    }

    public enum DiscordInboundDisposition
    {
        IgnoredBot,
        IgnoredDisabled,
        IgnoredRoute,
        IgnoredEvent,
        IgnoredEcho,
        Duplicate,
        RejectedBinding,
        RejectedCommand,
        RejectedContent,
        Bound,
        Dispatched,
        Forwarded,
        Enqueued,
        Accepted,
        Failed,
        ResultUnknown,
        NotRunning
    }

    public sealed record DiscordInboundResult(
        DiscordInboundDisposition Disposition,
        string ResultCode)
    {
        public static DiscordInboundResult From(
            DiscordInboundDisposition disposition,
            string resultCode) =>
            new(disposition, string.IsNullOrWhiteSpace(resultCode)
                ? throw new ArgumentException("A result code is required.", nameof(resultCode))
                : resultCode);
    }

    public sealed class DiscordMessageCreateEnvelope
    {
        public DiscordMessageCreateEnvelope(
            string messageId,
            string guildId,
            string channelId,
            string authorDiscordSubject,
            bool authorIsBot,
            bool isWebhook,
            string content)
        {
            MessageId = RequireText(messageId, nameof(messageId));
            GuildId = RequireText(guildId, nameof(guildId));
            ChannelId = RequireText(channelId, nameof(channelId));
            AuthorDiscordSubject = RequireText(authorDiscordSubject, nameof(authorDiscordSubject));
            if (content == null || content.Length < 1 || content.Length > 2000)
                throw new ArgumentException("discord_message_content_invalid", nameof(content));
            AuthorIsBot = authorIsBot;
            IsWebhook = isWebhook;
            Content = content;
        }

        public string MessageId { get; }
        public string GuildId { get; }
        public string ChannelId { get; }
        public string AuthorDiscordSubject { get; }
        public bool AuthorIsBot { get; }
        public bool IsWebhook { get; }
        public string Content { get; }

        public override string ToString() =>
            $"DiscordMessageCreateEnvelope {{ MessageId = {MessageId}, GuildId = {GuildId}, ChannelId = {ChannelId}, AuthorDiscordSubject = {AuthorDiscordSubject}, AuthorIsBot = {AuthorIsBot}, IsWebhook = {IsWebhook}, ContentLength = {Content.Length} }}";

        private static string RequireText(string value, string parameterName) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("A value is required.", parameterName)
                : value.Trim();
    }

    public sealed class DiscordInteractionEnvelope
    {
        public DiscordInteractionEnvelope(
            string interactionId,
            int interactionType,
            string guildId,
            string channelId,
            string discordSubject,
            bool authorIsBot,
            string commandName,
            string? bindingCode)
        {
            InteractionId = RequireText(interactionId, nameof(interactionId));
            InteractionType = interactionType;
            GuildId = RequireText(guildId, nameof(guildId));
            ChannelId = RequireText(channelId, nameof(channelId));
            DiscordSubject = RequireText(discordSubject, nameof(discordSubject));
            AuthorIsBot = authorIsBot;
            CommandName = RequireText(commandName, nameof(commandName)).ToLowerInvariant();
            BindingCode = string.IsNullOrWhiteSpace(bindingCode) ? null : bindingCode!.Trim();
        }

        public string InteractionId { get; }
        public int InteractionType { get; }
        public string GuildId { get; }
        public string ChannelId { get; }
        public string DiscordSubject { get; }
        public bool AuthorIsBot { get; }
        public string CommandName { get; }
        public string? BindingCode { get; }

        public override string ToString() =>
            $"DiscordInteractionEnvelope {{ InteractionId = {InteractionId}, InteractionType = {InteractionType}, GuildId = {GuildId}, ChannelId = {ChannelId}, DiscordSubject = {DiscordSubject}, AuthorIsBot = {AuthorIsBot}, CommandName = {CommandName}, BindingCode = [REDACTED] }}";

        private static string RequireText(string value, string parameterName) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("A value is required.", parameterName)
                : value.Trim();
    }

    public abstract record DiscordInboundCommand(
        string InteractionId,
        string DiscordSubject,
        string CrossplatformId,
        string GuildId,
        string ChannelId);

    public sealed record DiscordServerStatusCommand(
        string InteractionId,
        string DiscordSubject,
        string CrossplatformId,
        string GuildId,
        string ChannelId)
        : DiscordInboundCommand(
            InteractionId,
            DiscordSubject,
            CrossplatformId,
            GuildId,
            ChannelId);

    public sealed record DiscordOnlinePlayersCommand(
        string InteractionId,
        string DiscordSubject,
        string CrossplatformId,
        string GuildId,
        string ChannelId)
        : DiscordInboundCommand(
            InteractionId,
            DiscordSubject,
            CrossplatformId,
            GuildId,
            ChannelId);

    public enum DiscordCommandDispatchStatus
    {
        Succeeded,
        Rejected,
        Failed,
        ResultUnknown
    }

    public sealed class DiscordCommandDispatchResult
    {
        private DiscordCommandDispatchResult(
            DiscordCommandDispatchStatus status,
            string? responseContent)
        {
            Status = status;
            ResponseContent = responseContent;
        }

        public DiscordCommandDispatchStatus Status { get; }
        public string? ResponseContent { get; }

        public static DiscordCommandDispatchResult Succeeded(string? responseContent = null) =>
            new DiscordCommandDispatchResult(
                DiscordCommandDispatchStatus.Succeeded,
                string.IsNullOrWhiteSpace(responseContent) ? null : responseContent.Trim());

        public static DiscordCommandDispatchResult Rejected() =>
            new DiscordCommandDispatchResult(DiscordCommandDispatchStatus.Rejected, null);

        public static DiscordCommandDispatchResult Failed() =>
            new DiscordCommandDispatchResult(DiscordCommandDispatchStatus.Failed, null);

        public static DiscordCommandDispatchResult ResultUnknown() =>
            new DiscordCommandDispatchResult(DiscordCommandDispatchStatus.ResultUnknown, null);

        public override string ToString() =>
            $"DiscordCommandDispatchResult {{ Status = {Status}, ResponseLength = {ResponseContent?.Length ?? 0} }}";
    }

    public interface IDiscordInboundCommandDispatcher
    {
        Task<DiscordCommandDispatchResult> DispatchAsync(
            DiscordServerStatusCommand command,
            CancellationToken cancellationToken);

        Task<DiscordCommandDispatchResult> DispatchAsync(
            DiscordOnlinePlayersCommand command,
            CancellationToken cancellationToken);
    }

    public sealed class DiscordIntegrationDisabledException : InvalidOperationException
    {
        public DiscordIntegrationDisabledException() : base("discord_integration_disabled") { }
    }

    public sealed class DiscordDeliveryValidationException : ArgumentException
    {
        public DiscordDeliveryValidationException() : base("discord_delivery_invalid") { }
    }
}
