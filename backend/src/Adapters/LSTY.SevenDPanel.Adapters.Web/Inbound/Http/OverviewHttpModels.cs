using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application;
using Newtonsoft.Json;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class OverviewHttpResponse
    {
        private OverviewHttpResponse(
            string availability,
            GameOverviewHttpResponse game,
            HostOverviewHttpResponse host,
            RestartPolicyHttpResponse restartPolicy,
            RecentActivityHttpResponse recentActivity,
            IReadOnlyList<OverviewAttentionHttpResponse> attention)
        {
            Availability = availability;
            Game = game;
            Host = host;
            RestartPolicy = restartPolicy;
            RecentActivity = recentActivity;
            Attention = attention;
        }

        public string Availability { get; }
        public GameOverviewHttpResponse Game { get; }
        public HostOverviewHttpResponse Host { get; }
        public RestartPolicyHttpResponse RestartPolicy { get; }
        public RecentActivityHttpResponse RecentActivity { get; }
        public IReadOnlyList<OverviewAttentionHttpResponse> Attention { get; }

        internal static OverviewHttpResponse FromSnapshot(OverviewSnapshot snapshot, bool includeSensitive)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            return new OverviewHttpResponse(
                ToContract(snapshot.Availability),
                new GameOverviewHttpResponse(snapshot.Game),
                new HostOverviewHttpResponse(snapshot.Host, includeSensitive),
                new RestartPolicyHttpResponse(snapshot.RestartPolicy),
                new RecentActivityHttpResponse(snapshot.RecentActivity),
                snapshot.Attention.Select(item => new OverviewAttentionHttpResponse
                {
                    Code = item.Code
                }).ToArray());
        }

        internal static string ToContract(AvailabilityState value) =>
            value.ToString().ToLowerInvariant();
    }

    public sealed class GameOverviewHttpResponse
    {
        internal GameOverviewHttpResponse(GameOverviewSnapshot source)
        {
            Availability = OverviewHttpResponse.ToContract(source.Availability);
            SampledAtUtc = source.SampledAtUtc;
            GameTitle = source.GameTitle;
            SaveGameName = source.SaveGameName;
            WorldName = source.WorldName;
            WorldSessionUptimeSeconds = source.WorldSessionUptimeSeconds;
            Version = source.Version;
            GameMode = source.GameMode;
            Difficulty = source.Difficulty;
            Region = source.Region;
            Language = source.Language;
            ConnectionAddress = source.ConnectionAddress;
            ConnectionPort = source.ConnectionPort;
            MaximumPlayerCount = source.MaximumPlayerCount;
            RuntimeMetrics = source.RuntimeMetrics == null
                ? null
                : new GameRuntimeMetricsHttpResponse(source.RuntimeMetrics);
        }

        public string Availability { get; }
        public DateTimeOffset? SampledAtUtc { get; }
        public string? GameTitle { get; }
        public string? SaveGameName { get; }
        public string? WorldName { get; }
        public long? WorldSessionUptimeSeconds { get; }
        public string? Version { get; }
        public string? GameMode { get; }
        public string? Difficulty { get; }
        public string? Region { get; }
        public string? Language { get; }
        public string? ConnectionAddress { get; }
        public int? ConnectionPort { get; }
        public int? MaximumPlayerCount { get; }
        public GameRuntimeMetricsHttpResponse? RuntimeMetrics { get; }
    }

    public sealed class GameRuntimeMetricsHttpResponse
    {
        internal GameRuntimeMetricsHttpResponse(GameRuntimeMetrics source)
        {
            GameDayTime = new ObservedMetricHttpResponse<string>(source.GameDayTime);
            IsBloodMoon = new ObservedMetricHttpResponse<bool?>(source.IsBloodMoon);
            FramesPerSecond = new ObservedMetricHttpResponse<double?>(source.FramesPerSecond);
            OnlinePlayerCount = new ObservedMetricHttpResponse<int?>(source.OnlinePlayerCount);
            HistoricalPlayerCount = new ObservedMetricHttpResponse<int?>(source.HistoricalPlayerCount);
            AnimalCount = new ObservedMetricHttpResponse<int?>(source.AnimalCount);
            HostileEntityCount = new ObservedMetricHttpResponse<int?>(source.HostileEntityCount);
            ActiveEntityCount = new ObservedMetricHttpResponse<int?>(source.ActiveEntityCount);
            ChunkCount = new ObservedMetricHttpResponse<int?>(source.ChunkCount);
            DroppedItemCount = new ObservedMetricHttpResponse<int?>(source.DroppedItemCount);
            GameMemoryBytes = new ObservedMetricHttpResponse<long?>(source.GameMemoryBytes);
        }

        public ObservedMetricHttpResponse<string> GameDayTime { get; }
        public ObservedMetricHttpResponse<bool?> IsBloodMoon { get; }
        public ObservedMetricHttpResponse<double?> FramesPerSecond { get; }
        public ObservedMetricHttpResponse<int?> OnlinePlayerCount { get; }
        public ObservedMetricHttpResponse<int?> HistoricalPlayerCount { get; }
        public ObservedMetricHttpResponse<int?> AnimalCount { get; }
        public ObservedMetricHttpResponse<int?> HostileEntityCount { get; }
        public ObservedMetricHttpResponse<int?> ActiveEntityCount { get; }
        public ObservedMetricHttpResponse<int?> ChunkCount { get; }
        public ObservedMetricHttpResponse<int?> DroppedItemCount { get; }
        public ObservedMetricHttpResponse<long?> GameMemoryBytes { get; }
    }

    public sealed class ObservedMetricHttpResponse<T>
    {
        internal ObservedMetricHttpResponse(ObservedMetric<T> source)
        {
            Value = source.Value;
            Source = source.Source;
            Unit = source.Unit;
            ObservedAtUtc = source.ObservedAtUtc;
            Warning = ToContract(source.Warning);
        }

        public T? Value { get; }
        public string Source { get; }
        public string Unit { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public string? Warning { get; }

        private static string? ToContract(RuntimeMetricWarningCode? warning)
        {
            switch (warning)
            {
                case RuntimeMetricWarningCode.ReadFailed:
                    return "readFailed";
                case RuntimeMetricWarningCode.Unsupported:
                    return "unsupported";
                case null:
                    return null;
                default:
                    throw new ArgumentOutOfRangeException(nameof(warning));
            }
        }
    }

    public sealed class HostOverviewHttpResponse
    {
        internal HostOverviewHttpResponse(HostOverviewSnapshot source, bool includeSensitive)
        {
            Availability = OverviewHttpResponse.ToContract(source.Availability);
            IdentityAvailability = OverviewHttpResponse.ToContract(source.IdentityAvailability);
            SampledAtUtc = source.SampledAtUtc;
            ProcessUptimeSeconds = source.ProcessUptimeSeconds;
            ResidentSetBytes = source.ResidentSetBytes;
            ManagedHeapBytes = source.ManagedHeapBytes;
            OtherMemoryBytes = source.OtherMemoryBytes;
            CpuUsagePercent = source.CpuUsagePercent;
            OperatingSystem = source.OperatingSystem;
            OperatingSystemVersion = source.OperatingSystemVersion;
            ProcessorCount = source.ProcessorCount;
            MemoryTotalBytes = source.MemoryTotalBytes;
            MemoryAvailableBytes = source.MemoryAvailableBytes;
            AdditionalMemory = HostAdditionalMemoryHttpResponse.From(source.AdditionalMemory);
            StorageVolumes = source.StorageVolumes
                .Select(volume => new HostStorageVolumeHttpResponse(volume, includeSensitive))
                .ToArray();
            PublicNetwork = new HostPublicNetworkHttpResponse(source.PublicNetwork, includeSensitive);
            DeviceId = includeSensitive ? source.DeviceId : null;
            CurrentSystemUser = includeSensitive ? source.CurrentSystemUser : null;
            OsFamily = source.OsFamily;
            OperatingSystemArchitecture = source.OperatingSystemArchitecture;
            RuntimeVersion = source.RuntimeVersion;
            CpuModel = source.CpuModel;
            LogicalCoreCount = source.LogicalCoreCount;
            CpuFrequencyMhz = source.CpuFrequencyMhz;
            DeviceName = source.DeviceName;
            DeviceModel = source.DeviceModel;
            DeviceType = source.DeviceType;
            ProcessId = source.ProcessId;
            ProcessStartedAtUtc = source.ProcessStartedAtUtc;
        }

        public string Availability { get; }
        public string IdentityAvailability { get; }
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
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public HostAdditionalMemoryHttpResponse? AdditionalMemory { get; }
        public IReadOnlyList<HostStorageVolumeHttpResponse> StorageVolumes { get; }
        public HostPublicNetworkHttpResponse PublicNetwork { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? DeviceId { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
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
    }

    public sealed class HostAdditionalMemoryHttpResponse
    {
        private HostAdditionalMemoryHttpResponse(HostAdditionalMemory source, string kind)
        {
            Kind = kind;
            TotalBytes = source.TotalBytes;
            UsedBytes = source.UsedBytes;
        }

        internal static HostAdditionalMemoryHttpResponse? From(HostAdditionalMemory? source)
        {
            if (source == null) return null;

            switch (source.Kind)
            {
                case HostAdditionalMemoryKind.WindowsVirtualAddressSpace:
                    return new HostAdditionalMemoryHttpResponse(source, "virtualAddressSpace");
                case HostAdditionalMemoryKind.LinuxSwap:
                    return new HostAdditionalMemoryHttpResponse(source, "swap");
                default:
                    return null;
            }
        }

        public string Kind { get; }
        public long? TotalBytes { get; }
        public long? UsedBytes { get; }
    }

    public sealed class HostStorageVolumeHttpResponse
    {
        internal HostStorageVolumeHttpResponse(HostStorageVolume source, bool includeSensitive)
        {
            Name = source.Name;
            RootPath = includeSensitive ? source.RootPath : null;
            TotalBytes = source.TotalBytes;
            AvailableBytes = source.AvailableBytes;
            IsPrimaryDataVolume = source.IsPrimaryDataVolume;
        }

        public string Name { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? RootPath { get; }

        public long? TotalBytes { get; }
        public long? AvailableBytes { get; }
        public bool? IsPrimaryDataVolume { get; }
    }

    public sealed class HostPublicNetworkHttpResponse
    {
        internal HostPublicNetworkHttpResponse(HostPublicNetwork source, bool includeSensitive)
        {
            Availability = OverviewHttpResponse.ToContract(source.Availability);
            Ipv4 = includeSensitive ? source.Ipv4 : null;
            Ipv6 = includeSensitive ? source.Ipv6 : null;
        }

        public string Availability { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Ipv4 { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Ipv6 { get; }
    }

    public sealed class RestartPolicyHttpResponse
    {
        internal RestartPolicyHttpResponse(RestartPolicySummary source)
        {
            Availability = OverviewHttpResponse.ToContract(source.Availability);
            IsConfigured = source.IsConfigured;
            ScheduleDescription = source.ScheduleDescription;
            NextRestartAtUtc = source.NextRestartAtUtc;
        }

        public string Availability { get; }
        public bool IsConfigured { get; }
        public string? ScheduleDescription { get; }
        public DateTimeOffset? NextRestartAtUtc { get; }
    }

    public sealed class RecentActivityHttpResponse
    {
        internal RecentActivityHttpResponse(RecentActivitySnapshot source)
        {
            Availability = OverviewHttpResponse.ToContract(source.Availability);
            SampledAtUtc = source.SampledAtUtc;
            TotalCount = source.TotalCount;
            LatestOccurredAtUtc = source.LatestOccurredAtUtc;
            Items = source.Items.Select(item => new RecentActivityItemHttpResponse(item)).ToArray();
        }

        public string Availability { get; }
        public DateTimeOffset? SampledAtUtc { get; }
        public int TotalCount { get; }
        public DateTimeOffset? LatestOccurredAtUtc { get; }
        public IReadOnlyList<RecentActivityItemHttpResponse> Items { get; }
    }

    public sealed class RecentActivityItemHttpResponse
    {
        internal RecentActivityItemHttpResponse(RecentActivityItem source)
        {
            OccurredAtUtc = source.OccurredAtUtc;
            MessageKey = source.MessageKey;
            MessageArguments = source.MessageArguments;
        }

        public DateTimeOffset OccurredAtUtc { get; }
        public string MessageKey { get; }
        public IReadOnlyDictionary<string, string> MessageArguments { get; }
    }

    public sealed class OverviewAttentionHttpResponse
    {
        public required string Code { get; init; }
    }
}
