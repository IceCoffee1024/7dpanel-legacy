using System;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;

namespace LSTY.SevenDPanel.Application.Backups
{
    public sealed record CompletedBackup(
        Guid Id,
        BackupKind Kind,
        string BackupRootId,
        string RelativeResourceId,
        long SizeBytes,
        string Sha256,
        string? WorldId,
        string? GameVersion,
        string ValidationStatus,
        DateTimeOffset CreatedAtUtc,
        Guid SourceJobId,
        int ManifestVersion);

    public sealed record BackupArtifact(
        Guid Id,
        BackupKind Kind,
        string BackupRootId,
        string RelativeResourceId,
        long SizeBytes,
        string Sha256,
        string? WorldId,
        string? GameVersion,
        string ValidationStatus,
        DateTimeOffset CreatedAtUtc,
        Guid SourceJobId,
        int ManifestVersion);

    public sealed record BackupCursor(DateTimeOffset CreatedAtUtc, Guid Id);

    public sealed record BackupQuery
    {
        public BackupQuery(int pageSize, BackupKind? kind, BackupCursor? cursor)
            : this(pageSize, kind, null, null, cursor)
        {
        }

        public BackupQuery(
            int pageSize,
            BackupKind? kind,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            BackupCursor? cursor)
        {
            PageSize = pageSize;
            Kind = kind;
            FromUtc = fromUtc;
            ToUtc = toUtc;
            Cursor = cursor;
        }

        public int PageSize { get; }
        public BackupKind? Kind { get; }
        public DateTimeOffset? FromUtc { get; }
        public DateTimeOffset? ToUtc { get; }
        public BackupCursor? Cursor { get; }
    }

    public interface IBackupCatalog
    {
        BackupArtifact Add(CompletedBackup backup);
        BackupArtifact Get(Guid backupId);
        PagedResult<BackupArtifact, BackupCursor> List(BackupQuery query);
        bool Delete(Guid backupId);
    }
}
