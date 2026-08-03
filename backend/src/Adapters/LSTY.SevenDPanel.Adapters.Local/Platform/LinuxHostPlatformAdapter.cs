using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LSTY.SevenDPanel.Adapters.Local.Platform
{
    public sealed class LinuxHostPlatformAdapter : IHostPlatformAdapter
    {
        private readonly Func<string, string?> readFile;

        public LinuxHostPlatformAdapter(Func<string, string?>? readFile = null)
        {
            this.readFile = readFile ?? ReadOptionalFile;
        }

        public HostPlatformInfo ReadPlatformInfo()
        {
            using (var process = Process.GetCurrentProcess())
            {
                return new HostPlatformInfo(
                    HostPlatformFamily.Linux,
                    "Linux",
                    ReadOperatingSystemVersion(),
                    Environment.ProcessorCount,
                    Environment.UserName,
                    ReadFirstLine("/etc/machine-id"),
                    GetUptimeSeconds(process),
                    GetLong(() => process.WorkingSet64),
                    GetManagedHeapBytes(),
                    null,
                    "linux",
                    GetArchitecture(),
                    Environment.Version.ToString(),
                    ReadCpuInfoValue("model name"),
                    Environment.ProcessorCount,
                    ReadCpuFrequencyMhz(),
                    Environment.MachineName,
                    ReadFirstLine("/sys/class/dmi/id/product_name"),
                    "linux-host",
                    GetInt(() => process.Id),
                    GetProcessStartedAtUtc(process));
            }
        }

        public HostCpuCounters ReadCpuCounters()
        {
            var line = (readFile("/proc/stat") ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(value => value.StartsWith("cpu ", StringComparison.Ordinal));
            if (line == null) throw new InvalidOperationException("/proc/stat does not contain aggregate CPU counters.");
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) throw new InvalidOperationException("/proc/stat aggregate CPU counters are incomplete.");
            long total = 0L;
            for (var index = 1; index < parts.Length; index++) total += ParseLong(parts[index]);
            var idle = ParseLong(parts[4]);
            if (parts.Length > 5) idle += ParseLong(parts[5]);
            return new HostCpuCounters(total, idle);
        }

        public HostMemorySample ReadMemory()
        {
            var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in (readFile("/proc/meminfo") ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = line.Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (pieces.Length >= 2) values[pieces[0]] = ParseLong(pieces[1]) * 1024L;
            }
            var total = GetValue(values, "MemTotal");
            var available = GetValue(values, "MemAvailable") ?? Add(GetValue(values, "MemFree"), GetValue(values, "Buffers"), GetValue(values, "Cached"));
            return new HostMemorySample(total, available, GetValue(values, "SwapTotal"), GetValue(values, "SwapFree"));
        }

        public IEnumerable<IHostStorageVolumeSource> ReadStorageVolumes()
        {
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in (readFile("/proc/mounts") ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3 || parts[2].Equals("overlay", StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsFixedFileSystem(parts[0], parts[2])) continue;
                IHostStorageVolumeSource? volume = null;
                try
                {
                    var root = DecodeMountPath(parts[1]);
                    if (roots.Add(root)) volume = new MountVolumeSource(parts[0], root, false);
                }
                catch (Exception)
                {
                }
                if (volume != null) yield return volume;
            }
        }

        private string ReadOperatingSystemVersion()
        {
            var prettyName = (readFile("/etc/os-release") ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal));
            return prettyName == null ? Environment.OSVersion.VersionString : prettyName.Substring("PRETTY_NAME=".Length).Trim('"');
        }

        private string? ReadCpuInfoValue(string key)
        {
            var prefix = key + ":";
            var line = (readFile("/proc/cpuinfo") ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return line == null ? null : line.Substring(prefix.Length).Trim();
        }

        private double? ReadCpuFrequencyMhz()
        {
            var value = ReadCpuInfoValue("cpu MHz");
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mhz) ? mhz : (double?)null;
        }

        private string? ReadFirstLine(string path)
        {
            var value = readFile(path);
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value!.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        }

        private static string? ReadOptionalFile(string path)
        {
            try { return File.ReadAllText(path); }
            catch (Exception) { return null; }
        }

        private static bool IsFixedFileSystem(string source, string type)
        {
            if (!source.StartsWith("/", StringComparison.Ordinal)) return false;
            if (source.StartsWith("/dev/loop", StringComparison.OrdinalIgnoreCase)) return false;
            return !type.Equals("tmpfs", StringComparison.OrdinalIgnoreCase) &&
                   !type.Equals("devtmpfs", StringComparison.OrdinalIgnoreCase) &&
                   !type.Equals("squashfs", StringComparison.OrdinalIgnoreCase) &&
                   !type.Equals("proc", StringComparison.OrdinalIgnoreCase) &&
                   !type.Equals("sysfs", StringComparison.OrdinalIgnoreCase) &&
                   !type.Equals("cgroup", StringComparison.OrdinalIgnoreCase);
        }

        private static string DecodeMountPath(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] == '\\' && index + 3 < value.Length &&
                    value[index + 1] >= '0' && value[index + 1] <= '7' &&
                    value[index + 2] >= '0' && value[index + 2] <= '7' &&
                    value[index + 3] >= '0' && value[index + 3] <= '7')
                {
                    var character = (char)((value[index + 1] - '0') * 64 +
                                          (value[index + 2] - '0') * 8 +
                                          (value[index + 3] - '0'));
                    builder.Append(character);
                    index += 3;
                }
                else
                {
                    builder.Append(value[index]);
                }
            }
            return builder.ToString();
        }

        private static long ParseLong(string value)
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                throw new InvalidOperationException("Host counter is not an integer.");
            return parsed;
        }

        private static long? GetValue(IDictionary<string, long> values, string key) => values.TryGetValue(key, out var value) ? value : (long?)null;
        private static long? Add(params long?[] values) => values.All(value => value.HasValue) ? values.Sum(value => value!.Value) : (long?)null;
        private static long? GetUptimeSeconds(Process process)
        {
            try { return Math.Max(0L, (long)(DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds); }
            catch (Exception) { return null; }
        }
        private static DateTimeOffset? GetProcessStartedAtUtc(Process process)
        {
            try { return new DateTimeOffset(process.StartTime.ToUniversalTime()); }
            catch (Exception) { return null; }
        }
        private static int? GetInt(Func<int> value)
        {
            try { return value(); }
            catch (Exception) { return null; }
        }
        private static long? GetLong(Func<long> value)
        {
            try { return value(); }
            catch (Exception) { return null; }
        }
        private static long GetManagedHeapBytes()
        {
            try { return GC.GetTotalMemory(false); }
            catch (Exception) { return 0L; }
        }
        private static string GetArchitecture() => Environment.Is64BitOperatingSystem ? "x64" : "x86";

        private sealed class MountVolumeSource : IHostStorageVolumeSource
        {
            private readonly DriveInfo? drive;
            public MountVolumeSource(string name, string rootPath, bool isOverlay)
            {
                Name = name;
                RootPath = rootPath;
                IsOverlay = isOverlay;
                try { drive = new DriveInfo(rootPath); }
                catch (Exception) { drive = null; }
            }
            public string Name { get; }
            public string RootPath { get; }
            public bool IsFixed => true;
            public bool IsOverlay { get; }
            public long TotalBytes => drive == null ? throw new IOException("Mount metadata is unavailable.") : drive.TotalSize;
            public long AvailableBytes => drive == null ? throw new IOException("Mount metadata is unavailable.") : drive.AvailableFreeSpace;
            public bool? IsPrimaryDataVolume => null;
        }
    }
}
