using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Chat;

namespace LSTY.SevenDPanel.Application.Discord
{
    public sealed class HandleDiscordInteractionUseCase
    {
        private static readonly TimeSpan InteractionRetention = TimeSpan.FromMinutes(15);
        private readonly IDiscordIntegrationStore store;
        private readonly IDiscordInboundCommandDispatcher dispatcher;
        private readonly Func<DateTimeOffset> utcNow;

        public HandleDiscordInteractionUseCase(
            IDiscordIntegrationStore store,
            IDiscordInboundCommandDispatcher dispatcher,
            Func<DateTimeOffset> utcNow)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public async Task<DiscordInboundResult> ExecuteAsync(
            DiscordInteractionEnvelope interaction,
            CancellationToken cancellationToken)
        {
            if (interaction == null) throw new ArgumentNullException(nameof(interaction));
            if (interaction.AuthorIsBot)
                return Result(DiscordInboundDisposition.IgnoredBot, "discord_inbound_bot_ignored");
            if (interaction.InteractionType != DiscordInteractionTypes.ApplicationCommand)
                return Result(DiscordInboundDisposition.IgnoredEvent, "discord_interaction_type_ignored");

            var settings = store.GetSettings();
            if (settings == null || !settings.IsEnabled || settings.Mode != DiscordIntegrationMode.Bot)
                return Result(DiscordInboundDisposition.IgnoredDisabled, "discord_inbound_disabled");
            if (!DiscordInboundRoutePolicy.IsAllowed(
                    settings,
                    store.ListTargets(),
                    interaction.GuildId,
                    interaction.ChannelId))
                return Result(DiscordInboundDisposition.IgnoredRoute, "discord_inbound_route_ignored");
            if (!DiscordSlashCommandNames.IsAllowed(interaction.CommandName))
                return Result(DiscordInboundDisposition.RejectedCommand, "discord_command_not_allowed");

            var setting = store.ListCommandSettings().SingleOrDefault(command =>
                string.Equals(command.CommandKey, interaction.CommandName, StringComparison.Ordinal));
            if (setting == null || !setting.IsEnabled || !setting.RemoteAllowed)
                return Result(DiscordInboundDisposition.RejectedCommand, "discord_command_disabled");

            var now = EnqueueDiscordDeliveryUseCase.RequireUtc(utcNow());
            if (!store.TryRegisterInteraction(new DiscordInteraction(
                    interaction.InteractionId,
                    interaction.CommandName,
                    DiscordInteractionStatuses.Pending,
                    now.Add(InteractionRetention),
                    null)))
                return Result(DiscordInboundDisposition.Duplicate, "discord_interaction_duplicate");

            if (string.Equals(
                    interaction.CommandName,
                    DiscordSlashCommandNames.Bind,
                    StringComparison.Ordinal))
                return Bind(interaction, now);

            var binding = store.FindBinding(interaction.DiscordSubject);
            if (binding == null || !binding.IsActive)
            {
                Complete(interaction.InteractionId, DiscordInteractionStatuses.Rejected, now);
                return Result(DiscordInboundDisposition.RejectedBinding, "discord_binding_required");
            }

            try
            {
                DiscordCommandDispatchResult dispatchResult;
                if (string.Equals(
                        interaction.CommandName,
                        DiscordSlashCommandNames.Status,
                        StringComparison.Ordinal))
                {
                    dispatchResult = await dispatcher.DispatchAsync(
                        new DiscordServerStatusCommand(
                            interaction.InteractionId,
                            interaction.DiscordSubject,
                            binding.CrossplatformId,
                            interaction.GuildId,
                            interaction.ChannelId),
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    dispatchResult = await dispatcher.DispatchAsync(
                        new DiscordOnlinePlayersCommand(
                            interaction.InteractionId,
                            interaction.DiscordSubject,
                            binding.CrossplatformId,
                            interaction.GuildId,
                            interaction.ChannelId),
                        cancellationToken).ConfigureAwait(false);
                }

                if (dispatchResult == null)
                {
                    Complete(interaction.InteractionId, DiscordInteractionStatuses.Failed, now);
                    return Result(DiscordInboundDisposition.Failed, "discord_command_failed");
                }
                return CompleteDispatch(interaction.InteractionId, dispatchResult, now);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Complete(interaction.InteractionId, DiscordInteractionStatuses.ResultUnknown, now);
                throw;
            }
            catch
            {
                Complete(interaction.InteractionId, DiscordInteractionStatuses.ResultUnknown, now);
                return Result(
                    DiscordInboundDisposition.ResultUnknown,
                    "discord_command_result_unknown");
            }
        }

        private DiscordInboundResult Bind(
            DiscordInteractionEnvelope interaction,
            DateTimeOffset now)
        {
            if (interaction.BindingCode == null)
            {
                Complete(interaction.InteractionId, DiscordInteractionStatuses.Rejected, now);
                return Result(DiscordInboundDisposition.RejectedBinding, "discord_binding_code_invalid");
            }

            var binding = store.TryConsumeBindingCode(
                DiscordBindingCodeHash.Compute(interaction.BindingCode),
                interaction.DiscordSubject,
                now);
            if (binding == null)
            {
                Complete(interaction.InteractionId, DiscordInteractionStatuses.Rejected, now);
                return Result(DiscordInboundDisposition.RejectedBinding, "discord_binding_code_invalid");
            }

            Complete(interaction.InteractionId, DiscordInteractionStatuses.Succeeded, now);
            return Result(DiscordInboundDisposition.Bound, "discord_binding_succeeded");
        }

        private DiscordInboundResult CompleteDispatch(
            string interactionId,
            DiscordCommandDispatchResult dispatchResult,
            DateTimeOffset completedAtUtc)
        {
            switch (dispatchResult.Status)
            {
                case DiscordCommandDispatchStatus.Succeeded:
                    Complete(interactionId, DiscordInteractionStatuses.Succeeded, completedAtUtc);
                    return Result(DiscordInboundDisposition.Dispatched, "discord_command_succeeded");
                case DiscordCommandDispatchStatus.Rejected:
                    Complete(interactionId, DiscordInteractionStatuses.Rejected, completedAtUtc);
                    return Result(DiscordInboundDisposition.RejectedCommand, "discord_command_rejected");
                case DiscordCommandDispatchStatus.Failed:
                    Complete(interactionId, DiscordInteractionStatuses.Failed, completedAtUtc);
                    return Result(DiscordInboundDisposition.Failed, "discord_command_failed");
                case DiscordCommandDispatchStatus.ResultUnknown:
                    Complete(interactionId, DiscordInteractionStatuses.ResultUnknown, completedAtUtc);
                    return Result(
                        DiscordInboundDisposition.ResultUnknown,
                        "discord_command_result_unknown");
                default:
                    Complete(interactionId, DiscordInteractionStatuses.Failed, completedAtUtc);
                    return Result(DiscordInboundDisposition.Failed, "discord_command_failed");
            }
        }

        private void Complete(
            string interactionId,
            string status,
            DateTimeOffset completedAtUtc) =>
            store.CompleteInteraction(interactionId, status, completedAtUtc);

        private static DiscordInboundResult Result(
            DiscordInboundDisposition disposition,
            string code) => DiscordInboundResult.From(disposition, code);
    }

    public sealed class AcceptDiscordInteractionUseCase
    {
        private static readonly TimeSpan InteractionRetention = TimeSpan.FromMinutes(15);
        private readonly IDiscordIntegrationStore store;
        private readonly IDiscordInteractionPersistenceStore interactionStore;
        private readonly Func<DateTimeOffset> utcNow;

        public AcceptDiscordInteractionUseCase(
            IDiscordIntegrationStore store,
            IDiscordInteractionPersistenceStore interactionStore,
            Func<DateTimeOffset> utcNow)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.interactionStore = interactionStore ??
                throw new ArgumentNullException(nameof(interactionStore));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public DiscordInboundResult Execute(
            DiscordInteractionEnvelope interaction,
            string interactionToken)
        {
            if (interaction == null) throw new ArgumentNullException(nameof(interaction));
            if (string.IsNullOrWhiteSpace(interactionToken))
                throw new ArgumentException("A token is required.", nameof(interactionToken));
            if (interaction.AuthorIsBot)
                return Result(DiscordInboundDisposition.IgnoredBot, "discord_inbound_bot_ignored");
            if (interaction.InteractionType != DiscordInteractionTypes.ApplicationCommand)
                return Result(DiscordInboundDisposition.IgnoredEvent, "discord_interaction_type_ignored");

            var settings = store.GetSettings();
            if (settings == null || !settings.IsEnabled || settings.Mode != DiscordIntegrationMode.Bot)
                return Result(DiscordInboundDisposition.IgnoredDisabled, "discord_inbound_disabled");
            if (!DiscordInboundRoutePolicy.IsAllowed(
                    settings,
                    store.ListTargets(),
                    interaction.GuildId,
                    interaction.ChannelId))
                return Result(DiscordInboundDisposition.IgnoredRoute, "discord_inbound_route_ignored");
            if (!DiscordSlashCommandNames.IsAllowed(interaction.CommandName))
                return Result(DiscordInboundDisposition.RejectedCommand, "discord_command_not_allowed");

            var setting = store.ListCommandSettings().SingleOrDefault(command =>
                string.Equals(command.CommandKey, interaction.CommandName, StringComparison.Ordinal));
            if (setting == null || !setting.IsEnabled || !setting.RemoteAllowed)
                return Result(DiscordInboundDisposition.RejectedCommand, "discord_command_disabled");

            var now = EnqueueDiscordDeliveryUseCase.RequireUtc(utcNow());
            var accepted = interactionStore.TrySaveInteractionWithToken(
                new DiscordInteraction(
                    interaction.InteractionId,
                    interaction.CommandName,
                    DiscordInteractionStatuses.Pending,
                    now.Add(InteractionRetention),
                    null,
                    interaction.GuildId,
                    interaction.ChannelId,
                    interaction.DiscordSubject,
                    interaction.BindingCode == null
                        ? null
                        : DiscordBindingCodeHash.Compute(interaction.BindingCode)),
                interactionToken.Trim());
            return accepted
                ? Result(DiscordInboundDisposition.Accepted, "discord_interaction_accepted")
                : Result(DiscordInboundDisposition.Duplicate, "discord_interaction_duplicate");
        }

        private static DiscordInboundResult Result(
            DiscordInboundDisposition disposition,
            string code) => DiscordInboundResult.From(disposition, code);
    }

    public sealed class ProcessDiscordInteractionUseCase
    {
        private readonly IDiscordIntegrationStore store;
        private readonly IDiscordInteractionPersistenceStore interactionStore;
        private readonly IDiscordInboundCommandDispatcher dispatcher;
        private readonly IDiscordInteractionResponseSender? responseSender;
        private readonly Func<DateTimeOffset> utcNow;

        public ProcessDiscordInteractionUseCase(
            IDiscordIntegrationStore store,
            IDiscordInteractionPersistenceStore interactionStore,
            IDiscordInboundCommandDispatcher dispatcher,
            Func<DateTimeOffset> utcNow,
            IDiscordInteractionResponseSender? responseSender = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.interactionStore = interactionStore ??
                throw new ArgumentNullException(nameof(interactionStore));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.responseSender = responseSender;
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public int RecoverRunningInteractions() =>
            interactionStore.RecoverRunningInteractions(EnqueueDiscordDeliveryUseCase.RequireUtc(utcNow()));

        public async Task<DiscordInboundResult?> ExecuteNextAsync(CancellationToken cancellationToken)
        {
            var now = EnqueueDiscordDeliveryUseCase.RequireUtc(utcNow());
            var interaction = interactionStore.TryClaimNextInteraction(now);
            if (interaction == null) return null;

            return await ExecuteClaimedAsync(interaction, cancellationToken).ConfigureAwait(false);
        }

        public async Task<DiscordInboundResult> ExecuteClaimedAsync(
            DiscordInteraction interaction,
            CancellationToken cancellationToken)
        {
            if (interaction == null) throw new ArgumentNullException(nameof(interaction));
            var now = EnqueueDiscordDeliveryUseCase.RequireUtc(utcNow());

            try
            {
                if (!DiscordSlashCommandNames.IsAllowed(interaction.CommandKey))
                    return Complete(
                        interaction.InteractionId,
                        DiscordInteractionStatuses.Rejected,
                        now,
                        DiscordInboundDisposition.RejectedCommand,
                        "discord_command_not_allowed");
                if (string.Equals(
                        interaction.CommandKey,
                        DiscordSlashCommandNames.Bind,
                        StringComparison.Ordinal))
                    return Bind(interaction, now);

                var binding = store.FindBinding(interaction.DiscordSubject!);
                if (binding == null || !binding.IsActive)
                    return Complete(
                        interaction.InteractionId,
                        DiscordInteractionStatuses.Rejected,
                        now,
                        DiscordInboundDisposition.RejectedBinding,
                        "discord_binding_required");

                var dispatchResult = string.Equals(
                        interaction.CommandKey,
                        DiscordSlashCommandNames.Status,
                        StringComparison.Ordinal)
                    ? await dispatcher.DispatchAsync(
                        new DiscordServerStatusCommand(
                            interaction.InteractionId,
                            interaction.DiscordSubject!,
                            binding.CrossplatformId,
                            interaction.GuildId!,
                            interaction.ChannelId!),
                        cancellationToken).ConfigureAwait(false)
                    : await dispatcher.DispatchAsync(
                        new DiscordOnlinePlayersCommand(
                            interaction.InteractionId,
                            interaction.DiscordSubject!,
                            binding.CrossplatformId,
                            interaction.GuildId!,
                            interaction.ChannelId!),
                        cancellationToken).ConfigureAwait(false);
                if (dispatchResult == null)
                    return Complete(
                        interaction.InteractionId,
                        DiscordInteractionStatuses.Failed,
                        now,
                        DiscordInboundDisposition.Failed,
                        "discord_command_failed");
                return await CompleteDispatchAsync(interaction, dispatchResult, now, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                store.CompleteInteraction(
                    interaction.InteractionId,
                    DiscordInteractionStatuses.ResultUnknown,
                    now);
                throw;
            }
            catch
            {
                return Complete(
                    interaction.InteractionId,
                    DiscordInteractionStatuses.ResultUnknown,
                    now,
                    DiscordInboundDisposition.ResultUnknown,
                    "discord_command_result_unknown");
            }
        }

        private DiscordInboundResult Bind(DiscordInteraction interaction, DateTimeOffset now)
        {
            if (interaction.BindingCodeHash == null || interaction.BindingCodeHash.Length == 0)
                return Complete(
                    interaction.InteractionId,
                    DiscordInteractionStatuses.Rejected,
                    now,
                    DiscordInboundDisposition.RejectedBinding,
                    "discord_binding_code_invalid");

            var binding = store.TryConsumeBindingCode(
                interaction.BindingCodeHash,
                interaction.DiscordSubject!,
                now);
            return binding == null
                ? Complete(
                    interaction.InteractionId,
                    DiscordInteractionStatuses.Rejected,
                    now,
                    DiscordInboundDisposition.RejectedBinding,
                    "discord_binding_code_invalid")
                : Complete(
                    interaction.InteractionId,
                    DiscordInteractionStatuses.Succeeded,
                    now,
                    DiscordInboundDisposition.Bound,
                    "discord_binding_succeeded");
        }

        private DiscordInboundResult CompleteDispatch(
            string interactionId,
            DiscordCommandDispatchResult dispatchResult,
            DateTimeOffset completedAtUtc)
        {
            switch (dispatchResult.Status)
            {
                case DiscordCommandDispatchStatus.Succeeded:
                    return Complete(
                        interactionId,
                        DiscordInteractionStatuses.Succeeded,
                        completedAtUtc,
                        DiscordInboundDisposition.Dispatched,
                        "discord_command_succeeded");
                case DiscordCommandDispatchStatus.Rejected:
                    return Complete(
                        interactionId,
                        DiscordInteractionStatuses.Rejected,
                        completedAtUtc,
                        DiscordInboundDisposition.RejectedCommand,
                        "discord_command_rejected");
                case DiscordCommandDispatchStatus.Failed:
                    return Complete(
                        interactionId,
                        DiscordInteractionStatuses.Failed,
                        completedAtUtc,
                        DiscordInboundDisposition.Failed,
                        "discord_command_failed");
                default:
                    return Complete(
                        interactionId,
                        DiscordInteractionStatuses.ResultUnknown,
                        completedAtUtc,
                        DiscordInboundDisposition.ResultUnknown,
                        "discord_command_result_unknown");
            }
        }

        private async Task<DiscordInboundResult> CompleteDispatchAsync(
            DiscordInteraction interaction,
            DiscordCommandDispatchResult dispatchResult,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken)
        {
            if (dispatchResult.Status != DiscordCommandDispatchStatus.Succeeded ||
                string.IsNullOrWhiteSpace(dispatchResult.ResponseContent) ||
                responseSender == null)
                return CompleteDispatch(interaction.InteractionId, dispatchResult, completedAtUtc);

            var settings = store.GetSettings();
            var token = store.GetInteractionToken(interaction.InteractionId, completedAtUtc);
            if (settings == null || !settings.IsEnabled ||
                string.IsNullOrWhiteSpace(settings.ApplicationId) || token == null)
                return Complete(
                    interaction.InteractionId,
                    DiscordInteractionStatuses.ResultUnknown,
                    completedAtUtc,
                    DiscordInboundDisposition.ResultUnknown,
                    "discord_interaction_response_unavailable");

            var response = new DiscordInteractionResponse(
                settings.ApplicationId!,
                token.TokenValue,
                dispatchResult.ResponseContent!,
                CreateProxy(settings));
            var outcome = await responseSender.SendEphemeralAsync(response, cancellationToken)
                .ConfigureAwait(false);
            return outcome == DiscordInteractionResponseDisposition.Succeeded
                ? CompleteDispatch(interaction.InteractionId, dispatchResult, completedAtUtc)
                : Complete(
                    interaction.InteractionId,
                    DiscordInteractionStatuses.ResultUnknown,
                    completedAtUtc,
                    DiscordInboundDisposition.ResultUnknown,
                    "discord_interaction_response_" + outcome.ToString().ToLowerInvariant());
        }

        private DiscordProxyConfiguration? CreateProxy(DiscordIntegrationSettings settings)
        {
            if (!settings.ProxyEnabled ||
                !Uri.TryCreate(settings.ProxyUri, UriKind.Absolute, out var endpoint))
                return null;
            return new DiscordProxyConfiguration(
                endpoint,
                store.GetSecret(DiscordSecretKeys.ProxyCredentials)?.SecretValue);
        }

        private DiscordInboundResult Complete(
            string interactionId,
            string status,
            DateTimeOffset completedAtUtc,
            DiscordInboundDisposition disposition,
            string code)
        {
            store.CompleteInteraction(interactionId, status, completedAtUtc);
            return DiscordInboundResult.From(disposition, code);
        }
    }

    public sealed class BridgeDiscordMessageToGameUseCase
    {
        private static readonly TimeSpan BridgeRetention = TimeSpan.FromDays(7);
        private readonly IDiscordIntegrationStore store;
        private readonly IChatMessageSender sender;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Func<string> createBridgeMessageId;

        public BridgeDiscordMessageToGameUseCase(
            IDiscordIntegrationStore store,
            IChatMessageSender sender,
            Func<DateTimeOffset> utcNow,
            Func<string> createBridgeMessageId)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.createBridgeMessageId = createBridgeMessageId ??
                throw new ArgumentNullException(nameof(createBridgeMessageId));
        }

        public async Task<DiscordInboundResult> ExecuteAsync(
            DiscordMessageCreateEnvelope message,
            CancellationToken cancellationToken)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.AuthorIsBot || message.IsWebhook)
                return Result(DiscordInboundDisposition.IgnoredBot, "discord_inbound_bot_ignored");

            var settings = store.GetSettings();
            if (settings == null || !settings.IsEnabled ||
                settings.Mode != DiscordIntegrationMode.Bot ||
                !settings.BridgeDiscordToGame)
                return Result(DiscordInboundDisposition.IgnoredDisabled, "discord_bridge_disabled");
            if (!DiscordInboundRoutePolicy.IsAllowed(
                    settings,
                    store.ListTargets(),
                    message.GuildId,
                    message.ChannelId))
                return Result(DiscordInboundDisposition.IgnoredRoute, "discord_inbound_route_ignored");

            var binding = store.FindBinding(message.AuthorDiscordSubject);
            if (binding == null || !binding.IsActive)
                return Result(DiscordInboundDisposition.RejectedBinding, "discord_binding_required");

            var now = EnqueueDiscordDeliveryUseCase.RequireUtc(utcNow());
            var bridgeMessageId = createBridgeMessageId();
            if (string.IsNullOrWhiteSpace(bridgeMessageId))
                return Result(DiscordInboundDisposition.Failed, "discord_bridge_id_invalid");
            if (!store.TryRegisterBridgeMessage(
                    bridgeMessageId.Trim(),
                    "Discord",
                    message.MessageId,
                    now.Add(BridgeRetention)))
                return Result(DiscordInboundDisposition.Duplicate, "discord_bridge_duplicate");

            try
            {
                var sendResult = await sender.SendGlobalAsync(
                    "[Discord] " + message.Content,
                    cancellationToken).ConfigureAwait(false);
                return sendResult.Status == ChatSendStatus.Accepted
                    ? Result(DiscordInboundDisposition.Forwarded, "discord_bridge_forwarded")
                    : Result(DiscordInboundDisposition.Failed, "discord_bridge_game_rejected");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return Result(
                    DiscordInboundDisposition.ResultUnknown,
                    "discord_bridge_result_unknown");
            }
        }

        private static DiscordInboundResult Result(
            DiscordInboundDisposition disposition,
            string code) => DiscordInboundResult.From(disposition, code);
    }

    public sealed class BridgeGameChatToDiscordUseCase
    {
        private static readonly TimeSpan BridgeRetention = TimeSpan.FromDays(7);
        private readonly IDiscordIntegrationStore store;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Func<string> createBridgeMessageId;
        private readonly EnqueueDiscordDeliveryUseCase enqueue;
        private readonly string targetKey;

        public BridgeGameChatToDiscordUseCase(
            IDiscordIntegrationStore store,
            Func<DateTimeOffset> utcNow,
            Func<string> createBridgeMessageId,
            Func<string> createDeliveryId,
            string targetKey)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.createBridgeMessageId = createBridgeMessageId ??
                throw new ArgumentNullException(nameof(createBridgeMessageId));
            if (string.IsNullOrWhiteSpace(targetKey))
                throw new ArgumentException("A target key is required.", nameof(targetKey));
            this.targetKey = targetKey.Trim();
            enqueue = new EnqueueDiscordDeliveryUseCase(
                store,
                utcNow,
                createDeliveryId ?? throw new ArgumentNullException(nameof(createDeliveryId)));
        }

        public DiscordInboundResult Execute(ChatMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.SourceKind != ChatSourceKind.Player || message.Channel != ChatChannel.Global)
                return Result(DiscordInboundDisposition.IgnoredEcho, "discord_bridge_echo_ignored");

            var settings = store.GetSettings();
            if (settings == null || !settings.IsEnabled || !settings.BridgeGameToDiscord)
                return Result(DiscordInboundDisposition.IgnoredDisabled, "discord_bridge_disabled");
            var target = store.FindTarget(targetKey);
            if (target == null || !target.IsEnabled)
                return Result(DiscordInboundDisposition.IgnoredRoute, "discord_bridge_target_disabled");

            var sourceMessageId = message.Sequence.ToString(CultureInfo.InvariantCulture);
            var content = "[Game] " + message.SenderName + ": " + message.Message;
            if (content.Length < 1 || content.Length > 2000)
                return Result(DiscordInboundDisposition.RejectedContent, "discord_message_content_invalid");

            var now = EnqueueDiscordDeliveryUseCase.RequireUtc(utcNow());
            var bridgeMessageId = createBridgeMessageId();
            if (string.IsNullOrWhiteSpace(bridgeMessageId))
                return Result(DiscordInboundDisposition.Failed, "discord_bridge_id_invalid");
            if (!store.TryRegisterBridgeMessage(
                    bridgeMessageId.Trim(),
                    "Game",
                    sourceMessageId,
                    now.Add(BridgeRetention)))
                return Result(DiscordInboundDisposition.Duplicate, "discord_bridge_duplicate");

            enqueue.Execute(
                "discord-bridge:game:" + sourceMessageId,
                targetKey,
                content);
            return Result(DiscordInboundDisposition.Enqueued, "discord_bridge_enqueued");
        }

        private static DiscordInboundResult Result(
            DiscordInboundDisposition disposition,
            string code) => DiscordInboundResult.From(disposition, code);
    }

    internal static class DiscordInboundRoutePolicy
    {
        public static bool IsAllowed(
            DiscordIntegrationSettings settings,
            System.Collections.Generic.IReadOnlyList<DiscordTarget> targets,
            string guildId,
            string channelId)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (!string.Equals(settings.GuildId, guildId, StringComparison.Ordinal)) return false;
            if (string.Equals(settings.PublicChannelId, channelId, StringComparison.Ordinal)) return true;
            return targets.Any(target =>
                target.IsEnabled &&
                string.Equals(target.DeliveryMode, DiscordIntegrationMode.Bot.ToString(), StringComparison.Ordinal) &&
                string.Equals(target.ChannelId, channelId, StringComparison.Ordinal));
        }
    }
}
