using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Application.Backups
{
    public sealed class BackupCatalogService
    {
        public const string IntegrityFailedError = "backup_integrity_failed";
        public const string BackupInUseError = "backup_in_use";
        public const string DeleteFailedError = "backup_delete_failed";
        public const string UsageCheckFailedError = "backup_usage_check_failed";

        private static readonly JobStatus[] ProtectedRestoreStatuses =
        {
            JobStatus.Queued,
            JobStatus.Running,
            JobStatus.PendingRestart,
            JobStatus.Interrupted,
            JobStatus.ResultUnknown
        };

        private readonly IBackupCatalog catalog;
        private readonly IBackupArchiveStorage archives;
        private readonly IJobStore jobs;
        private readonly IJobPayloadReader payloads;

        public BackupCatalogService(
            IBackupCatalog catalog,
            IBackupArchiveStorage archives,
            IJobStore jobs,
            IJobPayloadReader payloads)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.archives = archives ?? throw new ArgumentNullException(nameof(archives));
            this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            this.payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
        }

        public BackupArtifact Get(Guid backupId) => catalog.Get(backupId);

        public PagedResult<BackupArtifact, BackupCursor> List(BackupQuery query) =>
            catalog.List(query ?? throw new ArgumentNullException(nameof(query)));

        public PreparedBackupDownload PrepareDownload(Guid backupId)
        {
            var artifact = catalog.Get(backupId);
            Stream? content = null;
            try
            {
                content = archives.OpenRead(artifact);
                if (!content.CanRead || !content.CanSeek || content.Length != artifact.SizeBytes)
                    throw new InvalidDataException(IntegrityFailedError);

                content.Position = 0;
                var actualSha256 = ComputeSha256(content);
                if (!string.Equals(actualSha256, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(IntegrityFailedError);

                content.Position = 0;
                var download = new PreparedBackupDownload(
                    BuildAttachmentFileName(artifact),
                    artifact.SizeBytes,
                    content);
                content = null;
                return download;
            }
            catch (Exception exception) when (!(exception is BackupCatalogException))
            {
                content?.Dispose();
                throw new BackupCatalogException(IntegrityFailedError, exception);
            }
        }

        public void Delete(Guid backupId)
        {
            var artifact = catalog.Get(backupId);
            if (IsInUse(backupId))
                throw new BackupCatalogException(BackupInUseError);

            try
            {
                archives.Delete(artifact);
            }
            catch (Exception exception) when (!(exception is BackupCatalogException))
            {
                throw new BackupCatalogException(DeleteFailedError, exception);
            }

            try
            {
                if (!catalog.Delete(backupId))
                    throw new InvalidOperationException(DeleteFailedError);
            }
            catch (Exception exception) when (!(exception is BackupCatalogException))
            {
                throw new BackupCatalogException(DeleteFailedError, exception);
            }
        }

        public bool IsInUse(Guid backupId)
        {
            try
            {
                foreach (var status in ProtectedRestoreStatuses)
                {
                    JobCursor? cursor = null;
                    do
                    {
                        var page = jobs.List(new JobQuery(
                            100,
                            JobKind.Restore,
                            status,
                            null,
                            null,
                            cursor));
                        if (page.Items.Any(job => payloads.GetRestore(job.Id).BackupId == backupId))
                            return true;
                        cursor = page.NextCursor;
                    }
                    while (cursor != null);
                }

                return false;
            }
            catch (BackupCatalogException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new BackupCatalogException(UsageCheckFailedError, exception);
            }
        }

        private static string BuildAttachmentFileName(BackupArtifact artifact)
        {
            var kind = artifact.Kind switch
            {
                BackupKind.World => "world",
                BackupKind.PanelDatabase => "panel-database",
                BackupKind.ServerConfiguration => "server-configuration",
                _ => throw new InvalidOperationException(IntegrityFailedError)
            };
            return kind + "-" +
                artifact.CreatedAtUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
                artifact.Id.ToString("N") + ".zip";
        }

        private static string ComputeSha256(Stream content)
        {
            using var sha256 = SHA256.Create();
            return string.Concat(sha256.ComputeHash(content)
                .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }

    public sealed class BackupCatalogException : Exception
    {
        public BackupCatalogException(string errorCode)
            : base(errorCode)
        {
            ErrorCode = errorCode;
        }

        public BackupCatalogException(string errorCode, Exception innerException)
            : base(errorCode, innerException)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }
}
