using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Rewards;
using Microsoft.Data.Sqlite;
using static LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce.SqliteCommerceStoreRows;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce
{
    public sealed partial class SqliteCommerceStore :
        ICommerceStore,
        IShopCatalogQueryStore,
        IDailyRewardClaimStore,
        IDailyRewardPolicyStore
    {
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
