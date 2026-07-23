using System;
using HarmonyLib;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Compatibility
{
    internal sealed class ConsoleCommandHarmonyRuntime : IModRuntime, IDisposable
    {
        private const string HarmonyId = "com.lsty.7dpanel.console-command-audit";
        private readonly object operationSync = new object();
        private readonly IModRuntime inner;
        private Action? unpatch;
        private bool stopped;

        internal ConsoleCommandHarmonyRuntime(IModRuntime inner, Action unpatch)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.unpatch = unpatch ?? throw new ArgumentNullException(nameof(unpatch));
        }

        public static ConsoleCommandHarmonyRuntime Install(IModRuntime inner)
        {
            var harmony = Harmony.CreateAndPatchAll(
                typeof(ConsoleCommandExecutionPatch),
                HarmonyId);
            return new ConsoleCommandHarmonyRuntime(inner, harmony.UnpatchSelf);
        }

        public void Start() => inner.Start();

        public void MarkGameReady() => inner.MarkGameReady();

        public void Stop()
        {
            lock (operationSync)
            {
                if (stopped) return;
                stopped = true;
                var failures = new System.Collections.Generic.List<Exception>();
                try { inner.Stop(); }
                catch (Exception ex) { failures.Add(ex); }
                var candidate = unpatch;
                unpatch = null;
                try { candidate?.Invoke(); }
                catch (Exception ex) { failures.Add(ex); }
                if (failures.Count > 0) throw new AggregateException(failures);
            }
        }

        public void Dispose() => Stop();
    }
}