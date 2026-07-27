using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Commerce;

namespace LSTY.SevenDPanel.Application.Rewards
{
    public sealed class SaveAchievementDefinitionUseCase
    {
        private readonly ICommerceStore store;
        private readonly Func<DateTimeOffset> utcClock;

        public SaveAchievementDefinitionUseCase(ICommerceStore store)
            : this(store, () => DateTimeOffset.UtcNow) { }

        internal SaveAchievementDefinitionUseCase(
            ICommerceStore store,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public AchievementDefinitionSnapshot Execute(AchievementDefinitionDraft definition) =>
            store.SaveAchievement(
                definition ?? throw new ArgumentNullException(nameof(definition)),
                CommerceGrantSupport.UtcNow(utcClock));
    }

    public sealed class ObserveAchievementUseCase
    {
        private readonly ICommerceStore store;
        private readonly GrantRewardUseCase grant;

        public ObserveAchievementUseCase(ICommerceStore store, GrantRewardUseCase grant)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.grant = grant ?? throw new ArgumentNullException(nameof(grant));
        }

        public async Task<IReadOnlyList<RewardEligibilitySnapshot>> ExecuteAsync(
            ObserveAchievementCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var candidates = store.ObserveAchievement(command);
            var results = new List<RewardEligibilitySnapshot>(candidates.Count);
            foreach (var candidate in candidates)
            {
                var result = await RewardEligibilityGrantSupport.TryGrantAsync(
                    store,
                    grant,
                    candidate,
                    command.ExpectedEntityId,
                    command.ExpectedWorldId,
                    command.CorrelationId,
                    command.ObservedAtUtc,
                    cancellationToken).ConfigureAwait(false);
                if (result != null) results.Add(result);
            }
            return results;
        }
    }

    public sealed class SaveOnlineRewardRuleUseCase
    {
        private readonly ICommerceStore store;
        private readonly Func<DateTimeOffset> utcClock;

        public SaveOnlineRewardRuleUseCase(ICommerceStore store)
            : this(store, () => DateTimeOffset.UtcNow) { }

        internal SaveOnlineRewardRuleUseCase(ICommerceStore store, Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public OnlineRewardRuleSnapshot Execute(OnlineRewardRuleDraft rule) =>
            store.SaveOnlineRewardRule(
                rule ?? throw new ArgumentNullException(nameof(rule)),
                CommerceGrantSupport.UtcNow(utcClock));
    }

    public sealed class EvaluateOnlineRewardsUseCase
    {
        private readonly ICommerceStore store;
        private readonly GrantRewardUseCase grant;

        public EvaluateOnlineRewardsUseCase(ICommerceStore store, GrantRewardUseCase grant)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.grant = grant ?? throw new ArgumentNullException(nameof(grant));
        }

        public async Task<IReadOnlyList<RewardEligibilitySnapshot>> ExecuteAsync(
            EvaluateOnlineRewardsCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var candidates = store.EvaluateOnlineRewards(command);
            var results = new List<RewardEligibilitySnapshot>(candidates.Count);
            foreach (var candidate in candidates)
            {
                if (candidate.State != RewardEligibilityState.Eligible)
                {
                    results.Add(candidate);
                    continue;
                }
                var result = await RewardEligibilityGrantSupport.TryGrantAsync(
                    store,
                    grant,
                    candidate,
                    command.ExpectedEntityId,
                    command.ExpectedWorldId,
                    command.CorrelationId,
                    command.EvidenceToUtc,
                    cancellationToken).ConfigureAwait(false);
                if (result != null) results.Add(result);
            }
            return results;
        }
    }

    public sealed class ManualOnlineRewardGrantUseCase
    {
        private readonly ICommerceStore store;
        private readonly GrantRewardUseCase grant;

        public ManualOnlineRewardGrantUseCase(ICommerceStore store, GrantRewardUseCase grant)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.grant = grant ?? throw new ArgumentNullException(nameof(grant));
        }

        public async Task<RewardEligibilitySnapshot> ExecuteAsync(
            ManualOnlineRewardCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var eligibility = store.ReserveManualOnlineReward(command);
            if (eligibility.State != RewardEligibilityState.Eligible)
                return eligibility;
            return await RewardEligibilityGrantSupport.TryGrantAsync(
                    store,
                    grant,
                    eligibility,
                    command.ExpectedEntityId,
                    command.ExpectedWorldId,
                    command.CorrelationId,
                    command.OccurredAtUtc,
                    cancellationToken)
                .ConfigureAwait(false) ?? eligibility;
        }
    }

    internal static class RewardEligibilityGrantSupport
    {
        internal static async Task<RewardEligibilitySnapshot?> TryGrantAsync(
            ICommerceStore store,
            GrantRewardUseCase grant,
            RewardEligibilitySnapshot eligibility,
            int expectedEntityId,
            string expectedWorldId,
            string? correlationId,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            var reserved = store.TryReserveEligibilityGrant(
                eligibility.EligibilityId,
                occurredAtUtc);
            if (reserved == null) return null;
            try
            {
                var result = await grant.ExecuteAsync(new GrantRewardCommand(
                        reserved.RewardPackageId,
                        reserved.CrossplatformId,
                        expectedEntityId,
                        expectedWorldId,
                        "eligibility-grant:" + reserved.EligibilityId,
                        reserved.EligibilityKey,
                        reserved.RuleKind,
                        reserved.RuleId,
                        "System",
                        "reward:evidence",
                        correlationId),
                    cancellationToken).ConfigureAwait(false);
                return store.ResolveEligibilityGrant(new EligibilityGrantResolution(
                    reserved.EligibilityId,
                    CommerceGrantSupport.Classify(result.Operation),
                    result.Operation.OperationId,
                    result.Operation.ErrorCode,
                    occurredAtUtc));
            }
            catch (Exception exception) when (CommerceGrantSupport.IsKnownPreDispatchFailure(exception))
            {
                return store.ResolveEligibilityGrant(new EligibilityGrantResolution(
                    reserved.EligibilityId,
                    CommerceGrantResolutionKind.FailedBeforeSideEffects,
                    null,
                    exception.Message,
                    occurredAtUtc));
            }
            catch
            {
                return store.ResolveEligibilityGrant(new EligibilityGrantResolution(
                    reserved.EligibilityId,
                    CommerceGrantResolutionKind.PendingReconciliation,
                    null,
                    "reward_delivery_result_unknown",
                    occurredAtUtc));
            }
        }
    }
}
