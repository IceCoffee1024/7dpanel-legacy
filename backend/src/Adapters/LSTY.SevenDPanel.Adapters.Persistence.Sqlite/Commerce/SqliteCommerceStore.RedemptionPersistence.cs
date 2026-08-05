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
    }
}
