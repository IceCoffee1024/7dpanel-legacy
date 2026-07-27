using System;
using System.IO;

namespace LSTY.SevenDPanel.Adapters.Local.Files
{
    public sealed class ApprovedStorageRoots
    {
        private static readonly StringComparison PathComparison =
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        public ApprovedStorageRoots(
            string currentWorldName,
            string currentWorldDirectory,
            string panelStateRoot,
            string serverConfigurationRoot,
            string backupRootId,
            string backupRoot,
            string gameVersion)
        {
            CurrentWorldName = RequireIdentifier(currentWorldName, nameof(currentWorldName));
            CurrentWorldDirectory = NormalizeRoot(currentWorldDirectory, nameof(currentWorldDirectory));
            PanelStateRoot = NormalizeRoot(panelStateRoot, nameof(panelStateRoot));
            ServerConfigurationRoot = NormalizeRoot(serverConfigurationRoot, nameof(serverConfigurationRoot));
            BackupRootId = RequireIdentifier(backupRootId, nameof(backupRootId));
            BackupRoot = NormalizeRoot(backupRoot, nameof(backupRoot));
            GameVersion = RequireIdentifier(gameVersion, nameof(gameVersion));
        }

        public string CurrentWorldName { get; }
        public string CurrentWorldDirectory { get; }
        public string PanelStateRoot { get; }
        public string ServerConfigurationRoot { get; }
        public string BackupRootId { get; }
        public string BackupRoot { get; }
        public string GameVersion { get; }

        public string RequireCurrentWorldDirectory(string worldName)
        {
            var normalized = RequireIdentifier(worldName, nameof(worldName));
            if (!string.Equals(normalized, CurrentWorldName, StringComparison.Ordinal))
                throw new ArgumentException("world_not_current", nameof(worldName));

            RejectReparsePoints(CurrentWorldDirectory, CurrentWorldDirectory);
            return CurrentWorldDirectory;
        }

        public string ResolveBackupResource(string relativeResourceId)
        {
            var relative = ValidateRelativePath(relativeResourceId, nameof(relativeResourceId));
            var fullPath = Path.GetFullPath(Path.Combine(
                BackupRoot,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            EnsureContained(BackupRoot, fullPath, nameof(relativeResourceId));
            RejectReparsePoints(BackupRoot, fullPath);
            return fullPath;
        }

        public void ValidatePanelStatePath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentException("A path is required.", nameof(fullPath));
            var canonical = Path.GetFullPath(fullPath);
            EnsureContained(PanelStateRoot, canonical, nameof(fullPath), allowRoot: true);
            RejectReparsePoints(PanelStateRoot, canonical);
        }

        public string NormalizeServerConfigurationRelativePath(string relativePath) =>
            ValidateRelativePath(relativePath, nameof(relativePath));

        public string ResolveServerConfigurationFile(string relativePath)
        {
            var relative = NormalizeServerConfigurationRelativePath(relativePath);
            var fullPath = Path.GetFullPath(Path.Combine(
                ServerConfigurationRoot,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            EnsureContained(ServerConfigurationRoot, fullPath, nameof(relativePath));
            RejectReparsePoints(ServerConfigurationRoot, fullPath);
            return fullPath;
        }

        public void ValidateCurrentWorldPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentException("A path is required.", nameof(fullPath));
            var canonical = Path.GetFullPath(fullPath);
            EnsureContained(CurrentWorldDirectory, canonical, nameof(fullPath), allowRoot: true);
            RejectReparsePoints(CurrentWorldDirectory, canonical);
        }

        private static string NormalizeRoot(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                throw new ArgumentException("approved_root_must_be_absolute", parameterName);
            return TrimEndingSeparators(Path.GetFullPath(path));
        }

        private static string RequireIdentifier(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty identifier is required.", parameterName);
            var normalized = value.Trim();
            if (Path.IsPathRooted(normalized) ||
                normalized.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                normalized.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
                normalized == "." || normalized == "..")
            {
                throw new ArgumentException("path_identifier_invalid", parameterName);
            }
            return normalized;
        }

        private static string ValidateRelativePath(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
                throw new ArgumentException("relative_path_required", parameterName);
            var normalized = value.Replace('\\', '/').Trim();
            if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.IndexOf(':') >= 0)
                throw new ArgumentException("relative_path_required", parameterName);
            var segments = normalized.Split('/');
            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                    throw new ArgumentException("path_traversal_not_allowed", parameterName);
            }
            return normalized;
        }

        private static void EnsureContained(
            string root,
            string fullPath,
            string parameterName,
            bool allowRoot = false)
        {
            if (allowRoot && string.Equals(root, fullPath, PathComparison)) return;
            var prefix = root + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, PathComparison))
                throw new ArgumentException("path_outside_approved_root", parameterName);
        }

        private static void RejectReparsePoints(string root, string fullPath)
        {
            CheckReparsePoint(root);
            if (string.Equals(root, fullPath, PathComparison)) return;

            var relative = fullPath.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = root;
            foreach (var segment in relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                CheckReparsePoint(current);
            }
        }

        private static void CheckReparsePoint(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("path_reparse_not_allowed");
        }

        private static string TrimEndingSeparators(string path)
        {
            var root = Path.GetPathRoot(path);
            if (string.Equals(path, root, PathComparison)) return path;
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
