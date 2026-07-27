using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Backups
{
    public sealed class SqliteBackupCatalog : IBackupCatalog
    {
        private const string SelectColumns = @"SELECT
            id AS Id, kind AS Kind, backup_root_id AS BackupRootId,
            relative_resource_id AS RelativeResourceId, size_bytes AS SizeBytes,
            sha256 AS Sha256, world_id AS WorldId, game_version AS GameVersion,
            validation_status AS ValidationStatus, created_at_utc AS CreatedAtUtc,
            source_job_id AS SourceJobId, manifest_version AS ManifestVersion
            FROM backup_artifacts";

        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteBackupCatalog(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public BackupArtifact Add(CompletedBackup backup)
        {
            if (backup == null) throw new ArgumentNullException(nameof(backup));
            Validate(backup);
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO backup_artifacts (
                      id, kind, backup_root_id, relative_resource_id, size_bytes, sha256,
                      world_id, game_version, validation_status, created_at_utc,
                      source_job_id, manifest_version)
                  VALUES (@Id, @Kind, @BackupRootId, @RelativeResourceId, @SizeBytes, @Sha256,
                      @WorldId, @GameVersion, @ValidationStatus, @CreatedAtUtc,
                      @SourceJobId, @ManifestVersion);",
                new
                {
                    Id = backup.Id.ToString("D"),
                    Kind = backup.Kind.ToString(),
                    BackupRootId = backup.BackupRootId.Trim(),
                    RelativeResourceId = backup.RelativeResourceId.Trim(),
                    backup.SizeBytes,
                    Sha256 = backup.Sha256.Trim().ToLowerInvariant(),
                    WorldId = Normalize(backup.WorldId),
                    GameVersion = Normalize(backup.GameVersion),
                    ValidationStatus = backup.ValidationStatus.Trim(),
                    CreatedAtUtc = backup.CreatedAtUtc.ToUnixTimeMilliseconds(),
                    SourceJobId = backup.SourceJobId.ToString("D"),
                    backup.ManifestVersion
                });
            return Get(backup.Id);
        }

        public BackupArtifact Get(Guid backupId)
        {
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<BackupRow>(
                SelectColumns + " WHERE id = @Id;", new { Id = backupId.ToString("D") });
            return row == null
                ? throw new KeyNotFoundException("The backup artifact does not exist.")
                : ToArtifact(row);
        }

        public PagedResult<BackupArtifact, BackupCursor> List(BackupQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (query.PageSize < 1 || query.PageSize > 100)
                throw new ArgumentOutOfRangeException(nameof(query));
            if (query.FromUtc.HasValue) RequireUtc(query.FromUtc.Value, nameof(query));
            if (query.ToUtc.HasValue) RequireUtc(query.ToUtc.Value, nameof(query));
            if (query.Cursor != null) RequireUtc(query.Cursor.CreatedAtUtc, nameof(query));
            if (query.FromUtc.HasValue && query.ToUtc.HasValue && query.FromUtc > query.ToUtc)
                throw new ArgumentException("The UTC range is invalid.", nameof(query));
            var where = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("Take", query.PageSize + 1);
            if (query.Kind.HasValue)
            {
                where.Add("kind = @Kind");
                parameters.Add("Kind", query.Kind.Value.ToString());
            }
            if (query.FromUtc.HasValue)
            {
                where.Add("created_at_utc >= @FromUtc");
                parameters.Add("FromUtc", query.FromUtc.Value.ToUnixTimeMilliseconds());
            }
            if (query.ToUtc.HasValue)
            {
                where.Add("created_at_utc <= @ToUtc");
                parameters.Add("ToUtc", query.ToUtc.Value.ToUnixTimeMilliseconds());
            }
            if (query.Cursor != null)
            {
                where.Add("(created_at_utc < @CursorUtc OR (created_at_utc = @CursorUtc AND id < @CursorId))");
                parameters.Add("CursorUtc", query.Cursor.CreatedAtUtc.ToUnixTimeMilliseconds());
                parameters.Add("CursorId", query.Cursor.Id.ToString("D"));
            }

            using var connection = connectionFactory.Open();
            var rows = connection.Query<BackupRow>(
                SelectColumns +
                (where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where)) +
                " ORDER BY created_at_utc DESC, id DESC LIMIT @Take;", parameters).ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            BackupCursor? nextCursor = rows.Length > query.PageSize && pageRows.Length > 0
                ? new BackupCursor(
                    DateTimeOffset.FromUnixTimeMilliseconds(pageRows[pageRows.Length - 1].CreatedAtUtc),
                    Guid.Parse(pageRows[pageRows.Length - 1].Id))
                : null;
            return new PagedResult<BackupArtifact, BackupCursor>(pageRows.Select(ToArtifact).ToArray(), nextCursor);
        }

        public bool Delete(Guid backupId)
        {
            using var connection = connectionFactory.Open();
            return connection.Execute(
                "DELETE FROM backup_artifacts WHERE id = @Id;",
                new { Id = backupId.ToString("D") }) == 1;
        }

        private static void Validate(CompletedBackup backup)
        {
            if (backup.Id == Guid.Empty) throw new ArgumentException("A backup id is required.", nameof(backup));
            if (!Enum.IsDefined(typeof(BackupKind), backup.Kind)) throw new ArgumentOutOfRangeException(nameof(backup));
            RequireOpaqueId(backup.BackupRootId, nameof(backup.BackupRootId));
            RequireOpaqueId(backup.RelativeResourceId, nameof(backup.RelativeResourceId));
            if (backup.SizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(backup));
            if (backup.Sha256 == null || backup.Sha256.Length != 64 || !backup.Sha256.All(IsHex))
                throw new ArgumentException("A SHA-256 hexadecimal digest is required.", nameof(backup));
            if (string.IsNullOrWhiteSpace(backup.ValidationStatus))
                throw new ArgumentException("A validation status is required.", nameof(backup));
            RequireUtc(backup.CreatedAtUtc, nameof(backup));
            if (backup.SourceJobId == Guid.Empty) throw new ArgumentException("A source job is required.", nameof(backup));
            if (backup.ManifestVersion < 1) throw new ArgumentOutOfRangeException(nameof(backup));
        }

        private static void RequireOpaqueId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains("/") || value.Contains("\\") || value.Contains(".."))
                throw new ArgumentException("An opaque server-generated identifier is required.", parameterName);
        }

        private static bool IsHex(char value) =>
            (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F');

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        private static BackupArtifact ToArtifact(BackupRow row)
        {
            RequireOpaqueId(row.BackupRootId, nameof(row.BackupRootId));
            RequireOpaqueId(row.RelativeResourceId, nameof(row.RelativeResourceId));
            return new BackupArtifact(
                Guid.Parse(row.Id),
                (BackupKind)Enum.Parse(typeof(BackupKind), row.Kind),
                row.BackupRootId,
                row.RelativeResourceId,
                row.SizeBytes,
                row.Sha256,
                row.WorldId,
                row.GameVersion,
                row.ValidationStatus,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                Guid.Parse(row.SourceJobId),
                row.ManifestVersion);
        }

        private sealed class BackupRow
        {
            public string Id { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public string BackupRootId { get; set; } = string.Empty;
            public string RelativeResourceId { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
            public string Sha256 { get; set; } = string.Empty;
            public string? WorldId { get; set; }
            public string? GameVersion { get; set; }
            public string ValidationStatus { get; set; } = string.Empty;
            public long CreatedAtUtc { get; set; }
            public string SourceJobId { get; set; } = string.Empty;
            public int ManifestVersion { get; set; }
        }
    }
}
