using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Announcements;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Discord;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Automations;
using LSTY.SevenDPanel.Domain.Economy;
using LSTY.SevenDPanel.Domain.Rewards;

namespace LSTY.SevenDPanel.Application.Automations
{
    public enum AutomationDispatchStatus
    {
        Succeeded,
        Failed,
        ResultUnknown,
        Unavailable
    }

    public sealed record AutomationDispatchResult(
        AutomationDispatchStatus Status,
        string? ErrorCode,
        bool ConsumerStarted,
        bool ConsumerIsIdempotent)
    {
        public static AutomationDispatchResult Succeeded(bool consumerIsIdempotent) =>
            new(AutomationDispatchStatus.Succeeded, null, true, consumerIsIdempotent);

        public static AutomationDispatchResult Failed(
            string errorCode,
            bool consumerStarted,
            bool consumerIsIdempotent = false) =>
            new(
                AutomationDispatchStatus.Failed,
                RequireCode(errorCode),
                consumerStarted,
                consumerIsIdempotent);

        public static AutomationDispatchResult ResultUnknown(
            string errorCode,
            bool consumerIsIdempotent,
            bool consumerStarted) =>
            new(
                AutomationDispatchStatus.ResultUnknown,
                RequireCode(errorCode),
                consumerStarted,
                consumerIsIdempotent);

        public static AutomationDispatchResult Unavailable(
            string errorCode,
            bool consumerIsIdempotent) =>
            new(
                AutomationDispatchStatus.Unavailable,
                RequireCode(errorCode),
                false,
                consumerIsIdempotent);

        private static string RequireCode(string value) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("A dispatch error code is required.", nameof(value))
                : value;
    }

    public sealed record AutomationActionDispatchContext(
        string RuleId,
        string ExecutionId,
        int Ordinal,
        string ConsumerIdempotencyKey,
        string ResolvedTargetId,
        AutomationTriggerSnapshot Trigger,
        DateTimeOffset StartedAtUtc);

    public interface IAutomationActionDispatcher
    {
        bool IsConsumerIdempotent(AutomationAction action);

        Task<AutomationDispatchResult> DispatchAsync(
            AutomationAction action,
            AutomationActionDispatchContext context,
            CancellationToken cancellationToken);
    }

    public abstract record AutomationTypedAction;
    public sealed record AutomationBroadcastMessageAction(string Message) : AutomationTypedAction;
    public sealed record AutomationPrivateMessageAction(string Message) : AutomationTypedAction;
    public sealed record AutomationAnnouncementAction(string Message) : AutomationTypedAction;
    public sealed record AutomationGrantItemAction(string ResourceId, long Amount) : AutomationTypedAction;
    public sealed record AutomationGrantRewardPackageAction(string PackageId) : AutomationTypedAction;
    public sealed record AutomationAdjustEconomyAction(long Amount) : AutomationTypedAction;
    public sealed record AutomationKickPlayerAction(string Reason) : AutomationTypedAction;
    public sealed record AutomationMutePlayerAction(TimeSpan Duration, string Reason) : AutomationTypedAction;
    public sealed record AutomationRestrictedCommandAction(string RegisteredCommandKey) : AutomationTypedAction;
    public sealed record AutomationDiscordMessageAction(string Message) : AutomationTypedAction;

    public sealed class AutomationActionDispatcher : IAutomationActionDispatcher
    {
        private readonly SendGlobalChatMessageUseCase? broadcastMessages;
        private readonly SendPrivateChatMessageUseCase? privateMessages;
        private readonly AnnouncementService? announcements;
        private readonly GrantItemUseCase? grantItems;
        private readonly GrantRewardUseCase? grantRewards;
        private readonly AdjustPlayerBalanceUseCase? economy;
        private readonly KickPlayerUseCase? kickPlayers;
        private readonly ChatMuteUseCases? mutePlayers;
        private readonly ResetSkillsUseCase? resetSkills;
        private readonly IDiscordIntegrationStore? discordOutbox;
        private readonly IOnlinePlayerQuery? onlinePlayers;
        private readonly IGameResourceCatalog? resources;
        private readonly string? worldId;
        private readonly Func<DateTimeOffset> utcNow;

        public AutomationActionDispatcher(
            SendGlobalChatMessageUseCase? broadcastMessages = null,
            SendPrivateChatMessageUseCase? privateMessages = null,
            AnnouncementService? announcements = null,
            GrantItemUseCase? grantItems = null,
            GrantRewardUseCase? grantRewards = null,
            AdjustPlayerBalanceUseCase? economy = null,
            KickPlayerUseCase? kickPlayers = null,
            ChatMuteUseCases? mutePlayers = null,
            ResetSkillsUseCase? resetSkills = null,
            IDiscordIntegrationStore? discordOutbox = null,
            IOnlinePlayerQuery? onlinePlayers = null,
            IGameResourceCatalog? resources = null,
            string? worldId = null,
            Func<DateTimeOffset>? utcNow = null)
        {
            this.broadcastMessages = broadcastMessages;
            this.privateMessages = privateMessages;
            this.announcements = announcements;
            this.grantItems = grantItems;
            this.grantRewards = grantRewards;
            this.economy = economy;
            this.kickPlayers = kickPlayers;
            this.mutePlayers = mutePlayers;
            this.resetSkills = resetSkills;
            this.discordOutbox = discordOutbox;
            this.onlinePlayers = onlinePlayers;
            this.resources = resources;
            this.worldId = string.IsNullOrWhiteSpace(worldId) ? null : worldId;
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        public bool IsConsumerIdempotent(AutomationAction action)
        {
            var typed = ToTypedAction(action);
            return typed switch
            {
                AutomationBroadcastMessageAction => false,
                AutomationPrivateMessageAction => false,
                AutomationAnnouncementAction => false,
                AutomationGrantItemAction => true,
                AutomationGrantRewardPackageAction => true,
                AutomationAdjustEconomyAction => true,
                AutomationKickPlayerAction => false,
                AutomationMutePlayerAction => false,
                AutomationRestrictedCommandAction => true,
                AutomationDiscordMessageAction => true,
                _ => throw new InvalidOperationException("automation_action_type_not_exhaustive")
            };
        }

        public Task<AutomationDispatchResult> DispatchAsync(
            AutomationAction action,
            AutomationActionDispatchContext context,
            CancellationToken cancellationToken)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (context == null) throw new ArgumentNullException(nameof(context));
            var typed = ToTypedAction(action);
            return typed switch
            {
                AutomationBroadcastMessageAction value => DispatchBroadcastAsync(value, context, cancellationToken),
                AutomationPrivateMessageAction value => DispatchPrivateAsync(value, context, cancellationToken),
                AutomationAnnouncementAction value => DispatchAnnouncementAsync(value, cancellationToken),
                AutomationGrantItemAction value => DispatchGrantItemAsync(value, context, cancellationToken),
                AutomationGrantRewardPackageAction value => DispatchGrantRewardAsync(value, context, cancellationToken),
                AutomationAdjustEconomyAction value => DispatchEconomyAsync(value, context),
                AutomationKickPlayerAction value => DispatchKickAsync(value, context, cancellationToken),
                AutomationMutePlayerAction value => DispatchMuteAsync(value, context),
                AutomationRestrictedCommandAction value => DispatchRestrictedAsync(value, context, cancellationToken),
                AutomationDiscordMessageAction value => DispatchDiscordAsync(value, context),
                _ => throw new InvalidOperationException("automation_action_type_not_exhaustive")
            };
        }

        public static AutomationTypedAction ToTypedAction(AutomationAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return action.Type switch
            {
                "BroadcastMessage" => new AutomationBroadcastMessageAction(RequireText(action)),
                "PrivateMessage" => new AutomationPrivateMessageAction(RequireText(action)),
                "Announcement" => new AutomationAnnouncementAction(RequireText(action)),
                "GrantItem" => new AutomationGrantItemAction(
                    RequireText(action),
                    RequirePositiveAmount(action)),
                "GrantRewardPackage" => new AutomationGrantRewardPackageAction(RequireText(action)),
                "AdjustEconomy" => new AutomationAdjustEconomyAction(RequireNonZeroAmount(action)),
                "KickPlayer" => new AutomationKickPlayerAction(RequireText(action)),
                "MutePlayer" => new AutomationMutePlayerAction(
                    RequirePositiveDuration(action),
                    RequireText(action)),
                "RestrictedCommand" => new AutomationRestrictedCommandAction(RequireText(action)),
                "DiscordMessage" => new AutomationDiscordMessageAction(RequireText(action)),
                _ => throw new InvalidOperationException("automation_action_type_unavailable")
            };
        }

        private async Task<AutomationDispatchResult> DispatchBroadcastAsync(
            AutomationBroadcastMessageAction action,
            AutomationActionDispatchContext context,
            CancellationToken cancellationToken)
        {
            if (broadcastMessages == null)
                return AutomationDispatchResult.Unavailable("automation_broadcast_unavailable", false);
            try
            {
                var result = await broadcastMessages.ExecuteAsync(
                    Actor(context), action.Message, cancellationToken).ConfigureAwait(false);
                return ChatResult(result, false);
            }
            catch
            {
                return AutomationDispatchResult.ResultUnknown(
                    "automation_broadcast_result_unknown", false, true);
            }
        }

        private async Task<AutomationDispatchResult> DispatchPrivateAsync(
            AutomationPrivateMessageAction action,
            AutomationActionDispatchContext context,
            CancellationToken cancellationToken)
        {
            if (privateMessages == null)
                return AutomationDispatchResult.Unavailable("automation_private_message_unavailable", false);
            try
            {
                var result = await privateMessages.ExecuteAsync(
                    Actor(context),
                    context.ResolvedTargetId,
                    action.Message,
                    cancellationToken).ConfigureAwait(false);
                return ChatResult(result, false);
            }
            catch
            {
                return AutomationDispatchResult.ResultUnknown(
                    "automation_private_message_result_unknown", false, true);
            }
        }

        private async Task<AutomationDispatchResult> DispatchAnnouncementAsync(
            AutomationAnnouncementAction action,
            CancellationToken cancellationToken)
        {
            if (announcements == null)
                return AutomationDispatchResult.Unavailable("automation_announcement_unavailable", false);
            try
            {
                await announcements.SendAsync(action.Message, cancellationToken).ConfigureAwait(false);
                return AutomationDispatchResult.Succeeded(false);
            }
            catch
            {
                return AutomationDispatchResult.ResultUnknown(
                    "automation_announcement_result_unknown", false, true);
            }
        }

        private async Task<AutomationDispatchResult> DispatchGrantItemAsync(
            AutomationGrantItemAction action,
            AutomationActionDispatchContext context,
            CancellationToken cancellationToken)
        {
            if (grantItems == null || resources == null)
                return AutomationDispatchResult.Unavailable("automation_grant_item_unavailable", true);
            var player = await ResolvePlayerAsync(context.ResolvedTargetId, cancellationToken).ConfigureAwait(false);
            if (player == null)
                return AutomationDispatchResult.Failed("automation_target_not_online", false, true);
            GameResourceCatalogReadResult catalog;
            try { catalog = resources.Read(); }
            catch { return AutomationDispatchResult.Unavailable("automation_resource_catalog_unavailable", true); }
            if (catalog.Status != GameResourceCatalogReadStatus.Available || catalog.Snapshot == null)
                return AutomationDispatchResult.Unavailable("automation_resource_catalog_unavailable", true);
            if (action.Amount > int.MaxValue)
                return AutomationDispatchResult.Failed("automation_grant_item_amount_invalid", false, true);
            try
            {
                var result = await grantItems.ExecuteAsync(new GrantItemRequest(
                    Actor(context),
                    player.Target,
                    catalog.Snapshot.CatalogVersion,
                    action.ResourceId,
                    checked((int)action.Amount),
                    null,
                    false,
                    context.ConsumerIdempotencyKey,
                    context.ExecutionId), cancellationToken).ConfigureAwait(false);
                return PlayerActionResult(result.Status, result.FailureCode, true);
            }
            catch
            {
                return AutomationDispatchResult.ResultUnknown(
                    "automation_grant_item_result_unknown", true, true);
            }
        }

        private async Task<AutomationDispatchResult> DispatchGrantRewardAsync(
            AutomationGrantRewardPackageAction action,
            AutomationActionDispatchContext context,
            CancellationToken cancellationToken)
        {
            if (grantRewards == null)
                return AutomationDispatchResult.Unavailable("automation_grant_reward_unavailable", true);
            var player = await ResolvePlayerAsync(context.ResolvedTargetId, cancellationToken).ConfigureAwait(false);
            if (player == null)
                return AutomationDispatchResult.Failed("automation_target_not_online", false, true);
            try
            {
                var result = await grantRewards.ExecuteAsync(new GrantRewardCommand(
                    action.PackageId,
                    player.Target.CrossplatformId,
                    player.Target.EntityId,
                    player.Target.WorldId,
                    context.ConsumerIdempotencyKey,
                    null,
                    "Automation",
                    context.RuleId,
                    "System",
                    Actor(context),
                    context.ExecutionId), cancellationToken).ConfigureAwait(false);
                return result.Operation.State switch
                {
                    GrantOperationState.Completed => AutomationDispatchResult.Succeeded(true),
                    GrantOperationState.Failed => AutomationDispatchResult.Failed(
                        result.Operation.ErrorCode ?? "automation_grant_reward_failed", true, true),
                    GrantOperationState.PendingReconciliation => AutomationDispatchResult.ResultUnknown(
                        result.Operation.ErrorCode ?? "automation_grant_reward_result_unknown", true, true),
                    _ => AutomationDispatchResult.ResultUnknown(
                        "automation_grant_reward_not_terminal", true, true)
                };
            }
            catch
            {
                return AutomationDispatchResult.ResultUnknown(
                    "automation_grant_reward_result_unknown", true, true);
            }
        }

        private Task<AutomationDispatchResult> DispatchEconomyAsync(
            AutomationAdjustEconomyAction action,
            AutomationActionDispatchContext context)
        {
            if (economy == null)
                return FromResult(AutomationDispatchResult.Unavailable("automation_economy_unavailable", true));
            if (action.Amount == long.MinValue)
                return FromResult(AutomationDispatchResult.Failed("automation_economy_amount_invalid", false, true));
            try
            {
                economy.Execute(new AdjustPlayerBalanceCommand(
                    context.ConsumerIdempotencyKey,
                    context.ConsumerIdempotencyKey,
                    context.ResolvedTargetId,
                    action.Amount > 0 ? LedgerSide.Credit : LedgerSide.Debit,
                    Math.Abs(action.Amount),
                    Actor(context),
                    UtcNow(),
                    context.ExecutionId,
                    "Automation rule " + context.RuleId));
                return FromResult(AutomationDispatchResult.Succeeded(true));
            }
            catch
            {
                return FromResult(AutomationDispatchResult.ResultUnknown(
                    "automation_economy_result_unknown", true, true));
            }
        }

        private async Task<AutomationDispatchResult> DispatchKickAsync(
            AutomationKickPlayerAction action,
            AutomationActionDispatchContext context,
            CancellationToken cancellationToken)
        {
            if (kickPlayers == null)
                return AutomationDispatchResult.Unavailable("automation_kick_unavailable", false);
            var player = await ResolvePlayerAsync(context.ResolvedTargetId, cancellationToken).ConfigureAwait(false);
            if (player == null)
                return AutomationDispatchResult.Failed("automation_target_not_online", false);
            try
            {
                await kickPlayers.ExecuteAsync(new KickPlayerRequest(
                    Actor(context),
                    player.Player.EntityId,
                    player.Player.CrossplatformIdentity ?? player.Player.PlatformIdentity,
                    action.Reason,
                    true), cancellationToken).ConfigureAwait(false);
                return AutomationDispatchResult.Succeeded(false);
            }
            catch (PlayerNotOnlineException)
            {
                return AutomationDispatchResult.Failed("automation_target_not_online", false);
            }
            catch
            {
                return AutomationDispatchResult.ResultUnknown(
                    "automation_kick_result_unknown", false, true);
            }
        }

        private Task<AutomationDispatchResult> DispatchMuteAsync(
            AutomationMutePlayerAction action,
            AutomationActionDispatchContext context)
        {
            if (mutePlayers == null)
                return FromResult(AutomationDispatchResult.Unavailable("automation_mute_unavailable", false));
            try
            {
                mutePlayers.Create(
                    Actor(context),
                    context.ResolvedTargetId,
                    null,
                    action.Reason,
                    UtcNow().Add(action.Duration),
                    context.ExecutionId);
                return FromResult(AutomationDispatchResult.Succeeded(false));
            }
            catch
            {
                return FromResult(AutomationDispatchResult.ResultUnknown(
                    "automation_mute_result_unknown", false, true));
            }
        }

        private async Task<AutomationDispatchResult> DispatchRestrictedAsync(
            AutomationRestrictedCommandAction action,
            AutomationActionDispatchContext context,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(
                    action.RegisteredCommandKey,
                    RewardRegisteredActions.ResetSkills,
                    StringComparison.Ordinal))
            {
                return AutomationDispatchResult.Unavailable(
                    "automation_registered_command_unavailable", true);
            }
            if (resetSkills == null)
                return AutomationDispatchResult.Unavailable("automation_reset_skills_unavailable", true);
            var player = await ResolvePlayerAsync(context.ResolvedTargetId, cancellationToken).ConfigureAwait(false);
            if (player == null)
                return AutomationDispatchResult.Failed("automation_target_not_online", false, true);
            try
            {
                var result = await resetSkills.ExecuteAsync(new ResetSkillsRequest(
                    Actor(context),
                    player.Target,
                    context.ConsumerIdempotencyKey,
                    context.ExecutionId,
                    true), cancellationToken).ConfigureAwait(false);
                return result.Status switch
                {
                    ResetSkillsOperationStatus.Succeeded => AutomationDispatchResult.Succeeded(true),
                    ResetSkillsOperationStatus.ResultUnknown => AutomationDispatchResult.ResultUnknown(
                        result.FailureCode ?? "automation_reset_skills_result_unknown", true, true),
                    _ => AutomationDispatchResult.Failed(
                        result.FailureCode ?? "automation_reset_skills_failed", true, true)
                };
            }
            catch
            {
                return AutomationDispatchResult.ResultUnknown(
                    "automation_reset_skills_result_unknown", true, true);
            }
        }

        private Task<AutomationDispatchResult> DispatchDiscordAsync(
            AutomationDiscordMessageAction action,
            AutomationActionDispatchContext context)
        {
            if (discordOutbox == null)
                return FromResult(AutomationDispatchResult.Unavailable("automation_discord_unavailable", true));
            try
            {
                var target = discordOutbox.ListTargets().SingleOrDefault(candidate =>
                    candidate.IsEnabled &&
                    string.Equals(candidate.TargetKey, context.ResolvedTargetId, StringComparison.Ordinal));
                if (target == null)
                    return FromResult(AutomationDispatchResult.Failed("automation_discord_target_invalid", false, true));
                discordOutbox.EnqueueDelivery(new DiscordDelivery(
                    context.ConsumerIdempotencyKey,
                    context.ConsumerIdempotencyKey,
                    target.TargetKey,
                    DiscordDeliveryStatus.Pending,
                    action.Message,
                    "Automation rule " + context.RuleId,
                    UtcNow(),
                    0,
                    UtcNow(),
                    null));
                return FromResult(AutomationDispatchResult.Succeeded(true));
            }
            catch
            {
                return FromResult(AutomationDispatchResult.ResultUnknown(
                    "automation_discord_outbox_result_unknown", true, true));
            }
        }

        private async Task<ResolvedPlayer?> ResolvePlayerAsync(
            string stablePlayerId,
            CancellationToken cancellationToken)
        {
            if (onlinePlayers == null || worldId == null) return null;
            OnlinePlayersSnapshot snapshot;
            try { snapshot = await onlinePlayers.GetOnlineAsync(cancellationToken).ConfigureAwait(false); }
            catch { return null; }
            var player = snapshot.Players.SingleOrDefault(candidate =>
                string.Equals(
                    candidate.CrossplatformIdentity?.CombinedId ?? candidate.PlatformIdentity.CombinedId,
                    stablePlayerId,
                    StringComparison.Ordinal));
            if (player == null) return null;
            var crossplatformId = player.CrossplatformIdentity?.CombinedId ?? player.PlatformIdentity.CombinedId;
            return new ResolvedPlayer(
                player,
                new PlayerTargetStamp(
                    crossplatformId,
                    player.EntityId,
                    player.ObservedAtUtc,
                    worldId));
        }

        private static AutomationDispatchResult ChatResult(
            ChatSendResult result,
            bool idempotent) =>
            result.Status switch
            {
                ChatSendStatus.Accepted => AutomationDispatchResult.Succeeded(idempotent),
                ChatSendStatus.Disabled => AutomationDispatchResult.Unavailable("automation_chat_disabled", idempotent),
                ChatSendStatus.NotReady => AutomationDispatchResult.Unavailable("automation_chat_not_ready", idempotent),
                ChatSendStatus.Unknown => AutomationDispatchResult.ResultUnknown(
                    "automation_chat_result_unknown", idempotent, true),
                _ => AutomationDispatchResult.Failed(
                    "automation_chat_" + result.Status.ToString().ToLowerInvariant(), true, idempotent)
            };

        private static AutomationDispatchResult PlayerActionResult(
            PlayerActionStatus status,
            string? errorCode,
            bool idempotent) =>
            status switch
            {
                PlayerActionStatus.Succeeded => AutomationDispatchResult.Succeeded(idempotent),
                PlayerActionStatus.ResultUnknown => AutomationDispatchResult.ResultUnknown(
                    errorCode ?? "automation_player_action_result_unknown", idempotent, true),
                _ => AutomationDispatchResult.Failed(
                    errorCode ?? "automation_player_action_failed", true, idempotent)
            };

        private DateTimeOffset UtcNow()
        {
            var value = utcNow();
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("automation_dispatch_clock_must_be_utc");
            return value;
        }

        private static string Actor(AutomationActionDispatchContext context) =>
            "automation:" + context.RuleId;

        private static Task<AutomationDispatchResult> FromResult(AutomationDispatchResult result) =>
            Task.FromResult(result);

        private static string RequireText(AutomationAction action) =>
            string.IsNullOrWhiteSpace(action.TextValue)
                ? throw new InvalidOperationException("automation_action_text_missing")
                : action.TextValue!;

        private static long RequirePositiveAmount(AutomationAction action) =>
            action.Amount.HasValue && action.Amount.Value > 0
                ? action.Amount.Value
                : throw new InvalidOperationException("automation_action_amount_invalid");

        private static long RequireNonZeroAmount(AutomationAction action) =>
            action.Amount.HasValue && action.Amount.Value != 0
                ? action.Amount.Value
                : throw new InvalidOperationException("automation_action_amount_invalid");

        private static TimeSpan RequirePositiveDuration(AutomationAction action) =>
            action.Duration.HasValue && action.Duration.Value > TimeSpan.Zero
                ? action.Duration.Value
                : throw new InvalidOperationException("automation_action_duration_invalid");

        private sealed record ResolvedPlayer(PlayerSnapshot Player, PlayerTargetStamp Target);
    }
}
