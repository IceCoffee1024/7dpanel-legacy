using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public interface IPlayerActionRecoveryStore
    {
        string OperationType { get; }

        IReadOnlyList<PlayerActionRecoveryRecord> ReadRecoverable();

        bool TryComplete(PlayerActionRecoveryCompletion completion);
    }

    public sealed class PlayerActionRecoveryRecord
    {
        public PlayerActionRecoveryRecord(
            PlayerActionOperation operation,
            PlayerActionRecoveryTerminal? committedTerminal)
        {
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            CommittedTerminal = committedTerminal;
        }

        public PlayerActionOperation Operation { get; }
        public PlayerActionRecoveryTerminal? CommittedTerminal { get; }
    }

    public sealed class PlayerActionRecoveryTerminal
    {
        public PlayerActionRecoveryTerminal(
            PlayerActionStatus status,
            DateTimeOffset completedAtUtc,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId,
            long? beforeSkillSnapshotId,
            long? afterSkillSnapshotId)
        {
            RequireTerminal(status, nameof(status));
            Status = status;
            CompletedAtUtc = PlayerEvidenceValidation.RequireUtc(completedAtUtc, nameof(completedAtUtc));
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            BeforeInventorySnapshotId = RequireOptionalId(
                beforeInventorySnapshotId,
                nameof(beforeInventorySnapshotId));
            AfterInventorySnapshotId = RequireOptionalId(
                afterInventorySnapshotId,
                nameof(afterInventorySnapshotId));
            BeforeSkillSnapshotId = RequireOptionalId(
                beforeSkillSnapshotId,
                nameof(beforeSkillSnapshotId));
            AfterSkillSnapshotId = RequireOptionalId(
                afterSkillSnapshotId,
                nameof(afterSkillSnapshotId));
        }

        public PlayerActionStatus Status { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
        public long? BeforeSkillSnapshotId { get; }
        public long? AfterSkillSnapshotId { get; }

        internal PlayerActionRecoveryCompletion ToCompletion(string operationId) =>
            new PlayerActionRecoveryCompletion(
                operationId,
                Status,
                CompletedAtUtc,
                FailureCode,
                BeforeInventorySnapshotId,
                AfterInventorySnapshotId,
                BeforeSkillSnapshotId,
                AfterSkillSnapshotId);

        internal static void RequireTerminal(PlayerActionStatus status, string parameterName)
        {
            if (status == PlayerActionStatus.Pending ||
                !Enum.IsDefined(typeof(PlayerActionStatus), status))
            {
                throw new ArgumentException("A terminal player action status is required.", parameterName);
            }
        }

        internal static long? RequireOptionalId(long? value, string parameterName)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    public sealed class PlayerActionRecoveryCompletion
    {
        public PlayerActionRecoveryCompletion(
            string operationId,
            PlayerActionStatus status,
            DateTimeOffset completedAtUtc,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId,
            long? beforeSkillSnapshotId,
            long? afterSkillSnapshotId)
        {
            PlayerActionRecoveryTerminal.RequireTerminal(status, nameof(status));
            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            Status = status;
            CompletedAtUtc = PlayerEvidenceValidation.RequireUtc(completedAtUtc, nameof(completedAtUtc));
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            BeforeInventorySnapshotId = PlayerActionRecoveryTerminal.RequireOptionalId(
                beforeInventorySnapshotId,
                nameof(beforeInventorySnapshotId));
            AfterInventorySnapshotId = PlayerActionRecoveryTerminal.RequireOptionalId(
                afterInventorySnapshotId,
                nameof(afterInventorySnapshotId));
            BeforeSkillSnapshotId = PlayerActionRecoveryTerminal.RequireOptionalId(
                beforeSkillSnapshotId,
                nameof(beforeSkillSnapshotId));
            AfterSkillSnapshotId = PlayerActionRecoveryTerminal.RequireOptionalId(
                afterSkillSnapshotId,
                nameof(afterSkillSnapshotId));
        }

        public string OperationId { get; }
        public PlayerActionStatus Status { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
        public long? BeforeSkillSnapshotId { get; }
        public long? AfterSkillSnapshotId { get; }
        public bool ManualVerificationRequired => Status == PlayerActionStatus.ResultUnknown;
    }

    public sealed class PlayerActionRecoveryReport
    {
        internal PlayerActionRecoveryReport(
            int attemptedCompletionCount,
            int persistedCompletionCount,
            IEnumerable<string> manualVerificationOperationIds,
            IEnumerable<string> unpersistedOperationIds)
        {
            AttemptedCompletionCount = attemptedCompletionCount;
            PersistedCompletionCount = persistedCompletionCount;
            ManualVerificationOperationIds = Copy(manualVerificationOperationIds);
            UnpersistedOperationIds = Copy(unpersistedOperationIds);
        }

        public int AttemptedCompletionCount { get; }
        public int PersistedCompletionCount { get; }
        public IReadOnlyList<string> ManualVerificationOperationIds { get; }
        public IReadOnlyList<string> UnpersistedOperationIds { get; }

        private static IReadOnlyList<string> Copy(IEnumerable<string> values) =>
            new ReadOnlyCollection<string>((values ?? throw new ArgumentNullException(nameof(values))).ToArray());
    }

    public sealed class PlayerActionRecoveryService
    {
        private static readonly TimeSpan DefaultStalePendingAge = TimeSpan.FromMinutes(5);
        private readonly IPlayerActionRecoveryStore[] stores;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly TimeSpan stalePendingAge;

        public PlayerActionRecoveryService(
            IPlayerActionRecoveryStore grantItemStore,
            IPlayerActionRecoveryStore removeItemStore,
            IPlayerActionRecoveryStore resetSkillsStore,
            IPlayerActionRecoveryStore clearInventoryStore,
            IPlayerActionRecoveryStore resetPlayerDataStore)
            : this(
                grantItemStore,
                removeItemStore,
                resetSkillsStore,
                clearInventoryStore,
                resetPlayerDataStore,
                () => DateTimeOffset.UtcNow,
                DefaultStalePendingAge)
        {
        }

        internal PlayerActionRecoveryService(
            IPlayerActionRecoveryStore grantItemStore,
            IPlayerActionRecoveryStore removeItemStore,
            IPlayerActionRecoveryStore resetSkillsStore,
            IPlayerActionRecoveryStore clearInventoryStore,
            IPlayerActionRecoveryStore resetPlayerDataStore,
            Func<DateTimeOffset> utcClock,
            TimeSpan stalePendingAge)
        {
            stores = new[]
            {
                RequireStore(grantItemStore, PlayerActionOperationTypes.GrantItem, nameof(grantItemStore)),
                RequireStore(removeItemStore, PlayerActionOperationTypes.RemoveItem, nameof(removeItemStore)),
                RequireStore(resetSkillsStore, PlayerActionOperationTypes.ResetSkills, nameof(resetSkillsStore)),
                RequireStore(clearInventoryStore, PlayerActionOperationTypes.ClearInventory, nameof(clearInventoryStore)),
                RequireStore(resetPlayerDataStore, PlayerActionOperationTypes.ResetPlayerData, nameof(resetPlayerDataStore))
            };
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            if (stalePendingAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(stalePendingAge));
            this.stalePendingAge = stalePendingAge;
        }

        public PlayerActionRecoveryReport Recover()
        {
            var now = PlayerEvidenceValidation.RequireUtc(utcClock(), "utcNow");
            var attempted = 0;
            var persisted = 0;
            var manual = new List<string>();
            var unpersisted = new List<string>();

            foreach (var store in stores)
            {
                var records = store.ReadRecoverable() ??
                    throw new InvalidOperationException("A recovery store returned no records.");
                foreach (var record in records)
                {
                    if (record == null)
                        throw new InvalidOperationException("A recovery store returned a null record.");
                    var operation = record.Operation;
                    if (!string.Equals(operation.OperationType, store.OperationType, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "A recovery store returned a different player action type.");
                    }
                    if (operation.Status != PlayerActionStatus.Pending)
                        continue;

                    var completion = ResolveCompletion(record, now);
                    if (completion == null)
                        continue;

                    attempted++;
                    if (completion.ManualVerificationRequired)
                        manual.Add(completion.OperationId);
                    var wasPersisted = false;
                    try { wasPersisted = store.TryComplete(completion); }
                    catch { }
                    if (wasPersisted)
                        persisted++;
                    else
                        unpersisted.Add(completion.OperationId);
                }
            }

            return new PlayerActionRecoveryReport(attempted, persisted, manual, unpersisted);
        }

        private PlayerActionRecoveryCompletion? ResolveCompletion(
            PlayerActionRecoveryRecord record,
            DateTimeOffset now)
        {
            if (record.CommittedTerminal != null)
                return record.CommittedTerminal.ToCompletion(record.Operation.OperationId);
            if (record.Operation.StartedAtUtc.HasValue)
            {
                return Completion(
                    record.Operation,
                    PlayerActionStatus.ResultUnknown,
                    now,
                    "process_interrupted_after_start");
            }

            var age = now - record.Operation.CreatedAtUtc;
            return age >= stalePendingAge
                ? Completion(
                    record.Operation,
                    PlayerActionStatus.Cancelled,
                    now,
                    "stale_pending_not_started")
                : null;
        }

        private static PlayerActionRecoveryCompletion Completion(
            PlayerActionOperation operation,
            PlayerActionStatus status,
            DateTimeOffset completedAtUtc,
            string failureCode) =>
            new PlayerActionRecoveryCompletion(
                operation.OperationId,
                status,
                completedAtUtc,
                failureCode,
                operation.BeforeInventorySnapshotId,
                operation.AfterInventorySnapshotId,
                operation.BeforeSkillSnapshotId,
                operation.AfterSkillSnapshotId);

        private static IPlayerActionRecoveryStore RequireStore(
            IPlayerActionRecoveryStore? store,
            string operationType,
            string parameterName)
        {
            if (store == null) throw new ArgumentNullException(parameterName);
            if (!string.Equals(store.OperationType, operationType, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The recovery store has a different fixed player action type.",
                    parameterName);
            }
            return store;
        }
    }
}
