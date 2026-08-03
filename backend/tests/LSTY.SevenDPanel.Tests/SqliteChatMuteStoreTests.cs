using System;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application.Chat;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Community")]
    [Trait("Boundary", "Persistence")]
    public sealed class SqliteChatMuteStoreTests
    {
        [Fact]
        public void Store_writes_current_state_and_operation_atomically_then_expires_at_most_100_records()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            var store = new SqliteChatMuteStore(database.ConnectionFactory);
            var now = Utc(10);

            for (var index = 0; index < 101; index++)
            {
                var id = "EOS_" + index;
                var record = Mute(id, Utc(9));
                store.Create(record, Operation(ChatMuteOperationKind.Create, record, "owner", Utc(1)));
            }

            var snapshot = store.Expire(now, 500);

            Assert.Single(snapshot);
            Assert.Equal("EOS_99", Assert.Single(snapshot).CrossplatformId);
            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(201L, connection.QuerySingle<long>("SELECT COUNT(*) FROM chat_mute_operation;"));
            Assert.Equal(1L, connection.QuerySingle<long>("SELECT COUNT(*) FROM chat_mute;"));
            Assert.Equal(100L, connection.QuerySingle<long>("SELECT COUNT(*) FROM chat_mute_operation WHERE operation_kind = 'Expire' AND actor_subject IS NULL;"));
        }

        [Fact]
        public void Store_preserves_permanent_and_future_mutes_but_expires_exactly_at_the_cutoff()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            var store = new SqliteChatMuteStore(database.ConnectionFactory);
            var now = Utc(10);
            var permanent = Mute("EOS_PERMANENT", null);
            var future = Mute("EOS_FUTURE", Utc(11));
            var exact = Mute("EOS_EXACT", now);
            store.Create(permanent, Operation(ChatMuteOperationKind.Create, permanent, "owner", Utc(1)));
            store.Create(future, Operation(ChatMuteOperationKind.Create, future, "owner", Utc(1)));
            store.Create(exact, Operation(ChatMuteOperationKind.Create, exact, "owner", Utc(1)));

            var snapshot = store.Expire(now, 100);

            Assert.Equal(new[] { "EOS_FUTURE", "EOS_PERMANENT" }, snapshot.Select(record => record.CrossplatformId).OrderBy(id => id));
        }

        private static ChatMuteRecord Mute(string id, DateTimeOffset? until) =>
            new ChatMuteRecord(id, "Alice", "reason", until, "owner", Utc(1), "owner", Utc(1));

        private static ChatMuteOperation Operation(ChatMuteOperationKind kind, ChatMuteRecord record, string? actor, DateTimeOffset at) =>
            new ChatMuteOperation(Guid.NewGuid().ToString("D"), kind, record.CrossplatformId, actor, at, "Succeeded", null, record.MutedUntilUtc, record.Reason);

        private static DateTimeOffset Utc(int hour) => new DateTimeOffset(2026, 7, 26, hour, 0, 0, TimeSpan.Zero);

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Persistence")]

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(Path.GetTempPath(), "7dpanel-chat-mute-tests", Guid.NewGuid().ToString("N"));
            public TemporaryDatabase() => ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
            public SqliteConnectionFactory ConnectionFactory { get; }
            public void Upgrade() => new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
