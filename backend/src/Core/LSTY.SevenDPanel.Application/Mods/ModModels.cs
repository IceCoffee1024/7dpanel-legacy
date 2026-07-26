using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Mods
{
    public sealed class ModDiskEntry
    {
        public ModDiskEntry(
            string directoryId,
            string name,
            string displayName,
            string author,
            string version,
            string? website,
            string? description,
            bool isEnabledNextStart,
            bool isProtected)
        {
            DirectoryId = directoryId;
            Name = name;
            DisplayName = displayName;
            Author = author;
            Version = version;
            Website = website;
            Description = description;
            IsEnabledNextStart = isEnabledNextStart;
            IsProtected = isProtected;
        }

        public string DirectoryId { get; }
        public string Name { get; }
        public string DisplayName { get; }
        public string Author { get; }
        public string Version { get; }
        public string? Website { get; }
        public string? Description { get; }
        public bool IsEnabledNextStart { get; }
        public bool IsProtected { get; }
    }

    public sealed class ModView
    {
        public ModView(ModDiskEntry entry, bool? isLoadedNow)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            DirectoryId = entry.DirectoryId;
            Name = entry.Name;
            DisplayName = entry.DisplayName;
            Author = entry.Author;
            Version = entry.Version;
            Website = entry.Website;
            Description = entry.Description;
            IsLoadedNow = isLoadedNow;
            IsEnabledNextStart = entry.IsEnabledNextStart;
            IsProtected = entry.IsProtected;
        }

        public string DirectoryId { get; }
        public string Name { get; }
        public string DisplayName { get; }
        public string Author { get; }
        public string Version { get; }
        public string? Website { get; }
        public string? Description { get; }
        public bool? IsLoadedNow { get; }
        public bool IsEnabledNextStart { get; }
        public bool IsProtected { get; }
    }

    public sealed class LoadedModSnapshot
    {
        public LoadedModSnapshot(bool available, IEnumerable<string> names)
        {
            Available = available;
            Names = (names ?? throw new ArgumentNullException(nameof(names)))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
        }

        public bool Available { get; }
        public IReadOnlyCollection<string> Names { get; }

        public static LoadedModSnapshot Unavailable() =>
            new LoadedModSnapshot(false, Array.Empty<string>());
    }

    public enum ModStateChangeStatus
    {
        Changed,
        Unchanged,
        InvalidDirectory,
        NotFound,
        Protected,
        Conflict,
        Failed
    }

    public sealed class ModStateChangeResult
    {
        private ModStateChangeResult(ModStateChangeStatus status)
        {
            Status = status;
        }

        public ModStateChangeStatus Status { get; }

        public static ModStateChangeResult Changed() => new ModStateChangeResult(ModStateChangeStatus.Changed);
        public static ModStateChangeResult Unchanged() => new ModStateChangeResult(ModStateChangeStatus.Unchanged);
        public static ModStateChangeResult InvalidDirectory() => new ModStateChangeResult(ModStateChangeStatus.InvalidDirectory);
        public static ModStateChangeResult NotFound() => new ModStateChangeResult(ModStateChangeStatus.NotFound);
        public static ModStateChangeResult Protected() => new ModStateChangeResult(ModStateChangeStatus.Protected);
        public static ModStateChangeResult Conflict() => new ModStateChangeResult(ModStateChangeStatus.Conflict);
        public static ModStateChangeResult Failed() => new ModStateChangeResult(ModStateChangeStatus.Failed);
    }
}
