using System;
using System.IO;
using HarmonyLib;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Lifecycle;
using LSTY.SevenDPanel.Compatibility;
using LSTY.SevenDPanel.Configuration;
using LSTY.SevenDPanel.DependencyInjection;

namespace LSTY.SevenDPanel
{
    public sealed class ModMain : IModApi
    {
        private const string HarmonyId = "com.lsty.7dpanel.assembly-location";

        private Harmony? harmony;
        private ServiceProviderRuntime? runtime;
        private SevenDaysGameLifecycleAdapter? adapter;

        internal static Mod? ModInstance { get; private set; }

        public void InitMod(Mod? modInstance)
        {
            if (runtime != null || harmony != null) return;
            if (modInstance == null) throw new ArgumentNullException(nameof(modInstance));

            Action<string> log = message => Log.Out("[7DPanel] " + message);
            Harmony? candidateHarmony = null;
            ServiceProviderRuntime? candidateRuntime = null;
            SevenDaysGameLifecycleAdapter? candidateAdapter = null;
            try
            {
                ModInstance = modInstance;
                candidateHarmony = Harmony.CreateAndPatchAll(
                    typeof(AssemblyLocationPatch),
                    HarmonyId);
                if (string.IsNullOrEmpty(typeof(ModMain).Assembly.Location))
                    throw new InvalidOperationException(
                        "The 7DPanel assembly location compatibility patch did not produce a path.");
                log("Assembly location compatibility patch applied.");

                var options = PanelHostConfigurationLoader.FromMod(modInstance, log);
                var modDirectory = string.IsNullOrWhiteSpace(modInstance.Path)
                    ? AppContext.BaseDirectory
                    : modInstance.Path;
                var dataDirectory = Path.Combine(modDirectory, "data");
                var assetRoot = Path.Combine(modDirectory, "wwwroot");
                if (options.Authentication.Enabled && options.Authentication.AllowInsecureHttp)
                {
                    log("WARNING: authentication over insecure HTTP is enabled. " +
                        "Use this only for local development or behind a controlled local TLS proxy.");
                }
                candidateRuntime = PanelServiceProviderFactory.CreateRuntime(
                    options,
                    dataDirectory,
                    assetRoot,
                    log);
                candidateAdapter = new SevenDaysGameLifecycleAdapter(candidateRuntime);
                candidateAdapter.RegisterAndStart();
                runtime = candidateRuntime;
                adapter = candidateAdapter;
                harmony = candidateHarmony;
                Log.Out("[7DPanel] Mod initialized. URL: " + options.Url);
            }
            catch
            {
                try { candidateAdapter?.Dispose(); } catch { }
                try { candidateRuntime?.Dispose(); } catch { }
                try { candidateHarmony?.UnpatchSelf(); } catch { }
                ModInstance = null;
                throw;
            }
        }
    }
}
