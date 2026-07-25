using System;
using System.Collections.Generic;
using System.IO;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Hosting.Platform
{
    public sealed class HostStorageSampler
    {
        public HostStorageSampler(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("The data directory is required.", nameof(dataDirectory));
            DataDirectory = Path.GetFullPath(dataDirectory);
        }

        public string DataDirectory { get; }

        public IEnumerable<HostStorageVolume> Sample(IHostPlatformAdapter platform)
        {
            return Sample(platform, HostPlatformFamily.Windows);
        }

        public IEnumerable<HostStorageVolume> Sample(IHostPlatformAdapter platform, HostPlatformFamily family)
        {
            if (platform == null) throw new ArgumentNullException(nameof(platform));
            var volumes = new List<HostStorageVolume>();
            try
            {
                foreach (var volume in platform.ReadStorageVolumes())
                {
                    try
                    {
                        if (!volume.IsFixed || volume.IsOverlay) continue;
                        volumes.Add(new HostStorageVolume(
                            volume.Name,
                            volume.RootPath,
                            volume.TotalBytes,
                            volume.AvailableBytes,
                            volume.IsPrimaryDataVolume ?? IsPrimaryDataVolume(volume.RootPath, family)));
                    }
                    catch (Exception)
                    {
                        try
                        {
                            volumes.Add(new HostStorageVolume(
                                volume.Name,
                                volume.RootPath,
                                null,
                                null,
                                volume.IsPrimaryDataVolume ?? IsPrimaryDataVolume(volume.RootPath, family)));
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return volumes;
        }

        private bool IsPrimaryDataVolume(string rootPath, HostPlatformFamily family)
        {
            try
            {
                var root = TrimTrailingDirectorySeparator(Path.GetFullPath(rootPath));
                var data = TrimTrailingDirectorySeparator(DataDirectory);
                var comparison = family == HostPlatformFamily.Windows
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                                        root.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? root
                    : root + Path.DirectorySeparatorChar;
                return data.Equals(root, comparison) || data.StartsWith(rootWithSeparator, comparison);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string TrimTrailingDirectorySeparator(string path)
        {
            var root = Path.GetPathRoot(path) ?? string.Empty;
            while (path.Length > root.Length &&
                   (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                    path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)))
            {
                path = path.Substring(0, path.Length - 1);
            }
            return path;
        }
    }
}
