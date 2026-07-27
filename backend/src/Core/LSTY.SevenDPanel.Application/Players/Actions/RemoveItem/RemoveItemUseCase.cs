using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class RemoveItemUseCase
    {
        private static readonly TimeSpan DefaultTargetFreshness = TimeSpan.FromSeconds(30);
        private static long snapshotIdSequence = DateTimeOffset.UtcNow.Ticks;

        private readonly IGameResourceCatalog catalog;
        private readonly IRemoveItemOperationStore operationStore;
        private readonly IRemoveItemGateway gateway;
        private readonly IPlayerEvidenceStore evidenceStore;
        private readonly string serverId;
        private readonly Func<string> operationIdFactory;
        private readonly Func<long> snapshotIdFactory;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly TimeSpan targetFreshness;

        public RemoveItemUseCase(
            IGameResourceCatalog catalog,
            IRemoveItemOperationStore operationStore,
            IRemoveItemGateway gateway,
            IPlayerEvidenceStore evidenceStore,
            string serverId)
            : this(
                catalog,
                operationStore,
                gateway,
                evidenceStore,
                serverId,
                () => Guid.NewGuid().ToString("N"),
                () => Interlocked.Increment(ref snapshotIdSequence),
                () => DateTimeOffset.UtcNow,
                DefaultTargetFreshness)
        {
        }

        internal RemoveItemUseCase(
            IGameResourceCatalog catalog,
            IRemoveItemOperationStore operationStore,
            IRemoveItemGateway gateway,
            IPlayerEvidenceStore evidenceStore,
            string serverId,
            Func<string> operationIdFactory,
            Func<long> snapshotIdFactory,
            Func<DateTimeOffset> utcClock,
            TimeSpan? targetFreshness = null)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            this.evidenceStore = evidenceStore ?? throw new ArgumentNullException(nameof(evidenceStore));
            this.serverId = PlayerEvidenceValidation.RequireText(serverId, nameof(serverId));
            this.operationIdFactory = operationIdFactory ?? throw new ArgumentNullException(nameof(operationIdFactory));
            this.snapshotIdFactory = snapshotIdFactory ?? throw new ArgumentNullException(nameof(snapshotIdFactory));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            this.targetFreshness = targetFreshness ?? DefaultTargetFreshness;
            if (this.targetFreshness <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(targetFreshness));
        }

        public async Task<RemoveItemResult> ExecuteAsync(
            RemoveItemRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var createdAtUtc = utcClock();
            var targetAge = createdAtUtc - request.Target.OnlineObservedAtUtc;
            if (targetAge < TimeSpan.Zero || targetAge > targetFreshness)
                throw new RemoveItemTargetNotFreshException();
            var resource = ResolveCatalogResource(request);

            var operationId = operationIdFactory();
            var intent = new RemoveItemPendingIntent(
                operationId,
                request.OperatorId,
                request.Target,
                request.ClientRequestKey,
                request.CorrelationId,
                createdAtUtc,
                request.CatalogVersion,
                request.ResourceId,
                resource.InternalName,
                resource.Kind,
                request.Quantity,
                request.Quality,
                request.RemovalScope,
                request.RemovalMode);

            PlayerActionOperation operation;
            try
            {
                operation = operationStore.CreatePending(intent);
            }
            catch (RemoveItemIdempotencyConflictException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new RemoveItemPendingStoreException(exception);
            }

            if (!string.Equals(operation.OperationId, operationId, StringComparison.Ordinal))
                return RemoveItemResult.FromExisting(operation);

            if (cancellationToken.IsCancellationRequested)
            {
                return CompleteTerminal(
                    operationId,
                    PlayerActionStatus.Cancelled,
                    "remove_item_cancelled",
                    null,
                    null,
                    null);
            }

            bool started;
            try
            {
                started = operationStore.TryStart(operationId, utcClock());
            }
            catch (Exception exception)
            {
                throw new RemoveItemOperationStateConflictException(exception);
            }
            if (!started) throw new RemoveItemOperationStateConflictException();

            RemoveItemGatewayResult gatewayResult;
            try
            {
                gatewayResult = await gateway.RemoveAsync(
                        new RemoveItemCommand(
                            request.Target,
                            request.CatalogVersion,
                            request.ResourceId,
                            resource.InternalName,
                            resource.Kind,
                            request.Quantity,
                            request.Quality,
                            request.RemovalScope,
                            request.RemovalMode),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                gatewayResult = RemoveItemGatewayResult.Terminal(
                    RemoveItemGatewayStatus.Cancelled,
                    "remove_item_cancelled");
            }
            catch (TimeoutException)
            {
                gatewayResult = RemoveItemGatewayResult.Terminal(
                    RemoveItemGatewayStatus.Failed,
                    "game_thread_timeout");
            }
            catch
            {
                gatewayResult = RemoveItemGatewayResult.Terminal(
                    RemoveItemGatewayStatus.ResultUnknown,
                    "remove_item_result_unknown");
            }

            if (gatewayResult.Status != RemoveItemGatewayStatus.Succeeded)
            {
                return CompleteTerminal(
                    operationId,
                    MapStatus(gatewayResult.Status),
                    gatewayResult.FailureCode,
                    null,
                    null,
                    null);
            }

            var before = CreateSnapshot(request, gatewayResult.BeforeInventory!);
            var after = CreateSnapshot(request, gatewayResult.AfterInventory!);
            try
            {
                evidenceStore.AppendInventorySnapshot(before);
                evidenceStore.AppendInventorySnapshot(after);
            }
            catch
            {
                return CompleteTerminal(
                    operationId,
                    PlayerActionStatus.ResultUnknown,
                    "inventory_evidence_store_failed",
                    null,
                    null,
                    null);
            }

            return CompleteTerminal(
                operationId,
                PlayerActionStatus.Succeeded,
                null,
                gatewayResult.ActualQuantity,
                before.SnapshotId,
                after.SnapshotId);
        }

        private GameResourceCatalogEntry ResolveCatalogResource(RemoveItemRequest request)
        {
            GameResourceCatalogReadResult read;
            try { read = catalog.Read(); }
            catch (Exception exception) { throw new RemoveItemCatalogUnavailableException(exception); }

            if (read.Status != GameResourceCatalogReadStatus.Available || read.Snapshot == null)
                throw new RemoveItemCatalogUnavailableException();
            if (!string.Equals(
                    read.Snapshot.CatalogVersion,
                    request.CatalogVersion,
                    StringComparison.Ordinal))
            {
                throw new RemoveItemCatalogConflictException();
            }

            var matches = read.Snapshot.Resources
                .Where(resource => string.Equals(
                    resource.ResourceId,
                    request.ResourceId,
                    StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new RemoveItemCatalogConflictException();
            var resource = matches[0];
            if (resource.Kind != GameResourceKind.Item ||
                (request.Quality.HasValue && resource.HasQuality == false))
            {
                throw new RemoveItemCatalogConflictException();
            }
            return resource;
        }

        private PlayerInventorySnapshot CreateSnapshot(
            RemoveItemRequest request,
            RemoveItemInventorySnapshot source) =>
            new PlayerInventorySnapshot(
                snapshotIdFactory(),
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

        private RemoveItemResult CompleteTerminal(
            string operationId,
            PlayerActionStatus status,
            string? failureCode,
            int? actualQuantity,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId)
        {
            var completion = new RemoveItemOperationCompletion(
                operationId,
                status,
                utcClock(),
                failureCode,
                actualQuantity,
                beforeInventorySnapshotId,
                afterInventorySnapshotId);
            var persisted = false;
            try
            {
                persisted = operationStore.TryComplete(completion);
            }
            catch { persisted = false; }

            return new RemoveItemResult(
                operationId,
                status,
                actualQuantity,
                failureCode,
                beforeInventorySnapshotId,
                afterInventorySnapshotId,
                reused: false,
                terminalStatePersisted: persisted);
        }

        private static PlayerActionStatus MapStatus(RemoveItemGatewayStatus status)
        {
            switch (status)
            {
                case RemoveItemGatewayStatus.Rejected:
                    return PlayerActionStatus.Rejected;
                case RemoveItemGatewayStatus.Failed:
                    return PlayerActionStatus.Failed;
                case RemoveItemGatewayStatus.Cancelled:
                    return PlayerActionStatus.Cancelled;
                case RemoveItemGatewayStatus.ResultUnknown:
                    return PlayerActionStatus.ResultUnknown;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status));
            }
        }
    }

    public sealed class RemoveItemResult
    {
        internal RemoveItemResult(
            string operationId,
            PlayerActionStatus status,
            int? actualQuantity,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId,
            bool reused,
            bool terminalStatePersisted)
        {
            OperationId = operationId;
            Status = status;
            ActualQuantity = actualQuantity;
            FailureCode = failureCode;
            BeforeInventorySnapshotId = beforeInventorySnapshotId;
            AfterInventorySnapshotId = afterInventorySnapshotId;
            Reused = reused;
            TerminalStatePersisted = terminalStatePersisted;
        }

        public string OperationId { get; }
        public PlayerActionStatus Status { get; }
        public int? ActualQuantity { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
        public bool Reused { get; }
        public bool TerminalStatePersisted { get; }

        internal static RemoveItemResult FromExisting(PlayerActionOperation operation) =>
            new RemoveItemResult(
                operation.OperationId,
                operation.Status,
                null,
                operation.FailureCode,
                operation.BeforeInventorySnapshotId,
                operation.AfterInventorySnapshotId,
                reused: true,
                terminalStatePersisted: operation.CompletedAtUtc.HasValue);
    }

    public sealed class RemoveItemCatalogUnavailableException : InvalidOperationException
    {
        public RemoveItemCatalogUnavailableException()
            : base("The game resource catalog is not available.")
        {
        }

        public RemoveItemCatalogUnavailableException(Exception innerException)
            : base("The game resource catalog is not available.", innerException)
        {
        }
    }

    public sealed class RemoveItemCatalogConflictException : InvalidOperationException
    {
        public RemoveItemCatalogConflictException()
            : base("The submitted remove-item resource no longer matches the current catalog.")
        {
        }
    }

    public sealed class RemoveItemTargetNotFreshException : InvalidOperationException
    {
        public RemoveItemTargetNotFreshException()
            : base("The fixed online player target is no longer fresh.")
        {
        }
    }

    public sealed class RemoveItemPendingStoreException : InvalidOperationException
    {
        public RemoveItemPendingStoreException(Exception innerException)
            : base("The pending remove-item operation could not be persisted.", innerException)
        {
        }
    }

    public sealed class RemoveItemOperationStateConflictException : InvalidOperationException
    {
        public RemoveItemOperationStateConflictException()
            : base("The remove-item operation is no longer pending.")
        {
        }

        public RemoveItemOperationStateConflictException(Exception innerException)
            : base("The remove-item operation could not be started.", innerException)
        {
        }
    }

}
