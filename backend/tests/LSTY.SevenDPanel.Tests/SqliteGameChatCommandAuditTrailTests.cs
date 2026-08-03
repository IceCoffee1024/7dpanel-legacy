using System;
using System.IO;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application.Chat;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Community")]
    [Trait("Boundary", "Persistence")]
    public sealed class SqliteGameChatCommandAuditTrailTests
    {
        [Fact]
        public void Completion_updates_the_pending_intent_without_creating_a_second_record()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            var store = new SqliteGameChatCommandAuditTrail(database.ConnectionFactory);

            var auditId = store.Begin(Intent("claim"));
            Assert.Equal("pending", ReadResult(database.ConnectionFactory, auditId));

            store.Complete(auditId, new GameChatCommandAuditCompletion(
                "community.command.daily.succeeded",
                true));

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                "community.command.daily.succeeded",
                connection.ExecuteScalar<string>(
                    "SELECT result FROM chat_operation_audit WHERE id = @AuditId;",
                    new { AuditId = auditId }));
            Assert.Equal(
                1,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM chat_operation_audit WHERE id = @AuditId;",
                    new { AuditId = auditId }));
            Assert.Throws<InvalidOperationException>(() => store.Complete(
                auditId,
                new GameChatCommandAuditCompletion("chat.command.failed", true)));
        }

        [Fact]
        public void Write_lock_failures_do_not_forge_a_terminal_result_and_the_store_recovers()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            using var shortTimeoutFactory = new SqliteConnectionFactory(
                database.DatabasePath,
                defaultTimeoutSeconds: 1);
            var store = new SqliteGameChatCommandAuditTrail(shortTimeoutFactory);

            using (var lockConnection = database.ConnectionFactory.Open())
            using (var transaction = lockConnection.BeginTransaction(deferred: false))
            {
                Assert.Throws<SqliteException>(() => store.Begin(Intent("locked")));
                transaction.Rollback();
            }

            var auditId = store.Begin(Intent("claim"));
            using (var lockConnection = database.ConnectionFactory.Open())
            using (var transaction = lockConnection.BeginTransaction(deferred: false))
            {
                Assert.Throws<SqliteException>(() => store.Complete(
                    auditId,
                    new GameChatCommandAuditCompletion("community.command.daily.succeeded", true)));
                transaction.Rollback();
            }

            Assert.Equal("pending", ReadResult(database.ConnectionFactory, auditId));
            store.Complete(auditId, new GameChatCommandAuditCompletion(
                "community.command.daily.succeeded",
                true));
            Assert.Equal(
                "community.command.daily.succeeded",
                ReadResult(database.ConnectionFactory, auditId));
        }

        private static GameChatCommandAuditIntent Intent(string invokedName) =>
            new GameChatCommandAuditIntent(
                "player:EOS_1",
                "DailyReward",
                invokedName,
                new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero));

        private static string ReadResult(SqliteConnectionFactory factory, long auditId)
        {
            using var connection = factory.Open();
            return connection.ExecuteScalar<string>(
                    "SELECT result FROM chat_operation_audit WHERE id = @AuditId;",
                    new { AuditId = auditId })
                ?? throw new InvalidOperationException("The game chat command audit intent was not found.");
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Persistence")]

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-game-chat-command-audit-tests",
                Guid.NewGuid().ToString("N"));

            public TemporaryDatabase() =>
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));

            public SqliteConnectionFactory ConnectionFactory { get; }
            public string DatabasePath => ConnectionFactory.DatabasePath;

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
