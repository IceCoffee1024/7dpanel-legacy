using System;
using Dapper;
using LSTY.SevenDPanel.Application;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class PlayerActionIdempotencyConflictException : InvalidOperationException
    {
        public PlayerActionIdempotencyConflictException(
            string operatorId,
            string clientRequestKey,
            string existingOperationId)
            : base("The client request key is already associated with different player action parameters.")
        {
            OperatorId = operatorId;
            ClientRequestKey = clientRequestKey;
            ExistingOperationId = existingOperationId;
        }

        public string OperatorId { get; }
        public string ClientRequestKey { get; }
        public string ExistingOperationId { get; }
    }

    public sealed class PlayerActionOperationIdConflictException : InvalidOperationException
    {
        public PlayerActionOperationIdConflictException(string operationId)
            : base("The player action operation ID is already in use.")
        {
            OperationId = operationId;
        }

        public string OperationId { get; }
    }

    public sealed class PlayerActionOperationCompletion
    {
        public PlayerActionOperationCompletion(
            string operationId,
            PlayerActionStatus status,
            DateTimeOffset completedAtUtc,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId,
            long? beforeSkillSnapshotId,
            long? afterSkillSnapshotId)
        {
            if (status == PlayerActionStatus.Pending || !Enum.IsDefined(typeof(PlayerActionStatus), status))
                throw new ArgumentException("A terminal player action status is required.", nameof(status));
            OperationId = PlayerActionStoreValidation.RequireText(operationId, nameof(operationId));
            Status = status;
            CompletedAtUtc = PlayerActionStoreValidation.RequireUtc(completedAtUtc, nameof(completedAtUtc));
            FailureCode = PlayerActionStoreValidation.OptionalText(failureCode, nameof(failureCode));
            BeforeInventorySnapshotId = PlayerActionStoreValidation.RequireOptionalId(
                beforeInventorySnapshotId, nameof(beforeInventorySnapshotId));
            AfterInventorySnapshotId = PlayerActionStoreValidation.RequireOptionalId(
                afterInventorySnapshotId, nameof(afterInventorySnapshotId));
            BeforeSkillSnapshotId = PlayerActionStoreValidation.RequireOptionalId(
                beforeSkillSnapshotId, nameof(beforeSkillSnapshotId));
            AfterSkillSnapshotId = PlayerActionStoreValidation.RequireOptionalId(
                afterSkillSnapshotId, nameof(afterSkillSnapshotId));
        }

        public string OperationId { get; }
        public PlayerActionStatus Status { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
        public long? BeforeSkillSnapshotId { get; }
        public long? AfterSkillSnapshotId { get; }
    }

    public sealed class GrantItemOperationIntent
    {
        private readonly CommonOperationIntent common;

        public GrantItemOperationIntent(
            string operationId,
            string operatorId,
            PlayerTargetStamp target,
            string clientRequestKey,
            string? correlationId,
            DateTimeOffset createdAtUtc,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId,
            string catalogVersion,
            string internalName,
            string itemKind,
            int quantity,
            int? quality,
            bool hiddenItemConfirmed,
            string? resourceId = null,
            string? gameVersion = null,
            int? numericId = null)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (quality < 0) throw new ArgumentOutOfRangeException(nameof(quality));
            if (numericId < 0) throw new ArgumentOutOfRangeException(nameof(numericId));
            common = new CommonOperationIntent(
                operationId, operatorId, target, clientRequestKey, correlationId,
                createdAtUtc, beforeInventorySnapshotId, beforeSkillSnapshotId);
            CatalogVersion = PlayerActionStoreValidation.RequireText(catalogVersion, nameof(catalogVersion));
            InternalName = PlayerActionStoreValidation.RequireText(internalName, nameof(internalName));
            ItemKind = PlayerActionStoreValidation.RequireText(itemKind, nameof(itemKind));
            Quantity = quantity;
            Quality = quality;
            HiddenItemConfirmed = hiddenItemConfirmed;
            ResourceId = PlayerActionStoreValidation.OptionalText(resourceId, nameof(resourceId));
            GameVersion = PlayerActionStoreValidation.OptionalText(gameVersion, nameof(gameVersion));
            NumericId = numericId;
        }

        internal CommonOperationIntent Common => common;
        public string OperationId => common.OperationId;
        public string OperatorId => common.OperatorId;
        public PlayerTargetStamp Target => common.Target;
        public string ClientRequestKey => common.ClientRequestKey;
        public string? CorrelationId => common.CorrelationId;
        public DateTimeOffset CreatedAtUtc => common.CreatedAtUtc;
        public long? BeforeInventorySnapshotId => common.BeforeInventorySnapshotId;
        public long? BeforeSkillSnapshotId => common.BeforeSkillSnapshotId;
        public string CatalogVersion { get; }
        public string InternalName { get; }
        public string ItemKind { get; }
        public int Quantity { get; }
        public int? Quality { get; }
        public bool HiddenItemConfirmed { get; }
        public string? ResourceId { get; }
        public string? GameVersion { get; }
        public int? NumericId { get; }
    }

    public sealed class RemoveItemOperationIntent
    {
        private readonly CommonOperationIntent common;

        public RemoveItemOperationIntent(
            string operationId,
            string operatorId,
            PlayerTargetStamp target,
            string clientRequestKey,
            string? correlationId,
            DateTimeOffset createdAtUtc,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId,
            string catalogVersion,
            string internalName,
            string itemKind,
            int quantity,
            int? quality,
            PlayerItemRemovalScope removalScope,
            PlayerItemRemovalMode removalMode,
            string? resourceId = null)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (quality < 0) throw new ArgumentOutOfRangeException(nameof(quality));
            if (!Enum.IsDefined(typeof(PlayerItemRemovalScope), removalScope))
                throw new ArgumentOutOfRangeException(nameof(removalScope));
            if (!Enum.IsDefined(typeof(PlayerItemRemovalMode), removalMode))
                throw new ArgumentOutOfRangeException(nameof(removalMode));
            common = new CommonOperationIntent(
                operationId, operatorId, target, clientRequestKey, correlationId,
                createdAtUtc, beforeInventorySnapshotId, beforeSkillSnapshotId);
            CatalogVersion = PlayerActionStoreValidation.RequireText(catalogVersion, nameof(catalogVersion));
            InternalName = PlayerActionStoreValidation.RequireText(internalName, nameof(internalName));
            ItemKind = PlayerActionStoreValidation.RequireText(itemKind, nameof(itemKind));
            Quantity = quantity;
            Quality = quality;
            RemovalScope = removalScope;
            RemovalMode = removalMode;
            ResourceId = PlayerActionStoreValidation.OptionalText(resourceId, nameof(resourceId));
        }

        internal CommonOperationIntent Common => common;
        public string OperationId => common.OperationId;
        public string OperatorId => common.OperatorId;
        public PlayerTargetStamp Target => common.Target;
        public string ClientRequestKey => common.ClientRequestKey;
        public string? CorrelationId => common.CorrelationId;
        public DateTimeOffset CreatedAtUtc => common.CreatedAtUtc;
        public long? BeforeInventorySnapshotId => common.BeforeInventorySnapshotId;
        public long? BeforeSkillSnapshotId => common.BeforeSkillSnapshotId;
        public string CatalogVersion { get; }
        public string InternalName { get; }
        public string ItemKind { get; }
        public int Quantity { get; }
        public int? Quality { get; }
        public PlayerItemRemovalScope RemovalScope { get; }
        public PlayerItemRemovalMode RemovalMode { get; }
        public string? ResourceId { get; }
    }

    public sealed class ResetSkillsOperationIntent
    {
        private readonly CommonOperationIntent common;

        public ResetSkillsOperationIntent(
            string operationId,
            string operatorId,
            PlayerTargetStamp target,
            string clientRequestKey,
            string? correlationId,
            DateTimeOffset createdAtUtc,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId,
            bool dangerConfirmed)
        {
            common = new CommonOperationIntent(
                operationId, operatorId, target, clientRequestKey, correlationId,
                createdAtUtc, beforeInventorySnapshotId, beforeSkillSnapshotId);
            DangerConfirmed = dangerConfirmed;
        }

        internal CommonOperationIntent Common => common;
        public string OperationId => common.OperationId;
        public string OperatorId => common.OperatorId;
        public PlayerTargetStamp Target => common.Target;
        public string ClientRequestKey => common.ClientRequestKey;
        public string? CorrelationId => common.CorrelationId;
        public DateTimeOffset CreatedAtUtc => common.CreatedAtUtc;
        public long? BeforeInventorySnapshotId => common.BeforeInventorySnapshotId;
        public long? BeforeSkillSnapshotId => common.BeforeSkillSnapshotId;
        public bool DangerConfirmed { get; }
    }

    public sealed class ClearInventoryOperationIntent
    {
        private readonly CommonOperationIntent common;

        public ClearInventoryOperationIntent(
            string operationId,
            string operatorId,
            PlayerTargetStamp target,
            string clientRequestKey,
            string? correlationId,
            DateTimeOffset createdAtUtc,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId,
            PlayerItemRemovalScope removalScope,
            bool dangerConfirmed)
        {
            if (!Enum.IsDefined(typeof(PlayerItemRemovalScope), removalScope))
                throw new ArgumentOutOfRangeException(nameof(removalScope));
            common = new CommonOperationIntent(
                operationId, operatorId, target, clientRequestKey, correlationId,
                createdAtUtc, beforeInventorySnapshotId, beforeSkillSnapshotId);
            RemovalScope = removalScope;
            DangerConfirmed = dangerConfirmed;
        }

        internal CommonOperationIntent Common => common;
        public string OperationId => common.OperationId;
        public string OperatorId => common.OperatorId;
        public PlayerTargetStamp Target => common.Target;
        public string ClientRequestKey => common.ClientRequestKey;
        public string? CorrelationId => common.CorrelationId;
        public DateTimeOffset CreatedAtUtc => common.CreatedAtUtc;
        public long? BeforeInventorySnapshotId => common.BeforeInventorySnapshotId;
        public long? BeforeSkillSnapshotId => common.BeforeSkillSnapshotId;
        public PlayerItemRemovalScope RemovalScope { get; }
        public bool DangerConfirmed { get; }
    }

    public sealed class ResetPlayerDataOperationIntent
    {
        private readonly CommonOperationIntent common;

        public ResetPlayerDataOperationIntent(
            string operationId,
            string operatorId,
            PlayerTargetStamp target,
            string clientRequestKey,
            string? correlationId,
            DateTimeOffset createdAtUtc,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId,
            bool dangerConfirmed)
        {
            common = new CommonOperationIntent(
                operationId, operatorId, target, clientRequestKey, correlationId,
                createdAtUtc, beforeInventorySnapshotId, beforeSkillSnapshotId);
            DangerConfirmed = dangerConfirmed;
        }

        internal CommonOperationIntent Common => common;
        public string OperationId => common.OperationId;
        public string OperatorId => common.OperatorId;
        public PlayerTargetStamp Target => common.Target;
        public string ClientRequestKey => common.ClientRequestKey;
        public string? CorrelationId => common.CorrelationId;
        public DateTimeOffset CreatedAtUtc => common.CreatedAtUtc;
        public long? BeforeInventorySnapshotId => common.BeforeInventorySnapshotId;
        public long? BeforeSkillSnapshotId => common.BeforeSkillSnapshotId;
        public bool DangerConfirmed { get; }
    }

    public sealed class SqliteGrantItemOperationStore
    {
        private readonly PlayerActionStoreCore core;

        public SqliteGrantItemOperationStore(SqliteConnectionFactory connectionFactory) =>
            core = new PlayerActionStoreCore(
                connectionFactory,
                "player_grant_item_operations",
                PlayerActionOperationTypes.GrantItem);

        public PlayerActionOperation CreatePending(GrantItemOperationIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            var parameters = intent.Common.CreateParameters();
            parameters.Add("CatalogVersion", intent.CatalogVersion);
            parameters.Add("InternalName", intent.InternalName);
            parameters.Add("ItemKind", intent.ItemKind);
            parameters.Add("Quantity", intent.Quantity);
            parameters.Add("Quality", intent.Quality);
            parameters.Add("HiddenItemConfirmed", intent.HiddenItemConfirmed ? 1 : 0);
            parameters.Add("ResourceId", intent.ResourceId);
            parameters.Add("GameVersion", intent.GameVersion);
            parameters.Add("NumericId", intent.NumericId);
            return core.CreatePending<GrantOperationRow>(
                intent.Common,
                @"catalog_version AS CatalogVersion, internal_name AS InternalName,
                  item_kind AS ItemKind, quantity AS Quantity, quality AS Quality,
                  hidden_item_confirmed AS HiddenItemConfirmed, resource_id AS ResourceId,
                  game_version AS GameVersion, numeric_id AS NumericId",
                "catalog_version, internal_name, item_kind, quantity, quality, hidden_item_confirmed, " +
                "resource_id, game_version, numeric_id",
                "@CatalogVersion, @InternalName, @ItemKind, @Quantity, @Quality, @HiddenItemConfirmed, " +
                "@ResourceId, @GameVersion, @NumericId",
                parameters,
                row =>
                    string.Equals(row.CatalogVersion, intent.CatalogVersion, StringComparison.Ordinal)
                    && string.Equals(row.InternalName, intent.InternalName, StringComparison.Ordinal)
                    && string.Equals(row.ItemKind, intent.ItemKind, StringComparison.Ordinal)
                    && row.Quantity == intent.Quantity
                    && row.Quality == intent.Quality
                    && row.HiddenItemConfirmed == (intent.HiddenItemConfirmed ? 1 : 0)
                    && string.Equals(row.ResourceId, intent.ResourceId, StringComparison.Ordinal)
                    && string.Equals(row.GameVersion, intent.GameVersion, StringComparison.Ordinal)
                    && row.NumericId == intent.NumericId);
        }

        public bool TryStart(string operationId, DateTimeOffset startedAtUtc) =>
            core.TryStart(operationId, startedAtUtc);

        public bool TryComplete(
            PlayerActionOperationCompletion completion,
            int? actualQuantity = null)
        {
            if (actualQuantity < 0) throw new ArgumentOutOfRangeException(nameof(actualQuantity));
            var parameters = new DynamicParameters();
            parameters.Add("ActualQuantity", actualQuantity);
            return core.TryComplete(completion, "actual_quantity = @ActualQuantity", parameters);
        }
    }

    public sealed class SqliteRemoveItemOperationStore
    {
        private readonly PlayerActionStoreCore core;

        public SqliteRemoveItemOperationStore(SqliteConnectionFactory connectionFactory) =>
            core = new PlayerActionStoreCore(
                connectionFactory,
                "player_remove_item_operations",
                PlayerActionOperationTypes.RemoveItem);

        public PlayerActionOperation CreatePending(RemoveItemOperationIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            var parameters = intent.Common.CreateParameters();
            parameters.Add("CatalogVersion", intent.CatalogVersion);
            parameters.Add("InternalName", intent.InternalName);
            parameters.Add("ItemKind", intent.ItemKind);
            parameters.Add("Quantity", intent.Quantity);
            parameters.Add("Quality", intent.Quality);
            parameters.Add("RemovalScope", intent.RemovalScope.ToString());
            parameters.Add("RemovalMode", intent.RemovalMode.ToString());
            parameters.Add("ResourceId", intent.ResourceId);
            return core.CreatePending<RemoveOperationRow>(
                intent.Common,
                @"catalog_version AS CatalogVersion, internal_name AS InternalName,
                  item_kind AS ItemKind, quantity AS Quantity, quality AS Quality,
                  removal_scope AS RemovalScope, removal_mode AS RemovalMode,
                  resource_id AS ResourceId",
                "catalog_version, internal_name, item_kind, quantity, quality, removal_scope, removal_mode, " +
                "resource_id",
                "@CatalogVersion, @InternalName, @ItemKind, @Quantity, @Quality, @RemovalScope, @RemovalMode, " +
                "@ResourceId",
                parameters,
                row =>
                    string.Equals(row.CatalogVersion, intent.CatalogVersion, StringComparison.Ordinal)
                    && string.Equals(row.InternalName, intent.InternalName, StringComparison.Ordinal)
                    && string.Equals(row.ItemKind, intent.ItemKind, StringComparison.Ordinal)
                    && row.Quantity == intent.Quantity
                    && row.Quality == intent.Quality
                    && string.Equals(row.RemovalScope, intent.RemovalScope.ToString(), StringComparison.Ordinal)
                    && string.Equals(row.RemovalMode, intent.RemovalMode.ToString(), StringComparison.Ordinal)
                    && string.Equals(row.ResourceId, intent.ResourceId, StringComparison.Ordinal));
        }

        public bool TryStart(string operationId, DateTimeOffset startedAtUtc) =>
            core.TryStart(operationId, startedAtUtc);

        public bool TryComplete(
            PlayerActionOperationCompletion completion,
            int? actualQuantity = null)
        {
            if (actualQuantity < 0) throw new ArgumentOutOfRangeException(nameof(actualQuantity));
            var parameters = new DynamicParameters();
            parameters.Add("ActualQuantity", actualQuantity);
            return core.TryComplete(completion, "actual_quantity = @ActualQuantity", parameters);
        }
    }

    public sealed class SqliteResetSkillsOperationStore
    {
        private readonly PlayerActionStoreCore core;

        public SqliteResetSkillsOperationStore(SqliteConnectionFactory connectionFactory) =>
            core = new PlayerActionStoreCore(
                connectionFactory,
                "player_reset_skills_operations",
                PlayerActionOperationTypes.ResetSkills);

        public PlayerActionOperation CreatePending(ResetSkillsOperationIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            var parameters = intent.Common.CreateParameters();
            parameters.Add("DangerConfirmed", intent.DangerConfirmed ? 1 : 0);
            return core.CreatePending<DangerOperationRow>(
                intent.Common,
                "danger_confirmed AS DangerConfirmed",
                "danger_confirmed",
                "@DangerConfirmed",
                parameters,
                row => row.DangerConfirmed == (intent.DangerConfirmed ? 1 : 0));
        }

        public bool TryStart(string operationId, DateTimeOffset startedAtUtc) =>
            core.TryStart(operationId, startedAtUtc);

        public bool TryComplete(PlayerActionOperationCompletion completion) =>
            core.TryComplete(completion, string.Empty, new DynamicParameters());
    }

    public sealed class SqliteClearInventoryOperationStore
    {
        private readonly PlayerActionStoreCore core;

        public SqliteClearInventoryOperationStore(SqliteConnectionFactory connectionFactory) =>
            core = new PlayerActionStoreCore(
                connectionFactory,
                "player_clear_inventory_operations",
                PlayerActionOperationTypes.ClearInventory);

        public PlayerActionOperation CreatePending(ClearInventoryOperationIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            var parameters = intent.Common.CreateParameters();
            parameters.Add("RemovalScope", intent.RemovalScope.ToString());
            parameters.Add("DangerConfirmed", intent.DangerConfirmed ? 1 : 0);
            return core.CreatePending<ClearOperationRow>(
                intent.Common,
                "removal_scope AS RemovalScope, danger_confirmed AS DangerConfirmed",
                "removal_scope, danger_confirmed",
                "@RemovalScope, @DangerConfirmed",
                parameters,
                row =>
                    string.Equals(row.RemovalScope, intent.RemovalScope.ToString(), StringComparison.Ordinal)
                    && row.DangerConfirmed == (intent.DangerConfirmed ? 1 : 0));
        }

        public bool TryStart(string operationId, DateTimeOffset startedAtUtc) =>
            core.TryStart(operationId, startedAtUtc);

        public bool TryComplete(PlayerActionOperationCompletion completion) =>
            core.TryComplete(completion, string.Empty, new DynamicParameters());
    }

    public sealed class SqliteResetPlayerDataOperationStore
    {
        private readonly PlayerActionStoreCore core;

        public SqliteResetPlayerDataOperationStore(SqliteConnectionFactory connectionFactory) =>
            core = new PlayerActionStoreCore(
                connectionFactory,
                "player_reset_data_operations",
                PlayerActionOperationTypes.ResetPlayerData);

        public PlayerActionOperation CreatePending(ResetPlayerDataOperationIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            var parameters = intent.Common.CreateParameters();
            parameters.Add("DangerConfirmed", intent.DangerConfirmed ? 1 : 0);
            return core.CreatePending<DangerOperationRow>(
                intent.Common,
                "danger_confirmed AS DangerConfirmed",
                "danger_confirmed",
                "@DangerConfirmed",
                parameters,
                row => row.DangerConfirmed == (intent.DangerConfirmed ? 1 : 0));
        }

        public bool TryStart(string operationId, DateTimeOffset startedAtUtc) =>
            core.TryStart(operationId, startedAtUtc);

        public bool TryComplete(PlayerActionOperationCompletion completion) =>
            core.TryComplete(completion, string.Empty, new DynamicParameters());
    }

    public sealed class SqlitePlayerActionOperationQuery : IPlayerActionOperationQuery
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqlitePlayerActionOperationQuery(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
        }

        public PlayerActionOperation? Get(string operationId)
        {
            var id = PlayerActionStoreValidation.RequireText(operationId, nameof(operationId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<CommonOperationRow>(
                "SELECT * FROM (" + PlayerActionSql.SummaryUnion + ") WHERE OperationId = @OperationId;",
                new { OperationId = id });
            return row == null ? null : PlayerActionSql.ToSummary(row.OperationType, row);
        }
    }

    internal sealed class CommonOperationIntent
    {
        public CommonOperationIntent(
            string operationId,
            string operatorId,
            PlayerTargetStamp target,
            string clientRequestKey,
            string? correlationId,
            DateTimeOffset createdAtUtc,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId)
        {
            OperationId = PlayerActionStoreValidation.RequireText(operationId, nameof(operationId));
            OperatorId = PlayerActionStoreValidation.RequireText(operatorId, nameof(operatorId));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            ClientRequestKey = PlayerActionStoreValidation.RequireText(
                clientRequestKey, nameof(clientRequestKey));
            CorrelationId = PlayerActionStoreValidation.OptionalText(correlationId, nameof(correlationId));
            CreatedAtUtc = PlayerActionStoreValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            BeforeInventorySnapshotId = PlayerActionStoreValidation.RequireOptionalId(
                beforeInventorySnapshotId, nameof(beforeInventorySnapshotId));
            BeforeSkillSnapshotId = PlayerActionStoreValidation.RequireOptionalId(
                beforeSkillSnapshotId, nameof(beforeSkillSnapshotId));
        }

        public string OperationId { get; }
        public string OperatorId { get; }
        public PlayerTargetStamp Target { get; }
        public string ClientRequestKey { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? BeforeSkillSnapshotId { get; }

        public DynamicParameters CreateParameters()
        {
            var parameters = new DynamicParameters();
            parameters.Add("OperationId", OperationId);
            parameters.Add("OperatorId", OperatorId);
            parameters.Add("TargetCrossplatformId", Target.CrossplatformId);
            parameters.Add("TargetEntityId", Target.EntityId);
            parameters.Add("TargetOnlineObservedAtUtc", PlayerActionSql.ToUnixMilliseconds(Target.OnlineObservedAtUtc));
            parameters.Add("WorldId", Target.WorldId);
            parameters.Add("ClientRequestKey", ClientRequestKey);
            parameters.Add("CorrelationId", CorrelationId);
            parameters.Add("CreatedAtUtc", PlayerActionSql.ToUnixMilliseconds(CreatedAtUtc));
            parameters.Add("BeforeInventorySnapshotId", BeforeInventorySnapshotId);
            parameters.Add("BeforeSkillSnapshotId", BeforeSkillSnapshotId);
            return parameters;
        }
    }

    internal sealed class PlayerActionStoreCore
    {
        private readonly SqliteConnectionFactory connectionFactory;
        private readonly string table;
        private readonly string operationType;

        public PlayerActionStoreCore(
            SqliteConnectionFactory connectionFactory,
            string table,
            string operationType)
        {
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
            this.table = table;
            this.operationType = operationType;
        }

        public PlayerActionOperation CreatePending<TRow>(
            CommonOperationIntent intent,
            string typedSelect,
            string typedColumns,
            string typedValues,
            DynamicParameters parameters,
            Func<TRow, bool> typedMatches)
            where TRow : CommonOperationRow
        {
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var existing = connection.QuerySingleOrDefault<TRow>(
                "SELECT " + PlayerActionSql.CommonSelect + ", " + typedSelect +
                " FROM " + table +
                " WHERE operator_id = @OperatorId AND client_request_key = @ClientRequestKey;",
                new { intent.OperatorId, intent.ClientRequestKey },
                transaction);
            if (existing != null)
            {
                if (!CommonMatches(existing, intent) || !typedMatches(existing))
                    throw new PlayerActionIdempotencyConflictException(
                        intent.OperatorId,
                        intent.ClientRequestKey,
                        existing.OperationId);
                transaction.Commit();
                return PlayerActionSql.ToSummary(operationType, existing);
            }

            if (PlayerActionSql.OperationIdExists(connection, transaction, intent.OperationId))
                throw new PlayerActionOperationIdConflictException(intent.OperationId);
            connection.Execute(
                "INSERT INTO " + table + " (" + PlayerActionSql.CommonInsertColumns + ", " +
                typedColumns + ") VALUES (" + PlayerActionSql.CommonInsertValues + ", " +
                typedValues + ");",
                parameters,
                transaction);
            transaction.Commit();
            return new PlayerActionOperation(
                intent.OperationId,
                operationType,
                intent.OperatorId,
                intent.Target,
                PlayerActionStatus.Pending,
                intent.CreatedAtUtc,
                null,
                null,
                null,
                intent.BeforeInventorySnapshotId,
                null,
                intent.BeforeSkillSnapshotId,
                null,
                intent.CorrelationId);
        }

        public bool TryStart(string operationId, DateTimeOffset startedAtUtc)
        {
            var id = PlayerActionStoreValidation.RequireText(operationId, nameof(operationId));
            var started = PlayerActionStoreValidation.RequireUtc(startedAtUtc, nameof(startedAtUtc));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                "UPDATE " + table + " SET started_at_utc = @StartedAtUtc " +
                "WHERE operation_id = @OperationId AND status = 'Pending' " +
                "AND started_at_utc IS NULL;",
                new
                {
                    OperationId = id,
                    StartedAtUtc = PlayerActionSql.ToUnixMilliseconds(started)
                }) == 1;
        }

        public bool TryComplete(
            PlayerActionOperationCompletion completion,
            string typedAssignment,
            DynamicParameters typedParameters)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            var parameters = typedParameters;
            parameters.Add("OperationId", completion.OperationId);
            parameters.Add("Status", completion.Status.ToString());
            parameters.Add("CompletedAtUtc", PlayerActionSql.ToUnixMilliseconds(completion.CompletedAtUtc));
            parameters.Add("FailureCode", completion.FailureCode);
            parameters.Add("BeforeInventorySnapshotId", completion.BeforeInventorySnapshotId);
            parameters.Add("AfterInventorySnapshotId", completion.AfterInventorySnapshotId);
            parameters.Add("BeforeSkillSnapshotId", completion.BeforeSkillSnapshotId);
            parameters.Add("AfterSkillSnapshotId", completion.AfterSkillSnapshotId);
            using var connection = connectionFactory.Open();
            return connection.Execute(
                "UPDATE " + table + " SET status = @Status, completed_at_utc = @CompletedAtUtc, " +
                "failure_code = @FailureCode, " +
                "before_inventory_snapshot_id = COALESCE(@BeforeInventorySnapshotId, before_inventory_snapshot_id), " +
                "after_inventory_snapshot_id = COALESCE(@AfterInventorySnapshotId, after_inventory_snapshot_id), " +
                "before_skill_snapshot_id = COALESCE(@BeforeSkillSnapshotId, before_skill_snapshot_id), " +
                "after_skill_snapshot_id = COALESCE(@AfterSkillSnapshotId, after_skill_snapshot_id)" +
                (typedAssignment.Length == 0 ? string.Empty : ", " + typedAssignment) +
                " WHERE operation_id = @OperationId AND status = 'Pending';",
                parameters) == 1;
        }

        private static bool CommonMatches(CommonOperationRow row, CommonOperationIntent intent) =>
            string.Equals(row.OperatorId, intent.OperatorId, StringComparison.Ordinal)
            && string.Equals(row.TargetCrossplatformId, intent.Target.CrossplatformId, StringComparison.Ordinal)
            && row.TargetEntityId == intent.Target.EntityId
            && row.TargetOnlineObservedAtUtc == PlayerActionSql.ToUnixMilliseconds(intent.Target.OnlineObservedAtUtc)
            && string.Equals(row.WorldId, intent.Target.WorldId, StringComparison.Ordinal)
            && string.Equals(row.ClientRequestKey, intent.ClientRequestKey, StringComparison.Ordinal);
    }

    internal static class PlayerActionSql
    {
        public const string CommonSelect = @"
            operation_id AS OperationId, operator_id AS OperatorId,
            target_crossplatform_id AS TargetCrossplatformId,
            target_entity_id AS TargetEntityId,
            target_online_observed_at_utc AS TargetOnlineObservedAtUtc,
            world_id AS WorldId, client_request_key AS ClientRequestKey,
            correlation_id AS CorrelationId, status AS Status,
            created_at_utc AS CreatedAtUtc, started_at_utc AS StartedAtUtc,
            completed_at_utc AS CompletedAtUtc, failure_code AS FailureCode,
            before_inventory_snapshot_id AS BeforeInventorySnapshotId,
            after_inventory_snapshot_id AS AfterInventorySnapshotId,
            before_skill_snapshot_id AS BeforeSkillSnapshotId,
            after_skill_snapshot_id AS AfterSkillSnapshotId";

        public const string CommonInsertColumns = @"
            operation_id, operator_id, target_crossplatform_id, target_entity_id,
            target_online_observed_at_utc, world_id, client_request_key,
            correlation_id, status, created_at_utc, started_at_utc, completed_at_utc,
            failure_code, before_inventory_snapshot_id, after_inventory_snapshot_id,
            before_skill_snapshot_id, after_skill_snapshot_id";

        public const string CommonInsertValues = @"
            @OperationId, @OperatorId, @TargetCrossplatformId, @TargetEntityId,
            @TargetOnlineObservedAtUtc, @WorldId, @ClientRequestKey,
            @CorrelationId, 'Pending', @CreatedAtUtc, NULL, NULL,
            NULL, @BeforeInventorySnapshotId, NULL, @BeforeSkillSnapshotId, NULL";

        public const string SummaryUnion = @"
            SELECT operation_id AS OperationId, 'GrantItem' AS OperationType,
                   operator_id AS OperatorId, target_crossplatform_id AS TargetCrossplatformId,
                   target_entity_id AS TargetEntityId,
                   target_online_observed_at_utc AS TargetOnlineObservedAtUtc,
                   world_id AS WorldId, client_request_key AS ClientRequestKey,
                   correlation_id AS CorrelationId, status AS Status,
                   created_at_utc AS CreatedAtUtc, started_at_utc AS StartedAtUtc,
                   completed_at_utc AS CompletedAtUtc, failure_code AS FailureCode,
                   before_inventory_snapshot_id AS BeforeInventorySnapshotId,
                   after_inventory_snapshot_id AS AfterInventorySnapshotId,
                   before_skill_snapshot_id AS BeforeSkillSnapshotId,
                   after_skill_snapshot_id AS AfterSkillSnapshotId
            FROM player_grant_item_operations
            UNION ALL
            SELECT operation_id, 'RemoveItem', operator_id, target_crossplatform_id,
                   target_entity_id, target_online_observed_at_utc, world_id,
                   client_request_key, correlation_id, status, created_at_utc,
                   started_at_utc, completed_at_utc, failure_code,
                   before_inventory_snapshot_id, after_inventory_snapshot_id,
                   before_skill_snapshot_id, after_skill_snapshot_id
            FROM player_remove_item_operations
            UNION ALL
            SELECT operation_id, 'ResetSkills', operator_id, target_crossplatform_id,
                   target_entity_id, target_online_observed_at_utc, world_id,
                   client_request_key, correlation_id, status, created_at_utc,
                   started_at_utc, completed_at_utc, failure_code,
                   before_inventory_snapshot_id, after_inventory_snapshot_id,
                   before_skill_snapshot_id, after_skill_snapshot_id
            FROM player_reset_skills_operations
            UNION ALL
            SELECT operation_id, 'ClearInventory', operator_id, target_crossplatform_id,
                   target_entity_id, target_online_observed_at_utc, world_id,
                   client_request_key, correlation_id, status, created_at_utc,
                   started_at_utc, completed_at_utc, failure_code,
                   before_inventory_snapshot_id, after_inventory_snapshot_id,
                   before_skill_snapshot_id, after_skill_snapshot_id
            FROM player_clear_inventory_operations
            UNION ALL
            SELECT operation_id, 'ResetPlayerData', operator_id, target_crossplatform_id,
                   target_entity_id, target_online_observed_at_utc, world_id,
                   client_request_key, correlation_id, status, created_at_utc,
                   started_at_utc, completed_at_utc, failure_code,
                   before_inventory_snapshot_id, after_inventory_snapshot_id,
                   before_skill_snapshot_id, after_skill_snapshot_id
            FROM player_reset_data_operations";

        public static bool OperationIdExists(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string operationId) =>
            connection.ExecuteScalar<long>(
                @"SELECT EXISTS(SELECT 1 FROM player_grant_item_operations WHERE operation_id = @OperationId)
                    OR EXISTS(SELECT 1 FROM player_remove_item_operations WHERE operation_id = @OperationId)
                    OR EXISTS(SELECT 1 FROM player_reset_skills_operations WHERE operation_id = @OperationId)
                    OR EXISTS(SELECT 1 FROM player_clear_inventory_operations WHERE operation_id = @OperationId)
                    OR EXISTS(SELECT 1 FROM player_reset_data_operations WHERE operation_id = @OperationId);",
                new { OperationId = operationId },
                transaction) != 0;

        public static PlayerActionOperation ToSummary(string operationType, CommonOperationRow row) =>
            new PlayerActionOperation(
                row.OperationId,
                operationType,
                row.OperatorId,
                new PlayerTargetStamp(
                    row.TargetCrossplatformId,
                    row.TargetEntityId,
                    FromUnixMilliseconds(row.TargetOnlineObservedAtUtc),
                    row.WorldId),
                (PlayerActionStatus)Enum.Parse(typeof(PlayerActionStatus), row.Status, ignoreCase: false),
                FromUnixMilliseconds(row.CreatedAtUtc),
                row.StartedAtUtc.HasValue ? FromUnixMilliseconds(row.StartedAtUtc.Value) : (DateTimeOffset?)null,
                row.CompletedAtUtc.HasValue ? FromUnixMilliseconds(row.CompletedAtUtc.Value) : (DateTimeOffset?)null,
                row.FailureCode,
                row.BeforeInventorySnapshotId,
                row.AfterInventorySnapshotId,
                row.BeforeSkillSnapshotId,
                row.AfterSkillSnapshotId,
                row.CorrelationId);

        public static long ToUnixMilliseconds(DateTimeOffset value) =>
            value.ToUniversalTime().ToUnixTimeMilliseconds();

        private static DateTimeOffset FromUnixMilliseconds(long value) =>
            DateTimeOffset.FromUnixTimeMilliseconds(value);
    }

    internal static class PlayerActionStoreValidation
    {
        public static string RequireText(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value!;
        }

        public static string? OptionalText(string? value, string parameterName)
        {
            if (value == null) return null;
            return RequireText(value, parameterName);
        }

        public static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
            return value;
        }

        public static long? RequireOptionalId(long? value, string parameterName)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    internal class CommonOperationRow
    {
        public string OperationId { get; set; } = string.Empty;
        public string OperationType { get; set; } = string.Empty;
        public string OperatorId { get; set; } = string.Empty;
        public string TargetCrossplatformId { get; set; } = string.Empty;
        public int TargetEntityId { get; set; }
        public long TargetOnlineObservedAtUtc { get; set; }
        public string WorldId { get; set; } = string.Empty;
        public string ClientRequestKey { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public string Status { get; set; } = string.Empty;
        public long CreatedAtUtc { get; set; }
        public long? StartedAtUtc { get; set; }
        public long? CompletedAtUtc { get; set; }
        public string? FailureCode { get; set; }
        public long? BeforeInventorySnapshotId { get; set; }
        public long? AfterInventorySnapshotId { get; set; }
        public long? BeforeSkillSnapshotId { get; set; }
        public long? AfterSkillSnapshotId { get; set; }
    }

    internal sealed class GrantOperationRow : CommonOperationRow
    {
        public string CatalogVersion { get; set; } = string.Empty;
        public string InternalName { get; set; } = string.Empty;
        public string ItemKind { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int? Quality { get; set; }
        public int HiddenItemConfirmed { get; set; }
        public string? ResourceId { get; set; }
        public string? GameVersion { get; set; }
        public int? NumericId { get; set; }
    }

    internal sealed class RemoveOperationRow : CommonOperationRow
    {
        public string CatalogVersion { get; set; } = string.Empty;
        public string InternalName { get; set; } = string.Empty;
        public string ItemKind { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int? Quality { get; set; }
        public string RemovalScope { get; set; } = string.Empty;
        public string RemovalMode { get; set; } = string.Empty;
        public string? ResourceId { get; set; }
    }

    internal class DangerOperationRow : CommonOperationRow
    {
        public int DangerConfirmed { get; set; }
    }

    internal sealed class ClearOperationRow : DangerOperationRow
    {
        public string RemovalScope { get; set; } = string.Empty;
    }
}
