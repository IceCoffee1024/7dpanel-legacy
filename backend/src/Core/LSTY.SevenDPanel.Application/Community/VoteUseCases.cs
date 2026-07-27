using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Domain.Community;

namespace LSTY.SevenDPanel.Application.Community
{
    public enum VoteKind
    {
        Kick,
        Restart
    }

    public enum VoteChoice
    {
        Yes,
        No
    }

    public sealed class VoteConfiguration
    {
        public VoteConfiguration(
            string configurationId,
            VoteKind kind,
            bool enabled,
            TimeSpan duration,
            int thresholdPercent,
            int minimumParticipants,
            TimeSpan initiatorMinimumOnline,
            TimeSpan participantMinimumOnline,
            TimeSpan initiatorCooldown,
            TimeSpan targetCooldown,
            TimeSpan globalCooldown,
            string mutualExclusionScope,
            bool allowVoteChange,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            ConfigurationId = VoteValidation.RequireText(configurationId, nameof(configurationId));
            VoteValidation.RequireDefined(kind, nameof(kind));
            if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
            if (thresholdPercent < 1 || thresholdPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(thresholdPercent));
            if (minimumParticipants < 1) throw new ArgumentOutOfRangeException(nameof(minimumParticipants));
            VoteValidation.RequireNonNegative(initiatorMinimumOnline, nameof(initiatorMinimumOnline));
            VoteValidation.RequireNonNegative(participantMinimumOnline, nameof(participantMinimumOnline));
            VoteValidation.RequireNonNegative(initiatorCooldown, nameof(initiatorCooldown));
            VoteValidation.RequireNonNegative(targetCooldown, nameof(targetCooldown));
            VoteValidation.RequireNonNegative(globalCooldown, nameof(globalCooldown));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));

            Kind = kind;
            Enabled = enabled;
            Duration = duration;
            ThresholdPercent = thresholdPercent;
            MinimumParticipants = minimumParticipants;
            InitiatorMinimumOnline = initiatorMinimumOnline;
            ParticipantMinimumOnline = participantMinimumOnline;
            InitiatorCooldown = initiatorCooldown;
            TargetCooldown = targetCooldown;
            GlobalCooldown = globalCooldown;
            MutualExclusionScope = VoteValidation.RequireText(
                mutualExclusionScope,
                nameof(mutualExclusionScope));
            AllowVoteChange = allowVoteChange;
            UpdatedAtUtc = VoteValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            RowVersion = rowVersion;
        }

        public string ConfigurationId { get; }
        public VoteKind Kind { get; }
        public bool Enabled { get; }
        public TimeSpan Duration { get; }
        public int ThresholdPercent { get; }
        public int MinimumParticipants { get; }
        public TimeSpan InitiatorMinimumOnline { get; }
        public TimeSpan ParticipantMinimumOnline { get; }
        public TimeSpan InitiatorCooldown { get; }
        public TimeSpan TargetCooldown { get; }
        public TimeSpan GlobalCooldown { get; }
        public string MutualExclusionScope { get; }
        public bool AllowVoteChange { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class VoteEligiblePlayer
    {
        public VoteEligiblePlayer(string crossplatformId, TimeSpan onlineDuration)
        {
            CrossplatformId = VoteValidation.RequireText(crossplatformId, nameof(crossplatformId));
            VoteValidation.RequireNonNegative(onlineDuration, nameof(onlineDuration));
            OnlineDuration = onlineDuration;
        }

        public string CrossplatformId { get; }
        public TimeSpan OnlineDuration { get; }
    }

    public sealed class StartVoteRequest
    {
        public StartVoteRequest(
            string roundId,
            VoteKind kind,
            string initiatorCrossplatformId,
            string? targetCrossplatformId,
            IReadOnlyList<VoteEligiblePlayer> eligiblePlayers,
            string idempotencyKey,
            string? correlationId,
            DateTimeOffset openedAtUtc)
        {
            RoundId = VoteValidation.RequireText(roundId, nameof(roundId));
            VoteValidation.RequireDefined(kind, nameof(kind));
            Kind = kind;
            InitiatorCrossplatformId = VoteValidation.RequireText(
                initiatorCrossplatformId,
                nameof(initiatorCrossplatformId));
            TargetCrossplatformId = string.IsNullOrWhiteSpace(targetCrossplatformId)
                ? null
                : targetCrossplatformId!.Trim();
            EligiblePlayers = (eligiblePlayers ?? throw new ArgumentNullException(nameof(eligiblePlayers)))
                .ToArray();
            IdempotencyKey = VoteValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId!.Trim();
            OpenedAtUtc = VoteValidation.RequireUtc(openedAtUtc, nameof(openedAtUtc));
        }

        public string RoundId { get; }
        public VoteKind Kind { get; }
        public string InitiatorCrossplatformId { get; }
        public string? TargetCrossplatformId { get; }
        public IReadOnlyList<VoteEligiblePlayer> EligiblePlayers { get; }
        public string IdempotencyKey { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset OpenedAtUtc { get; }
    }

    public sealed class VoteRoundDraft
    {
        public VoteRoundDraft(
            StartVoteRequest request,
            VoteConfiguration configuration,
            IReadOnlyList<string> eligibleCrossplatformIds)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            EligibleCrossplatformIds = (eligibleCrossplatformIds ??
                throw new ArgumentNullException(nameof(eligibleCrossplatformIds))).ToArray();
        }

        public StartVoteRequest Request { get; }
        public VoteConfiguration Configuration { get; }
        public IReadOnlyList<string> EligibleCrossplatformIds { get; }
        public DateTimeOffset ExpiresAtUtc => Request.OpenedAtUtc.Add(Configuration.Duration);
    }

    public sealed class VoteRoundSnapshot
    {
        public VoteRoundSnapshot(
            string roundId,
            string configurationId,
            VoteKind kind,
            VoteRoundState state,
            string initiatorCrossplatformId,
            string? targetCrossplatformId,
            string scopeKey,
            int eligibleCount,
            int thresholdPercent,
            int minimumParticipants,
            bool allowVoteChange,
            string idempotencyKey,
            string? actionJobId,
            string? actionOperationId,
            string? correlationId,
            DateTimeOffset openedAtUtc,
            DateTimeOffset expiresAtUtc,
            DateTimeOffset? settledAtUtc,
            DateTimeOffset? actionCompletedAtUtc,
            long rowVersion)
        {
            RoundId = roundId;
            ConfigurationId = configurationId;
            Kind = kind;
            State = state;
            InitiatorCrossplatformId = initiatorCrossplatformId;
            TargetCrossplatformId = targetCrossplatformId;
            ScopeKey = scopeKey;
            EligibleCount = eligibleCount;
            ThresholdPercent = thresholdPercent;
            MinimumParticipants = minimumParticipants;
            AllowVoteChange = allowVoteChange;
            IdempotencyKey = idempotencyKey;
            ActionJobId = actionJobId;
            ActionOperationId = actionOperationId;
            CorrelationId = correlationId;
            OpenedAtUtc = openedAtUtc;
            ExpiresAtUtc = expiresAtUtc;
            SettledAtUtc = settledAtUtc;
            ActionCompletedAtUtc = actionCompletedAtUtc;
            RowVersion = rowVersion;
        }

        public string RoundId { get; }
        public string ConfigurationId { get; }
        public VoteKind Kind { get; }
        public VoteRoundState State { get; }
        public string InitiatorCrossplatformId { get; }
        public string? TargetCrossplatformId { get; }
        public string ScopeKey { get; }
        public int EligibleCount { get; }
        public int ThresholdPercent { get; }
        public int MinimumParticipants { get; }
        public bool AllowVoteChange { get; }
        public string IdempotencyKey { get; }
        public string? ActionJobId { get; }
        public string? ActionOperationId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset OpenedAtUtc { get; }
        public DateTimeOffset ExpiresAtUtc { get; }
        public DateTimeOffset? SettledAtUtc { get; }
        public DateTimeOffset? ActionCompletedAtUtc { get; }
        public long RowVersion { get; }
    }

    public enum VoteStartStatus
    {
        Started,
        Replayed,
        Disabled,
        InvalidTarget,
        InitiatorIneligible,
        TargetIneligible,
        InsufficientEligiblePlayers,
        ScopeBusy,
        InitiatorCooldown,
        TargetCooldown,
        GlobalCooldown
    }

    public sealed class VoteStartResult
    {
        public VoteStartResult(VoteStartStatus status, VoteRoundSnapshot? round)
        {
            Status = status;
            Round = round;
        }

        public VoteStartStatus Status { get; }
        public VoteRoundSnapshot? Round { get; }
    }

    public enum VoteCastStatus
    {
        Accepted,
        Replayed,
        Changed,
        RoundNotFound,
        NoOpenRound,
        NotEligible,
        RoundClosed,
        VotingExpired,
        ChangeNotAllowed
    }

    public sealed class VoteCastResult
    {
        public VoteCastResult(VoteCastStatus status, VoteRoundSnapshot? round)
        {
            Status = status;
            Round = round;
        }

        public VoteCastStatus Status { get; }
        public VoteRoundSnapshot? Round { get; }
    }

    public enum VoteSettlementStatus
    {
        NotDue,
        Settled,
        AlreadySettled
    }

    public sealed class VoteSettlementResult
    {
        public VoteSettlementResult(
            VoteSettlementStatus status,
            VoteRoundSnapshot round,
            int participantCount,
            int yesCount,
            int noCount,
            bool wasSettled)
        {
            Status = status;
            Round = round ?? throw new ArgumentNullException(nameof(round));
            ParticipantCount = participantCount;
            YesCount = yesCount;
            NoCount = noCount;
            WasSettled = wasSettled;
        }

        public VoteSettlementStatus Status { get; }
        public VoteRoundSnapshot Round { get; }
        public int ParticipantCount { get; }
        public int YesCount { get; }
        public int NoCount { get; }
        public bool WasSettled { get; }
    }

    public interface IVoteStore
    {
        VoteConfiguration? GetConfiguration(VoteKind kind);
        VoteConfiguration SaveConfiguration(VoteConfiguration configuration);
        VoteStartResult TryStart(VoteRoundDraft draft);
        VoteRoundSnapshot GetRound(string roundId);
        VoteRoundSnapshot? FindOpenRound(VoteKind kind, string crossplatformId);
        VoteCastResult Cast(
            string roundId,
            string crossplatformId,
            VoteChoice choice,
            DateTimeOffset castAtUtc);
        VoteSettlementResult TrySettle(string roundId, DateTimeOffset settledAtUtc);
        bool TryQueueAction(string roundId, long expectedRowVersion, DateTimeOffset queuedAtUtc);
        bool TryCompleteAction(
            string roundId,
            long expectedRowVersion,
            VoteRoundState resultState,
            string? actionJobId,
            string? actionOperationId,
            DateTimeOffset completedAtUtc);
        IReadOnlyList<VoteRoundSnapshot> ListRounds();
        IReadOnlyList<VoteRoundSnapshot> ListActionQueued();
    }

    public interface IExpiringVoteRoundReader
    {
        IReadOnlyList<VoteRoundSnapshot> ListDueOpenRounds(DateTimeOffset dueAtUtc);
    }

    public sealed class StartVoteUseCase
    {
        private readonly IVoteStore store;

        public StartVoteUseCase(IVoteStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public VoteStartResult Execute(StartVoteRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var configuration = store.GetConfiguration(request.Kind);
            if (configuration == null || !configuration.Enabled)
                return new VoteStartResult(VoteStartStatus.Disabled, null);

            if ((request.Kind == VoteKind.Kick &&
                 (request.TargetCrossplatformId == null || string.Equals(
                     request.TargetCrossplatformId,
                     request.InitiatorCrossplatformId,
                     StringComparison.Ordinal))) ||
                (request.Kind == VoteKind.Restart && request.TargetCrossplatformId != null))
            {
                return new VoteStartResult(VoteStartStatus.InvalidTarget, null);
            }

            var candidates = request.EligiblePlayers
                .GroupBy(player => player.CrossplatformId, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(player => player.OnlineDuration).First())
                .ToArray();
            var initiator = candidates.FirstOrDefault(player => string.Equals(
                player.CrossplatformId,
                request.InitiatorCrossplatformId,
                StringComparison.Ordinal));
            if (initiator == null || initiator.OnlineDuration < configuration.InitiatorMinimumOnline)
                return new VoteStartResult(VoteStartStatus.InitiatorIneligible, null);

            var eligible = candidates
                .Where(player => player.OnlineDuration >= configuration.ParticipantMinimumOnline)
                .Select(player => player.CrossplatformId)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (eligible.Length < configuration.MinimumParticipants)
                return new VoteStartResult(VoteStartStatus.InsufficientEligiblePlayers, null);
            if (request.TargetCrossplatformId != null && !eligible.Contains(
                    request.TargetCrossplatformId,
                    StringComparer.Ordinal))
            {
                return new VoteStartResult(VoteStartStatus.TargetIneligible, null);
            }

            return store.TryStart(new VoteRoundDraft(request, configuration, eligible));
        }
    }

    public sealed class CastVoteUseCase
    {
        private readonly IVoteStore store;

        public CastVoteUseCase(IVoteStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public VoteCastResult Execute(
            string roundId,
            string crossplatformId,
            VoteChoice choice,
            DateTimeOffset castAtUtc)
        {
            VoteValidation.RequireDefined(choice, nameof(choice));
            return store.Cast(
                VoteValidation.RequireText(roundId, nameof(roundId)),
                VoteValidation.RequireText(crossplatformId, nameof(crossplatformId)),
                choice,
                VoteValidation.RequireUtc(castAtUtc, nameof(castAtUtc)));
        }

        public VoteCastResult ExecuteActive(
            VoteKind kind,
            string crossplatformId,
            VoteChoice choice,
            DateTimeOffset castAtUtc)
        {
            VoteValidation.RequireDefined(kind, nameof(kind));
            crossplatformId = VoteValidation.RequireText(crossplatformId, nameof(crossplatformId));
            var round = store.FindOpenRound(kind, crossplatformId);
            return round == null
                ? new VoteCastResult(VoteCastStatus.NoOpenRound, null)
                : Execute(round.RoundId, crossplatformId, choice, castAtUtc);
        }
    }

    public sealed class SettleVoteUseCase
    {
        private readonly IVoteStore store;

        public SettleVoteUseCase(IVoteStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public VoteSettlementResult Execute(string roundId, DateTimeOffset settledAtUtc) =>
            store.TrySettle(
                VoteValidation.RequireText(roundId, nameof(roundId)),
                VoteValidation.RequireUtc(settledAtUtc, nameof(settledAtUtc)));
    }

    public enum VoteActionExecutionStatus
    {
        Succeeded,
        Failed,
        ResultUnknown
    }

    public sealed class VoteActionCommand
    {
        public VoteActionCommand(
            string roundId,
            VoteKind kind,
            string? targetCrossplatformId,
            string? correlationId,
            DateTimeOffset requestedAtUtc)
        {
            RoundId = VoteValidation.RequireText(roundId, nameof(roundId));
            VoteValidation.RequireDefined(kind, nameof(kind));
            Kind = kind;
            TargetCrossplatformId = targetCrossplatformId;
            CorrelationId = correlationId;
            RequestedAtUtc = VoteValidation.RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
        }

        public string RoundId { get; }
        public VoteKind Kind { get; }
        public string? TargetCrossplatformId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset RequestedAtUtc { get; }
    }

    public sealed class VoteActionResult
    {
        private VoteActionResult(
            VoteActionExecutionStatus status,
            string? errorCode,
            string? actionOperationId,
            string? actionJobId)
        {
            Status = status;
            ErrorCode = errorCode;
            ActionOperationId = actionOperationId;
            ActionJobId = actionJobId;
        }

        public VoteActionExecutionStatus Status { get; }
        public string? ErrorCode { get; }
        public string? ActionOperationId { get; }
        public string? ActionJobId { get; }

        public static VoteActionResult Succeeded(string? actionOperationId, string? actionJobId) =>
            new VoteActionResult(
                VoteActionExecutionStatus.Succeeded,
                null,
                actionOperationId,
                actionJobId);

        public static VoteActionResult Failed(string errorCode) =>
            new VoteActionResult(
                VoteActionExecutionStatus.Failed,
                VoteValidation.RequireText(errorCode, nameof(errorCode)),
                null,
                null);

        public static VoteActionResult ResultUnknown(string errorCode) =>
            new VoteActionResult(
                VoteActionExecutionStatus.ResultUnknown,
                VoteValidation.RequireText(errorCode, nameof(errorCode)),
                null,
                null);
    }

    public interface ICommunityVoteActionPort
    {
        Task<VoteActionResult> ExecuteAsync(
            VoteActionCommand command,
            CancellationToken cancellationToken);
    }

    public enum VoteActionDispatchStatus
    {
        Dispatched,
        NotPassed,
        AlreadyQueuedOrCompleted
    }

    public sealed class VoteActionDispatchResult
    {
        public VoteActionDispatchResult(VoteActionDispatchStatus status, VoteRoundSnapshot round)
        {
            Status = status;
            Round = round ?? throw new ArgumentNullException(nameof(round));
        }

        public VoteActionDispatchStatus Status { get; }
        public VoteRoundSnapshot Round { get; }
    }

    public sealed class DispatchVoteActionUseCase
    {
        private readonly IVoteStore store;
        private readonly ICommunityVoteActionPort actionPort;

        public DispatchVoteActionUseCase(IVoteStore store, ICommunityVoteActionPort actionPort)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.actionPort = actionPort ?? throw new ArgumentNullException(nameof(actionPort));
        }

        public async Task<VoteActionDispatchResult> ExecuteAsync(
            string roundId,
            DateTimeOffset requestedAtUtc,
            CancellationToken cancellationToken)
        {
            roundId = VoteValidation.RequireText(roundId, nameof(roundId));
            requestedAtUtc = VoteValidation.RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
            var round = store.GetRound(roundId);
            if (round.State != VoteRoundState.Passed)
            {
                var status = round.State == VoteRoundState.ActionQueued ||
                    round.State == VoteRoundState.ActionSucceeded ||
                    round.State == VoteRoundState.ActionFailed ||
                    round.State == VoteRoundState.ActionResultUnknown
                    ? VoteActionDispatchStatus.AlreadyQueuedOrCompleted
                    : VoteActionDispatchStatus.NotPassed;
                return new VoteActionDispatchResult(status, round);
            }

            if (!store.TryQueueAction(roundId, round.RowVersion, requestedAtUtc))
            {
                return new VoteActionDispatchResult(
                    VoteActionDispatchStatus.AlreadyQueuedOrCompleted,
                    store.GetRound(roundId));
            }

            var queued = store.GetRound(roundId);
            VoteActionResult result;
            try
            {
                result = await actionPort.ExecuteAsync(
                        new VoteActionCommand(
                            queued.RoundId,
                            queued.Kind,
                            queued.TargetCrossplatformId,
                            queued.CorrelationId,
                            requestedAtUtc),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                result = VoteActionResult.ResultUnknown("vote_action_exception");
            }

            var next = result.Status == VoteActionExecutionStatus.Succeeded
                ? VoteRoundState.ActionSucceeded
                : result.Status == VoteActionExecutionStatus.Failed
                    ? VoteRoundState.ActionFailed
                    : VoteRoundState.ActionResultUnknown;
            if (!store.TryCompleteAction(
                    roundId,
                    queued.RowVersion,
                    next,
                    result.ActionJobId,
                    result.ActionOperationId,
                    requestedAtUtc))
            {
                return new VoteActionDispatchResult(
                    VoteActionDispatchStatus.AlreadyQueuedOrCompleted,
                    store.GetRound(roundId));
            }

            return new VoteActionDispatchResult(
                VoteActionDispatchStatus.Dispatched,
                store.GetRound(roundId));
        }
    }

    public sealed class RecoverQueuedVoteActionsUseCase
    {
        private readonly IVoteStore store;

        public RecoverQueuedVoteActionsUseCase(IVoteStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public int Execute(DateTimeOffset recoveredAtUtc)
        {
            recoveredAtUtc = VoteValidation.RequireUtc(recoveredAtUtc, nameof(recoveredAtUtc));
            var recovered = 0;
            foreach (var round in store.ListActionQueued())
            {
                if (store.TryCompleteAction(
                        round.RoundId,
                        round.RowVersion,
                        VoteRoundState.ActionResultUnknown,
                        null,
                        null,
                        recoveredAtUtc))
                {
                    recovered++;
                }
            }

            return recovered;
        }
    }

    public sealed class VoteRoundNotFoundException : Exception
    {
        public VoteRoundNotFoundException()
            : base("vote_round_not_found")
        {
        }
    }

    public sealed class VoteIdempotencyConflictException : Exception
    {
        public VoteIdempotencyConflictException()
            : base("vote_idempotency_conflict")
        {
        }
    }

    internal static class VoteValidation
    {
        public static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        public static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
            return value;
        }

        public static void RequireNonNegative(TimeSpan value, string parameterName)
        {
            if (value < TimeSpan.Zero) throw new ArgumentOutOfRangeException(parameterName);
        }

        public static void RequireDefined<T>(T value, string parameterName) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
