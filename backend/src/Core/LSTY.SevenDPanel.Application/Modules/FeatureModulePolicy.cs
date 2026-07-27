using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Modules
{
    public static class FeatureModulePolicy
    {
        private static readonly FeatureModuleId[] Core =
        {
            FeatureModuleId.IdentityAndAuthorization,
            FeatureModuleId.Audit,
            FeatureModuleId.RuntimeHealth
        };

        private static readonly IReadOnlyList<FeatureModuleDescriptor> Descriptors =
            new ReadOnlyCollection<FeatureModuleDescriptor>(
                Enum.GetValues(typeof(FeatureModuleId))
                    .Cast<FeatureModuleId>()
                    .Select(Describe)
                    .ToArray());

        public static IReadOnlyList<FeatureModuleDescriptor> All => Descriptors;

        public static FeatureModuleDescriptor Describe(FeatureModuleId id)
        {
            switch (id)
            {
                case FeatureModuleId.IdentityAndAuthorization:
                    return Descriptor(id, false, None(), "authentication", "OwinStartup.Authentication");
                case FeatureModuleId.Audit:
                    return Descriptor(id, false, One(FeatureModuleId.IdentityAndAuthorization), "audit-store", "SqliteUnifiedAuditQuery");
                case FeatureModuleId.RuntimeHealth:
                    return Descriptor(id, false, new[] { FeatureModuleId.IdentityAndAuthorization, FeatureModuleId.Audit }, "runtime", "ModHost");
                case FeatureModuleId.Overview:
                    return Descriptor(id, false, Core, "overview", "GetOverviewUseCase");
                case FeatureModuleId.PlayerHistoryAndMap:
                    return Descriptor(id, true, WithCore(FeatureModuleId.Overview), "map-projection", "SevenDaysMapProjectionRuntime", FeatureModuleDisableMode.Drain);
                case FeatureModuleId.Console:
                    return Descriptor(id, true, Core, "console-runtime", "SevenDaysConsoleCommandService");
                case FeatureModuleId.Chat:
                    return Descriptor(id, true, Core, "chat-runtime", "SevenDaysChatRuntime", FeatureModuleDisableMode.Drain);
                case FeatureModuleId.GameResources:
                    return Descriptor(id, true, Core, "game-resource-catalog", "GameResourceCatalogRuntime", FeatureModuleDisableMode.RestartRequired);
                case FeatureModuleId.Backups:
                    return Descriptor(id, true, Core, "job-worker", "JobsAndSchedulingRuntime.Backups", FeatureModuleDisableMode.Drain);
                case FeatureModuleId.AnnouncementsAndScheduling:
                    return Descriptor(id, true, Core, "scheduler", "BackgroundScheduler", FeatureModuleDisableMode.Drain);
                case FeatureModuleId.PlayerItems:
                    return Descriptor(id, true, WithCore(FeatureModuleId.GameResources, FeatureModuleId.PlayerHistoryAndMap), "player-evidence", "PlayerEvidenceRuntime", FeatureModuleDisableMode.Drain);
                case FeatureModuleId.EconomyAndRewards:
                    return Descriptor(id, true, WithCore(FeatureModuleId.PlayerItems), "economy-ledger", "EconomyLedgerUseCase");
                case FeatureModuleId.TeleportAndVoting:
                    return Descriptor(id, true, WithCore(FeatureModuleId.PlayerHistoryAndMap), "community-runtime", "CommunityTeleportUseCase");
                case FeatureModuleId.Automation:
                    return Descriptor(id, true, WithCore(FeatureModuleId.AnnouncementsAndScheduling), "automation-runtime", "AutomationTriggerRuntime", FeatureModuleDisableMode.Drain);
                case FeatureModuleId.Discord:
                    return Descriptor(id, true, WithCore(FeatureModuleId.Chat), "discord-worker", "DiscordDeliveryWorker", FeatureModuleDisableMode.Drain);
                case FeatureModuleId.GeoIp:
                    return Descriptor(id, true, Core, "geoip-policy", "GeoIpJoinPolicyAdapter");
                case FeatureModuleId.WorldTools:
                    return Descriptor(id, true, WithCore(
                        FeatureModuleId.Backups,
                        FeatureModuleId.GameResources,
                        FeatureModuleId.PlayerHistoryAndMap), "world-operation-worker", "WorldOperationRuntime", FeatureModuleDisableMode.Drain);
                default:
                    throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        private static FeatureModuleDescriptor Descriptor(
            FeatureModuleId id,
            bool toggleable,
            IEnumerable<FeatureModuleId> dependencies,
            string healthSource,
            string consumerId,
            FeatureModuleDisableMode disableMode = FeatureModuleDisableMode.Immediate) =>
            new FeatureModuleDescriptor(
                id,
                toggleable,
                dependencies,
                NoneText(),
                healthSource,
                disableMode,
                "preserve-existing-data",
                new[] { consumerId });

        private static FeatureModuleId[] None() => Array.Empty<FeatureModuleId>();
        private static string[] NoneText() => Array.Empty<string>();
        private static FeatureModuleId[] One(FeatureModuleId value) => new[] { value };

        private static FeatureModuleId[] WithCore(params FeatureModuleId[] extra) =>
            Core.Concat(extra).Distinct().ToArray();
    }
}
