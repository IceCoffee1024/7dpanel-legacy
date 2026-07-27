using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Domain.Backups;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Backups
{
    public sealed class SqliteBackupPolicyStore : IBackupPolicyStore
    {
        private const string SelectColumns = @"SELECT
            kind AS Kind, enabled AS Enabled, cron_expression AS CronExpression,
            time_zone_id AS TimeZoneId, backup_root_id AS BackupRootId,
            retention_count AS RetentionCount, retention_days AS RetentionDays,
            compression_enabled AS CompressionEnabled, row_version AS RowVersion
            FROM backup_policies";

        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteBackupPolicyStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));

        public IReadOnlyList<BackupPolicyDefinition> List()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<BackupPolicyRow>(
                    SelectColumns + @" ORDER BY CASE kind
                        WHEN 'World' THEN 0
                        WHEN 'PanelDatabase' THEN 1
                        WHEN 'ServerConfiguration' THEN 2
                        ELSE 3 END;")
                .Select(ToDefinition)
                .ToArray();
        }

        public BackupPolicyDefinition? Get(BackupKind kind)
        {
            if (!Enum.IsDefined(typeof(BackupKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<BackupPolicyRow>(
                SelectColumns + " WHERE kind = @Kind;",
                new { Kind = kind.ToString() });
            return row == null ? null : ToDefinition(row);
        }

        public BackupPolicyDefinition Upsert(BackupPolicyDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction();
            var existingVersion = connection.QuerySingleOrDefault<long?>(
                "SELECT row_version FROM backup_policies WHERE kind = @Kind;",
                new { Kind = definition.Kind.ToString() },
                transaction);
            if (!existingVersion.HasValue)
            {
                if (definition.RowVersion != 0) throw Conflict();
                connection.Execute(
                    @"INSERT INTO backup_policies (
                          kind, enabled, cron_expression, time_zone_id, backup_root_id,
                          retention_count, retention_days, compression_enabled, row_version)
                      VALUES (
                          @Kind, @Enabled, @CronExpression, @TimeZoneId, @BackupRootId,
                          @RetentionCount, @RetentionDays, @CompressionEnabled, 0);",
                    Parameters(definition),
                    transaction);
            }
            else
            {
                var changed = connection.Execute(
                    @"UPDATE backup_policies
                      SET enabled = @Enabled,
                          cron_expression = @CronExpression,
                          time_zone_id = @TimeZoneId,
                          backup_root_id = @BackupRootId,
                          retention_count = @RetentionCount,
                          retention_days = @RetentionDays,
                          compression_enabled = @CompressionEnabled,
                          row_version = row_version + 1
                      WHERE kind = @Kind AND row_version = @ExpectedRowVersion;",
                    Parameters(definition),
                    transaction);
                if (changed != 1) throw Conflict();
            }

            var stored = connection.QuerySingle<BackupPolicyRow>(
                SelectColumns + " WHERE kind = @Kind;",
                new { Kind = definition.Kind.ToString() },
                transaction);
            transaction.Commit();
            return ToDefinition(stored);
        }

        private static object Parameters(BackupPolicyDefinition definition) => new
        {
            Kind = definition.Kind.ToString(),
            Enabled = definition.Enabled ? 1 : 0,
            definition.CronExpression,
            definition.TimeZoneId,
            definition.BackupRootId,
            definition.RetentionCount,
            definition.RetentionDays,
            CompressionEnabled = definition.CompressionEnabled ? 1 : 0,
            ExpectedRowVersion = definition.RowVersion
        };

        private static BackupPolicyDefinition ToDefinition(BackupPolicyRow row) =>
            new BackupPolicyDefinition(
                (BackupKind)Enum.Parse(typeof(BackupKind), row.Kind),
                row.Enabled != 0,
                row.CronExpression,
                row.TimeZoneId,
                row.BackupRootId,
                row.RetentionCount,
                row.RetentionDays,
                row.CompressionEnabled != 0,
                row.RowVersion);

        private static InvalidOperationException Conflict() =>
            new InvalidOperationException("backup_policy_row_version_conflict");

        private sealed class BackupPolicyRow
        {
            public string Kind { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public string CronExpression { get; set; } = string.Empty;
            public string TimeZoneId { get; set; } = string.Empty;
            public string BackupRootId { get; set; } = string.Empty;
            public int RetentionCount { get; set; }
            public int RetentionDays { get; set; }
            public int CompressionEnabled { get; set; }
            public long RowVersion { get; set; }
        }
    }
}
