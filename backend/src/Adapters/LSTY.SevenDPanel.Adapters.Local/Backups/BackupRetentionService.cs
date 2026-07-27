using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Domain.Backups;

namespace LSTY.SevenDPanel.Adapters.Local.Backups
{
    public sealed record BackupRetentionPolicy(
        BackupKind Kind,
        int RetentionCount,
        int RetentionDays,
        IReadOnlyCollection<Guid> RetainedBackupIds);

    public sealed record BackupRetentionError(Guid? BackupId, string ErrorCode);

    public sealed record BackupRetentionResult(
        IReadOnlyCollection<Guid> DeletedBackupIds,
        IReadOnlyCollection<BackupRetentionError> Errors);

    public sealed class BackupRetentionService
    {
        public const string RetentionFailedError = "backup_retention_failed";

        private readonly BackupCatalogService backups;

        public BackupRetentionService(BackupCatalogService backups) =>
            this.backups = backups ?? throw new ArgumentNullException(nameof(backups));

        public BackupRetentionResult Apply(
            BackupRetentionPolicy policy,
            DateTimeOffset nowUtc)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (!Enum.IsDefined(typeof(BackupKind), policy.Kind))
                throw new ArgumentOutOfRangeException(nameof(policy));
            if (policy.RetentionCount < 0 || policy.RetentionDays < 0)
                throw new ArgumentOutOfRangeException(nameof(policy));
            if (policy.RetainedBackupIds == null)
                throw new ArgumentException("Retained backup ids are required.", nameof(policy));
            if (nowUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", nameof(nowUtc));

            BackupArtifact[] artifacts;
            try
            {
                artifacts = ListAll(policy.Kind)
                    .OrderByDescending(artifact => artifact.CreatedAtUtc)
                    .ThenByDescending(
                        artifact => artifact.Id.ToString("D"),
                        StringComparer.Ordinal)
                    .ToArray();
            }
            catch (BackupCatalogException exception)
            {
                return Failed(exception.ErrorCode);
            }
            catch
            {
                return Failed(RetentionFailedError);
            }

            var retained = new HashSet<Guid>(policy.RetainedBackupIds);
            var cutoff = policy.RetentionDays == 0
                ? (DateTimeOffset?)null
                : nowUtc.AddDays(-policy.RetentionDays);
            var deleted = new List<Guid>();
            var errors = new List<BackupRetentionError>();
            for (var index = 0; index < artifacts.Length; index++)
            {
                var artifact = artifacts[index];
                var exceedsCount = policy.RetentionCount > 0 && index >= policy.RetentionCount;
                var exceedsAge = cutoff.HasValue && artifact.CreatedAtUtc < cutoff.Value;
                if ((!exceedsCount && !exceedsAge) || retained.Contains(artifact.Id))
                    continue;

                try
                {
                    if (backups.IsInUse(artifact.Id))
                        continue;
                    backups.Delete(artifact.Id);
                    deleted.Add(artifact.Id);
                }
                catch (BackupCatalogException exception)
                {
                    if (string.Equals(
                        exception.ErrorCode,
                        BackupCatalogService.BackupInUseError,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }
                    errors.Add(new BackupRetentionError(artifact.Id, exception.ErrorCode));
                }
                catch
                {
                    errors.Add(new BackupRetentionError(artifact.Id, RetentionFailedError));
                }
            }

            return new BackupRetentionResult(deleted.ToArray(), errors.ToArray());
        }

        private IEnumerable<BackupArtifact> ListAll(BackupKind kind)
        {
            BackupCursor? cursor = null;
            do
            {
                var page = backups.List(new BackupQuery(100, kind, cursor));
                foreach (var artifact in page.Items)
                    yield return artifact;
                cursor = page.NextCursor;
            }
            while (cursor != null);
        }

        private static BackupRetentionResult Failed(string errorCode) =>
            new BackupRetentionResult(
                Array.Empty<Guid>(),
                new[] { new BackupRetentionError(null, errorCode) });
    }
}
