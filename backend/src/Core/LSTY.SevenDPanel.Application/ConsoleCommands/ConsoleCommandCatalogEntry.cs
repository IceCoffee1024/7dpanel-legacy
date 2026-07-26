using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application.ConsoleCommands
{
    public sealed class ConsoleCommandCatalogEntry
    {
        public ConsoleCommandCatalogEntry(
            string name,
            IEnumerable<string> aliases,
            string? description,
            string? help,
            int? permissionLevel)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A console command name is required.", nameof(name));

            Name = name;
            Aliases = (aliases ?? throw new ArgumentNullException(nameof(aliases))).ToArray();
            Description = description;
            Help = help;
            PermissionLevel = permissionLevel;
        }

        public string Name { get; }
        public IReadOnlyList<string> Aliases { get; }
        public string? Description { get; }
        public string? Help { get; }
        public int? PermissionLevel { get; }
    }

    public sealed class ConsoleCommandCatalog
    {
        public ConsoleCommandCatalog(
            DateTimeOffset capturedAtUtc,
            IEnumerable<ConsoleCommandCatalogEntry> commands)
        {
            CapturedAtUtc = capturedAtUtc;
            Commands = (commands ?? throw new ArgumentNullException(nameof(commands))).ToArray();
        }

        public DateTimeOffset CapturedAtUtc { get; }
        public IReadOnlyList<ConsoleCommandCatalogEntry> Commands { get; }
    }
}
