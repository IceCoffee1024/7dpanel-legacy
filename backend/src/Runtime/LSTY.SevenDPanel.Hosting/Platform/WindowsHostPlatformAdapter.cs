using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LSTY.SevenDPanel.Hosting.Platform
{
    public sealed class WindowsHostPlatformAdapter : IHostPlatformAdapter
    {
        public HostPlatformInfo ReadPlatformInfo()
        {
            using (var process = Process.GetCurrentProcess())
            {
                return new HostPlatformInfo(
                    HostPlatformFamily.Windows,
                    "Windows",
                    Environment.OSVersion.VersionString,
                    Environment.ProcessorCount,
                    Environment.UserName,
                    ReadMachineGuid(),
                    GetUptimeSeconds(process),
                    GetLong(() => process.WorkingSet64),
                    GetManagedHeapBytes(),
                    null,
                    "windows",
                    GetArchitecture(),
                    Environment.Version.ToString(),
                    ReadProcessorValue("ProcessorNameString"),
                    Environment.ProcessorCount,
                    ReadCpuFrequencyMhz(),
                    Environment.MachineName,
                    ReadDeviceModel(),
                    "windows-host",
                    GetInt(() => process.Id),
                    GetProcessStartedAtUtc(process));
            }
        }

        public HostCpuCounters ReadCpuCounters()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
                throw new InvalidOperationException("GetSystemTimes failed.");
            var idleTicks = ToUInt64(idle);
            var totalTicks = ToUInt64(kernel) + ToUInt64(user);
            return new HostCpuCounters(totalTicks > long.MaxValue ? long.MaxValue : (long)totalTicks, idleTicks > long.MaxValue ? long.MaxValue : (long)idleTicks);
        }

        public HostMemorySample ReadMemory()
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx)) };
            if (!GlobalMemoryStatusEx(ref status)) throw new InvalidOperationException("GlobalMemoryStatusEx failed.");
            return new HostMemorySample(ToLong(status.TotalPhys), ToLong(status.AvailPhys), ToLong(status.TotalVirtual), ToLong(status.AvailVirtual));
        }

        public IEnumerable<IHostStorageVolumeSource> ReadStorageVolumes()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Fixed) yield return new DriveVolumeSource(drive, false);
            }
        }

        private static string? ReadMachineGuid()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Cryptography"))
                {
                    return key?.GetValue("MachineGuid") as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

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

        private static string? ReadProcessorValue(string valueName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0"))
                {
                    return key?.GetValue(valueName)?.ToString();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static double? ReadCpuFrequencyMhz()
        {
            var value = ReadProcessorValue("~MHz");
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mhz) ? mhz : (double?)null;
        }

        private static string? ReadDeviceModel()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\SystemInformation"))
                {
                    return key?.GetValue("SystemProductName") as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static ulong ToUInt64(FileTime value) => ((ulong)value.HighDateTime << 32) | value.LowDateTime;
        private static long? ToLong(ulong value) => value > long.MaxValue ? null : (long)value;

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime { public uint LowDateTime; public uint HighDateTime; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        private sealed class DriveVolumeSource : IHostStorageVolumeSource
        {
            private readonly DriveInfo drive;
            public DriveVolumeSource(DriveInfo drive, bool isOverlay) { this.drive = drive; IsOverlay = isOverlay; }
            public string Name => drive.Name;
            public string RootPath => drive.RootDirectory.FullName;
            public bool IsFixed => drive.DriveType == DriveType.Fixed;
            public bool IsOverlay { get; }
            public long TotalBytes => drive.TotalSize;
            public long AvailableBytes => drive.AvailableFreeSpace;
            public bool? IsPrimaryDataVolume => null;
        }
    }
}
