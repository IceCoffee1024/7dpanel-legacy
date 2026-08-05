using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Rewards;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce
{
    internal static class SqliteCommerceStoreRows
    {
        internal const string ProductSelect = @"SELECT
            product_id AS ProductId, name AS Name, description AS Description,
            enabled AS Enabled, price_amount AS PriceAmount,
            stock_remaining AS StockRemaining, per_player_limit AS PerPlayerLimit,
            reward_package_id AS RewardPackageId, sort_order AS SortOrder,
            created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc,
            row_version AS RowVersion
            FROM shop_products";

        internal const string PurchaseSelect = @"SELECT
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

        internal const string CodeSelect = @"SELECT
            code_id AS CodeId, masked_prefix AS LastFour,
            normalization_version AS NormalizationVersion,
            reward_package_id AS RewardPackageId, enabled AS Enabled,
            valid_from_utc AS ValidFromUtc, expires_at_utc AS ExpiresAtUtc,
            max_redemptions AS MaxRedemptions, per_player_limit AS PerPlayerLimit,
            redemption_count AS RedemptionCount, created_at_utc AS CreatedAtUtc,
            updated_at_utc AS UpdatedAtUtc, row_version AS RowVersion
            FROM redeem_codes";

        internal const string AttemptSelect = @"SELECT
            attempt.attempt_id AS AttemptId, attempt.code_id AS CodeId,
            code.reward_package_id AS RewardPackageId,
            attempt.crossplatform_id AS CrossplatformId,
            attempt.result AS State, attempt.result_code AS ResultCode,
            attempt.grant_operation_id AS GrantOperationId,
            attempt.correlation_id AS CorrelationId,
            attempt.attempted_at_utc AS AttemptedAtUtc
            FROM redeem_attempts attempt
            JOIN redeem_codes code ON code.code_id = attempt.code_id";

        internal const string EligibilitySelect = @"SELECT
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

        internal const string DailyClaimSelect = @"SELECT
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

        internal const string DailyPolicySelect = @"SELECT
            rule_id AS RuleId, reward_package_id AS RewardPackageId,
            enabled AS Enabled, created_at_utc AS CreatedAtUtc,
            updated_at_utc AS UpdatedAtUtc, row_version AS RowVersion
            FROM daily_reward_policies";

        internal const string AchievementSelect = @"SELECT
            achievement_id AS AchievementId, name AS Name, description AS Description,
            statistic_key AS Statistic, threshold_value AS ThresholdValue,
            reward_package_id AS RewardPackageId, enabled AS Enabled,
            sort_order AS SortOrder, created_at_utc AS CreatedAtUtc,
            updated_at_utc AS UpdatedAtUtc, row_version AS RowVersion
            FROM achievement_definitions";

        internal const string AchievementProgressSelect = @"SELECT
            achievement_id AS AchievementId, crossplatform_id AS CrossplatformId,
            current_value AS CurrentValue, eligibility_key AS EligibilityKey,
            grant_operation_id AS GrantOperationId,
            completed_at_utc AS CompletedAtUtc, updated_at_utc AS UpdatedAtUtc,
            row_version AS RowVersion FROM achievement_progress";

        internal const string OnlineRuleSelect = @"SELECT
            rule_id AS RuleId, name AS Name, required_online_ms AS RequiredOnlineMs,
            repeat_interval_ms AS RepeatIntervalMs,
            evidence_gap_policy AS GapPolicy, reward_package_id AS RewardPackageId,
            enabled AS Enabled, sort_order AS SortOrder,
            created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc,
            row_version AS RowVersion FROM online_reward_rules";

        internal static ShopProductSnapshot LoadProduct(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string productId) => connection.QuerySingleOrDefault<ProductRow>(
                ProductSelect + " WHERE product_id = @ProductId;",
                new { ProductId = productId }, transaction).Let(row => row == null
                    ? throw new KeyNotFoundException("The shop product does not exist.")
                    : ToProduct(row));

        internal static DailyRewardClaimSnapshot LoadDailyClaim(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string claimId) => connection.QuerySingleOrDefault<DailyClaimRow>(
                DailyClaimSelect + " WHERE claim_id = @ClaimId;",
                new { ClaimId = claimId }, transaction).Let(row => row == null
                    ? throw new KeyNotFoundException("The daily reward claim does not exist.")
                    : ToDailyClaim(row));

        internal static ShopPurchaseSnapshot LoadPurchase(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string purchaseId) => connection.QuerySingleOrDefault<PurchaseRow>(
                PurchaseSelect + " WHERE p.purchase_id = @PurchaseId;",
                new { PurchaseId = purchaseId }, transaction).Let(row => row == null
                    ? throw new KeyNotFoundException("The shop purchase does not exist.")
                    : ToPurchase(row));

        internal static RedeemCodeSnapshot LoadCode(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string codeId) => connection.QuerySingleOrDefault<CodeRow>(
                CodeSelect + " WHERE code_id = @CodeId;",
                new { CodeId = codeId }, transaction).Let(row => row == null
                    ? throw new KeyNotFoundException("The redeem code does not exist.")
                    : ToCode(row));

        internal static RedeemAttemptSnapshot LoadAttempt(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string attemptId) => connection.QuerySingleOrDefault<AttemptRow>(
                AttemptSelect + " WHERE attempt.attempt_id = @AttemptId;",
                new { AttemptId = attemptId }, transaction).Let(row => row == null
                    ? throw new KeyNotFoundException("The redeem attempt does not exist.")
                    : ToAttempt(row));

        internal static RewardEligibilitySnapshot LoadEligibility(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string eligibilityId) => connection.QuerySingleOrDefault<EligibilityRow>(
                EligibilitySelect + " WHERE eligibility_id = @EligibilityId;",
                new { EligibilityId = eligibilityId }, transaction).Let(row => row == null
                    ? throw new KeyNotFoundException("The reward eligibility does not exist.")
                    : ToEligibility(row));

        internal static ShopProductSnapshot ToProduct(ProductRow row) =>
            new ShopProductSnapshot(
                new ShopProductDraft(
                    row.ProductId, row.Name, row.Description, row.Enabled != 0,
                    row.PriceAmount, row.StockRemaining, row.PerPlayerLimit,
                    row.RewardPackageId, row.SortOrder),
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.RowVersion);

        internal static DailyRewardClaimSnapshot ToDailyClaim(DailyClaimRow row) =>
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

        internal static DailyRewardPolicySnapshot ToDailyPolicy(DailyPolicyRow row) =>
            new DailyRewardPolicySnapshot(
                new DailyRewardPolicyDraft(
                    row.RuleId,
                    row.RewardPackageId,
                    row.Enabled != 0,
                    null),
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.RowVersion);

        internal static ShopPurchaseSnapshot ToPurchase(PurchaseRow row) =>
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

        internal static RedeemCodeSnapshot ToCode(CodeRow row) =>
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

        internal static RedeemAttemptSnapshot ToAttempt(AttemptRow row) =>
            new RedeemAttemptSnapshot(
                row.AttemptId, row.CodeId, row.RewardPackageId,
                row.CrossplatformId, Parse<RedeemAttemptState>(row.State),
                row.ResultCode, row.GrantOperationId, row.CorrelationId,
                DateTimeOffset.FromUnixTimeMilliseconds(row.AttemptedAtUtc));

        internal static AchievementDefinitionSnapshot ToAchievement(AchievementRow row) =>
            new AchievementDefinitionSnapshot(
                new AchievementDefinitionDraft(
                    row.AchievementId, row.Name, row.Description,
                    Parse<AchievementStatistic>(row.Statistic), row.ThresholdValue,
                    row.RewardPackageId, row.Enabled != 0, row.SortOrder),
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.RowVersion);

        internal static AchievementProgressSnapshot ToAchievementProgress(
            AchievementProgressRow row) => new AchievementProgressSnapshot(
                row.AchievementId, row.CrossplatformId, row.CurrentValue,
                row.EligibilityKey, row.GrantOperationId, FromUnix(row.CompletedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc), row.RowVersion);

        internal static OnlineRewardRuleSnapshot ToOnlineRule(OnlineRuleRow row) =>
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

        internal static RewardEligibilitySnapshot ToEligibility(EligibilityRow row) =>
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

        internal sealed class ProductRow
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

        internal sealed class DailyClaimRow
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

        internal sealed class DailyPolicyRow
        {
            public string RuleId { get; set; } = string.Empty;
            public string RewardPackageId { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        internal sealed class PurchaseRow
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

        internal sealed class AccountRow
        {
            public string AccountId { get; set; } = string.Empty;
            public string? CrossplatformId { get; set; }
            public int Enabled { get; set; }
            public int IsFrozen { get; set; }
            public long PostedBalance { get; set; }
            public long ReservedDebit { get; set; }
            public long RowVersion { get; set; }
        }

        internal sealed class SystemAccountRow
        {
            public long PostedBalance { get; set; }
            public long RowVersion { get; set; }
        }

        internal sealed class ReservationRow
        {
            public string ReservationId { get; set; } = string.Empty;
            public string AccountId { get; set; } = string.Empty;
            public long Amount { get; set; }
            public string State { get; set; } = string.Empty;
            public string BusinessKind { get; set; } = string.Empty;
            public string BusinessId { get; set; } = string.Empty;
            public long RowVersion { get; set; }
        }

        internal sealed class CodeRow
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

        internal sealed class CodeSecretRow
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

        internal sealed class AttemptRow
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

        internal sealed class AchievementRow
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

        internal sealed class AchievementProgressRow
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

        internal sealed class OnlineRuleRow
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

        internal sealed class EligibilityRow
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

        internal sealed class SessionRow
        {
            public long Id { get; set; }
            public long StartedAtUtc { get; set; }
            public long? EndedAtUtc { get; set; }
            public string Completeness { get; set; } = string.Empty;
        }

        internal sealed class GapRow
        {
            public long StartedAtUtc { get; set; }
            public long EndedAtUtc { get; set; }
        }
    }
}
