using System;
using Dapper;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Rewards;
using Microsoft.Data.Sqlite;
using static LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce.SqliteCommerceStoreRows;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce
{
    public sealed partial class SqliteCommerceStore
    {
            public PurchaseReservationResult ReservePurchase(PurchaseReservationRequest request)
            {
                if (request == null) throw new ArgumentNullException(nameof(request));
                using var connection = connectionFactory.Open();
                using var transaction = connection.BeginTransaction(deferred: false);
                var existing = connection.QuerySingleOrDefault<PurchaseRow>(
                    PurchaseSelect + " WHERE p.idempotency_key = @IdempotencyKey;",
                    new { request.IdempotencyKey }, transaction);
                if (existing != null)
                {
                    if (!string.Equals(existing.ProductId, request.ProductId, StringComparison.Ordinal) ||
                        !string.Equals(existing.CrossplatformId, request.CrossplatformId, StringComparison.Ordinal) ||
                        existing.Quantity != request.Quantity)
                        throw new CommerceIdempotencyConflictException();
                    transaction.Commit();
                    return new PurchaseReservationResult(
                        PurchaseReservationStatus.Reserved,
                        ToPurchase(existing),
                        false);
                }
                if (connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM shop_purchases WHERE purchase_id = @PurchaseId;",
                        new { request.PurchaseId }, transaction) != 0 ||
                    connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM economy_reservations WHERE reservation_id = @ReservationId;",
                        new { request.ReservationId }, transaction) != 0)
                    throw new CommerceIdempotencyConflictException();

                var product = connection.QuerySingleOrDefault<ProductRow>(
                    ProductSelect + " WHERE product_id = @ProductId;",
                    new { request.ProductId }, transaction) ??
                    throw new KeyNotFoundException("The shop product does not exist.");
                if (product.Enabled == 0)
                    return Finish(transaction, PurchaseReservationStatus.ProductDisabled);
                if (product.StockRemaining.HasValue && product.StockRemaining.Value < request.Quantity)
                    return Finish(transaction, PurchaseReservationStatus.OutOfStock);

                var account = connection.QuerySingleOrDefault<AccountRow>(
                    @"SELECT account_id AS AccountId, crossplatform_id AS CrossplatformId,
                             enabled AS Enabled, is_frozen AS IsFrozen,
                             posted_balance AS PostedBalance, reserved_debit AS ReservedDebit,
                             row_version AS RowVersion
                      FROM economy_accounts
                      WHERE account_kind = 'Player' AND crossplatform_id = @CrossplatformId;",
                    new { request.CrossplatformId }, transaction) ??
                    throw new KeyNotFoundException("The player economy account does not exist.");
                if (account.Enabled == 0)
                    return Finish(transaction, PurchaseReservationStatus.AccountDisabled);
                if (account.IsFrozen != 0)
                    return Finish(transaction, PurchaseReservationStatus.AccountFrozen);

                var alreadyPurchased = connection.ExecuteScalar<long>(
                    @"SELECT COALESCE(SUM(quantity), 0) FROM shop_purchases
                      WHERE product_id = @ProductId AND crossplatform_id = @CrossplatformId
                        AND state IN ('Reserved', 'Dispatching', 'PendingReconciliation', 'Completed');",
                    new { request.ProductId, request.CrossplatformId }, transaction);
                if (product.PerPlayerLimit.HasValue &&
                    alreadyPurchased + request.Quantity > product.PerPlayerLimit.Value)
                    return Finish(transaction, PurchaseReservationStatus.PlayerLimitReached);

                long total;
                try { total = checked(product.PriceAmount * request.Quantity); }
                catch (OverflowException) { throw new ArgumentOutOfRangeException(nameof(request)); }
                if (account.PostedBalance - account.ReservedDebit < total)
                    return Finish(transaction, PurchaseReservationStatus.InsufficientFunds);

                var occurred = request.OccurredAtUtc.ToUnixTimeMilliseconds();
                if (product.StockRemaining.HasValue)
                {
                    var stockChanged = connection.Execute(
                        @"UPDATE shop_products
                          SET stock_remaining = stock_remaining - @Quantity,
                              updated_at_utc = @Occurred, row_version = row_version + 1
                          WHERE product_id = @ProductId AND row_version = @RowVersion
                            AND enabled = 1 AND stock_remaining >= @Quantity;",
                        new
                        {
                            request.Quantity,
                            Occurred = occurred,
                            request.ProductId,
                            product.RowVersion
                        }, transaction);
                    if (stockChanged != 1) throw new CommerceConcurrencyException();
                }
                var accountChanged = connection.Execute(
                    @"UPDATE economy_accounts
                      SET reserved_debit = reserved_debit + @Amount,
                          updated_at_utc = @Occurred, row_version = row_version + 1
                      WHERE account_id = @AccountId AND row_version = @RowVersion
                        AND enabled = 1 AND is_frozen = 0
                        AND posted_balance - reserved_debit >= @Amount;",
                    new
                    {
                        Amount = total,
                        Occurred = occurred,
                        account.AccountId,
                        account.RowVersion
                    }, transaction);
                if (accountChanged != 1) throw new CommerceConcurrencyException();

                connection.Execute(
                    @"INSERT INTO economy_reservations (
                          reservation_id, account_id, amount, state, idempotency_key,
                          business_kind, business_id, captured_transaction_id,
                          created_at_utc, updated_at_utc, expires_at_utc, row_version)
                      VALUES (@ReservationId, @AccountId, @Amount, 'Reserved',
                          @ReservationKey, 'ShopPurchase', @PurchaseId, NULL,
                          @Occurred, @Occurred, @ExpiresAt, 0);",
                    new
                    {
                        request.ReservationId,
                        account.AccountId,
                        Amount = total,
                        ReservationKey = "purchase-reservation:" + request.IdempotencyKey,
                        request.PurchaseId,
                        Occurred = occurred,
                        ExpiresAt = request.ExpiresAtUtc?.ToUnixTimeMilliseconds()
                    }, transaction);
                connection.Execute(
                    @"INSERT INTO shop_purchases (
                          purchase_id, product_id, crossplatform_id, quantity,
                          unit_price, total_amount, state, idempotency_key,
                          reservation_id, captured_transaction_id, grant_operation_id,
                          correlation_id, error_code, created_at_utc, updated_at_utc,
                          completed_at_utc, row_version)
                      VALUES (@PurchaseId, @ProductId, @CrossplatformId, @Quantity,
                          @UnitPrice, @TotalAmount, 'Reserved', @IdempotencyKey,
                          @ReservationId, NULL, NULL, @CorrelationId, NULL,
                          @Occurred, @Occurred, NULL, 0);",
                    new
                    {
                        request.PurchaseId,
                        request.ProductId,
                        request.CrossplatformId,
                        request.Quantity,
                        UnitPrice = product.PriceAmount,
                        TotalAmount = total,
                        request.IdempotencyKey,
                        request.ReservationId,
                        request.CorrelationId,
                        Occurred = occurred
                    }, transaction);
                var purchase = LoadPurchase(connection, transaction, request.PurchaseId);
                transaction.Commit();
                return new PurchaseReservationResult(
                    PurchaseReservationStatus.Reserved,
                    purchase,
                    true);
            }

            public ShopPurchaseSnapshot GetPurchase(string purchaseId)
            {
                purchaseId = RequireText(purchaseId, nameof(purchaseId));
                using var connection = connectionFactory.Open();
                return LoadPurchase(connection, null, purchaseId);
            }

            public ShopPurchaseSnapshot? TryStartPurchaseDispatch(
                string purchaseId,
                DateTimeOffset occurredAtUtc)
            {
                purchaseId = RequireText(purchaseId, nameof(purchaseId));
                RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
                using var connection = connectionFactory.Open();
                using var transaction = connection.BeginTransaction(deferred: false);
                var changed = connection.Execute(
                    @"UPDATE shop_purchases
                      SET state = 'Dispatching', updated_at_utc = @Occurred,
                          row_version = row_version + 1
                      WHERE purchase_id = @PurchaseId AND state = 'Reserved';",
                    new
                    {
                        PurchaseId = purchaseId,
                        Occurred = occurredAtUtc.ToUnixTimeMilliseconds()
                    }, transaction);
                var result = changed == 1 ? LoadPurchase(connection, transaction, purchaseId) : null;
                transaction.Commit();
                return result;
            }

            public ShopPurchaseSnapshot ResolvePurchaseGrant(PurchaseGrantResolution resolution)
            {
                if (resolution == null) throw new ArgumentNullException(nameof(resolution));
                using var connection = connectionFactory.Open();
                using var transaction = connection.BeginTransaction(deferred: false);
                var purchase = connection.QuerySingleOrDefault<PurchaseRow>(
                    PurchaseSelect + " WHERE p.purchase_id = @PurchaseId;",
                    new { resolution.PurchaseId }, transaction) ??
                    throw new KeyNotFoundException("The shop purchase does not exist.");
                if (purchase.State == "Completed" || purchase.State == "Failed" ||
                    purchase.State == "PendingReconciliation")
                {
                    transaction.Commit();
                    return ToPurchase(purchase);
                }
                if (purchase.State != "Dispatching" && purchase.State != "Reserved")
                    throw new CommerceConcurrencyException();

                switch (resolution.Kind)
                {
                    case CommerceGrantResolutionKind.Completed:
                        CapturePurchase(connection, transaction, purchase, resolution);
                        break;
                    case CommerceGrantResolutionKind.FailedBeforeSideEffects:
                        ReleasePurchase(connection, transaction, purchase, resolution);
                        break;
                    case CommerceGrantResolutionKind.PendingReconciliation:
                        connection.Execute(
                            @"UPDATE shop_purchases
                              SET state = 'PendingReconciliation', grant_operation_id = @GrantOperationId,
                                  error_code = @ErrorCode, updated_at_utc = @Occurred,
                                  row_version = row_version + 1
                              WHERE purchase_id = @PurchaseId
                                AND state IN ('Reserved', 'Dispatching');",
                            new
                            {
                                resolution.GrantOperationId,
                                resolution.ErrorCode,
                                Occurred = resolution.OccurredAtUtc.ToUnixTimeMilliseconds(),
                                resolution.PurchaseId
                            }, transaction);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                var result = LoadPurchase(connection, transaction, resolution.PurchaseId);
                transaction.Commit();
                return result;
            }

            private static PurchaseReservationResult Finish(
                SqliteTransaction transaction,
                PurchaseReservationStatus status)
            {
                transaction.Commit();
                return new PurchaseReservationResult(status, null, false);
            }

            private static void CapturePurchase(
                SqliteConnection connection,
                SqliteTransaction transaction,
                PurchaseRow purchase,
                PurchaseGrantResolution resolution)
            {
                if (purchase.ReservationId == null || resolution.CaptureTransactionId == null)
                    throw new CommerceConcurrencyException();
                var reservation = connection.QuerySingleOrDefault<ReservationRow>(
                    @"SELECT reservation_id AS ReservationId, account_id AS AccountId,
                             amount AS Amount, state AS State, business_kind AS BusinessKind,
                             business_id AS BusinessId, row_version AS RowVersion
                      FROM economy_reservations WHERE reservation_id = @ReservationId;",
                    new { purchase.ReservationId }, transaction) ??
                    throw new CommerceConcurrencyException();
                if (reservation.State != "Reserved" || reservation.Amount != purchase.TotalAmount)
                    throw new CommerceConcurrencyException();
                var account = connection.QuerySingle<AccountRow>(
                    @"SELECT account_id AS AccountId, crossplatform_id AS CrossplatformId,
                             enabled AS Enabled, is_frozen AS IsFrozen,
                             posted_balance AS PostedBalance, reserved_debit AS ReservedDebit,
                             row_version AS RowVersion
                      FROM economy_accounts WHERE account_id = @AccountId;",
                    new { reservation.AccountId }, transaction);
                var occurred = resolution.OccurredAtUtc.ToUnixTimeMilliseconds();
                var playerBalance = checked(account.PostedBalance - reservation.Amount);
                if (playerBalance < 0 || account.ReservedDebit < reservation.Amount)
                    throw new CommerceConcurrencyException();
                var accountChanged = connection.Execute(
                    @"UPDATE economy_accounts
                      SET posted_balance = @PostedBalance,
                          reserved_debit = reserved_debit - @Amount,
                          updated_at_utc = @Occurred, row_version = row_version + 1
                      WHERE account_id = @AccountId AND row_version = @RowVersion
                        AND posted_balance = @OldPostedBalance
                        AND reserved_debit >= @Amount;",
                    new
                    {
                        PostedBalance = playerBalance,
                        Amount = reservation.Amount,
                        Occurred = occurred,
                        account.AccountId,
                        account.RowVersion,
                        OldPostedBalance = account.PostedBalance
                    }, transaction);
                if (accountChanged != 1) throw new CommerceConcurrencyException();

                connection.Execute(
                    @"INSERT INTO economy_accounts (
                          account_id, account_kind, crossplatform_id, enabled, is_frozen,
                          posted_balance, reserved_debit, created_at_utc, updated_at_utc, row_version)
                      VALUES (@AccountId, 'System', NULL, 1, 0, 0, 0,
                          @Occurred, @Occurred, 0)
                      ON CONFLICT(account_id) DO NOTHING;",
                    new { AccountId = SystemAccountIds.Shop, Occurred = occurred }, transaction);
                var shop = connection.QuerySingle<SystemAccountRow>(
                    @"SELECT posted_balance AS PostedBalance, row_version AS RowVersion
                      FROM economy_accounts WHERE account_id = @AccountId;",
                    new { AccountId = SystemAccountIds.Shop }, transaction);
                var shopBalance = checked(shop.PostedBalance + reservation.Amount);
                if (connection.Execute(
                        @"UPDATE economy_accounts
                          SET posted_balance = @PostedBalance, updated_at_utc = @Occurred,
                              row_version = row_version + 1
                          WHERE account_id = @AccountId AND row_version = @RowVersion;",
                        new
                        {
                            PostedBalance = shopBalance,
                            Occurred = occurred,
                            AccountId = SystemAccountIds.Shop,
                            shop.RowVersion
                        }, transaction) != 1)
                    throw new CommerceConcurrencyException();

                var transactionId = resolution.CaptureTransactionId;
                connection.Execute(
                    @"INSERT INTO economy_transactions (
                          transaction_id, transaction_type, idempotency_key, occurred_utc,
                          actor_kind, actor_id, related_crossplatform_id,
                          business_kind, business_id, correlation_id, reason, status)
                      VALUES (@TransactionId, 'ReservationCapture', @IdempotencyKey, @Occurred,
                          'System', @ActorId, @CrossplatformId, 'ShopPurchase', @PurchaseId,
                          @CorrelationId, NULL, 'Committed');",
                    new
                    {
                        TransactionId = transactionId,
                        IdempotencyKey = "purchase-capture:" + purchase.PurchaseId,
                        Occurred = occurred,
                        ActorId = SystemAccountIds.Shop,
                        purchase.CrossplatformId,
                        purchase.PurchaseId,
                        purchase.CorrelationId
                    }, transaction);
                connection.Execute(
                    @"INSERT INTO economy_entries (
                          entry_id, transaction_id, account_id, ordinal, side, amount, balance_after)
                      VALUES (@PlayerEntryId, @TransactionId, @PlayerAccountId, 0, 'Debit', @Amount, @PlayerBalance),
                             (@ShopEntryId, @TransactionId, @ShopAccountId, 1, 'Credit', @Amount, @ShopBalance);",
                    new
                    {
                        PlayerEntryId = Guid.NewGuid().ToString("D"),
                        ShopEntryId = Guid.NewGuid().ToString("D"),
                        TransactionId = transactionId,
                        PlayerAccountId = account.AccountId,
                        ShopAccountId = SystemAccountIds.Shop,
                        Amount = reservation.Amount,
                        PlayerBalance = playerBalance,
                        ShopBalance = shopBalance
                    }, transaction);
                if (connection.Execute(
                        @"UPDATE economy_reservations
                          SET state = 'Captured', captured_transaction_id = @TransactionId,
                              updated_at_utc = @Occurred, row_version = row_version + 1
                          WHERE reservation_id = @ReservationId AND state = 'Reserved'
                            AND row_version = @RowVersion;",
                        new
                        {
                            TransactionId = transactionId,
                            Occurred = occurred,
                            reservation.ReservationId,
                            reservation.RowVersion
                        }, transaction) != 1)
                    throw new CommerceConcurrencyException();
                if (connection.Execute(
                        @"UPDATE shop_purchases
                          SET state = 'Completed', captured_transaction_id = @TransactionId,
                              grant_operation_id = @GrantOperationId, error_code = NULL,
                              updated_at_utc = @Occurred, completed_at_utc = @Occurred,
                              row_version = row_version + 1
                          WHERE purchase_id = @PurchaseId
                            AND state IN ('Reserved', 'Dispatching');",
                        new
                        {
                            TransactionId = transactionId,
                            resolution.GrantOperationId,
                            Occurred = occurred,
                            purchase.PurchaseId
                        }, transaction) != 1)
                    throw new CommerceConcurrencyException();
            }

            private static void ReleasePurchase(
                SqliteConnection connection,
                SqliteTransaction transaction,
                PurchaseRow purchase,
                PurchaseGrantResolution resolution)
            {
                if (purchase.ReservationId == null) throw new CommerceConcurrencyException();
                var reservation = connection.QuerySingle<ReservationRow>(
                    @"SELECT reservation_id AS ReservationId, account_id AS AccountId,
                             amount AS Amount, state AS State, business_kind AS BusinessKind,
                             business_id AS BusinessId, row_version AS RowVersion
                      FROM economy_reservations WHERE reservation_id = @ReservationId;",
                    new { purchase.ReservationId }, transaction);
                if (reservation.State != "Reserved") throw new CommerceConcurrencyException();
                var occurred = resolution.OccurredAtUtc.ToUnixTimeMilliseconds();
                if (connection.Execute(
                        @"UPDATE economy_accounts
                          SET reserved_debit = reserved_debit - @Amount,
                              updated_at_utc = @Occurred, row_version = row_version + 1
                          WHERE account_id = @AccountId AND reserved_debit >= @Amount;",
                        new
                        {
                            reservation.Amount,
                            Occurred = occurred,
                            reservation.AccountId
                        }, transaction) != 1)
                    throw new CommerceConcurrencyException();
                if (connection.Execute(
                        @"UPDATE economy_reservations
                          SET state = 'Released', updated_at_utc = @Occurred,
                              row_version = row_version + 1
                          WHERE reservation_id = @ReservationId AND state = 'Reserved'
                            AND row_version = @RowVersion;",
                        new
                        {
                            Occurred = occurred,
                            reservation.ReservationId,
                            reservation.RowVersion
                        }, transaction) != 1)
                    throw new CommerceConcurrencyException();
                connection.Execute(
                    @"UPDATE shop_products
                      SET stock_remaining = stock_remaining + @Quantity,
                          updated_at_utc = @Occurred, row_version = row_version + 1
                      WHERE product_id = @ProductId AND stock_remaining IS NOT NULL;",
                    new { purchase.Quantity, Occurred = occurred, purchase.ProductId }, transaction);
                if (connection.Execute(
                        @"UPDATE shop_purchases
                          SET state = 'Failed', grant_operation_id = @GrantOperationId,
                              error_code = @ErrorCode, updated_at_utc = @Occurred,
                              completed_at_utc = @Occurred, row_version = row_version + 1
                          WHERE purchase_id = @PurchaseId
                            AND state IN ('Reserved', 'Dispatching');",
                        new
                        {
                            resolution.GrantOperationId,
                            resolution.ErrorCode,
                            Occurred = occurred,
                            purchase.PurchaseId
                        }, transaction) != 1)
                    throw new CommerceConcurrencyException();
            }

    }
}
