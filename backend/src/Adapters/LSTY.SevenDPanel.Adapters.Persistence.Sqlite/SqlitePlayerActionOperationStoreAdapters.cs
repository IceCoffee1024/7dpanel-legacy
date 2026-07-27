using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteGrantItemOperationStoreAdapter :
        IGrantItemOperationStore,
        IPlayerActionRecoveryStore
    {
        private readonly SqliteGrantItemOperationStore store;
        private readonly SqlitePlayerActionRecoveryStoreCore recovery;

        public SqliteGrantItemOperationStoreAdapter(
            SqliteGrantItemOperationStore store,
            SqliteConnectionFactory connectionFactory)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            recovery = new SqlitePlayerActionRecoveryStoreCore(
                connectionFactory,
                "player_grant_item_operations",
                PlayerActionOperationTypes.GrantItem);
        }

        public string OperationType => PlayerActionOperationTypes.GrantItem;

        public PlayerActionOperation CreatePending(GrantItemPendingIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            try
            {
                return store.CreatePending(new GrantItemOperationIntent(
                    intent.OperationId,
                    intent.OperatorId,
                    intent.Target,
                    intent.ClientRequestKey,
                    intent.CorrelationId,
                    intent.CreatedAtUtc,
                    null,
                    null,
                    intent.CatalogVersion,
                    intent.InternalName,
                    intent.ItemKind.ToString(),
                    intent.Quantity,
                    intent.Quality,
                    intent.HiddenItemConfirmed,
                    intent.ResourceId,
                    intent.GameVersion,
                    intent.NumericId));
            }
            catch (PlayerActionIdempotencyConflictException exception)
            {
                throw new GrantItemIdempotencyConflictException(
                    exception.OperatorId,
                    exception.ClientRequestKey,
                    exception.ExistingOperationId);
            }
        }

        public bool TryStart(string operationId, DateTimeOffset startedAtUtc) =>
            store.TryStart(operationId, startedAtUtc);

        public bool TryComplete(GrantItemOperationCompletion completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            return store.TryComplete(
                SqlitePlayerActionAdapterMapping.Completion(
                    completion.OperationId,
                    completion.Status,
                    completion.CompletedAtUtc,
                    completion.FailureCode,
                    completion.BeforeInventorySnapshotId,
                    completion.AfterInventorySnapshotId,
                    null,
                    null),
                completion.ActualQuantity);
        }

        public IReadOnlyList<PlayerActionRecoveryRecord> ReadRecoverable() =>
            recovery.ReadRecoverable();

        public bool TryComplete(PlayerActionRecoveryCompletion completion) =>
            recovery.TryComplete(completion);
    }

    public sealed class SqliteRemoveItemOperationStoreAdapter :
        IRemoveItemOperationStore,
        IPlayerActionRecoveryStore
    {
        private readonly SqliteRemoveItemOperationStore store;
        private readonly SqlitePlayerActionRecoveryStoreCore recovery;

        public SqliteRemoveItemOperationStoreAdapter(
            SqliteRemoveItemOperationStore store,
            SqliteConnectionFactory connectionFactory)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            recovery = new SqlitePlayerActionRecoveryStoreCore(
                connectionFactory,
                "player_remove_item_operations",
                PlayerActionOperationTypes.RemoveItem);
        }

        public string OperationType => PlayerActionOperationTypes.RemoveItem;

        public PlayerActionOperation CreatePending(RemoveItemPendingIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            try
            {
                return store.CreatePending(new RemoveItemOperationIntent(
                    intent.OperationId,
                    intent.OperatorId,
                    intent.Target,
                    intent.ClientRequestKey,
                    intent.CorrelationId,
                    intent.CreatedAtUtc,
                    null,
                    null,
                    intent.CatalogVersion,
                    intent.InternalName,
                    intent.ItemKind.ToString(),
                    intent.Quantity,
                    intent.Quality,
                    intent.RemovalScope,
                    intent.RemovalMode,
                    intent.ResourceId));
            }
            catch (PlayerActionIdempotencyConflictException exception)
            {
                throw new RemoveItemIdempotencyConflictException(
                    exception.OperatorId,
                    exception.ClientRequestKey,
                    exception.ExistingOperationId);
            }
        }

        public bool TryStart(string operationId, DateTimeOffset startedAtUtc) =>
            store.TryStart(operationId, startedAtUtc);

        public bool TryComplete(RemoveItemOperationCompletion completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            return store.TryComplete(
                SqlitePlayerActionAdapterMapping.Completion(
                    completion.OperationId,
                    completion.Status,
                    completion.CompletedAtUtc,
                    completion.FailureCode,
                    completion.BeforeInventorySnapshotId,
                    completion.AfterInventorySnapshotId,
                    null,
                    null),
                completion.ActualQuantity);
        }

        public IReadOnlyList<PlayerActionRecoveryRecord> ReadRecoverable() =>
            recovery.ReadRecoverable();

        public bool TryComplete(PlayerActionRecoveryCompletion completion) =>
            recovery.TryComplete(completion);
    }

    public sealed class SqliteResetSkillsOperationStoreAdapter :
        IResetSkillsOperationStore,
        IPlayerActionRecoveryStore
    {
        private readonly SqliteResetSkillsOperationStore store;
        private readonly SqlitePlayerActionRecoveryStoreCore recovery;

        public SqliteResetSkillsOperationStoreAdapter(
            SqliteResetSkillsOperationStore store,
            SqliteConnectionFactory connectionFactory)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            recovery = new SqlitePlayerActionRecoveryStoreCore(
                connectionFactory,
                "player_reset_skills_operations",
                PlayerActionOperationTypes.ResetSkills);
        }

        public string OperationType => PlayerActionOperationTypes.ResetSkills;

        public PlayerActionOperation CreatePending(ResetSkillsPendingIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            try
            {
                return store.CreatePending(new ResetSkillsOperationIntent(
                    intent.OperationId,
                    intent.OperatorId,
                    intent.Target,
                    intent.ClientRequestKey,
                    intent.CorrelationId,
                    intent.CreatedAtUtc,
                    null,
                    null,
                    intent.DangerConfirmed));
            }
            catch (PlayerActionIdempotencyConflictException exception)
            {
                throw new ResetSkillsIdempotencyConflictException(
                    exception.OperatorId,
                    exception.ClientRequestKey,
                    exception.ExistingOperationId);
            }
        }

        public bool TryStart(string operationId, DateTimeOffset startedAtUtc) =>
            store.TryStart(operationId, startedAtUtc);

        public bool TryComplete(ResetSkillsOperationCompletion completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            return store.TryComplete(SqlitePlayerActionAdapterMapping.Completion(
                completion.OperationId,
                SqlitePlayerActionAdapterMapping.Status(completion.Status),
                completion.CompletedAtUtc,
                completion.FailureCode,
                null,
                null,
                completion.BeforeSkillSnapshotId,
                completion.AfterSkillSnapshotId));
        }

        public IReadOnlyList<PlayerActionRecoveryRecord> ReadRecoverable() =>
            recovery.ReadRecoverable();

        public bool TryComplete(PlayerActionRecoveryCompletion completion) =>
            recovery.TryComplete(completion);
    }

    public sealed class SqliteClearInventoryOperationStoreAdapter :
        IClearInventoryOperationStore,
        IPlayerActionRecoveryStore
    {
        private readonly SqliteClearInventoryOperationStore store;
        private readonly SqlitePlayerActionRecoveryStoreCore recovery;

        public SqliteClearInventoryOperationStoreAdapter(
            SqliteClearInventoryOperationStore store,
            SqliteConnectionFactory connectionFactory)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            recovery = new SqlitePlayerActionRecoveryStoreCore(
                connectionFactory,
                "player_clear_inventory_operations",
                PlayerActionOperationTypes.ClearInventory);
        }

        public string OperationType => PlayerActionOperationTypes.ClearInventory;

        public PlayerActionOperation CreatePending(ClearInventoryPendingIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            try
            {
                return store.CreatePending(new ClearInventoryOperationIntent(
                    intent.OperationId,
                    intent.OperatorId,
                    intent.Target,
                    intent.ClientRequestKey,
                    intent.CorrelationId,
                    intent.CreatedAtUtc,
                    null,
                    null,
                    intent.RemovalScope,
                    intent.DangerConfirmed));
            }
            catch (PlayerActionIdempotencyConflictException exception)
            {
                throw new ClearInventoryIdempotencyConflictException(
                    exception.OperatorId,
                    exception.ClientRequestKey,
                    exception.ExistingOperationId);
            }
        }

        public bool TryStart(string operationId, DateTimeOffset startedAtUtc) =>
            store.TryStart(operationId, startedAtUtc);

        public bool TryComplete(ClearInventoryOperationCompletion completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            return store.TryComplete(SqlitePlayerActionAdapterMapping.Completion(
                completion.OperationId,
                SqlitePlayerActionAdapterMapping.Status(completion.Status),
                completion.CompletedAtUtc,
                completion.FailureCode,
                completion.BeforeInventorySnapshotId,
                completion.AfterInventorySnapshotId,
                null,
                null));
        }

        public IReadOnlyList<PlayerActionRecoveryRecord> ReadRecoverable() =>
            recovery.ReadRecoverable();

        public bool TryComplete(PlayerActionRecoveryCompletion completion) =>
            recovery.TryComplete(completion);
    }

    public sealed class SqliteResetPlayerDataOperationStoreAdapter :
        IResetPlayerDataOperationStore,
        IPlayerActionRecoveryStore
    {
        private readonly SqliteResetPlayerDataOperationStore store;
        private readonly SqlitePlayerActionRecoveryStoreCore recovery;

        public SqliteResetPlayerDataOperationStoreAdapter(
            SqliteResetPlayerDataOperationStore store,
            SqliteConnectionFactory connectionFactory)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            recovery = new SqlitePlayerActionRecoveryStoreCore(
                connectionFactory,
                "player_reset_data_operations",
                PlayerActionOperationTypes.ResetPlayerData);
        }

        public string OperationType => PlayerActionOperationTypes.ResetPlayerData;

        public PlayerActionOperation CreatePending(ResetPlayerDataPendingIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            try
            {
                return store.CreatePending(new ResetPlayerDataOperationIntent(
                    intent.OperationId,
                    intent.OperatorId,
                    intent.Target,
                    intent.ClientRequestKey,
                    intent.CorrelationId,
                    intent.CreatedAtUtc,
                    intent.BeforeInventorySnapshotId,
                    intent.BeforeSkillSnapshotId,
                    intent.DangerConfirmed));
            }
            catch (PlayerActionIdempotencyConflictException exception)
            {
                throw new ResetPlayerDataIdempotencyConflictException(
                    exception.OperatorId,
                    exception.ClientRequestKey,
                    exception.ExistingOperationId);
            }
        }

        public bool TryStart(string operationId, DateTimeOffset startedAtUtc) =>
            store.TryStart(operationId, startedAtUtc);

        public bool TryComplete(ResetPlayerDataOperationCompletion completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            return store.TryComplete(SqlitePlayerActionAdapterMapping.Completion(
                completion.OperationId,
                SqlitePlayerActionAdapterMapping.Status(completion.Status),
                completion.CompletedAtUtc,
                completion.FailureCode,
                completion.BeforeInventorySnapshotId,
                null,
                completion.BeforeSkillSnapshotId,
                null));
        }

        public IReadOnlyList<PlayerActionRecoveryRecord> ReadRecoverable() =>
            recovery.ReadRecoverable();

        public bool TryComplete(PlayerActionRecoveryCompletion completion) =>
            recovery.TryComplete(completion);
    }

    internal sealed class SqlitePlayerActionRecoveryStoreCore
    {
        private readonly SqliteConnectionFactory connectionFactory;
        private readonly string table;
        private readonly string operationType;
        private readonly PlayerActionStoreCore store;

        public SqlitePlayerActionRecoveryStoreCore(
            SqliteConnectionFactory connectionFactory,
            string table,
            string operationType)
        {
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
            this.table = table;
            this.operationType = operationType;
            store = new PlayerActionStoreCore(connectionFactory, table, operationType);
        }

        public IReadOnlyList<PlayerActionRecoveryRecord> ReadRecoverable()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<CommonOperationRow>(
                    "SELECT " + PlayerActionSql.CommonSelect + " FROM " + table +
                    " WHERE status = 'Pending' ORDER BY created_at_utc, operation_id;")
                .Select(row => new PlayerActionRecoveryRecord(
                    PlayerActionSql.ToSummary(operationType, row),
                    null))
                .ToArray();
        }

        public bool TryComplete(PlayerActionRecoveryCompletion completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            return store.TryComplete(
                SqlitePlayerActionAdapterMapping.Completion(
                    completion.OperationId,
                    completion.Status,
                    completion.CompletedAtUtc,
                    completion.FailureCode,
                    completion.BeforeInventorySnapshotId,
                    completion.AfterInventorySnapshotId,
                    completion.BeforeSkillSnapshotId,
                    completion.AfterSkillSnapshotId),
                string.Empty,
                new DynamicParameters());
        }
    }

    internal static class SqlitePlayerActionAdapterMapping
    {
        public static PlayerActionOperationCompletion Completion(
            string operationId,
            PlayerActionStatus status,
            DateTimeOffset completedAtUtc,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId,
            long? beforeSkillSnapshotId,
            long? afterSkillSnapshotId) =>
            new PlayerActionOperationCompletion(
                operationId,
                status,
                completedAtUtc,
                failureCode,
                beforeInventorySnapshotId,
                afterInventorySnapshotId,
                beforeSkillSnapshotId,
                afterSkillSnapshotId);

        public static PlayerActionStatus Status<TStatus>(TStatus status)
            where TStatus : struct =>
            (PlayerActionStatus)Enum.Parse(
                typeof(PlayerActionStatus),
                status.ToString(),
                ignoreCase: false);
    }
}
