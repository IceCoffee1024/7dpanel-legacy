using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Adapters.Local.Restore;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;
using LSTY.SevenDPanel.Tests.Local;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Application
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class StageRestoreTests
    {
        [Fact]
        public void Marker_is_persisted_before_the_job_CAS_and_launcher_runs_last()
        {
            var fixture = new Fixture();

            var result = fixture.Stage.Execute(fixture.Job.Id, fixture.Payload, true);

            Assert.Equal(JobStatus.PendingRestart, result.Status);
            Assert.Equal(fixture.Job.RowVersion + 1, result.RowVersion);
            Assert.Equal(new[] { "job:get", "catalog:get", "marker", "job:cas", "launcher" }, fixture.Events);
            Assert.True(fixture.MarkerExistsWhenCasRuns);
            Assert.True(fixture.CasCompletedWhenLauncherRuns);
            Assert.Equal(JobStatus.PendingRestart, fixture.MarkerJob!.Status);
            Assert.Equal(fixture.Artifact, fixture.MarkerArtifact);
        }

        [Theory]
        [InlineData("checksum")]
        [InlineData("manifest")]
        [InlineData("validation")]
        [InlineData("kind")]
        public void Invalid_catalog_metadata_is_rejected_before_creating_a_marker(string mismatch)
        {
            var fixture = new Fixture();
            fixture.Artifact = mismatch switch
            {
                "checksum" => fixture.Artifact with { Sha256 = "not-a-sha256" },
                "manifest" => fixture.Artifact with { ManifestVersion = 2 },
                "validation" => fixture.Artifact with { ValidationStatus = "Corrupt" },
                "kind" => fixture.Artifact with { Kind = BackupKind.ServerConfiguration },
                _ => throw new InvalidOperationException()
            };

            var error = Assert.Throws<StageRestoreException>(() =>
                fixture.Stage.Execute(fixture.Job.Id, fixture.Payload, true));

            Assert.Equal(StageRestore.BackupIntegrityFailedError, error.ErrorCode);
            Assert.DoesNotContain("marker", fixture.Events);
            Assert.DoesNotContain("job:cas", fixture.Events);
            Assert.DoesNotContain("launcher", fixture.Events);
        }

        [Fact]
        public void Missing_backup_maps_to_the_stable_not_found_code()
        {
            var fixture = new Fixture { BackupMissing = true };

            var error = Assert.Throws<StageRestoreException>(() =>
                fixture.Stage.Execute(fixture.Job.Id, fixture.Payload, true));

            Assert.Equal(StageRestore.BackupNotFoundError, error.ErrorCode);
            Assert.Equal(new[] { "job:get", "catalog:get" }, fixture.Events);
        }

        [Fact]
        public void Existing_marker_maps_to_restore_already_pending_without_CAS_or_launch()
        {
            var fixture = new Fixture { MarkerCreated = false };

            var error = Assert.Throws<StageRestoreException>(() =>
                fixture.Stage.Execute(fixture.Job.Id, fixture.Payload, true));

            Assert.Equal(StageRestore.AlreadyPendingError, error.ErrorCode);
            Assert.Contains("marker", fixture.Events);
            Assert.DoesNotContain("job:cas", fixture.Events);
            Assert.DoesNotContain("launcher", fixture.Events);
        }

        [Fact]
        public void CAS_failure_keeps_the_marker_and_never_launches_restart()
        {
            var fixture = new Fixture { CasResult = false };

            var error = Assert.Throws<StageRestoreException>(() =>
                fixture.Stage.Execute(fixture.Job.Id, fixture.Payload, true));

            Assert.Equal(StageRestore.JobStateConflictError, error.ErrorCode);
            Assert.True(fixture.MarkerPersisted);
            Assert.DoesNotContain("launcher", fixture.Events);
        }

        [Fact]
        public void Launcher_failure_leaves_the_marker_and_PendingRestart_job_durable()
        {
            var fixture = new Fixture
            {
                LauncherFailure = new InvalidOperationException("launcher failed")
            };

            var error = Assert.Throws<InvalidOperationException>(() =>
                fixture.Stage.Execute(fixture.Job.Id, fixture.Payload, true));

            Assert.Equal("launcher failed", error.Message);
            Assert.True(fixture.MarkerPersisted);
            Assert.True(fixture.CasCompleted);
            Assert.Equal(JobStatus.PendingRestart, fixture.CurrentJob.Status);
        }

        [Fact]
        public void Missing_strong_confirmation_is_rejected_before_any_read_or_write()
        {
            var fixture = new Fixture();

            var error = Assert.Throws<StageRestoreException>(() =>
                fixture.Stage.Execute(fixture.Job.Id, fixture.Payload, false));

            Assert.Equal(StageRestore.StrongConfirmationRequiredError, error.ErrorCode);
            Assert.Empty(fixture.Events);
        }

        [Fact]
        public void Json_store_maps_the_staged_artifact_and_job_to_the_versioned_marker()
        {
            using var directories = new TestDirectories();
            var store = new JsonPendingRestoreStore(directories.CreateRoots());
            IPendingRestoreMarkerStore markerStore = store;
            var fixture = new Fixture(markerStore);

            Assert.True(markerStore.TryCreateMarker(
                fixture.Artifact,
                fixture.Job with { Status = JobStatus.PendingRestart }));

            var marker = store.ReadMarker();
            Assert.NotNull(marker);
            Assert.Equal(fixture.Artifact.Id, marker!.ArtifactId);
            Assert.Equal(fixture.Artifact.Kind, marker.BackupKind);
            Assert.Equal(fixture.Artifact.BackupRootId, marker.BackupRootId);
            Assert.Equal(fixture.Artifact.RelativeResourceId, marker.RelativeResourceId);
            Assert.Equal(fixture.Artifact.Sha256, marker.Sha256);
            Assert.Equal(fixture.Job.Id, marker.JobSnapshot.JobId);
            Assert.False(markerStore.TryCreateMarker(
                fixture.Artifact,
                fixture.Job with { Status = JobStatus.PendingRestart }));
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class Fixture : IBackupCatalog, IJobStore, IPendingRestoreMarkerStore, IRestartScriptLauncher
        {
            private readonly IPendingRestoreMarkerStore? markerStore;

            public Fixture(IPendingRestoreMarkerStore? markerStore = null)
            {
                this.markerStore = markerStore;
                Job = new JobRecord(
                    Guid.NewGuid(),
                    JobKind.Restore,
                    JobStatus.Queued,
                    "owner",
                    null,
                    "restore-1",
                    "corr-1",
                    Utc(0),
                    null,
                    null,
                    null,
                    null,
                    null,
                    7);
                CurrentJob = Job;
                Artifact = new BackupArtifact(
                    Guid.NewGuid(),
                    BackupKind.PanelDatabase,
                    "primary",
                    "panel-backup.zip",
                    42,
                    new string('a', 64),
                    null,
                    null,
                    "Verified",
                    Utc(0),
                    Guid.NewGuid(),
                    StageRestore.SupportedManifestVersion);
                Payload = new RestorePayload(Artifact.Id, Artifact.Kind, true);
                Stage = new StageRestore(this, this, markerStore ?? this, this, () => Utc(1));
            }

            public List<string> Events { get; } = new List<string>();
            public JobRecord Job { get; }
            public JobRecord CurrentJob { get; private set; }
            public BackupArtifact Artifact { get; set; }
            public RestorePayload Payload { get; }
            public StageRestore Stage { get; }
            public bool BackupMissing { get; set; }
            public bool MarkerCreated { get; set; } = true;
            public bool MarkerPersisted { get; private set; }
            public bool MarkerExistsWhenCasRuns { get; private set; }
            public bool CasResult { get; set; } = true;
            public bool CasCompleted { get; private set; }
            public bool CasCompletedWhenLauncherRuns { get; private set; }
            public Exception? LauncherFailure { get; set; }
            public BackupArtifact? MarkerArtifact { get; private set; }
            public JobRecord? MarkerJob { get; private set; }

            public BackupArtifact Add(CompletedBackup backup) => throw new NotSupportedException();

            public BackupArtifact Get(Guid backupId)
            {
                Events.Add("catalog:get");
                if (BackupMissing) throw new KeyNotFoundException();
                return Artifact;
            }

            public PagedResult<BackupArtifact, BackupCursor> List(BackupQuery query) =>
                throw new NotSupportedException();

            public bool Delete(Guid backupId) => throw new NotSupportedException();

            JobRecord IJobStore.Enqueue(NewJob job) => throw new NotSupportedException();

            public JobRecord? TryClaimNext(string workerId, DateTimeOffset now) =>
                throw new NotSupportedException();

            public bool TryTransition(
                Guid jobId,
                long expectedRowVersion,
                JobStatus expected,
                JobStatus next,
                JobCompletion completion)
            {
                Events.Add("job:cas");
                MarkerExistsWhenCasRuns = MarkerPersisted;
                if (!CasResult) return false;
                CurrentJob = CurrentJob with
                {
                    Status = next,
                    RowVersion = CurrentJob.RowVersion + 1
                };
                CasCompleted = true;
                return true;
            }

            JobRecord IJobStore.Get(Guid jobId)
            {
                Events.Add("job:get");
                return CurrentJob;
            }

            PagedResult<JobRecord, JobCursor> IJobStore.List(JobQuery query) =>
                throw new NotSupportedException();

            public bool TryCreateMarker(BackupArtifact artifact, JobRecord pendingRestartJob)
            {
                Events.Add("marker");
                MarkerArtifact = artifact;
                MarkerJob = pendingRestartJob;
                if (!MarkerCreated) return false;
                MarkerPersisted = true;
                return markerStore?.TryCreateMarker(artifact, pendingRestartJob) ?? true;
            }

            public DateTimeOffset StartConfiguredScript()
            {
                Events.Add("launcher");
                CasCompletedWhenLauncherRuns = CasCompleted;
                if (LauncherFailure != null) throw LauncherFailure;
                return Utc(2);
            }

            private static DateTimeOffset Utc(int minute) =>
                new DateTimeOffset(2026, 7, 27, 6, minute, 0, TimeSpan.Zero);
        }
    }
}
