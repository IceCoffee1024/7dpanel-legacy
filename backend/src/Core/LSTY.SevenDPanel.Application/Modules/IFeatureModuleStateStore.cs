using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application.Modules
{
    public interface IFeatureModuleStateStore
    {
        FeatureModuleState Get(FeatureModuleId moduleId);
        IReadOnlyList<FeatureModuleState> List();
        FeatureModuleState Save(FeatureModuleStateChange change);
    }
}
