using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;
using static LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players.PlayerPartialResetNativeHelpers;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class SevenDaysResetPlayerDataGateway : IResetPlayerDataGateway
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan TargetFreshness = TimeSpan.FromSeconds(30);
        private static long snapshotIdSequence = DateTimeOffset.UtcNow.Ticks;

        private readonly Func<string, Func<object>, TimeSpan, CancellationToken, Task<object>> dispatcher;
        private readonly Func<PlayerTargetStamp, ResetPlayerDataNativeSnapshotResult> capture;
        private readonly Func<PlayerTargetStamp, ResetPlayerDataNativeExecutionResult> reset;
        private readonly IPlayerEvidenceStore evidenceStore;
        private readonly Func<long> snapshotIdFactory;

        public SevenDaysResetPlayerDataGateway(
            SevenDaysPlayerEvidenceSnapshotReader snapshotReader,
            IPlayerEvidenceStore evidenceStore)
        {
            if (snapshotReader == null) throw new ArgumentNullException(nameof(snapshotReader));
            this.evidenceStore = evidenceStore ?? throw new ArgumentNullException(nameof(evidenceStore));
            dispatcher = (name, action, timeout, cancellationToken) =>
                GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken);
            capture = target => CaptureCurrent(snapshotReader, target);
            reset = ResetCurrent;
            snapshotIdFactory = NextSnapshotId;
        }

        internal SevenDaysResetPlayerDataGateway(
            Func<string, Func<object>, TimeSpan, CancellationToken, Task<object>> dispatcher,
            Func<PlayerTargetStamp, ResetPlayerDataNativeSnapshotResult> capture,
            Func<PlayerTargetStamp, ResetPlayerDataNativeExecutionResult> reset,
            IPlayerEvidenceStore evidenceStore,
            Func<long> snapshotIdFactory)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.reset = reset ?? throw new ArgumentNullException(nameof(reset));
            this.evidenceStore = evidenceStore ?? throw new ArgumentNullException(nameof(evidenceStore));
            this.snapshotIdFactory = snapshotIdFactory ?? throw new ArgumentNullException(nameof(snapshotIdFactory));
        }

        public async Task<ResetPlayerDataPreparationResult> PrepareAsync(
            PlayerTargetStamp target,
            CancellationToken cancellationToken)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            ResetPlayerDataNativeSnapshotResult native;
            try
            {
                native = (ResetPlayerDataNativeSnapshotResult)await dispatcher(
                        "7DPanel.Players.ResetPlayerData.Prepare",
                        () => capture(target),
                        DispatchTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return ResetPlayerDataPreparationResult.Failed("complete_before_snapshot_unavailable");
            }

            if (native.Status == ResetPlayerDataNativeSnapshotStatus.Rejected)
                return ResetPlayerDataPreparationResult.Rejected(native.FailureCode!);
            if (native.Status == ResetPlayerDataNativeSnapshotStatus.Failed || native.Snapshot == null)
            {
                return ResetPlayerDataPreparationResult.Failed(
                    native.FailureCode ?? "complete_before_snapshot_unavailable");
            }

            try
            {
                var inventorySnapshotId = RequireSnapshotId(snapshotIdFactory());
                var skillSnapshotId = RequireSnapshotId(snapshotIdFactory());
                evidenceStore.AppendInventorySnapshot(
                    native.Snapshot.Inventory.ToSnapshot(inventorySnapshotId));
                evidenceStore.AppendSkillSnapshot(
                    native.Snapshot.Skills.ToSnapshot(skillSnapshotId));
                return ResetPlayerDataPreparationResult.Ready(
                    inventorySnapshotId,
                    skillSnapshotId);
            }
            catch
            {
                return ResetPlayerDataPreparationResult.Failed("complete_before_snapshot_store_failed");
            }
        }

        public async Task<ResetPlayerDataGatewayResult> ExecuteAsync(
            PlayerTargetStamp target,
            CancellationToken cancellationToken)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            ResetPlayerDataNativeExecutionResult native;
            try
            {
                native = (ResetPlayerDataNativeExecutionResult)await dispatcher(
                        "7DPanel.Players.ResetPlayerData.Execute",
                        () => reset(target),
                        DispatchTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                return ResetPlayerDataGatewayResult.ResultUnknown(
                    "reset_player_data_dispatch_interrupted");
            }

            return native.Status switch
            {
                ResetPlayerDataNativeExecutionStatus.Succeeded =>
                    ResetPlayerDataGatewayResult.Succeeded(),
                ResetPlayerDataNativeExecutionStatus.Rejected =>
                    ResetPlayerDataGatewayResult.Rejected(native.FailureCode!),
                ResetPlayerDataNativeExecutionStatus.Failed =>
                    ResetPlayerDataGatewayResult.Failed(native.FailureCode!),
                ResetPlayerDataNativeExecutionStatus.ResultUnknown =>
                    ResetPlayerDataGatewayResult.ResultUnknown(native.FailureCode!),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        internal static ResetPlayerDataNativeExecutionResult ResolveAndReset(
            IReadOnlyCollection<ResetPlayerDataConnectionSnapshot> connections,
            PlayerTargetStamp target,
            string currentWorldId,
            DateTimeOffset nowUtc,
            Action<object, ResetPlayerDataNativeOptions> nativeReset)
        {
            if (nativeReset == null) throw new ArgumentNullException(nameof(nativeReset));
            var resolution = ResolveTarget(connections, target, currentWorldId, nowUtc);
            if (!resolution.IsMatched)
                return ResetPlayerDataNativeExecutionResult.Rejected(resolution.FailureCode!);
            try
            {
                nativeReset(resolution.Handle!, ResetPlayerDataNativeOptions.Complete);
                return ResetPlayerDataNativeExecutionResult.Succeeded();
            }
            catch
            {
                return ResetPlayerDataNativeExecutionResult.ResultUnknown(
                    "reset_player_data_native_interrupted");
            }
        }

        private static ResetPlayerDataNativeSnapshotResult CaptureCurrent(
            SevenDaysPlayerEvidenceSnapshotReader reader,
            PlayerTargetStamp target)
        {
            var resolution = ResolveTarget(
                CurrentConnections(),
                target,
                CurrentWorldId(),
                DateTimeOffset.UtcNow);
            if (!resolution.IsMatched)
                return ResetPlayerDataNativeSnapshotResult.Rejected(resolution.FailureCode!);
            try
            {
                var draft = CaptureDraft(reader, (global::ClientInfo)resolution.Handle!);
                if (draft.Position == null)
                    return ResetPlayerDataNativeSnapshotResult.Rejected("before_player_snapshot_unavailable");
                if (draft.Inventory == null)
                    return ResetPlayerDataNativeSnapshotResult.Rejected("before_inventory_snapshot_unavailable");
                if (draft.Skills == null)
                    return ResetPlayerDataNativeSnapshotResult.Rejected("before_skill_snapshot_unavailable");
                return ResetPlayerDataNativeSnapshotResult.Ready(
                    new ResetPlayerDataSnapshotScalar(
                        new ClearInventorySnapshotScalar(
                            draft.CrossplatformId,
                            draft.ServerId,
                            draft.WorldId,
                            draft.ObservedAtUtc,
                            draft.Inventory.GameVersion,
                            draft.Inventory.CatalogVersion,
                            draft.Inventory.CatalogResolution,
                            draft.Inventory.Fingerprint,
                            draft.Inventory.Items),
                        new ResetSkillsSnapshotScalar(
                            draft.CrossplatformId,
                            draft.ServerId,
                            draft.WorldId,
                            draft.ObservedAtUtc,
                            draft.Skills.GameVersion,
                            draft.Skills.Level,
                            draft.Skills.SkillPoints,
                            draft.Skills.Values)));
            }
            catch (PlayerPartialResetRejectedException exception)
            {
                return ResetPlayerDataNativeSnapshotResult.Rejected(exception.FailureCode);
            }
            catch
            {
                return ResetPlayerDataNativeSnapshotResult.Failed(
                    "complete_before_snapshot_unavailable");
            }
        }

        private static ResetPlayerDataNativeExecutionResult ResetCurrent(PlayerTargetStamp target) =>
            ResolveAndReset(
                CurrentConnections(),
                target,
                CurrentWorldId(),
                DateTimeOffset.UtcNow,
                ApplyCompleteReset);

        private static void ApplyCompleteReset(
            object handle,
            ResetPlayerDataNativeOptions options)
        {
            var entity = ResolveEntity((global::ClientInfo)handle);
            var action = new global::GameEvent.SequenceActions.ActionResetPlayerData
            {
                resetLevels = options.ResetLevels,
                resetSkills = options.ResetSkills,
                removeLandclaims = options.RemoveLandClaims,
                removeSleepingBag = options.RemoveSleepingBag,
                removeBooks = options.RemoveBooks,
                removeCrafting = options.RemoveCrafting,
                removeQuests = options.RemoveQuests,
                removeChallenges = options.RemoveChallenges,
                removeBackpack = options.RemoveBackpack,
                resetStats = options.ResetStats
            };
            action.OnServerPerform(entity);
        }

        private static IReadOnlyCollection<ResetPlayerDataConnectionSnapshot> CurrentConnections()
        {
            var clients = global::ConnectionManager.Instance?.Clients?.List;
            if (clients == null) return Array.Empty<ResetPlayerDataConnectionSnapshot>();
            return clients
                .Where(client => client != null)
                .Select(client => new ResetPlayerDataConnectionSnapshot(
                    client.entityId,
                    client.CrossplatformId?.CombinedString ?? string.Empty,
                    client))
                .ToArray();
        }

        private static ResetPlayerDataTargetResolution ResolveTarget(
            IReadOnlyCollection<ResetPlayerDataConnectionSnapshot> connections,
            PlayerTargetStamp target,
            string currentWorldId,
            DateTimeOffset nowUtc)
        {
            if (connections == null) throw new ArgumentNullException(nameof(connections));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!string.Equals(currentWorldId, target.WorldId, StringComparison.Ordinal))
                return ResetPlayerDataTargetResolution.Rejected("world_changed");
            var age = nowUtc - target.OnlineObservedAtUtc;
            if (age < TimeSpan.Zero || age > TargetFreshness)
                return ResetPlayerDataTargetResolution.Rejected("target_not_fresh");

            ResetPlayerDataConnectionSnapshot? matched = null;
            foreach (var connection in connections)
            {
                if (connection.EntityId != target.EntityId) continue;
                if (matched != null)
                    return ResetPlayerDataTargetResolution.Rejected("target_ambiguous");
                matched = connection;
            }
            if (matched == null)
                return ResetPlayerDataTargetResolution.Rejected("player_not_online");
            if (!string.Equals(matched.CrossplatformId, target.CrossplatformId, StringComparison.Ordinal))
                return ResetPlayerDataTargetResolution.Rejected("target_identity_changed");
            return ResetPlayerDataTargetResolution.Matched(matched.Handle);
        }

        private static long NextSnapshotId() => Interlocked.Increment(ref snapshotIdSequence);

        private static long RequireSnapshotId(long value)
        {
            if (value <= 0)
                throw new InvalidOperationException("The snapshot ID factory returned an invalid ID.");
            return value;
        }

        private sealed class ResetPlayerDataTargetResolution
        {
            private ResetPlayerDataTargetResolution(object? handle, string? failureCode)
            {
                Handle = handle;
                FailureCode = failureCode;
            }

            public object? Handle { get; }
            public string? FailureCode { get; }
            public bool IsMatched => Handle != null;

            public static ResetPlayerDataTargetResolution Matched(object handle) =>
                new ResetPlayerDataTargetResolution(
                    handle ?? throw new ArgumentNullException(nameof(handle)),
                    null);

            public static ResetPlayerDataTargetResolution Rejected(string failureCode) =>
                new ResetPlayerDataTargetResolution(
                    null,
                    RequireText(failureCode, nameof(failureCode)));
        }
    }

    internal sealed class ResetPlayerDataConnectionSnapshot
    {
        public ResetPlayerDataConnectionSnapshot(
            int entityId,
            string crossplatformId,
            object handle)
        {
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            EntityId = entityId;
            CrossplatformId = RequireText(
                crossplatformId,
                nameof(crossplatformId));
            Handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        public int EntityId { get; }
        public string CrossplatformId { get; }
        public object Handle { get; }
    }

    internal sealed class ResetPlayerDataNativeOptions
    {
        private ResetPlayerDataNativeOptions()
        {
        }

        public static ResetPlayerDataNativeOptions Complete { get; } =
            new ResetPlayerDataNativeOptions();

        public bool ResetLevels => true;
        public bool ResetSkills => true;
        public bool RemoveLandClaims => true;
        public bool RemoveSleepingBag => true;
        public bool RemoveBooks => true;
        public bool RemoveCrafting => true;
        public bool RemoveQuests => true;
        public bool RemoveChallenges => true;
        public bool RemoveBackpack => true;
        public bool ResetStats => true;
    }

    internal enum ResetPlayerDataNativeSnapshotStatus
    {
        Ready,
        Rejected,
        Failed
    }

    internal sealed class ResetPlayerDataNativeSnapshotResult
    {
        private ResetPlayerDataNativeSnapshotResult(
            ResetPlayerDataNativeSnapshotStatus status,
            ResetPlayerDataSnapshotScalar? snapshot,
            string? failureCode)
        {
            Status = status;
            Snapshot = snapshot;
            FailureCode = failureCode;
        }

        public ResetPlayerDataNativeSnapshotStatus Status { get; }
        public ResetPlayerDataSnapshotScalar? Snapshot { get; }
        public string? FailureCode { get; }

        public static ResetPlayerDataNativeSnapshotResult Ready(
            ResetPlayerDataSnapshotScalar snapshot) =>
            new ResetPlayerDataNativeSnapshotResult(
                ResetPlayerDataNativeSnapshotStatus.Ready,
                snapshot ?? throw new ArgumentNullException(nameof(snapshot)),
                null);

        public static ResetPlayerDataNativeSnapshotResult Rejected(string failureCode) =>
            Failure(ResetPlayerDataNativeSnapshotStatus.Rejected, failureCode);

        public static ResetPlayerDataNativeSnapshotResult Failed(string failureCode) =>
            Failure(ResetPlayerDataNativeSnapshotStatus.Failed, failureCode);

        private static ResetPlayerDataNativeSnapshotResult Failure(
            ResetPlayerDataNativeSnapshotStatus status,
            string failureCode) =>
            new ResetPlayerDataNativeSnapshotResult(
                status,
                null,
                RequireText(failureCode, nameof(failureCode)));
    }

    internal sealed class ResetPlayerDataSnapshotScalar
    {
        public ResetPlayerDataSnapshotScalar(
            ClearInventorySnapshotScalar inventory,
            ResetSkillsSnapshotScalar skills)
        {
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Skills = skills ?? throw new ArgumentNullException(nameof(skills));
        }

        public ClearInventorySnapshotScalar Inventory { get; }
        public ResetSkillsSnapshotScalar Skills { get; }
    }

    internal enum ResetPlayerDataNativeExecutionStatus
    {
        Succeeded,
        Rejected,
        Failed,
        ResultUnknown
    }

    internal sealed class ResetPlayerDataNativeExecutionResult
    {
        private ResetPlayerDataNativeExecutionResult(
            ResetPlayerDataNativeExecutionStatus status,
            string? failureCode)
        {
            Status = status;
            FailureCode = failureCode;
        }

        public ResetPlayerDataNativeExecutionStatus Status { get; }
        public string? FailureCode { get; }

        public static ResetPlayerDataNativeExecutionResult Succeeded() =>
            new ResetPlayerDataNativeExecutionResult(
                ResetPlayerDataNativeExecutionStatus.Succeeded,
                null);

        public static ResetPlayerDataNativeExecutionResult Rejected(string failureCode) =>
            Failure(ResetPlayerDataNativeExecutionStatus.Rejected, failureCode);

        public static ResetPlayerDataNativeExecutionResult Failed(string failureCode) =>
            Failure(ResetPlayerDataNativeExecutionStatus.Failed, failureCode);

        public static ResetPlayerDataNativeExecutionResult ResultUnknown(string failureCode) =>
            Failure(ResetPlayerDataNativeExecutionStatus.ResultUnknown, failureCode);

        private static ResetPlayerDataNativeExecutionResult Failure(
            ResetPlayerDataNativeExecutionStatus status,
            string failureCode) =>
            new ResetPlayerDataNativeExecutionResult(
                status,
                RequireText(failureCode, nameof(failureCode)));
    }
}
