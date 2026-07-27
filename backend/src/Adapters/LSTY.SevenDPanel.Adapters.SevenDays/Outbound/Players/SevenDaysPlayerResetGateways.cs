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
    public sealed class SevenDaysResetSkillsGateway : IResetSkillsGateway
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan TargetFreshness = TimeSpan.FromSeconds(30);
        private static long snapshotIdSequence = DateTimeOffset.UtcNow.Ticks;

        private readonly Func<string, Func<object>, TimeSpan, CancellationToken, Task<object>> dispatcher;
        private readonly Func<PlayerTargetStamp, ResetSkillsNativeSnapshotResult> capture;
        private readonly Func<PlayerTargetStamp, ResetSkillsNativeExecutionResult> reset;
        private readonly IPlayerEvidenceStore evidenceStore;
        private readonly Func<long> snapshotIdFactory;

        public SevenDaysResetSkillsGateway(
            SevenDaysPlayerEvidenceSnapshotReader snapshotReader,
            IPlayerEvidenceStore evidenceStore)
        {
            if (snapshotReader == null) throw new ArgumentNullException(nameof(snapshotReader));
            this.evidenceStore = evidenceStore ?? throw new ArgumentNullException(nameof(evidenceStore));
            dispatcher = (name, action, timeout, cancellationToken) =>
                GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken);
            capture = target => CaptureCurrentSkills(snapshotReader, target);
            reset = target => ResetCurrentSkills(snapshotReader, target);
            snapshotIdFactory = NextSnapshotId;
        }

        internal SevenDaysResetSkillsGateway(
            Func<string, Func<object>, TimeSpan, CancellationToken, Task<object>> dispatcher,
            Func<PlayerTargetStamp, ResetSkillsNativeSnapshotResult> capture,
            Func<PlayerTargetStamp, ResetSkillsNativeExecutionResult> reset,
            IPlayerEvidenceStore evidenceStore,
            Func<long> snapshotIdFactory)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.reset = reset ?? throw new ArgumentNullException(nameof(reset));
            this.evidenceStore = evidenceStore ?? throw new ArgumentNullException(nameof(evidenceStore));
            this.snapshotIdFactory = snapshotIdFactory ?? throw new ArgumentNullException(nameof(snapshotIdFactory));
        }

        public async Task<ResetSkillsPreparationResult> PrepareAsync(
            PlayerTargetStamp target,
            CancellationToken cancellationToken)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            ResetSkillsNativeSnapshotResult native;
            try
            {
                native = (ResetSkillsNativeSnapshotResult)await dispatcher(
                        "7DPanel.Players.ResetSkills.Prepare",
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
                return ResetSkillsPreparationResult.Failed("before_skill_snapshot_unavailable");
            }

            if (native.Status == ResetSkillsNativeSnapshotStatus.Rejected)
                return ResetSkillsPreparationResult.Rejected(native.FailureCode!);
            if (native.Status == ResetSkillsNativeSnapshotStatus.Failed || native.Snapshot == null)
                return ResetSkillsPreparationResult.Failed(native.FailureCode ?? "before_skill_snapshot_unavailable");

            try
            {
                var snapshotId = RequireSnapshotId(snapshotIdFactory());
                evidenceStore.AppendSkillSnapshot(native.Snapshot.ToSnapshot(snapshotId));
                return ResetSkillsPreparationResult.Ready(snapshotId);
            }
            catch
            {
                return ResetSkillsPreparationResult.Failed("before_skill_snapshot_store_failed");
            }
        }

        public async Task<ResetSkillsGatewayResult> ExecuteAsync(
            PlayerTargetStamp target,
            CancellationToken cancellationToken)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            ResetSkillsNativeExecutionResult native;
            try
            {
                native = (ResetSkillsNativeExecutionResult)await dispatcher(
                        "7DPanel.Players.ResetSkills.Execute",
                        () => reset(target),
                        DispatchTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                return ResetSkillsGatewayResult.ResultUnknown("reset_skills_dispatch_interrupted");
            }

            if (native.Status == ResetSkillsNativeExecutionStatus.Rejected)
                return ResetSkillsGatewayResult.Rejected(native.FailureCode!);
            if (native.Status == ResetSkillsNativeExecutionStatus.Failed)
                return ResetSkillsGatewayResult.Failed(native.FailureCode!);
            if (native.Status == ResetSkillsNativeExecutionStatus.ResultUnknown || native.Snapshot == null)
                return ResetSkillsGatewayResult.ResultUnknown(native.FailureCode ?? "reset_skills_result_unknown");

            try
            {
                var snapshotId = RequireSnapshotId(snapshotIdFactory());
                evidenceStore.AppendSkillSnapshot(native.Snapshot.ToSnapshot(snapshotId));
                return ResetSkillsGatewayResult.Succeeded(snapshotId);
            }
            catch
            {
                return ResetSkillsGatewayResult.ResultUnknown("after_skill_snapshot_store_failed");
            }
        }

        internal static ResetSkillsNativeExecutionResult ResolveAndReset(
            IReadOnlyCollection<ResetSkillsConnectionSnapshot> connections,
            PlayerTargetStamp target,
            string currentWorldId,
            DateTimeOffset nowUtc,
            Action<object> nativeProgressionReset,
            Func<object, ResetSkillsSnapshotScalar> captureAfter)
        {
            if (connections == null) throw new ArgumentNullException(nameof(connections));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (nativeProgressionReset == null) throw new ArgumentNullException(nameof(nativeProgressionReset));
            if (captureAfter == null) throw new ArgumentNullException(nameof(captureAfter));
            var resolution = ResolveResetSkillsTarget(connections, target, currentWorldId, nowUtc);
            if (!resolution.IsMatched)
                return ResetSkillsNativeExecutionResult.Rejected(resolution.FailureCode!);
            try
            {
                nativeProgressionReset(resolution.Handle!);
                return ResetSkillsNativeExecutionResult.Succeeded(captureAfter(resolution.Handle!));
            }
            catch (PlayerPartialResetRejectedException exception)
            {
                return ResetSkillsNativeExecutionResult.Rejected(exception.FailureCode);
            }
            catch
            {
                return ResetSkillsNativeExecutionResult.ResultUnknown("reset_skills_native_interrupted");
            }
        }

        private static ResetSkillsNativeSnapshotResult CaptureCurrentSkills(
            SevenDaysPlayerEvidenceSnapshotReader reader,
            PlayerTargetStamp target)
        {
            var resolution = ResolveResetSkillsTarget(
                CurrentResetSkillsConnections(),
                target,
                CurrentWorldId(),
                DateTimeOffset.UtcNow);
            if (!resolution.IsMatched)
                return ResetSkillsNativeSnapshotResult.Rejected(resolution.FailureCode!);
            try
            {
                return ResetSkillsNativeSnapshotResult.Ready(
                    CaptureSkillScalar(reader, resolution.Handle!));
            }
            catch (PlayerPartialResetRejectedException exception)
            {
                return ResetSkillsNativeSnapshotResult.Rejected(exception.FailureCode);
            }
            catch
            {
                return ResetSkillsNativeSnapshotResult.Failed("before_skill_snapshot_unavailable");
            }
        }

        private static ResetSkillsNativeExecutionResult ResetCurrentSkills(
            SevenDaysPlayerEvidenceSnapshotReader reader,
            PlayerTargetStamp target)
        {
            return ResolveAndReset(
                CurrentResetSkillsConnections(),
                target,
                CurrentWorldId(),
                DateTimeOffset.UtcNow,
                handle =>
                {
                    var entity = ResolveEntity((global::ClientInfo)handle);
                    if (entity.Progression == null)
                        throw new PlayerPartialResetRejectedException("progression_unavailable");
                    entity.Progression.ResetProgression(true, false, false);
                    entity.Progression.bProgressionStatsChanged = true;
                    entity.bPlayerStatsChanged = true;
                },
                handle => CaptureSkillScalar(reader, handle));
        }

        private static ResetSkillsSnapshotScalar CaptureSkillScalar(
            SevenDaysPlayerEvidenceSnapshotReader reader,
            object handle)
        {
            var client = (global::ClientInfo)handle;
            var draft = CaptureDraft(reader, client);
            if (draft.Skills == null)
                throw new PlayerPartialResetRejectedException("skill_snapshot_unavailable");
            return new ResetSkillsSnapshotScalar(
                draft.CrossplatformId,
                draft.ServerId,
                draft.WorldId,
                draft.ObservedAtUtc,
                draft.Skills.GameVersion,
                draft.Skills.Level,
                draft.Skills.SkillPoints,
                draft.Skills.Values);
        }

        private static IReadOnlyCollection<ResetSkillsConnectionSnapshot> CurrentResetSkillsConnections()
        {
            var clients = global::ConnectionManager.Instance?.Clients?.List;
            if (clients == null) return Array.Empty<ResetSkillsConnectionSnapshot>();
            return clients
                .Where(client => client != null)
                .Select(client => new ResetSkillsConnectionSnapshot(
                    client.entityId,
                    client.CrossplatformId?.CombinedString ?? string.Empty,
                    client))
                .ToArray();
        }

        private static ResetSkillsTargetResolution ResolveResetSkillsTarget(
            IReadOnlyCollection<ResetSkillsConnectionSnapshot> connections,
            PlayerTargetStamp target,
            string currentWorldId,
            DateTimeOffset nowUtc)
        {
            if (!string.Equals(currentWorldId, target.WorldId, StringComparison.Ordinal))
                return ResetSkillsTargetResolution.Rejected("world_changed");
            var age = nowUtc - target.OnlineObservedAtUtc;
            if (age < TimeSpan.Zero || age > TargetFreshness)
                return ResetSkillsTargetResolution.Rejected("target_not_fresh");
            ResetSkillsConnectionSnapshot? matched = null;
            foreach (var connection in connections)
            {
                if (connection.EntityId != target.EntityId) continue;
                if (matched != null) return ResetSkillsTargetResolution.Rejected("target_ambiguous");
                matched = connection;
            }
            if (matched == null) return ResetSkillsTargetResolution.Rejected("player_not_online");
            if (!string.Equals(matched.CrossplatformId, target.CrossplatformId, StringComparison.Ordinal))
                return ResetSkillsTargetResolution.Rejected("target_identity_changed");
            return ResetSkillsTargetResolution.Matched(matched.Handle);
        }

        private static long NextSnapshotId() => Interlocked.Increment(ref snapshotIdSequence);

        private static long RequireSnapshotId(long value)
        {
            if (value <= 0) throw new InvalidOperationException("The snapshot ID factory returned an invalid ID.");
            return value;
        }

        private sealed class ResetSkillsTargetResolution
        {
            private ResetSkillsTargetResolution(object? handle, string? failureCode)
            {
                Handle = handle;
                FailureCode = failureCode;
            }

            public object? Handle { get; }
            public string? FailureCode { get; }
            public bool IsMatched => Handle != null;
            public static ResetSkillsTargetResolution Matched(object handle) =>
                new ResetSkillsTargetResolution(handle ?? throw new ArgumentNullException(nameof(handle)), null);
            public static ResetSkillsTargetResolution Rejected(string failureCode) =>
                new ResetSkillsTargetResolution(null, failureCode);
        }

    }

    public sealed class SevenDaysClearInventoryGateway : IClearInventoryGateway
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan TargetFreshness = TimeSpan.FromSeconds(30);
        private static long snapshotIdSequence = DateTimeOffset.UtcNow.Ticks;

        private readonly Func<string, Func<object>, TimeSpan, CancellationToken, Task<object>> dispatcher;
        private readonly Func<PlayerTargetStamp, ClearInventoryNativeSnapshotResult> capture;
        private readonly Func<PlayerTargetStamp, ClearInventoryNativeExecutionResult> clearBag;
        private readonly IPlayerEvidenceStore evidenceStore;
        private readonly Func<long> snapshotIdFactory;

        public SevenDaysClearInventoryGateway(
            SevenDaysPlayerEvidenceSnapshotReader snapshotReader,
            IPlayerEvidenceStore evidenceStore)
        {
            if (snapshotReader == null) throw new ArgumentNullException(nameof(snapshotReader));
            this.evidenceStore = evidenceStore ?? throw new ArgumentNullException(nameof(evidenceStore));
            dispatcher = (name, action, timeout, cancellationToken) =>
                GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken);
            capture = target => CaptureCurrentInventory(snapshotReader, target);
            clearBag = target => ClearCurrentBag(snapshotReader, target);
            snapshotIdFactory = NextSnapshotId;
        }

        internal SevenDaysClearInventoryGateway(
            Func<string, Func<object>, TimeSpan, CancellationToken, Task<object>> dispatcher,
            Func<PlayerTargetStamp, ClearInventoryNativeSnapshotResult> capture,
            Func<PlayerTargetStamp, ClearInventoryNativeExecutionResult> clearBag,
            IPlayerEvidenceStore evidenceStore,
            Func<long> snapshotIdFactory)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.clearBag = clearBag ?? throw new ArgumentNullException(nameof(clearBag));
            this.evidenceStore = evidenceStore ?? throw new ArgumentNullException(nameof(evidenceStore));
            this.snapshotIdFactory = snapshotIdFactory ?? throw new ArgumentNullException(nameof(snapshotIdFactory));
        }

        public async Task<ClearInventoryPreparationResult> PrepareAsync(
            PlayerTargetStamp target,
            CancellationToken cancellationToken)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            ClearInventoryNativeSnapshotResult native;
            try
            {
                native = (ClearInventoryNativeSnapshotResult)await dispatcher(
                        "7DPanel.Players.ClearInventory.Prepare",
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
                return ClearInventoryPreparationResult.Failed("before_inventory_snapshot_unavailable");
            }

            if (native.Status == ClearInventoryNativeSnapshotStatus.Rejected)
                return ClearInventoryPreparationResult.Rejected(native.FailureCode!);
            if (native.Status == ClearInventoryNativeSnapshotStatus.Failed || native.Snapshot == null)
                return ClearInventoryPreparationResult.Failed(
                    native.FailureCode ?? "before_inventory_snapshot_unavailable");

            try
            {
                var snapshotId = RequireSnapshotId(snapshotIdFactory());
                evidenceStore.AppendInventorySnapshot(native.Snapshot.ToSnapshot(snapshotId));
                return ClearInventoryPreparationResult.Ready(snapshotId);
            }
            catch
            {
                return ClearInventoryPreparationResult.Failed("before_inventory_snapshot_store_failed");
            }
        }

        public async Task<ClearInventoryGatewayResult> ExecuteAsync(
            PlayerTargetStamp target,
            CancellationToken cancellationToken)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            ClearInventoryNativeExecutionResult native;
            try
            {
                native = (ClearInventoryNativeExecutionResult)await dispatcher(
                        "7DPanel.Players.ClearInventory.Execute",
                        () => clearBag(target),
                        DispatchTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                return ClearInventoryGatewayResult.ResultUnknown("clear_inventory_dispatch_interrupted");
            }

            if (native.Status == ClearInventoryNativeExecutionStatus.Rejected)
                return ClearInventoryGatewayResult.Rejected(native.FailureCode!);
            if (native.Status == ClearInventoryNativeExecutionStatus.Failed)
                return ClearInventoryGatewayResult.Failed(native.FailureCode!);
            if (native.Status == ClearInventoryNativeExecutionStatus.ResultUnknown || native.Snapshot == null)
                return ClearInventoryGatewayResult.ResultUnknown(
                    native.FailureCode ?? "clear_inventory_result_unknown");

            try
            {
                var snapshotId = RequireSnapshotId(snapshotIdFactory());
                evidenceStore.AppendInventorySnapshot(native.Snapshot.ToSnapshot(snapshotId));
                return ClearInventoryGatewayResult.Succeeded(snapshotId);
            }
            catch
            {
                return ClearInventoryGatewayResult.ResultUnknown("after_inventory_snapshot_store_failed");
            }
        }

        internal static ClearInventoryNativeExecutionResult ResolveAndClearBag(
            IReadOnlyCollection<ClearInventoryConnectionSnapshot> connections,
            PlayerTargetStamp target,
            string currentWorldId,
            DateTimeOffset nowUtc,
            Action<object> nativeBagClear,
            Func<object, ClearInventorySnapshotScalar> captureAfter)
        {
            if (connections == null) throw new ArgumentNullException(nameof(connections));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (nativeBagClear == null) throw new ArgumentNullException(nameof(nativeBagClear));
            if (captureAfter == null) throw new ArgumentNullException(nameof(captureAfter));
            var resolution = ResolveClearInventoryTarget(connections, target, currentWorldId, nowUtc);
            if (!resolution.IsMatched)
                return ClearInventoryNativeExecutionResult.Rejected(resolution.FailureCode!);
            try
            {
                nativeBagClear(resolution.Handle!);
                return ClearInventoryNativeExecutionResult.Succeeded(captureAfter(resolution.Handle!));
            }
            catch (PlayerPartialResetRejectedException exception)
            {
                return ClearInventoryNativeExecutionResult.Rejected(exception.FailureCode);
            }
            catch
            {
                return ClearInventoryNativeExecutionResult.ResultUnknown("clear_inventory_native_interrupted");
            }
        }

        private static ClearInventoryNativeSnapshotResult CaptureCurrentInventory(
            SevenDaysPlayerEvidenceSnapshotReader reader,
            PlayerTargetStamp target)
        {
            var resolution = ResolveClearInventoryTarget(
                CurrentClearInventoryConnections(),
                target,
                CurrentWorldId(),
                DateTimeOffset.UtcNow);
            if (!resolution.IsMatched)
                return ClearInventoryNativeSnapshotResult.Rejected(resolution.FailureCode!);
            try
            {
                return ClearInventoryNativeSnapshotResult.Ready(
                    CaptureInventoryScalar(reader, resolution.Handle!));
            }
            catch (PlayerPartialResetRejectedException exception)
            {
                return ClearInventoryNativeSnapshotResult.Rejected(exception.FailureCode);
            }
            catch
            {
                return ClearInventoryNativeSnapshotResult.Failed("before_inventory_snapshot_unavailable");
            }
        }

        private static ClearInventoryNativeExecutionResult ClearCurrentBag(
            SevenDaysPlayerEvidenceSnapshotReader reader,
            PlayerTargetStamp target)
        {
            return ResolveAndClearBag(
                CurrentClearInventoryConnections(),
                target,
                CurrentWorldId(),
                DateTimeOffset.UtcNow,
                handle =>
                {
                    var entity = ResolveEntity((global::ClientInfo)handle);
                    var bag = entity.bag;
                    var slots = bag?.GetSlots();
                    if (bag == null || slots == null)
                        throw new PlayerPartialResetRejectedException("bag_unavailable");
                    var emptySlots = new global::ItemStack[slots.Length];
                    for (var index = 0; index < emptySlots.Length; index++)
                        emptySlots[index] = global::ItemStack.Empty;
                    bag.SetSlots(emptySlots);
                    entity.bPlayerStatsChanged = true;
                },
                handle => CaptureInventoryScalar(reader, handle));
        }

        private static ClearInventorySnapshotScalar CaptureInventoryScalar(
            SevenDaysPlayerEvidenceSnapshotReader reader,
            object handle)
        {
            var client = (global::ClientInfo)handle;
            var draft = CaptureDraft(reader, client);
            if (draft.Inventory == null)
                throw new PlayerPartialResetRejectedException("inventory_snapshot_unavailable");
            return new ClearInventorySnapshotScalar(
                draft.CrossplatformId,
                draft.ServerId,
                draft.WorldId,
                draft.ObservedAtUtc,
                draft.Inventory.GameVersion,
                draft.Inventory.CatalogVersion,
                draft.Inventory.CatalogResolution,
                draft.Inventory.Fingerprint,
                draft.Inventory.Items);
        }

        private static IReadOnlyCollection<ClearInventoryConnectionSnapshot> CurrentClearInventoryConnections()
        {
            var clients = global::ConnectionManager.Instance?.Clients?.List;
            if (clients == null) return Array.Empty<ClearInventoryConnectionSnapshot>();
            return clients
                .Where(client => client != null)
                .Select(client => new ClearInventoryConnectionSnapshot(
                    client.entityId,
                    client.CrossplatformId?.CombinedString ?? string.Empty,
                    client))
                .ToArray();
        }

        private static ClearInventoryTargetResolution ResolveClearInventoryTarget(
            IReadOnlyCollection<ClearInventoryConnectionSnapshot> connections,
            PlayerTargetStamp target,
            string currentWorldId,
            DateTimeOffset nowUtc)
        {
            if (!string.Equals(currentWorldId, target.WorldId, StringComparison.Ordinal))
                return ClearInventoryTargetResolution.Rejected("world_changed");
            var age = nowUtc - target.OnlineObservedAtUtc;
            if (age < TimeSpan.Zero || age > TargetFreshness)
                return ClearInventoryTargetResolution.Rejected("target_not_fresh");
            ClearInventoryConnectionSnapshot? matched = null;
            foreach (var connection in connections)
            {
                if (connection.EntityId != target.EntityId) continue;
                if (matched != null) return ClearInventoryTargetResolution.Rejected("target_ambiguous");
                matched = connection;
            }
            if (matched == null) return ClearInventoryTargetResolution.Rejected("player_not_online");
            if (!string.Equals(matched.CrossplatformId, target.CrossplatformId, StringComparison.Ordinal))
                return ClearInventoryTargetResolution.Rejected("target_identity_changed");
            return ClearInventoryTargetResolution.Matched(matched.Handle);
        }

        private static long NextSnapshotId() => Interlocked.Increment(ref snapshotIdSequence);

        private static long RequireSnapshotId(long value)
        {
            if (value <= 0) throw new InvalidOperationException("The snapshot ID factory returned an invalid ID.");
            return value;
        }

        private sealed class ClearInventoryTargetResolution
        {
            private ClearInventoryTargetResolution(object? handle, string? failureCode)
            {
                Handle = handle;
                FailureCode = failureCode;
            }

            public object? Handle { get; }
            public string? FailureCode { get; }
            public bool IsMatched => Handle != null;
            public static ClearInventoryTargetResolution Matched(object handle) =>
                new ClearInventoryTargetResolution(handle ?? throw new ArgumentNullException(nameof(handle)), null);
            public static ClearInventoryTargetResolution Rejected(string failureCode) =>
                new ClearInventoryTargetResolution(null, failureCode);
        }

    }

    internal enum ResetSkillsNativeSnapshotStatus
    {
        Ready,
        Rejected,
        Failed
    }

    internal sealed class ResetSkillsNativeSnapshotResult
    {
        private ResetSkillsNativeSnapshotResult(
            ResetSkillsNativeSnapshotStatus status,
            ResetSkillsSnapshotScalar? snapshot,
            string? failureCode)
        {
            Status = status;
            Snapshot = snapshot;
            FailureCode = failureCode;
        }

        public ResetSkillsNativeSnapshotStatus Status { get; }
        public ResetSkillsSnapshotScalar? Snapshot { get; }
        public string? FailureCode { get; }
        public static ResetSkillsNativeSnapshotResult Ready(ResetSkillsSnapshotScalar snapshot) =>
            new ResetSkillsNativeSnapshotResult(
                ResetSkillsNativeSnapshotStatus.Ready,
                snapshot ?? throw new ArgumentNullException(nameof(snapshot)),
                null);
        public static ResetSkillsNativeSnapshotResult Rejected(string failureCode) =>
            new ResetSkillsNativeSnapshotResult(ResetSkillsNativeSnapshotStatus.Rejected, null, failureCode);
        public static ResetSkillsNativeSnapshotResult Failed(string failureCode) =>
            new ResetSkillsNativeSnapshotResult(ResetSkillsNativeSnapshotStatus.Failed, null, failureCode);
    }

    internal enum ResetSkillsNativeExecutionStatus
    {
        Succeeded,
        Rejected,
        Failed,
        ResultUnknown
    }

    internal sealed class ResetSkillsNativeExecutionResult
    {
        private ResetSkillsNativeExecutionResult(
            ResetSkillsNativeExecutionStatus status,
            ResetSkillsSnapshotScalar? snapshot,
            string? failureCode)
        {
            Status = status;
            Snapshot = snapshot;
            FailureCode = failureCode;
        }

        public ResetSkillsNativeExecutionStatus Status { get; }
        public ResetSkillsSnapshotScalar? Snapshot { get; }
        public string? FailureCode { get; }
        public static ResetSkillsNativeExecutionResult Succeeded(ResetSkillsSnapshotScalar snapshot) =>
            new ResetSkillsNativeExecutionResult(
                ResetSkillsNativeExecutionStatus.Succeeded,
                snapshot ?? throw new ArgumentNullException(nameof(snapshot)),
                null);
        public static ResetSkillsNativeExecutionResult Rejected(string failureCode) =>
            new ResetSkillsNativeExecutionResult(ResetSkillsNativeExecutionStatus.Rejected, null, failureCode);
        public static ResetSkillsNativeExecutionResult Failed(string failureCode) =>
            new ResetSkillsNativeExecutionResult(ResetSkillsNativeExecutionStatus.Failed, null, failureCode);
        public static ResetSkillsNativeExecutionResult ResultUnknown(string failureCode) =>
            new ResetSkillsNativeExecutionResult(ResetSkillsNativeExecutionStatus.ResultUnknown, null, failureCode);
    }

    internal sealed class ResetSkillsSnapshotScalar
    {
        private readonly PlayerSkillValue[] values;

        public ResetSkillsSnapshotScalar(
            string crossplatformId,
            string serverId,
            string worldId,
            DateTimeOffset observedAtUtc,
            string gameVersion,
            int? level,
            int? skillPoints,
            IEnumerable<PlayerSkillValue> values)
        {
            CrossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            ServerId = RequireText(serverId, nameof(serverId));
            WorldId = RequireText(worldId, nameof(worldId));
            RequireUtc(observedAtUtc, nameof(observedAtUtc));
            ObservedAtUtc = observedAtUtc;
            GameVersion = RequireText(gameVersion, nameof(gameVersion));
            Level = level;
            SkillPoints = skillPoints;
            this.values = values?.ToArray() ?? throw new ArgumentNullException(nameof(values));
        }

        public string CrossplatformId { get; }
        public string ServerId { get; }
        public string WorldId { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public string GameVersion { get; }
        public int? Level { get; }
        public int? SkillPoints { get; }
        public IReadOnlyList<PlayerSkillValue> Values => Array.AsReadOnly(values);

        public PlayerSkillSnapshot ToSnapshot(long snapshotId) =>
            new PlayerSkillSnapshot(
                snapshotId,
                CrossplatformId,
                ServerId,
                WorldId,
                ObservedAtUtc,
                GameVersion,
                Level,
                SkillPoints,
                values);
    }

    internal enum ClearInventoryNativeSnapshotStatus
    {
        Ready,
        Rejected,
        Failed
    }

    internal sealed class ClearInventoryNativeSnapshotResult
    {
        private ClearInventoryNativeSnapshotResult(
            ClearInventoryNativeSnapshotStatus status,
            ClearInventorySnapshotScalar? snapshot,
            string? failureCode)
        {
            Status = status;
            Snapshot = snapshot;
            FailureCode = failureCode;
        }

        public ClearInventoryNativeSnapshotStatus Status { get; }
        public ClearInventorySnapshotScalar? Snapshot { get; }
        public string? FailureCode { get; }
        public static ClearInventoryNativeSnapshotResult Ready(ClearInventorySnapshotScalar snapshot) =>
            new ClearInventoryNativeSnapshotResult(
                ClearInventoryNativeSnapshotStatus.Ready,
                snapshot ?? throw new ArgumentNullException(nameof(snapshot)),
                null);
        public static ClearInventoryNativeSnapshotResult Rejected(string failureCode) =>
            new ClearInventoryNativeSnapshotResult(ClearInventoryNativeSnapshotStatus.Rejected, null, failureCode);
        public static ClearInventoryNativeSnapshotResult Failed(string failureCode) =>
            new ClearInventoryNativeSnapshotResult(ClearInventoryNativeSnapshotStatus.Failed, null, failureCode);
    }

    internal enum ClearInventoryNativeExecutionStatus
    {
        Succeeded,
        Rejected,
        Failed,
        ResultUnknown
    }

    internal sealed class ClearInventoryNativeExecutionResult
    {
        private ClearInventoryNativeExecutionResult(
            ClearInventoryNativeExecutionStatus status,
            ClearInventorySnapshotScalar? snapshot,
            string? failureCode)
        {
            Status = status;
            Snapshot = snapshot;
            FailureCode = failureCode;
        }

        public ClearInventoryNativeExecutionStatus Status { get; }
        public ClearInventorySnapshotScalar? Snapshot { get; }
        public string? FailureCode { get; }
        public static ClearInventoryNativeExecutionResult Succeeded(ClearInventorySnapshotScalar snapshot) =>
            new ClearInventoryNativeExecutionResult(
                ClearInventoryNativeExecutionStatus.Succeeded,
                snapshot ?? throw new ArgumentNullException(nameof(snapshot)),
                null);
        public static ClearInventoryNativeExecutionResult Rejected(string failureCode) =>
            new ClearInventoryNativeExecutionResult(ClearInventoryNativeExecutionStatus.Rejected, null, failureCode);
        public static ClearInventoryNativeExecutionResult Failed(string failureCode) =>
            new ClearInventoryNativeExecutionResult(ClearInventoryNativeExecutionStatus.Failed, null, failureCode);
        public static ClearInventoryNativeExecutionResult ResultUnknown(string failureCode) =>
            new ClearInventoryNativeExecutionResult(ClearInventoryNativeExecutionStatus.ResultUnknown, null, failureCode);
    }

    internal sealed class ClearInventorySnapshotScalar
    {
        private readonly InventoryItemScalar[] items;

        public ClearInventorySnapshotScalar(
            string crossplatformId,
            string serverId,
            string worldId,
            DateTimeOffset observedAtUtc,
            string gameVersion,
            string? catalogVersion,
            CatalogResolutionState catalogResolution,
            string fingerprint,
            IEnumerable<InventoryItemScalar> items)
        {
            CrossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            ServerId = RequireText(serverId, nameof(serverId));
            WorldId = RequireText(worldId, nameof(worldId));
            RequireUtc(observedAtUtc, nameof(observedAtUtc));
            ObservedAtUtc = observedAtUtc;
            GameVersion = RequireText(gameVersion, nameof(gameVersion));
            CatalogVersion = string.IsNullOrWhiteSpace(catalogVersion) ? null : catalogVersion!.Trim();
            CatalogResolution = catalogResolution;
            Fingerprint = RequireText(fingerprint, nameof(fingerprint));
            this.items = items?.ToArray() ?? throw new ArgumentNullException(nameof(items));
        }

        public string CrossplatformId { get; }
        public string ServerId { get; }
        public string WorldId { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public string GameVersion { get; }
        public string? CatalogVersion { get; }
        public CatalogResolutionState CatalogResolution { get; }
        public string Fingerprint { get; }
        public IReadOnlyList<InventoryItemScalar> Items => Array.AsReadOnly(items);

        public PlayerInventorySnapshot ToSnapshot(long snapshotId) =>
            new PlayerInventorySnapshot(
                snapshotId,
                CrossplatformId,
                ServerId,
                WorldId,
                ObservedAtUtc,
                GameVersion,
                CatalogVersion,
                CatalogResolution,
                Fingerprint,
                true,
                items);
    }

    internal sealed class ResetSkillsConnectionSnapshot
    {
        public ResetSkillsConnectionSnapshot(int entityId, string crossplatformId, object handle)
        {
            EntityId = entityId;
            CrossplatformId = crossplatformId ?? throw new ArgumentNullException(nameof(crossplatformId));
            Handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        public int EntityId { get; }
        public string CrossplatformId { get; }
        public object Handle { get; }
    }

    internal sealed class ClearInventoryConnectionSnapshot
    {
        public ClearInventoryConnectionSnapshot(int entityId, string crossplatformId, object handle)
        {
            EntityId = entityId;
            CrossplatformId = crossplatformId ?? throw new ArgumentNullException(nameof(crossplatformId));
            Handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        public int EntityId { get; }
        public string CrossplatformId { get; }
        public object Handle { get; }
    }

    internal sealed class PlayerPartialResetRejectedException : Exception
    {
        public PlayerPartialResetRejectedException(string failureCode)
            : base(failureCode)
        {
            FailureCode = RequireText(failureCode, nameof(failureCode));
        }

        public string FailureCode { get; }
    }

    internal static class PlayerPartialResetNativeHelpers
    {
        internal static string CurrentWorldId() =>
            global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld);

        internal static global::EntityPlayer ResolveEntity(global::ClientInfo client)
        {
            var entity = global::GameManager.Instance?.World?.GetEntity(client.entityId) as global::EntityPlayer;
            if (entity == null || entity.entityId != client.entityId)
                throw new PlayerPartialResetRejectedException("player_not_online");
            return entity;
        }

        internal static PlayerEvidenceDraft CaptureDraft(
            SevenDaysPlayerEvidenceSnapshotReader reader,
            global::ClientInfo client)
        {
            var entity = ResolveEntity(client);
            var playerData = new global::PlayerDataFile();
            playerData.FromPlayer(entity);
            return reader.Read(client, playerData) ??
                throw new PlayerPartialResetRejectedException("snapshot_unavailable");
        }

        internal static string RequireText(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-blank scalar is required.", parameterName);
            return value!.Trim();
        }

        internal static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }
    }
}
