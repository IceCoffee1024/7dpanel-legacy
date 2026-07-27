using System;
using System.Linq;
using LSTY.SevenDPanel.Application.Modules;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class SetFeatureModuleStateHttpRequest
    {
        public long ExpectedRowVersion { get; set; }
    }

    public sealed class FeatureModuleHttpResponse
    {
        public FeatureModuleHttpResponse(FeatureModuleSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            var descriptor = summary.Descriptor;
            var state = summary.State;
            ModuleId = descriptor.Id.ToString();
            IsToggleable = descriptor.IsToggleable;
            Dependencies = descriptor.Dependencies.Select(value => value.ToString()).ToArray();
            SettingsSummaryFields = descriptor.SettingsSummaryFields.ToArray();
            HealthSource = descriptor.HealthSource;
            DisableMode = descriptor.DisableMode.ToString();
            DataRetentionSummary = descriptor.DataRetentionSummary;
            ConsumerIds = descriptor.ConsumerIds.ToArray();
            IsEnabled = state.IsEnabled;
            LifecycleState = state.LifecycleState.ToString();
            UpdatedBy = state.UpdatedBy;
            CorrelationId = state.CorrelationId;
            UpdatedAtUtc = state.UpdatedAtUtc;
            RowVersion = state.RowVersion;
        }

        public string ModuleId { get; }
        public bool IsToggleable { get; }
        public string[] Dependencies { get; }
        public string[] SettingsSummaryFields { get; }
        public string HealthSource { get; }
        public string DisableMode { get; }
        public string DataRetentionSummary { get; }
        public string[] ConsumerIds { get; }
        public bool IsEnabled { get; }
        public string LifecycleState { get; }
        public string UpdatedBy { get; }
        public string CorrelationId { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }
}
