using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Modules
{
    public interface IFeatureModuleActivityQuery
    {
        bool HasActiveWork(FeatureModuleId moduleId);
    }

    public sealed record FeatureModuleSummary(
        FeatureModuleDescriptor Descriptor,
        FeatureModuleState State);

    public sealed record SetFeatureModuleStateRequest(
        FeatureModuleId ModuleId,
        bool IsEnabled,
        string ActorSubject,
        string CorrelationId,
        long ExpectedRowVersion);

    public sealed class FeatureModuleUseCases
    {
        private readonly IFeatureModuleStateStore states;
        private readonly IFeatureModuleActivityQuery activity;
        private readonly Func<DateTimeOffset> utcNow;

        public FeatureModuleUseCases(
            IFeatureModuleStateStore states,
            IFeatureModuleActivityQuery activity,
            Func<DateTimeOffset> utcNow)
        {
            this.states = states ?? throw new ArgumentNullException(nameof(states));
            this.activity = activity ?? throw new ArgumentNullException(nameof(activity));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public IReadOnlyList<FeatureModuleSummary> List() =>
            FeatureModulePolicy.All
                .Select(descriptor => new FeatureModuleSummary(
                    descriptor,
                    states.Get(descriptor.Id)))
                .ToArray();

        public FeatureModuleState SetEnabled(SetFeatureModuleStateRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var descriptor = FeatureModulePolicy.Describe(request.ModuleId);
            if (!descriptor.IsToggleable)
                throw new FeatureModuleNotToggleableException(request.ModuleId);

            var current = states.Get(request.ModuleId);
            if (current.RowVersion != request.ExpectedRowVersion)
                throw new FeatureModuleStateConflictException(request.ModuleId);
            if (current.IsEnabled == request.IsEnabled &&
                current.LifecycleState != FeatureModuleLifecycleState.Draining)
            {
                return current;
            }

            FeatureModuleLifecycleState lifecycle;
            if (request.IsEnabled)
            {
                var missing = descriptor.Dependencies
                    .FirstOrDefault(dependency => !states.Get(dependency).IsEnabled);
                if (descriptor.Dependencies.Contains(missing) &&
                    !states.Get(missing).IsEnabled)
                {
                    throw new FeatureModuleDependencyException(request.ModuleId, missing);
                }
                lifecycle = descriptor.DisableMode == FeatureModuleDisableMode.RestartRequired
                    ? FeatureModuleLifecycleState.RestartRequired
                    : FeatureModuleLifecycleState.Enabled;
            }
            else
            {
                var dependent = FeatureModulePolicy.All.FirstOrDefault(candidate =>
                    candidate.Dependencies.Contains(request.ModuleId) &&
                    states.Get(candidate.Id).IsEnabled);
                if (dependent != null)
                    throw new FeatureModuleDependencyException(request.ModuleId, dependent.Id);

                var active = activity.HasActiveWork(request.ModuleId);
                if (active && descriptor.DisableMode != FeatureModuleDisableMode.Drain)
                    throw new FeatureModuleActiveWorkException(request.ModuleId);
                lifecycle = active
                    ? FeatureModuleLifecycleState.Draining
                    : descriptor.DisableMode == FeatureModuleDisableMode.RestartRequired
                        ? FeatureModuleLifecycleState.RestartRequired
                        : FeatureModuleLifecycleState.Disabled;
            }

            var now = utcNow();
            if (now.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("feature_module_clock_not_utc");
            return states.Save(new FeatureModuleStateChange(
                request.ModuleId,
                request.IsEnabled,
                lifecycle,
                request.ActorSubject,
                request.CorrelationId,
                now,
                request.ExpectedRowVersion));
        }
    }

    public sealed class FeatureModuleNotToggleableException : InvalidOperationException
    {
        public FeatureModuleNotToggleableException(FeatureModuleId moduleId)
            : base("feature_module_not_toggleable") => ModuleId = moduleId;

        public FeatureModuleId ModuleId { get; }
    }

    public sealed class FeatureModuleDependencyException : InvalidOperationException
    {
        public FeatureModuleDependencyException(
            FeatureModuleId moduleId,
            FeatureModuleId dependencyOrDependent)
            : base("feature_module_dependency_conflict")
        {
            ModuleId = moduleId;
            DependencyOrDependent = dependencyOrDependent;
        }

        public FeatureModuleId ModuleId { get; }
        public FeatureModuleId DependencyOrDependent { get; }
    }

    public sealed class FeatureModuleActiveWorkException : InvalidOperationException
    {
        public FeatureModuleActiveWorkException(FeatureModuleId moduleId)
            : base("feature_module_active_work") => ModuleId = moduleId;

        public FeatureModuleId ModuleId { get; }
    }
}
