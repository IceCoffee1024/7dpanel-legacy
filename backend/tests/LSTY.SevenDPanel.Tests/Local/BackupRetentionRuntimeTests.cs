using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Adapters.Local.Jobs;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Local
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Local")]
    public sealed class BackupRetentionRuntimeTests
    {
        [Fact]
        public async Task Enabled_policy_applies_retention_after_cataloging_and_keeps_the_completed_artifact()
        {
            using var fixture = new Fixture(enabled: true, includeOldArchive: true);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var completed = fixture.Catalog.Artifacts.Single(artifact => artifact.SourceJobId == fixture.Running.Id);
            Assert.Equal(JobStatus.Succeeded, Assert.Single(fixture.Jobs.Transitions).Next);
            Assert.Contains(completed.Id, fixture.Catalog.Artifacts.Select(artifact => artifact.Id));
            Assert.DoesNotContain(fixture.Old.Id, fixture.Catalog.Artifacts.Select(artifact => artifact.Id));
        }

        [Fact]
        public async Task Disabled_policy_skips_retention_after_cataloging()
        {
            using var fixture = new Fixture(enabled: false, includeOldArchive: true);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            Assert.Equal(JobStatus.Succeeded, Assert.Single(fixture.Jobs.Transitions).Next);
            Assert.Contains(fixture.Old.Id, fixture.Catalog.Artifacts.Select(artifact => artifact.Id));
        }

        [Fact]
        public async Task Retention_cleanup_failure_does_not_reverse_the_successful_backup_or_delete_it()
        {
            using var fixture = new Fixture(enabled: true, includeOldArchive: false);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var completed = fixture.Catalog.Artifacts.Single(artifact => artifact.SourceJobId == fixture.Running.Id);
            Assert.Equal(JobStatus.Succeeded, Assert.Single(fixture.Jobs.Transitions).Next);
            Assert.Contains(completed.Id, fixture.Catalog.Artifacts.Select(artifact => artifact.Id));
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Local")]

        private sealed class Fixture : IDisposable
        {
            private readonly TestDirectories directories = new TestDirectories();

            public Fixture(bool enabled, bool includeOldArchive)
            {
                File.WriteAllText(Path.Combine(directories.World, "main.ttw"), "world-state");
                var events = new RecordingEvents();
                Running = TestJobs.Running(JobKind.WorldBackup, 301);
                Jobs = new RecordingJobStore(events, Running);
                var payloads = new RecordingPayloadReader(events, Running.Id, "Navezgane");
                var roots = directories.CreateRoots();
                var archives = new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots));
                Catalog = new InMemoryCatalog();
                Old = new BackupArtifact(
                    Guid.NewGuid(), BackupKind.World, roots.BackupRootId, "old-world.zip", 3,
                    new string('0', 64), "Navezgane", roots.GameVersion, "Verified", Utc(0),
                    Guid.NewGuid(), 1);
                Catalog.Artifacts.Add(Old);
                if (includeOldArchive)
                    File.WriteAllText(roots.ResolveBackupResource(Old.RelativeResourceId), "old");

                var retention = new BackupRetentionService(
                    new BackupCatalogService(Catalog, archives, Jobs, payloads));
                var policies = new PolicyStore(new BackupPolicyDefinition(
                    BackupKind.World, enabled, "0 0 * * *", "UTC", roots.BackupRootId, 1, 0, true, 0));
                Consumer = new BackgroundWorkConsumer(
                    Jobs,
                    payloads,
                    new RecordingWorldSaveGateway(events),
                    archives,
                    Catalog,
                    "worker-1",
                    () => Utc(1),
                    TimeSpan.FromMilliseconds(1),
                    backupRetention: retention,
                    backupPolicies: policies);
            }

            public BackgroundWorkConsumer Consumer { get; }
            public RecordingJobStore Jobs { get; }
            public InMemoryCatalog Catalog { get; }
            public BackupArtifact Old { get; }
            public JobRecord Running { get; }

            public void Dispose() => directories.Dispose();
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Local")]

        private sealed class PolicyStore : IBackupPolicyStore
        {
            private readonly BackupPolicyDefinition policy;

            public PolicyStore(BackupPolicyDefinition policy) => this.policy = policy;

            public IReadOnlyList<BackupPolicyDefinition> List() => new[] { policy };
            public BackupPolicyDefinition? Get(BackupKind kind) => kind == policy.Kind ? policy : null;
            public BackupPolicyDefinition Upsert(BackupPolicyDefinition definition) => throw new NotSupportedException();
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Local")]

        private sealed class InMemoryCatalog : IBackupCatalog
        {
            public List<BackupArtifact> Artifacts { get; } = new List<BackupArtifact>();

            public BackupArtifact Add(CompletedBackup backup)
            {
                var artifact = new BackupArtifact(
                    backup.Id, backup.Kind, backup.BackupRootId, backup.RelativeResourceId, backup.SizeBytes,
                    backup.Sha256, backup.WorldId, backup.GameVersion, backup.ValidationStatus,
                    backup.CreatedAtUtc, backup.SourceJobId, backup.ManifestVersion);
                Artifacts.Add(artifact);
                return artifact;
            }

            public BackupArtifact Get(Guid backupId) => Artifacts.Single(artifact => artifact.Id == backupId);

            public PagedResult<BackupArtifact, BackupCursor> List(BackupQuery query) =>
                new PagedResult<BackupArtifact, BackupCursor>(
                    Artifacts.Where(artifact => !query.Kind.HasValue || artifact.Kind == query.Kind.Value).ToArray(),
                    null);

            public bool Delete(Guid backupId) => Artifacts.RemoveAll(artifact => artifact.Id == backupId) == 1;
        }

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 27, 0, minute, 0, TimeSpan.Zero);
    }
}
