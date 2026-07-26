using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application.Mods
{
    public interface IModCatalog
    {
        IReadOnlyList<ModDiskEntry> List();
        ModStateChangeResult SetEnabled(string directoryId, bool enabled);
    }
}
