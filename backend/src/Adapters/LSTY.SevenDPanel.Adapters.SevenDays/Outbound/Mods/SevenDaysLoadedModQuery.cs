using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Mods;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Mods
{
    public sealed class SevenDaysLoadedModQuery : ILoadedModQuery
    {
        private readonly Func<(bool Available, IEnumerable<string> Names)> capture;

        public SevenDaysLoadedModQuery()
            : this(Capture)
        {
        }

        internal SevenDaysLoadedModQuery(Func<(bool Available, IEnumerable<string> Names)> capture)
        {
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
        }

        public LoadedModSnapshot GetLoadedNames()
        {
            try
            {
                var sample = capture();
                return sample.Available
                    ? new LoadedModSnapshot(true, sample.Names.ToArray())
                    : LoadedModSnapshot.Unavailable();
            }
            catch
            {
                return LoadedModSnapshot.Unavailable();
            }
        }

        private static (bool Available, IEnumerable<string> Names) Capture()
        {
            var manager = global::GameManager.Instance;
            if (manager?.World == null)
                return (false, Array.Empty<string>());

            return (true, global::ModManager.GetLoadedMods()
                .Select(mod => mod.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray());
        }
    }
}
