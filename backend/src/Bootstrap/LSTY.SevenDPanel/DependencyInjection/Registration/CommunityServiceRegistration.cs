using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Community;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Community;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Community;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Diagnostics;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Application.Discord;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Application.Modules;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LSTY.SevenDPanel.DependencyInjection.Registration
{
    internal static class CommunityServiceRegistration
    {
        internal static void Register(
            IServiceCollection services,
            PanelCompositionContext context)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var options = context.Options;
            var log = context.Log;

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

            services.AddSingleton(serviceProvider => new SevenDaysChatRuntime(
                serviceProvider.GetRequiredService<ChatRuntimeState>(),
                serviceProvider.GetRequiredService<ChatHistoryWriteService>(),
                serviceProvider.GetRequiredService<SevenDaysChatMessageCoordinator>(),
                serviceProvider.GetRequiredService<SevenDaysMapProjectionRuntime>(),
                serviceProvider.GetRequiredService<ChatMuteExpiryService>()));

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
    }
}
