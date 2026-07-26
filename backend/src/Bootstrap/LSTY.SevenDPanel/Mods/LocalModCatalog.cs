using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using LSTY.SevenDPanel.Application.Mods;

namespace LSTY.SevenDPanel.Mods
{
    public sealed class LocalModCatalog : IModCatalog
    {
        private const string EnabledMarker = "ModInfo.xml";
        private const string DisabledMarker = "_ModInfo.xml";

        private readonly string rootPath;
        private readonly HashSet<string> protectedDirectories;

        public LocalModCatalog(string rootPath, IEnumerable<string> protectedDirectories)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("A Mods root directory is required.", nameof(rootPath));

            this.rootPath = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            this.protectedDirectories = new HashSet<string>(
                protectedDirectories ?? throw new ArgumentNullException(nameof(protectedDirectories)),
                StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<ModDiskEntry> List()
        {
            if (!Directory.Exists(rootPath))
                return Array.Empty<ModDiskEntry>();

            var result = new List<ModDiskEntry>();
            foreach (var directory in Directory.GetDirectories(rootPath).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                        continue;

                    var directoryId = Path.GetFileName(directory);
                    var enabledPath = Path.Combine(directory, EnabledMarker);
                    var disabledPath = Path.Combine(directory, DisabledMarker);
                    var enabledExists = File.Exists(enabledPath);
                    var disabledExists = File.Exists(disabledPath);
                    if (enabledExists == disabledExists)
                        continue;

                    var metadata = ReadMetadata(enabledExists ? enabledPath : disabledPath);
                    if (metadata == null || string.IsNullOrWhiteSpace(metadata.Name))
                        continue;

                    var name = metadata.Name!;
                    result.Add(new ModDiskEntry(
                        directoryId,
                        name,
                        string.IsNullOrWhiteSpace(metadata.DisplayName) ? name : metadata.DisplayName!,
                        metadata.Author ?? string.Empty,
                        metadata.Version ?? string.Empty,
                        metadata.Website,
                        metadata.Description,
                        enabledExists,
                        protectedDirectories.Contains(directoryId)));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (XmlException) { }
            }

            return result;
        }

        public ModStateChangeResult SetEnabled(string directoryId, bool enabled)
        {
            if (!TryResolveChild(directoryId, out var directory))
                return ModStateChangeResult.InvalidDirectory();
            if (!Directory.Exists(directory))
                return ModStateChangeResult.NotFound();

            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    return ModStateChangeResult.InvalidDirectory();
                if (protectedDirectories.Contains(directoryId))
                    return ModStateChangeResult.Protected();

                var enabledPath = Path.Combine(directory, EnabledMarker);
                var disabledPath = Path.Combine(directory, DisabledMarker);
                var enabledExists = File.Exists(enabledPath);
                var disabledExists = File.Exists(disabledPath);
                if (enabledExists && disabledExists)
                    return ModStateChangeResult.Conflict();
                if (!enabledExists && !disabledExists)
                    return ModStateChangeResult.NotFound();
                if (enabled == enabledExists)
                    return ModStateChangeResult.Unchanged();

                File.Move(enabled ? disabledPath : enabledPath, enabled ? enabledPath : disabledPath);
                return ModStateChangeResult.Changed();
            }
            catch (IOException) { return ModStateChangeResult.Failed(); }
            catch (UnauthorizedAccessException) { return ModStateChangeResult.Failed(); }
        }

        private bool TryResolveChild(string directoryId, out string directory)
        {
            directory = string.Empty;
            if (string.IsNullOrWhiteSpace(directoryId)
                || directoryId == "."
                || directoryId == ".."
                || directoryId.IndexOf(Path.DirectorySeparatorChar) >= 0
                || directoryId.IndexOf(Path.AltDirectorySeparatorChar) >= 0
                || Path.IsPathRooted(directoryId)
                || !string.Equals(Path.GetFileName(directoryId), directoryId, StringComparison.Ordinal))
                return false;

            var candidate = Path.GetFullPath(Path.Combine(rootPath, directoryId));
            var prefix = rootPath + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            directory = candidate;
            return true;
        }

        private static Metadata? ReadMetadata(string path)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(path, settings))
                document.Load(reader);

            var root = document.DocumentElement;
            if (root == null)
                return null;

            return new Metadata(
                Value(root, "Name"),
                Value(root, "DisplayName"),
                Value(root, "Author"),
                Value(root, "Version"),
                Value(root, "Website"),
                Value(root, "Description"));
        }

        private static string? Value(XmlElement root, string elementName)
        {
            var element = root.ChildNodes
                .OfType<XmlElement>()
                .FirstOrDefault(child => string.Equals(child.Name, elementName, StringComparison.Ordinal));
            return element?.GetAttribute("value");
        }

        private sealed class Metadata
        {
            public Metadata(string? name, string? displayName, string? author, string? version, string? website, string? description)
            {
                Name = name;
                DisplayName = displayName;
                Author = author;
                Version = version;
                Website = website;
                Description = description;
            }

            public string? Name { get; }
            public string? DisplayName { get; }
            public string? Author { get; }
            public string? Version { get; }
            public string? Website { get; }
            public string? Description { get; }
        }
    }
}
