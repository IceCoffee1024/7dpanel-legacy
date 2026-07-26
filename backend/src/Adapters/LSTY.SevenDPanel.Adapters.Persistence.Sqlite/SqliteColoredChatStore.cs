using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Chat;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteColoredChatStore : IColoredChatStore
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteColoredChatStore(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public ColoredChatSettings GetSettings()
        {
            using var connection = connectionFactory.Open();
            return ToSettings(connection.QuerySingle<SettingsRow>(SettingsSelect));
        }

        public ColoredChatSettings SaveSettings(ColoredChatSettings settings)
        {
            var normalized = ChatValidation.Normalize(settings);
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"UPDATE colored_chat_settings SET
                      is_enabled = @IsEnabled,
                      global_default_color = @GlobalDefaultColor,
                      whisper_default_color = @WhisperDefaultColor,
                      friends_default_color = @FriendsDefaultColor,
                      party_default_color = @PartyDefaultColor,
                      admin_default_color = @AdminDefaultColor,
                      system_default_color = @SystemDefaultColor,
                      player_color_tag_permission = @PlayerColorTagPermission
                  WHERE singleton_id = 1;",
                new
                {
                    IsEnabled = normalized.IsEnabled ? 1 : 0,
                    normalized.GlobalDefaultColor,
                    normalized.WhisperDefaultColor,
                    normalized.FriendsDefaultColor,
                    normalized.PartyDefaultColor,
                    normalized.AdminDefaultColor,
                    normalized.SystemDefaultColor,
                    PlayerColorTagPermission = normalized.PlayerColorTagPermission.ToString()
                });
            return GetSettings();
        }

        public ColoredChatSettings ResetSettings()
        {
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"UPDATE colored_chat_settings SET is_enabled = 0,
                      global_default_color = NULL, whisper_default_color = NULL,
                      friends_default_color = NULL, party_default_color = NULL,
                      admin_default_color = NULL, system_default_color = NULL,
                      player_color_tag_permission = 'None'
                  WHERE singleton_id = 1;");
            return GetSettings();
        }

        public ColoredChatProfilePage GetProfiles(ColoredChatProfileQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var where = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("Take", query.PageSize + 1);
            if (query.CrossplatformId != null)
            {
                where.Add("crossplatform_id LIKE @CrossplatformId ESCAPE '\\'");
                parameters.Add("CrossplatformId", "%" + EscapeLike(query.CrossplatformId) + "%");
            }
            if (query.CustomName != null)
            {
                where.Add("custom_name LIKE @CustomName ESCAPE '\\'");
                parameters.Add("CustomName", "%" + EscapeLike(query.CustomName) + "%");
            }
            if (query.NameColor != null)
            {
                where.Add("name_color = @NameColor");
                parameters.Add("NameColor", query.NameColor);
            }
            if (query.TextColor != null)
            {
                where.Add("text_color = @TextColor");
                parameters.Add("TextColor", query.TextColor);
            }
            if (query.CreatedAfterUtc.HasValue)
            {
                where.Add("created_utc >= @CreatedAfterUtc");
                parameters.Add("CreatedAfterUtc", query.CreatedAfterUtc.Value.ToUnixTimeMilliseconds());
            }
            if (query.CreatedBeforeUtc.HasValue)
            {
                where.Add("created_utc <= @CreatedBeforeUtc");
                parameters.Add("CreatedBeforeUtc", query.CreatedBeforeUtc.Value.ToUnixTimeMilliseconds());
            }
            if (query.Keyset != null)
            {
                where.Add("(updated_utc < @CursorUpdatedUtc OR (updated_utc = @CursorUpdatedUtc AND crossplatform_id > @CursorCrossplatformId))");
                parameters.Add("CursorUpdatedUtc", query.Keyset.UpdatedAtUtc.ToUnixTimeMilliseconds());
                parameters.Add("CursorCrossplatformId", query.Keyset.CrossplatformId);
            }

            using var connection = connectionFactory.Open();
            var rows = connection.Query<ProfileRow>(
                ProfileSelect +
                (where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where)) +
                " ORDER BY updated_utc DESC, crossplatform_id ASC LIMIT @Take;",
                parameters).ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            var next = rows.Length > query.PageSize && pageRows.Length > 0
                ? new ColoredChatProfileKeyset(
                    FromUnixMilliseconds(pageRows[pageRows.Length - 1].UpdatedUtc),
                    pageRows[pageRows.Length - 1].CrossplatformId)
                : null;
            return new ColoredChatProfilePage(pageRows.Select(ToProfile), next);
        }

        public IReadOnlyList<ColoredChatProfile> GetAllProfiles()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<ProfileRow>(
                ProfileSelect + " ORDER BY updated_utc DESC, crossplatform_id ASC;")
                .Select(ToProfile).ToArray();
        }

        public bool TryCreateProfile(ColoredChatProfile profile)
        {
            var normalized = ChatValidation.Normalize(profile);
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"INSERT OR IGNORE INTO colored_chat_profiles (
                      crossplatform_id, custom_name, name_color, text_color, description,
                      created_utc, updated_utc)
                  VALUES (
                      @CrossplatformId, @CustomName, @NameColor, @TextColor, @Description,
                      @CreatedUtc, @UpdatedUtc);",
                ProfileParameters(normalized)) == 1;
        }

        public bool TryUpdateProfile(ColoredChatProfile profile)
        {
            var normalized = ChatValidation.Normalize(profile);
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE colored_chat_profiles SET
                      custom_name = @CustomName,
                      name_color = @NameColor,
                      text_color = @TextColor,
                      description = @Description,
                      created_utc = @CreatedUtc,
                      updated_utc = @UpdatedUtc
                  WHERE crossplatform_id = @CrossplatformId;",
                ProfileParameters(normalized)) == 1;
        }

        public bool TryDeleteProfile(string crossplatformId)
        {
            var key = RequireBusinessKey(crossplatformId);
            using var connection = connectionFactory.Open();
            return connection.Execute(
                "DELETE FROM colored_chat_profiles WHERE crossplatform_id = @CrossplatformId;",
                new { CrossplatformId = key }) == 1;
        }

        private const string SettingsSelect = @"SELECT is_enabled AS IsEnabled,
            global_default_color AS GlobalDefaultColor,
            whisper_default_color AS WhisperDefaultColor,
            friends_default_color AS FriendsDefaultColor,
            party_default_color AS PartyDefaultColor,
            admin_default_color AS AdminDefaultColor,
            system_default_color AS SystemDefaultColor,
            player_color_tag_permission AS PlayerColorTagPermission
            FROM colored_chat_settings WHERE singleton_id = 1;";

        private const string ProfileSelect = @"SELECT crossplatform_id AS CrossplatformId,
            custom_name AS CustomName, name_color AS NameColor, text_color AS TextColor,
            description AS Description, created_utc AS CreatedUtc, updated_utc AS UpdatedUtc
            FROM colored_chat_profiles";

        private static ColoredChatSettings ToSettings(SettingsRow row) => new ColoredChatSettings
        {
            IsEnabled = row.IsEnabled != 0,
            GlobalDefaultColor = row.GlobalDefaultColor,
            WhisperDefaultColor = row.WhisperDefaultColor,
            FriendsDefaultColor = row.FriendsDefaultColor,
            PartyDefaultColor = row.PartyDefaultColor,
            AdminDefaultColor = row.AdminDefaultColor,
            SystemDefaultColor = row.SystemDefaultColor,
            PlayerColorTagPermission = (PlayerColorTagPermission)Enum.Parse(
                typeof(PlayerColorTagPermission), row.PlayerColorTagPermission, ignoreCase: false)
        };

        private static ColoredChatProfile ToProfile(ProfileRow row) => new ColoredChatProfile
        {
            CrossplatformId = row.CrossplatformId,
            CustomName = row.CustomName,
            NameColor = row.NameColor,
            TextColor = row.TextColor,
            Description = row.Description,
            CreatedAtUtc = FromUnixMilliseconds(row.CreatedUtc),
            UpdatedAtUtc = FromUnixMilliseconds(row.UpdatedUtc)
        };

        private static object ProfileParameters(ColoredChatProfile profile) => new
        {
            profile.CrossplatformId,
            profile.CustomName,
            profile.NameColor,
            profile.TextColor,
            profile.Description,
            CreatedUtc = profile.CreatedAtUtc.ToUnixTimeMilliseconds(),
            UpdatedUtc = profile.UpdatedAtUtc.ToUnixTimeMilliseconds()
        };

        private static string RequireBusinessKey(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace))
                throw new ArgumentException("A non-empty business key without whitespace is required.", nameof(value));
            return value;
        }

        private static DateTimeOffset FromUnixMilliseconds(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
        private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        private sealed class SettingsRow
        {
            public int IsEnabled { get; set; }
            public string? GlobalDefaultColor { get; set; }
            public string? WhisperDefaultColor { get; set; }
            public string? FriendsDefaultColor { get; set; }
            public string? PartyDefaultColor { get; set; }
            public string? AdminDefaultColor { get; set; }
            public string? SystemDefaultColor { get; set; }
            public string PlayerColorTagPermission { get; set; } = string.Empty;
        }

        private sealed class ProfileRow
        {
            public string CrossplatformId { get; set; } = string.Empty;
            public string? CustomName { get; set; }
            public string? NameColor { get; set; }
            public string? TextColor { get; set; }
            public string? Description { get; set; }
            public long CreatedUtc { get; set; }
            public long UpdatedUtc { get; set; }
        }
    }
}
