using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Backups;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class CreateWorldBackupHttpRequest
    {
        public string? WorldName { get; set; }
        public string? IdempotencyKey { get; set; }
        public string? CorrelationId { get; set; }
    }

    public sealed class CreateBackupHttpRequest
    {
        public string? IdempotencyKey { get; set; }
        public string? CorrelationId { get; set; }
    }

    public sealed class RestoreBackupHttpRequest
    {
        public string? IdempotencyKey { get; set; }
        public string? CorrelationId { get; set; }
        public bool RestartAfterStage { get; set; }
        public bool StrongConfirmed { get; set; }
    }

    public sealed class BackupHttpResponse
    {
        public BackupHttpResponse(BackupArtifact artifact)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            Id = artifact.Id;
            Kind = artifact.Kind.ToString();
            SizeBytes = artifact.SizeBytes;
            Sha256 = artifact.Sha256;
            WorldId = artifact.WorldId;
            GameVersion = artifact.GameVersion;
            ValidationStatus = artifact.ValidationStatus;
            CreatedAtUtc = artifact.CreatedAtUtc;
            SourceJobId = artifact.SourceJobId;
            ManifestVersion = artifact.ManifestVersion;
        }

        public Guid Id { get; }
        public string Kind { get; }
        public long SizeBytes { get; }
        public string Sha256 { get; }
        public string? WorldId { get; }
        public string? GameVersion { get; }
        public string ValidationStatus { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public Guid SourceJobId { get; }
        public int ManifestVersion { get; }
    }

    public sealed class BackupPageHttpResponse
    {
        public BackupPageHttpResponse(
            IReadOnlyList<BackupArtifact> items,
            string? nextCursor)
        {
            Items = (items ?? throw new ArgumentNullException(nameof(items)))
                .Select(item => new BackupHttpResponse(item))
                .ToArray();
            NextCursor = nextCursor;
        }

        public IReadOnlyList<BackupHttpResponse> Items { get; }
        public string? NextCursor { get; }
    }
}
