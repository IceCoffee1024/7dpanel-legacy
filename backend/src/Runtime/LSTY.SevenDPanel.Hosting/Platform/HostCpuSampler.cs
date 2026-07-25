using System;

namespace LSTY.SevenDPanel.Hosting.Platform
{
    public sealed class HostCpuSampler
    {
        private readonly object sync = new object();
        private HostCpuCounters? previous;

        public double? Sample(IHostPlatformAdapter platform)
        {
            if (platform == null) throw new ArgumentNullException(nameof(platform));
            lock (sync)
            {
                var current = platform.ReadCpuCounters();
                if (previous == null)
                {
                    previous = current;
                    return null;
                }

                var totalDelta = current.TotalTicks - previous.TotalTicks;
                var idleDelta = current.IdleTicks - previous.IdleTicks;
                previous = current;
                if (totalDelta <= 0L) return null;
                var usage = (totalDelta - idleDelta) * 100d / totalDelta;
                return Math.Max(0d, Math.Min(100d, usage));
            }
        }
    }
}
