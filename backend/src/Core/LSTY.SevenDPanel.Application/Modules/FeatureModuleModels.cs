using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Modules
{
    public enum FeatureModuleId
    {
        IdentityAndAuthorization,
        Audit,
        RuntimeHealth,
        Overview,
        PlayerHistoryAndMap,
        Console,
        Chat,
        GameResources,
        Backups,
        AnnouncementsAndScheduling,
        PlayerItems,
        EconomyAndRewards,
        TeleportAndVoting,
        Automation,
        Discord,
        GeoIp,
        WorldTools
    }

    public enum FeatureModuleDisableMode
    {
        Immediate,
        Drain,
        RestartRequired
    }

    public enum FeatureModuleLifecycleState
    {
        Enabled,
        Disabled,
        Draining,
        RestartRequired
    }

    public sealed class FeatureModuleDescriptor
    {
        public FeatureModuleDescriptor(
            FeatureModuleId id,
            bool isToggleable,
            IEnumerable<FeatureModuleId> dependencies,
            IEnumerable<string> settingsSummaryFields,
            string healthSource,
            FeatureModuleDisableMode disableMode,
            string dataRetentionSummary,
            IEnumerable<string> consumerIds)
        {
            if (!Enum.IsDefined(typeof(FeatureModuleId), id))
                throw new ArgumentOutOfRangeException(nameof(id));
            if (!Enum.IsDefined(typeof(FeatureModuleDisableMode), disableMode))
                throw new ArgumentOutOfRangeException(nameof(disableMode));
            Id = id;
            IsToggleable = isToggleable;
            Dependencies = CopyDistinct(dependencies, nameof(dependencies));
            SettingsSummaryFields = CopyText(settingsSummaryFields, nameof(settingsSummaryFields), true);
            HealthSource = RequireText(healthSource, nameof(healthSource));
            DisableMode = disableMode;
            DataRetentionSummary = RequireText(dataRetentionSummary, nameof(dataRetentionSummary));
            ConsumerIds = CopyText(consumerIds, nameof(consumerIds), false);
        }

        public FeatureModuleId Id { get; }
        public bool IsToggleable { get; }
        public IReadOnlyList<FeatureModuleId> Dependencies { get; }
        public IReadOnlyList<string> SettingsSummaryFields { get; }
        public string HealthSource { get; }
        public FeatureModuleDisableMode DisableMode { get; }
        public string DataRetentionSummary { get; }
        public IReadOnlyList<string> ConsumerIds { get; }

        private static IReadOnlyList<FeatureModuleId> CopyDistinct(
            IEnumerable<FeatureModuleId> values,
            string parameterName)
        {
            if (values == null) throw new ArgumentNullException(parameterName);
            var copy = values.ToArray();
            if (copy.Any(value => !Enum.IsDefined(typeof(FeatureModuleId), value)) ||
                copy.Distinct().Count() != copy.Length)
            {
                throw new ArgumentException("Feature module dependencies must be unique fixed IDs.", parameterName);
            }
            return new ReadOnlyCollection<FeatureModuleId>(copy);
        }

        private static IReadOnlyList<string> CopyText(
            IEnumerable<string> values,
            string parameterName,
            bool allowEmpty)
        {
            if (values == null) throw new ArgumentNullException(parameterName);
            var copy = values.Select(value => RequireText(value, parameterName)).ToArray();
            if ((!allowEmpty && copy.Length == 0) ||
                copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            {
                throw new ArgumentException("Feature module metadata must be unique and non-empty.", parameterName);
            }
            return new ReadOnlyCollection<string>(copy);
        }

        internal static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
                throw new ArgumentException("A bounded non-empty value is required.", parameterName);
            return value.Trim();
        }
    }

    public sealed record FeatureModuleState(
        FeatureModuleId ModuleId,
        bool IsEnabled,
        FeatureModuleLifecycleState LifecycleState,
        string UpdatedBy,
        string CorrelationId,
        DateTimeOffset UpdatedAtUtc,
        long RowVersion)
    {
        public static FeatureModuleState DefaultEnabled(FeatureModuleId moduleId) =>
            new FeatureModuleState(
                moduleId,
                true,
                FeatureModuleLifecycleState.Enabled,
                "system-default",
                "default:" + moduleId,
                new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero),
                0);
    }

    public sealed class FeatureModuleStateChange
    {
        public FeatureModuleStateChange(
            FeatureModuleId moduleId,
            bool isEnabled,
            FeatureModuleLifecycleState lifecycleState,
            string updatedBy,
            string correlationId,
            DateTimeOffset updatedAtUtc,
            long expectedRowVersion)
        {
            if (!Enum.IsDefined(typeof(FeatureModuleId), moduleId))
                throw new ArgumentOutOfRangeException(nameof(moduleId));
            if (!Enum.IsDefined(typeof(FeatureModuleLifecycleState), lifecycleState))
                throw new ArgumentOutOfRangeException(nameof(lifecycleState));
            if (expectedRowVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            if (updatedAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", nameof(updatedAtUtc));
            if (isEnabled && lifecycleState != FeatureModuleLifecycleState.Enabled &&
                lifecycleState != FeatureModuleLifecycleState.RestartRequired)
            {
                throw new ArgumentException("Enabled module lifecycle is invalid.", nameof(lifecycleState));
            }
            if (!isEnabled && lifecycleState == FeatureModuleLifecycleState.Enabled)
                throw new ArgumentException("Disabled module lifecycle is invalid.", nameof(lifecycleState));

            ModuleId = moduleId;
            IsEnabled = isEnabled;
            LifecycleState = lifecycleState;
            UpdatedBy = FeatureModuleDescriptor.RequireText(updatedBy, nameof(updatedBy));
            CorrelationId = FeatureModuleDescriptor.RequireText(correlationId, nameof(correlationId));
            UpdatedAtUtc = updatedAtUtc;
            ExpectedRowVersion = expectedRowVersion;
        }

        public FeatureModuleId ModuleId { get; }
        public bool IsEnabled { get; }
        public FeatureModuleLifecycleState LifecycleState { get; }
        public string UpdatedBy { get; }
        public string CorrelationId { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long ExpectedRowVersion { get; }
    }
}
