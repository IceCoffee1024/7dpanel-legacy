using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Domain.Rewards;

namespace LSTY.SevenDPanel.Application.Rewards
{
    public enum DailyRewardClaimState
    {
        Reserved,
        Dispatching,
        PendingReconciliation,
        Completed,
        Failed
    }

    public enum DailyRewardClaimStatus
    {
        Claimed,
        AlreadyClaimed,
        PendingReconciliation,
        Failed
    }

    public sealed class DailyRewardClaimCommand
    {
        public DailyRewardClaimCommand(
            string ruleId,
            string crossplatformId,
            int expectedEntityId,
            string expectedWorldId,
            string? correlationId)
        {
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            RuleId = RewardValidation.RequireText(ruleId, nameof(ruleId));
            CrossplatformId = RewardValidation.RequireText(
                crossplatformId,
                nameof(crossplatformId));
            ExpectedEntityId = expectedEntityId;
            ExpectedWorldId = RewardValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            CorrelationId = RewardValidation.OptionalText(correlationId);
        }

        public string RuleId { get; }
        public string CrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public string? CorrelationId { get; }
    }

    public sealed class DailyRewardClaimDraft
    {
        public DailyRewardClaimDraft(
            string claimId,
            string ruleId,
            string rewardPackageId,
            string crossplatformId,
            string periodKey,
            DateTimeOffset periodStartUtc,
            DateTimeOffset periodEndUtc,
            string idempotencyKey,
            int expectedEntityId,
            string expectedWorldId,
            string? correlationId,
            DateTimeOffset createdAtUtc)
        {
            ClaimId = RewardValidation.RequireText(claimId, nameof(claimId));
            RuleId = RewardValidation.RequireText(ruleId, nameof(ruleId));
            RewardPackageId = RewardValidation.RequireText(
                rewardPackageId,
                nameof(rewardPackageId));
            CrossplatformId = RewardValidation.RequireText(
                crossplatformId,
                nameof(crossplatformId));
            PeriodKey = RewardValidation.RequireText(periodKey, nameof(periodKey));
            RewardValidation.RequireUtc(periodStartUtc, nameof(periodStartUtc));
            RewardValidation.RequireUtc(periodEndUtc, nameof(periodEndUtc));
            PeriodStartUtc = periodStartUtc;
            PeriodEndUtc = periodEndUtc;
            if (periodEndUtc <= periodStartUtc) throw new ArgumentOutOfRangeException(nameof(periodEndUtc));
            IdempotencyKey = RewardValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            ExpectedEntityId = expectedEntityId;
            ExpectedWorldId = RewardValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            CorrelationId = RewardValidation.OptionalText(correlationId);
            RewardValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            CreatedAtUtc = createdAtUtc;
        }

        public string ClaimId { get; }
        public string RuleId { get; }
        public string RewardPackageId { get; }
        public string CrossplatformId { get; }
        public string PeriodKey { get; }
        public DateTimeOffset PeriodStartUtc { get; }
        public DateTimeOffset PeriodEndUtc { get; }
        public string IdempotencyKey { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
    }

    public sealed class DailyRewardClaimSnapshot
    {
        public DailyRewardClaimSnapshot(
            DailyRewardClaimDraft draft,
            DailyRewardClaimState state,
            string? grantOperationId,
            string? errorCode,
            DateTimeOffset updatedAtUtc,
            DateTimeOffset? completedAtUtc,
            long rowVersion)
        {
            Draft = draft ?? throw new ArgumentNullException(nameof(draft));
            if (!Enum.IsDefined(typeof(DailyRewardClaimState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            GrantOperationId = RewardValidation.OptionalText(grantOperationId);
            ErrorCode = RewardValidation.OptionalText(errorCode);
            RewardValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            UpdatedAtUtc = updatedAtUtc;
            if (updatedAtUtc < draft.CreatedAtUtc) throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
            if (completedAtUtc.HasValue)
            {
                RewardValidation.RequireUtc(completedAtUtc.Value, nameof(completedAtUtc));
                CompletedAtUtc = completedAtUtc.Value;
            }
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            State = state;
            RowVersion = rowVersion;
        }

        public DailyRewardClaimDraft Draft { get; }
        public string ClaimId => Draft.ClaimId;
        public DailyRewardClaimState State { get; }
        public string? GrantOperationId { get; }
        public string? ErrorCode { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class DailyRewardClaimCreationResult
    {
        public DailyRewardClaimCreationResult(DailyRewardClaimSnapshot claim, bool created)
        {
            Claim = claim ?? throw new ArgumentNullException(nameof(claim));
            Created = created;
        }

        public DailyRewardClaimSnapshot Claim { get; }
        public bool Created { get; }
    }

    public interface IDailyRewardClaimStore
    {
        DailyRewardClaimCreationResult GetOrCreateDailyRewardClaim(DailyRewardClaimDraft claim);
        DailyRewardClaimSnapshot GetDailyRewardClaim(string claimId);
        bool TryStartDailyRewardClaim(
            string claimId,
            long expectedRowVersion,
            DateTimeOffset occurredAtUtc);
        bool TryResolveDailyRewardClaim(
            string claimId,
            long expectedRowVersion,
            DailyRewardClaimState state,
            string? grantOperationId,
            string? errorCode,
            DateTimeOffset occurredAtUtc);
    }

    public sealed class DailyRewardClaimResult
    {
        public DailyRewardClaimResult(
            DailyRewardClaimStatus status,
            DailyRewardClaimSnapshot claim)
        {
            if (!Enum.IsDefined(typeof(DailyRewardClaimStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            Status = status;
            Claim = claim ?? throw new ArgumentNullException(nameof(claim));
        }

        public DailyRewardClaimStatus Status { get; }
        public DailyRewardClaimSnapshot Claim { get; }
    }

    public sealed class ClaimDailyRewardUseCase
    {
        private readonly IDailyRewardClaimStore store;
        private readonly GrantRewardUseCase grant;
        private readonly IDailyRewardPolicyStore policies;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly Func<string> claimIdFactory;

        public ClaimDailyRewardUseCase(
            IDailyRewardClaimStore store,
            GrantRewardUseCase grant,
            IDailyRewardPolicyStore policies)
            : this(
                store,
                grant,
                policies,
                () => DateTimeOffset.UtcNow,
                () => "daily-claim-" + Guid.NewGuid().ToString("N"))
        {
        }

        internal ClaimDailyRewardUseCase(
            IDailyRewardClaimStore store,
            GrantRewardUseCase grant,
            IDailyRewardPolicyStore policies,
            Func<DateTimeOffset> utcClock,
            Func<string> claimIdFactory)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.grant = grant ?? throw new ArgumentNullException(nameof(grant));
            this.policies = policies ?? throw new ArgumentNullException(nameof(policies));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            this.claimIdFactory = claimIdFactory ?? throw new ArgumentNullException(nameof(claimIdFactory));
        }

        public async Task<DailyRewardClaimResult> ExecuteAsync(
            DailyRewardClaimCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            cancellationToken.ThrowIfCancellationRequested();
            DailyRewardPolicySnapshot policy;
            try
            {
                policy = policies.GetDailyRewardPolicy(command.RuleId);
            }
            catch (System.Collections.Generic.KeyNotFoundException)
            {
                throw new DailyRewardPolicyUnavailableException();
            }
            if (!policy.Enabled) throw new DailyRewardPolicyUnavailableException();
            var now = UtcNow();
            var periodStart = new DateTimeOffset(
                now.Year,
                now.Month,
                now.Day,
                0,
                0,
                0,
                TimeSpan.Zero);
            var periodEnd = periodStart.AddDays(1);
            var periodKey = periodStart.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var idempotencyKey = string.Join(
                ":",
                "daily-claim",
                policy.RuleId,
                command.CrossplatformId,
                periodKey);
            var creation = store.GetOrCreateDailyRewardClaim(new DailyRewardClaimDraft(
                RewardValidation.RequireText(claimIdFactory(), nameof(claimIdFactory)),
                policy.RuleId,
                policy.RewardPackageId,
                command.CrossplatformId,
                periodKey,
                periodStart,
                periodEnd,
                idempotencyKey,
                command.ExpectedEntityId,
                command.ExpectedWorldId,
                command.CorrelationId,
                now));
            if (!creation.Created) return Existing(creation.Claim);
            if (!store.TryStartDailyRewardClaim(
                    creation.Claim.ClaimId,
                    creation.Claim.RowVersion,
                    UtcNow()))
            {
                return Existing(store.GetDailyRewardClaim(creation.Claim.ClaimId));
            }

            var dispatching = store.GetDailyRewardClaim(creation.Claim.ClaimId);
            GrantOperationSnapshot? operation = null;
            DailyRewardClaimState resolvedState;
            string? errorCode;
            try
            {
                var result = await grant.ExecuteAsync(
                        new GrantRewardCommand(
                            policy.RewardPackageId,
                            command.CrossplatformId,
                            command.ExpectedEntityId,
                            command.ExpectedWorldId,
                            "daily-grant:" + dispatching.ClaimId,
                            null,
                            "Daily",
                            policy.RuleId,
                            "Player",
                            command.CrossplatformId,
                            command.CorrelationId),
                        cancellationToken)
                    .ConfigureAwait(false);
                operation = result.Operation;
                resolvedState = Classify(operation);
                errorCode = operation.ErrorCode;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Resolve(
                    dispatching,
                    DailyRewardClaimState.PendingReconciliation,
                    operation?.OperationId,
                    "daily_grant_result_unknown");
                throw;
            }
            catch
            {
                resolvedState = DailyRewardClaimState.PendingReconciliation;
                errorCode = "daily_grant_result_unknown";
            }

            Resolve(dispatching, resolvedState, operation?.OperationId, errorCode);
            var resolved = store.GetDailyRewardClaim(dispatching.ClaimId);
            return new DailyRewardClaimResult(MapResolved(resolved.State), resolved);
        }

        private void Resolve(
            DailyRewardClaimSnapshot dispatching,
            DailyRewardClaimState state,
            string? operationId,
            string? errorCode) =>
            store.TryResolveDailyRewardClaim(
                dispatching.ClaimId,
                dispatching.RowVersion,
                state,
                operationId,
                errorCode,
                UtcNow());

        private static DailyRewardClaimState Classify(GrantOperationSnapshot operation)
        {
            if (operation.State == GrantOperationState.Completed)
                return DailyRewardClaimState.Completed;
            if (operation.State == GrantOperationState.Failed && operation.Entries.All(entry =>
                    entry.DeliveryOperationId == null && entry.LedgerTransactionId == null))
                return DailyRewardClaimState.Failed;
            return DailyRewardClaimState.PendingReconciliation;
        }

        private static DailyRewardClaimResult Existing(DailyRewardClaimSnapshot claim) =>
            new DailyRewardClaimResult(claim.State switch
            {
                DailyRewardClaimState.Completed => DailyRewardClaimStatus.AlreadyClaimed,
                DailyRewardClaimState.Failed => DailyRewardClaimStatus.Failed,
                _ => DailyRewardClaimStatus.PendingReconciliation
            }, claim);

        private static DailyRewardClaimStatus MapResolved(DailyRewardClaimState state) => state switch
        {
            DailyRewardClaimState.Completed => DailyRewardClaimStatus.Claimed,
            DailyRewardClaimState.Failed => DailyRewardClaimStatus.Failed,
            _ => DailyRewardClaimStatus.PendingReconciliation
        };

        private DateTimeOffset UtcNow()
        {
            var value = utcClock();
            RewardValidation.RequireUtc(value, nameof(utcClock));
            return value;
        }
    }
}
