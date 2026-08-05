using System;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Community;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Community
{
    internal static class GameCommandConfigurationPersistence
    {
        internal static CommunityGameCommandConfiguration Read(
            SqliteConnection connection,
            SqliteTransaction? transaction)
        {
            var metadata = connection.QuerySingleOrDefault<SqliteCommunityStore.GameCommandConfigurationRow>(
                @"SELECT updated_at_utc AS UpdatedAtUtc, row_version AS RowVersion
                  FROM community_game_command_configurations WHERE configuration_id = 1;",
                transaction: transaction) ?? throw new CommunityNotFoundException();
            var tokens = connection.Query<SqliteCommunityStore.GameCommandTokenRow>(
                    SqliteCommunityStore.GameCommandTokenSelect +
                    " ORDER BY command_id ASC, is_primary DESC, sort_order ASC;",
                    transaction: transaction)
                .ToArray();
            var settings = Enum.GetValues(typeof(CommunityGameCommandId))
                .Cast<CommunityGameCommandId>()
                .Select(commandId =>
                {
                    var rows = tokens
                        .Where(row => string.Equals(
                            row.CommandId,
                            commandId.ToString(),
                            StringComparison.Ordinal))
                        .ToArray();
                    var primary = rows.Single(row => row.IsPrimary == 1);
                    return new CommunityGameCommandSetting(
                        commandId,
                        primary.Token,
                        rows.Where(row => row.IsPrimary == 0)
                            .OrderBy(row => row.SortOrder)
                            .Select(row => row.Token));
                })
                .ToArray();
            return new CommunityGameCommandConfiguration(
                settings,
                DateTimeOffset.FromUnixTimeMilliseconds(metadata.UpdatedAtUtc),
                metadata.RowVersion);
        }

        internal static void ReplaceTokens(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CommunityGameCommandConfiguration configuration)
        {
            connection.Execute("DELETE FROM community_game_command_tokens;", transaction: transaction);
            var rows = configuration.Commands.SelectMany(command =>
                new[]
                {
                    new
                    {
                        Token = command.Name,
                        CommandId = command.CommandId.ToString(),
                        IsPrimary = 1,
                        SortOrder = 0
                    }
                }.Concat(command.Aliases.Select((alias, index) => new
                {
                    Token = alias,
                    CommandId = command.CommandId.ToString(),
                    IsPrimary = 0,
                    SortOrder = index
                }))).ToArray();
            connection.Execute(
                @"INSERT INTO community_game_command_tokens
                      (token, command_id, is_primary, sort_order)
                  VALUES (@Token, @CommandId, @IsPrimary, @SortOrder);",
                rows,
                transaction);
        }

        internal static CommunityGameCommandConfiguration WithHomeExperience(
            CommunityGameCommandConfiguration current,
            HomeTeleportExperience home,
            DateTimeOffset updatedAtUtc) =>
            new CommunityGameCommandConfiguration(
                current.Commands.Select(command => command.CommandId switch
                {
                    CommunityGameCommandId.Homes => Setting(command.CommandId, home.ListCommandName),
                    CommunityGameCommandId.SetHome => Setting(command.CommandId, home.SetCommandName),
                    CommunityGameCommandId.DeleteHome => Setting(command.CommandId, home.DeleteCommandName),
                    CommunityGameCommandId.Home => Setting(command.CommandId, home.TeleportCommandName),
                    _ => command
                }),
                updatedAtUtc,
                current.RowVersion);

        internal static SqliteCommunityStore.HomeCommandNames HomeExperience(
            CommunityGameCommandConfiguration configuration) =>
            new SqliteCommunityStore.HomeCommandNames(
                configuration.Get(CommunityGameCommandId.Homes).Name,
                configuration.Get(CommunityGameCommandId.SetHome).Name,
                configuration.Get(CommunityGameCommandId.DeleteHome).Name,
                configuration.Get(CommunityGameCommandId.Home).Name);

        internal static bool HomeCommandNamesChanged(
            CommunityGameCommandConfiguration first,
            CommunityGameCommandConfiguration second)
        {
            var firstHome = HomeExperience(first);
            var secondHome = HomeExperience(second);
            return !string.Equals(firstHome.ListCommandName, secondHome.ListCommandName, StringComparison.Ordinal) ||
                   !string.Equals(firstHome.SetCommandName, secondHome.SetCommandName, StringComparison.Ordinal) ||
                   !string.Equals(firstHome.DeleteCommandName, secondHome.DeleteCommandName, StringComparison.Ordinal) ||
                   !string.Equals(firstHome.TeleportCommandName, secondHome.TeleportCommandName, StringComparison.Ordinal);
        }

        private static CommunityGameCommandSetting Setting(
            CommunityGameCommandId commandId,
            string name) =>
            new CommunityGameCommandSetting(commandId, name, Array.Empty<string>());
    }
}
