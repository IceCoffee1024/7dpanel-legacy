using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources
{
    internal sealed class GameResourceIconIndex
    {
        private static readonly StringComparer IconNameComparer =
            StringComparer.OrdinalIgnoreCase;

        private readonly IReadOnlyDictionary<string, GameResourceIndexedIcon> iconsByResourceId;

        private GameResourceIconIndex(
            IEnumerable<GameResourceIndexedResource> resources,
            IDictionary<string, GameResourceIndexedIcon> iconsByResourceId,
            IEnumerable<string> warnings)
        {
            Resources = new ReadOnlyCollection<GameResourceIndexedResource>(resources.ToArray());
            this.iconsByResourceId = new ReadOnlyDictionary<string, GameResourceIndexedIcon>(
                new Dictionary<string, GameResourceIndexedIcon>(
                    iconsByResourceId,
                    StringComparer.Ordinal));
            Warnings = new ReadOnlyCollection<string>(warnings.ToArray());
        }

        public IReadOnlyList<GameResourceIndexedResource> Resources { get; }

        public IReadOnlyList<string> Warnings { get; }

        public bool TryGetIcon(string resourceId, out GameResourceIndexedIcon icon) =>
            iconsByResourceId.TryGetValue(resourceId, out icon!);

        public static GameResourceIconIndex Build(
            IEnumerable<GameResourceScalarEntry> resources,
            IEnumerable<GameResourceIconRootDescriptor> roots,
            CancellationToken cancellationToken)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            cancellationToken.ThrowIfCancellationRequested();

            var copiedResources = resources.ToArray();
            var orderedRoots = roots.OrderBy(root => root.Precedence).ToArray();
            if (orderedRoots.GroupBy(root => root.Precedence).Any(group => group.Count() != 1))
            {
                throw new InvalidOperationException(
                    "Icon root precedence values must be unique.");
            }

            var warnings = new List<string>();
            var filesByIconName = new Dictionary<string, GameResourceIndexedIcon>(IconNameComparer);
            var ambiguousIconNames = new HashSet<string>(IconNameComparer);
            foreach (var descriptor in orderedRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryNormalizeRoot(descriptor.RootPath, out var canonicalRoot, out var rejected))
                {
                    warnings.Add(rejected ? "icon-root-rejected" : "icon-root-unavailable");
                    continue;
                }

                Dictionary<string, GameResourceIndexedIcon> rootFiles;
                HashSet<string> rootAmbiguous;
                try
                {
                    rootFiles = new Dictionary<string, GameResourceIndexedIcon>(IconNameComparer);
                    rootAmbiguous = new HashSet<string>(IconNameComparer);
                    foreach (var file in Directory
                                 .GetFiles(canonicalRoot, "*", SearchOption.TopDirectoryOnly)
                                 .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!TryIndexFile(canonicalRoot, file, out var iconName, out var indexedIcon))
                            continue;

                        if (rootFiles.ContainsKey(iconName))
                        {
                            rootFiles.Remove(iconName);
                            rootAmbiguous.Add(iconName);
                            continue;
                        }

                        if (!rootAmbiguous.Contains(iconName))
                            rootFiles.Add(iconName, indexedIcon);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (IsFileSystemException(exception))
                {
                    warnings.Add("icon-root-unavailable");
                    continue;
                }

                foreach (var ambiguous in rootAmbiguous)
                {
                    filesByIconName.Remove(ambiguous);
                    ambiguousIconNames.Add(ambiguous);
                }

                foreach (var pair in rootFiles)
                {
                    filesByIconName[pair.Key] = pair.Value;
                    ambiguousIconNames.Remove(pair.Key);
                }
            }

            var indexedResources = new List<GameResourceIndexedResource>(copiedResources.Length);
            var iconsByResourceId = new Dictionary<string, GameResourceIndexedIcon>(
                StringComparer.Ordinal);
            foreach (var resource in copiedResources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resourceId = CreateResourceId();
                GameResourceIconStatus status;
                if (!IsValidIconLeaf(resource.IconName))
                {
                    status = GameResourceIconStatus.Invalid;
                }
                else if (ambiguousIconNames.Contains(resource.IconName!))
                {
                    status = GameResourceIconStatus.Invalid;
                    warnings.Add("icon-name-ambiguous");
                }
                else if (filesByIconName.TryGetValue(resource.IconName!, out var icon))
                {
                    status = GameResourceIconStatus.Available;
                    iconsByResourceId.Add(resourceId, icon);
                }
                else
                {
                    status = GameResourceIconStatus.Missing;
                }

                indexedResources.Add(new GameResourceIndexedResource(
                    resource,
                    resourceId,
                    status));
            }

            return new GameResourceIconIndex(indexedResources, iconsByResourceId, warnings);
        }

        internal static bool IsValidIconLeaf(string? iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName) || iconName!.Length > 128)
                return false;
            if (iconName.IndexOf('/') >= 0 ||
                iconName.IndexOf('\\') >= 0 ||
                iconName.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                iconName.IndexOf(Path.VolumeSeparatorChar) >= 0)
            {
                return false;
            }

            foreach (var character in iconName)
            {
                if (char.IsControl(character)) return false;
            }

            return string.Equals(Path.GetFileName(iconName), iconName, StringComparison.Ordinal);
        }

        internal static bool IsSafeIndexedFile(GameResourceIndexedIcon icon)
        {
            if (icon == null) return false;
            if (!TryNormalizeRoot(icon.CanonicalRoot, out var currentRoot, out _) ||
                !PathEquals(currentRoot, icon.CanonicalRoot))
            {
                return false;
            }

            try
            {
                var currentPath = Path.GetFullPath(icon.CanonicalPath);
                if (!PathEquals(currentPath, icon.CanonicalPath) ||
                    !IsDirectChild(currentRoot, currentPath) ||
                    !string.Equals(Path.GetExtension(currentPath), ".png", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var attributes = File.GetAttributes(currentPath);
                return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                return false;
            }
        }

        private static bool TryIndexFile(
            string canonicalRoot,
            string path,
            out string iconName,
            out GameResourceIndexedIcon indexedIcon)
        {
            iconName = string.Empty;
            indexedIcon = null!;
            try
            {
                var canonicalPath = Path.GetFullPath(path);
                if (!IsDirectChild(canonicalRoot, canonicalPath) ||
                    !string.Equals(Path.GetExtension(canonicalPath), ".png", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var attributes = File.GetAttributes(canonicalPath);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    return false;

                iconName = Path.GetFileNameWithoutExtension(canonicalPath);
                if (!IsValidIconLeaf(iconName)) return false;

                var info = new FileInfo(canonicalPath);
                if (!info.Exists) return false;
                indexedIcon = new GameResourceIndexedIcon(
                    canonicalRoot,
                    canonicalPath,
                    info.Length,
                    info.LastWriteTimeUtc.Ticks,
                    info.CreationTimeUtc.Ticks);
                return true;
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                return false;
            }
        }

        private static bool TryNormalizeRoot(
            string path,
            out string canonicalRoot,
            out bool rejected)
        {
            canonicalRoot = string.Empty;
            rejected = false;
            try
            {
                canonicalRoot = Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!Directory.Exists(canonicalRoot)) return false;
                if (HasReparsePointInPath(canonicalRoot))
                {
                    rejected = true;
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                return false;
            }
        }

        private static bool HasReparsePointInPath(string path)
        {
            var pathRoot = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(pathRoot)) return true;

            var current = pathRoot;
            var relative = path.Substring(pathRoot.Length);
            foreach (var segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    return true;
            }

            return false;
        }

        private static bool IsDirectChild(string root, string candidate)
        {
            var parent = Path.GetDirectoryName(candidate);
            return parent != null && PathEquals(parent, root);
        }

        private static bool PathEquals(string left, string right) =>
            string.Equals(left, right, PathComparison);

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static string CreateResourceId()
        {
            var bytes = new byte[24];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static bool IsFileSystemException(Exception exception) =>
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is ArgumentException ||
            exception is NotSupportedException ||
            exception is System.Security.SecurityException;
    }

    internal sealed class GameResourceIndexedResource
    {
        public GameResourceIndexedResource(
            GameResourceScalarEntry scalar,
            string resourceId,
            GameResourceIconStatus iconStatus)
        {
            Scalar = scalar ?? throw new ArgumentNullException(nameof(scalar));
            ResourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
            IconStatus = iconStatus;
        }

        public GameResourceScalarEntry Scalar { get; }
        public string ResourceId { get; }
        public GameResourceIconStatus IconStatus { get; }
    }

    internal sealed class GameResourceIndexedIcon
    {
        public GameResourceIndexedIcon(
            string canonicalRoot,
            string canonicalPath,
            long length,
            long lastWriteTimeUtcTicks,
            long creationTimeUtcTicks)
        {
            CanonicalRoot = canonicalRoot;
            CanonicalPath = canonicalPath;
            Length = length;
            LastWriteTimeUtcTicks = lastWriteTimeUtcTicks;
            CreationTimeUtcTicks = creationTimeUtcTicks;
        }

        public string CanonicalRoot { get; }
        public string CanonicalPath { get; }
        public long Length { get; }
        public long LastWriteTimeUtcTicks { get; }
        public long CreationTimeUtcTicks { get; }
    }
}
