using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using DbUp;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Community;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Community;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class CommunityGameCommandPersistenceContractTests
    {
        private static readonly string[] ExpectedTables =
        {
            "daily_reward_claims",
            "shop_products",
            "teleport_friend_requests"
        };

        private static readonly string[] ExpectedIndexes =
        {
            "ix_shop_products_enabled_sort",
            "ux_daily_reward_claim_period",
            "ux_teleport_friend_requests_idempotency",
            "ux_teleport_friend_requests_target_pending"
        };

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Dedicated_command_contracts_exist_from_empty_database_and_010_upgrade(
            bool upgradeFrom010)
        {
            using var database = new TemporaryDatabase();
            if (upgradeFrom010) UpgradeThrough010(database.ConnectionFactory);

            database.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(ExpectedTables, ReadNames(connection, "table", ExpectedTables));
            Assert.Equal(ExpectedIndexes, ReadNames(connection, "index", ExpectedIndexes));
        }

        [Fact]
        public void Shop_query_returns_only_enabled_products_in_stable_keyset_pages()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            SeedRewardPackage(database.ConnectionFactory);
            var store = new SqliteCommerceStore(database.ConnectionFactory);
            store.SaveProduct(Product("disabled", false, 0), Now);
            store.SaveProduct(Product("product-b", true, 10), Now);
            store.SaveProduct(Product("product-a", true, 10), Now);
            store.SaveProduct(Product("product-c", true, 20), Now);
            var query = new BrowseShopUseCase(store);

            var first = query.Execute(new ShopProductKeysetQuery(2));
            var second = query.Execute(new ShopProductKeysetQuery(2, first.Next));

            Assert.Equal(new[] { "product-a", "product-b" },
                first.Products.Select(product => product.ProductId));
            Assert.NotNull(first.Next);
            Assert.Equal(new[] { "product-c" },
                second.Products.Select(product => product.ProductId));
            Assert.Null(second.Next);
        }

        [Fact]
        public void Community_configuration_updates_reject_stale_row_versions_in_the_store()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            var community = new SqliteCommunityStore(database.ConnectionFactory);
            var votes = new SqliteVoteStore(database.ConnectionFactory);

            var initialSetting = community.SaveTeleportSettings(Setting(TeleportKind.Home, true, 0));
            var currentSetting = community.SaveTeleportSettings(Setting(TeleportKind.Home, false, initialSetting.RowVersion));
            Assert.Equal(1, currentSetting.RowVersion);
            Assert.Throws<CommunityConflictException>(() =>
                community.SaveTeleportSettings(Setting(TeleportKind.Home, true, initialSetting.RowVersion)));
            Assert.False(community.GetTeleportSettings(TeleportKind.Home).Enabled);

            var initialConfiguration = votes.SaveConfiguration(Configuration(VoteKind.Kick, true, 0));
            var currentConfiguration = votes.SaveConfiguration(
                Configuration(VoteKind.Kick, false, initialConfiguration.RowVersion));
            Assert.Equal(1, currentConfiguration.RowVersion);
            Assert.Throws<CommunityConflictException>(() =>
                votes.SaveConfiguration(Configuration(VoteKind.Kick, true, initialConfiguration.RowVersion)));
            Assert.False(votes.GetConfiguration(VoteKind.Kick)!.Enabled);
        }

        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 3, 0, 0, TimeSpan.Zero);

        private static ShopProductDraft Product(string id, bool enabled, int sortOrder) =>
            new ShopProductDraft(
                id,
                id,
                string.Empty,
                enabled,
                10,
                null,
                null,
                "package-1",
                sortOrder);

        private static TeleportSettings Setting(TeleportKind kind, bool enabled, long rowVersion) =>
            new TeleportSettings(
                kind,
                enabled,
                kind == TeleportKind.Home ? 3 : null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                false,
                0,
                Now,
                rowVersion);

        private static VoteConfiguration Configuration(VoteKind kind, bool enabled, long rowVersion) =>
            new VoteConfiguration(
                "configuration-" + kind.ToString().ToLowerInvariant(),
                kind,
                enabled,
                TimeSpan.FromMinutes(1),
                60,
                2,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                "global",
                true,
                Now,
                rowVersion);

        private static void SeedRewardPackage(SqliteConnectionFactory connectionFactory)
        {
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO reward_packages (
                      package_id, name, description, enabled, sort_order,
                      created_at_utc, updated_at_utc, row_version)
                  VALUES ('package-1', 'Package', '', 1, 0, @Now, @Now, 0);",
                new { Now = Now.ToUnixTimeMilliseconds() });
        }

        private static string[] ReadNames(
            SqliteConnection connection,
            string type,
            IReadOnlyList<string> expected) =>
            connection.Query<string>(
                    @"SELECT name FROM sqlite_master
                      WHERE type = @Type AND name IN (" +
                    string.Join(",", expected.Select((_, index) => "@p" + index)) +
                    ") ORDER BY name;",
                    expected.Select((name, index) =>
                            new KeyValuePair<string, object>("p" + index, name))
                        .Append(new KeyValuePair<string, object>("Type", type))
                        .ToDictionary(pair => pair.Key, pair => pair.Value))
                .ToArray();

        private static void UpgradeThrough010(SqliteConnectionFactory connectionFactory)
        {
            var directory = Path.GetDirectoryName(connectionFactory.DatabasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var result = DeployChanges.To
                .SqliteDatabase(connectionFactory.ConnectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(SqliteDatabaseBootstrapper).Assembly,
                    resourceName =>
                        resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) &&
                        Enumerable.Range(1, 10).Any(version => resourceName.IndexOf(
                            $".Migrations.{version:D3}_",
                            StringComparison.OrdinalIgnoreCase) >= 0))
                .WithTransactionPerScript()
                .Build()
                .PerformUpgrade();
            Assert.True(result.Successful, result.Error?.ToString());
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-community-command-contract-tests",
                Guid.NewGuid().ToString("N"));

            public TemporaryDatabase() =>
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));

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
