using System;
using System.Collections.Generic;
using System.IO;
using Dapper;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Discord;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Adapters.Local.GeoIp;
using LSTY.SevenDPanel.Adapters.Local.Jobs;
using LSTY.SevenDPanel.Adapters.Local.MapTiles;
using LSTY.SevenDPanel.Adapters.Local.Restore;
using LSTY.SevenDPanel.Adapters.Local.Schedules;
using LSTY.SevenDPanel.Adapters.Local.WorldOperations;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Backups;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Community;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Jobs;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.MapTiles;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Modules;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Rewards;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Schedules;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.WorldOperations;
using LSTY.SevenDPanel.Adapters.SevenDays.Announcements;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.AccessPolicies;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Activity;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Automations;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.GameEvents;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Community;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.AccessLists;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Backups;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GamePermissions;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Mods;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Overview;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Rewards;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ServerOperations;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Community;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.GameEvents;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Rewards;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Announcements;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Application.Discord;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.GameEvents;
using LSTY.SevenDPanel.Application.GeoIp;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Application.Modules;
using LSTY.SevenDPanel.Application.Mods;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Application.Schedules;
using LSTY.SevenDPanel.Application.ServerConfiguration;
using LSTY.SevenDPanel.Application.WorldOperations;
using LSTY.SevenDPanel.Domain.Jobs;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Authentication;
using LSTY.SevenDPanel.Hosting.Platform;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Microsoft.Extensions.DependencyInjection;
using LSTY.SevenDPanel.Mods;
using LSTY.SevenDPanel.ServerConfiguration;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal static class PanelServiceProviderFactory
    {
        public static ServiceProviderRuntime CreateRuntime(
            PanelHostOptions options,
            string dataDirectory,
            string? assetRoot,
            Action<string> log)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(dataDirectory))
                throw new ArgumentException("The panel data directory is required.", nameof(dataDirectory));
            if (log == null) throw new ArgumentNullException(nameof(log));

            ServiceProvider? provider = null;
            try
            {
                var services = new ServiceCollection();
                services.AddSingleton(options);
                services.AddSingleton(options.Authentication);
                services.AddSingleton(options.Overview);
                services.AddSingleton(options.Restart);
                services.AddSingleton(options.PlayerEvidence);
                services.AddSingleton(_ => new SqliteConnectionFactory(
                    Path.Combine(dataDirectory, "7dpanel.db")));
                services.AddSingleton(serviceProvider => new SqliteDatabaseBootstrapper(
                    serviceProvider.GetRequiredService<SqliteConnectionFactory>(),
                    log));
                services.AddSingleton(_ => CreateApprovedStorageRoots(options, dataDirectory));
                services.AddSingleton<AtomicFileWriter>();
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
                services.AddSingleton<SqliteWorldChangeSetMetadataStore>();
                services.AddSingleton<IWorldChangeSetMetadataStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteWorldChangeSetMetadataStore>());
                services.AddSingleton(_ => new LocalWorldChangeSetBlobStore(
                    Path.Combine(dataDirectory, "world-change-sets")));
                services.AddSingleton<IWorldChangeSetBlobStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<LocalWorldChangeSetBlobStore>());
                services.AddSingleton<SqliteFeatureModuleStateStore>();
                services.AddSingleton<IFeatureModuleStateStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteFeatureModuleStateStore>());
                services.AddSingleton<FeatureModuleGate>();
                services.AddSingleton(serviceProvider => new FeatureModuleWorldOperationJobBridge(
                    serviceProvider.GetRequiredService<SqliteWorldOperationJobBridge>(),
                    serviceProvider.GetRequiredService<FeatureModuleGate>()));
                services.AddSingleton<IWorldOperationJobBridge>(serviceProvider =>
                    serviceProvider.GetRequiredService<FeatureModuleWorldOperationJobBridge>());
                services.AddSingleton<FeatureModuleJobActivityQuery>();
                services.AddSingleton<IFeatureModuleActivityQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<FeatureModuleJobActivityQuery>());
                services.AddSingleton(serviceProvider => new FeatureModuleUseCases(
                    serviceProvider.GetRequiredService<IFeatureModuleStateStore>(),
                    serviceProvider.GetRequiredService<IFeatureModuleActivityQuery>(),
                    () => DateTimeOffset.UtcNow));
                services.AddSingleton<SqliteAutomationStore>();
                services.AddSingleton<IAutomationStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteAutomationStore>());
                services.AddSingleton<IAutomationExecutionRecoveryStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteAutomationStore>());
                services.AddSingleton<IAutomationExecutionQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteAutomationStore>());
                services.AddSingleton<AutomationExecutionRecoveryService>();
                services.AddSingleton<SqliteDiscordIntegrationStore>();
                services.AddSingleton<IDiscordIntegrationStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteDiscordIntegrationStore>());
                services.AddSingleton<IDiscordInteractionPersistenceStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteDiscordIntegrationStore>());
                services.AddSingleton<IDiscordInteractionSignatureVerifier>(serviceProvider =>
                    new DiscordInteractionSignatureVerifier(
                        () => serviceProvider
                            .GetRequiredService<IDiscordIntegrationStore>()
                            .GetSecret("interactionPublicKey")?.SecretValue,
                        () => DateTimeOffset.UtcNow,
                        TimeSpan.FromMinutes(5)));
                services.AddSingleton<GetDiscordConfigurationUseCase>();
                services.AddSingleton(serviceProvider => new SaveDiscordConfigurationUseCase(
                    serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                    () => DateTimeOffset.UtcNow));
                services.AddSingleton(serviceProvider => new SetDiscordSecretUseCase(
                    serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                    () => DateTimeOffset.UtcNow));
                services.AddSingleton(serviceProvider => new EnqueueDiscordDeliveryUseCase(
                    serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                    () => DateTimeOffset.UtcNow,
                    () => "discord-delivery-" + Guid.NewGuid().ToString("N")));
                services.AddSingleton(serviceProvider => new RetryDiscordDeliveryUseCase(
                    serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                    () => DateTimeOffset.UtcNow));
                services.AddSingleton(serviceProvider => new CancelDiscordDeliveryUseCase(
                    serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                    () => DateTimeOffset.UtcNow));
                services.AddSingleton<DiscordApiClient>();
                services.AddSingleton<IDiscordApiClient>(serviceProvider =>
                    serviceProvider.GetRequiredService<DiscordApiClient>());
                services.AddSingleton(serviceProvider => new DiscordDeliveryWorker(
                    serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                    serviceProvider.GetRequiredService<IDiscordApiClient>(),
                    () => DateTimeOffset.UtcNow,
                    TimeSpan.FromSeconds(1),
                    log));
                services.AddSingleton<DiscordInboundCommandDispatcher>();
                services.AddSingleton<IDiscordInboundCommandDispatcher>(serviceProvider =>
                    serviceProvider.GetRequiredService<DiscordInboundCommandDispatcher>());
                services.AddSingleton<DiscordInteractionFollowupClient>();
                services.AddSingleton<IDiscordInteractionResponseSender>(serviceProvider =>
                    serviceProvider.GetRequiredService<DiscordInteractionFollowupClient>());
                services.AddSingleton(serviceProvider => new BridgeDiscordMessageToGameUseCase(
                    serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                    serviceProvider.GetRequiredService<IChatMessageSender>(),
                    () => DateTimeOffset.UtcNow,
                    () => "discord-bridge-" + Guid.NewGuid().ToString("N")));
                services.AddSingleton(serviceProvider => new BridgeGameChatToDiscordUseCase(
                    serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                    () => DateTimeOffset.UtcNow,
                    () => "discord-bridge-" + Guid.NewGuid().ToString("N"),
                    () => "discord-delivery-" + Guid.NewGuid().ToString("N"),
                    "public"));
                services.AddSingleton(serviceProvider => new HandleDiscordInteractionUseCase(
                    serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                    serviceProvider.GetRequiredService<IDiscordInboundCommandDispatcher>(),
                    () => DateTimeOffset.UtcNow));
                services.AddSingleton(serviceProvider => new AcceptDiscordInteractionUseCase(
                    serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                    serviceProvider.GetRequiredService<IDiscordInteractionPersistenceStore>(),
                    () => DateTimeOffset.UtcNow));
                services.AddSingleton(serviceProvider => new ProcessDiscordInteractionUseCase(
                    serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                    serviceProvider.GetRequiredService<IDiscordInteractionPersistenceStore>(),
                    serviceProvider.GetRequiredService<IDiscordInboundCommandDispatcher>(),
                    () => DateTimeOffset.UtcNow,
                    serviceProvider.GetRequiredService<IDiscordInteractionResponseSender>()));
                services.AddSingleton<DiscordInboundRuntime>();
                services.AddSingleton<IDiscordInboundTransportSink>(serviceProvider =>
                    serviceProvider.GetRequiredService<DiscordInboundRuntime>());
                services.AddSingleton<IDiscordDeferredInteractionSink>(serviceProvider =>
                    serviceProvider.GetRequiredService<DiscordInboundRuntime>());
                services.AddSingleton<AutomationFieldCatalog>();
                services.AddSingleton<FeatureModuleAutomationDependencyCatalog>();
                services.AddSingleton<IAutomationDependencyCatalog>(serviceProvider =>
                    serviceProvider.GetRequiredService<FeatureModuleAutomationDependencyCatalog>());
                services.AddSingleton<StableAutomationTargetResolver>();
                services.AddSingleton<IAutomationTargetResolver>(serviceProvider =>
                    serviceProvider.GetRequiredService<StableAutomationTargetResolver>());
                services.AddSingleton<AutomationRuleValidator>();
                services.AddSingleton(_ => new AutomationConditionEvaluator(
                    options.PlayerEvidence.TimeZone));
                services.AddSingleton<AutomationRuleUseCases>();
                services.AddSingleton<DryRunAutomationRuleUseCase>();
                services.AddSingleton<SqliteEconomyLedgerStore>();
                services.AddSingleton<IEconomyLedgerStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteEconomyLedgerStore>());
                services.AddSingleton<IEconomyAccountAdministrationStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteEconomyLedgerStore>());
                services.AddSingleton<SqliteRewardStore>();
                services.AddSingleton<IRewardStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteRewardStore>());
                services.AddSingleton<IRewardDeliveryJournal>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteRewardStore>());
                services.AddSingleton<SqliteCommerceStore>();
                services.AddSingleton<ICommerceStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteCommerceStore>());
                services.AddSingleton<IShopCatalogQueryStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteCommerceStore>());
                services.AddSingleton<IDailyRewardClaimStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteCommerceStore>());
                services.AddSingleton<IDailyRewardPolicyStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteCommerceStore>());
                services.AddSingleton<SqliteCommunityStore>();
                services.AddSingleton<ICommunityStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteCommunityStore>());
                services.AddSingleton<ICommunityGameCommandConfigurationStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteCommunityStore>());
                services.AddSingleton<ITeleportFriendRequestStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteCommunityStore>());
                services.AddSingleton<SqliteChargedTeleportOperationStore>();
                services.AddSingleton<IChargedTeleportOperationStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteChargedTeleportOperationStore>());
                services.AddSingleton<SqliteVoteStore>();
                services.AddSingleton<IVoteStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteVoteStore>());
                services.AddSingleton<IExpiringVoteRoundReader>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteVoteStore>());
                services.AddSingleton<SqliteGeoIpAccessPolicyStore>();
                services.AddSingleton<IGeoIpAccessPolicyStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteGeoIpAccessPolicyStore>());
                services.AddSingleton(serviceProvider =>
                    new LocalMmdbGeoIpProvider(options.GeoIpDatabasePath));
                services.AddSingleton<MaxMindWebServiceGeoIpProvider>();
                services.AddSingleton<IGeoIpProvider>(serviceProvider =>
                    serviceProvider.GetRequiredService<LocalMmdbGeoIpProvider>());
                services.AddSingleton<IGeoIpProvider>(serviceProvider =>
                    serviceProvider.GetRequiredService<MaxMindWebServiceGeoIpProvider>());
                services.AddSingleton(serviceProvider => new GeoIpRefreshWorker(
                    serviceProvider.GetRequiredService<IGeoIpAccessPolicyStore>(),
                    serviceProvider.GetServices<IGeoIpProvider>()));
                services.AddSingleton<IGeoIpRefreshQueue>(serviceProvider =>
                    serviceProvider.GetRequiredService<GeoIpRefreshWorker>());
                services.AddSingleton<IGeoIpRefreshDiagnostics>(serviceProvider =>
                    serviceProvider.GetRequiredService<GeoIpRefreshWorker>());
                services.AddSingleton<GeoIpPolicyEvaluator>();
                services.AddSingleton<EvaluateGeoIpJoinUseCase>();
                services.AddSingleton<GetGeoIpDiagnosticsUseCase>();
                services.AddSingleton<SevenDaysGeoIpJoinPolicyRuntime>();
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
                services.AddSingleton<SqliteAuthenticationStore>();
                services.AddSingleton<IPanelCredentialStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteAuthenticationStore>());
                services.AddSingleton<IPanelAccessTokenStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteAuthenticationStore>());
                services.AddSingleton<IPanelApiKeyStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteAuthenticationStore>());
                services.AddSingleton<IPanelUserAdministrationStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteAuthenticationStore>());
                services.AddSingleton<SqliteRecentActivityStore>(serviceProvider =>
                    new SqliteRecentActivityStore(
                        serviceProvider.GetRequiredService<SqliteConnectionFactory>(),
                        retentionLimit: 256));
                services.AddSingleton<IRecentActivityQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteRecentActivityStore>());
                services.AddSingleton<IRecentActivityWriter>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteRecentActivityStore>());
                services.AddSingleton(ServerConfigurationFieldCatalog.Create());
                services.AddSingleton<IServerConfigurationStore>(_ =>
                    new LocalServerConfigurationStore(options.ServerConfigurationPath));
                services.AddSingleton<GetServerConfigurationUseCase>();
                services.AddSingleton<UpdateServerConfigurationUseCase>();
                services.AddSingleton<SevenDaysPlayerAccessControl>();
                services.AddSingleton<IPlayerAccessControl>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysPlayerAccessControl>());
                services.AddSingleton<AccessListUseCases>();
                services.AddSingleton<SevenDaysGamePermissionControl>();
                services.AddSingleton<IGamePermissionControl>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysGamePermissionControl>());
                services.AddSingleton<GamePermissionUseCases>();
                services.AddSingleton<LocalModCatalog>(_ => new LocalModCatalog(
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")),
                    new[] { Path.GetFileName(AppContext.BaseDirectory.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) }));
                services.AddSingleton<IModCatalog>(serviceProvider =>
                    serviceProvider.GetRequiredService<LocalModCatalog>());
                services.AddSingleton<SevenDaysLoadedModQuery>();
                services.AddSingleton<ILoadedModQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysLoadedModQuery>());
                services.AddSingleton<ListModsUseCase>();
                services.AddSingleton<SetModStateUseCase>();
                OwinStartup.RegisterAuthenticationServices(services, log);
                services.AddSingleton(serviceProvider =>
                    new SevenDaysRecentActivityRecorder(
                        serviceProvider.GetRequiredService<IRecentActivityWriter>(),
                        log));
                services.AddSingleton<WindowsHostPlatformAdapter>();
                services.AddSingleton<LinuxHostPlatformAdapter>();
                services.AddSingleton<IHostPlatformAdapter>(serviceProvider =>
                    Environment.OSVersion.Platform == PlatformID.Win32NT
                        ? (IHostPlatformAdapter)serviceProvider.GetRequiredService<WindowsHostPlatformAdapter>()
                        : serviceProvider.GetRequiredService<LinuxHostPlatformAdapter>());
                services.AddSingleton<HostCpuSampler>();
                services.AddSingleton<HostMemorySampler>();
                services.AddSingleton(_ => new HostStorageSampler(dataDirectory));
                services.AddSingleton(_ => new DeviceIdentityProvider("LSTY.SevenDPanel"));
                services.AddSingleton(serviceProvider => new PublicNetworkAddressResolver(
                    serviceProvider.GetRequiredService<PanelOverviewOptions>()));
                services.AddSingleton(serviceProvider => new HostOverviewQuery(
                    serviceProvider.GetRequiredService<IHostPlatformAdapter>(),
                    serviceProvider.GetRequiredService<HostCpuSampler>(),
                    serviceProvider.GetRequiredService<HostMemorySampler>(),
                    serviceProvider.GetRequiredService<HostStorageSampler>(),
                    serviceProvider.GetRequiredService<DeviceIdentityProvider>(),
                    serviceProvider.GetRequiredService<PublicNetworkAddressResolver>()));
                services.AddSingleton<IHostOverviewQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<HostOverviewQuery>());
                services.AddSingleton<SevenDaysGameOverviewQuery>();
                services.AddSingleton<IGameOverviewQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysGameOverviewQuery>());
                services.AddSingleton<IRestartPolicyQuery, UnavailableRestartPolicyQuery>();
                services.AddSingleton<GetOverviewUseCase>();
                services.AddSingleton<SqliteServerOperationAuditTrail>();
                services.AddSingleton<IServerOperationAuditTrail>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteServerOperationAuditTrail>());
                services.AddSingleton<RestartScriptLauncher>();
                services.AddSingleton<IRestartScriptLauncher>(serviceProvider =>
                    serviceProvider.GetRequiredService<RestartScriptLauncher>());
                services.AddSingleton<SevenDaysShutdownServerGateway>();
                services.AddSingleton<IShutdownServerGateway>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysShutdownServerGateway>());
                services.AddSingleton<RestartServerUseCase>();
                services.AddSingleton<ShutdownServerUseCase>();
                services.AddSingleton(_ => new ConsoleLogService(log));
                services.AddSingleton<IRecentConsoleLogQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<ConsoleLogService>().LiveWindow);
                services.AddSingleton<IRecentChatMessageQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<ConsoleLogService>().LiveWindow);
                services.AddSingleton<IServerEventStream>(serviceProvider =>
                    serviceProvider.GetRequiredService<ConsoleLogService>().Stream);
                services.AddSingleton<SqliteChatStore>();
                services.AddSingleton<IChatHistoryStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteChatStore>());
                services.AddSingleton<IChatSettingsStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteChatStore>());
                services.AddSingleton<SqliteColoredChatStore>();
                services.AddSingleton<IColoredChatStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteColoredChatStore>());
                services.AddSingleton<SqliteChatOperationAuditTrail>();
                services.AddSingleton<IChatOperationAuditTrail>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteChatOperationAuditTrail>());
                services.AddSingleton<SqliteGameChatCommandAuditTrail>();
                services.AddSingleton<IGameChatCommandAuditTrail>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteGameChatCommandAuditTrail>());
                services.AddSingleton<SqliteChatMuteStore>();
                services.AddSingleton<IChatMuteStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteChatMuteStore>());
                services.AddSingleton<IChatMuteExpirationStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteChatMuteStore>());
                services.AddSingleton<ChatRuntimeState>();
                services.AddSingleton<IChatRuntimeConfiguration>(serviceProvider =>
                    serviceProvider.GetRequiredService<ChatRuntimeState>());
                services.AddSingleton<IChatMuteRuntimeConfiguration>(serviceProvider =>
                    serviceProvider.GetRequiredService<ChatRuntimeState>());
                services.AddSingleton(serviceProvider => new ChatMuteUseCases(
                    serviceProvider.GetRequiredService<IChatMuteStore>(),
                    serviceProvider.GetRequiredService<IChatMuteRuntimeConfiguration>(),
                    () => DateTimeOffset.UtcNow));
                services.AddSingleton(serviceProvider => new ChatMuteExpiryService(
                    serviceProvider.GetRequiredService<IChatMuteExpirationStore>(),
                    serviceProvider.GetRequiredService<IChatMuteRuntimeConfiguration>(),
                    () => DateTimeOffset.UtcNow,
                    log));
                services.AddSingleton<ChatHistoryWriteService>();
                services.AddSingleton<ColoredChatRenderer>();
                services.AddSingleton(serviceProvider => new HelpGameChatCommandHandler(
                    () => true,
                    () => serviceProvider.GetRequiredService<GameChatCommandCatalog>().Commands));
                services.AddSingleton(serviceProvider => new GameChatCommandCatalog(
                    CreateGameChatCommandHandlers(serviceProvider)));
                services.AddSingleton(serviceProvider => new GameChatCommandRegistrationService(
                    serviceProvider.GetRequiredService<GameChatCommandCatalog>(),
                    () => CreateGameChatCommandHandlers(serviceProvider)));
                services.AddSingleton<SevenDaysGameChatCommandReplySender>();
                services.AddSingleton(serviceProvider => new SevenDaysChatMessageCoordinator(
                    serviceProvider.GetRequiredService<ChatRuntimeState>(),
                    serviceProvider.GetRequiredService<ColoredChatRenderer>(),
                    serviceProvider.GetRequiredService<ConsoleLogService>(),
                    serviceProvider.GetRequiredService<ChatHistoryWriteService>(),
                    log,
                    serviceProvider.GetRequiredService<GameChatCommandCatalog>(),
                    serviceProvider.GetRequiredService<SevenDaysGameChatCommandReplySender>(),
                    serviceProvider.GetRequiredService<IAutomationTriggerIngress>(),
                    serviceProvider.GetRequiredService<BridgeGameChatToDiscordUseCase>(),
                    serviceProvider.GetRequiredService<IGameChatCommandAuditTrail>()));
                services.AddSingleton<SevenDaysChatMessageSender>();
                services.AddSingleton<IChatMessageSender>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysChatMessageSender>());
                services.AddSingleton<GetChatHistoryUseCase>();
                services.AddSingleton<GetChatSettingsUseCase>();
                services.AddSingleton<GetColoredChatSettingsUseCase>();
                services.AddSingleton<GetColoredChatProfilesUseCase>();
                services.AddSingleton<SendGlobalChatMessageUseCase>();
                services.AddSingleton<SendPrivateChatMessageUseCase>();
                services.AddSingleton<SaveChatSettingsUseCase>();
                services.AddSingleton<ResetChatSettingsUseCase>();
                services.AddSingleton<SaveColoredChatSettingsUseCase>();
                services.AddSingleton<ResetColoredChatSettingsUseCase>();
                services.AddSingleton<CreateColoredChatProfileUseCase>();
                services.AddSingleton<UpdateColoredChatProfileUseCase>();
                services.AddSingleton<DeleteColoredChatProfileUseCase>();
                services.AddSingleton<SqliteConsoleCommandAuditStore>();
                services.AddSingleton<IConsoleCommandAuditStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteConsoleCommandAuditStore>());
                services.AddSingleton(serviceProvider => new ConsoleCommandAuditService(
                    serviceProvider.GetRequiredService<IConsoleCommandAuditStore>(),
                    log));
                services.AddSingleton<SevenDaysConsoleCommandService>();
                services.AddSingleton(_ => new SevenDaysConsoleCommandCatalogQuery(log));
                services.AddSingleton<IConsoleCommandCatalogQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysConsoleCommandCatalogQuery>());
                services.AddSingleton<IConsoleCommandGateway>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysConsoleCommandService>());
                services.AddSingleton<ExecuteConsoleCommandUseCase>();
                services.AddSingleton<SqliteGameEventStore>();
                services.AddSingleton<IGameEventStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteGameEventStore>());
                services.AddSingleton<GameEventWriteService>();
                services.AddSingleton<SevenDaysGameEventAdapter>();
                services.AddSingleton<SqliteUnifiedAuditQuery>();
                services.AddSingleton<IUnifiedAuditQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteUnifiedAuditQuery>());
                services.AddSingleton<SqlitePlayerHistoryStore>();
                services.AddSingleton<IPlayerHistoryStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqlitePlayerHistoryStore>());
                services.AddSingleton<IPlayerMapSpatialQueryStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqlitePlayerHistoryStore>());
                services.AddSingleton<PlayerHistoryWriteService>();
                services.AddSingleton<GetHistoricalPlayersUseCase>();
                services.AddSingleton<GetHistoricalPlayerUseCase>();
                services.AddSingleton<GetPlayerHistorySnapshotsUseCase>();
                services.AddSingleton<GetPlayerTrackUseCase>();
                services.AddSingleton<SevenDaysMapMetadataProjection>();
                services.AddSingleton<SevenDaysMapGameTimeProjection>();
                services.AddSingleton<SevenDaysMapLayerProjection>();
                services.AddSingleton<SevenDaysTransientEntityProjection>();
                services.AddSingleton<SevenDaysWorldSnapshotProjection>();
                services.AddSingleton<IWorldSnapshotProjection>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysWorldSnapshotProjection>());
                services.AddSingleton<SevenDaysWorldToolCatalog>();
                services.AddSingleton<IWorldToolCatalog>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysWorldToolCatalog>());
                services.AddSingleton<IMapMetadataQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysMapMetadataProjection>());
                services.AddSingleton<IMapGameTimeQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysMapGameTimeProjection>());
                services.AddSingleton<IMapLayerProjection>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysMapLayerProjection>());
                services.AddSingleton<ITransientEntityMapProjection>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysTransientEntityProjection>());
                services.AddSingleton<GetMapMetadataUseCase>();
                services.AddSingleton<GetMapGameTimeUseCase>();
                services.AddSingleton<GetMapLayerUseCase>();
                services.AddSingleton<SearchPlayersInAreaUseCase>();
                services.AddSingleton<GetTransientEntityMapLayerUseCase>();
                services.AddSingleton<QueryWorldUseCase>();
                services.AddSingleton<QueryWorldToolCatalogUseCase>();
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
                services.AddSingleton(serviceProvider => new SevenDaysOnlinePlayerProjection(
                    serviceProvider.GetRequiredService<PlayerHistoryWriteService>(),
                    log));
                services.AddSingleton<IOnlinePlayerQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysOnlinePlayerProjection>());
                services.AddSingleton<GetOnlinePlayersUseCase>();
                services.AddSingleton<GetHistoricalPlayerLastLocationsUseCase>();
                services.AddSingleton<SqlitePlayerActionAuditTrail>();
                services.AddSingleton<IPlayerActionAuditTrail>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqlitePlayerActionAuditTrail>());
                services.AddSingleton<SevenDaysPlayerActions>();
                services.AddSingleton<IPlayerActions>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysPlayerActions>());
                services.AddSingleton<KickPlayerUseCase>();
                services.AddScoped<ServerEventSseSession>();
                services.AddSingleton(serviceProvider => new ModHost(
                    () =>
                    {
                        var authenticationStore = serviceProvider
                            .GetRequiredService<SqliteAuthenticationStore>();
                        var playerActionAuditTrail = serviceProvider
                            .GetRequiredService<SqlitePlayerActionAuditTrail>();
                        playerActionAuditTrail.MarkPendingUnknown(DateTimeOffset.UtcNow);
                        if (options.Authentication.Enabled)
                        {
                            authenticationStore.EnsureBootstrapOwner(
                                options.Authentication.Username,
                                options.Authentication.Password);
                        }

                        return new OwinWebHost(
                            options.Url,
                            app => OwinStartup.Configure(
                                app,
                                serviceProvider,
                                assetRoot,
                                log));
                    },
                    log));
                services.AddSingleton(serviceProvider => new ConsoleLogRuntime(
                    serviceProvider.GetRequiredService<ConsoleLogService>(),
                    serviceProvider.GetRequiredService<ModHost>()));
                services.AddSingleton(serviceProvider => new ConsoleCommandRuntime(
                    serviceProvider.GetRequiredService<ConsoleCommandAuditService>(),
                    serviceProvider.GetRequiredService<SevenDaysConsoleCommandService>(),
                    serviceProvider.GetRequiredService<ConsoleLogRuntime>()));
                services.AddSingleton(serviceProvider => new OnlinePlayerProjectionRuntime(
                    serviceProvider.GetRequiredService<SevenDaysOnlinePlayerProjection>(),
                    serviceProvider.GetRequiredService<ConsoleCommandRuntime>()));
                services.AddSingleton(serviceProvider => new PlayerHistoryRuntime(
                    serviceProvider.GetRequiredService<PlayerHistoryWriteService>(),
                    serviceProvider.GetRequiredService<OnlinePlayerProjectionRuntime>()));
                services.AddSingleton(serviceProvider => new SevenDaysRecentActivityRuntime(
                    serviceProvider.GetRequiredService<SevenDaysRecentActivityRecorder>(),
                    serviceProvider.GetRequiredService<PlayerHistoryRuntime>()));
                services.AddSingleton(serviceProvider => new SevenDaysMapProjectionRuntime(
                    serviceProvider.GetRequiredService<SevenDaysMapMetadataProjection>(),
                    serviceProvider.GetRequiredService<SevenDaysMapGameTimeProjection>(),
                    serviceProvider.GetRequiredService<SevenDaysMapLayerProjection>(),
                    serviceProvider.GetRequiredService<SevenDaysTransientEntityProjection>(),
                    serviceProvider.GetRequiredService<SevenDaysWorldSnapshotProjection>(),
                    serviceProvider.GetRequiredService<SevenDaysWorldToolCatalog>(),
                    serviceProvider.GetRequiredService<SevenDaysRecentActivityRuntime>()));
                services.AddSingleton(serviceProvider => new SevenDaysChatRuntime(
                    serviceProvider.GetRequiredService<ChatRuntimeState>(),
                    serviceProvider.GetRequiredService<ChatHistoryWriteService>(),
                    serviceProvider.GetRequiredService<SevenDaysChatMessageCoordinator>(),
                    serviceProvider.GetRequiredService<SevenDaysMapProjectionRuntime>(),
                    serviceProvider.GetRequiredService<ChatMuteExpiryService>()));
                services.AddSingleton(serviceProvider => new SevenDaysGameEventRuntime(
                    serviceProvider.GetRequiredService<GameEventWriteService>(),
                    serviceProvider.GetRequiredService<SevenDaysGameEventAdapter>(),
                    serviceProvider.GetRequiredService<SevenDaysChatRuntime>()));
                services.AddSingleton<SevenDaysGameResourceCatalog>();
                services.AddSingleton<IGameResourceCatalog>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysGameResourceCatalog>());
                services.AddSingleton<QueryGameResourcesUseCase>();
                services.AddSingleton<GetGameResourceIconUseCase>();

                services.AddSingleton<SqlitePlayerEvidenceStore>();
                services.AddSingleton<IPlayerEvidenceStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqlitePlayerEvidenceStore>());
                services.AddSingleton<SqlitePlayerActionOperationQuery>();
                services.AddSingleton<IPlayerActionOperationQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqlitePlayerActionOperationQuery>());
                services.AddSingleton<PlayerInventoryDiffService>();
                services.AddSingleton(serviceProvider => new GetPlayerProfileUseCase(
                    serviceProvider.GetRequiredService<IPlayerHistoryStore>(),
                    serviceProvider.GetRequiredService<IPlayerEvidenceStore>(),
                    options.PlayerEvidence.TimeZone));
                services.AddSingleton<GetInventorySnapshotsUseCase>();
                services.AddSingleton<GetInventoryDiffsUseCase>();
                services.AddSingleton<GetPlayerSkillsUseCase>();
                services.AddSingleton<SevenDaysPlayerEvidenceSnapshotReader>();
                services.AddSingleton<PlayerEvidenceWriteService>();
                services.AddSingleton<SevenDaysPlayerEvidenceProjection>();

                services.AddSingleton<SqliteGrantItemOperationStore>();
                services.AddSingleton<SqliteGrantItemOperationStoreAdapter>();
                services.AddSingleton<IGrantItemOperationStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteGrantItemOperationStoreAdapter>());
                services.AddSingleton<SqliteRemoveItemOperationStore>();
                services.AddSingleton<SqliteRemoveItemOperationStoreAdapter>();
                services.AddSingleton<IRemoveItemOperationStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteRemoveItemOperationStoreAdapter>());
                services.AddSingleton<SqliteResetSkillsOperationStore>();
                services.AddSingleton<SqliteResetSkillsOperationStoreAdapter>();
                services.AddSingleton<IResetSkillsOperationStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteResetSkillsOperationStoreAdapter>());
                services.AddSingleton<SqliteClearInventoryOperationStore>();
                services.AddSingleton<SqliteClearInventoryOperationStoreAdapter>();
                services.AddSingleton<IClearInventoryOperationStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteClearInventoryOperationStoreAdapter>());
                services.AddSingleton<SqliteResetPlayerDataOperationStore>();
                services.AddSingleton<SqliteResetPlayerDataOperationStoreAdapter>();
                services.AddSingleton<IResetPlayerDataOperationStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteResetPlayerDataOperationStoreAdapter>());

                services.AddSingleton<SevenDaysGrantItemEvidenceCapture>();
                services.AddSingleton(serviceProvider => new SevenDaysGrantItemGateway(
                    serviceProvider.GetRequiredService<IGameResourceCatalog>(),
                    serviceProvider.GetRequiredService<SevenDaysGrantItemEvidenceCapture>()
                        .FindOnlineObservedAtUtc,
                    serviceProvider.GetRequiredService<SevenDaysGrantItemEvidenceCapture>()
                        .CaptureAsync));
                services.AddSingleton<IGrantItemGateway>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysGrantItemGateway>());
                services.AddSingleton<SevenDaysRemoveItemGateway>();
                services.AddSingleton<IRemoveItemGateway>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysRemoveItemGateway>());
                services.AddSingleton<SevenDaysResetSkillsGateway>();
                services.AddSingleton<IResetSkillsGateway>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysResetSkillsGateway>());
                services.AddSingleton<SevenDaysClearInventoryGateway>();
                services.AddSingleton<IClearInventoryGateway>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysClearInventoryGateway>());
                services.AddSingleton<SevenDaysResetPlayerDataGateway>();
                services.AddSingleton<IResetPlayerDataGateway>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysResetPlayerDataGateway>());

                services.AddSingleton(serviceProvider => new GrantItemUseCase(
                    serviceProvider.GetRequiredService<IGameResourceCatalog>(),
                    serviceProvider.GetRequiredService<IGrantItemOperationStore>(),
                    serviceProvider.GetRequiredService<IGrantItemGateway>(),
                    serviceProvider.GetRequiredService<IPlayerEvidenceStore>(),
                    options.PlayerEvidence.ServerId));
                services.AddSingleton(serviceProvider => new RemoveItemUseCase(
                    serviceProvider.GetRequiredService<IGameResourceCatalog>(),
                    serviceProvider.GetRequiredService<IRemoveItemOperationStore>(),
                    serviceProvider.GetRequiredService<IRemoveItemGateway>(),
                    serviceProvider.GetRequiredService<IPlayerEvidenceStore>(),
                    options.PlayerEvidence.ServerId));
                services.AddSingleton<ResetSkillsUseCase>();
                services.AddSingleton<ClearInventoryUseCase>();
                services.AddSingleton<ResetPlayerDataUseCase>();
                services.AddSingleton<ThirdWaveRewardDeliveryAdapter>();
                services.AddSingleton<IRewardDeliveryPort>(serviceProvider =>
                    serviceProvider.GetRequiredService<ThirdWaveRewardDeliveryAdapter>());
                services.AddSingleton<SaveRewardPackageUseCase>();
                services.AddSingleton<GrantRewardUseCase>();
                services.AddSingleton<SaveDailyRewardPolicyUseCase>();
                services.AddSingleton<ClaimDailyRewardUseCase>();
                services.AddSingleton<PendingRewardReconciliationUseCase>();
                services.AddSingleton<ConfirmRewardGrantUseCase>();
                services.AddSingleton<RefundRewardGrantUseCase>();
                services.AddSingleton<CompensateRewardGrantUseCase>();
                services.AddSingleton<OpenPlayerAccountUseCase>();
                services.AddSingleton<TransferBalanceUseCase>();
                services.AddSingleton<QueryEconomyAccountsUseCase>();
                services.AddSingleton<QueryEconomyTransactionsUseCase>();
                services.AddSingleton<SetAccountFrozenUseCase>();
                services.AddSingleton<AdjustPlayerBalanceUseCase>();
                services.AddSingleton<SaveShopProductUseCase>();
                services.AddSingleton<BrowseShopUseCase>();
                services.AddSingleton<PurchaseProductUseCase>();
                services.AddSingleton<CreateRedeemCodeUseCase>();
                services.AddSingleton<RedeemCodeUseCase>();
                services.AddSingleton<SaveAchievementDefinitionUseCase>();
                services.AddSingleton<SaveOnlineRewardRuleUseCase>();
                services.AddSingleton<ObserveAchievementUseCase>();
                services.AddSingleton<EvaluateOnlineRewardsUseCase>();
                services.AddSingleton<ManualOnlineRewardGrantUseCase>();
                services.AddSingleton(serviceProvider => new AutomationActionDispatcher(
                    broadcastMessages: serviceProvider.GetRequiredService<SendGlobalChatMessageUseCase>(),
                    privateMessages: serviceProvider.GetRequiredService<SendPrivateChatMessageUseCase>(),
                    announcements: serviceProvider.GetRequiredService<AnnouncementService>(),
                    grantItems: serviceProvider.GetRequiredService<GrantItemUseCase>(),
                    grantRewards: serviceProvider.GetRequiredService<GrantRewardUseCase>(),
                    economy: serviceProvider.GetRequiredService<AdjustPlayerBalanceUseCase>(),
                    kickPlayers: serviceProvider.GetRequiredService<KickPlayerUseCase>(),
                    mutePlayers: serviceProvider.GetRequiredService<ChatMuteUseCases>(),
                    resetSkills: serviceProvider.GetRequiredService<ResetSkillsUseCase>(),
                    discordOutbox: serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                    onlinePlayers: serviceProvider.GetRequiredService<IOnlinePlayerQuery>(),
                    resources: serviceProvider.GetRequiredService<IGameResourceCatalog>(),
                    worldId: global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld),
                    utcNow: () => DateTimeOffset.UtcNow));
                services.AddSingleton<IAutomationActionDispatcher>(serviceProvider =>
                    serviceProvider.GetRequiredService<AutomationActionDispatcher>());
                services.AddSingleton<AutomationExecutionEngine>();
                services.AddSingleton<AutomationTriggerRuntime>();
                services.AddSingleton<IAutomationTriggerIngress>(serviceProvider =>
                    serviceProvider.GetRequiredService<AutomationTriggerRuntime>());
                services.AddSingleton<SevenDaysCommunityGameGateway>();
                services.AddSingleton<ICommunityGameGateway>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysCommunityGameGateway>());
                services.AddSingleton(serviceProvider => new HomeUseCases(
                    serviceProvider.GetRequiredService<ICommunityStore>(),
                    () => DateTimeOffset.UtcNow,
                    serviceProvider.GetRequiredService<IEconomyLedgerStore>()));
                services.AddSingleton<CityUseCases>();
                services.AddSingleton<FriendUseCases>();
                services.AddSingleton(serviceProvider => new TeleportUseCases(
                    serviceProvider.GetRequiredService<ICommunityStore>(),
                    serviceProvider.GetRequiredService<IEconomyLedgerStore>(),
                    serviceProvider.GetRequiredService<ICommunityGameGateway>(),
                    () => DateTimeOffset.UtcNow,
                    serviceProvider.GetRequiredService<IChargedTeleportOperationStore>()));
                services.AddSingleton(serviceProvider => new TeleportFriendRequestUseCases(
                    serviceProvider.GetRequiredService<ITeleportFriendRequestStore>(),
                    serviceProvider.GetRequiredService<TeleportUseCases>(),
                    serviceProvider.GetRequiredService<ICommunityPlayerCommandSnapshotProvider>(),
                    TimeSpan.FromSeconds(30),
                    () => DateTimeOffset.UtcNow,
                    () => "teleport-friend-request-" + Guid.NewGuid().ToString("N")));
                services.AddSingleton<StartVoteUseCase>();
                services.AddSingleton<CastVoteUseCase>();
                services.AddSingleton<SettleVoteUseCase>();
                services.AddSingleton<SevenDaysCommunityPlayerSnapshotProvider>();
                services.AddSingleton<ICommunityPlayerCommandSnapshotProvider>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysCommunityPlayerSnapshotProvider>());
                services.AddSingleton<ICommunityVoteCommandSnapshotProvider>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysCommunityPlayerSnapshotProvider>());
                services.AddSingleton(serviceProvider =>
                {
                    var moduleStates = serviceProvider.GetRequiredService<IFeatureModuleStateStore>();
                    var playerSnapshots = serviceProvider.GetRequiredService<SevenDaysCommunityPlayerSnapshotProvider>();
                    var startVote = serviceProvider.GetRequiredService<StartVoteUseCase>();
                    var castVote = serviceProvider.GetRequiredService<CastVoteUseCase>();
                    Func<bool> votingEnabled = () =>
                        moduleStates.Get(FeatureModuleId.TeleportAndVoting).IsEnabled;
                    var voteKick = new VoteGameCommandConsumer(
                        VoteKind.Kick,
                        startVote,
                        castVote,
                        playerSnapshots,
                        votingEnabled,
                        () => DateTimeOffset.UtcNow);
                    var voteRestart = new VoteGameCommandConsumer(
                        VoteKind.Restart,
                        startVote,
                        castVote,
                        playerSnapshots,
                        votingEnabled,
                        () => DateTimeOffset.UtcNow);
                    var consumers = CommunityGameCommandConsumerSet.Create(
                        serviceProvider.GetRequiredService<OpenPlayerAccountUseCase>(),
                        serviceProvider.GetRequiredService<TransferBalanceUseCase>(),
                        serviceProvider.GetRequiredService<QueryEconomyAccountsUseCase>(),
                        serviceProvider.GetRequiredService<BrowseShopUseCase>(),
                        serviceProvider.GetRequiredService<PurchaseProductUseCase>(),
                        serviceProvider.GetRequiredService<RedeemCodeUseCase>(),
                        serviceProvider.GetRequiredService<ClaimDailyRewardUseCase>(),
                        serviceProvider.GetRequiredService<HomeUseCases>(),
                        serviceProvider.GetRequiredService<CityUseCases>(),
                        serviceProvider.GetRequiredService<TeleportUseCases>(),
                        serviceProvider.GetRequiredService<TeleportFriendRequestUseCases>(),
                        playerSnapshots,
                        voteKick,
                        voteRestart,
                        command => moduleStates.Get(ModuleForCommunityCommand(command)).IsEnabled,
                        () => DateTimeOffset.UtcNow);
                    return new CommunityGameCommandRouter(consumers);
                });
                services.AddSingleton(serviceProvider => new CommunityVoteActionAdapter(
                    serviceProvider.GetRequiredService<KickPlayerUseCase>(),
                    serviceProvider.GetRequiredService<IJobSubmissionStore>(),
                    serviceProvider.GetRequiredService<IJobStore>(),
                    () => DateTimeOffset.UtcNow));
                services.AddSingleton<ICommunityVoteActionPort>(serviceProvider =>
                    serviceProvider.GetRequiredService<CommunityVoteActionAdapter>());
                services.AddSingleton<DispatchVoteActionUseCase>();
                services.AddSingleton<RecoverQueuedVoteActionsUseCase>();
                services.AddSingleton(serviceProvider => new CommunityVoteRuntime(
                    serviceProvider.GetRequiredService<IExpiringVoteRoundReader>(),
                    serviceProvider.GetRequiredService<SettleVoteUseCase>(),
                    serviceProvider.GetRequiredService<DispatchVoteActionUseCase>(),
                    serviceProvider.GetRequiredService<RecoverQueuedVoteActionsUseCase>(),
                    () => DateTimeOffset.UtcNow,
                    serviceProvider.GetRequiredService<GeoIpRuntime>()));
                services.AddSingleton(serviceProvider => new PlayerActionRecoveryService(
                    serviceProvider.GetRequiredService<SqliteGrantItemOperationStoreAdapter>(),
                    serviceProvider.GetRequiredService<SqliteRemoveItemOperationStoreAdapter>(),
                    serviceProvider.GetRequiredService<SqliteResetSkillsOperationStoreAdapter>(),
                    serviceProvider.GetRequiredService<SqliteClearInventoryOperationStoreAdapter>(),
                    serviceProvider.GetRequiredService<SqliteResetPlayerDataOperationStoreAdapter>()));
                services.AddSingleton(serviceProvider => new PlayerActionRecoveryRuntime(
                    serviceProvider.GetRequiredService<PlayerActionRecoveryService>(),
                    serviceProvider.GetRequiredService<SevenDaysGameEventRuntime>()));
                services.AddSingleton(serviceProvider => new PlayerEvidenceRuntime(
                    serviceProvider.GetRequiredService<PlayerEvidenceWriteService>(),
                    serviceProvider.GetRequiredService<SevenDaysPlayerEvidenceProjection>(),
                    serviceProvider.GetRequiredService<PlayerActionRecoveryRuntime>()));
                services.AddSingleton(serviceProvider => new RewardEvidenceRuntime(
                    serviceProvider.GetRequiredService<PlayerHistoryWriteService>(),
                    serviceProvider.GetRequiredService<PlayerEvidenceWriteService>(),
                    serviceProvider.GetRequiredService<ObserveAchievementUseCase>(),
                    serviceProvider.GetRequiredService<EvaluateOnlineRewardsUseCase>(),
                    serviceProvider.GetRequiredService<PlayerEvidenceRuntime>(),
                    log));
                services.AddSingleton(serviceProvider => new GameResourceCatalogRuntime(
                    serviceProvider.GetRequiredService<SevenDaysGameResourceCatalog>(),
                    serviceProvider.GetRequiredService<RewardEvidenceRuntime>()));
                services.AddSingleton(serviceProvider => new JobsAndSchedulingRuntime(
                    serviceProvider.GetRequiredService<PendingRestoreStartupStep>(),
                    serviceProvider.GetRequiredService<BackgroundWorkerJobStore>(),
                    serviceProvider.GetRequiredService<BackgroundWorkConsumer>(),
                    serviceProvider.GetRequiredService<BackgroundScheduler>(),
                    serviceProvider.GetRequiredService<GameResourceCatalogRuntime>(),
                    TimeSpan.FromSeconds(30)));
                services.AddSingleton(serviceProvider => new AutomationRuntime(
                    serviceProvider.GetRequiredService<AutomationTriggerRuntime>(),
                    serviceProvider.GetRequiredService<JobsAndSchedulingRuntime>()));
                services.AddSingleton(serviceProvider => new DiscordRuntime(
                    serviceProvider.GetRequiredService<DiscordDeliveryWorker>(),
                    serviceProvider.GetRequiredService<DiscordInboundRuntime>(),
                    () => CreateDiscordGateway(serviceProvider, log),
                    serviceProvider.GetRequiredService<AutomationRuntime>(),
                    TimeSpan.FromSeconds(30)));
                services.AddSingleton(serviceProvider => new AutomationRecoveryRuntime(
                    serviceProvider.GetRequiredService<AutomationExecutionRecoveryService>(),
                    serviceProvider.GetRequiredService<GeoIpRuntime>()));
                services.AddSingleton(serviceProvider => new GeoIpRuntime(
                    serviceProvider.GetRequiredService<GeoIpRefreshWorker>(),
                    serviceProvider.GetRequiredService<SevenDaysGeoIpJoinPolicyRuntime>(),
                    serviceProvider.GetRequiredService<DiscordRuntime>()));
                services.AddSingleton(serviceProvider => new WorldOperationRuntime(
                    serviceProvider.GetRequiredService<IWorldOperationRecoveryStore>(),
                    serviceProvider.GetRequiredService<CommunityVoteRuntime>(),
                    () => DateTimeOffset.UtcNow));
                services.AddSingleton<IPanelRuntimeStatus>(serviceProvider =>
                    serviceProvider.GetRequiredService<ModHost>());
                services.AddSingleton<IModRuntime>(serviceProvider =>
                    serviceProvider.GetRequiredService<WorldOperationRuntime>());

                provider = services.BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
                provider.GetRequiredService<SqliteDatabaseBootstrapper>().Upgrade();
                var inner = provider.GetRequiredService<IModRuntime>();
                var runtime = new ServiceProviderRuntime(inner, provider);
                provider = null;
                return runtime;
            }
            finally
            {
                provider?.Dispose();
            }
        }

        private static IReadOnlyList<IGameChatCommandHandler> CreateGameChatCommandHandlers(
            IServiceProvider serviceProvider)
        {
            var handlers = new List<IGameChatCommandHandler>
            {
                serviceProvider.GetRequiredService<HelpGameChatCommandHandler>()
            };
            handlers.AddRange(CommunityGameChatCommandHandlerSet.Create(
                serviceProvider.GetRequiredService<CommunityGameCommandRouter>(),
                serviceProvider.GetRequiredService<ICommunityGameCommandConfigurationStore>()
                    .GetGameCommandConfiguration()));
            return handlers;
        }

        private static DiscordGatewayClient? CreateDiscordGateway(
            IServiceProvider serviceProvider,
            Action<string> log)
        {
            var store = serviceProvider.GetRequiredService<IDiscordIntegrationStore>();
            var settings = store.GetSettings();
            if (settings == null || !settings.IsEnabled ||
                settings.Mode != DiscordIntegrationMode.Bot ||
                !settings.BridgeDiscordToGame)
            {
                return null;
            }

            var token = store.GetSecret(DiscordSecretKeys.BotToken)?.SecretValue;
            if (string.IsNullOrWhiteSpace(token) ||
                string.IsNullOrWhiteSpace(settings.GuildId) ||
                string.IsNullOrWhiteSpace(settings.PublicChannelId))
            {
                log("Discord gateway is disabled because its bot configuration is incomplete.");
                return null;
            }

            return new DiscordGatewayClient(
                new DiscordGatewayOptions(
                    token!,
                    settings.GuildId!,
                    new[] { settings.PublicChannelId! }),
                serviceProvider.GetRequiredService<IDiscordInboundTransportSink>());
        }

        private static FeatureModuleId ModuleForCommunityCommand(
            CommunityGameCommandId command)
        {
            switch (command)
            {
                case CommunityGameCommandId.Balance:
                case CommunityGameCommandId.Pay:
                case CommunityGameCommandId.MoneyTop:
                case CommunityGameCommandId.Daily:
                case CommunityGameCommandId.Shop:
                case CommunityGameCommandId.Buy:
                case CommunityGameCommandId.Redeem:
                    return FeatureModuleId.EconomyAndRewards;
                case CommunityGameCommandId.Homes:
                case CommunityGameCommandId.SetHome:
                case CommunityGameCommandId.DeleteHome:
                case CommunityGameCommandId.Home:
                case CommunityGameCommandId.Cities:
                case CommunityGameCommandId.City:
                case CommunityGameCommandId.TeleportAsk:
                case CommunityGameCommandId.TeleportAccept:
                case CommunityGameCommandId.TeleportReject:
                case CommunityGameCommandId.Back:
                case CommunityGameCommandId.VoteKick:
                case CommunityGameCommandId.VoteRestart:
                    return FeatureModuleId.TeleportAndVoting;
                default:
                    throw new InvalidOperationException("community_command_has_no_runtime_module");
            }
        }

        private static ApprovedStorageRoots CreateApprovedStorageRoots(
            PanelHostOptions options,
            string dataDirectory)
        {
            var serverConfigurationRoot = Path.GetDirectoryName(
                options.ServerConfigurationPath);
            if (string.IsNullOrWhiteSpace(serverConfigurationRoot))
            {
                throw new InvalidOperationException(
                    "The approved server configuration root is unavailable.");
            }

            return new ApprovedStorageRoots(
                global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld),
                global::GameIO.GetSaveGameDir(),
                dataDirectory,
                serverConfigurationRoot!,
                "primary",
                Path.Combine(dataDirectory, "backups"),
                global::GamePrefs.GetString(global::EnumGamePrefs.GameVersion));
        }

        private sealed class UnavailableRestartPolicyQuery : IRestartPolicyQuery
        {
            public RestartPolicySummary Query() => RestartPolicySummary.Unavailable();
        }
    }

    internal sealed class FeatureModuleJobActivityQuery : IFeatureModuleActivityQuery
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public FeatureModuleJobActivityQuery(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));

        public bool HasActiveWork(FeatureModuleId moduleId)
        {
            string[] kinds;
            switch (moduleId)
            {
                case FeatureModuleId.WorldTools:
                    kinds = new[] { JobKind.WorldOperation.ToString() };
                    break;
                case FeatureModuleId.Backups:
                    kinds = new[]
                    {
                        JobKind.WorldBackup.ToString(),
                        JobKind.PanelDatabaseBackup.ToString(),
                        JobKind.ServerConfigurationBackup.ToString(),
                        JobKind.Restore.ToString()
                    };
                    break;
                case FeatureModuleId.AnnouncementsAndScheduling:
                    kinds = new[]
                    {
                        JobKind.ScheduledConsoleCommand.ToString(),
                        JobKind.ScheduledRestart.ToString(),
                        JobKind.ScheduledAnnouncement.ToString()
                    };
                    break;
                default:
                    return false;
            }

            using var connection = connectionFactory.Open();
            return connection.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM jobs
                  WHERE status IN ('Queued', 'Running') AND kind IN @Kinds;",
                new { Kinds = kinds }) != 0;
        }
    }

    internal sealed class BackgroundWorkerJobStore : IJobStore
    {
        private const string InterruptedError = "worker_restart_interrupted";
        private const string SelectColumns = @"SELECT
            id AS Id, kind AS Kind, status AS Status, actor_subject AS ActorSubject,
            source_schedule_id AS SourceScheduleId, idempotency_key AS IdempotencyKey,
            correlation_id AS CorrelationId, created_at_utc AS CreatedAtUtc,
            started_at_utc AS StartedAtUtc, completed_at_utc AS CompletedAtUtc,
            progress_current AS ProgressCurrent, progress_total AS ProgressTotal,
            error_code AS ErrorCode, worker_id AS WorkerId, row_version AS RowVersion
            FROM jobs";

        private readonly SqliteJobStore inner;
        private readonly SqliteConnectionFactory connectionFactory;

        public BackgroundWorkerJobStore(
            SqliteJobStore inner,
            SqliteConnectionFactory connectionFactory)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
        }

        public JobRecord Enqueue(NewJob job) => inner.Enqueue(job);

        public int InterruptRunningJobs(DateTimeOffset now)
        {
            RequireUtc(now, nameof(now));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE jobs
                  SET status = 'Interrupted', completed_at_utc = @CompletedAtUtc,
                      error_code = @ErrorCode, worker_id = NULL,
                      row_version = row_version + 1
                  WHERE status = 'Running';",
                new
                {
                    CompletedAtUtc = now.ToUnixTimeMilliseconds(),
                    ErrorCode = InterruptedError
                });
        }

        public JobRecord? TryClaimNext(string workerId, DateTimeOffset now)
        {
            workerId = RequireText(workerId, nameof(workerId));
            RequireUtc(now, nameof(now));
            using var connection = connectionFactory.Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                var queued = connection.QueryFirstOrDefault<JobRow>(
                    SelectColumns +
                    " WHERE status = 'Queued' AND kind <> @RestoreKind" +
                    " ORDER BY created_at_utc ASC, id ASC LIMIT 1;",
                    new { RestoreKind = JobKind.Restore.ToString() });
                if (queued == null)
                {
                    connection.Execute("COMMIT;");
                    return null;
                }

                var changed = connection.Execute(
                    @"UPDATE jobs
                      SET status = 'Running', started_at_utc = @StartedAtUtc,
                          worker_id = @WorkerId, row_version = row_version + 1
                      WHERE id = @Id AND status = 'Queued'
                        AND kind <> @RestoreKind AND row_version = @RowVersion;",
                    new
                    {
                        queued.Id,
                        StartedAtUtc = now.ToUnixTimeMilliseconds(),
                        WorkerId = workerId,
                        RestoreKind = JobKind.Restore.ToString(),
                        queued.RowVersion
                    });
                if (changed != 1)
                {
                    connection.Execute("ROLLBACK;");
                    return null;
                }

                var claimed = connection.QuerySingle<JobRow>(
                    SelectColumns + " WHERE id = @Id;",
                    new { queued.Id });
                connection.Execute("COMMIT;");
                return ToRecord(claimed);
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }

        public bool TryTransition(
            Guid jobId,
            long expectedRowVersion,
            JobStatus expected,
            JobStatus next,
            JobCompletion completion) => inner.TryTransition(
                jobId,
                expectedRowVersion,
                expected,
                next,
                completion);

        public JobRecord Get(Guid jobId) => inner.Get(jobId);

        public PagedResult<JobRecord, JobCursor> List(JobQuery query) =>
            inner.List(query);

        private static JobRecord ToRecord(JobRow row) => new JobRecord(
            Guid.Parse(row.Id),
            (JobKind)Enum.Parse(typeof(JobKind), row.Kind),
            (JobStatus)Enum.Parse(typeof(JobStatus), row.Status),
            row.ActorSubject,
            string.IsNullOrEmpty(row.SourceScheduleId)
                ? null
                : Guid.Parse(row.SourceScheduleId),
            row.IdempotencyKey,
            row.CorrelationId,
            DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
            row.StartedAtUtc.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(row.StartedAtUtc.Value)
                : null,
            row.CompletedAtUtc.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(row.CompletedAtUtc.Value)
                : null,
            row.ProgressCurrent.HasValue || row.ProgressTotal.HasValue
                ? new JobProgress(row.ProgressCurrent, row.ProgressTotal)
                : null,
            row.ErrorCode,
            row.WorkerId,
            row.RowVersion);

        private static void TryRollback(System.Data.IDbConnection connection)
        {
            try { connection.Execute("ROLLBACK;"); }
            catch { }
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "A non-empty value is required.",
                    parameterName);
            return value.Trim();
        }

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException(
                    "A UTC timestamp is required.",
                    parameterName);
        }

        private sealed class JobRow
        {
            public string Id { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string? ActorSubject { get; set; }
            public string? SourceScheduleId { get; set; }
            public string IdempotencyKey { get; set; } = string.Empty;
            public string? CorrelationId { get; set; }
            public long CreatedAtUtc { get; set; }
            public long? StartedAtUtc { get; set; }
            public long? CompletedAtUtc { get; set; }
            public long? ProgressCurrent { get; set; }
            public long? ProgressTotal { get; set; }
            public string? ErrorCode { get; set; }
            public string? WorkerId { get; set; }
            public long RowVersion { get; set; }
        }
    }
}
