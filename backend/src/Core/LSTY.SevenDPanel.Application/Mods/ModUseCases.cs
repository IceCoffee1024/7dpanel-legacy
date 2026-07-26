using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Mods
{
    public sealed class ListModsUseCase
    {
        private readonly IModCatalog catalog;
        private readonly ILoadedModQuery loadedModQuery;

        public ListModsUseCase(IModCatalog catalog, ILoadedModQuery loadedModQuery)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.loadedModQuery = loadedModQuery ?? throw new ArgumentNullException(nameof(loadedModQuery));
        }

        public IReadOnlyList<ModView> Execute()
        {
            var loaded = loadedModQuery.GetLoadedNames();
            var names = new HashSet<string>(loaded.Names, StringComparer.Ordinal);
            return catalog.List()
                .Select(entry => new ModView(entry, loaded.Available ? names.Contains(entry.Name) : (bool?)null))
                .ToArray();
        }
    }

    public sealed class SetModStateUseCase
    {
        private readonly IModCatalog catalog;

        public SetModStateUseCase(IModCatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public ModStateChangeResult Execute(string directoryId, bool enabled) =>
            catalog.SetEnabled(directoryId, enabled);
    }
}
