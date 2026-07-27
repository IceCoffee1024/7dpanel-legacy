using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.WorldOperations;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World
{
    public sealed class WorldOperationJobHandler : IWorldOperationJobHandler
    {
        private readonly IWorldOperationExecutionStore executions;
        private readonly SevenDaysMapWorldOperationHandler map;
        private const string MapPublishFailedError = "map_publish_failed";

        private readonly IMapResourcePublisher mapPublisher;
        private readonly SevenDaysRegionOperationHandler region;
        private readonly SevenDaysBlockPrefabOperationHandler blockPrefab;
        private readonly SevenDaysEntityMaintenanceHandler entities;
        private readonly SevenDaysRuntimeMaintenanceHandler maintenance;
        private readonly SevenDaysUndoOperationHandler undo;
        private readonly Func<DateTimeOffset> utcNow;

        public WorldOperationJobHandler(
            IWorldOperationExecutionStore executions,
            SevenDaysMapWorldOperationHandler map,
            IMapResourcePublisher mapPublisher,
            SevenDaysRegionOperationHandler region,
            SevenDaysBlockPrefabOperationHandler blockPrefab,
            SevenDaysEntityMaintenanceHandler entities,
            SevenDaysRuntimeMaintenanceHandler maintenance,
            SevenDaysUndoOperationHandler undo,
            Func<DateTimeOffset> utcNow)
        {
            this.executions = executions ?? throw new ArgumentNullException(nameof(executions));
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.mapPublisher = mapPublisher ?? throw new ArgumentNullException(nameof(mapPublisher));
            this.region = region ?? throw new ArgumentNullException(nameof(region));
            this.blockPrefab = blockPrefab ?? throw new ArgumentNullException(nameof(blockPrefab));
            this.entities = entities ?? throw new ArgumentNullException(nameof(entities));
            this.maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
            this.undo = undo ?? throw new ArgumentNullException(nameof(undo));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public async Task<WorldOperationJobCompletion> ExecuteAsync(
            Guid jobId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var execution = executions.ReadForExecution(jobId);
            switch (execution.Intent.Kind)
            {
                case WorldOperationKind.DeleteLandClaim:
                case WorldOperationKind.MoveOnlinePlayer:
                case WorldOperationKind.MoveEntity:
                case WorldOperationKind.RefreshMapResources:
                case WorldOperationKind.RenderExploredMap:
                case WorldOperationKind.RenderFullMap:
                    return await ExecuteMapAsync(execution, cancellationToken).ConfigureAwait(false);

                case WorldOperationKind.CopyRegion:
                case WorldOperationKind.FillRegion:
                case WorldOperationKind.ClearRegion:
                case WorldOperationKind.PasteRegion:
                    return FromRegion(await region.HandleAsync(
                        execution,
                        cancellationToken).ConfigureAwait(false));

                case WorldOperationKind.SetBlock:
                case WorldOperationKind.PlacePrefab:
                case WorldOperationKind.RemovePrefab:
                    return FromBlockPrefab(await blockPrefab.HandleAsync(
                        execution,
                        cancellationToken).ConfigureAwait(false));

                case WorldOperationKind.SpawnEntity:
                case WorldOperationKind.DeleteEntity:
                case WorldOperationKind.CleanupEntities:
                    return FromEntity(await entities.HandleAsync(
                        execution.Intent,
                        cancellationToken).ConfigureAwait(false));

                case WorldOperationKind.ReloadBlocks:
                case WorldOperationKind.ReloadItems:
                case WorldOperationKind.ReloadEntityClasses:
                case WorldOperationKind.ReloadPrefabs:
                case WorldOperationKind.CollectGarbage:
                    return FromMaintenance(await maintenance.HandleAsync(
                        execution.Intent,
                        cancellationToken).ConfigureAwait(false));

                case WorldOperationKind.UndoChangeSet:
                    return FromUndo(
                        jobId,
                        await undo.HandleAsync(execution, cancellationToken).ConfigureAwait(false));

                default:
                    return Completion(
                        WorldOperationStatus.Failed,
                        "operation_kind_not_supported",
                        null);
            }
        }

        private async Task<WorldOperationJobCompletion> ExecuteMapAsync(
            WorldOperationExecutionRecord execution,
            CancellationToken cancellationToken)
        {
            var result = await map.HandleAsync(execution.Intent, cancellationToken)
                .ConfigureAwait(false);
            if (result.Outcome != SevenDaysMapWorldOperationOutcome.Succeeded)
                return FromMap(result);
            if (string.IsNullOrWhiteSpace(result.StagedMapRoot))
                return Completion(WorldOperationStatus.Succeeded, null, null);

            try
            {
                mapPublisher.Publish(execution.Intent.WorldId, result.StagedMapRoot!);
                return Completion(WorldOperationStatus.Succeeded, null, null);
            }
            catch (MapResourcePublishException exception)
            {
                return Completion(WorldOperationStatus.Failed, exception.ErrorCode, null);
            }
            catch
            {
                return Completion(
                    WorldOperationStatus.Failed,
                    MapPublishFailedError,
                    null);
            }
        }

        private static WorldOperationJobCompletion FromMap(
            SevenDaysMapWorldOperationResult result) => result.Outcome switch
        {
            SevenDaysMapWorldOperationOutcome.Succeeded =>
                Completion(WorldOperationStatus.Succeeded, null, null),
            SevenDaysMapWorldOperationOutcome.Rejected or
            SevenDaysMapWorldOperationOutcome.Failed =>
                Completion(WorldOperationStatus.Failed, result.ErrorCode, null),
            _ => Completion(WorldOperationStatus.ResultUnknown, result.ErrorCode, null)
        };

        private static WorldOperationJobCompletion FromRegion(
            SevenDaysRegionOperationResult result) => result.Outcome switch
        {
            SevenDaysRegionOperationOutcome.Succeeded =>
                Completion(WorldOperationStatus.Succeeded, null, result.Progress),
            SevenDaysRegionOperationOutcome.Rejected or
            SevenDaysRegionOperationOutcome.Failed =>
                Completion(WorldOperationStatus.Failed, result.ErrorCode, result.Progress),
            _ => Completion(WorldOperationStatus.ResultUnknown, result.ErrorCode, result.Progress)
        };

        private static WorldOperationJobCompletion FromBlockPrefab(
            SevenDaysBlockPrefabOperationResult result) => result.Outcome switch
        {
            SevenDaysBlockPrefabOperationOutcome.Succeeded =>
                Completion(WorldOperationStatus.Succeeded, null, null),
            SevenDaysBlockPrefabOperationOutcome.Rejected or
            SevenDaysBlockPrefabOperationOutcome.Failed =>
                Completion(WorldOperationStatus.Failed, result.ErrorCode, null),
            _ => Completion(WorldOperationStatus.ResultUnknown, result.ErrorCode, null)
        };

        private static WorldOperationJobCompletion FromEntity(
            SevenDaysEntityMaintenanceResult result) => result.Outcome switch
        {
            SevenDaysEntityMaintenanceOutcome.Succeeded =>
                Completion(WorldOperationStatus.Succeeded, null, null),
            SevenDaysEntityMaintenanceOutcome.Rejected or
            SevenDaysEntityMaintenanceOutcome.Failed =>
                Completion(WorldOperationStatus.Failed, result.ErrorCode, null),
            _ => Completion(WorldOperationStatus.ResultUnknown, result.ErrorCode, null)
        };

        private static WorldOperationJobCompletion FromMaintenance(
            SevenDaysRuntimeMaintenanceResult result) => result.Outcome switch
        {
            SevenDaysRuntimeMaintenanceOutcome.Succeeded =>
                Completion(WorldOperationStatus.Succeeded, null, null),
            SevenDaysRuntimeMaintenanceOutcome.Rejected or
            SevenDaysRuntimeMaintenanceOutcome.Failed =>
                Completion(WorldOperationStatus.Failed, result.ErrorCode, null),
            _ => Completion(WorldOperationStatus.ResultUnknown, result.ErrorCode, null)
        };

        private WorldOperationJobCompletion FromUndo(
            Guid jobId,
            SevenDaysUndoOperationResult result)
        {
            switch (result.Outcome)
            {
                case SevenDaysUndoOperationOutcome.Succeeded:
                    return Completion(WorldOperationStatus.Succeeded, null, result.Progress);
                case SevenDaysUndoOperationOutcome.Rejected:
                case SevenDaysUndoOperationOutcome.Failed:
                    return Completion(WorldOperationStatus.Failed, result.ErrorCode, result.Progress);
                case SevenDaysUndoOperationOutcome.Interrupted:
                    return Completion(WorldOperationStatus.Interrupted, result.ErrorCode, result.Progress);
                case SevenDaysUndoOperationOutcome.RollbackFailed:
                    var errorCode = result.ErrorCode ?? SevenDaysUndoOperationResult.RollbackFailed;
                    executions.MarkRollbackFailed(jobId, errorCode, RequireUtc(utcNow()));
                    return Completion(WorldOperationStatus.RollbackFailed, errorCode, result.Progress);
                default:
                    return Completion(WorldOperationStatus.ResultUnknown, result.ErrorCode, result.Progress);
            }
        }

        private static WorldOperationJobCompletion Completion(
            WorldOperationStatus status,
            string? errorCode,
            WorldOperationProgress? progress) =>
            new WorldOperationJobCompletion(status, errorCode, progress);

        private static DateTimeOffset RequireUtc(DateTimeOffset value)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("world_operation_clock_not_utc");
            return value;
        }
    }
}
