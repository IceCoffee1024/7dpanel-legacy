using System;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Automations;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Announcements;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Discord;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LSTY.SevenDPanel.DependencyInjection.Registration
{
    internal static class AutomationServiceRegistration
    {
        internal static void Register(
            IServiceCollection services,
            PanelCompositionContext context)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var options = context.Options;

            services.AddSingleton<SqliteAutomationStore>();
            services.AddSingleton<IAutomationStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteAutomationStore>());
            services.AddSingleton<IAutomationExecutionRecoveryStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteAutomationStore>());
            services.AddSingleton<IAutomationExecutionQuery>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteAutomationStore>());
            services.AddSingleton<AutomationExecutionRecoveryService>();

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

            services.AddSingleton(serviceProvider => new AutomationRuntime(
                serviceProvider.GetRequiredService<AutomationTriggerRuntime>(),
                serviceProvider.GetRequiredService<JobsAndSchedulingRuntime>()));
            services.AddSingleton(serviceProvider => new AutomationRecoveryRuntime(
                serviceProvider.GetRequiredService<AutomationExecutionRecoveryService>(),
                serviceProvider.GetRequiredService<GeoIpRuntime>()));
        }
    }
}
