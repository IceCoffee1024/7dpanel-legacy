using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.WorldOperations;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.WorldOperations
{
    public sealed class SqliteWorldOperationStore :
        IWorldOperationStore,
        IWorldOperationExecutionStore,
        IWorldOperationRecoveryStore
    {
        public const string RestartResultUnknownError =
            "world_operation_restart_result_unknown";

        private const string EffectiveStatus = @"CASE
            WHEN operation.rollback_failure_code IS NOT NULL THEN 'RollbackFailed'
            WHEN operation.submission_failure_code IS NOT NULL THEN 'Failed'
            WHEN job.status = 'PendingRestart' THEN 'Interrupted'
            ELSE job.status END";

        private const string SelectColumns = @"SELECT
            operation.operation_id AS OperationId,
            operation.job_id AS JobId,
            operation.actor_subject AS ActorSubject,
            operation.kind AS Kind,
            operation.world_id AS WorldId,
            operation.world_version AS WorldVersion,
            operation.map_resource_version AS MapResourceVersion,
            operation.correlation_id AS CorrelationId,
            operation.confirmation_summary AS ConfirmationSummary,
            operation.is_reversible AS IsReversible,
            operation.change_set_id AS ChangeSetId,
            job.status AS JobStatus,
            job.progress_current AS ProgressCurrent,
            job.progress_total AS ProgressTotal,
            job.error_code AS JobErrorCode,
            operation.submission_failure_code AS SubmissionFailureCode,
            operation.rollback_failure_code AS RollbackFailureCode,
            operation.created_at_utc AS CreatedAtUtc,
            job.started_at_utc AS StartedAtUtc,
            job.completed_at_utc AS JobCompletedAtUtc,
            operation.rollback_failed_at_utc AS RollbackFailedAtUtc
            FROM world_operations operation
            INNER JOIN jobs job ON job.id = operation.job_id";

        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteWorldOperationStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public WorldOperationRecord Get(string operationId)
        {
            operationId = RequireText(operationId, nameof(operationId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<WorldOperationRow>(
                SelectColumns + " WHERE operation.operation_id = @OperationId;",
                new { OperationId = operationId });
            return row == null
                ? throw new KeyNotFoundException("The world operation does not exist.")
                : ToRecord(row);
        }

        public WorldOperationExecutionRecord ReadForExecution(Guid jobId)
        {
            if (jobId == Guid.Empty) throw new ArgumentException("A job ID is required.", nameof(jobId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<WorldOperationIntentRow>(
                @"SELECT operation_id AS OperationId, job_id AS JobId,
                      actor_subject AS ActorSubject, kind AS Kind,
                      world_id AS WorldId, world_version AS WorldVersion,
                      map_resource_version AS MapResourceVersion,
                      correlation_id AS CorrelationId,
                      confirmation_summary AS ConfirmationSummary,
                      is_reversible AS IsReversible,
                      created_at_utc AS CreatedAtUtc
                  FROM world_operations WHERE job_id = @JobId;",
                new { JobId = jobId.ToString("D") });
            if (row == null)
                throw new KeyNotFoundException("The world operation job does not exist.");
            if (!Enum.TryParse(row.Kind, out WorldOperationKind kind) ||
                !Enum.IsDefined(typeof(WorldOperationKind), kind))
            {
                throw new InvalidDataException("world_operation_kind_invalid");
            }

            var target = ReadTarget(connection, row.OperationId, kind);
            return new WorldOperationExecutionRecord(
                row.OperationId,
                Guid.Parse(row.JobId),
                new WorldOperationIntent(
                    row.ActorSubject,
                    kind,
                    row.WorldId,
                    row.WorldVersion,
                    row.MapResourceVersion,
                    row.CorrelationId,
                    row.ConfirmationSummary,
                    row.IsReversible != 0,
                    target,
                    DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc)));
        }

        public void MarkRollbackFailed(
            Guid jobId,
            string errorCode,
            DateTimeOffset failedAtUtc)
        {
            if (jobId == Guid.Empty) throw new ArgumentException("A job ID is required.", nameof(jobId));
            errorCode = RequireText(errorCode, nameof(errorCode));
            RequireUtc(failedAtUtc, nameof(failedAtUtc));
            using var connection = connectionFactory.Open();
            if (connection.Execute(
                    @"UPDATE world_operations
                      SET rollback_failure_code = @ErrorCode,
                          rollback_failed_at_utc = @FailedAtUtc
                      WHERE job_id = @JobId AND rollback_failure_code IS NULL;",
                    new
                    {
                        JobId = jobId.ToString("D"),
                        ErrorCode = errorCode,
                        FailedAtUtc = failedAtUtc.ToUnixTimeMilliseconds()
                    }) != 1)
            {
                throw new InvalidOperationException("world_operation_rollback_failure_conflict");
            }
        }

        public int RecoverRunning(DateTimeOffset recoveredAtUtc)
        {
            RequireUtc(recoveredAtUtc, nameof(recoveredAtUtc));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE jobs
                  SET status = 'ResultUnknown',
                      completed_at_utc = @CompletedAtUtc,
                      error_code = @ErrorCode,
                      worker_id = NULL,
                      row_version = row_version + 1
                  WHERE kind = 'WorldOperation' AND status = 'Running';",
                new
                {
                    CompletedAtUtc = recoveredAtUtc.ToUnixTimeMilliseconds(),
                    ErrorCode = RestartResultUnknownError
                });
        }

        public WorldOperationPage Query(WorldOperationQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (query.PageSize < 1 || query.PageSize > 100)
                throw new ArgumentOutOfRangeException(nameof(query));
            if (query.FromUtc.HasValue) RequireUtc(query.FromUtc.Value, nameof(query));
            if (query.ToUtc.HasValue) RequireUtc(query.ToUtc.Value, nameof(query));
            if (query.Cursor != null)
            {
                RequireUtc(query.Cursor.CreatedAtUtc, nameof(query));
                RequireText(query.Cursor.OperationId, nameof(query));
            }

            var where = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("Take", query.PageSize + 1);
            if (query.Kind.HasValue)
            {
                where.Add("operation.kind = @Kind");
                parameters.Add("Kind", query.Kind.Value.ToString());
            }
            if (query.Status.HasValue)
            {
                where.Add(EffectiveStatus + " = @Status");
                parameters.Add("Status", query.Status.Value.ToString());
            }
            if (query.FromUtc.HasValue)
            {
                where.Add("operation.created_at_utc >= @FromUtc");
                parameters.Add("FromUtc", query.FromUtc.Value.ToUnixTimeMilliseconds());
            }
            if (query.ToUtc.HasValue)
            {
                where.Add("operation.created_at_utc <= @ToUtc");
                parameters.Add("ToUtc", query.ToUtc.Value.ToUnixTimeMilliseconds());
            }
            if (query.Cursor != null)
            {
                where.Add(@"(operation.created_at_utc < @CursorUtc OR
                    (operation.created_at_utc = @CursorUtc AND operation.operation_id < @CursorId))");
                parameters.Add("CursorUtc", query.Cursor.CreatedAtUtc.ToUnixTimeMilliseconds());
                parameters.Add("CursorId", query.Cursor.OperationId);
            }

            using var connection = connectionFactory.Open();
            var rows = connection.Query<WorldOperationRow>(
                SelectColumns +
                (where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where)) +
                " ORDER BY operation.created_at_utc DESC, operation.operation_id DESC LIMIT @Take;",
                parameters).ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            var nextCursor = rows.Length > query.PageSize && pageRows.Length > 0
                ? new WorldOperationCursor(
                    DateTimeOffset.FromUnixTimeMilliseconds(pageRows[pageRows.Length - 1].CreatedAtUtc),
                    pageRows[pageRows.Length - 1].OperationId)
                : null;
            return new WorldOperationPage(pageRows.Select(ToRecord).ToArray(), nextCursor);
        }

        internal static void InsertOperation(
            IDbConnection connection,
            IDbTransaction transaction,
            string operationId,
            Guid jobId,
            WorldOperationIntent intent)
        {
            connection.Execute(
                @"INSERT INTO world_operations (
                      operation_id, job_id, actor_subject, kind, world_id, world_version,
                      map_resource_version, correlation_id, confirmation_summary,
                      is_reversible, change_set_id, created_at_utc, submission_failure_code,
                      rollback_failure_code, rollback_failed_at_utc)
                  VALUES (@OperationId, @JobId, @ActorSubject, @Kind, @WorldId, @WorldVersion,
                      @MapResourceVersion, @CorrelationId, @ConfirmationSummary,
                      @IsReversible, NULL, @CreatedAtUtc, NULL, NULL, NULL);",
                new
                {
                    OperationId = operationId,
                    JobId = jobId.ToString("D"),
                    intent.ActorSubject,
                    Kind = intent.Kind.ToString(),
                    intent.WorldId,
                    intent.WorldVersion,
                    intent.MapResourceVersion,
                    intent.CorrelationId,
                    intent.ConfirmationSummary,
                    IsReversible = intent.IsReversible ? 1 : 0,
                    CreatedAtUtc = intent.CreatedAtUtc.ToUnixTimeMilliseconds()
                },
                transaction);
        }

        internal static void InsertTarget(
            IDbConnection connection,
            IDbTransaction transaction,
            string operationId,
            WorldOperationKind kind,
            WorldOperationTarget target)
        {
            RequireTargetKind(kind, target);
            switch (target)
            {
                case WorldEntityOperationTarget entity:
                    connection.Execute(
                        @"INSERT INTO world_operation_entity_targets (
                              operation_id, target_id, entity_id, stable_identity,
                              entity_type_resource_id, owner_identity,
                              observed_x, observed_y, observed_z,
                              destination_x, destination_y, destination_z,
                              quantity, radius, entity_category)
                          VALUES (@OperationId, @TargetId, @EntityId, @StableIdentity,
                              @EntityTypeResourceId, @OwnerIdentity,
                              @ObservedX, @ObservedY, @ObservedZ,
                              @DestinationX, @DestinationY, @DestinationZ,
                              @Quantity, @Radius, @EntityCategory);",
                        new
                        {
                            OperationId = operationId,
                            entity.TargetId,
                            entity.EntityId,
                            entity.StableIdentity,
                            entity.EntityTypeResourceId,
                            entity.OwnerIdentity,
                            entity.ObservedX,
                            entity.ObservedY,
                            entity.ObservedZ,
                            entity.DestinationX,
                            entity.DestinationY,
                            entity.DestinationZ,
                            entity.Quantity,
                            entity.Radius,
                            entity.EntityCategory
                        }, transaction);
                    return;
                case WorldMapOperationTarget map:
                    connection.Execute(
                        @"INSERT INTO world_operation_map_targets (
                              operation_id, minimum_x, minimum_z, maximum_x, maximum_z)
                          VALUES (@OperationId, @MinimumX, @MinimumZ, @MaximumX, @MaximumZ);",
                        new { OperationId = operationId, map.MinimumX, map.MinimumZ, map.MaximumX, map.MaximumZ },
                        transaction);
                    return;
                case WorldRegionOperationTarget region:
                    connection.Execute(
                        @"INSERT INTO world_operation_region_targets (
                              operation_id, minimum_x, minimum_y, minimum_z,
                              maximum_x, maximum_y, maximum_z,
                              source_change_set_id, block_internal_name)
                          VALUES (@OperationId, @MinimumX, @MinimumY, @MinimumZ,
                              @MaximumX, @MaximumY, @MaximumZ,
                              @SourceChangeSetId, @BlockInternalName);",
                        new
                        {
                            OperationId = operationId,
                            region.MinimumX,
                            region.MinimumY,
                            region.MinimumZ,
                            region.MaximumX,
                            region.MaximumY,
                            region.MaximumZ,
                            region.SourceChangeSetId,
                            region.BlockInternalName
                        }, transaction);
                    return;
                case WorldBlockOperationTarget block:
                    connection.Execute(
                        @"INSERT INTO world_operation_block_targets (
                              operation_id, x, y, z, block_internal_name, rotation, shape)
                          VALUES (@OperationId, @X, @Y, @Z, @BlockInternalName, @Rotation, @Shape);",
                        new
                        {
                            OperationId = operationId,
                            block.X,
                            block.Y,
                            block.Z,
                            block.BlockInternalName,
                            block.Rotation,
                            block.Shape
                        }, transaction);
                    return;
                case WorldPrefabOperationTarget prefab:
                    connection.Execute(
                        @"INSERT INTO world_operation_prefab_targets (
                              operation_id, prefab_resource_id, prefab_instance_id,
                              anchor_x, anchor_y, anchor_z, rotation,
                              minimum_x, minimum_y, minimum_z,
                              maximum_x, maximum_y, maximum_z)
                          VALUES (@OperationId, @PrefabResourceId, @PrefabInstanceId,
                              @AnchorX, @AnchorY, @AnchorZ, @Rotation,
                              @MinimumX, @MinimumY, @MinimumZ,
                              @MaximumX, @MaximumY, @MaximumZ);",
                        new
                        {
                            OperationId = operationId,
                            prefab.PrefabResourceId,
                            prefab.PrefabInstanceId,
                            prefab.AnchorX,
                            prefab.AnchorY,
                            prefab.AnchorZ,
                            prefab.Rotation,
                            prefab.MinimumX,
                            prefab.MinimumY,
                            prefab.MinimumZ,
                            prefab.MaximumX,
                            prefab.MaximumY,
                            prefab.MaximumZ
                        }, transaction);
                    return;
                case WorldMaintenanceOperationTarget maintenance:
                    connection.Execute(
                        @"INSERT INTO world_operation_maintenance_targets (
                              operation_id, entity_type_resource_id)
                          VALUES (@OperationId, @EntityTypeResourceId);",
                        new { OperationId = operationId, maintenance.EntityTypeResourceId },
                        transaction);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target));
            }
        }

        private static void RequireTargetKind(WorldOperationKind kind, WorldOperationTarget target)
        {
            var valid = target switch
            {
                WorldEntityOperationTarget =>
                    kind == WorldOperationKind.DeleteLandClaim ||
                    kind == WorldOperationKind.MoveOnlinePlayer ||
                    kind == WorldOperationKind.MoveEntity ||
                    kind == WorldOperationKind.SpawnEntity ||
                    kind == WorldOperationKind.DeleteEntity ||
                    kind == WorldOperationKind.CleanupEntities,
                WorldMapOperationTarget =>
                    kind == WorldOperationKind.RefreshMapResources ||
                    kind == WorldOperationKind.RenderExploredMap ||
                    kind == WorldOperationKind.RenderFullMap,
                WorldRegionOperationTarget =>
                    kind == WorldOperationKind.CopyRegion ||
                    kind == WorldOperationKind.FillRegion ||
                    kind == WorldOperationKind.ClearRegion ||
                    kind == WorldOperationKind.PasteRegion ||
                    kind == WorldOperationKind.UndoChangeSet,
                WorldBlockOperationTarget => kind == WorldOperationKind.SetBlock,
                WorldPrefabOperationTarget =>
                    kind == WorldOperationKind.PlacePrefab ||
                    kind == WorldOperationKind.RemovePrefab,
                WorldMaintenanceOperationTarget =>
                    kind == WorldOperationKind.ReloadBlocks ||
                    kind == WorldOperationKind.ReloadItems ||
                    kind == WorldOperationKind.ReloadEntityClasses ||
                    kind == WorldOperationKind.ReloadPrefabs ||
                    kind == WorldOperationKind.CollectGarbage,
                _ => false
            };
            if (!valid) throw new ArgumentException("world_operation_target_kind_mismatch", nameof(target));
        }

        private static WorldOperationTarget ReadTarget(
            IDbConnection connection,
            string operationId,
            WorldOperationKind kind)
        {
            switch (kind)
            {
                case WorldOperationKind.DeleteLandClaim:
                case WorldOperationKind.MoveOnlinePlayer:
                case WorldOperationKind.MoveEntity:
                case WorldOperationKind.SpawnEntity:
                case WorldOperationKind.DeleteEntity:
                case WorldOperationKind.CleanupEntities:
                {
                    var row = RequiredTarget<EntityTargetRow>(connection,
                        @"SELECT target_id AS TargetId, entity_id AS EntityId,
                              stable_identity AS StableIdentity,
                              entity_type_resource_id AS EntityTypeResourceId,
                              owner_identity AS OwnerIdentity,
                              observed_x AS ObservedX, observed_y AS ObservedY, observed_z AS ObservedZ,
                              destination_x AS DestinationX, destination_y AS DestinationY,
                              destination_z AS DestinationZ, quantity AS Quantity,
                              radius AS Radius, entity_category AS EntityCategory
                          FROM world_operation_entity_targets WHERE operation_id = @OperationId;",
                        operationId);
                    return new WorldEntityOperationTarget(
                        row.TargetId, row.EntityId, row.StableIdentity, row.EntityTypeResourceId,
                        row.OwnerIdentity, row.ObservedX, row.ObservedY, row.ObservedZ,
                        row.DestinationX, row.DestinationY, row.DestinationZ,
                        row.Quantity, row.Radius, row.EntityCategory);
                }
                case WorldOperationKind.RefreshMapResources:
                case WorldOperationKind.RenderExploredMap:
                case WorldOperationKind.RenderFullMap:
                {
                    var row = RequiredTarget<MapTargetRow>(connection,
                        @"SELECT minimum_x AS MinimumX, minimum_z AS MinimumZ,
                              maximum_x AS MaximumX, maximum_z AS MaximumZ
                          FROM world_operation_map_targets WHERE operation_id = @OperationId;",
                        operationId);
                    return new WorldMapOperationTarget(
                        row.MinimumX, row.MinimumZ, row.MaximumX, row.MaximumZ);
                }
                case WorldOperationKind.CopyRegion:
                case WorldOperationKind.FillRegion:
                case WorldOperationKind.ClearRegion:
                case WorldOperationKind.PasteRegion:
                case WorldOperationKind.UndoChangeSet:
                {
                    var row = RequiredTarget<RegionTargetRow>(connection,
                        @"SELECT minimum_x AS MinimumX, minimum_y AS MinimumY,
                              minimum_z AS MinimumZ, maximum_x AS MaximumX,
                              maximum_y AS MaximumY, maximum_z AS MaximumZ,
                              source_change_set_id AS SourceChangeSetId,
                              block_internal_name AS BlockInternalName
                          FROM world_operation_region_targets WHERE operation_id = @OperationId;",
                        operationId);
                    return new WorldRegionOperationTarget(
                        row.MinimumX, row.MinimumY, row.MinimumZ,
                        row.MaximumX, row.MaximumY, row.MaximumZ,
                        row.SourceChangeSetId, row.BlockInternalName);
                }
                case WorldOperationKind.SetBlock:
                {
                    var row = RequiredTarget<BlockTargetRow>(connection,
                        @"SELECT x AS X, y AS Y, z AS Z,
                              block_internal_name AS BlockInternalName,
                              rotation AS Rotation, shape AS Shape
                          FROM world_operation_block_targets WHERE operation_id = @OperationId;",
                        operationId);
                    return new WorldBlockOperationTarget(
                        row.X, row.Y, row.Z, row.BlockInternalName, row.Rotation, row.Shape);
                }
                case WorldOperationKind.PlacePrefab:
                case WorldOperationKind.RemovePrefab:
                {
                    var row = RequiredTarget<PrefabTargetRow>(connection,
                        @"SELECT prefab_resource_id AS PrefabResourceId,
                              prefab_instance_id AS PrefabInstanceId,
                              anchor_x AS AnchorX, anchor_y AS AnchorY, anchor_z AS AnchorZ,
                              rotation AS Rotation, minimum_x AS MinimumX,
                              minimum_y AS MinimumY, minimum_z AS MinimumZ,
                              maximum_x AS MaximumX, maximum_y AS MaximumY,
                              maximum_z AS MaximumZ
                          FROM world_operation_prefab_targets WHERE operation_id = @OperationId;",
                        operationId);
                    return new WorldPrefabOperationTarget(
                        row.PrefabResourceId, row.PrefabInstanceId,
                        row.AnchorX, row.AnchorY, row.AnchorZ, row.Rotation,
                        row.MinimumX, row.MinimumY, row.MinimumZ,
                        row.MaximumX, row.MaximumY, row.MaximumZ);
                }
                case WorldOperationKind.ReloadBlocks:
                case WorldOperationKind.ReloadItems:
                case WorldOperationKind.ReloadEntityClasses:
                case WorldOperationKind.ReloadPrefabs:
                case WorldOperationKind.CollectGarbage:
                {
                    var row = RequiredTarget<MaintenanceTargetRow>(connection,
                        @"SELECT entity_type_resource_id AS EntityTypeResourceId
                          FROM world_operation_maintenance_targets WHERE operation_id = @OperationId;",
                        operationId);
                    return new WorldMaintenanceOperationTarget(row.EntityTypeResourceId);
                }
                default:
                    throw new InvalidDataException("world_operation_kind_invalid");
            }
        }

        private static T RequiredTarget<T>(
            IDbConnection connection,
            string sql,
            string operationId)
            where T : class
        {
            var target = connection.QuerySingleOrDefault<T>(sql, new { OperationId = operationId });
            return target ?? throw new InvalidDataException("world_operation_target_missing");
        }

        private static WorldOperationRecord ToRecord(WorldOperationRow row)
        {
            var errorCode = row.RollbackFailureCode ?? row.SubmissionFailureCode ?? row.JobErrorCode;
            var completedAt = row.RollbackFailedAtUtc ?? row.JobCompletedAtUtc;
            var status = row.RollbackFailureCode != null
                ? WorldOperationStatus.RollbackFailed
                : row.SubmissionFailureCode != null
                    ? WorldOperationStatus.Failed
                    : string.Equals(row.JobStatus, "PendingRestart", StringComparison.Ordinal)
                        ? WorldOperationStatus.Interrupted
                        : (WorldOperationStatus)Enum.Parse(typeof(WorldOperationStatus), row.JobStatus);
            return new WorldOperationRecord(
                row.OperationId,
                Guid.Parse(row.JobId),
                row.ActorSubject,
                (WorldOperationKind)Enum.Parse(typeof(WorldOperationKind), row.Kind),
                row.WorldId,
                row.WorldVersion,
                row.MapResourceVersion,
                row.CorrelationId,
                row.ConfirmationSummary,
                row.IsReversible != 0,
                row.ChangeSetId,
                status,
                row.ProgressCurrent.HasValue || row.ProgressTotal.HasValue
                    ? new WorldOperationProgress(row.ProgressCurrent, row.ProgressTotal)
                    : null,
                errorCode,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                row.StartedAtUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(row.StartedAtUtc.Value)
                    : null,
                completedAt.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(completedAt.Value)
                    : null);
        }

        internal static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            var normalized = value.Trim();
            if (normalized.Length > 200)
                throw new ArgumentOutOfRangeException(parameterName);
            return normalized;
        }

        internal static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        private sealed class WorldOperationRow
        {
            public string OperationId { get; set; } = string.Empty;
            public string JobId { get; set; } = string.Empty;
            public string ActorSubject { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public string WorldId { get; set; } = string.Empty;
            public string WorldVersion { get; set; } = string.Empty;
            public string? MapResourceVersion { get; set; }
            public string CorrelationId { get; set; } = string.Empty;
            public string ConfirmationSummary { get; set; } = string.Empty;
            public int IsReversible { get; set; }
            public string? ChangeSetId { get; set; }
            public string JobStatus { get; set; } = string.Empty;
            public long? ProgressCurrent { get; set; }
            public long? ProgressTotal { get; set; }
            public string? JobErrorCode { get; set; }
            public string? SubmissionFailureCode { get; set; }
            public string? RollbackFailureCode { get; set; }
            public long CreatedAtUtc { get; set; }
            public long? StartedAtUtc { get; set; }
            public long? JobCompletedAtUtc { get; set; }
            public long? RollbackFailedAtUtc { get; set; }
        }

        private sealed class WorldOperationIntentRow
        {
            public string OperationId { get; set; } = string.Empty;
            public string JobId { get; set; } = string.Empty;
            public string ActorSubject { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public string WorldId { get; set; } = string.Empty;
            public string WorldVersion { get; set; } = string.Empty;
            public string? MapResourceVersion { get; set; }
            public string CorrelationId { get; set; } = string.Empty;
            public string ConfirmationSummary { get; set; } = string.Empty;
            public int IsReversible { get; set; }
            public long CreatedAtUtc { get; set; }
        }

        private sealed class EntityTargetRow
        {
            public string TargetId { get; set; } = string.Empty;
            public long? EntityId { get; set; }
            public string? StableIdentity { get; set; }
            public string? EntityTypeResourceId { get; set; }
            public string? OwnerIdentity { get; set; }
            public double? ObservedX { get; set; }
            public double? ObservedY { get; set; }
            public double? ObservedZ { get; set; }
            public double? DestinationX { get; set; }
            public double? DestinationY { get; set; }
            public double? DestinationZ { get; set; }
            public int? Quantity { get; set; }
            public double? Radius { get; set; }
            public string? EntityCategory { get; set; }
        }

        private sealed class MapTargetRow
        {
            public int? MinimumX { get; set; }
            public int? MinimumZ { get; set; }
            public int? MaximumX { get; set; }
            public int? MaximumZ { get; set; }
        }

        private sealed class RegionTargetRow
        {
            public int MinimumX { get; set; }
            public int MinimumY { get; set; }
            public int MinimumZ { get; set; }
            public int MaximumX { get; set; }
            public int MaximumY { get; set; }
            public int MaximumZ { get; set; }
            public string? SourceChangeSetId { get; set; }
            public string? BlockInternalName { get; set; }
        }

        private sealed class BlockTargetRow
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Z { get; set; }
            public string BlockInternalName { get; set; } = string.Empty;
            public int Rotation { get; set; }
            public string? Shape { get; set; }
        }

        private sealed class PrefabTargetRow
        {
            public string PrefabResourceId { get; set; } = string.Empty;
            public string? PrefabInstanceId { get; set; }
            public int AnchorX { get; set; }
            public int AnchorY { get; set; }
            public int AnchorZ { get; set; }
            public int Rotation { get; set; }
            public int? MinimumX { get; set; }
            public int? MinimumY { get; set; }
            public int? MinimumZ { get; set; }
            public int? MaximumX { get; set; }
            public int? MaximumY { get; set; }
            public int? MaximumZ { get; set; }
        }

        private sealed class MaintenanceTargetRow
        {
            public string? EntityTypeResourceId { get; set; }
        }
    }
}
