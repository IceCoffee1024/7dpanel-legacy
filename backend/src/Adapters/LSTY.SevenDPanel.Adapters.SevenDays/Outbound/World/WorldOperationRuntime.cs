using System;
using LSTY.SevenDPanel.Application.WorldOperations;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World
{
    public sealed class WorldOperationRuntime : IModRuntime
    {
        private readonly object sync = new object();
        private readonly IWorldOperationRecoveryStore recovery;
        private readonly IModRuntime inner;
        private readonly Func<DateTimeOffset> utcNow;
        private bool started;

        public WorldOperationRuntime(
            IWorldOperationRecoveryStore recovery,
            IModRuntime inner,
            Func<DateTimeOffset> utcNow)
        {
            this.recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public void Start()
        {
            lock (sync)
            {
                if (started) return;
                recovery.RecoverRunning(RequireUtc(utcNow()));
                inner.Start();
                started = true;
            }
        }

        public void MarkGameReady() => inner.MarkGameReady();

        public void Stop() => inner.Stop();

        private static DateTimeOffset RequireUtc(DateTimeOffset value)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("world_operation_recovery_clock_not_utc");
            return value;
        }
    }
}
