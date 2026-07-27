using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Schedules;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Application.WorldOperations;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Adapters.Local.Jobs
{
    public sealed class BackgroundWorkConsumer : IDisposable
    {
        private const string SaveFailedError = "world_save_failed";
        private const string InterruptedError = "world_backup_interrupted";
        private const string PayloadMissingError = "world_payload_missing";
        private const string PanelDatabasePayloadMissingError = "panel_database_payload_missing";
        private const string ServerConfigurationPayloadMissingError = "server_configuration_payload_missing";
        private const string CatalogFailedError = "backup_catalog_failed";
        private const string KindNotWiredError = "job_kind_not_wired";
        private const string WorldOperationInterruptedError = "world_operation_interrupted";
        private const string WorldOperationResultUnknownError = "world_operation_result_unknown";

        private readonly IJobStore jobs;
        private readonly IJobPayloadReader payloads;
        private readonly IWorldSaveGateway worldSave;
        private readonly FileSystemBackupArchiveStore archives;
        private readonly IBackupCatalog catalog;
        private readonly BackupRetentionService? backupRetention;
        private readonly IBackupPolicyStore? backupPolicies;
        private readonly PanelDatabaseBackupJobHandler? panelDatabaseBackups;
        private readonly ServerConfigurationBackupJobHandler? serverConfigurationBackups;
        private readonly ScheduledJobExecutor? scheduledJobs;
        private readonly IWorldOperationJobHandler? worldOperations;
        private readonly string workerId;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly TimeSpan emptyQueueDelay;
        private readonly SemaphoreSlim activeJob = new SemaphoreSlim(1, 1);

        public BackgroundWorkConsumer(
            IJobStore jobs,
            IJobPayloadReader payloads,
            IWorldSaveGateway worldSave,
            FileSystemBackupArchiveStore archives,
            IBackupCatalog catalog,
            string workerId,
            Func<DateTimeOffset> utcNow,
            TimeSpan emptyQueueDelay,
            ScheduledJobExecutor? scheduledJobs = null,
            IWorldOperationJobHandler? worldOperations = null,
            BackupRetentionService? backupRetention = null,
            IBackupPolicyStore? backupPolicies = null)
        {
            this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            this.payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
            this.worldSave = worldSave ?? throw new ArgumentNullException(nameof(worldSave));
            this.archives = archives ?? throw new ArgumentNullException(nameof(archives));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.backupRetention = backupRetention;
            this.backupPolicies = backupPolicies;
            this.scheduledJobs = scheduledJobs;
            this.worldOperations = worldOperations;
            this.workerId = string.IsNullOrWhiteSpace(workerId)
                ? throw new ArgumentException("A worker id is required.", nameof(workerId))
                : workerId.Trim();
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            if (emptyQueueDelay <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(emptyQueueDelay));
            this.emptyQueueDelay = emptyQueueDelay;
        }

        public BackgroundWorkConsumer(
            IJobStore jobs,
            IJobPayloadReader payloads,
            IWorldSaveGateway worldSave,
            FileSystemBackupArchiveStore archives,
            PanelDatabaseBackupJobHandler panelDatabaseBackups,
            ServerConfigurationBackupJobHandler serverConfigurationBackups,
            IBackupCatalog catalog,
            string workerId,
            Func<DateTimeOffset> utcNow,
            TimeSpan emptyQueueDelay,
            ScheduledJobExecutor? scheduledJobs = null,
            IWorldOperationJobHandler? worldOperations = null,
            BackupRetentionService? backupRetention = null,
            IBackupPolicyStore? backupPolicies = null)
            : this(
                jobs,
                payloads,
                worldSave,
                archives,
                catalog,
                workerId,
                utcNow,
                emptyQueueDelay,
                scheduledJobs,
                worldOperations,
                backupRetention,
                backupPolicies)
        {
            this.panelDatabaseBackups = panelDatabaseBackups ??
                throw new ArgumentNullException(nameof(panelDatabaseBackups));
            this.serverConfigurationBackups = serverConfigurationBackups ??
                throw new ArgumentNullException(nameof(serverConfigurationBackups));
        }

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var consumed = await ConsumeNextAsync(stoppingToken).ConfigureAwait(false);
                if (consumed) continue;
                try
                {
                    await Task.Delay(emptyQueueDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        public async Task<bool> ConsumeNextAsync(CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested) return false;
            try
            {
                await activeJob.WaitAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                if (stoppingToken.IsCancellationRequested) return false;
                var claimed = jobs.TryClaimNext(workerId, RequireUtc(utcNow()));
                if (claimed == null) return false;
                if (claimed.Status != JobStatus.Running)
                    throw new InvalidOperationException("claimed_job_not_running");

                switch (claimed.Kind)
                {
                    case JobKind.WorldBackup:
                        await ProcessWorldBackupAsync(claimed, stoppingToken).ConfigureAwait(false);
                        break;
                    case JobKind.PanelDatabaseBackup:
                        if (panelDatabaseBackups == null)
                            Complete(claimed, JobStatus.Failed, KindNotWiredError, null);
                        else
                            ProcessPanelDatabaseBackup(claimed);
                        break;
                    case JobKind.ServerConfigurationBackup:
                        if (serverConfigurationBackups == null)
                            Complete(claimed, JobStatus.Failed, KindNotWiredError, null);
                        else
                            ProcessServerConfigurationBackup(claimed);
                        break;
                    case JobKind.Restore:
                        Complete(claimed, JobStatus.Failed, KindNotWiredError, null);
                        break;
                    case JobKind.ScheduledConsoleCommand:
                        if (scheduledJobs == null)
                            Complete(claimed, JobStatus.Failed, KindNotWiredError, null);
                        else
                            await scheduledJobs.ExecuteConsoleCommandAsync(claimed, stoppingToken)
                                .ConfigureAwait(false);
                        break;
                    case JobKind.ScheduledRestart:
                        if (scheduledJobs == null)
                            Complete(claimed, JobStatus.Failed, KindNotWiredError, null);
                        else
                            await scheduledJobs.ExecuteRestartAsync(claimed, stoppingToken)
                                .ConfigureAwait(false);
                        break;
                    case JobKind.ScheduledAnnouncement:
                        if (scheduledJobs == null)
                            Complete(claimed, JobStatus.Failed, KindNotWiredError, null);
                        else
                            await scheduledJobs.ExecuteAnnouncementAsync(claimed, stoppingToken)
                                .ConfigureAwait(false);
                        break;
                    case JobKind.WorldOperation:
                        if (worldOperations == null)
                            Complete(claimed, JobStatus.Failed, KindNotWiredError, null);
                        else
                            await ProcessWorldOperationAsync(claimed, stoppingToken).ConfigureAwait(false);
                        break;
                    default:
                        Complete(claimed, JobStatus.Failed, KindNotWiredError, null);
                        break;
                }
                return true;
            }
            finally
            {
                activeJob.Release();
            }
        }

        public void Dispose()
        {
            activeJob.Dispose();
        }

        private async Task ProcessWorldOperationAsync(
            JobRecord claimed,
            CancellationToken stoppingToken)
        {
            WorldOperationJobCompletion completion;
            try
            {
                completion = await worldOperations!.ExecuteAsync(claimed.Id, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                Complete(claimed, JobStatus.Interrupted, WorldOperationInterruptedError, null);
                return;
            }
            catch
            {
                Complete(claimed, JobStatus.ResultUnknown, WorldOperationResultUnknownError, null);
                return;
            }

            var progress = completion.Progress == null
                ? null
                : new JobProgress(completion.Progress.Current, completion.Progress.Total);
            Complete(
                claimed,
                ToJobStatus(completion.Status),
                completion.ErrorCode,
                progress);
        }

        private static JobStatus ToJobStatus(WorldOperationStatus status)
        {
            switch (status)
            {
                case WorldOperationStatus.Succeeded:
                    return JobStatus.Succeeded;
                case WorldOperationStatus.Failed:
                case WorldOperationStatus.RollbackFailed:
                    return JobStatus.Failed;
                case WorldOperationStatus.Cancelled:
                case WorldOperationStatus.Interrupted:
                    return JobStatus.Interrupted;
                case WorldOperationStatus.ResultUnknown:
                case WorldOperationStatus.Queued:
                case WorldOperationStatus.Running:
                default:
                    return JobStatus.ResultUnknown;
            }
        }

        private async Task ProcessWorldBackupAsync(
            JobRecord claimed,
            CancellationToken stoppingToken)
        {
            WorldBackupPayload payload;
            try
            {
                payload = payloads.GetWorldBackup(claimed.Id);
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, PayloadMissingError, null);
                return;
            }

            try
            {
                archives.ValidateWorldSelection(payload.WorldName);
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, FileSystemBackupArchiveStore.SourceUnavailableError, null);
                return;
            }

            try
            {
                await worldSave.SaveCurrentWorldAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                Complete(claimed, JobStatus.Interrupted, InterruptedError, null);
                return;
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, SaveFailedError, null);
                return;
            }

            WorldBackupArchive archive;
            var completedAt = RequireUtc(utcNow());
            try
            {
                archive = archives.CreateWorldArchive(
                    claimed.Id,
                    payload.WorldName,
                    completedAt);
            }
            catch (BackupArchiveException exception)
            {
                Complete(claimed, JobStatus.Failed, exception.ErrorCode, null);
                return;
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, FileSystemBackupArchiveStore.ZipFailedError, null);
                return;
            }

            try
            {
                var artifact = catalog.Add(new CompletedBackup(
                    archive.Id,
                    BackupKind.World,
                    archives.BackupRootId,
                    archive.RelativeResourceId,
                    archive.SizeBytes,
                    archive.Sha256,
                    payload.WorldName,
                    archives.GameVersion,
                    "Verified",
                    completedAt,
                    claimed.Id,
                    archive.ManifestVersion));
                ApplyRetention(artifact);
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, CatalogFailedError, null);
                return;
            }

            Complete(
                claimed,
                JobStatus.Succeeded,
                null,
                new JobProgress(archive.SourceBytes, archive.SourceBytes));
        }

        private void ProcessPanelDatabaseBackup(JobRecord claimed)
        {
            try
            {
                _ = payloads.GetPanelDatabaseBackup(claimed.Id);
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, PanelDatabasePayloadMissingError, null);
                return;
            }

            ProcessFileBackup(
                claimed,
                () => panelDatabaseBackups!.Execute(claimed.Id, RequireUtc(utcNow())));
        }

        private void ProcessServerConfigurationBackup(JobRecord claimed)
        {
            try
            {
                _ = payloads.GetServerConfigurationBackup(claimed.Id);
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, ServerConfigurationPayloadMissingError, null);
                return;
            }

            ProcessFileBackup(
                claimed,
                () => serverConfigurationBackups!.Execute(claimed.Id, RequireUtc(utcNow())));
        }

        private void ProcessFileBackup(
            JobRecord claimed,
            Func<CompletedBackup> execute)
        {
            CompletedBackup completed;
            try
            {
                completed = execute();
            }
            catch (BackupArchiveException exception)
            {
                Complete(claimed, JobStatus.Failed, exception.ErrorCode, null);
                return;
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, FileSystemBackupArchiveStore.ZipFailedError, null);
                return;
            }

            try
            {
                var artifact = catalog.Add(completed);
                ApplyRetention(artifact);
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, CatalogFailedError, null);
                return;
            }

            Complete(claimed, JobStatus.Succeeded, null, null);
        }

        private void ApplyRetention(BackupArtifact artifact)
        {
            if (backupRetention == null || backupPolicies == null)
                return;

            try
            {
                var policy = backupPolicies.Get(artifact.Kind);
                if (policy == null || !policy.Enabled)
                    return;

                backupRetention.Apply(
                    new BackupRetentionPolicy(
                        artifact.Kind,
                        policy.RetentionCount,
                        policy.RetentionDays,
                        new[] { artifact.Id }),
                    RequireUtc(utcNow()));
            }
            catch
            {
                // Retention is best-effort after a successful backup catalog write.
            }
        }

        private void Complete(
            JobRecord claimed,
            JobStatus next,
            string? errorCode,
            JobProgress? progress)
        {
            var changed = jobs.TryTransition(
                claimed.Id,
                claimed.RowVersion,
                JobStatus.Running,
                next,
                new JobCompletion(RequireUtc(utcNow()), progress, errorCode));
            if (!changed)
                throw new InvalidOperationException("job_state_conflict");
        }

        private static DateTimeOffset RequireUtc(DateTimeOffset value)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("worker_clock_not_utc");
            return value;
        }
    }
}
