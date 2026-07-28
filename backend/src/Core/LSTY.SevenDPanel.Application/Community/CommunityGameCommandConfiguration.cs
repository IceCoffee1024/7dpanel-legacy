using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Community
{
    public sealed class CommunityGameCommandSetting
    {
        public CommunityGameCommandSetting(
            CommunityGameCommandId commandId,
            string name,
            IEnumerable<string> aliases)
        {
            if (!Enum.IsDefined(typeof(CommunityGameCommandId), commandId))
                throw new ArgumentOutOfRangeException(nameof(commandId));
            CommandId = commandId;
            Name = RequireToken(name, nameof(name));
            Aliases = (aliases ?? throw new ArgumentNullException(nameof(aliases)))
                .Select(alias => RequireToken(alias, nameof(aliases)))
                .ToArray();
        }

        public CommunityGameCommandId CommandId { get; }
        public string Name { get; }
        public IReadOnlyList<string> Aliases { get; }

        private static string RequireToken(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A command name or alias is required.", parameterName);
            var normalized = value.Trim();
            if (normalized.Any(char.IsWhiteSpace))
                throw new ArgumentException("Command names and aliases cannot contain whitespace.", parameterName);
            return normalized;
        }
    }

    public sealed class CommunityGameCommandConfiguration
    {
        private readonly IReadOnlyDictionary<CommunityGameCommandId, CommunityGameCommandSetting> byId;

        public CommunityGameCommandConfiguration(
            IEnumerable<CommunityGameCommandSetting> commands,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (updatedAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", nameof(updatedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));

            var values = commands.ToArray();
            var expectedIds = Enum.GetValues(typeof(CommunityGameCommandId))
                .Cast<CommunityGameCommandId>()
                .ToArray();
            if (values.Length != expectedIds.Length ||
                values.Select(value => value.CommandId).Distinct().Count() != expectedIds.Length)
            {
                throw new ArgumentException("Every Community command must be configured exactly once.", nameof(commands));
            }

            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "help" };
            foreach (var command in values)
            {
                if (!tokens.Add(command.Name) || command.Aliases.Any(alias => !tokens.Add(alias)))
                    throw new ArgumentException("Community command names and aliases must be globally unique.", nameof(commands));
            }

            Commands = expectedIds
                .Select(id => values.Single(value => value.CommandId == id))
                .ToArray();
            byId = Commands.ToDictionary(value => value.CommandId);
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
        }

        public IReadOnlyList<CommunityGameCommandSetting> Commands { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }

        public CommunityGameCommandSetting Get(CommunityGameCommandId commandId) =>
            byId.TryGetValue(commandId, out var setting)
                ? setting
                : throw new ArgumentOutOfRangeException(nameof(commandId));

        public static CommunityGameCommandConfiguration Default(DateTimeOffset updatedAtUtc) =>
            new CommunityGameCommandConfiguration(
                CommunityGameCommandDirectory.Definitions.Select(definition =>
                    new CommunityGameCommandSetting(definition.Id, definition.Name, definition.Aliases)),
                updatedAtUtc,
                0);
    }
}
