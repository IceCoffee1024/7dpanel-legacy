using System;
using System.Collections.Generic;
using System.Threading;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Automations;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Application.Modules;
using LSTY.SevenDPanel.Domain.Automations;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal sealed class AutomationRuntime : IModRuntime
    {
        private readonly AutomationTriggerRuntime triggers;
        private readonly IModRuntime inner;
        private readonly object sync = new object();
        private bool started;

        public AutomationRuntime(AutomationTriggerRuntime triggers, IModRuntime inner)
        {
            this.triggers = triggers ?? throw new ArgumentNullException(nameof(triggers));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start()
        {
            lock (sync)
            {
                if (started) return;
                triggers.Start();
                try
                {
                    inner.Start();
                    started = true;
                }
                catch
                {
                    triggers.StopAsync(() => { }, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    throw;
                }
            }
        }

        public void MarkGameReady()
        {
            lock (sync) inner.MarkGameReady();
        }

        public void Stop()
        {
            lock (sync)
            {
                if (!started) return;
                var failures = new List<Exception>();
                try { inner.Stop(); }
                catch (Exception exception) { failures.Add(exception); }
                try
                {
                    triggers.StopAsync(() => { }, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception exception) { failures.Add(exception); }
                if (failures.Count == 0) started = false;
                else throw new AggregateException(failures);
            }
        }
    }

    internal sealed class FeatureModuleAutomationDependencyCatalog :
        IAutomationDependencyCatalog
    {
        private readonly IFeatureModuleStateStore modules;

        public FeatureModuleAutomationDependencyCatalog(IFeatureModuleStateStore modules) =>
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));

        public AutomationDependencyState Resolve(AutomationAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var module = ModuleFor(action.Type);
            return modules.Get(module).IsEnabled
                ? AutomationDependencyState.Ready
                : AutomationDependencyState.Disabled(
                    "automation_dependency_" + module.ToString().ToLowerInvariant() + "_disabled");
        }

        private static FeatureModuleId ModuleFor(string actionType)
        {
            switch (actionType)
            {
                case "BroadcastMessage":
                case "PrivateMessage":
                case "MutePlayer":
                    return FeatureModuleId.Chat;
                case "Announcement":
                    return FeatureModuleId.AnnouncementsAndScheduling;
                case "GrantItem":
                    return FeatureModuleId.PlayerItems;
                case "GrantRewardPackage":
                case "AdjustEconomy":
                    return FeatureModuleId.EconomyAndRewards;
                case "DiscordMessage":
                    return FeatureModuleId.Discord;
                case "RestrictedCommand":
                    return FeatureModuleId.Console;
                case "KickPlayer":
                    return FeatureModuleId.PlayerHistoryAndMap;
                default:
                    return FeatureModuleId.Automation;
            }
        }
    }

    internal sealed class StableAutomationTargetResolver : IAutomationTargetResolver
    {
        public AutomationTargetResolution Resolve(
            AutomationAction action,
            AutomationTriggerSnapshot snapshot)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            switch (action.TargetKind)
            {
                case "Global":
                    return AutomationTargetResolution.Resolved("global");
                case "TriggerPlayer":
                    return string.IsNullOrWhiteSpace(snapshot.ActorCrossplatformId)
                        ? AutomationTargetResolution.Unresolved("automation_trigger_player_missing")
                        : AutomationTargetResolution.Resolved(snapshot.ActorCrossplatformId!);
                case "StablePlayer":
                case "DiscordTarget":
                    return string.IsNullOrWhiteSpace(action.ReferenceId)
                        ? AutomationTargetResolution.Unresolved("automation_target_reference_missing")
                        : AutomationTargetResolution.Resolved(action.ReferenceId!);
                default:
                    return AutomationTargetResolution.Unresolved("automation_target_kind_unavailable");
            }
        }
    }
}
