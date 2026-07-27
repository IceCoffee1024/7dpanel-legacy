using System;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal sealed class AutomationRecoveryRuntime : IModRuntime
    {
        private readonly AutomationExecutionRecoveryService recovery;
        private readonly IModRuntime inner;
        private readonly object sync = new object();
        private bool started;

        public AutomationRecoveryRuntime(
            AutomationExecutionRecoveryService recovery,
            IModRuntime inner)
        {
            this.recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start()
        {
            lock (sync)
            {
                if (started) return;
                recovery.Recover();
                inner.Start();
                started = true;
            }
        }

        public void MarkGameReady()
        {
            lock (sync) inner.MarkGameReady();
        }

        public void Stop()
        {
            lock (sync)
            {
                if (!started) return;
                inner.Stop();
                started = false;
            }
        }
    }
}
