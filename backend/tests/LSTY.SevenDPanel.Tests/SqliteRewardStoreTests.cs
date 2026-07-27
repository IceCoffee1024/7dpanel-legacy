using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Rewards;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Rewards;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SqliteRewardStoreTests
    {
        [Fact]
        public void Package_round_trip_preserves_typed_entries_and_order()
        {
            using var database = new RewardTestDatabase();
            var store = new SqliteRewardStore(database.ConnectionFactory);
            var saved = store.SavePackage(Package(), Utc(0));

            var loaded = store.GetPackage(saved.PackageId);

            Assert.Equal("Starter Package", loaded.Name);
            Assert.Equal("A typed package", loaded.Description);
            Assert.True(loaded.Enabled);
            Assert.Equal(7, loaded.SortOrder);
            Assert.Equal(
                new[] { RewardEntryKind.Item, RewardEntryKind.Currency, RewardEntryKind.RegisteredAction },
                loaded.Entries.Select(entry => entry.Kind));
            Assert.Equal("medicalBandage", loaded.Entries[0].ItemInternalName);
            Assert.Equal(50, loaded.Entries[1].CurrencyAmount);
            Assert.Equal(RewardRegisteredActions.ResetSkills, loaded.Entries[2].RegisteredAction);
        }

        [Fact]
        public void Idempotency_and_eligibility_each_create_only_one_grant()
        {
            using var database = new RewardTestDatabase();
            var store = new SqliteRewardStore(database.ConnectionFactory);
            store.SavePackage(Package(), Utc(0));
            var first = store.GetOrCreateGrant(Grant("operation-1", "idempotency-1", "eligibility-1"));
            var replay = store.GetOrCreateGrant(Grant("operation-2", "idempotency-1", "eligibility-1"));
            var duplicateEligibility = store.GetOrCreateGrant(
                Grant("operation-3", "idempotency-2", "eligibility-1"));

            Assert.True(first.Created);
            Assert.False(replay.Created);
            Assert.False(duplicateEligibility.Created);
            Assert.Equal(first.Operation.OperationId, replay.Operation.OperationId);
            Assert.Equal(first.Operation.OperationId, duplicateEligibility.Operation.OperationId);
            Assert.Throws<RewardIdempotencyConflictException>(() =>
                store.GetOrCreateGrant(new GrantOperationDraft(
                    "conflict",
                    "different-package",
                    "EOS-player",
                    42,
                    "world-1",
                    "idempotency-1",
                    "eligibility-1",
                    "Achievement",
                    "achievement-1",
                    "System",
                    "tests",
                    null,
                    null,
                    "correlation",
                    Utc(1),
                    Array.Empty<GrantOperationEntryDraft>())));
        }

        [Fact]
        public void Unknown_dispatch_is_recoverable_but_only_manual_confirmation_completes_it()
        {
            using var database = new RewardTestDatabase();
            var store = new SqliteRewardStore(database.ConnectionFactory);
            store.SavePackage(Package(), Utc(0));
            var created = store.GetOrCreateGrant(Grant("operation-1", "idempotency-1", "eligibility-1"));
            Assert.True(store.TryStartDispatch(
                created.Operation.OperationId,
                created.Operation.RowVersion,
                Utc(1)));
            var dispatching = store.GetGrant(created.Operation.OperationId);
            var item = dispatching.Entries[0];
            store.RecordDeliveryOperation(
                dispatching.OperationId,
                item.OperationEntryId,
                "grant-item-operation-1",
                Utc(2));

            Assert.True(store.TryResolveDispatch(new GrantDispatchResolution(
                dispatching.OperationId,
                dispatching.RowVersion,
                GrantOperationState.PendingReconciliation,
                new[]
                {
                    new GrantEntryResolution(
                        item.OperationEntryId,
                        GrantOperationState.PendingReconciliation,
                        "grant-item-operation-1",
                        null,
                        "ResultUnknown")
                },
                "ResultUnknown",
                Utc(3))));
            Assert.Single(store.ListPendingReconciliation(20));
            Assert.False(store.TryConfirmReconciled(
                "not-pending",
                0,
                "owner",
                "correlation",
                null,
                Utc(4)));

            var pending = store.GetGrant(dispatching.OperationId);
            Assert.True(store.TryConfirmReconciled(
                pending.OperationId,
                pending.RowVersion,
                "owner",
                "manual-confirm",
                null,
                Utc(4)));
            var completed = store.GetGrant(pending.OperationId);
            Assert.Equal(GrantOperationState.Completed, completed.State);
            Assert.Equal("owner", completed.ReconciledBy);
            Assert.Equal("manual-confirm", completed.CorrelationId);
        }

        internal static RewardPackageDraft Package() => new RewardPackageDraft(
            "starter-package",
            "Starter Package",
            "A typed package",
            true,
            7,
            new[]
            {
                RewardPackageEntryDraft.Item(
                    "package-item",
                    "medicalBandage",
                    GameResourceKind.Item,
                    2,
                    null,
                    null,
                    "catalog-v1"),
                RewardPackageEntryDraft.Currency("package-currency", 50),
                RewardPackageEntryDraft.RegisteredActionEntry(
                    "package-reset",
                    RewardRegisteredActions.ResetSkills)
            });

        private static GrantOperationDraft Grant(
            string operationId,
            string idempotencyKey,
            string eligibilityKey) => new GrantOperationDraft(
                operationId,
                "starter-package",
                "EOS-player",
                42,
                "world-1",
                idempotencyKey,
                eligibilityKey,
                "Achievement",
                "achievement-1",
                "System",
                "tests",
                null,
                null,
                "correlation",
                Utc(0),
                new[]
                {
                    new GrantOperationEntryDraft("operation-entry-item", "package-item", 0, RewardEntryKind.Item),
                    new GrantOperationEntryDraft("operation-entry-currency", "package-currency", 1, RewardEntryKind.Currency),
                    new GrantOperationEntryDraft("operation-entry-action", "package-reset", 2, RewardEntryKind.RegisteredAction)
                });

        private static DateTimeOffset Utc(int seconds) =>
            new DateTimeOffset(2026, 7, 27, 0, 0, seconds, TimeSpan.Zero);
    }

    internal sealed class RewardTestCatalog : IGameResourceCatalog
    {
        private readonly GameResourceCatalogReadResult result;

        private RewardTestCatalog(GameResourceCatalogReadResult result) => this.result = result;

        public static RewardTestCatalog Available() => new RewardTestCatalog(
            GameResourceCatalogReadResult.Available(new GameResourceCatalogSnapshot(
                "catalog-v1",
                "2.4",
                new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
                new[]
                {
                    new GameResourceCatalogEntry(
                        "item-medical-bandage",
                        10,
                        "medicalBandage",
                        "绷带",
                        "Bandage",
                        GameResourceKind.Item,
                        GameResourceVisibility.Public,
                        10,
                        false,
                        GameResourceIconStatus.Available,
                        null)
                },
                Array.Empty<string>())));

        public GameResourceCatalogReadResult Read() => result;

        public System.Threading.Tasks.Task<GameResourceIconReadResult> ReadIconAsync(
            string catalogVersion,
            string resourceId,
            System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult(GameResourceIconReadResult.Missing());
    }

    internal sealed class RewardTestDatabase : IDisposable
    {
        private readonly string databasePath;

        public RewardTestDatabase()
        {
            databasePath = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-rewards-" + Guid.NewGuid().ToString("N") + ".db");
            ConnectionFactory = new SqliteConnectionFactory(databasePath);
            new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
        }

        public SqliteConnectionFactory ConnectionFactory { get; }

        public void Dispose()
        {
            ConnectionFactory.Dispose();
            Delete(databasePath);
            Delete(databasePath + "-wal");
            Delete(databasePath + "-shm");
        }

        private static void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
