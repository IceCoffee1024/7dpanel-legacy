using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Adapters.Local.Jobs;
using LSTY.SevenDPanel.Adapters.Local.Restore;
using LSTY.SevenDPanel.Adapters.Local.Schedules;
using LSTY.SevenDPanel.Adapters.Local.ServerOperations;
using LSTY.SevenDPanel.Adapters.Local.MapTiles;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Backups;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Jobs;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.MapTiles;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Schedules;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.WorldOperations;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Backups;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ServerOperations;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World;
using LSTY.SevenDPanel.Adapters.SevenDays.Announcements;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Community;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Diagnostics;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Announcements;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Application.Schedules;
using LSTY.SevenDPanel.Application.WorldOperations;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Diagnostics;
using LSTY.SevenDPanel.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LSTY.SevenDPanel.DependencyInjection.Registration
{
    internal static class OperationsServiceRegistration
    {
        internal static void Register(
            IServiceCollection services,
            PanelCompositionContext context)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var options = context.Options;
            var dataDirectory = context.DataDirectory;
            var log = context.Log;

            services.AddSingleton<FileSystemBackupArchiveStore>();
            services.AddSingleton<IBackupArchiveStorage>(serviceProvider =>
                serviceProvider.GetRequiredService<FileSystemBackupArchiveStore>());
            services.AddSingleton<SqliteJobStore>();
            services.AddSingleton<IJobStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteJobStore>());
            services.AddSingleton<BackgroundWorkerJobStore>();
            services.AddSingleton<SqliteJobPayloadStore>();
            services.AddSingleton<IJobSubmissionStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteJobPayloadStore>());
            services.AddSingleton<IJobPayloadReader>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteJobPayloadStore>());
            services.AddSingleton<SqliteBackupCatalog>();
            services.AddSingleton<IBackupCatalog>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteBackupCatalog>());
            services.AddSingleton<SqliteBackupPolicyStore>();
            services.AddSingleton<IBackupPolicyStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteBackupPolicyStore>());
            services.AddSingleton<SqliteWorldOperationStore>();
            services.AddSingleton<IWorldOperationStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteWorldOperationStore>());
            services.AddSingleton<IWorldOperationExecutionStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteWorldOperationStore>());
            services.AddSingleton<IWorldOperationRecoveryStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteWorldOperationStore>());
            services.AddSingleton(serviceProvider => new SqliteWorldOperationJobBridge(
                serviceProvider.GetRequiredService<SqliteConnectionFactory>(),
                () => DateTimeOffset.UtcNow));

            services.AddSingleton<SqliteRestoreResultMergeStore>();
            services.AddSingleton<IRestoreResultMergeStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteRestoreResultMergeStore>());
            services.AddSingleton<JsonPendingRestoreStore>();
            services.AddSingleton<IPendingRestoreMarkerStore>(serviceProvider =>
                serviceProvider.GetRequiredService<JsonPendingRestoreStore>());
            services.AddSingleton<SqliteScheduleStore>();
            services.AddSingleton<IScheduleStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteScheduleStore>());
            services.AddSingleton<SevenDaysWorldSaveGateway>();
            services.AddSingleton<IWorldSaveGateway>(serviceProvider =>
                serviceProvider.GetRequiredService<SevenDaysWorldSaveGateway>());
            services.AddSingleton<SevenDaysWorldToolCatalog>();
            services.AddSingleton<IWorldToolCatalog>(serviceProvider =>
                serviceProvider.GetRequiredService<SevenDaysWorldToolCatalog>());
            services.AddSingleton<QueryWorldUseCase>();
            services.AddSingleton<QueryWorldToolCatalogUseCase>();
            services.AddSingleton<SevenDaysAnnouncementGateway>();
            services.AddSingleton<IAnnouncementGateway>(serviceProvider =>
                serviceProvider.GetRequiredService<SevenDaysAnnouncementGateway>());
            services.AddSingleton(serviceProvider => new JobService(
                serviceProvider.GetRequiredService<IJobStore>(),
                () => DateTimeOffset.UtcNow));
            services.AddSingleton(serviceProvider => new CreateWorldBackup(
                serviceProvider.GetRequiredService<IJobSubmissionStore>(),
                () => DateTimeOffset.UtcNow));
            services.AddSingleton<CreatePanelDatabaseBackup>();
            services.AddSingleton<CreateServerConfigurationBackup>();
            services.AddSingleton<BackupCatalogService>();
            services.AddSingleton(serviceProvider => new BackupPolicyService(
                serviceProvider.GetRequiredService<IBackupPolicyStore>(),
                new[]
                {
                    serviceProvider.GetRequiredService<ApprovedStorageRoots>().BackupRootId
                }));
            services.AddSingleton<BackupRetentionService>();
            services.AddSingleton(serviceProvider => new BackupPolicyScheduler(
                serviceProvider.GetRequiredService<IBackupPolicyStore>(),
                serviceProvider.GetRequiredService<CreateWorldBackup>(),
                serviceProvider.GetRequiredService<CreatePanelDatabaseBackup>(),
                serviceProvider.GetRequiredService<CreateServerConfigurationBackup>(),
                serviceProvider.GetRequiredService<ApprovedStorageRoots>(),
                log));
            services.AddSingleton(serviceProvider => new StageRestore(
                serviceProvider.GetRequiredService<IBackupCatalog>(),
                serviceProvider.GetRequiredService<IJobStore>(),
                serviceProvider.GetRequiredService<IPendingRestoreMarkerStore>(),
                serviceProvider.GetRequiredService<IRestartScriptLauncher>(),
                () => DateTimeOffset.UtcNow));
            services.AddSingleton<ScheduleService>();
            services.AddSingleton<AnnouncementService>();
            services.AddSingleton<IWorldRestoreRuntimeEvidenceSource,
                SevenDaysWorldRestoreRuntimeEvidenceSource>();
            services.AddSingleton<WorldRestoreTimingGate>();
            services.AddSingleton(serviceProvider => new PanelDatabaseBackupJobHandler(
                serviceProvider.GetRequiredService<ApprovedStorageRoots>(),
                serviceProvider.GetRequiredService<FileSystemBackupArchiveStore>(),
                serviceProvider.GetRequiredService<SqliteConnectionFactory>().DatabasePath));
            services.AddSingleton(serviceProvider => new ServerConfigurationBackupJobHandler(
                serviceProvider.GetRequiredService<ApprovedStorageRoots>(),
                serviceProvider.GetRequiredService<FileSystemBackupArchiveStore>(),
                new[] { Path.GetFileName(options.ServerConfigurationPath) }));
            services.AddSingleton(serviceProvider => new ScheduledJobExecutor(
                serviceProvider.GetRequiredService<IJobStore>(),
                serviceProvider.GetRequiredService<IJobPayloadReader>(),
                serviceProvider.GetRequiredService<IConsoleCommandGateway>(),
                serviceProvider.GetRequiredService<IRestartScriptLauncher>(),
                serviceProvider.GetRequiredService<IAnnouncementGateway>()));
            services.AddSingleton(serviceProvider => new PendingRestoreApplier(
                serviceProvider.GetRequiredService<ApprovedStorageRoots>(),
                serviceProvider.GetRequiredService<IBackupCatalog>(),
                serviceProvider.GetRequiredService<JsonPendingRestoreStore>(),
                serviceProvider.GetRequiredService<WorldRestoreTimingGate>(),
                Path.GetFileName(
                    serviceProvider.GetRequiredService<SqliteConnectionFactory>().DatabasePath),
                RestoreArchiveLimits.Default));
            services.AddSingleton(serviceProvider => new RestoreResultReconciler(
                serviceProvider.GetRequiredService<JsonPendingRestoreStore>(),
                serviceProvider.GetRequiredService<IRestoreResultMergeStore>(),
                () => DateTimeOffset.UtcNow));
            services.AddSingleton<PendingRestoreStartupStep>();
            services.AddSingleton(serviceProvider => new BackgroundWorkConsumer(
                serviceProvider.GetRequiredService<BackgroundWorkerJobStore>(),
                serviceProvider.GetRequiredService<IJobPayloadReader>(),
                serviceProvider.GetRequiredService<IWorldSaveGateway>(),
                serviceProvider.GetRequiredService<FileSystemBackupArchiveStore>(),
                serviceProvider.GetRequiredService<PanelDatabaseBackupJobHandler>(),
                serviceProvider.GetRequiredService<ServerConfigurationBackupJobHandler>(),
                serviceProvider.GetRequiredService<IBackupCatalog>(),
                "7dpanel-worker",
                () => DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(1),
                serviceProvider.GetRequiredService<ScheduledJobExecutor>(),
                serviceProvider.GetRequiredService<IWorldOperationJobHandler>(),
                serviceProvider.GetRequiredService<BackupRetentionService>(),
                serviceProvider.GetRequiredService<IBackupPolicyStore>()));
            services.AddSingleton(serviceProvider => new BackgroundScheduler(
                serviceProvider.GetRequiredService<IScheduleStore>(),
                "7dpanel-scheduler",
                () => DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(30),
                serviceProvider.GetRequiredService<IAutomationTriggerIngress>(),
                serviceProvider.GetRequiredService<BackupPolicyScheduler>()));

            services.AddSingleton<SqliteServerOperationAuditTrail>();
            services.AddSingleton<IServerOperationAuditTrail>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteServerOperationAuditTrail>());
            services.AddSingleton<SqliteServerOperationStore>();
            services.AddSingleton<IServerOperationStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteServerOperationStore>());
            services.AddSingleton<ServerOperationProcessInstance>();
            services.AddSingleton<GetServerOperationUseCase>();
            services.AddSingleton<ReconcileServerOperationsUseCase>();
            services.AddSingleton<RestartScriptLauncher>();
            services.AddSingleton<IRestartScriptLauncher>(serviceProvider =>
                serviceProvider.GetRequiredService<RestartScriptLauncher>());
            services.AddSingleton<SevenDaysShutdownServerGateway>();
            services.AddSingleton<IShutdownServerGateway>(serviceProvider =>
                serviceProvider.GetRequiredService<SevenDaysShutdownServerGateway>());
            services.AddSingleton<RestartServerUseCase>();
            services.AddSingleton<ShutdownServerUseCase>();

            services.AddSingleton(_ => new LocalMapResourcePublisher(
                Path.Combine(dataDirectory, "world-operations", "map-staging"),
                Path.Combine(dataDirectory, "map-resources")));
            services.AddSingleton<IMapResourcePublisher>(serviceProvider =>
                serviceProvider.GetRequiredService<LocalMapResourcePublisher>());
            services.AddSingleton<IMapTileStore>(serviceProvider => new LocalMapTileStore(() =>
            {
                var current = serviceProvider
                    .GetRequiredService<LocalMapResourcePublisher>()
                    .Current;
                return current == null
                    ? null
                    : new LocalMapTileRoot(
                        current.WorldId,
                        current.RootPath,
                        current.MapResourceVersion);
            }));
            services.AddSingleton<GetMapTileUseCase>();
            services.AddSingleton<DeleteLandClaimUseCase>();
            services.AddSingleton<MoveOnlinePlayerUseCase>();
            services.AddSingleton<MoveWorldEntityUseCase>();
            services.AddSingleton<SubmitMapJobUseCase>();
            services.AddSingleton<CopyRegionUseCase>();
            services.AddSingleton<FillRegionUseCase>();
            services.AddSingleton<ClearRegionUseCase>();
            services.AddSingleton<PasteRegionUseCase>();
            services.AddSingleton<SetBlockUseCase>();
            services.AddSingleton<PlacePrefabUseCase>();
            services.AddSingleton<RemovePrefabUseCase>();
            services.AddSingleton<SpawnWorldEntityUseCase>();
            services.AddSingleton<DeleteWorldEntityUseCase>();
            services.AddSingleton<CleanupWorldEntitiesUseCase>();
            services.AddSingleton<ReloadGameResourceUseCase>();
            services.AddSingleton<CollectGameGarbageUseCase>();
            services.AddSingleton<UndoWorldChangeSetUseCase>();
            services.AddSingleton(serviceProvider => new SevenDaysMapWorldOperationHandler(
                Path.Combine(dataDirectory, "world-operations", "map-staging"),
                () => serviceProvider
                    .GetRequiredService<LocalMapResourcePublisher>()
                    .Current?.MapResourceVersion));
            services.AddSingleton(serviceProvider => new SevenDaysRegionOperationHandler(
                serviceProvider.GetRequiredService<IWorldChangeSetMetadataStore>(),
                serviceProvider.GetRequiredService<IWorldChangeSetBlobStore>(),
                () => serviceProvider
                    .GetRequiredService<LocalMapResourcePublisher>()
                    .Current?.MapResourceVersion));
            services.AddSingleton(serviceProvider => new SevenDaysBlockPrefabOperationHandler(
                serviceProvider.GetRequiredService<IWorldChangeSetMetadataStore>(),
                serviceProvider.GetRequiredService<IWorldChangeSetBlobStore>(),
                () => serviceProvider
                    .GetRequiredService<LocalMapResourcePublisher>()
                    .Current?.MapResourceVersion));
            services.AddSingleton(serviceProvider => new SevenDaysEntityMaintenanceHandler(
                () => serviceProvider
                    .GetRequiredService<LocalMapResourcePublisher>()
                    .Current?.MapResourceVersion));
            services.AddSingleton(serviceProvider => new SevenDaysRuntimeMaintenanceHandler(
                () => serviceProvider
                    .GetRequiredService<LocalMapResourcePublisher>()
                    .Current?.MapResourceVersion));
            services.AddSingleton<SevenDaysUndoOperationHandler>();
            services.AddSingleton<IWorldChangeSetPreflightGateway>(serviceProvider =>
                serviceProvider.GetRequiredService<SevenDaysUndoOperationHandler>());
            services.AddSingleton(serviceProvider => new WorldOperationJobHandler(
                serviceProvider.GetRequiredService<IWorldOperationExecutionStore>(),
                serviceProvider.GetRequiredService<SevenDaysMapWorldOperationHandler>(),
                serviceProvider.GetRequiredService<IMapResourcePublisher>(),
                serviceProvider.GetRequiredService<SevenDaysRegionOperationHandler>(),
                serviceProvider.GetRequiredService<SevenDaysBlockPrefabOperationHandler>(),
                serviceProvider.GetRequiredService<SevenDaysEntityMaintenanceHandler>(),
                serviceProvider.GetRequiredService<SevenDaysRuntimeMaintenanceHandler>(),
                serviceProvider.GetRequiredService<SevenDaysUndoOperationHandler>(),
                () => DateTimeOffset.UtcNow));
            services.AddSingleton<IWorldOperationJobHandler>(serviceProvider =>
                serviceProvider.GetRequiredService<WorldOperationJobHandler>());

            services.AddSingleton(serviceProvider => new JobsAndSchedulingRuntime(
                serviceProvider.GetRequiredService<PendingRestoreStartupStep>(),
                serviceProvider.GetRequiredService<BackgroundWorkerJobStore>(),
                serviceProvider.GetRequiredService<BackgroundWorkConsumer>(),
                serviceProvider.GetRequiredService<BackgroundScheduler>(),
                serviceProvider.GetRequiredService<GameResourceCatalogRuntime>(),
                TimeSpan.FromSeconds(30)));
            services.AddSingleton(serviceProvider => new WorldOperationRuntime(
                serviceProvider.GetRequiredService<IWorldOperationRecoveryStore>(),
                serviceProvider.GetRequiredService<CommunityVoteRuntime>(),
                () => DateTimeOffset.UtcNow));
            services.AddSingleton(serviceProvider => new ChatCommandMixedTestRuntime(
                options.ChatCommandTesting,
                serviceProvider.GetRequiredService<GameChatCommandCatalog>(),
                RunClientInfoBoundaryProbes,
                serviceProvider.GetRequiredService<WorldOperationRuntime>()));
            services.AddSingleton(serviceProvider => new ServerOperationRecoveryRuntime(
                serviceProvider.GetRequiredService<ReconcileServerOperationsUseCase>(),
                serviceProvider.GetRequiredService<ServerOperationProcessInstance>(),
                serviceProvider.GetRequiredService<ChatCommandMixedTestRuntime>()));
        }

        private static IReadOnlyList<string> RunClientInfoBoundaryProbes(string stableIdentity)
        {
            var probe = new ClientInfoBoundaryProbe(stableIdentity);
            var results = new[]
            {
                probe.ProbeIdentityAsync().GetAwaiter().GetResult(),
                probe.ProbeCurrentPositionAsync().GetAwaiter().GetResult(),
                probe.ProbePrivateReplyAsync(
                    "[7DPanel] Chat-command boundary probe succeeded.")
                    .GetAwaiter()
                    .GetResult()
            };
            return results.Select(result =>
            {
                var detail = string.IsNullOrWhiteSpace(result.Detail)
                    ? string.Empty
                    : " - " + result.Detail;
                return "boundary/" + result.ProbeName + ": " +
                       result.Status.ToString().ToUpperInvariant() +
                       " (" + result.Code + ")" + detail;
            }).ToArray();
        }

    }
}
