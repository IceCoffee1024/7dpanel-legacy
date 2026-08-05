using System;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Activity;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.GameEvents;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LSTY.SevenDPanel.DependencyInjection.Registration
{
    internal static class PlayersServiceRegistration
    {
        internal static void Register(
            IServiceCollection services,
            PanelCompositionContext context)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var options = context.Options;
            var log = context.Log;

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

            services.AddSingleton(serviceProvider => new OnlinePlayerProjectionRuntime(
                serviceProvider.GetRequiredService<SevenDaysOnlinePlayerProjection>(),
                serviceProvider.GetRequiredService<ConsoleCommandRuntime>()));
            services.AddSingleton(serviceProvider => new PlayerHistoryRuntime(
                serviceProvider.GetRequiredService<PlayerHistoryWriteService>(),
                serviceProvider.GetRequiredService<OnlinePlayerProjectionRuntime>()));
            services.AddSingleton(serviceProvider => new SevenDaysMapProjectionRuntime(
                serviceProvider.GetRequiredService<SevenDaysMapMetadataProjection>(),
                serviceProvider.GetRequiredService<SevenDaysMapGameTimeProjection>(),
                serviceProvider.GetRequiredService<SevenDaysMapLayerProjection>(),
                serviceProvider.GetRequiredService<SevenDaysTransientEntityProjection>(),
                serviceProvider.GetRequiredService<SevenDaysWorldSnapshotProjection>(),
                serviceProvider.GetRequiredService<SevenDaysWorldToolCatalog>(),
                serviceProvider.GetRequiredService<SevenDaysRecentActivityRuntime>()));

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
        }
    }
}
