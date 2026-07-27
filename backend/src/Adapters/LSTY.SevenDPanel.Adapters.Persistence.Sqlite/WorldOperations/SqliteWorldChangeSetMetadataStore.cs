using System;
using System.Collections.Generic;
using Dapper;
using LSTY.SevenDPanel.Application.WorldOperations;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.WorldOperations
{
    public sealed class SqliteWorldChangeSetMetadataStore : IWorldChangeSetMetadataStore
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteWorldChangeSetMetadataStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public WorldChangeSetDescriptor Create(WorldChangeSetDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var changeSetId = Guid.NewGuid().ToString("D");
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            connection.Execute(
                @"INSERT INTO world_change_sets (
                      change_set_id, source_operation_id, world_id, world_version,
                      minimum_x, minimum_y, minimum_z, maximum_x, maximum_y, maximum_z,
                      before_hash, after_hash, storage_resource_id, created_at_utc, expires_at_utc)
                  VALUES (@ChangeSetId, @SourceOperationId, @WorldId, @WorldVersion,
                      @MinimumX, @MinimumY, @MinimumZ, @MaximumX, @MaximumY, @MaximumZ,
                      @BeforeHash, @AfterHash, @StorageResourceId, @CreatedAtUtc, @ExpiresAtUtc);",
                new
                {
                    ChangeSetId = changeSetId,
                    draft.SourceOperationId,
                    draft.WorldId,
                    draft.WorldVersion,
                    MinimumX = checked((int)draft.Region.Minimum.X),
                    MinimumY = checked((int)draft.Region.Minimum.Y),
                    MinimumZ = checked((int)draft.Region.Minimum.Z),
                    MaximumX = checked((int)draft.Region.Maximum.X),
                    MaximumY = checked((int)draft.Region.Maximum.Y),
                    MaximumZ = checked((int)draft.Region.Maximum.Z),
                    draft.BeforeHash,
                    draft.AfterHash,
                    draft.StorageResourceId,
                    CreatedAtUtc = draft.CreatedAtUtc.ToUnixTimeMilliseconds(),
                    ExpiresAtUtc = draft.ExpiresAtUtc.ToUnixTimeMilliseconds()
                },
                transaction);
            if (connection.Execute(
                    @"UPDATE world_operations SET change_set_id = @ChangeSetId
                      WHERE operation_id = @SourceOperationId AND change_set_id IS NULL;",
                    new { ChangeSetId = changeSetId, draft.SourceOperationId },
                    transaction) != 1)
            {
                throw new InvalidOperationException("world_change_set_source_operation_conflict");
            }
            transaction.Commit();
            return ToDescriptor(changeSetId, draft);
        }

        public WorldChangeSetDescriptor Read(string changeSetId)
        {
            changeSetId = SqliteWorldOperationStore.RequireText(changeSetId, nameof(changeSetId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<Row>(
                @"SELECT change_set_id AS ChangeSetId, source_operation_id AS SourceOperationId,
                      world_id AS WorldId, world_version AS WorldVersion,
                      minimum_x AS MinimumX, minimum_y AS MinimumY, minimum_z AS MinimumZ,
                      maximum_x AS MaximumX, maximum_y AS MaximumY, maximum_z AS MaximumZ,
                      before_hash AS BeforeHash, after_hash AS AfterHash,
                      storage_resource_id AS StorageResourceId,
                      created_at_utc AS CreatedAtUtc, expires_at_utc AS ExpiresAtUtc
                  FROM world_change_sets WHERE change_set_id = @ChangeSetId;",
                new { ChangeSetId = changeSetId });
            return row == null
                ? throw new KeyNotFoundException("The world change set does not exist.")
                : row.ToDescriptor();
        }

        public void MarkApplied(string changeSetId, string afterHash)
        {
            changeSetId = SqliteWorldOperationStore.RequireText(changeSetId, nameof(changeSetId));
            afterHash = WorldChangeSetValidation.RequireHash(afterHash, nameof(afterHash));
            using var connection = connectionFactory.Open();
            if (connection.Execute(
                    "UPDATE world_change_sets SET after_hash = @AfterHash WHERE change_set_id = @ChangeSetId;",
                    new { ChangeSetId = changeSetId, AfterHash = afterHash }) != 1)
            {
                throw new KeyNotFoundException("The world change set does not exist.");
            }
        }

        private static WorldChangeSetDescriptor ToDescriptor(string changeSetId, WorldChangeSetDraft draft) =>
            new WorldChangeSetDescriptor(
                changeSetId,
                draft.SourceOperationId,
                draft.WorldId,
                draft.WorldVersion,
                draft.Region,
                draft.BeforeHash,
                draft.AfterHash,
                draft.StorageResourceId,
                draft.CreatedAtUtc,
                draft.ExpiresAtUtc);

        private sealed class Row
        {
            public string ChangeSetId { get; set; } = string.Empty;
            public string SourceOperationId { get; set; } = string.Empty;
            public string WorldId { get; set; } = string.Empty;
            public string WorldVersion { get; set; } = string.Empty;
            public int MinimumX { get; set; }
            public int MinimumY { get; set; }
            public int MinimumZ { get; set; }
            public int MaximumX { get; set; }
            public int MaximumY { get; set; }
            public int MaximumZ { get; set; }
            public string BeforeHash { get; set; } = string.Empty;
            public string AfterHash { get; set; } = string.Empty;
            public string StorageResourceId { get; set; } = string.Empty;
            public long CreatedAtUtc { get; set; }
            public long ExpiresAtUtc { get; set; }

            public WorldChangeSetDescriptor ToDescriptor() =>
                new WorldChangeSetDescriptor(
                    ChangeSetId,
                    SourceOperationId,
                    WorldId,
                    WorldVersion,
                    new WorldRegion(
                        new WorldCoordinate(MinimumX, MinimumY, MinimumZ),
                        new WorldCoordinate(MaximumX, MaximumY, MaximumZ)),
                    BeforeHash,
                    AfterHash,
                    StorageResourceId,
                    DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUtc),
                    DateTimeOffset.FromUnixTimeMilliseconds(ExpiresAtUtc));
        }
    }
}
