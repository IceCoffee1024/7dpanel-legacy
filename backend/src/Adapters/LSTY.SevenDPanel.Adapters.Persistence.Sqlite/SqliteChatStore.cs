using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Chat;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteChatStore : IChatHistoryStore, IChatSettingsStore
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteChatStore(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public void Append(ChatMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.Sequence < 0) throw new ArgumentOutOfRangeException(nameof(message));
            ChatValidation.NormalizeMessage(message.Message);
            RequireUtc(message.OccurredAtUtc, nameof(message));

            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO chat_messages (
                      sequence, occurred_utc, entity_id, crossplatform_id, sender_name,
                      chat_type, source_kind, message)
                  VALUES (
                      @Sequence, @OccurredUtc, @EntityId, @CrossplatformId, @SenderName,
                      @ChatType, @SourceKind, @Message);",
                new
                {
                    message.Sequence,
                    OccurredUtc = message.OccurredAtUtc.ToUnixTimeMilliseconds(),
                    message.EntityId,
                    message.CrossplatformId,
                    message.SenderName,
                    ChatType = message.Channel.ToString(),
                    SourceKind = message.SourceKind.ToString(),
                    message.Message
                });
        }

        public void AppendGap(ChatHistoryGap gap)
        {
            if (gap == null) throw new ArgumentNullException(nameof(gap));
            RequireUtc(gap.StartedAtUtc, nameof(gap));
            RequireUtc(gap.EndedAtUtc, nameof(gap));
            if (gap.EndedAtUtc < gap.StartedAtUtc) throw new ArgumentException("A gap cannot end before it starts.", nameof(gap));
            if (gap.DroppedMessageCount <= 0) throw new ArgumentOutOfRangeException(nameof(gap));
            if (string.IsNullOrWhiteSpace(gap.Reason) || gap.Reason.Length > 64) throw new ArgumentException("A bounded gap reason is required.", nameof(gap));

            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO chat_history_gaps (started_utc, ended_utc, dropped_message_count, reason)
                  VALUES (@StartedUtc, @EndedUtc, @DroppedMessageCount, @Reason);",
                new
                {
                    StartedUtc = gap.StartedAtUtc.ToUnixTimeMilliseconds(),
                    EndedUtc = gap.EndedAtUtc.ToUnixTimeMilliseconds(),
                    gap.DroppedMessageCount,
                    Reason = gap.Reason.Trim()
                });
        }

        public ChatHistoryPage GetHistory(ChatHistoryQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var where = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("Take", query.PageSize + 1);
            if (query.CrossplatformId != null)
            {
                where.Add("crossplatform_id = @CrossplatformId");
                parameters.Add("CrossplatformId", query.CrossplatformId);
            }
            if (query.SenderName != null)
            {
                where.Add("sender_name LIKE @SenderName ESCAPE '\\'");
                parameters.Add("SenderName", "%" + EscapeLike(query.SenderName) + "%");
            }
            if (query.Channel.HasValue)
            {
                where.Add("chat_type = @ChatType");
                parameters.Add("ChatType", query.Channel.Value.ToString());
            }
            if (query.SourceKind.HasValue)
            {
                where.Add("source_kind = @SourceKind");
                parameters.Add("SourceKind", query.SourceKind.Value.ToString());
            }
            if (query.StartUtc.HasValue)
            {
                where.Add("occurred_utc >= @StartUtc");
                parameters.Add("StartUtc", query.StartUtc.Value.ToUnixTimeMilliseconds());
            }
            if (query.EndUtc.HasValue)
            {
                where.Add("occurred_utc <= @EndUtc");
                parameters.Add("EndUtc", query.EndUtc.Value.ToUnixTimeMilliseconds());
            }
            if (query.Keyset != null)
            {
                where.Add("(occurred_utc < @CursorUtc OR (occurred_utc = @CursorUtc AND id < @CursorId))");
                parameters.Add("CursorUtc", query.Keyset.OccurredAtUtc.ToUnixTimeMilliseconds());
                parameters.Add("CursorId", query.Keyset.RowId);
            }

            using var connection = connectionFactory.Open();
            var rows = connection.Query<MessageRow>(
                @"SELECT id AS Id, sequence AS Sequence, occurred_utc AS OccurredUtc,
                         entity_id AS EntityId, crossplatform_id AS CrossplatformId,
                         sender_name AS SenderName, chat_type AS ChatType,
                         source_kind AS SourceKind, message AS Message
                  FROM chat_messages" +
                (where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where)) +
                " ORDER BY occurred_utc DESC, id DESC LIMIT @Take;",
                parameters).ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            var next = rows.Length > query.PageSize && pageRows.Length > 0
                ? new ChatHistoryKeyset(FromUnixMilliseconds(pageRows[pageRows.Length - 1].OccurredUtc), pageRows[pageRows.Length - 1].Id)
                : null;
            if (pageRows.Length == 0)
                return new ChatHistoryPage(Array.Empty<ChatMessage>(), next, Array.Empty<ChatHistoryGap>());

            var oldest = pageRows.Min(row => row.OccurredUtc);
            var newest = query.Keyset == null
                ? pageRows.Max(row => row.OccurredUtc)
                : Math.Max(
                    pageRows.Max(row => row.OccurredUtc),
                    query.Keyset.OccurredAtUtc.ToUnixTimeMilliseconds());
            var gaps = connection.Query<GapRow>(
                @"SELECT started_utc AS StartedUtc, ended_utc AS EndedUtc,
                         dropped_message_count AS DroppedMessageCount, reason AS Reason
                  FROM chat_history_gaps
                  WHERE ended_utc >= @OldestUtc AND started_utc <= @NewestUtc
                  ORDER BY started_utc ASC, id ASC;",
                new { OldestUtc = oldest, NewestUtc = newest }).Select(ToGap).ToArray();
            return new ChatHistoryPage(pageRows.Select(ToMessage), next, gaps);
        }

        public int DeleteBefore(DateTimeOffset cutoffUtc, int maximumDeletes)
        {
            RequireUtc(cutoffUtc, nameof(cutoffUtc));
            if (maximumDeletes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDeletes));
            var take = Math.Min(maximumDeletes, 1000);
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"DELETE FROM chat_messages
                  WHERE id IN (
                      SELECT id FROM chat_messages
                      WHERE occurred_utc < @CutoffUtc
                      ORDER BY occurred_utc ASC, id ASC
                      LIMIT @Take
                  );",
                new { CutoffUtc = cutoffUtc.ToUnixTimeMilliseconds(), Take = take });
        }

        public ChatSettings Get()
        {
            using var connection = connectionFactory.Open();
            return ToSettings(connection.QuerySingle<SettingsRow>(SettingsSelect));
        }

        public ChatSettings Save(ChatSettings settings)
        {
            var normalized = ChatValidation.Normalize(settings);
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"UPDATE chat_settings SET
                      is_enabled = @IsEnabled,
                      global_server_name = @GlobalServerName,
                      whisper_server_name = @WhisperServerName,
                      command_prefixes = @CommandPrefixes,
                      exclude_commands_from_history = @ExcludeCommandsFromHistory,
                      history_retention_days = @HistoryRetentionDays
                  WHERE singleton_id = 1;",
                new
                {
                    IsEnabled = normalized.IsEnabled ? 1 : 0,
                    normalized.GlobalServerName,
                    normalized.WhisperServerName,
                    CommandPrefixes = string.Concat(normalized.CommandPrefixes),
                    ExcludeCommandsFromHistory = normalized.ExcludeCommandsFromHistory ? 1 : 0,
                    normalized.HistoryRetentionDays
                });
            return Get();
        }

        public ChatSettings Reset()
        {
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"UPDATE chat_settings SET is_enabled = 1, global_server_name = NULL,
                      whisper_server_name = NULL, command_prefixes = '/',
                      exclude_commands_from_history = 1, history_retention_days = 30
                  WHERE singleton_id = 1;");
            return Get();
        }

        private const string SettingsSelect = @"SELECT is_enabled AS IsEnabled,
            global_server_name AS GlobalServerName, whisper_server_name AS WhisperServerName,
            command_prefixes AS CommandPrefixes,
            exclude_commands_from_history AS ExcludeCommandsFromHistory,
            history_retention_days AS HistoryRetentionDays
            FROM chat_settings WHERE singleton_id = 1;";

        private static ChatSettings ToSettings(SettingsRow row) => new ChatSettings
        {
            IsEnabled = row.IsEnabled != 0,
            GlobalServerName = row.GlobalServerName,
            WhisperServerName = row.WhisperServerName,
            CommandPrefixes = row.CommandPrefixes.Select(value => value.ToString()).ToArray(),
            ExcludeCommandsFromHistory = row.ExcludeCommandsFromHistory != 0,
            HistoryRetentionDays = row.HistoryRetentionDays
        };

        private static ChatMessage ToMessage(MessageRow row) => new ChatMessage
        {
            Sequence = row.Sequence,
            OccurredAtUtc = FromUnixMilliseconds(row.OccurredUtc),
            EntityId = row.EntityId,
            CrossplatformId = row.CrossplatformId,
            SenderName = row.SenderName,
            Channel = (ChatChannel)Enum.Parse(typeof(ChatChannel), row.ChatType, ignoreCase: false),
            SourceKind = (ChatSourceKind)Enum.Parse(typeof(ChatSourceKind), row.SourceKind, ignoreCase: false),
            Message = row.Message
        };

        private static ChatHistoryGap ToGap(GapRow row) => new ChatHistoryGap
        {
            StartedAtUtc = FromUnixMilliseconds(row.StartedUtc),
            EndedAtUtc = FromUnixMilliseconds(row.EndedUtc),
            DroppedMessageCount = row.DroppedMessageCount,
            Reason = row.Reason
        };

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero) throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        private static DateTimeOffset FromUnixMilliseconds(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
        private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        private sealed class MessageRow
        {
            public long Id { get; set; }
            public long Sequence { get; set; }
            public long OccurredUtc { get; set; }
            public int EntityId { get; set; }
            public string? CrossplatformId { get; set; }
            public string SenderName { get; set; } = string.Empty;
            public string ChatType { get; set; } = string.Empty;
            public string SourceKind { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
        }

        private sealed class GapRow
        {
            public long StartedUtc { get; set; }
            public long EndedUtc { get; set; }
            public long DroppedMessageCount { get; set; }
            public string Reason { get; set; } = string.Empty;
        }

        private sealed class SettingsRow
        {
            public int IsEnabled { get; set; }
            public string? GlobalServerName { get; set; }
            public string? WhisperServerName { get; set; }
            public string CommandPrefixes { get; set; } = string.Empty;
            public int ExcludeCommandsFromHistory { get; set; }
            public int HistoryRetentionDays { get; set; }
        }
    }
}
