using System;
using System.IO;

namespace LSTY.SevenDPanel.Hosting
{
    public sealed class RestartScriptOptions
    {
        public const string DefaultWindowsScript = "scripts/restart-server.cmd";
        public const string DefaultLinuxScript = "scripts/restart-server.sh";
        public const string DefaultWorkingDirectory = ".";

        private RestartScriptOptions(string windowsScript, string linuxScript, string workingDirectory)
        {
            WindowsScript = windowsScript;
            LinuxScript = linuxScript;
            WorkingDirectory = workingDirectory;
        }

        public string WindowsScript { get; }
        public string LinuxScript { get; }
        public string WorkingDirectory { get; }

        public static RestartScriptOptions CreateDefault(string dataDirectory)
        {
            return FromBinding(DefaultWindowsScript, DefaultLinuxScript, DefaultWorkingDirectory, dataDirectory);
        }

        public static RestartScriptOptions FromBinding(
            string? windowsScript,
            string? linuxScript,
            string? workingDirectory,
            string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
                throw new InvalidDataException("The panel data directory is required for restart scripts.");

            return new RestartScriptOptions(
                NormalizeScriptPath(
                    windowsScript,
                    dataDirectory,
                    "Windows restart script",
                    ".cmd",
                    StringComparison.OrdinalIgnoreCase,
                    "\"%&|<>^!()\r\n".ToCharArray()),
                NormalizeScriptPath(
                    linuxScript,
                    dataDirectory,
                    "Linux restart script",
                    ".sh",
                    StringComparison.Ordinal,
                    "\"'$`\r\n".ToCharArray()),
                NormalizePath(workingDirectory, dataDirectory, "Restart working directory"));
        }

        private static string NormalizeScriptPath(
            string? value,
            string dataDirectory,
            string label,
            string requiredExtension,
            StringComparison comparison,
            char[] unsupportedCharacters)
        {
            EnsureSupportedCharacters(value, label, unsupportedCharacters);
            var normalizedPath = NormalizePath(value, dataDirectory, label);
            EnsureSupportedCharacters(normalizedPath, label, unsupportedCharacters);
            if (!string.Equals(
                    Path.GetExtension(normalizedPath),
                    requiredExtension,
                    comparison))
            {
                throw new InvalidDataException(
                    label + " must use the " + requiredExtension + " extension.");
            }

            return normalizedPath;
        }

        private static void EnsureSupportedCharacters(
            string? value,
            string label,
            char[] unsupportedCharacters)
        {
            if ((value ?? string.Empty).IndexOfAny(unsupportedCharacters) >= 0)
                throw new InvalidDataException(label + " contains unsupported characters.");
        }

        private static string NormalizePath(string? value, string dataDirectory, string label)
        {
            try
            {
                var normalized = (value ?? string.Empty).Trim();
                if (normalized.Length == 0) throw new InvalidDataException(label + " is required.");
                var dataRoot = TrimTrailingDirectorySeparator(Path.GetFullPath(dataDirectory));
                var normalizedPath = Path.GetFullPath(Path.IsPathRooted(normalized)
                    ? normalized
                    : Path.Combine(dataRoot, normalized));
                if (!IsWithinDataDirectory(dataRoot, normalizedPath))
                    throw new InvalidDataException(label + " must be located under the panel data directory.");
                return normalizedPath;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is System.Security.SecurityException)
            {
                throw new InvalidDataException(label + " could not be normalized.", ex);
            }
        }

        private static bool IsWithinDataDirectory(string dataDirectory, string candidate)
        {
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var rootWithSeparator = dataDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                                    dataDirectory.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? dataDirectory
                : dataDirectory + Path.DirectorySeparatorChar;
            return candidate.Equals(dataDirectory, comparison) || candidate.StartsWith(rootWithSeparator, comparison);
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
