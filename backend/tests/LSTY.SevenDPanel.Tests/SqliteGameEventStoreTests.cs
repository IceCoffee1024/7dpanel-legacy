using System;
using System.IO;
using System.Linq;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application.GameEvents;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SqliteGameEventStoreTests
    {
        [Fact]
        public void Query_uses_descending_keyset_filters_and_keeps_gaps_separate()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            var store = new SqliteGameEventStore(database.ConnectionFactory);
            var at = Utc(1);
            var first = Record("00000000-0000-0000-0000-000000000001", GameEventType.PlayerJoined, at, "EOS_1");
            var second = Record("ffffffff-ffff-ffff-ffff-ffffffffffff", GameEventType.PlayerLeft, at, "EOS_1");
            var third = Record("00000000-0000-0000-0000-000000000002", GameEventType.PlayerDied, at.AddMinutes(1), "EOS_2");
            store.Append(first);
            store.Append(second);
            store.Append(third);
            store.AppendGap(new GameEventGap(Guid.NewGuid().ToString("D"), GameEventGapReason.QueueFull, at, at.AddMinutes(1), 2));

            var page1 = store.Query(new GameEventQuery(2));
            Assert.Equal(new[] { third.EventId, second.EventId }, page1.Events.Select(value => value.EventId));
            Assert.Single(page1.Gaps);
            var page2 = store.Query(new GameEventQuery(2, cursor: page1.NextCursor));
            Assert.Equal(new[] { first.EventId }, page2.Events.Select(value => value.EventId));

            var filtered = store.Query(new GameEventQuery(10, at, at, GameEventType.PlayerLeft, "EOS_1"));
            Assert.Equal(second.EventId, Assert.Single(filtered.Events).EventId);
        }

        [Fact]
        public void Append_preserves_subject_when_stable_identity_is_missing()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            var store = new SqliteGameEventStore(database.ConnectionFactory);
            var record = new GameEventRecord(
                Guid.NewGuid().ToString("D"), GameEventType.PlayerKilledEntity, Utc(1), Utc(1),
                new GameEventSubject(null, "Steam_1", 9, "Same Name"),
                new GameEventSubject(null, null, 12, "zombie"), null);

            store.Append(record);

            var stored = Assert.Single(store.Query(new GameEventQuery()).Events);
            Assert.Null(stored.Actor!.StableIdentity);
            Assert.Equal("Steam_1", stored.Actor.PlatformId);
            Assert.Equal(9, stored.Actor.EntityId);
            Assert.Equal("zombie", stored.Target!.DisplayName);
        }

        private static GameEventRecord Record(string id, GameEventType type, DateTimeOffset at, string crossplatformId) =>
            new GameEventRecord(id, type, at, at, new GameEventSubject(crossplatformId, null, 1, "player"), null, null);
        private static DateTimeOffset Utc(int minute) => new DateTimeOffset(2026, 7, 26, 0, minute, 0, TimeSpan.Zero);

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(Path.GetTempPath(), "7dpanel-game-events-tests", Guid.NewGuid().ToString("N"));
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
