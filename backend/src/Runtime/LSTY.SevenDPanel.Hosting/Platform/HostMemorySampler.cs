using System;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Hosting.Platform
{
    public sealed class HostMemorySnapshot
    {
        public HostMemorySnapshot(long? totalBytes, long? availableBytes, HostAdditionalMemory? additionalMemory)
        {
            TotalBytes = totalBytes;
            AvailableBytes = availableBytes;
            AdditionalMemory = additionalMemory;
        }

        public long? TotalBytes { get; }
        public long? AvailableBytes { get; }
        public HostAdditionalMemory? AdditionalMemory { get; }
    }

    public sealed class HostMemorySampler
    {
        public HostMemorySnapshot Sample(IHostPlatformAdapter platform, HostPlatformFamily family)
        {
            if (platform == null) throw new ArgumentNullException(nameof(platform));
            var sample = platform.ReadMemory();
            HostAdditionalMemory? additionalMemory = null;
            if (sample.SecondaryTotalBytes.HasValue && sample.SecondaryAvailableBytes.HasValue)
            {
                var used = Math.Max(0L, sample.SecondaryTotalBytes.Value - sample.SecondaryAvailableBytes.Value);
                additionalMemory = new HostAdditionalMemory(
                    family == HostPlatformFamily.Windows
                        ? HostAdditionalMemoryKind.WindowsVirtualAddressSpace
                        : HostAdditionalMemoryKind.LinuxSwap,
                    sample.SecondaryTotalBytes,
                    used);
            }

            return new HostMemorySnapshot(sample.TotalBytes, sample.AvailableBytes, additionalMemory);
        }
    }
}
