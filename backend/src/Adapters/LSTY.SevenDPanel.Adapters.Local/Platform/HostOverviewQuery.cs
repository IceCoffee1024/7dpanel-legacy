using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Local.Platform
{
    public enum HostPlatformFamily
    {
        Windows,
        Linux
    }

    public sealed class HostPlatformInfo
    {
        public HostPlatformInfo(
            HostPlatformFamily family,
            string operatingSystem,
            string operatingSystemVersion,
            int processorCount,
            string? currentSystemUser,
            string? machineIdentity,
            long? processUptimeSeconds,
            long? residentSetBytes,
            long? managedHeapBytes,
            long? otherMemoryBytes,
            string? osFamily = null,
            string? operatingSystemArchitecture = null,
            string? runtimeVersion = null,
            string? cpuModel = null,
            int? logicalCoreCount = null,
            double? cpuFrequencyMhz = null,
            string? deviceName = null,
            string? deviceModel = null,
            string? deviceType = null,
            int? processId = null,
            DateTimeOffset? processStartedAtUtc = null)
        {
            Family = family;
            OperatingSystem = operatingSystem;
            OperatingSystemVersion = operatingSystemVersion;
            ProcessorCount = processorCount;
            CurrentSystemUser = currentSystemUser;
            MachineIdentity = machineIdentity;
            ProcessUptimeSeconds = processUptimeSeconds;
            ResidentSetBytes = residentSetBytes;
            ManagedHeapBytes = managedHeapBytes;
            OtherMemoryBytes = otherMemoryBytes;
            OsFamily = osFamily;
            OperatingSystemArchitecture = operatingSystemArchitecture;
            RuntimeVersion = runtimeVersion;
            CpuModel = cpuModel;
            LogicalCoreCount = logicalCoreCount;
            CpuFrequencyMhz = cpuFrequencyMhz;
            DeviceName = deviceName;
            DeviceModel = deviceModel;
            DeviceType = deviceType;
            ProcessId = processId;
            ProcessStartedAtUtc = processStartedAtUtc;
        }

        public HostPlatformFamily Family { get; }
        public string OperatingSystem { get; }
        public string OperatingSystemVersion { get; }
        public int ProcessorCount { get; }
        public string? CurrentSystemUser { get; }
        public string? MachineIdentity { get; }
        public long? ProcessUptimeSeconds { get; }
        public long? ResidentSetBytes { get; }
        public long? ManagedHeapBytes { get; }
        public long? OtherMemoryBytes { get; }
        public string? OsFamily { get; }
        public string? OperatingSystemArchitecture { get; }
        public string? RuntimeVersion { get; }
        public string? CpuModel { get; }
        public int? LogicalCoreCount { get; }
        public double? CpuFrequencyMhz { get; }
        public string? DeviceName { get; }
        public string? DeviceModel { get; }
        public string? DeviceType { get; }
        public int? ProcessId { get; }
        public DateTimeOffset? ProcessStartedAtUtc { get; }
    }

    public sealed class HostCpuCounters
    {
        public HostCpuCounters(long totalTicks, long idleTicks)
        {
            TotalTicks = totalTicks;
            IdleTicks = idleTicks;
        }

        public long TotalTicks { get; }
        public long IdleTicks { get; }
    }

    public sealed class HostMemorySample
    {
        public HostMemorySample(long? totalBytes, long? availableBytes, long? secondaryTotalBytes, long? secondaryAvailableBytes)
        {
            TotalBytes = totalBytes;
            AvailableBytes = availableBytes;
            SecondaryTotalBytes = secondaryTotalBytes;
            SecondaryAvailableBytes = secondaryAvailableBytes;
        }

        public long? TotalBytes { get; }
        public long? AvailableBytes { get; }
        public long? SecondaryTotalBytes { get; }
        public long? SecondaryAvailableBytes { get; }
    }

    public interface IHostStorageVolumeSource
    {
        string Name { get; }
        string RootPath { get; }
        bool IsFixed { get; }
        bool IsOverlay { get; }
        long TotalBytes { get; }
        long AvailableBytes { get; }
        bool? IsPrimaryDataVolume { get; }
    }

    public interface IHostPlatformAdapter
    {
        HostPlatformInfo ReadPlatformInfo();
        HostCpuCounters ReadCpuCounters();
        HostMemorySample ReadMemory();
        IEnumerable<IHostStorageVolumeSource> ReadStorageVolumes();
    }

    public sealed class HostOverviewQuery : IHostOverviewQuery
    {
        private readonly IHostPlatformAdapter platform;
        private readonly HostCpuSampler cpuSampler;
        private readonly HostMemorySampler memorySampler;
        private readonly HostStorageSampler storageSampler;
        private readonly DeviceIdentityProvider deviceIdentityProvider;
        private readonly PublicNetworkAddressResolver publicNetworkAddressResolver;
        private readonly Func<DateTimeOffset> utcNow;

        public HostOverviewQuery(
            IHostPlatformAdapter platform,
            HostCpuSampler cpuSampler,
            HostMemorySampler memorySampler,
            HostStorageSampler storageSampler,
            DeviceIdentityProvider deviceIdentityProvider,
            PublicNetworkAddressResolver publicNetworkAddressResolver,
            Func<DateTimeOffset>? utcNow = null)
        {
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            this.cpuSampler = cpuSampler ?? throw new ArgumentNullException(nameof(cpuSampler));
            this.memorySampler = memorySampler ?? throw new ArgumentNullException(nameof(memorySampler));
            this.storageSampler = storageSampler ?? throw new ArgumentNullException(nameof(storageSampler));
            this.deviceIdentityProvider = deviceIdentityProvider ?? throw new ArgumentNullException(nameof(deviceIdentityProvider));
            this.publicNetworkAddressResolver = publicNetworkAddressResolver ?? throw new ArgumentNullException(nameof(publicNetworkAddressResolver));
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        public async Task<HostOverviewSnapshot> GetHostOverviewAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = platform.ReadPlatformInfo();
            var memory = memorySampler.Sample(platform, info.Family);
            var network = await publicNetworkAddressResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
            var deviceId = deviceIdentityProvider.CreateDeviceId(info);
            var identityAvailability = string.IsNullOrEmpty(deviceId) && string.IsNullOrEmpty(info.CurrentSystemUser)
                ? AvailabilityState.Unavailable
                : AvailabilityState.Available;

            return new HostOverviewSnapshot(
                AvailabilityState.Available,
                identityAvailability,
                utcNow(),
                info.ProcessUptimeSeconds,
                info.ResidentSetBytes,
                info.ManagedHeapBytes,
                info.OtherMemoryBytes,
                cpuSampler.Sample(platform),
                info.OperatingSystem,
                info.OperatingSystemVersion,
                info.ProcessorCount,
                memory.TotalBytes,
                memory.AvailableBytes,
                memory.AdditionalMemory,
                storageSampler.Sample(platform, info.Family),
                network,
                deviceId,
                info.CurrentSystemUser,
                info.OsFamily,
                info.OperatingSystemArchitecture,
                info.RuntimeVersion,
                info.CpuModel,
                info.LogicalCoreCount,
                info.CpuFrequencyMhz,
                info.DeviceName,
                info.DeviceModel,
                info.DeviceType,
                info.ProcessId,
                info.ProcessStartedAtUtc);
        }
    }
}
