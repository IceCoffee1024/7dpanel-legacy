using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public enum HostAdditionalMemoryKind
    {
        WindowsVirtualAddressSpace,
        LinuxSwap
    }

    public sealed class HostAdditionalMemory
    {
        public HostAdditionalMemory(HostAdditionalMemoryKind kind, long? totalBytes, long? usedBytes)
        {
            Kind = kind;
            TotalBytes = totalBytes;
            UsedBytes = usedBytes;
        }

        public HostAdditionalMemoryKind Kind { get; }
        public long? TotalBytes { get; }
        public long? UsedBytes { get; }
    }

    public sealed class HostPublicNetwork
    {
        public HostPublicNetwork(AvailabilityState availability, string? ipv4, string? ipv6)
        {
            Availability = availability;
            Ipv4 = ipv4;
            Ipv6 = ipv6;
        }

        public AvailabilityState Availability { get; }
        public string? Ipv4 { get; }
        public string? Ipv6 { get; }

        public HostPublicNetwork WithoutAddresses() => new HostPublicNetwork(Availability, null, null);
    }

    public sealed class HostOverviewSnapshot
    {
        public HostOverviewSnapshot(AvailabilityState availability, AvailabilityState identityAvailability, DateTimeOffset? sampledAtUtc, long? processUptimeSeconds, long? residentSetBytes, long? managedHeapBytes, long? otherMemoryBytes, double? cpuUsagePercent, string? operatingSystem, string? operatingSystemVersion, int? processorCount, long? memoryTotalBytes, long? memoryAvailableBytes, HostAdditionalMemory? additionalMemory, IEnumerable<HostStorageVolume>? storageVolumes, HostPublicNetwork publicNetwork, string? deviceId, string? currentSystemUser)
        {
            Availability = availability;
            IdentityAvailability = identityAvailability;
            SampledAtUtc = sampledAtUtc;
            ProcessUptimeSeconds = processUptimeSeconds;
            ResidentSetBytes = residentSetBytes;
            ManagedHeapBytes = managedHeapBytes;
            OtherMemoryBytes = otherMemoryBytes;
            CpuUsagePercent = cpuUsagePercent;
            OperatingSystem = operatingSystem;
            OperatingSystemVersion = operatingSystemVersion;
            ProcessorCount = processorCount;
            MemoryTotalBytes = memoryTotalBytes;
            MemoryAvailableBytes = memoryAvailableBytes;
            AdditionalMemory = additionalMemory;
            StorageVolumes = new ReadOnlyCollection<HostStorageVolume>((storageVolumes ?? Enumerable.Empty<HostStorageVolume>()).ToArray());
            PublicNetwork = publicNetwork ?? throw new ArgumentNullException(nameof(publicNetwork));
            DeviceId = deviceId;
            CurrentSystemUser = currentSystemUser;
        }

        public HostOverviewSnapshot(
            AvailabilityState availability,
            AvailabilityState identityAvailability,
            DateTimeOffset? sampledAtUtc,
            long? processUptimeSeconds,
            long? residentSetBytes,
            long? managedHeapBytes,
            long? otherMemoryBytes,
            double? cpuUsagePercent,
            string? operatingSystem,
            string? operatingSystemVersion,
            int? processorCount,
            long? memoryTotalBytes,
            long? memoryAvailableBytes,
            HostAdditionalMemory? additionalMemory,
            IEnumerable<HostStorageVolume>? storageVolumes,
            HostPublicNetwork publicNetwork,
            string? deviceId,
            string? currentSystemUser,
            string? osFamily,
            string? operatingSystemArchitecture,
            string? runtimeVersion,
            string? cpuModel,
            int? logicalCoreCount,
            double? cpuFrequencyMhz,
            string? deviceName,
            string? deviceModel,
            string? deviceType,
            int? processId,
            DateTimeOffset? processStartedAtUtc)
            : this(
                availability,
                identityAvailability,
                sampledAtUtc,
                processUptimeSeconds,
                residentSetBytes,
                managedHeapBytes,
                otherMemoryBytes,
                cpuUsagePercent,
                operatingSystem,
                operatingSystemVersion,
                processorCount,
                memoryTotalBytes,
                memoryAvailableBytes,
                additionalMemory,
                storageVolumes,
                publicNetwork,
                deviceId,
                currentSystemUser)
        {
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

        public AvailabilityState Availability { get; }
        public AvailabilityState IdentityAvailability { get; }
        public DateTimeOffset? SampledAtUtc { get; }
        public long? ProcessUptimeSeconds { get; }
        public long? ResidentSetBytes { get; }
        public long? ManagedHeapBytes { get; }
        public long? OtherMemoryBytes { get; }
        public double? CpuUsagePercent { get; }
        public string? OperatingSystem { get; }
        public string? OperatingSystemVersion { get; }
        public int? ProcessorCount { get; }
        public long? MemoryTotalBytes { get; }
        public long? MemoryAvailableBytes { get; }
        public HostAdditionalMemory? AdditionalMemory { get; }
        public IReadOnlyList<HostStorageVolume> StorageVolumes { get; }
        public HostPublicNetwork PublicNetwork { get; }
        public string? DeviceId { get; }
        public string? CurrentSystemUser { get; }

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

        internal HostOverviewSnapshot ForNonOwner() => new HostOverviewSnapshot(Availability, AvailabilityState.Forbidden, SampledAtUtc, ProcessUptimeSeconds, ResidentSetBytes, ManagedHeapBytes, OtherMemoryBytes, CpuUsagePercent, OperatingSystem, OperatingSystemVersion, ProcessorCount, MemoryTotalBytes, MemoryAvailableBytes, AdditionalMemory, StorageVolumes.Select(volume => volume.WithoutRootPath()), PublicNetwork.WithoutAddresses(), null, null, OsFamily, OperatingSystemArchitecture, RuntimeVersion, CpuModel, LogicalCoreCount, CpuFrequencyMhz, DeviceName, DeviceModel, DeviceType, ProcessId, ProcessStartedAtUtc);
        public static HostOverviewSnapshot Unavailable() => new HostOverviewSnapshot(AvailabilityState.Unavailable, AvailabilityState.Unavailable, null, null, null, null, null, null, null, null, null, null, null, null, Enumerable.Empty<HostStorageVolume>(), new HostPublicNetwork(AvailabilityState.Unavailable, null, null), null, null);
    }
}
