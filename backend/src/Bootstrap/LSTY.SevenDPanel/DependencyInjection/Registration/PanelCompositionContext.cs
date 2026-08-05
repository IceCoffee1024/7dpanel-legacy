using System;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.DependencyInjection.Registration
{
    internal sealed class PanelCompositionContext
    {
        internal PanelCompositionContext(
            PanelHostOptions options,
            string dataDirectory,
            string? assetRoot,
            Action<string> log)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                throw new ArgumentException(
                    "The panel data directory is required.",
                    nameof(dataDirectory));
            }

            DataDirectory = dataDirectory;
            AssetRoot = assetRoot;
            Log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal PanelHostOptions Options { get; }
        internal string DataDirectory { get; }
        internal string? AssetRoot { get; }
        internal Action<string> Log { get; }
    }
}
