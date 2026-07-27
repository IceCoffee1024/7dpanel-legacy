using System;

namespace LSTY.SevenDPanel.Application.Rewards
{
    public sealed class DailyRewardPolicyDraft
    {
        public DailyRewardPolicyDraft(
            string ruleId,
            string rewardPackageId,
            bool enabled,
            long? expectedRowVersion)
        {
            RuleId = RewardValidation.RequireText(ruleId, nameof(ruleId));
            RewardPackageId = RewardValidation.RequireText(
                rewardPackageId,
                nameof(rewardPackageId));
            if (expectedRowVersion < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            Enabled = enabled;
            ExpectedRowVersion = expectedRowVersion;
        }

        public string RuleId { get; }
        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public long? ExpectedRowVersion { get; }
    }

    public sealed class DailyRewardPolicySnapshot
    {
        public DailyRewardPolicySnapshot(
            DailyRewardPolicyDraft draft,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            RewardValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            RewardValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (updatedAtUtc < createdAtUtc) throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            RuleId = draft.RuleId;
            RewardPackageId = draft.RewardPackageId;
            Enabled = draft.Enabled;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
        }

        public string RuleId { get; }
        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public interface IDailyRewardPolicyStore
    {
        DailyRewardPolicySnapshot SaveDailyRewardPolicy(
            DailyRewardPolicyDraft policy,
            DateTimeOffset occurredAtUtc);

        DailyRewardPolicySnapshot GetDailyRewardPolicy(string ruleId);
    }

    public sealed class DailyRewardPolicyUnavailableException : InvalidOperationException
    {
        public DailyRewardPolicyUnavailableException()
            : base("daily_reward_policy_unavailable") { }
    }

    public sealed class DailyRewardPolicyConcurrencyException : InvalidOperationException
    {
        public DailyRewardPolicyConcurrencyException()
            : base("daily_reward_policy_concurrency_conflict") { }
    }

    public sealed class SaveDailyRewardPolicyUseCase
    {
        private readonly IDailyRewardPolicyStore policies;
        private readonly IRewardStore rewards;
        private readonly Func<DateTimeOffset> utcClock;

        public SaveDailyRewardPolicyUseCase(
            IDailyRewardPolicyStore policies,
            IRewardStore rewards)
            : this(policies, rewards, () => DateTimeOffset.UtcNow) { }

        internal SaveDailyRewardPolicyUseCase(
            IDailyRewardPolicyStore policies,
            IRewardStore rewards,
            Func<DateTimeOffset> utcClock)
        {
            this.policies = policies ?? throw new ArgumentNullException(nameof(policies));
            this.rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public DailyRewardPolicySnapshot Execute(DailyRewardPolicyDraft policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            rewards.GetPackage(policy.RewardPackageId);
            var now = utcClock();
            RewardValidation.RequireUtc(now, nameof(utcClock));
            return policies.SaveDailyRewardPolicy(policy, now);
        }
    }
}
