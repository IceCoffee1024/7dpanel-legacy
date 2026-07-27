using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GrantItemUseCase
    {
        private const int MinimumQuality = 1;
        private const int MaximumQuality = 6;
        private const int DefaultMaximumQuantity = 10_000;
        private const int DefaultMaximumStacks = 100;
        private static long snapshotIdSequence = DateTimeOffset.UtcNow.Ticks;

        private readonly IGameResourceCatalog catalog;
        private readonly IGrantItemOperationStore store;
        private readonly IGrantItemGateway gateway;
        private readonly IPlayerEvidenceStore evidenceStore;
        private readonly string serverId;
        private readonly Func<string> operationId;
        private readonly Func<long> snapshotId;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly int maximumQuantity;
        private readonly int maximumStacks;

        public GrantItemUseCase(
            IGameResourceCatalog catalog,
            IGrantItemOperationStore store,
            IGrantItemGateway gateway,
            IPlayerEvidenceStore evidenceStore,
            string serverId)
            : this(
                catalog,
                store,
                gateway,
                evidenceStore,
                serverId,
                () => Guid.NewGuid().ToString("N"),
                () => Interlocked.Increment(ref snapshotIdSequence),
                () => DateTimeOffset.UtcNow,
                DefaultMaximumQuantity,
                DefaultMaximumStacks)
        {
        }

        internal GrantItemUseCase(
            IGameResourceCatalog catalog,
            IGrantItemOperationStore store,
            IGrantItemGateway gateway,
            IPlayerEvidenceStore evidenceStore,
            string serverId,
            Func<string> operationId,
            Func<long> snapshotId,
            Func<DateTimeOffset> utcClock,
            int maximumQuantity,
            int maximumStacks)
        {
            if (maximumQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(maximumQuantity));
            if (maximumStacks <= 0) throw new ArgumentOutOfRangeException(nameof(maximumStacks));

            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            this.evidenceStore = evidenceStore ?? throw new ArgumentNullException(nameof(evidenceStore));
            this.serverId = PlayerEvidenceValidation.RequireText(serverId, nameof(serverId));
            this.operationId = operationId ?? throw new ArgumentNullException(nameof(operationId));
            this.snapshotId = snapshotId ?? throw new ArgumentNullException(nameof(snapshotId));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            this.maximumQuantity = maximumQuantity;
            this.maximumStacks = maximumStacks;
        }

        public async Task<GrantItemResult> ExecuteAsync(
            GrantItemRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var resource = ResolveResource(request);
            var createdAtUtc = PlayerEvidenceValidation.RequireUtc(
                utcClock(),
                nameof(utcClock));
            var requestedOperationId = PlayerEvidenceValidation.RequireText(
                operationId(),
                nameof(operationId));
            var intent = new GrantItemPendingIntent(
                requestedOperationId,
                request.OperatorId,
                request.Target,
                request.ClientRequestKey,
                request.CorrelationId,
                createdAtUtc,
                request.CatalogVersion,
                request.ResourceId,
                resource.GameVersion,
                resource.Entry.NumericId,
                resource.Entry.InternalName,
                resource.Entry.Kind,
                request.Quantity,
                request.Quality,
                request.HiddenItemConfirmed);

            var operation = store.CreatePending(intent);
            if (!string.Equals(
                    operation.OperationId,
                    requestedOperationId,
                    StringComparison.Ordinal))
            {
                return Existing(operation, request.Quantity);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Complete(
                    operation.OperationId,
                    PlayerActionStatus.Cancelled,
                    null,
                    null,
                    null,
                    null,
                    reused: false);
            }

            long? beforeSnapshotId = null;
            long? afterSnapshotId = null;
            var started = false;
            GrantItemGatewayResult gatewayResult;

            try
            {
                var beforeScalar = await gateway.CaptureInventorySnapshotAsync(
                        new GrantItemSnapshotCommand(
                            operation.OperationId,
                            request.Target,
                            request.CatalogVersion,
                            GrantItemSnapshotPhase.Before),
                        cancellationToken)
                    .ConfigureAwait(false);
                var beforeSnapshot = CreateSnapshot(request, beforeScalar);
                evidenceStore.AppendInventorySnapshot(beforeSnapshot);
                beforeSnapshotId = beforeSnapshot.SnapshotId;
            }
            catch (OperationCanceledException)
            {
                return Complete(
                    operation.OperationId,
                    PlayerActionStatus.Cancelled,
                    null,
                    null,
                    null,
                    null,
                    reused: false);
            }
            catch
            {
                return Complete(
                    operation.OperationId,
                    PlayerActionStatus.Failed,
                    GrantItemFailureCodes.SnapshotUnavailable,
                    null,
                    null,
                    null,
                    reused: false);
            }

            var command = new GrantItemCommand(
                operation.OperationId,
                request.Target,
                request.CatalogVersion,
                request.ResourceId,
                resource.Entry.NumericId,
                resource.Entry.InternalName,
                resource.Entry.Kind,
                resource.Entry.Visibility,
                request.HiddenItemConfirmed,
                request.Quantity,
                request.Quality,
                resource.Entry.MaxStack!.Value,
                resource.Entry.HasQuality!.Value,
                resource.GameVersion);

            bool TryStart(DateTimeOffset startedAtUtc)
            {
                var persisted = store.TryStart(operation.OperationId, startedAtUtc);
                if (persisted) started = true;
                return persisted;
            }

            try
            {
                gatewayResult = await gateway.GrantAsync(
                        command,
                        TryStart,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                gatewayResult = started
                    ? GrantItemGatewayResult.ResultUnknown(GrantItemFailureCodes.ResultUnknown)
                    : GrantItemGatewayResult.Cancelled();
            }
            catch
            {
                gatewayResult = started
                    ? GrantItemGatewayResult.ResultUnknown(GrantItemFailureCodes.ResultUnknown)
                    : GrantItemGatewayResult.Failed(GrantItemFailureCodes.GatewayFailure);
            }

            if (gatewayResult.Status == GrantItemGatewayStatus.Succeeded &&
                gatewayResult.ActualQuantity != request.Quantity)
            {
                gatewayResult = GrantItemGatewayResult.ResultUnknown(
                    GrantItemFailureCodes.ResultUnknown);
            }

            if (gatewayResult.Status == GrantItemGatewayStatus.Succeeded)
            {
                try
                {
                    var afterScalar = await gateway.CaptureInventorySnapshotAsync(
                            new GrantItemSnapshotCommand(
                                operation.OperationId,
                                request.Target,
                                request.CatalogVersion,
                                GrantItemSnapshotPhase.After),
                            cancellationToken)
                        .ConfigureAwait(false);
                    var afterSnapshot = CreateSnapshot(request, afterScalar);
                    evidenceStore.AppendInventorySnapshot(afterSnapshot);
                    afterSnapshotId = afterSnapshot.SnapshotId;
                }
                catch
                {
                    gatewayResult = GrantItemGatewayResult.ResultUnknown(
                        GrantItemFailureCodes.ResultUnknown);
                }
            }

            var status = ToPlayerActionStatus(gatewayResult.Status);
            return Complete(
                operation.OperationId,
                status,
                gatewayResult.FailureCode,
                status == PlayerActionStatus.Succeeded
                    ? gatewayResult.ActualQuantity
                    : null,
                beforeSnapshotId,
                status == PlayerActionStatus.Succeeded ? afterSnapshotId : null,
                reused: false);
        }

        private ResolvedGrantItem ResolveResource(GrantItemRequest request)
        {
            GameResourceCatalogReadResult read;
            try
            {
                read = catalog.Read();
            }
            catch
            {
                throw new GrantItemRequestRejectedException(
                    GrantItemFailureCodes.CatalogUnavailable);
            }

            if (read.Status != GameResourceCatalogReadStatus.Available || read.Snapshot == null)
            {
                throw new GrantItemRequestRejectedException(
                    GrantItemFailureCodes.CatalogUnavailable);
            }

            var snapshot = read.Snapshot;
            if (!string.Equals(
                    snapshot.CatalogVersion,
                    request.CatalogVersion,
                    StringComparison.Ordinal))
            {
                throw new GrantItemRequestRejectedException(
                    GrantItemFailureCodes.CatalogChanged);
            }

            var matches = snapshot.Resources
                .Where(entry => string.Equals(
                    entry.ResourceId,
                    request.ResourceId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (matches.Length == 0)
            {
                throw new GrantItemRequestRejectedException(
                    GrantItemFailureCodes.ResourceNotFound);
            }
            if (matches.Length != 1 || matches[0].Kind != GameResourceKind.Item)
            {
                throw new GrantItemRequestRejectedException(
                    GrantItemFailureCodes.ResourceNotGrantable);
            }

            var entry = matches[0];
            if (entry.Visibility == GameResourceVisibility.Hidden &&
                !request.HiddenItemConfirmed)
            {
                throw new GrantItemRequestRejectedException(
                    GrantItemFailureCodes.HiddenItemConfirmationRequired);
            }
            if (request.Quantity > maximumQuantity)
            {
                throw new GrantItemRequestRejectedException(
                    GrantItemFailureCodes.QuantityLimitExceeded);
            }
            if (!entry.MaxStack.HasValue || entry.MaxStack.Value <= 0)
            {
                throw new GrantItemRequestRejectedException(
                    GrantItemFailureCodes.VersionUnsupported);
            }

            var stackCount = ((long)request.Quantity + entry.MaxStack.Value - 1L) /
                             entry.MaxStack.Value;
            if (stackCount > maximumStacks)
            {
                throw new GrantItemRequestRejectedException(
                    GrantItemFailureCodes.StackLimitExceeded);
            }

            if (!entry.HasQuality.HasValue ||
                (entry.HasQuality.Value &&
                 (!request.Quality.HasValue ||
                  request.Quality.Value < MinimumQuality ||
                  request.Quality.Value > MaximumQuality)) ||
                (!entry.HasQuality.Value && request.Quality.HasValue))
            {
                throw new GrantItemRequestRejectedException(
                    GrantItemFailureCodes.QualityUnsupported);
            }
            if (string.IsNullOrWhiteSpace(snapshot.GameVersion))
            {
                throw new GrantItemRequestRejectedException(
                    GrantItemFailureCodes.VersionUnsupported);
            }

            return new ResolvedGrantItem(entry, snapshot.GameVersion!);
        }

        private PlayerInventorySnapshot CreateSnapshot(
            GrantItemRequest request,
            GrantItemInventorySnapshot source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new PlayerInventorySnapshot(
                snapshotId(),
                request.Target.CrossplatformId,
                serverId,
                request.Target.WorldId,
                source.ObservedAtUtc,
                source.GameVersion,
                source.CatalogVersion,
                source.CatalogResolution,
                source.Fingerprint,
                true,
                source.Items);
        }

        private GrantItemResult Complete(
            string operationId,
            PlayerActionStatus status,
            string? failureCode,
            int? actualQuantity,
            long? beforeSnapshotId,
            long? afterSnapshotId,
            bool reused)
        {
            var completion = new GrantItemOperationCompletion(
                operationId,
                status,
                PlayerEvidenceValidation.RequireUtc(utcClock(), nameof(utcClock)),
                failureCode,
                beforeSnapshotId,
                afterSnapshotId,
                actualQuantity);
            var persisted = false;
            try
            {
                persisted = store.TryComplete(completion);
            }
            catch
            {
                persisted = false;
            }

            return new GrantItemResult(
                operationId,
                status,
                failureCode,
                actualQuantity,
                beforeSnapshotId,
                afterSnapshotId,
                reused,
                persisted);
        }

        private static GrantItemResult Existing(
            PlayerActionOperation operation,
            int requestedQuantity)
        {
            return new GrantItemResult(
                operation.OperationId,
                operation.Status,
                operation.FailureCode,
                operation.Status == PlayerActionStatus.Succeeded
                    ? requestedQuantity
                    : null,
                operation.BeforeInventorySnapshotId,
                operation.AfterInventorySnapshotId,
                reused: true,
                terminalStatePersisted: true);
        }

        private static PlayerActionStatus ToPlayerActionStatus(GrantItemGatewayStatus status)
        {
            return status switch
            {
                GrantItemGatewayStatus.Succeeded => PlayerActionStatus.Succeeded,
                GrantItemGatewayStatus.Rejected => PlayerActionStatus.Rejected,
                GrantItemGatewayStatus.Failed => PlayerActionStatus.Failed,
                GrantItemGatewayStatus.Cancelled => PlayerActionStatus.Cancelled,
                GrantItemGatewayStatus.ResultUnknown => PlayerActionStatus.ResultUnknown,
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };
        }

        private sealed class ResolvedGrantItem
        {
            public ResolvedGrantItem(GameResourceCatalogEntry entry, string gameVersion)
            {
                Entry = entry;
                GameVersion = gameVersion;
            }

            public GameResourceCatalogEntry Entry { get; }
            public string GameVersion { get; }
        }
    }
}
