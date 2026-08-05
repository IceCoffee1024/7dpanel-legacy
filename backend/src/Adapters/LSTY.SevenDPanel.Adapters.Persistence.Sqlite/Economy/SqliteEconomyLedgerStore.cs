using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Domain.Economy;
using Microsoft.Data.Sqlite;
using static LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy.SqliteEconomyLedgerStoreRows;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy
{
    public sealed class SqliteEconomyLedgerStore :
        IEconomyLedgerStore,
        IEconomyAccountAdministrationStore
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteEconomyLedgerStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));

        public AccountSnapshot GetOrCreatePlayerAccount(
            string crossplatformId,
            string idempotencyKey,
            long openingAmount,
            DateTimeOffset occurredAtUtc)
        {
            crossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            idempotencyKey = RequireText(idempotencyKey, nameof(idempotencyKey));
            LedgerRules.ValidateAmount(openingAmount);
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));

            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var existingTransaction = FindTransactionByIdempotency(
                connection, transaction, idempotencyKey);
            if (existingTransaction != null)
            {
                var existingAccount = FindPlayerAccount(
                    connection, transaction, crossplatformId) ??
                    throw new EconomyIdempotencyConflictException();
                EnsureOpeningMatches(
                    connection,
                    transaction,
                    existingTransaction,
                    existingAccount.AccountId,
                    crossplatformId,
                    openingAmount);
                transaction.Commit();
                return ToAccount(existingAccount);
            }

            var account = FindPlayerAccount(connection, transaction, crossplatformId);
            if (account != null)
            {
                transaction.Commit();
                return ToAccount(account);
            }

            var accountId = "player:" + crossplatformId;
            var occurredUtc = occurredAtUtc.ToUnixTimeMilliseconds();
            connection.Execute(
                @"INSERT INTO economy_accounts (
                      account_id, account_kind, crossplatform_id, enabled, is_frozen,
                      posted_balance, reserved_debit, created_at_utc, updated_at_utc, row_version)
                  VALUES (@AccountId, 'Player', @CrossplatformId, 1, 0, 0, 0,
                      @OccurredUtc, @OccurredUtc, 0);",
                new { AccountId = accountId, CrossplatformId = crossplatformId, OccurredUtc = occurredUtc },
                transaction);
            CommitInTransaction(
                connection,
                transaction,
                new LedgerTransactionDraft(
                    Guid.NewGuid().ToString("D"),
                    "AccountOpening",
                    idempotencyKey,
                    occurredAtUtc,
                    EconomyActorKinds.System,
                    SystemAccountIds.Issuance,
                    crossplatformId,
                    "AccountOpening",
                    accountId,
                    idempotencyKey,
                    "Initial account issuance",
                    new[]
                    {
                        new LedgerEntryDraft(SystemAccountIds.Issuance, LedgerSide.Debit, openingAmount),
                        new LedgerEntryDraft(accountId, LedgerSide.Credit, openingAmount)
                    }));
            account = GetAccount(connection, transaction, accountId);
            transaction.Commit();
            return ToAccount(account);
        }

        public LedgerWriteResult Commit(LedgerTransactionDraft transactionDraft)
        {
            if (transactionDraft == null) throw new ArgumentNullException(nameof(transactionDraft));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var result = CommitInTransaction(connection, transaction, transactionDraft);
            transaction.Commit();
            return result;
        }

        public FundsReservationResult TryReserve(FundsReservationDraft reservation)
        {
            if (reservation == null) throw new ArgumentNullException(nameof(reservation));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var result = TryReserve(connection, transaction, reservation);
            transaction.Commit();
            return result;
        }

        internal static FundsReservationResult TryReserve(
            SqliteConnection connection,
            SqliteTransaction transaction,
            FundsReservationDraft reservation)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (reservation == null) throw new ArgumentNullException(nameof(reservation));
            var existing = connection.QuerySingleOrDefault<ReservationRow>(
                ReservationSelect + " WHERE idempotency_key = @IdempotencyKey;",
                new { reservation.IdempotencyKey }, transaction);
            if (existing != null)
            {
                EnsureReservationMatches(existing, reservation);
                var existingAccount = GetAccount(connection, transaction, existing.AccountId);
                return ToReservationResult(existing, existingAccount);
            }

            if (connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM economy_reservations WHERE reservation_id = @ReservationId;",
                new { reservation.ReservationId }, transaction) != 0)
            {
                throw new EconomyIdempotencyConflictException();
            }

            var account = GetAccount(connection, transaction, reservation.AccountId);
            if (!string.Equals(account.AccountKind, EconomyAccountKind.Player.ToString(), StringComparison.Ordinal))
                throw new EconomyAccountNotFoundException();
            if (account.Enabled == 0)
            {
                return FailedReservation(reservation, account, FundsReservationStatus.AccountDisabled);
            }
            if (account.IsFrozen != 0)
            {
                return FailedReservation(reservation, account, FundsReservationStatus.AccountFrozen);
            }
            if (account.PostedBalance - account.ReservedDebit < reservation.Amount)
            {
                return FailedReservation(reservation, account, FundsReservationStatus.InsufficientFunds);
            }

            var changed = connection.Execute(
                @"UPDATE economy_accounts
                  SET reserved_debit = reserved_debit + @Amount,
                      updated_at_utc = @UpdatedAtUtc,
                      row_version = row_version + 1
                  WHERE account_id = @AccountId
                    AND row_version = @RowVersion
                    AND enabled = 1 AND is_frozen = 0
                    AND posted_balance - reserved_debit >= @Amount;",
                new
                {
                    reservation.Amount,
                    UpdatedAtUtc = reservation.OccurredAtUtc.ToUnixTimeMilliseconds(),
                    reservation.AccountId,
                    account.RowVersion
                }, transaction);
            if (changed != 1) throw new EconomyConcurrencyException();

            connection.Execute(
                @"INSERT INTO economy_reservations (
                      reservation_id, account_id, amount, state, idempotency_key,
                      business_kind, business_id, captured_transaction_id,
                      created_at_utc, updated_at_utc, expires_at_utc, row_version)
                  VALUES (@ReservationId, @AccountId, @Amount, 'Reserved', @IdempotencyKey,
                      @BusinessKind, @BusinessId, NULL,
                      @OccurredAtUtc, @OccurredAtUtc, @ExpiresAtUtc, 0);",
                new
                {
                    reservation.ReservationId,
                    reservation.AccountId,
                    reservation.Amount,
                    reservation.IdempotencyKey,
                    reservation.BusinessKind,
                    reservation.BusinessId,
                    OccurredAtUtc = reservation.OccurredAtUtc.ToUnixTimeMilliseconds(),
                    ExpiresAtUtc = reservation.ExpiresAtUtc?.ToUnixTimeMilliseconds()
                }, transaction);
            var stored = connection.QuerySingle<ReservationRow>(
                ReservationSelect + " WHERE reservation_id = @ReservationId;",
                new { reservation.ReservationId }, transaction);
            account = GetAccount(connection, transaction, reservation.AccountId);
            return ToReservationResult(stored, account);
        }

        public LedgerWriteResult Capture(
            string reservationId,
            string transactionId,
            string idempotencyKey,
            DateTimeOffset occurredAtUtc)
        {
            reservationId = RequireText(reservationId, nameof(reservationId));
            transactionId = RequireText(transactionId, nameof(transactionId));
            idempotencyKey = RequireText(idempotencyKey, nameof(idempotencyKey));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));

            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var reservation = connection.QuerySingleOrDefault<ReservationRow>(
                ReservationSelect + " WHERE reservation_id = @ReservationId;",
                new { ReservationId = reservationId }, transaction) ??
                throw new KeyNotFoundException("The funds reservation does not exist.");
            var existingTransaction = FindTransactionByIdempotency(
                connection, transaction, idempotencyKey);
            if (existingTransaction != null)
            {
                if (!string.Equals(existingTransaction.TransactionId, transactionId, StringComparison.Ordinal) ||
                    !string.Equals(existingTransaction.TransactionType, "ReservationCapture", StringComparison.Ordinal) ||
                    !string.Equals(existingTransaction.BusinessKind, reservation.BusinessKind, StringComparison.Ordinal) ||
                    !string.Equals(existingTransaction.BusinessId, reservation.BusinessId, StringComparison.Ordinal) ||
                    !string.Equals(reservation.CapturedTransactionId, transactionId, StringComparison.Ordinal))
                {
                    throw new EconomyIdempotencyConflictException();
                }

                var replay = LoadWriteResult(connection, transaction, transactionId);
                transaction.Commit();
                return replay;
            }

            if (string.Equals(reservation.State, "Captured", StringComparison.Ordinal) ||
                string.Equals(reservation.State, "Released", StringComparison.Ordinal))
            {
                throw new EconomyIdempotencyConflictException();
            }

            var account = GetAccount(connection, transaction, reservation.AccountId);
            var released = connection.Execute(
                @"UPDATE economy_accounts
                  SET reserved_debit = reserved_debit - @Amount,
                      updated_at_utc = @UpdatedAtUtc,
                      row_version = row_version + 1
                  WHERE account_id = @AccountId
                    AND row_version = @RowVersion
                    AND reserved_debit >= @Amount;",
                new
                {
                    reservation.Amount,
                    UpdatedAtUtc = occurredAtUtc.ToUnixTimeMilliseconds(),
                    reservation.AccountId,
                    account.RowVersion
                }, transaction);
            if (released != 1) throw new EconomyConcurrencyException();

            var systemAccountId = CaptureSystemAccount(reservation.BusinessKind);
            var result = CommitInTransaction(
                connection,
                transaction,
                new LedgerTransactionDraft(
                    transactionId,
                    "ReservationCapture",
                    idempotencyKey,
                    occurredAtUtc,
                    EconomyActorKinds.System,
                    systemAccountId,
                    account.CrossplatformId,
                    reservation.BusinessKind,
                    reservation.BusinessId,
                    idempotencyKey,
                    null,
                    new[]
                    {
                        new LedgerEntryDraft(account.AccountId, LedgerSide.Debit, reservation.Amount),
                        new LedgerEntryDraft(systemAccountId, LedgerSide.Credit, reservation.Amount)
                    }));
            var reservationChanged = connection.Execute(
                @"UPDATE economy_reservations
                  SET state = 'Captured', captured_transaction_id = @TransactionId,
                      updated_at_utc = @UpdatedAtUtc, row_version = row_version + 1
                  WHERE reservation_id = @ReservationId
                    AND state = 'Reserved' AND row_version = @RowVersion;",
                new
                {
                    TransactionId = transactionId,
                    UpdatedAtUtc = occurredAtUtc.ToUnixTimeMilliseconds(),
                    ReservationId = reservationId,
                    reservation.RowVersion
                }, transaction);
            if (reservationChanged != 1) throw new EconomyConcurrencyException();
            transaction.Commit();
            return result;
        }

        public bool Release(string reservationId, DateTimeOffset occurredAtUtc)
        {
            reservationId = RequireText(reservationId, nameof(reservationId));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var reservation = connection.QuerySingleOrDefault<ReservationRow>(
                ReservationSelect + " WHERE reservation_id = @ReservationId;",
                new { ReservationId = reservationId }, transaction);
            if (reservation == null)
            {
                transaction.Commit();
                return false;
            }
            if (string.Equals(reservation.State, "Released", StringComparison.Ordinal))
            {
                transaction.Commit();
                return true;
            }
            if (!string.Equals(reservation.State, "Reserved", StringComparison.Ordinal))
            {
                transaction.Commit();
                return false;
            }

            var account = GetAccount(connection, transaction, reservation.AccountId);
            var accountChanged = connection.Execute(
                @"UPDATE economy_accounts
                  SET reserved_debit = reserved_debit - @Amount,
                      updated_at_utc = @UpdatedAtUtc,
                      row_version = row_version + 1
                  WHERE account_id = @AccountId
                    AND row_version = @RowVersion
                    AND reserved_debit >= @Amount;",
                new
                {
                    reservation.Amount,
                    UpdatedAtUtc = occurredAtUtc.ToUnixTimeMilliseconds(),
                    reservation.AccountId,
                    account.RowVersion
                }, transaction);
            if (accountChanged != 1) throw new EconomyConcurrencyException();
            var reservationChanged = connection.Execute(
                @"UPDATE economy_reservations
                  SET state = 'Released', updated_at_utc = @UpdatedAtUtc,
                      row_version = row_version + 1
                  WHERE reservation_id = @ReservationId
                    AND state = 'Reserved' AND row_version = @RowVersion;",
                new
                {
                    UpdatedAtUtc = occurredAtUtc.ToUnixTimeMilliseconds(),
                    ReservationId = reservationId,
                    reservation.RowVersion
                }, transaction);
            if (reservationChanged != 1) throw new EconomyConcurrencyException();
            transaction.Commit();
            return true;
        }

        public AccountSnapshot SetFrozen(
            string accountId,
            bool isFrozen,
            long expectedRowVersion,
            DateTimeOffset occurredAtUtc)
        {
            accountId = RequireText(accountId, nameof(accountId));
            if (expectedRowVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var changed = connection.Execute(
                @"UPDATE economy_accounts
                  SET is_frozen = @IsFrozen, updated_at_utc = @UpdatedAtUtc,
                      row_version = row_version + 1
                  WHERE account_id = @AccountId AND account_kind = 'Player'
                    AND row_version = @ExpectedRowVersion;",
                new
                {
                    AccountId = accountId,
                    IsFrozen = isFrozen ? 1 : 0,
                    UpdatedAtUtc = occurredAtUtc.ToUnixTimeMilliseconds(),
                    ExpectedRowVersion = expectedRowVersion
                }, transaction);
            if (changed != 1)
            {
                if (connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM economy_accounts WHERE account_id = @AccountId AND account_kind = 'Player';",
                    new { AccountId = accountId }, transaction) == 0)
                {
                    throw new EconomyAccountNotFoundException();
                }
                throw new EconomyConcurrencyException();
            }
            var account = GetAccount(connection, transaction, accountId);
            transaction.Commit();
            return ToAccount(account);
        }

        public AccountPage QueryAccounts(AccountKeysetQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var where = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("Take", query.PageSize + 1);
            if (!query.IncludeSystem) where.Add("account_kind = 'Player'");
            if (query.Search != null)
            {
                where.Add("(account_id LIKE @Search OR crossplatform_id LIKE @Search)");
                parameters.Add("Search", "%" + query.Search + "%");
            }
            if (query.Enabled.HasValue)
            {
                where.Add("enabled = @Enabled");
                parameters.Add("Enabled", query.Enabled.Value ? 1 : 0);
            }
            if (query.Frozen.HasValue)
            {
                where.Add("is_frozen = @Frozen");
                parameters.Add("Frozen", query.Frozen.Value ? 1 : 0);
            }
            if (query.Keyset != null)
            {
                where.Add("(posted_balance < @CursorBalance OR " +
                    "(posted_balance = @CursorBalance AND account_id > @CursorAccountId))");
                parameters.Add("CursorBalance", query.Keyset.PostedBalance);
                parameters.Add("CursorAccountId", query.Keyset.AccountId);
            }

            using var connection = connectionFactory.Open();
            var rows = connection.Query<AccountRow>(
                AccountSelect +
                (where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where)) +
                " ORDER BY posted_balance DESC, account_id ASC LIMIT @Take;",
                parameters).ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            var next = rows.Length > query.PageSize && pageRows.Length > 0
                ? new AccountKeyset(
                    pageRows[pageRows.Length - 1].PostedBalance,
                    pageRows[pageRows.Length - 1].AccountId)
                : null;
            return new AccountPage(pageRows.Select(ToAccount), next);
        }

        public TransactionPage QueryTransactions(TransactionKeysetQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var where = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("Take", query.PageSize + 1);
            if (query.RelatedCrossplatformId != null)
            {
                where.Add("related_crossplatform_id = @RelatedCrossplatformId");
                parameters.Add("RelatedCrossplatformId", query.RelatedCrossplatformId);
            }
            if (query.AccountId != null)
            {
                where.Add(@"EXISTS (
                    SELECT 1 FROM economy_entries
                    WHERE economy_entries.transaction_id = economy_transactions.transaction_id
                      AND economy_entries.account_id = @AccountId)");
                parameters.Add("AccountId", query.AccountId);
            }
            if (query.Type != null)
            {
                where.Add("transaction_type = @Type");
                parameters.Add("Type", query.Type);
            }
            if (query.BusinessKind != null)
            {
                where.Add("business_kind = @BusinessKind");
                parameters.Add("BusinessKind", query.BusinessKind);
            }
            if (query.Keyset != null)
            {
                where.Add("(occurred_utc < @CursorUtc OR " +
                    "(occurred_utc = @CursorUtc AND transaction_id < @CursorTransactionId))");
                parameters.Add("CursorUtc", query.Keyset.OccurredAtUtc.ToUnixTimeMilliseconds());
                parameters.Add("CursorTransactionId", query.Keyset.TransactionId);
            }

            using var connection = connectionFactory.Open();
            var rows = connection.Query<TransactionRow>(
                TransactionSelect +
                (where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where)) +
                " ORDER BY occurred_utc DESC, transaction_id DESC LIMIT @Take;",
                parameters).ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            var entries = LoadEntries(connection, null, pageRows.Select(row => row.TransactionId));
            var byTransaction = entries.ToLookup(entry => entry.TransactionId, StringComparer.Ordinal);
            var snapshots = pageRows.Select(row => ToTransaction(row, byTransaction[row.TransactionId])).ToArray();
            var next = rows.Length > query.PageSize && pageRows.Length > 0
                ? new TransactionKeyset(
                    DateTimeOffset.FromUnixTimeMilliseconds(pageRows[pageRows.Length - 1].OccurredUtc),
                    pageRows[pageRows.Length - 1].TransactionId)
                : null;
            return new TransactionPage(snapshots, next);
        }

        private static LedgerWriteResult CommitInTransaction(
            SqliteConnection connection,
            SqliteTransaction transaction,
            LedgerTransactionDraft draft)
        {
            ValidateDraft(draft);
            var existing = FindTransactionByIdempotency(connection, transaction, draft.IdempotencyKey);
            if (existing != null)
            {
                EnsureTransactionMatches(connection, transaction, existing, draft);
                return LoadWriteResult(connection, transaction, existing.TransactionId);
            }
            if (connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM economy_transactions WHERE transaction_id = @TransactionId;",
                new { draft.TransactionId }, transaction) != 0)
            {
                throw new EconomyIdempotencyConflictException();
            }

            var occurredUtc = draft.OccurredAtUtc.ToUnixTimeMilliseconds();
            foreach (var entry in draft.Entries)
            {
                if (SystemAccountIds.IsApproved(entry.AccountId))
                    EnsureSystemAccount(connection, transaction, entry.AccountId, occurredUtc);
            }

            var accountRows = new Dictionary<string, AccountRow>(StringComparer.Ordinal);
            foreach (var entry in draft.Entries)
            {
                if (accountRows.ContainsKey(entry.AccountId))
                    throw new ArgumentException(
                        "A transaction can contain only one entry per account.", nameof(draft));
                var account = connection.QuerySingleOrDefault<AccountRow>(
                    AccountSelect + " WHERE account_id = @AccountId;",
                    new { entry.AccountId }, transaction) ??
                    throw new EconomyAccountNotFoundException();
                accountRows.Add(entry.AccountId, account);
            }

            foreach (var entry in draft.Entries)
            {
                var account = accountRows[entry.AccountId];
                var playerDebit =
                    string.Equals(account.AccountKind, EconomyAccountKind.Player.ToString(), StringComparison.Ordinal) &&
                    entry.Side == LedgerSide.Debit;
                if (playerDebit &&
                    string.Equals(draft.ActorKind, EconomyActorKinds.Player, StringComparison.Ordinal))
                {
                    if (account.Enabled == 0) throw new EconomyAccountDisabledException();
                    if (account.IsFrozen != 0) throw new EconomyAccountFrozenException();
                }

                long next;
                try
                {
                    next = LedgerRules.Apply(
                        account.PostedBalance,
                        entry.Side,
                        entry.Amount,
                        string.Equals(account.AccountKind, EconomyAccountKind.System.ToString(), StringComparison.Ordinal));
                }
                catch (InvalidOperationException)
                {
                    throw new EconomyInsufficientFundsException();
                }
                if (playerDebit && next < account.ReservedDebit)
                    throw new EconomyInsufficientFundsException();

                var changed = connection.Execute(
                    @"UPDATE economy_accounts
                      SET posted_balance = @NextBalance,
                          updated_at_utc = @UpdatedAtUtc,
                          row_version = row_version + 1
                      WHERE account_id = @AccountId
                        AND row_version = @RowVersion
                        AND posted_balance = @PostedBalance
                        AND reserved_debit = @ReservedDebit;",
                    new
                    {
                        NextBalance = next,
                        UpdatedAtUtc = occurredUtc,
                        account.AccountId,
                        account.RowVersion,
                        account.PostedBalance,
                        account.ReservedDebit
                    }, transaction);
                if (changed != 1) throw new EconomyConcurrencyException();
                account.PostedBalance = next;
                account.UpdatedAtUtc = occurredUtc;
                account.RowVersion++;
            }

            connection.Execute(
                @"INSERT INTO economy_transactions (
                      transaction_id, transaction_type, idempotency_key, occurred_utc,
                      actor_kind, actor_id, related_crossplatform_id,
                      business_kind, business_id, correlation_id, reason, status)
                  VALUES (@TransactionId, @Type, @IdempotencyKey, @OccurredUtc,
                      @ActorKind, @ActorId, @RelatedCrossplatformId,
                      @BusinessKind, @BusinessId, @CorrelationId, @Reason, 'Committed');",
                new
                {
                    draft.TransactionId,
                    draft.Type,
                    draft.IdempotencyKey,
                    OccurredUtc = occurredUtc,
                    draft.ActorKind,
                    draft.ActorId,
                    draft.RelatedCrossplatformId,
                    draft.BusinessKind,
                    draft.BusinessId,
                    draft.CorrelationId,
                    draft.Reason
                }, transaction);
            for (var ordinal = 0; ordinal < draft.Entries.Count; ordinal++)
            {
                var entry = draft.Entries[ordinal];
                connection.Execute(
                    @"INSERT INTO economy_entries (
                          entry_id, transaction_id, account_id, ordinal,
                          side, amount, balance_after)
                      VALUES (@EntryId, @TransactionId, @AccountId, @Ordinal,
                          @Side, @Amount, @BalanceAfter);",
                    new
                    {
                        EntryId = Guid.NewGuid().ToString("D"),
                        draft.TransactionId,
                        entry.AccountId,
                        Ordinal = ordinal,
                        Side = entry.Side.ToString(),
                        entry.Amount,
                        BalanceAfter = accountRows[entry.AccountId].PostedBalance
                    }, transaction);
            }
            return LoadWriteResult(connection, transaction, draft.TransactionId);
        }

        private static void ValidateDraft(LedgerTransactionDraft draft)
        {
            RequireText(draft.TransactionId, nameof(draft));
            RequireText(draft.Type, nameof(draft));
            RequireText(draft.IdempotencyKey, nameof(draft));
            RequireUtc(draft.OccurredAtUtc, nameof(draft));
            RequireText(draft.ActorKind, nameof(draft));
            RequireText(draft.ActorId, nameof(draft));
            if (!LedgerRules.IsBalanced(draft.Entries.Select(
                entry => new LedgerEntryAmount(entry.Side, entry.Amount))))
            {
                throw new EconomyUnbalancedTransactionException();
            }
        }

        private static void EnsureTransactionMatches(
            SqliteConnection connection,
            SqliteTransaction transaction,
            TransactionRow existing,
            LedgerTransactionDraft draft)
        {
            if (!string.Equals(existing.TransactionId, draft.TransactionId, StringComparison.Ordinal) ||
                !string.Equals(existing.TransactionType, draft.Type, StringComparison.Ordinal) ||
                existing.OccurredUtc != draft.OccurredAtUtc.ToUnixTimeMilliseconds() ||
                !string.Equals(existing.ActorKind, draft.ActorKind, StringComparison.Ordinal) ||
                !string.Equals(existing.ActorId, draft.ActorId, StringComparison.Ordinal) ||
                !string.Equals(existing.RelatedCrossplatformId, draft.RelatedCrossplatformId, StringComparison.Ordinal) ||
                !string.Equals(existing.BusinessKind, draft.BusinessKind, StringComparison.Ordinal) ||
                !string.Equals(existing.BusinessId, draft.BusinessId, StringComparison.Ordinal) ||
                !string.Equals(existing.CorrelationId, draft.CorrelationId, StringComparison.Ordinal) ||
                !string.Equals(existing.Reason, draft.Reason, StringComparison.Ordinal))
            {
                throw new EconomyIdempotencyConflictException();
            }

            var entries = LoadEntries(connection, transaction, new[] { existing.TransactionId });
            if (entries.Count != draft.Entries.Count)
                throw new EconomyIdempotencyConflictException();
            for (var index = 0; index < entries.Count; index++)
            {
                if (!string.Equals(entries[index].AccountId, draft.Entries[index].AccountId, StringComparison.Ordinal) ||
                    !string.Equals(entries[index].Side, draft.Entries[index].Side.ToString(), StringComparison.Ordinal) ||
                    entries[index].Amount != draft.Entries[index].Amount)
                {
                    throw new EconomyIdempotencyConflictException();
                }
            }
        }

        private static void EnsureOpeningMatches(
            SqliteConnection connection,
            SqliteTransaction transaction,
            TransactionRow existing,
            string accountId,
            string crossplatformId,
            long openingAmount)
        {
            var issued = connection.ExecuteScalar<long?>(
                @"SELECT amount FROM economy_entries
                  WHERE transaction_id = @TransactionId AND account_id = @AccountId
                    AND side = 'Credit';",
                new { existing.TransactionId, AccountId = accountId }, transaction);
            if (!string.Equals(existing.TransactionType, "AccountOpening", StringComparison.Ordinal) ||
                !string.Equals(existing.RelatedCrossplatformId, crossplatformId, StringComparison.Ordinal) ||
                issued != openingAmount)
            {
                throw new EconomyIdempotencyConflictException();
            }
        }

        private static void EnsureReservationMatches(
            ReservationRow existing,
            FundsReservationDraft draft)
        {
            if (!string.Equals(existing.ReservationId, draft.ReservationId, StringComparison.Ordinal) ||
                !string.Equals(existing.AccountId, draft.AccountId, StringComparison.Ordinal) ||
                existing.Amount != draft.Amount ||
                !string.Equals(existing.BusinessKind, draft.BusinessKind, StringComparison.Ordinal) ||
                !string.Equals(existing.BusinessId, draft.BusinessId, StringComparison.Ordinal) ||
                existing.CreatedAtUtc != draft.OccurredAtUtc.ToUnixTimeMilliseconds() ||
                existing.ExpiresAtUtc != draft.ExpiresAtUtc?.ToUnixTimeMilliseconds())
            {
                throw new EconomyIdempotencyConflictException();
            }
        }

        private static FundsReservationResult FailedReservation(
            FundsReservationDraft reservation,
            AccountRow account,
            FundsReservationStatus status) => new FundsReservationResult(
                reservation.ReservationId,
                status,
                reservation.Amount,
                ToAccount(account),
                null);

        private static string CaptureSystemAccount(string businessKind) =>
            string.Equals(businessKind, "ShopPurchase", StringComparison.Ordinal)
                ? SystemAccountIds.Shop
                : SystemAccountIds.Recovery;

        private static void EnsureSystemAccount(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string accountId,
            long occurredUtc)
        {
            if (!SystemAccountIds.IsApproved(accountId))
                throw new EconomyAccountNotFoundException();
            connection.Execute(
                @"INSERT INTO economy_accounts (
                      account_id, account_kind, crossplatform_id, enabled, is_frozen,
                      posted_balance, reserved_debit, created_at_utc, updated_at_utc, row_version)
                  VALUES (@AccountId, 'System', NULL, 1, 0, 0, 0,
                      @OccurredUtc, @OccurredUtc, 0)
                  ON CONFLICT(account_id) DO NOTHING;",
                new { AccountId = accountId, OccurredUtc = occurredUtc }, transaction);
            var stored = GetAccount(connection, transaction, accountId);
            if (!string.Equals(stored.AccountKind, EconomyAccountKind.System.ToString(), StringComparison.Ordinal))
                throw new EconomyAccountNotFoundException();
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

    }
}
