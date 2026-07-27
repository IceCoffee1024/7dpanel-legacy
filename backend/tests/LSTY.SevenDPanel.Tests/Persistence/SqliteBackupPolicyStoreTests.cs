using System;
using System.IO;
using System.Linq;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Backups;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Domain.Backups;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Persistence
{
    public sealed class SqliteBackupPolicyStoreTests
    {
        [Fact]
        public void Policies_round_trip_in_fixed_kind_order_and_require_expected_row_version()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteBackupPolicyStore(database.ConnectionFactory);

            var serverConfiguration = store.Upsert(Definition(
                BackupKind.ServerConfiguration,
                enabled: false));
            var world = store.Upsert(Definition(BackupKind.World, enabled: true));
            var panelDatabase = store.Upsert(Definition(BackupKind.PanelDatabase, enabled: true));

            Assert.Equal(
                new[] { world, panelDatabase, serverConfiguration },
                store.List());
            Assert.Equal(world, store.Get(BackupKind.World));

            var updated = store.Upsert(new BackupPolicyDefinition(
                world.Kind,
                false,
                "15 3 * * *",
                "UTC",
                "primary",
                5,
                14,
                false,
                world.RowVersion));

            Assert.Equal(1, updated.RowVersion);
            Assert.False(updated.Enabled);
            Assert.Equal("15 3 * * *", updated.CronExpression);
            Assert.Equal(5, updated.RetentionCount);
            Assert.Equal(14, updated.RetentionDays);
            Assert.False(updated.CompressionEnabled);
            Assert.Throws<InvalidOperationException>(() => store.Upsert(Definition(
                BackupKind.World,
                enabled: true,
                rowVersion: world.RowVersion)));
        }

        [Fact]
        public void Two_writers_cannot_both_update_the_same_policy_version()
        {
            using var database = new TemporaryDatabase();
            var first = new SqliteBackupPolicyStore(database.ConnectionFactory);
            var second = new SqliteBackupPolicyStore(database.ConnectionFactory);
            var current = first.Upsert(Definition(BackupKind.World, enabled: false));

            var firstUpdate = first.Upsert(Definition(
                BackupKind.World,
                enabled: true,
                rowVersion: current.RowVersion));

            Assert.Equal(1, firstUpdate.RowVersion);
            Assert.Throws<InvalidOperationException>(() => second.Upsert(Definition(
                BackupKind.World,
                enabled: false,
                rowVersion: current.RowVersion)));
            Assert.True(second.Get(BackupKind.World)!.Enabled);
        }

        [Theory]
        [InlineData("../escape", "0 0 * * *", "UTC")]
        [InlineData("primary/root", "0 0 * * *", "UTC")]
        [InlineData("primary", "not cron", "UTC")]
        [InlineData("primary", "0 0 * * *", "Missing/TimeZone")]
        public void Policy_rejects_unsafe_roots_or_invalid_schedules(
            string backupRootId,
            string cronExpression,
            string timeZoneId)
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteBackupPolicyStore(database.ConnectionFactory);

            Assert.Throws<ArgumentException>(() => store.Upsert(new BackupPolicyDefinition(
                BackupKind.World,
                true,
                cronExpression,
                timeZoneId,
                backupRootId,
                3,
                7,
                true,
                0)));
        }

        [Fact]
        public void Service_exposes_all_fixed_kinds_and_rejects_an_unapproved_root()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteBackupPolicyStore(database.ConnectionFactory);
            var service = new BackupPolicyService(store, new[] { "primary" });

            Assert.Equal(
                new[]
                {
                    BackupKind.World,
                    BackupKind.PanelDatabase,
                    BackupKind.ServerConfiguration
                },
                service.List().Select(policy => policy.Kind));
            Assert.All(service.List(), policy => Assert.False(policy.Enabled));

            Assert.Throws<ArgumentException>(() => service.Save(new BackupPolicyDefinition(
                BackupKind.World,
                true,
                "0 0 * * *",
                "UTC",
                "secondary",
                3,
                7,
                true,
                0)));

            var saved = service.Save(Definition(BackupKind.World, enabled: true));
            Assert.True(saved.Enabled);
            Assert.Equal(saved, service.List().Single(policy => policy.Kind == BackupKind.World));
        }

        private static BackupPolicyDefinition Definition(
            BackupKind kind,
            bool enabled,
            long rowVersion = 0) =>
            new BackupPolicyDefinition(
                kind,
                enabled,
                "0 0 * * *",
                "UTC",
                "primary",
                3,
                7,
                true,
                rowVersion);

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-backup-policy-store-tests",
                Guid.NewGuid().ToString("N"));

            public TemporaryDatabase()
            {
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
