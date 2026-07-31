using System;
using System.IO;
using LSTY.SevenDPanel.Application.Backups;

namespace LSTY.SevenDPanel.Adapters.Local.Restore
{
    public sealed class WorldRestoreTimingGate
    {
        public const string UnverifiedError = "world_restore_timing_unverified";

        private static readonly StringComparison PathComparison =
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private readonly IWorldRestoreRuntimeEvidenceSource evidenceSource;

        public WorldRestoreTimingGate()
            : this(new UnavailableEvidenceSource())
        {
        }

        public WorldRestoreTimingGate(IWorldRestoreRuntimeEvidenceSource evidenceSource) =>
            this.evidenceSource = evidenceSource ??
                throw new ArgumentNullException(nameof(evidenceSource));

        public bool IsApproved(
            string expectedWorldName,
            string expectedWorldDirectory,
            string gameVersion)
        {
            if (string.IsNullOrWhiteSpace(expectedWorldName))
                throw new ArgumentException("A world name is required.", nameof(expectedWorldName));
            if (string.IsNullOrWhiteSpace(expectedWorldDirectory) ||
                !Path.IsPathRooted(expectedWorldDirectory))
            {
                throw new ArgumentException(
                    "An absolute world directory is required.",
                    nameof(expectedWorldDirectory));
            }
            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new ArgumentException("A game version is required.", nameof(gameVersion));

            try
            {
                var evidence = evidenceSource.Capture();
                return evidence != null &&
                       evidence.IsMainThread &&
                       evidence.IsDedicatedServer &&
                       evidence.HasGameManager &&
                       !evidence.IsWorldOpen &&
                       string.Equals(
                           evidence.WorldName,
                           expectedWorldName,
                           StringComparison.Ordinal) &&
                       string.Equals(
                           evidence.GameVersion,
                           gameVersion,
                           StringComparison.Ordinal) &&
                       SamePath(evidence.WorldDirectory, expectedWorldDirectory);
            }
            catch
            {
                return false;
            }
        }

        private static bool SamePath(string? observed, string expected) =>
            !string.IsNullOrWhiteSpace(observed) &&
            string.Equals(
                TrimEndingSeparators(Path.GetFullPath(observed!)),
                TrimEndingSeparators(Path.GetFullPath(expected)),
                PathComparison);

        private static string TrimEndingSeparators(string path)
        {
            var root = Path.GetPathRoot(path);
            return string.Equals(path, root, PathComparison)
                ? path
                : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private sealed class UnavailableEvidenceSource : IWorldRestoreRuntimeEvidenceSource
        {
            public WorldRestoreRuntimeEvidence Capture() =>
                new WorldRestoreRuntimeEvidence(
                    false,
                    false,
                    false,
                    false,
                    null,
                    null,
                    null);
        }
    }
}
