using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Rewards;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Rewards
{
    public sealed class ThirdWaveRewardDeliveryAdapter : IRewardDeliveryPort
    {
        private readonly GrantItemUseCase grantItem;
        private readonly ResetSkillsUseCase resetSkills;
        private readonly IPlayerActionOperationQuery operationQuery;
        private readonly IRewardDeliveryJournal journal;
        private readonly Func<DateTimeOffset> utcClock;

        public ThirdWaveRewardDeliveryAdapter(
            GrantItemUseCase grantItem,
            ResetSkillsUseCase resetSkills,
            IPlayerActionOperationQuery operationQuery,
            IRewardDeliveryJournal journal)
            : this(grantItem, resetSkills, operationQuery, journal, () => DateTimeOffset.UtcNow)
        {
        }

        internal ThirdWaveRewardDeliveryAdapter(
            GrantItemUseCase grantItem,
            ResetSkillsUseCase resetSkills,
            IPlayerActionOperationQuery operationQuery,
            IRewardDeliveryJournal journal,
            Func<DateTimeOffset> utcClock)
        {
            this.grantItem = grantItem ?? throw new ArgumentNullException(nameof(grantItem));
            this.resetSkills = resetSkills ?? throw new ArgumentNullException(nameof(resetSkills));
            this.operationQuery = operationQuery ?? throw new ArgumentNullException(nameof(operationQuery));
            this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public async Task<RewardDeliveryResult> DeliverAsync(
            RewardDeliveryCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var results = new List<RewardDeliveryEntryResult>();
            foreach (var entry in command.Entries)
            {
                if (entry.Kind == RewardEntryKind.Currency) continue;
                RewardDeliveryEntryResult result;
                try
                {
                    result = entry.Kind switch
                    {
                        RewardEntryKind.Item => await DeliverItemAsync(
                                command,
                                entry,
                                cancellationToken)
                            .ConfigureAwait(false),
                        RewardEntryKind.RegisteredAction => await DeliverActionAsync(
                                command,
                                entry,
                                cancellationToken)
                            .ConfigureAwait(false),
                        _ => RewardDeliveryEntryResult.Failed(
                            entry.OperationEntryId,
                            null,
                            "reward_entry_kind_unsupported")
                    };
                }
                catch (OperationCanceledException)
                {
                    result = RewardDeliveryEntryResult.ResultUnknown(
                        entry.OperationEntryId,
                        null,
                        "reward_delivery_cancelled_unknown");
                }
                catch
                {
                    result = RewardDeliveryEntryResult.ResultUnknown(
                        entry.OperationEntryId,
                        null,
                        "reward_delivery_result_unknown");
                }
                results.Add(result);
                if (result.Status == RewardDeliveryStatus.ResultUnknown)
                    return RewardDeliveryResult.ResultUnknown(results, result.ErrorCode);
                if (result.Status == RewardDeliveryStatus.Failed)
                    return RewardDeliveryResult.Failed(results, result.ErrorCode ?? "reward_delivery_failed");
            }
            return RewardDeliveryResult.Succeeded(results);
        }

        private async Task<RewardDeliveryEntryResult> DeliverItemAsync(
            RewardDeliveryCommand command,
            ResolvedRewardEntry entry,
            CancellationToken cancellationToken)
        {
            if (entry.ResourceId == null || entry.CatalogVersion == null ||
                !entry.Quantity.HasValue)
            {
                return RewardDeliveryEntryResult.Failed(
                    entry.OperationEntryId,
                    null,
                    "reward_item_not_resolved");
            }
            GrantItemResult result;
            try
            {
                result = await grantItem.ExecuteAsync(
                        new GrantItemRequest(
                            OperatorId(command),
                            Target(command),
                            entry.CatalogVersion,
                            entry.ResourceId,
                            entry.Quantity.Value,
                            entry.Quality,
                            entry.HiddenItemConfirmed,
                            ClientRequestKey(command, entry),
                            command.GrantOperationId),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (GrantItemRequestRejectedException exception)
            {
                return RewardDeliveryEntryResult.Failed(
                    entry.OperationEntryId,
                    null,
                    exception.Code);
            }
            return RecordAndReadTerminal(
                command,
                entry,
                result.OperationId,
                result.FailureCode);
        }

        private async Task<RewardDeliveryEntryResult> DeliverActionAsync(
            RewardDeliveryCommand command,
            ResolvedRewardEntry entry,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(
                    entry.RegisteredAction,
                    RewardRegisteredActions.ResetSkills,
                    StringComparison.Ordinal))
            {
                return RewardDeliveryEntryResult.Failed(
                    entry.OperationEntryId,
                    null,
                    "reward_action_not_registered");
            }
            var result = await resetSkills.ExecuteAsync(
                    new ResetSkillsRequest(
                        OperatorId(command),
                        Target(command),
                        ClientRequestKey(command, entry),
                        command.GrantOperationId,
                        true),
                    cancellationToken)
                .ConfigureAwait(false);
            return RecordAndReadTerminal(
                command,
                entry,
                result.OperationId,
                result.FailureCode);
        }

        private RewardDeliveryEntryResult RecordAndReadTerminal(
            RewardDeliveryCommand command,
            ResolvedRewardEntry entry,
            string operationId,
            string? reportedFailureCode)
        {
            try
            {
                journal.RecordDeliveryOperation(
                    command.GrantOperationId,
                    entry.OperationEntryId,
                    operationId,
                    UtcNow());
            }
            catch
            {
                return RewardDeliveryEntryResult.ResultUnknown(
                    entry.OperationEntryId,
                    operationId,
                    "reward_delivery_operation_journal_failed");
            }

            PlayerActionOperation? stored;
            try { stored = operationQuery.Get(operationId); }
            catch { stored = null; }
            if (stored == null || stored.Status == PlayerActionStatus.Pending ||
                stored.Status == PlayerActionStatus.ResultUnknown)
            {
                return RewardDeliveryEntryResult.ResultUnknown(
                    entry.OperationEntryId,
                    operationId,
                    stored?.FailureCode ?? reportedFailureCode ?? "ResultUnknown");
            }
            if (stored.Status == PlayerActionStatus.Succeeded)
                return RewardDeliveryEntryResult.Succeeded(entry.OperationEntryId, operationId);
            return RewardDeliveryEntryResult.Failed(
                entry.OperationEntryId,
                operationId,
                stored.FailureCode ?? reportedFailureCode ?? "reward_player_action_failed");
        }

        private PlayerTargetStamp Target(RewardDeliveryCommand command) => new PlayerTargetStamp(
            command.CrossplatformId,
            command.ExpectedEntityId,
            UtcNow(),
            command.ExpectedWorldId);

        private static string OperatorId(RewardDeliveryCommand command) =>
            "reward:" + command.GrantOperationId;

        private static string ClientRequestKey(
            RewardDeliveryCommand command,
            ResolvedRewardEntry entry) =>
            command.GrantOperationId + ":" + entry.OperationEntryId;

        private DateTimeOffset UtcNow()
        {
            var value = utcClock();
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("The reward delivery clock must return UTC.");
            return value;
        }
    }
}
