namespace LSTY.SevenDPanel.Application
{
    public sealed class HostStorageVolume
    {
        public HostStorageVolume(string name, string? rootPath, long? totalBytes, long? availableBytes)
            : this(name, rootPath, totalBytes, availableBytes, null)
        {
        }

        public HostStorageVolume(
            string name,
            string? rootPath,
            long? totalBytes,
            long? availableBytes,
            bool? isPrimaryDataVolume)
        {
            Name = name;
            RootPath = rootPath;
            TotalBytes = totalBytes;
            AvailableBytes = availableBytes;
            IsPrimaryDataVolume = isPrimaryDataVolume;
        }

        public string Name { get; }
        public string? RootPath { get; }
        public long? TotalBytes { get; }
        public long? AvailableBytes { get; }

        public bool? IsPrimaryDataVolume { get; }

        public HostStorageVolume WithoutRootPath() => new HostStorageVolume(
            Name,
            null,
            TotalBytes,
            AvailableBytes,
            IsPrimaryDataVolume);
    }
}
