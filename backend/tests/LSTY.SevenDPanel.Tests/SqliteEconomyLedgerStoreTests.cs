using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Domain.Economy;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SqliteEconomyLedgerStoreTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 2, 0, 0, TimeSpan.Zero);

        [Fact]
        public void First_opening_issues_once_and_every_committed_transaction_is_balanced()
        {
            using var database = new TemporaryDatabase();
            var store = database.Store;

            var first = store.GetOrCreatePlayerAccount("EOS-A", "open-a", 100, Now);
            var replay = store.GetOrCreatePlayerAccount("EOS-A", "open-a", 100, Now);
            var later = store.GetOrCreatePlayerAccount("EOS-A", "open-a-later", 999, Now.AddSeconds(1));

            Assert.Equal(100, first.PostedBalance);
            Assert.Equal(first.AccountId, replay.AccountId);
            Assert.Equal(100, later.PostedBalance);
            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM economy_transactions WHERE transaction_type = 'AccountOpening';"));
            AssertLedgerInvariants(connection);
        }

        [Fact]
        public void Frozen_accounts_block_player_debits_but_owner_adjustments_and_idempotent_transfers_are_atomic()
        {
            using var database = new TemporaryDatabase();
            var store = database.Store;
            var source = store.GetOrCreatePlayerAccount("EOS-A", "open-a", 100, Now);
            var target = store.GetOrCreatePlayerAccount("EOS-B", "open-b", 0, Now);
            store.SetFrozen(source.AccountId, true, source.RowVersion, Now.AddMilliseconds(1));

            Assert.Throws<EconomyAccountFrozenException>(() => store.Commit(Transfer(
                "blocked", "blocked-key", source.AccountId, target.AccountId, 10,
                Now.AddMilliseconds(2))));

            store.Commit(new LedgerTransactionDraft(
                "owner-debit", "OwnerAdjustmentDebit", "owner-debit-key", Now.AddMilliseconds(3),
                EconomyActorKinds.Owner, "owner", "EOS-A", "AccountAdjustment", source.AccountId,
                "corr-owner", "approved recovery",
                new[]
                {
                    new LedgerEntryDraft(source.AccountId, LedgerSide.Debit, 20),
                    new LedgerEntryDraft(SystemAccountIds.Recovery, LedgerSide.Credit, 20)
                }));

            var frozen = FindPlayer(store, "EOS-A");
            store.SetFrozen(frozen.AccountId, false, frozen.RowVersion, Now.AddMilliseconds(4));
            var draft = Transfer(
                "transfer", "transfer-key", source.AccountId, target.AccountId, 30,
                Now.AddMilliseconds(5));
            var first = store.Commit(draft);
            var replay = store.Commit(draft);

            Assert.Equal(first.Transaction.TransactionId, replay.Transaction.TransactionId);
            Assert.Equal(50, FindPlayer(store, "EOS-A").PostedBalance);
            Assert.Equal(30, FindPlayer(store, "EOS-B").PostedBalance);
            Assert.Throws<EconomyIdempotencyConflictException>(() => store.Commit(Transfer(
                "transfer", "transfer-key", source.AccountId, target.AccountId, 31,
                Now.AddMilliseconds(5))));
            using var connection = database.ConnectionFactory.Open();
            AssertLedgerInvariants(connection);
        }

        [Fact]
        public async Task Concurrent_reservations_do_not_overdraw_and_capture_or_release_are_atomic()
        {
            using var database = new TemporaryDatabase();
            var store = database.Store;
            var account = store.GetOrCreatePlayerAccount("EOS-A", "open-a", 100, Now);
            var firstDraft = Reservation("reservation-a", "reserve-a", account.AccountId, 80, Now);
            var secondDraft = Reservation("reservation-b", "reserve-b", account.AccountId, 80, Now);

            var results = await Task.WhenAll(
                Task.Run(() => store.TryReserve(firstDraft)),
                Task.Run(() => store.TryReserve(secondDraft)));

            Assert.Equal(1, results.Count(result => result.Status == FundsReservationStatus.Reserved));
            Assert.Equal(1, results.Count(result => result.Status == FundsReservationStatus.InsufficientFunds));
            var winner = results.Single(result => result.Status == FundsReservationStatus.Reserved);
            Assert.True(store.Release(winner.ReservationId, Now.AddMilliseconds(1)));
            Assert.True(store.Release(winner.ReservationId, Now.AddMilliseconds(2)));

            var captureReservation = store.TryReserve(Reservation(
                "reservation-c", "reserve-c", account.AccountId, 60, Now.AddMilliseconds(3)));
            Assert.Equal(FundsReservationStatus.Reserved, captureReservation.Status);
            var capture = store.Capture(
                captureReservation.ReservationId,
                "capture-c",
                "capture-c-key",
                Now.AddMilliseconds(4));
            var captureReplay = store.Capture(
                captureReservation.ReservationId,
                "capture-c",
                "capture-c-key",
                Now.AddMilliseconds(4));

            Assert.Equal(capture.Transaction.TransactionId, captureReplay.Transaction.TransactionId);
            var player = FindPlayer(store, "EOS-A");
            Assert.Equal(40, player.PostedBalance);
            Assert.Equal(0, player.ReservedDebit);
            using var connection = database.ConnectionFactory.Open();
            AssertLedgerInvariants(connection);
        }

        [Fact]
        public void Invalid_transactions_roll_back_without_accounts_entries_or_reservations_leaking()
        {
            using var database = new TemporaryDatabase();
            var store = database.Store;
            var source = store.GetOrCreatePlayerAccount("EOS-A", "open-a", 100, Now);
            var beforeAccounts = store.QueryAccounts(new AccountKeysetQuery(100)).Accounts.Count;

            Assert.Throws<EconomyUnbalancedTransactionException>(() => store.Commit(
                new LedgerTransactionDraft(
                    "unbalanced", "Test", "unbalanced-key", Now.AddMilliseconds(1),
                    EconomyActorKinds.Owner, "owner", "EOS-A", "Test", "unbalanced",
                    null, "must roll back",
                    new[]
                    {
                        new LedgerEntryDraft(source.AccountId, LedgerSide.Debit, 10),
                        new LedgerEntryDraft(SystemAccountIds.Recovery, LedgerSide.Credit, 9)
                    })));
            Assert.Throws<EconomyAccountNotFoundException>(() => store.Commit(
                new LedgerTransactionDraft(
                    "missing", "Test", "missing-key", Now.AddMilliseconds(2),
                    EconomyActorKinds.Owner, "owner", "EOS-A", "Test", "missing",
                    null, "must roll back",
                    new[]
                    {
                        new LedgerEntryDraft("player:missing", LedgerSide.Debit, 10),
                        new LedgerEntryDraft(SystemAccountIds.Recovery, LedgerSide.Credit, 10)
                    })));

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(beforeAccounts, store.QueryAccounts(new AccountKeysetQuery(100)).Accounts.Count);
            Assert.Equal(0, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM economy_transactions WHERE idempotency_key IN ('unbalanced-key', 'missing-key');"));
            Assert.Equal(0, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM economy_entries WHERE transaction_id IN ('unbalanced', 'missing');"));
            Assert.Equal(0, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM economy_reservations WHERE idempotency_key IN ('unbalanced-key', 'missing-key');"));
            Assert.Equal(100, FindPlayer(store, "EOS-A").PostedBalance);
            AssertLedgerInvariants(connection);
        }

        [Fact]
        public void Account_and_transaction_queries_have_stable_keysets_without_duplicates()
        {
            using var database = new TemporaryDatabase();
            var store = database.Store;
            var a = store.GetOrCreatePlayerAccount("EOS-A", "open-a", 300, Now);
            var b = store.GetOrCreatePlayerAccount("EOS-B", "open-b", 200, Now.AddMilliseconds(1));
            var c = store.GetOrCreatePlayerAccount("EOS-C", "open-c", 200, Now.AddMilliseconds(2));
            var d = store.GetOrCreatePlayerAccount("EOS-D", "open-d", 100, Now.AddMilliseconds(3));

            var firstAccounts = store.QueryAccounts(new AccountKeysetQuery(2, includeSystem: false));
            var secondAccounts = store.QueryAccounts(new AccountKeysetQuery(
                2, includeSystem: false, keyset: firstAccounts.NextKeyset));
            Assert.Equal(new[] { a.AccountId, b.AccountId }, firstAccounts.Accounts.Select(x => x.AccountId));
            Assert.Equal(new[] { c.AccountId, d.AccountId }, secondAccounts.Accounts.Select(x => x.AccountId));
            Assert.Empty(firstAccounts.Accounts.Select(x => x.AccountId)
                .Intersect(secondAccounts.Accounts.Select(x => x.AccountId), StringComparer.Ordinal));

            CommitKeysetTransaction(store, "keyset-a", a.AccountId, b.AccountId, Now.AddSeconds(10));
            CommitKeysetTransaction(store, "keyset-b", b.AccountId, c.AccountId, Now.AddSeconds(11));
            CommitKeysetTransaction(store, "keyset-c", c.AccountId, d.AccountId, Now.AddSeconds(11));
            var firstTransactions = store.QueryTransactions(new TransactionKeysetQuery(
                2, type: "Keyset"));
            var secondTransactions = store.QueryTransactions(new TransactionKeysetQuery(
                2, type: "Keyset", keyset: firstTransactions.NextKeyset));

            Assert.Equal(new[] { "keyset-c", "keyset-b" },
                firstTransactions.Transactions.Select(transaction => transaction.TransactionId));
            Assert.Equal(new[] { "keyset-a" },
                secondTransactions.Transactions.Select(transaction => transaction.TransactionId));
            Assert.Empty(firstTransactions.Transactions.Select(x => x.TransactionId)
                .Intersect(secondTransactions.Transactions.Select(x => x.TransactionId), StringComparer.Ordinal));
        }

        private static AccountSnapshot FindPlayer(SqliteEconomyLedgerStore store, string crossplatformId) =>
            Assert.Single(store.QueryAccounts(new AccountKeysetQuery(
                10,
                includeSystem: false,
                search: crossplatformId)).Accounts);

        private static LedgerTransactionDraft Transfer(
            string transactionId,
            string idempotencyKey,
            string sourceAccountId,
            string targetAccountId,
            long amount,
            DateTimeOffset occurredAtUtc) => new LedgerTransactionDraft(
                transactionId,
                "PlayerTransfer",
                idempotencyKey,
                occurredAtUtc,
                EconomyActorKinds.Player,
                sourceAccountId.Substring("player:".Length),
                sourceAccountId.Substring("player:".Length),
                "PlayerTransfer",
                transactionId,
                "corr-" + transactionId,
                null,
                new[]
                {
                    new LedgerEntryDraft(sourceAccountId, LedgerSide.Debit, amount),
                    new LedgerEntryDraft(targetAccountId, LedgerSide.Credit, amount)
                });

        private static FundsReservationDraft Reservation(
            string reservationId,
            string idempotencyKey,
            string accountId,
            long amount,
            DateTimeOffset occurredAtUtc) => new FundsReservationDraft(
                reservationId,
                accountId,
                amount,
                idempotencyKey,
                "Teleport",
                reservationId,
                occurredAtUtc,
                occurredAtUtc.AddMinutes(5));

        private static void CommitKeysetTransaction(
            SqliteEconomyLedgerStore store,
            string id,
            string debitAccountId,
            string creditAccountId,
            DateTimeOffset occurredAtUtc) => store.Commit(new LedgerTransactionDraft(
                id,
                "Keyset",
                id + "-key",
                occurredAtUtc,
                EconomyActorKinds.Owner,
                "owner",
                null,
                "Keyset",
                id,
                null,
                "keyset test",
                new[]
                {
                    new LedgerEntryDraft(debitAccountId, LedgerSide.Debit, 1),
                    new LedgerEntryDraft(creditAccountId, LedgerSide.Credit, 1)
                }));

        private static void AssertLedgerInvariants(SqliteConnection connection)
        {
            Assert.Equal(0, connection.ExecuteScalar<int>(
                @"SELECT COUNT(*)
                  FROM economy_transactions AS transaction_row
                  WHERE transaction_row.status = 'Committed'
                    AND COALESCE((
                        SELECT SUM(CASE WHEN side = 'Debit' THEN amount ELSE 0 END)
                        FROM economy_entries WHERE transaction_id = transaction_row.transaction_id), 0)
                        <> COALESCE((
                        SELECT SUM(CASE WHEN side = 'Credit' THEN amount ELSE 0 END)
                        FROM economy_entries WHERE transaction_id = transaction_row.transaction_id), 0);"));
            Assert.Equal(0, connection.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM economy_accounts
                  WHERE account_kind = 'Player'
                    AND (posted_balance < reserved_debit OR reserved_debit < 0);"));
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(), "7dpanel-economy-store-tests", Guid.NewGuid().ToString("N"));

            public TemporaryDatabase()
            {
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
                Store = new SqliteEconomyLedgerStore(ConnectionFactory);
            }

            public SqliteConnectionFactory ConnectionFactory { get; }
            public SqliteEconomyLedgerStore Store { get; }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
