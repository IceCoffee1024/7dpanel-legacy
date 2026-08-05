using System;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Rewards;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Rewards;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Rewards;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LSTY.SevenDPanel.DependencyInjection.Registration
{
    internal static class EconomyServiceRegistration
    {
        internal static void Register(
            IServiceCollection services,
            PanelCompositionContext context)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var options = context.Options;
            var log = context.Log;

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

            services.AddSingleton<SevenDaysGameResourceCatalog>();
            services.AddSingleton<IGameResourceCatalog>(serviceProvider =>
                serviceProvider.GetRequiredService<SevenDaysGameResourceCatalog>());
            services.AddSingleton<QueryGameResourcesUseCase>();
            services.AddSingleton<GetGameResourceIconUseCase>();

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
        }
    }
}
