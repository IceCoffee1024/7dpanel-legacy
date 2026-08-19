using System;
using System.IO;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application.Chat;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Community")]
    [Trait("Boundary", "Persistence")]
    public sealed class SqliteChatStoreTests
    {
        [Fact]
        public void Migration_is_recorded_once_and_enables_wal()
        {
            using var database = new TemporaryChatDatabase();
            database.Upgrade();
            database.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal("wal", connection.ExecuteScalar<string>("PRAGMA journal_mode;"), ignoreCase: true);
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName LIKE '%Migrations.007_GameChat.sql';"));
            Assert.Equal(5, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('chat_messages', 'chat_history_gaps', 'chat_settings', 'colored_chat_settings', 'colored_chat_profiles');"));
        }

        [Fact]
        public void History_uses_stable_descending_keyset_and_returns_overlapping_gaps()
        {
            using var database = new TemporaryChatDatabase();
            database.Upgrade();
            var store = new SqliteChatStore(database.ConnectionFactory);
            var first = Utc(0);
            store.Append(Message(1, first, "x-1", "Alpha", ChatChannel.Global, ChatSourceKind.Player, "one"));
            store.Append(Message(2, first.AddMinutes(1), "x-2", "Beta", ChatChannel.Party, ChatSourceKind.Administrator, "two"));
            store.Append(Message(3, first.AddMinutes(1), "x-3", "Gamma", ChatChannel.Whisper, ChatSourceKind.System, "three"));
            store.AppendGap(new ChatHistoryGap
            {
                StartedAtUtc = first.AddSeconds(30),
                EndedAtUtc = first.AddMinutes(2),
                DroppedMessageCount = 2,
                Reason = "queue_full"
            });

            var page1 = store.GetHistory(Query(pageSize: 2));
            Assert.Equal(new[] { "three", "two" }, page1.Messages.Select(message => message.Message));
            Assert.NotNull(page1.NextKeyset);
            Assert.Single(page1.Gaps);

            var page2 = store.GetHistory(Query(pageSize: 2, keyset: page1.NextKeyset));
            Assert.Equal(new[] { "one" }, page2.Messages.Select(message => message.Message));
            Assert.Null(page2.NextKeyset);
            Assert.Single(page2.Gaps);
        }

        [Fact]
        public void History_applies_every_supported_filter_and_returns_empty_pages()
        {
            using var database = new TemporaryChatDatabase();
            database.Upgrade();
            var store = new SqliteChatStore(database.ConnectionFactory);
            var time = Utc(3);
            store.Append(Message(10, time, "steam_123", "Some Player", ChatChannel.Friends, ChatSourceKind.Administrator, "match"));
            store.Append(Message(11, time.AddMinutes(1), "other", "Other", ChatChannel.Global, ChatSourceKind.Player, "other"));

            var result = store.GetHistory(new ChatHistoryQuery(
                10,
                "steam_123",
                "Some",
                ChatChannel.Friends,
                ChatSourceKind.Administrator,
                time,
                time,
                null));
            Assert.Equal("match", Assert.Single(result.Messages).Message);

            var empty = store.GetHistory(new ChatHistoryQuery(
                10, "missing", null, null, null, null, null, null));
            Assert.Empty(empty.Messages);
            Assert.Empty(empty.Gaps);
            Assert.Null(empty.NextKeyset);
        }

        [Fact]
        public void History_cleanup_is_bounded_and_zero_retention_can_skip_the_store()
        {
            using var database = new TemporaryChatDatabase();
            database.Upgrade();
            var store = new SqliteChatStore(database.ConnectionFactory);
            var cutoff = Utc(5);
            store.Append(Message(1, cutoff.AddDays(-3), null, "Old 1", ChatChannel.Global, ChatSourceKind.System, "old-1"));
            store.Append(Message(2, cutoff.AddDays(-2), null, "Old 2", ChatChannel.Global, ChatSourceKind.System, "old-2"));
            store.Append(Message(3, cutoff, null, "Current", ChatChannel.Global, ChatSourceKind.System, "current"));

            Assert.Equal(1, store.DeleteBefore(cutoff, 1));
            Assert.Equal(1, store.DeleteBefore(cutoff, 10));
            Assert.Equal("current", Assert.Single(store.GetHistory(Query(10)).Messages).Message);
        }

        [Fact]
        public void Chat_settings_have_defaults_round_trip_normalized_values_and_reset()
        {
            using var database = new TemporaryChatDatabase();
            database.Upgrade();
            var store = new SqliteChatStore(database.ConnectionFactory);

            var defaults = store.Get();
            Assert.True(defaults.IsEnabled);
            Assert.Equal(new[] { "/" }, defaults.CommandPrefixes);
            Assert.Equal(30, defaults.HistoryRetentionDays);

            var saved = store.Save(new ChatSettings
            {
                IsEnabled = false,
                GlobalServerName = " Server ",
                WhisperServerName = " Whisper ",
                CommandPrefixes = new[] { "!", "!", "/" },
                ExcludeCommandsFromHistory = false,
                HistoryRetentionDays = 0
            });
            Assert.Equal("Server", saved.GlobalServerName);
            Assert.Equal(new[] { "!", "/" }, store.Get().CommandPrefixes);
            Assert.Equal(0, store.Get().HistoryRetentionDays);

            Assert.Equal(new[] { "/" }, store.Reset().CommandPrefixes);
            Assert.True(store.Get().ExcludeCommandsFromHistory);
        }

        [Fact]
        public void Colored_settings_and_profiles_round_trip_filter_page_update_and_delete()
        {
            using var database = new TemporaryChatDatabase();
            database.Upgrade();
            var store = new SqliteColoredChatStore(database.ConnectionFactory);
            Assert.False(store.GetSettings().IsEnabled);
            Assert.Equal(PlayerColorTagPermission.None, store.GetSettings().PlayerColorTagPermission);

            var saved = store.SaveSettings(new ColoredChatSettings
            {
                IsEnabled = true,
                GlobalDefaultColor = "aabbcc",
                WhisperDefaultColor = null,
                FriendsDefaultColor = "112233",
                PartyDefaultColor = null,
                AdminDefaultColor = "445566",
                SystemDefaultColor = "778899",
                PlayerColorTagPermission = PlayerColorTagPermission.AdminOnly
            });
            Assert.Equal("AABBCC", saved.GlobalDefaultColor);
            Assert.Equal(PlayerColorTagPermission.AdminOnly, store.GetSettings().PlayerColorTagPermission);

            var created = Profile("id-1", Utc(1), Utc(3), " Alpha ", "abcdef", "123456");
            Assert.True(store.TryCreateProfile(created));
            Assert.False(store.TryCreateProfile(created));
            Assert.True(store.TryCreateProfile(Profile("id-2", Utc(2), Utc(4), "Beta", null, null)));

            var firstPage = store.GetProfiles(new ColoredChatProfileQuery(1, null, null, null, null, null, null, null));
            Assert.Equal("id-2", Assert.Single(firstPage.Profiles).CrossplatformId);
            Assert.NotNull(firstPage.NextKeyset);
            var secondPage = store.GetProfiles(new ColoredChatProfileQuery(1, null, null, null, null, null, null, firstPage.NextKeyset));
            Assert.Equal("id-1", Assert.Single(secondPage.Profiles).CrossplatformId);

            var filtered = store.GetProfiles(new ColoredChatProfileQuery(10, "id-1", "Alp", "ABCDEF", "123456", Utc(1), Utc(1), null));
            Assert.Equal("Alpha", Assert.Single(filtered.Profiles).CustomName);
            Assert.Equal(2, store.GetAllProfiles().Count);

            var updated = Profile("id-1", Utc(1), Utc(5), "Updated", null, "654321");
            Assert.True(store.TryUpdateProfile(updated));
            Assert.False(store.TryUpdateProfile(Profile("missing", Utc(1), Utc(5), null, null, null)));
            Assert.Equal("Updated", store.GetAllProfiles().Single(profile => profile.CrossplatformId == "id-1").CustomName);
            Assert.True(store.TryDeleteProfile("id-1"));
            Assert.False(store.TryDeleteProfile("id-1"));
            Assert.Single(store.GetAllProfiles());

            Assert.False(store.ResetSettings().IsEnabled);
        }

        [Fact]
        public void Direct_colored_settings_save_normalizes_values_without_application_use_case()
        {
            using var database = new TemporaryChatDatabase();
            database.Upgrade();
            var store = new SqliteColoredChatStore(database.ConnectionFactory);

            var saved = store.SaveSettings(new ColoredChatSettings
            {
                IsEnabled = true,
                GlobalDefaultColor = " aabbcc ",
                WhisperDefaultColor = "  ",
                FriendsDefaultColor = "112233 ",
                PartyDefaultColor = " 445566",
                AdminDefaultColor = "778899",
                SystemDefaultColor = "abcdef",
                PlayerColorTagPermission = PlayerColorTagPermission.AdminOnly
            });

            Assert.Equal("AABBCC", saved.GlobalDefaultColor);
            Assert.Null(saved.WhisperDefaultColor);
            Assert.Equal("112233", saved.FriendsDefaultColor);
            Assert.Equal("445566", saved.PartyDefaultColor);
            Assert.Equal("778899", saved.AdminDefaultColor);
            Assert.Equal("ABCDEF", saved.SystemDefaultColor);
            Assert.Equal(saved.GlobalDefaultColor, store.GetSettings().GlobalDefaultColor);
        }

        private static ChatHistoryQuery Query(int pageSize, ChatHistoryKeyset? keyset = null) =>
            new ChatHistoryQuery(pageSize, null, null, null, null, null, null, keyset);

        private static ChatMessage Message(long sequence, DateTimeOffset occurredAtUtc, string? id, string name, ChatChannel channel, ChatSourceKind source, string text) =>
            new ChatMessage
            {
                Sequence = sequence,
                OccurredAtUtc = occurredAtUtc,
                EntityId = 1,
                CrossplatformId = id,
                SenderName = name,
                Channel = channel,
                SourceKind = source,
                Message = text
            };

        private static ColoredChatProfile Profile(string id, DateTimeOffset created, DateTimeOffset updated, string? name, string? nameColor, string? textColor) =>
            new ColoredChatProfile
            {
                CrossplatformId = id,
                CustomName = name,
                NameColor = nameColor,
                TextColor = textColor,
                Description = "note",
                CreatedAtUtc = created,
                UpdatedAtUtc = updated
            };

        private static DateTimeOffset Utc(int day) =>
            new DateTimeOffset(2026, 7, 1 + day, 0, 0, 0, TimeSpan.Zero);

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Persistence")]

        private sealed class TemporaryChatDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(Path.GetTempPath(), "7dpanel-chat-tests", Guid.NewGuid().ToString("N"));

            public TemporaryChatDatabase() =>
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Upgrade() => new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }
}
