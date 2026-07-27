using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Rewards;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce
{
    public sealed class SqliteCommerceStore :
        ICommerceStore,
        IShopCatalogQueryStore,
        IDailyRewardClaimStore,
        IDailyRewardPolicyStore
    {
        private const string ProductSelect = @"SELECT
            product_id AS ProductId, name AS Name, description AS Description,
            enabled AS Enabled, price_amount AS PriceAmount,
            stock_remaining AS StockRemaining, per_player_limit AS PerPlayerLimit,
            reward_package_id AS RewardPackageId, sort_order AS SortOrder,
            created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc,
            row_version AS RowVersion
            FROM shop_products";

        private const string PurchaseSelect = @"SELECT
            p.purchase_id AS PurchaseId, p.product_id AS ProductId,
            product.reward_package_id AS RewardPackageId,
            p.crossplatform_id AS CrossplatformId, p.quantity AS Quantity,
            p.unit_price AS UnitPrice, p.total_amount AS TotalAmount,
            p.state AS State, p.idempotency_key AS IdempotencyKey,
            p.reservation_id AS ReservationId,
            p.captured_transaction_id AS CapturedTransactionId,
            p.grant_operation_id AS GrantOperationId,
            p.correlation_id AS CorrelationId, p.error_code AS ErrorCode,
            p.created_at_utc AS CreatedAtUtc, p.updated_at_utc AS UpdatedAtUtc,
            p.completed_at_utc AS CompletedAtUtc, p.row_version AS RowVersion
            FROM shop_purchases p
            JOIN shop_products product ON product.product_id = p.product_id";

        private const string CodeSelect = @"SELECT
            code_id AS CodeId, masked_prefix AS LastFour,
            normalization_version AS NormalizationVersion,
            reward_package_id AS RewardPackageId, enabled AS Enabled,
            valid_from_utc AS ValidFromUtc, expires_at_utc AS ExpiresAtUtc,
            max_redemptions AS MaxRedemptions, per_player_limit AS PerPlayerLimit,
            redemption_count AS RedemptionCount, created_at_utc AS CreatedAtUtc,
            updated_at_utc AS UpdatedAtUtc, row_version AS RowVersion
            FROM redeem_codes";

        private const string AttemptSelect = @"SELECT
            attempt.attempt_id AS AttemptId, attempt.code_id AS CodeId,
            code.reward_package_id AS RewardPackageId,
            attempt.crossplatform_id AS CrossplatformId,
            attempt.result AS State, attempt.result_code AS ResultCode,
            attempt.grant_operation_id AS GrantOperationId,
            attempt.correlation_id AS CorrelationId,
            attempt.attempted_at_utc AS AttemptedAtUtc
            FROM redeem_attempts attempt
            JOIN redeem_codes code ON code.code_id = attempt.code_id";

        private const string EligibilitySelect = @"SELECT
            eligibility_id AS EligibilityId, rule_kind AS RuleKind,
            rule_id AS RuleId,
            CASE
                WHEN rule_kind = 'Achievement' THEN
                    (SELECT reward_package_id FROM achievement_definitions a
                     WHERE a.achievement_id = reward_eligibilities.rule_id)
                ELSE
                    (SELECT reward_package_id FROM online_reward_rules o
                     WHERE o.rule_id = reward_eligibilities.rule_id)
            END AS RewardPackageId,
            crossplatform_id AS CrossplatformId, eligibility_key AS EligibilityKey,
            state AS State, grant_operation_id AS GrantOperationId,
            correlation_id AS CorrelationId,
            evidence_from_utc AS EvidenceFromUtc, evidence_to_utc AS EvidenceToUtc,
            created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc,
            row_version AS RowVersion
            FROM reward_eligibilities";

        private const string DailyClaimSelect = @"SELECT
            claim_id AS ClaimId, rule_id AS RuleId,
            reward_package_id AS RewardPackageId,
            crossplatform_id AS CrossplatformId, period_key AS PeriodKey,
            period_start_utc AS PeriodStartUtc, period_end_utc AS PeriodEndUtc,
            state AS State, idempotency_key AS IdempotencyKey,
            expected_entity_id AS ExpectedEntityId,
            expected_world_id AS ExpectedWorldId,
            grant_operation_id AS GrantOperationId,
            correlation_id AS CorrelationId, error_code AS ErrorCode,
            created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc,
            completed_at_utc AS CompletedAtUtc, row_version AS RowVersion
            FROM daily_reward_claims";

        private const string DailyPolicySelect = @"SELECT
            rule_id AS RuleId, reward_package_id AS RewardPackageId,
            enabled AS Enabled, created_at_utc AS CreatedAtUtc,
            updated_at_utc AS UpdatedAtUtc, row_version AS RowVersion
            FROM daily_reward_policies";

        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteCommerceStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));

        public ShopProductSnapshot SaveProduct(
            ShopProductDraft product,
            DateTimeOffset occurredAtUtc)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var occurred = occurredAtUtc.ToUnixTimeMilliseconds();
            connection.Execute(
                @"INSERT INTO shop_products (
                      product_id, name, description, enabled, price_amount,
                      stock_remaining, per_player_limit, reward_package_id,
                      sort_order, created_at_utc, updated_at_utc, row_version)
                  VALUES (@ProductId, @Name, @Description, @Enabled, @PriceAmount,
                      @StockRemaining, @PerPlayerLimit, @RewardPackageId,
                      @SortOrder, @Occurred, @Occurred, 0)
                  ON CONFLICT(product_id) DO UPDATE SET
                      name = excluded.name, description = excluded.description,
                      enabled = excluded.enabled, price_amount = excluded.price_amount,
                      stock_remaining = excluded.stock_remaining,
                      per_player_limit = excluded.per_player_limit,
                      reward_package_id = excluded.reward_package_id,
                      sort_order = excluded.sort_order,
                      updated_at_utc = excluded.updated_at_utc,
                      row_version = shop_products.row_version + 1;",
                new
                {
                    product.ProductId,
                    product.Name,
                    product.Description,
                    Enabled = product.Enabled ? 1 : 0,
                    product.PriceAmount,
                    product.StockRemaining,
                    product.PerPlayerLimit,
                    product.RewardPackageId,
                    product.SortOrder,
                    Occurred = occurred
                }, transaction);
            var result = LoadProduct(connection, transaction, product.ProductId);
            transaction.Commit();
            return result;
        }

        public ShopProductSnapshot GetProduct(string productId)
        {
            productId = RequireText(productId, nameof(productId));
            using var connection = connectionFactory.Open();
            return LoadProduct(connection, null, productId);
        }

        public ShopProductPage QueryEnabledProducts(ShopProductKeysetQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            using var connection = connectionFactory.Open();
            var rows = connection.Query<ProductRow>(
                    ProductSelect + @"
                    WHERE enabled = 1" +
                    (query.After == null
                        ? string.Empty
                        : @" AND (sort_order > @SortOrder OR
                                  (sort_order = @SortOrder AND product_id > @ProductId))") + @"
                    ORDER BY sort_order ASC, product_id ASC
                    LIMIT @Take;",
                    query.After == null
                        ? new { Take = query.PageSize + 1 }
                        : new
                        {
                            query.After.SortOrder,
                            query.After.ProductId,
                            Take = query.PageSize + 1
                        })
                .ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            var next = rows.Length > query.PageSize && pageRows.Length > 0
                ? new ShopProductCursor(
                    pageRows[pageRows.Length - 1].SortOrder,
                    pageRows[pageRows.Length - 1].ProductId)
                : null;
            return new ShopProductPage(pageRows.Select(ToProduct), next);
        }

        public DailyRewardClaimCreationResult GetOrCreateDailyRewardClaim(
            DailyRewardClaimDraft claim)
        {
            if (claim == null) throw new ArgumentNullException(nameof(claim));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var existing = connection.QuerySingleOrDefault<DailyClaimRow>(
                DailyClaimSelect + @"
                WHERE rule_id = @RuleId AND crossplatform_id = @CrossplatformId
                  AND period_key = @PeriodKey;",
                new { claim.RuleId, claim.CrossplatformId, claim.PeriodKey },
                transaction);
            if (existing != null)
            {
                EnsureDailyClaimMatches(existing, claim);
                transaction.Commit();
                return new DailyRewardClaimCreationResult(ToDailyClaim(existing), false);
            }

            var replay = connection.QuerySingleOrDefault<DailyClaimRow>(
                DailyClaimSelect + " WHERE idempotency_key = @IdempotencyKey;",
                new { claim.IdempotencyKey },
                transaction);
            if (replay != null)
            {
                EnsureDailyClaimMatches(replay, claim);
                transaction.Commit();
                return new DailyRewardClaimCreationResult(ToDailyClaim(replay), false);
            }

            connection.Execute(
                @"INSERT INTO daily_reward_claims (
                      claim_id, rule_id, reward_package_id, crossplatform_id,
                      period_key, period_start_utc, period_end_utc, state,
                      idempotency_key, expected_entity_id, expected_world_id,
                      grant_operation_id, correlation_id, error_code,
                      created_at_utc, updated_at_utc, completed_at_utc, row_version)
                  VALUES (@ClaimId, @RuleId, @RewardPackageId, @CrossplatformId,
                      @PeriodKey, @PeriodStartUtc, @PeriodEndUtc, 'Reserved',
                      @IdempotencyKey, @ExpectedEntityId, @ExpectedWorldId,
                      NULL, @CorrelationId, NULL, @CreatedAtUtc, @CreatedAtUtc, NULL, 0);",
                new
                {
                    claim.ClaimId,
                    claim.RuleId,
                    claim.RewardPackageId,
                    claim.CrossplatformId,
                    claim.PeriodKey,
                    PeriodStartUtc = claim.PeriodStartUtc.ToUnixTimeMilliseconds(),
                    PeriodEndUtc = claim.PeriodEndUtc.ToUnixTimeMilliseconds(),
                    claim.IdempotencyKey,
                    claim.ExpectedEntityId,
                    claim.ExpectedWorldId,
                    claim.CorrelationId,
                    CreatedAtUtc = claim.CreatedAtUtc.ToUnixTimeMilliseconds()
                },
                transaction);
            var created = LoadDailyClaim(connection, transaction, claim.ClaimId);
            transaction.Commit();
            return new DailyRewardClaimCreationResult(created, true);
        }

        public DailyRewardClaimSnapshot GetDailyRewardClaim(string claimId)
        {
            claimId = RequireText(claimId, nameof(claimId));
            using var connection = connectionFactory.Open();
            return LoadDailyClaim(connection, null, claimId);
        }

        public DailyRewardPolicySnapshot SaveDailyRewardPolicy(
            DailyRewardPolicyDraft policy,
            DateTimeOffset occurredAtUtc)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var existing = connection.QuerySingleOrDefault<DailyPolicyRow>(
                DailyPolicySelect + " WHERE rule_id = @RuleId;",
                new { policy.RuleId }, transaction);
            var occurred = occurredAtUtc.ToUnixTimeMilliseconds();
            if (existing == null)
            {
                if (policy.ExpectedRowVersion.HasValue)
                    throw new DailyRewardPolicyConcurrencyException();
                connection.Execute(
                    @"INSERT INTO daily_reward_policies (
                          rule_id, reward_package_id, enabled,
                          created_at_utc, updated_at_utc, row_version)
                      VALUES (@RuleId, @RewardPackageId, @Enabled,
                          @OccurredAtUtc, @OccurredAtUtc, 0);",
                    new
                    {
                        policy.RuleId,
                        policy.RewardPackageId,
                        Enabled = policy.Enabled ? 1 : 0,
                        OccurredAtUtc = occurred
                    }, transaction);
            }
            else
            {
                if (!policy.ExpectedRowVersion.HasValue ||
                    policy.ExpectedRowVersion.Value != existing.RowVersion)
                {
                    throw new DailyRewardPolicyConcurrencyException();
                }
                var updated = connection.Execute(
                    @"UPDATE daily_reward_policies
                      SET reward_package_id = @RewardPackageId,
                          enabled = @Enabled,
                          updated_at_utc = @OccurredAtUtc,
                          row_version = row_version + 1
                      WHERE rule_id = @RuleId
                        AND row_version = @ExpectedRowVersion;",
                    new
                    {
                        policy.RuleId,
                        policy.RewardPackageId,
                        Enabled = policy.Enabled ? 1 : 0,
                        OccurredAtUtc = occurred,
                        policy.ExpectedRowVersion
                    }, transaction);
                if (updated != 1) throw new DailyRewardPolicyConcurrencyException();
            }

            var stored = connection.QuerySingle<DailyPolicyRow>(
                DailyPolicySelect + " WHERE rule_id = @RuleId;",
                new { policy.RuleId }, transaction);
            transaction.Commit();
            return ToDailyPolicy(stored);
        }

        public DailyRewardPolicySnapshot GetDailyRewardPolicy(string ruleId)
        {
            ruleId = RequireText(ruleId, nameof(ruleId));
            using var connection = connectionFactory.Open();
            var stored = connection.QuerySingleOrDefault<DailyPolicyRow>(
                DailyPolicySelect + " WHERE rule_id = @RuleId;",
                new { RuleId = ruleId });
            return stored == null
                ? throw new KeyNotFoundException("The daily reward policy does not exist.")
                : ToDailyPolicy(stored);
        }

        public bool TryStartDailyRewardClaim(
            string claimId,
            long expectedRowVersion,
            DateTimeOffset occurredAtUtc)
        {
            claimId = RequireText(claimId, nameof(claimId));
            if (expectedRowVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE daily_reward_claims
                  SET state = 'Dispatching', updated_at_utc = @OccurredAtUtc,
                      row_version = row_version + 1
                  WHERE claim_id = @ClaimId AND state = 'Reserved'
                    AND row_version = @ExpectedRowVersion;",
                new
                {
                    ClaimId = claimId,
                    ExpectedRowVersion = expectedRowVersion,
                    OccurredAtUtc = occurredAtUtc.ToUnixTimeMilliseconds()
                }) == 1;
        }

        public bool TryResolveDailyRewardClaim(
            string claimId,
            long expectedRowVersion,
            DailyRewardClaimState state,
            string? grantOperationId,
            string? errorCode,
            DateTimeOffset occurredAtUtc)
        {
            claimId = RequireText(claimId, nameof(claimId));
            if (expectedRowVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            if (state != DailyRewardClaimState.Completed &&
                state != DailyRewardClaimState.Failed &&
                state != DailyRewardClaimState.PendingReconciliation)
                throw new ArgumentOutOfRangeException(nameof(state));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE daily_reward_claims
                  SET state = @State, grant_operation_id = @GrantOperationId,
                      error_code = @ErrorCode, updated_at_utc = @OccurredAtUtc,
                      completed_at_utc = CASE
                          WHEN @State IN ('Completed', 'Failed') THEN @OccurredAtUtc
                          ELSE NULL END,
                      row_version = row_version + 1
                  WHERE claim_id = @ClaimId AND state = 'Dispatching'
                    AND row_version = @ExpectedRowVersion;",
                new
                {
                    ClaimId = claimId,
                    ExpectedRowVersion = expectedRowVersion,
                    State = state.ToString(),
                    GrantOperationId = NullIfWhiteSpace(grantOperationId),
                    ErrorCode = NullIfWhiteSpace(errorCode),
                    OccurredAtUtc = occurredAtUtc.ToUnixTimeMilliseconds()
                }) == 1;
        }

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

        public RedeemCodeSnapshot SaveRedeemCode(
            RedeemCodeSecretDraft definition,
            DateTimeOffset occurredAtUtc)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var occurred = occurredAtUtc.ToUnixTimeMilliseconds();
            connection.Execute(
                @"INSERT INTO redeem_codes (
                      code_id, normalized_code_digest, masked_prefix,
                      normalization_version, reward_package_id, enabled,
                      valid_from_utc, expires_at_utc, max_redemptions,
                      per_player_limit, redemption_count, created_at_utc,
                      updated_at_utc, row_version)
                  VALUES (@CodeId, @NormalizedCodeDigest, @LastFour,
                      @NormalizationVersion, @RewardPackageId, @Enabled,
                      @ValidFromUtc, @ExpiresAtUtc, @MaxRedemptions,
                      @PerPlayerLimit, 0, @Occurred, @Occurred, 0);",
                new
                {
                    definition.CodeId,
                    definition.NormalizedCodeDigest,
                    definition.LastFour,
                    definition.NormalizationVersion,
                    definition.RewardPackageId,
                    Enabled = definition.Enabled ? 1 : 0,
                    ValidFromUtc = definition.ValidFromUtc?.ToUnixTimeMilliseconds(),
                    ExpiresAtUtc = definition.ExpiresAtUtc?.ToUnixTimeMilliseconds(),
                    definition.MaxRedemptions,
                    definition.PerPlayerLimit,
                    Occurred = occurred
                }, transaction);
            var result = LoadCode(connection, transaction, definition.CodeId);
            transaction.Commit();
            return result;
        }

        public RedeemCodeSnapshot GetRedeemCode(string codeId)
        {
            codeId = RequireText(codeId, nameof(codeId));
            using var connection = connectionFactory.Open();
            return LoadCode(connection, null, codeId);
        }

        public RedemptionReservationResult ReserveRedemption(RedeemReservationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var code = connection.QuerySingleOrDefault<CodeSecretRow>(
                @"SELECT code_id AS CodeId, normalized_code_digest AS Digest,
                         reward_package_id AS RewardPackageId, enabled AS Enabled,
                         valid_from_utc AS ValidFromUtc, expires_at_utc AS ExpiresAtUtc,
                         max_redemptions AS MaxRedemptions,
                         per_player_limit AS PerPlayerLimit,
                         redemption_count AS RedemptionCount, row_version AS RowVersion
                  FROM redeem_codes
                  WHERE normalization_version = @NormalizationVersion
                    AND normalized_code_digest = @NormalizedCodeDigest;",
                new { request.NormalizationVersion, request.NormalizedCodeDigest }, transaction);
            if (code == null)
            {
                transaction.Commit();
                return new RedemptionReservationResult(
                    RedeemReservationStatus.InvalidCode, null, false);
            }

            var existing = connection.QuerySingleOrDefault<AttemptRow>(
                AttemptSelect + @" WHERE attempt.code_id = @CodeId
                    AND attempt.crossplatform_id = @CrossplatformId
                    AND attempt.normalized_code_digest = @NormalizedCodeDigest;",
                new { code.CodeId, request.CrossplatformId, request.NormalizedCodeDigest }, transaction);
            if (existing != null)
            {
                transaction.Commit();
                return new RedemptionReservationResult(
                    RedeemReservationStatus.Reserved, ToAttempt(existing), false);
            }

            var now = request.AttemptedAtUtc.ToUnixTimeMilliseconds();
            var rejection = code.Enabled == 0
                ? RedeemReservationStatus.Disabled
                : code.ValidFromUtc.HasValue && now < code.ValidFromUtc.Value
                    ? RedeemReservationStatus.NotYetValid
                    : code.ExpiresAtUtc.HasValue && now >= code.ExpiresAtUtc.Value
                        ? RedeemReservationStatus.Expired
                        : code.MaxRedemptions.HasValue && code.RedemptionCount >= code.MaxRedemptions.Value
                            ? RedeemReservationStatus.GlobalLimitReached
                            : (RedeemReservationStatus?)null;
            if (!rejection.HasValue && code.PerPlayerLimit.HasValue)
            {
                var playerCount = connection.ExecuteScalar<int>(
                    @"SELECT COUNT(*) FROM redeem_attempts
                      WHERE code_id = @CodeId AND crossplatform_id = @CrossplatformId
                        AND result IN ('Pending', 'Succeeded', 'PendingReconciliation');",
                    new { code.CodeId, request.CrossplatformId }, transaction);
                if (playerCount >= code.PerPlayerLimit.Value)
                    rejection = RedeemReservationStatus.PlayerLimitReached;
            }
            if (rejection.HasValue)
            {
                InsertAttempt(
                    connection, transaction, request, code.CodeId,
                    "Rejected", RejectionCode(rejection.Value));
                var rejected = LoadAttempt(connection, transaction, request.AttemptId);
                transaction.Commit();
                return new RedemptionReservationResult(rejection.Value, rejected, true);
            }

            var codeChanged = connection.Execute(
                @"UPDATE redeem_codes
                  SET redemption_count = redemption_count + 1,
                      updated_at_utc = @Now, row_version = row_version + 1
                  WHERE code_id = @CodeId AND row_version = @RowVersion
                    AND enabled = 1
                    AND (max_redemptions IS NULL OR redemption_count < max_redemptions);",
                new { Now = now, code.CodeId, code.RowVersion }, transaction);
            if (codeChanged != 1) throw new CommerceConcurrencyException();
            InsertAttempt(connection, transaction, request, code.CodeId, "Pending", null);
            var attempt = LoadAttempt(connection, transaction, request.AttemptId);
            transaction.Commit();
            return new RedemptionReservationResult(
                RedeemReservationStatus.Reserved,
                attempt,
                true);
        }

        public RedeemAttemptSnapshot ResolveRedemptionGrant(RedeemGrantResolution resolution)
        {
            if (resolution == null) throw new ArgumentNullException(nameof(resolution));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var attempt = connection.QuerySingleOrDefault<AttemptRow>(
                AttemptSelect + " WHERE attempt.attempt_id = @AttemptId;",
                new { resolution.AttemptId }, transaction) ??
                throw new KeyNotFoundException("The redeem attempt does not exist.");
            if (attempt.State != "Pending")
            {
                transaction.Commit();
                return ToAttempt(attempt);
            }
            var state = resolution.Kind == CommerceGrantResolutionKind.Completed
                ? "Succeeded"
                : resolution.Kind == CommerceGrantResolutionKind.FailedBeforeSideEffects
                    ? "Failed"
                    : "PendingReconciliation";
            connection.Execute(
                @"UPDATE redeem_attempts
                  SET result = @State, result_code = @ErrorCode,
                      grant_operation_id = @GrantOperationId
                  WHERE attempt_id = @AttemptId AND result = 'Pending';",
                new
                {
                    State = state,
                    resolution.ErrorCode,
                    resolution.GrantOperationId,
                    resolution.AttemptId
                }, transaction);
            if (state == "Failed")
            {
                connection.Execute(
                    @"UPDATE redeem_codes
                      SET redemption_count = redemption_count - 1,
                          updated_at_utc = @Occurred, row_version = row_version + 1
                      WHERE code_id = @CodeId AND redemption_count > 0;",
                    new
                    {
                        Occurred = resolution.OccurredAtUtc.ToUnixTimeMilliseconds(),
                        attempt.CodeId
                    }, transaction);
            }
            var result = LoadAttempt(connection, transaction, resolution.AttemptId);
            transaction.Commit();
            return result;
        }

        public AchievementDefinitionSnapshot SaveAchievement(
            AchievementDefinitionDraft definition,
            DateTimeOffset occurredAtUtc)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var occurred = occurredAtUtc.ToUnixTimeMilliseconds();
            connection.Execute(
                @"INSERT INTO achievement_definitions (
                      achievement_id, name, description, statistic_key,
                      threshold_value, reward_package_id, enabled, sort_order,
                      created_at_utc, updated_at_utc, row_version)
                  VALUES (@AchievementId, @Name, @Description, @Statistic,
                      @ThresholdValue, @RewardPackageId, @Enabled, @SortOrder,
                      @Occurred, @Occurred, 0)
                  ON CONFLICT(achievement_id) DO UPDATE SET
                      name = excluded.name, description = excluded.description,
                      statistic_key = excluded.statistic_key,
                      threshold_value = excluded.threshold_value,
                      reward_package_id = excluded.reward_package_id,
                      enabled = excluded.enabled, sort_order = excluded.sort_order,
                      updated_at_utc = excluded.updated_at_utc,
                      row_version = achievement_definitions.row_version + 1;",
                new
                {
                    definition.AchievementId,
                    definition.Name,
                    definition.Description,
                    Statistic = definition.Statistic.ToString(),
                    definition.ThresholdValue,
                    definition.RewardPackageId,
                    Enabled = definition.Enabled ? 1 : 0,
                    definition.SortOrder,
                    Occurred = occurred
                }, transaction);
            var row = connection.QuerySingle<AchievementRow>(
                AchievementSelect + " WHERE achievement_id = @AchievementId;",
                new { definition.AchievementId }, transaction);
            transaction.Commit();
            return ToAchievement(row);
        }

        public AchievementProgressSnapshot GetAchievementProgress(
            string achievementId,
            string crossplatformId)
        {
            achievementId = RequireText(achievementId, nameof(achievementId));
            crossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<AchievementProgressRow>(
                AchievementProgressSelect + @" WHERE achievement_id = @AchievementId
                    AND crossplatform_id = @CrossplatformId;",
                new { AchievementId = achievementId, CrossplatformId = crossplatformId }) ??
                throw new KeyNotFoundException("The achievement progress does not exist.");
            return ToAchievementProgress(row);
        }

        public IReadOnlyList<RewardEligibilitySnapshot> ObserveAchievement(
            ObserveAchievementCommand observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var definitions = connection.Query<AchievementRow>(
                AchievementSelect + @" WHERE enabled = 1 AND statistic_key = @Statistic
                    ORDER BY sort_order, achievement_id;",
                new { Statistic = observation.Statistic.ToString() }, transaction).ToArray();
            var created = new List<RewardEligibilitySnapshot>();
            var occurred = observation.ObservedAtUtc.ToUnixTimeMilliseconds();
            foreach (var definition in definitions)
            {
                var progress = connection.QuerySingleOrDefault<AchievementProgressRow>(
                    AchievementProgressSelect + @" WHERE achievement_id = @AchievementId
                        AND crossplatform_id = @CrossplatformId;",
                    new { definition.AchievementId, observation.CrossplatformId }, transaction);
                var next = Math.Max(progress?.CurrentValue ?? 0, observation.Value);
                if (progress == null)
                {
                    connection.Execute(
                        @"INSERT INTO achievement_progress (
                              achievement_id, crossplatform_id, current_value,
                              eligibility_key, grant_operation_id, completed_at_utc,
                              updated_at_utc, row_version)
                          VALUES (@AchievementId, @CrossplatformId, @CurrentValue,
                              NULL, NULL, NULL, @Occurred, 0);",
                        new
                        {
                            definition.AchievementId,
                            observation.CrossplatformId,
                            CurrentValue = next,
                            Occurred = occurred
                        }, transaction);
                }
                else if (next > progress.CurrentValue)
                {
                    connection.Execute(
                        @"UPDATE achievement_progress
                          SET current_value = @CurrentValue, updated_at_utc = @Occurred,
                              row_version = row_version + 1
                          WHERE achievement_id = @AchievementId
                            AND crossplatform_id = @CrossplatformId
                            AND row_version = @RowVersion;",
                        new
                        {
                            CurrentValue = next,
                            Occurred = occurred,
                            definition.AchievementId,
                            observation.CrossplatformId,
                            progress.RowVersion
                        }, transaction);
                }
                if (next < definition.ThresholdValue || progress?.EligibilityKey != null)
                    continue;

                var key = "achievement:" + definition.AchievementId + ":" +
                    definition.ThresholdValue;
                var eligibility = InsertEligibility(
                    connection,
                    transaction,
                    "Achievement",
                    definition.AchievementId,
                    observation.CrossplatformId,
                    key,
                    RewardEligibilityState.Eligible,
                    observation.CorrelationId,
                    observation.ObservedAtUtc,
                    observation.ObservedAtUtc,
                    observation.ObservedAtUtc);
                connection.Execute(
                    @"UPDATE achievement_progress
                      SET eligibility_key = @EligibilityKey,
                          completed_at_utc = @Occurred,
                          updated_at_utc = @Occurred, row_version = row_version + 1
                      WHERE achievement_id = @AchievementId
                        AND crossplatform_id = @CrossplatformId
                        AND eligibility_key IS NULL;",
                    new
                    {
                        EligibilityKey = key,
                        Occurred = occurred,
                        definition.AchievementId,
                        observation.CrossplatformId
                    }, transaction);
                if (eligibility != null) created.Add(eligibility);
            }
            transaction.Commit();
            return created;
        }

        public OnlineRewardRuleSnapshot SaveOnlineRewardRule(
            OnlineRewardRuleDraft rule,
            DateTimeOffset occurredAtUtc)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var occurred = occurredAtUtc.ToUnixTimeMilliseconds();
            connection.Execute(
                @"INSERT INTO online_reward_rules (
                      rule_id, name, required_online_ms, repeat_interval_ms,
                      evidence_gap_policy, reward_package_id, enabled, sort_order,
                      created_at_utc, updated_at_utc, row_version)
                  VALUES (@RuleId, @Name, @RequiredOnlineMs, @RepeatIntervalMs,
                      @GapPolicy, @RewardPackageId, @Enabled, @SortOrder,
                      @Occurred, @Occurred, 0)
                  ON CONFLICT(rule_id) DO UPDATE SET
                      name = excluded.name,
                      required_online_ms = excluded.required_online_ms,
                      repeat_interval_ms = excluded.repeat_interval_ms,
                      evidence_gap_policy = excluded.evidence_gap_policy,
                      reward_package_id = excluded.reward_package_id,
                      enabled = excluded.enabled, sort_order = excluded.sort_order,
                      updated_at_utc = excluded.updated_at_utc,
                      row_version = online_reward_rules.row_version + 1;",
                new
                {
                    rule.RuleId,
                    rule.Name,
                    RequiredOnlineMs = checked((long)rule.RequiredOnline.TotalMilliseconds),
                    RepeatIntervalMs = rule.RepeatInterval.HasValue
                        ? checked((long?)rule.RepeatInterval.Value.TotalMilliseconds)
                        : null,
                    GapPolicy = rule.GapPolicy.ToString(),
                    rule.RewardPackageId,
                    Enabled = rule.Enabled ? 1 : 0,
                    rule.SortOrder,
                    Occurred = occurred
                }, transaction);
            var stored = connection.QuerySingle<OnlineRuleRow>(
                OnlineRuleSelect + " WHERE rule_id = @RuleId;",
                new { rule.RuleId }, transaction);
            transaction.Commit();
            return ToOnlineRule(stored);
        }

        public IReadOnlyList<RewardEligibilitySnapshot> EvaluateOnlineRewards(
            EvaluateOnlineRewardsCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var rules = connection.Query<OnlineRuleRow>(
                OnlineRuleSelect + " WHERE enabled = 1 ORDER BY sort_order, rule_id;",
                transaction: transaction).ToArray();
            var evidence = ReadOnlineEvidence(
                connection,
                transaction,
                command.CrossplatformId,
                command.EvidenceToUtc);
            var created = new List<RewardEligibilitySnapshot>();
            foreach (var rule in rules)
            {
                if (string.Equals(rule.GapPolicy, "Incomplete", StringComparison.Ordinal) &&
                    evidence.HasIncompleteEvidence)
                {
                    var marker = UpsertEvidenceMarker(
                        connection, transaction, rule, command,
                        RewardEligibilityState.Incomplete, evidence.FromUtc);
                    created.Add(marker);
                    continue;
                }
                if (evidence.HasIncompleteEvidence)
                    UpsertEvidenceMarker(
                        connection, transaction, rule, command,
                        RewardEligibilityState.Paused, evidence.FromUtc);

                var knownOnlineMs = Math.Max(
                    evidence.CumulativeOnlineMs,
                    SubtractGaps(evidence.Sessions, evidence.Gaps));
                if (knownOnlineMs < rule.RequiredOnlineMs) continue;
                var eligibleCount = rule.RepeatIntervalMs.HasValue
                    ? 1 + (knownOnlineMs - rule.RequiredOnlineMs) / rule.RepeatIntervalMs.Value
                    : 1;
                if (eligibleCount > 10000)
                    throw new InvalidOperationException("online_reward_catch_up_limit_exceeded");
                for (long ordinal = 0; ordinal < eligibleCount; ordinal++)
                {
                    var eligibility = InsertEligibility(
                        connection,
                        transaction,
                        "OnlineReward",
                        rule.RuleId,
                        command.CrossplatformId,
                        "online:" + rule.RuleId + ":" + ordinal,
                        RewardEligibilityState.Eligible,
                        command.CorrelationId,
                        evidence.FromUtc,
                        command.EvidenceToUtc,
                        command.EvidenceToUtc);
                    if (eligibility != null) created.Add(eligibility);
                }
            }
            transaction.Commit();
            return created;
        }

        public RewardEligibilitySnapshot ReserveManualOnlineReward(ManualOnlineRewardCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var rule = connection.QuerySingleOrDefault<OnlineRuleRow>(
                OnlineRuleSelect + " WHERE rule_id = @RuleId;",
                new { command.RuleId }, transaction) ??
                throw new KeyNotFoundException("The online reward rule does not exist.");
            var key = "manual:" + command.IdempotencyKey;
            var inserted = InsertEligibility(
                connection,
                transaction,
                "Manual",
                rule.RuleId,
                command.CrossplatformId,
                key,
                RewardEligibilityState.Eligible,
                command.CorrelationId,
                command.OccurredAtUtc,
                command.OccurredAtUtc,
                command.OccurredAtUtc);
            var result = inserted ?? connection.QuerySingle<EligibilityRow>(
                EligibilitySelect + @" WHERE rule_kind = 'Manual' AND rule_id = @RuleId
                    AND crossplatform_id = @CrossplatformId AND eligibility_key = @Key;",
                new { command.RuleId, command.CrossplatformId, Key = key }, transaction)
                .Let(ToEligibility);
            transaction.Commit();
            return result;
        }

        public RewardEligibilitySnapshot? TryReserveEligibilityGrant(
            string eligibilityId,
            DateTimeOffset occurredAtUtc)
        {
            eligibilityId = RequireText(eligibilityId, nameof(eligibilityId));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var changed = connection.Execute(
                @"UPDATE reward_eligibilities
                  SET state = 'GrantReserved', updated_at_utc = @Occurred,
                      row_version = row_version + 1
                  WHERE eligibility_id = @EligibilityId AND state = 'Eligible';",
                new
                {
                    EligibilityId = eligibilityId,
                    Occurred = occurredAtUtc.ToUnixTimeMilliseconds()
                }, transaction);
            var result = changed == 1
                ? LoadEligibility(connection, transaction, eligibilityId)
                : null;
            transaction.Commit();
            return result;
        }

        public RewardEligibilitySnapshot ResolveEligibilityGrant(
            EligibilityGrantResolution resolution)
        {
            if (resolution == null) throw new ArgumentNullException(nameof(resolution));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var current = LoadEligibility(connection, transaction, resolution.EligibilityId);
            if (current.State != RewardEligibilityState.GrantReserved)
            {
                transaction.Commit();
                return current;
            }
            var state = resolution.Kind == CommerceGrantResolutionKind.Completed
                ? "Granted"
                : resolution.Kind == CommerceGrantResolutionKind.FailedBeforeSideEffects
                    ? "Failed"
                    : "PendingReconciliation";
            connection.Execute(
                @"UPDATE reward_eligibilities
                  SET state = @State, grant_operation_id = @GrantOperationId,
                      updated_at_utc = @Occurred, row_version = row_version + 1
                  WHERE eligibility_id = @EligibilityId AND state = 'GrantReserved';",
                new
                {
                    State = state,
                    resolution.GrantOperationId,
                    Occurred = resolution.OccurredAtUtc.ToUnixTimeMilliseconds(),
                    resolution.EligibilityId
                }, transaction);
            if (current.RuleKind == "Achievement")
            {
                connection.Execute(
                    @"UPDATE achievement_progress
                      SET grant_operation_id = @GrantOperationId,
                          updated_at_utc = @Occurred, row_version = row_version + 1
                      WHERE achievement_id = @RuleId
                        AND crossplatform_id = @CrossplatformId
                        AND eligibility_key = @EligibilityKey;",
                    new
                    {
                        resolution.GrantOperationId,
                        Occurred = resolution.OccurredAtUtc.ToUnixTimeMilliseconds(),
                        current.RuleId,
                        current.CrossplatformId,
                        current.EligibilityKey
                    }, transaction);
            }
            var result = LoadEligibility(connection, transaction, resolution.EligibilityId);
            transaction.Commit();
            return result;
        }

        public IReadOnlyList<RewardEligibilitySnapshot> ListEligibilities(
            string ruleKind,
            string ruleId,
            string crossplatformId)
        {
            ruleKind = RequireText(ruleKind, nameof(ruleKind));
            ruleId = RequireText(ruleId, nameof(ruleId));
            crossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            using var connection = connectionFactory.Open();
            return connection.Query<EligibilityRow>(
                    EligibilitySelect + @" WHERE rule_kind = @RuleKind AND rule_id = @RuleId
                        AND crossplatform_id = @CrossplatformId
                        ORDER BY created_at_utc, eligibility_id;",
                    new { RuleKind = ruleKind, RuleId = ruleId, CrossplatformId = crossplatformId })
                .Select(ToEligibility).ToArray();
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

        private static void InsertAttempt(
            SqliteConnection connection,
            SqliteTransaction transaction,
            RedeemReservationRequest request,
            string codeId,
            string result,
            string? resultCode) => connection.Execute(
                @"INSERT INTO redeem_attempts (
                      attempt_id, code_id, crossplatform_id, normalized_code_digest,
                      result, result_code, grant_operation_id, correlation_id,
                      attempted_at_utc)
                  VALUES (@AttemptId, @CodeId, @CrossplatformId, @NormalizedCodeDigest,
                      @Result, @ResultCode, NULL, @CorrelationId, @AttemptedAtUtc);",
                new
                {
                    request.AttemptId,
                    CodeId = codeId,
                    request.CrossplatformId,
                    request.NormalizedCodeDigest,
                    Result = result,
                    ResultCode = resultCode,
                    request.CorrelationId,
                    AttemptedAtUtc = request.AttemptedAtUtc.ToUnixTimeMilliseconds()
                }, transaction);

        private static string RejectionCode(RedeemReservationStatus status) => status switch
        {
            RedeemReservationStatus.Disabled => "redeem_code_disabled",
            RedeemReservationStatus.NotYetValid => "redeem_code_not_yet_valid",
            RedeemReservationStatus.Expired => "redeem_code_expired",
            RedeemReservationStatus.GlobalLimitReached => "redeem_code_global_limit_reached",
            RedeemReservationStatus.PlayerLimitReached => "redeem_code_player_limit_reached",
            _ => "redeem_code_rejected"
        };

        private static RewardEligibilitySnapshot? InsertEligibility(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string ruleKind,
            string ruleId,
            string crossplatformId,
            string eligibilityKey,
            RewardEligibilityState state,
            string? correlationId,
            DateTimeOffset? evidenceFromUtc,
            DateTimeOffset? evidenceToUtc,
            DateTimeOffset occurredAtUtc)
        {
            var eligibilityId = "eligibility-" + Guid.NewGuid().ToString("N");
            var changed = connection.Execute(
                @"INSERT INTO reward_eligibilities (
                      eligibility_id, rule_kind, rule_id, crossplatform_id,
                      eligibility_key, state, grant_operation_id, correlation_id,
                      evidence_from_utc, evidence_to_utc, created_at_utc,
                      updated_at_utc, row_version)
                  VALUES (@EligibilityId, @RuleKind, @RuleId, @CrossplatformId,
                      @EligibilityKey, @State, NULL, @CorrelationId,
                      @EvidenceFromUtc, @EvidenceToUtc, @Occurred, @Occurred, 0)
                  ON CONFLICT(rule_kind, rule_id, crossplatform_id, eligibility_key)
                  DO NOTHING;",
                new
                {
                    EligibilityId = eligibilityId,
                    RuleKind = ruleKind,
                    RuleId = ruleId,
                    CrossplatformId = crossplatformId,
                    EligibilityKey = eligibilityKey,
                    State = state.ToString(),
                    CorrelationId = correlationId,
                    EvidenceFromUtc = evidenceFromUtc?.ToUnixTimeMilliseconds(),
                    EvidenceToUtc = evidenceToUtc?.ToUnixTimeMilliseconds(),
                    Occurred = occurredAtUtc.ToUnixTimeMilliseconds()
                }, transaction);
            return changed == 1 ? LoadEligibility(connection, transaction, eligibilityId) : null;
        }

        private static RewardEligibilitySnapshot UpsertEvidenceMarker(
            SqliteConnection connection,
            SqliteTransaction transaction,
            OnlineRuleRow rule,
            EvaluateOnlineRewardsCommand command,
            RewardEligibilityState state,
            DateTimeOffset? evidenceFromUtc)
        {
            var key = "online:" + rule.RuleId + ":evidence";
            var inserted = InsertEligibility(
                connection,
                transaction,
                "OnlineReward",
                rule.RuleId,
                command.CrossplatformId,
                key,
                state,
                command.CorrelationId,
                evidenceFromUtc,
                command.EvidenceToUtc,
                command.EvidenceToUtc);
            if (inserted != null) return inserted;
            connection.Execute(
                @"UPDATE reward_eligibilities
                  SET state = @State, correlation_id = @CorrelationId,
                      evidence_from_utc = @EvidenceFromUtc,
                      evidence_to_utc = @EvidenceToUtc,
                      updated_at_utc = @Occurred, row_version = row_version + 1
                  WHERE rule_kind = 'OnlineReward' AND rule_id = @RuleId
                    AND crossplatform_id = @CrossplatformId
                    AND eligibility_key = @EligibilityKey
                    AND state IN ('Paused', 'Incomplete');",
                new
                {
                    State = state.ToString(),
                    command.CorrelationId,
                    EvidenceFromUtc = evidenceFromUtc?.ToUnixTimeMilliseconds(),
                    EvidenceToUtc = command.EvidenceToUtc.ToUnixTimeMilliseconds(),
                    Occurred = command.EvidenceToUtc.ToUnixTimeMilliseconds(),
                    rule.RuleId,
                    command.CrossplatformId,
                    EligibilityKey = key
                }, transaction);
            return connection.QuerySingle<EligibilityRow>(
                    EligibilitySelect + @" WHERE rule_kind = 'OnlineReward' AND rule_id = @RuleId
                        AND crossplatform_id = @CrossplatformId AND eligibility_key = @EligibilityKey;",
                    new { rule.RuleId, command.CrossplatformId, EligibilityKey = key }, transaction)
                .Let(ToEligibility);
        }

        private static OnlineEvidence ReadOnlineEvidence(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string crossplatformId,
            DateTimeOffset evidenceToUtc)
        {
            var boundary = evidenceToUtc.ToUnixTimeMilliseconds();
            var latestScalarAt = connection.ExecuteScalar<long?>(
                @"SELECT MAX(observed_utc) FROM player_history_snapshots
                  WHERE crossplatform_id = @CrossplatformId AND observed_utc <= @Boundary;",
                new { CrossplatformId = crossplatformId, Boundary = boundary }, transaction);
            var cumulativeMinutes = connection.ExecuteScalar<double?>(
                @"SELECT MAX(total_time_played_minutes) FROM player_history_snapshots
                  WHERE crossplatform_id = @CrossplatformId AND observed_utc <= @Boundary;",
                new { CrossplatformId = crossplatformId, Boundary = boundary }, transaction) ?? 0d;
            var sessionRows = connection.Query<SessionRow>(
                @"SELECT id AS Id, started_at_utc AS StartedAtUtc,
                         ended_at_utc AS EndedAtUtc, completeness AS Completeness
                  FROM player_sessions
                  WHERE crossplatform_id = @CrossplatformId AND started_at_utc <= @Boundary
                  ORDER BY started_at_utc, id;",
                new { CrossplatformId = crossplatformId, Boundary = boundary }, transaction).ToArray();
            var sessions = new List<Interval>();
            var partial = false;
            foreach (var session in sessionRows)
            {
                if (!string.Equals(session.Completeness, "Available", StringComparison.Ordinal))
                {
                    partial = true;
                    continue;
                }
                var end = session.EndedAtUtc.HasValue
                    ? Math.Min(session.EndedAtUtc.Value, boundary)
                    : latestScalarAt.HasValue && latestScalarAt.Value >= session.StartedAtUtc
                        ? Math.Min(latestScalarAt.Value, boundary)
                        : session.StartedAtUtc;
                if (end > session.StartedAtUtc)
                    sessions.Add(new Interval(session.StartedAtUtc, end));
            }
            sessions = Merge(sessions);
            var from = sessions.Count > 0
                ? sessions.Min(interval => interval.Start)
                : latestScalarAt;
            var gaps = from.HasValue
                ? connection.Query<GapRow>(
                    @"SELECT started_utc AS StartedAtUtc, completed_utc AS EndedAtUtc
                      FROM player_history_gaps
                      WHERE crossplatform_id = @CrossplatformId
                        AND completed_utc >= @FromUtc AND started_utc <= @Boundary
                      ORDER BY started_utc, gap_id;",
                    new { CrossplatformId = crossplatformId, FromUtc = from.Value, Boundary = boundary },
                    transaction).Select(row => new Interval(
                        Math.Max(row.StartedAtUtc, from.Value),
                        Math.Min(row.EndedAtUtc, boundary))).Where(value => value.End >= value.Start)
                    .ToList()
                : new List<Interval>();
            var cumulativeMs = cumulativeMinutes <= 0
                ? 0L
                : checked((long)Math.Floor(cumulativeMinutes * 60000d));
            return new OnlineEvidence(
                sessions,
                Merge(gaps),
                cumulativeMs,
                partial || gaps.Count > 0,
                from.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(from.Value) : (DateTimeOffset?)null);
        }

        private static long SubtractGaps(
            IReadOnlyList<Interval> sessions,
            IReadOnlyList<Interval> gaps)
        {
            long total = 0;
            foreach (var session in sessions)
            {
                var known = session.End - session.Start;
                foreach (var gap in gaps)
                {
                    var overlapStart = Math.Max(session.Start, gap.Start);
                    var overlapEnd = Math.Min(session.End, gap.End);
                    if (overlapEnd > overlapStart) known -= overlapEnd - overlapStart;
                }
                total = checked(total + Math.Max(0, known));
            }
            return total;
        }

        private static List<Interval> Merge(IEnumerable<Interval> values)
        {
            var ordered = values.OrderBy(value => value.Start).ThenBy(value => value.End).ToArray();
            var merged = new List<Interval>();
            foreach (var value in ordered)
            {
                if (merged.Count == 0 || value.Start > merged[merged.Count - 1].End)
                {
                    merged.Add(value);
                    continue;
                }
                var current = merged[merged.Count - 1];
                merged[merged.Count - 1] = new Interval(current.Start, Math.Max(current.End, value.End));
            }
            return merged;
        }

        private static ShopProductSnapshot LoadProduct(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string productId) => connection.QuerySingleOrDefault<ProductRow>(
                ProductSelect + " WHERE product_id = @ProductId;",
                new { ProductId = productId }, transaction).Let(row => row == null
                    ? throw new KeyNotFoundException("The shop product does not exist.")
                    : ToProduct(row));

        private static DailyRewardClaimSnapshot LoadDailyClaim(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string claimId) => connection.QuerySingleOrDefault<DailyClaimRow>(
                DailyClaimSelect + " WHERE claim_id = @ClaimId;",
                new { ClaimId = claimId }, transaction).Let(row => row == null
                    ? throw new KeyNotFoundException("The daily reward claim does not exist.")
                    : ToDailyClaim(row));

        private static ShopPurchaseSnapshot LoadPurchase(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string purchaseId) => connection.QuerySingleOrDefault<PurchaseRow>(
                PurchaseSelect + " WHERE p.purchase_id = @PurchaseId;",
                new { PurchaseId = purchaseId }, transaction).Let(row => row == null
                    ? throw new KeyNotFoundException("The shop purchase does not exist.")
                    : ToPurchase(row));

        private static RedeemCodeSnapshot LoadCode(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string codeId) => connection.QuerySingleOrDefault<CodeRow>(
                CodeSelect + " WHERE code_id = @CodeId;",
                new { CodeId = codeId }, transaction).Let(row => row == null
                    ? throw new KeyNotFoundException("The redeem code does not exist.")
                    : ToCode(row));

        private static RedeemAttemptSnapshot LoadAttempt(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string attemptId) => connection.QuerySingleOrDefault<AttemptRow>(
                AttemptSelect + " WHERE attempt.attempt_id = @AttemptId;",
                new { AttemptId = attemptId }, transaction).Let(row => row == null
                    ? throw new KeyNotFoundException("The redeem attempt does not exist.")
                    : ToAttempt(row));

        private static RewardEligibilitySnapshot LoadEligibility(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string eligibilityId) => connection.QuerySingleOrDefault<EligibilityRow>(
                EligibilitySelect + " WHERE eligibility_id = @EligibilityId;",
                new { EligibilityId = eligibilityId }, transaction).Let(row => row == null
                    ? throw new KeyNotFoundException("The reward eligibility does not exist.")
                    : ToEligibility(row));

        private static ShopProductSnapshot ToProduct(ProductRow row) =>
            new ShopProductSnapshot(
                new ShopProductDraft(
                    row.ProductId, row.Name, row.Description, row.Enabled != 0,
                    row.PriceAmount, row.StockRemaining, row.PerPlayerLimit,
                    row.RewardPackageId, row.SortOrder),
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.RowVersion);

        private static DailyRewardClaimSnapshot ToDailyClaim(DailyClaimRow row) =>
            new DailyRewardClaimSnapshot(
                new DailyRewardClaimDraft(
                    row.ClaimId,
                    row.RuleId,
                    row.RewardPackageId,
                    row.CrossplatformId,
                    row.PeriodKey,
                    DateTimeOffset.FromUnixTimeMilliseconds(row.PeriodStartUtc),
                    DateTimeOffset.FromUnixTimeMilliseconds(row.PeriodEndUtc),
                    row.IdempotencyKey,
                    row.ExpectedEntityId,
                    row.ExpectedWorldId,
                    row.CorrelationId,
                    DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc)),
                Parse<DailyRewardClaimState>(row.State),
                row.GrantOperationId,
                row.ErrorCode,
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                FromUnix(row.CompletedAtUtc),
                row.RowVersion);

        private static DailyRewardPolicySnapshot ToDailyPolicy(DailyPolicyRow row) =>
            new DailyRewardPolicySnapshot(
                new DailyRewardPolicyDraft(
                    row.RuleId,
                    row.RewardPackageId,
                    row.Enabled != 0,
                    null),
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.RowVersion);

        private static void EnsureDailyClaimMatches(
            DailyClaimRow existing,
            DailyRewardClaimDraft requested)
        {
            if (!string.Equals(existing.RuleId, requested.RuleId, StringComparison.Ordinal) ||
                !string.Equals(
                    existing.CrossplatformId,
                    requested.CrossplatformId,
                    StringComparison.Ordinal) ||
                !string.Equals(existing.PeriodKey, requested.PeriodKey, StringComparison.Ordinal) ||
                existing.PeriodStartUtc != requested.PeriodStartUtc.ToUnixTimeMilliseconds() ||
                existing.PeriodEndUtc != requested.PeriodEndUtc.ToUnixTimeMilliseconds() ||
                !string.Equals(
                    existing.IdempotencyKey,
                    requested.IdempotencyKey,
                    StringComparison.Ordinal))
                throw new RewardConcurrencyException();
        }

        private static ShopPurchaseSnapshot ToPurchase(PurchaseRow row) =>
            new ShopPurchaseSnapshot(
                row.PurchaseId, row.ProductId, row.RewardPackageId,
                row.CrossplatformId, row.Quantity, row.UnitPrice, row.TotalAmount,
                Parse<PurchaseState>(row.State), row.IdempotencyKey, row.ReservationId,
                row.CapturedTransactionId, row.GrantOperationId, row.CorrelationId,
                row.ErrorCode,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.CompletedAtUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(row.CompletedAtUtc.Value)
                    : (DateTimeOffset?)null,
                row.RowVersion);

        private static RedeemCodeSnapshot ToCode(CodeRow row) =>
            new RedeemCodeSnapshot(
                row.CodeId,
                "****-****-****-" + row.LastFour,
                row.NormalizationVersion,
                row.RewardPackageId,
                row.Enabled != 0,
                FromUnix(row.ValidFromUtc),
                FromUnix(row.ExpiresAtUtc),
                row.MaxRedemptions,
                row.PerPlayerLimit,
                row.RedemptionCount,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.RowVersion);

        private static RedeemAttemptSnapshot ToAttempt(AttemptRow row) =>
            new RedeemAttemptSnapshot(
                row.AttemptId, row.CodeId, row.RewardPackageId,
                row.CrossplatformId, Parse<RedeemAttemptState>(row.State),
                row.ResultCode, row.GrantOperationId, row.CorrelationId,
                DateTimeOffset.FromUnixTimeMilliseconds(row.AttemptedAtUtc));

        private static AchievementDefinitionSnapshot ToAchievement(AchievementRow row) =>
            new AchievementDefinitionSnapshot(
                new AchievementDefinitionDraft(
                    row.AchievementId, row.Name, row.Description,
                    Parse<AchievementStatistic>(row.Statistic), row.ThresholdValue,
                    row.RewardPackageId, row.Enabled != 0, row.SortOrder),
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.RowVersion);

        private static AchievementProgressSnapshot ToAchievementProgress(
            AchievementProgressRow row) => new AchievementProgressSnapshot(
                row.AchievementId, row.CrossplatformId, row.CurrentValue,
                row.EligibilityKey, row.GrantOperationId, FromUnix(row.CompletedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc), row.RowVersion);

        private static OnlineRewardRuleSnapshot ToOnlineRule(OnlineRuleRow row) =>
            new OnlineRewardRuleSnapshot(
                new OnlineRewardRuleDraft(
                    row.RuleId, row.Name, TimeSpan.FromMilliseconds(row.RequiredOnlineMs),
                    row.RepeatIntervalMs.HasValue
                        ? TimeSpan.FromMilliseconds(row.RepeatIntervalMs.Value)
                        : (TimeSpan?)null,
                    Parse<EvidenceGapPolicy>(row.GapPolicy), row.RewardPackageId,
                    row.Enabled != 0, row.SortOrder),
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.RowVersion);

        private static RewardEligibilitySnapshot ToEligibility(EligibilityRow row) =>
            new RewardEligibilitySnapshot(
                row.EligibilityId, row.RuleKind, row.RuleId, row.RewardPackageId,
                row.CrossplatformId, row.EligibilityKey,
                Parse<RewardEligibilityState>(row.State), row.GrantOperationId,
                row.CorrelationId, FromUnix(row.EvidenceFromUtc),
                FromUnix(row.EvidenceToUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.RowVersion);

        private static DateTimeOffset? FromUnix(long? value) => value.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value)
            : (DateTimeOffset?)null;

        private static T Parse<T>(string value) where T : struct, Enum =>
            (T)Enum.Parse(typeof(T), value, false);

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        private static string? NullIfWhiteSpace(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        private const string AchievementSelect = @"SELECT
            achievement_id AS AchievementId, name AS Name, description AS Description,
            statistic_key AS Statistic, threshold_value AS ThresholdValue,
            reward_package_id AS RewardPackageId, enabled AS Enabled,
            sort_order AS SortOrder, created_at_utc AS CreatedAtUtc,
            updated_at_utc AS UpdatedAtUtc, row_version AS RowVersion
            FROM achievement_definitions";

        private const string AchievementProgressSelect = @"SELECT
            achievement_id AS AchievementId, crossplatform_id AS CrossplatformId,
            current_value AS CurrentValue, eligibility_key AS EligibilityKey,
            grant_operation_id AS GrantOperationId,
            completed_at_utc AS CompletedAtUtc, updated_at_utc AS UpdatedAtUtc,
            row_version AS RowVersion FROM achievement_progress";

        private const string OnlineRuleSelect = @"SELECT
            rule_id AS RuleId, name AS Name, required_online_ms AS RequiredOnlineMs,
            repeat_interval_ms AS RepeatIntervalMs,
            evidence_gap_policy AS GapPolicy, reward_package_id AS RewardPackageId,
            enabled AS Enabled, sort_order AS SortOrder,
            created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc,
            row_version AS RowVersion FROM online_reward_rules";

        private sealed class ProductRow
        {
            public string ProductId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public long PriceAmount { get; set; }
            public long? StockRemaining { get; set; }
            public int? PerPlayerLimit { get; set; }
            public string RewardPackageId { get; set; } = string.Empty;
            public int SortOrder { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class DailyClaimRow
        {
            public string ClaimId { get; set; } = string.Empty;
            public string RuleId { get; set; } = string.Empty;
            public string RewardPackageId { get; set; } = string.Empty;
            public string CrossplatformId { get; set; } = string.Empty;
            public string PeriodKey { get; set; } = string.Empty;
            public long PeriodStartUtc { get; set; }
            public long PeriodEndUtc { get; set; }
            public string State { get; set; } = string.Empty;
            public string IdempotencyKey { get; set; } = string.Empty;
            public int ExpectedEntityId { get; set; }
            public string ExpectedWorldId { get; set; } = string.Empty;
            public string? GrantOperationId { get; set; }
            public string? CorrelationId { get; set; }
            public string? ErrorCode { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long? CompletedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class DailyPolicyRow
        {
            public string RuleId { get; set; } = string.Empty;
            public string RewardPackageId { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class PurchaseRow
        {
            public string PurchaseId { get; set; } = string.Empty;
            public string ProductId { get; set; } = string.Empty;
            public string RewardPackageId { get; set; } = string.Empty;
            public string CrossplatformId { get; set; } = string.Empty;
            public int Quantity { get; set; }
            public long UnitPrice { get; set; }
            public long TotalAmount { get; set; }
            public string State { get; set; } = string.Empty;
            public string IdempotencyKey { get; set; } = string.Empty;
            public string? ReservationId { get; set; }
            public string? CapturedTransactionId { get; set; }
            public string? GrantOperationId { get; set; }
            public string? CorrelationId { get; set; }
            public string? ErrorCode { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long? CompletedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class AccountRow
        {
            public string AccountId { get; set; } = string.Empty;
            public string? CrossplatformId { get; set; }
            public int Enabled { get; set; }
            public int IsFrozen { get; set; }
            public long PostedBalance { get; set; }
            public long ReservedDebit { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class SystemAccountRow
        {
            public long PostedBalance { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class ReservationRow
        {
            public string ReservationId { get; set; } = string.Empty;
            public string AccountId { get; set; } = string.Empty;
            public long Amount { get; set; }
            public string State { get; set; } = string.Empty;
            public string BusinessKind { get; set; } = string.Empty;
            public string BusinessId { get; set; } = string.Empty;
            public long RowVersion { get; set; }
        }

        private sealed class CodeRow
        {
            public string CodeId { get; set; } = string.Empty;
            public string LastFour { get; set; } = string.Empty;
            public int NormalizationVersion { get; set; }
            public string RewardPackageId { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public long? ValidFromUtc { get; set; }
            public long? ExpiresAtUtc { get; set; }
            public int? MaxRedemptions { get; set; }
            public int? PerPlayerLimit { get; set; }
            public int RedemptionCount { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class CodeSecretRow
        {
            public string CodeId { get; set; } = string.Empty;
            public string Digest { get; set; } = string.Empty;
            public string RewardPackageId { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public long? ValidFromUtc { get; set; }
            public long? ExpiresAtUtc { get; set; }
            public int? MaxRedemptions { get; set; }
            public int? PerPlayerLimit { get; set; }
            public int RedemptionCount { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class AttemptRow
        {
            public string AttemptId { get; set; } = string.Empty;
            public string CodeId { get; set; } = string.Empty;
            public string RewardPackageId { get; set; } = string.Empty;
            public string CrossplatformId { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string? ResultCode { get; set; }
            public string? GrantOperationId { get; set; }
            public string? CorrelationId { get; set; }
            public long AttemptedAtUtc { get; set; }
        }

        private sealed class AchievementRow
        {
            public string AchievementId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Statistic { get; set; } = string.Empty;
            public long ThresholdValue { get; set; }
            public string RewardPackageId { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public int SortOrder { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class AchievementProgressRow
        {
            public string AchievementId { get; set; } = string.Empty;
            public string CrossplatformId { get; set; } = string.Empty;
            public long CurrentValue { get; set; }
            public string? EligibilityKey { get; set; }
            public string? GrantOperationId { get; set; }
            public long? CompletedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class OnlineRuleRow
        {
            public string RuleId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public long RequiredOnlineMs { get; set; }
            public long? RepeatIntervalMs { get; set; }
            public string GapPolicy { get; set; } = string.Empty;
            public string RewardPackageId { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public int SortOrder { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class EligibilityRow
        {
            public string EligibilityId { get; set; } = string.Empty;
            public string RuleKind { get; set; } = string.Empty;
            public string RuleId { get; set; } = string.Empty;
            public string RewardPackageId { get; set; } = string.Empty;
            public string CrossplatformId { get; set; } = string.Empty;
            public string EligibilityKey { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string? GrantOperationId { get; set; }
            public string? CorrelationId { get; set; }
            public long? EvidenceFromUtc { get; set; }
            public long? EvidenceToUtc { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class SessionRow
        {
            public long Id { get; set; }
            public long StartedAtUtc { get; set; }
            public long? EndedAtUtc { get; set; }
            public string Completeness { get; set; } = string.Empty;
        }

        private sealed class GapRow
        {
            public long StartedAtUtc { get; set; }
            public long EndedAtUtc { get; set; }
        }

        private readonly struct Interval
        {
            public Interval(long start, long end)
            {
                Start = start;
                End = end;
            }

            public long Start { get; }
            public long End { get; }
        }

        private sealed class OnlineEvidence
        {
            public OnlineEvidence(
                IReadOnlyList<Interval> sessions,
                IReadOnlyList<Interval> gaps,
                long cumulativeOnlineMs,
                bool hasIncompleteEvidence,
                DateTimeOffset? fromUtc)
            {
                Sessions = sessions;
                Gaps = gaps;
                CumulativeOnlineMs = cumulativeOnlineMs;
                HasIncompleteEvidence = hasIncompleteEvidence;
                FromUtc = fromUtc;
            }

            public IReadOnlyList<Interval> Sessions { get; }
            public IReadOnlyList<Interval> Gaps { get; }
            public long CumulativeOnlineMs { get; }
            public bool HasIncompleteEvidence { get; }
            public DateTimeOffset? FromUtc { get; }
        }
    }

    internal static class SqliteCommerceFunctionalExtensions
    {
        internal static TResult Let<T, TResult>(this T value, Func<T, TResult> selector) =>
            selector(value);
    }
}
