using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Chat;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteChatMuteStore : IChatMuteStore, IChatMuteExpirationStore
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteChatMuteStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public ChatMutePage GetPage(int pageSize, ChatMuteCursor? cursor)
        {
            if (pageSize < 1 || pageSize > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
            using var connection = connectionFactory.Open();
            var rows = connection.Query<Row>(
                Select + (cursor == null ? string.Empty :
                    " WHERE (updated_utc < @UpdatedUtc OR (updated_utc = @UpdatedUtc AND crossplatform_id > @CrossplatformId))") +
                " ORDER BY updated_utc DESC, crossplatform_id ASC LIMIT @Take;",
                cursor == null ? new { Take = pageSize + 1 } : new
                {
                    UpdatedUtc = cursor.UpdatedAtUtc.ToUnixTimeMilliseconds(),
                    cursor.CrossplatformId,
                    Take = pageSize + 1
                }).ToArray();
            var page = rows.Take(pageSize).ToArray();
            var next = rows.Length > pageSize && page.Length > 0
                ? new ChatMuteCursor(FromUnixMilliseconds(page[page.Length - 1].UpdatedUtc), page[page.Length - 1].CrossplatformId)
                : null;
            return new ChatMutePage(page.Select(ToRecord), next);
        }

        public ChatMuteRecord? Find(string crossplatformId)
        {
            var key = RequireKey(crossplatformId, nameof(crossplatformId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<Row>(Select + " WHERE crossplatform_id = @CrossplatformId;", new { CrossplatformId = key });
            return row == null ? null : ToRecord(row);
        }

        public IReadOnlyList<ChatMuteRecord> Create(ChatMuteRecord record, ChatMuteOperation operation) =>
            Write(record, operation, "INSERT INTO chat_mute (crossplatform_id, display_name, reason, muted_until_utc, created_by, created_utc, updated_by, updated_utc) VALUES (@CrossplatformId, @DisplayName, @Reason, @MutedUntilUtc, @CreatedBy, @CreatedUtc, @UpdatedBy, @UpdatedUtc);");

        public IReadOnlyList<ChatMuteRecord> Update(ChatMuteRecord record, ChatMuteOperation operation) =>
            Write(record, operation, "UPDATE chat_mute SET display_name = @DisplayName, reason = @Reason, muted_until_utc = @MutedUntilUtc, created_by = @CreatedBy, created_utc = @CreatedUtc, updated_by = @UpdatedBy, updated_utc = @UpdatedUtc WHERE crossplatform_id = @CrossplatformId;");

        public IReadOnlyList<ChatMuteRecord> Release(string crossplatformId, ChatMuteOperation operation)
        {
            var key = RequireKey(crossplatformId, nameof(crossplatformId));
            ValidateOperation(operation, key);
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction();
            if (connection.Execute("DELETE FROM chat_mute WHERE crossplatform_id = @CrossplatformId;", new { CrossplatformId = key }, transaction) != 1)
                throw new ChatMuteNotFoundException();
            InsertOperation(connection, transaction, operation);
            var records = ReadAll(connection, transaction);
            transaction.Commit();
            return records;
        }

        public IReadOnlyList<ChatMuteRecord> Expire(DateTimeOffset nowUtc, int maximumDeletes)
        {
            RequireUtc(nowUtc, nameof(nowUtc));
            if (maximumDeletes < 1) throw new ArgumentOutOfRangeException(nameof(maximumDeletes));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction();
            var expired = connection.Query<Row>(Select +
                " WHERE muted_until_utc IS NOT NULL AND muted_until_utc <= @NowUtc ORDER BY muted_until_utc ASC, crossplatform_id ASC LIMIT @Take;",
                new { NowUtc = nowUtc.ToUnixTimeMilliseconds(), Take = Math.Min(maximumDeletes, 100) }, transaction).ToArray();
            foreach (var row in expired)
            {
                var record = ToRecord(row);
                connection.Execute("DELETE FROM chat_mute WHERE crossplatform_id = @CrossplatformId;", new { record.CrossplatformId }, transaction);
                InsertOperation(connection, transaction, new ChatMuteOperation(
                    Guid.NewGuid().ToString("D"), ChatMuteOperationKind.Expire, record.CrossplatformId,
                    null, nowUtc, "Succeeded", null, record.MutedUntilUtc, record.Reason));
            }
            var records = ReadAll(connection, transaction);
            transaction.Commit();
            return records;
        }

        private IReadOnlyList<ChatMuteRecord> Write(ChatMuteRecord record, ChatMuteOperation operation, string statement)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            ValidateOperation(operation, record.CrossplatformId);
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction();
            var changed = connection.Execute(statement, Parameters(record), transaction);
            if (changed != 1) throw new ChatMuteNotFoundException();
            InsertOperation(connection, transaction, operation);
            var records = ReadAll(connection, transaction);
            transaction.Commit();
            return records;
        }

        private static IReadOnlyList<ChatMuteRecord> ReadAll(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction) =>
            connection.Query<Row>(Select + " ORDER BY updated_utc DESC, crossplatform_id ASC;", transaction: transaction).Select(ToRecord).ToArray();

        private static void InsertOperation(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, ChatMuteOperation operation) =>
            connection.Execute(@"INSERT INTO chat_mute_operation (operation_id, operation_kind, target_crossplatform_id, actor_subject, occurred_utc, result, correlation_id, muted_until_utc, reason)
                VALUES (@OperationId, @OperationKind, @TargetCrossplatformId, @ActorSubject, @OccurredUtc, @Result, @CorrelationId, @MutedUntilUtc, @Reason);",
                new
                {
                    operation.OperationId,
                    OperationKind = operation.Kind.ToString(),
                    operation.TargetCrossplatformId,
                    operation.ActorSubject,
                    OccurredUtc = operation.OccurredAtUtc.ToUnixTimeMilliseconds(),
                    operation.Result,
                    operation.CorrelationId,
                    MutedUntilUtc = operation.MutedUntilUtc?.ToUnixTimeMilliseconds(),
                    operation.Reason
                }, transaction);

        private static void ValidateOperation(ChatMuteOperation operation, string targetCrossplatformId)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (!string.Equals(operation.TargetCrossplatformId, targetCrossplatformId, StringComparison.Ordinal))
                throw new ArgumentException("The operation target must match the mute target.", nameof(operation));
        }

        private static object Parameters(ChatMuteRecord record) => new
        {
            record.CrossplatformId, record.DisplayName, record.Reason,
            MutedUntilUtc = record.MutedUntilUtc?.ToUnixTimeMilliseconds(),
            record.CreatedBy,
            CreatedUtc = record.CreatedAtUtc.ToUnixTimeMilliseconds(),
            record.UpdatedBy,
            UpdatedUtc = record.UpdatedAtUtc.ToUnixTimeMilliseconds()
        };

        private const string Select = @"SELECT crossplatform_id AS CrossplatformId, display_name AS DisplayName, reason AS Reason, muted_until_utc AS MutedUntilUtc, created_by AS CreatedBy, created_utc AS CreatedUtc, updated_by AS UpdatedBy, updated_utc AS UpdatedUtc FROM chat_mute";
        private static string RequireKey(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }
        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero) throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }
        private static DateTimeOffset FromUnixMilliseconds(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
        private static ChatMuteRecord ToRecord(Row row) => new ChatMuteRecord(row.CrossplatformId, row.DisplayName, row.Reason, row.MutedUntilUtc.HasValue ? FromUnixMilliseconds(row.MutedUntilUtc.Value) : null, row.CreatedBy, FromUnixMilliseconds(row.CreatedUtc), row.UpdatedBy, FromUnixMilliseconds(row.UpdatedUtc));

        private sealed class Row
        {
            public string CrossplatformId { get; set; } = string.Empty;
            public string? DisplayName { get; set; }
            public string Reason { get; set; } = string.Empty;
            public long? MutedUntilUtc { get; set; }
            public string CreatedBy { get; set; } = string.Empty;
            public long CreatedUtc { get; set; }
            public string UpdatedBy { get; set; } = string.Empty;
            public long UpdatedUtc { get; set; }
        }
    }
}
