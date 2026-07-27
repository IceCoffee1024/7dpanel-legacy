using System;

namespace LSTY.SevenDPanel.Application.Modules
{
    public sealed class FeatureModuleGate
    {
        private readonly IFeatureModuleStateStore states;

        public FeatureModuleGate(IFeatureModuleStateStore states) =>
            this.states = states ?? throw new ArgumentNullException(nameof(states));

        public void RequireEnabled(FeatureModuleId moduleId)
        {
            if (!states.Get(moduleId).IsEnabled)
                throw new FeatureModuleDisabledException(moduleId);
        }
    }

    public sealed class FeatureModuleDisabledException : InvalidOperationException
    {
        public FeatureModuleDisabledException(FeatureModuleId moduleId)
            : base("feature_module_disabled") => ModuleId = moduleId;

        public FeatureModuleId ModuleId { get; }
    }

    public sealed class FeatureModuleStateConflictException : InvalidOperationException
    {
        public FeatureModuleStateConflictException(FeatureModuleId moduleId)
            : base("feature_module_state_conflict") => ModuleId = moduleId;

        public FeatureModuleId ModuleId { get; }
    }
}
