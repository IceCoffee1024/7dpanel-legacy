using System;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class PlayerActionRecoveryRuntime : IModRuntime
    {
        private readonly object sync = new object();
        private readonly PlayerActionRecoveryService recovery;
        private readonly IModRuntime inner;
        private bool started;

        public PlayerActionRecoveryRuntime(
            PlayerActionRecoveryService recovery,
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

        public void MarkGameReady() => inner.MarkGameReady();

        public void Stop() => inner.Stop();
    }
}
