using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Domain.Community;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Community
{
    public sealed class SqliteVoteStore : IVoteStore, IExpiringVoteRoundReader
    {
        private const string ConfigurationSelect = @"SELECT
            configuration_id AS ConfigurationId, vote_kind AS VoteKind,
            enabled AS Enabled, duration_ms AS DurationMs,
            threshold_percent AS ThresholdPercent,
            minimum_participants AS MinimumParticipants,
            initiator_minimum_online_ms AS InitiatorMinimumOnlineMs,
            participant_minimum_online_ms AS ParticipantMinimumOnlineMs,
            initiator_cooldown_ms AS InitiatorCooldownMs,
            target_cooldown_ms AS TargetCooldownMs,
            global_cooldown_ms AS GlobalCooldownMs,
            mutual_exclusion_scope AS MutualExclusionScope,
            allow_vote_change AS AllowVoteChange,
            updated_at_utc AS UpdatedAtUtc, row_version AS RowVersion
            FROM vote_configurations";

        private const string RoundSelect = @"SELECT
            round_id AS RoundId, configuration_id AS ConfigurationId,
            vote_kind AS VoteKind, state AS State,
            initiator_crossplatform_id AS InitiatorCrossplatformId,
            target_crossplatform_id AS TargetCrossplatformId,
            scope_key AS ScopeKey, eligible_count AS EligibleCount,
            threshold_percent AS ThresholdPercent,
            minimum_participants AS MinimumParticipants,
            allow_vote_change AS AllowVoteChange,
            idempotency_key AS IdempotencyKey,
            action_job_id AS ActionJobId,
            action_operation_id AS ActionOperationId,
            correlation_id AS CorrelationId,
            opened_at_utc AS OpenedAtUtc, expires_at_utc AS ExpiresAtUtc,
            settled_at_utc AS SettledAtUtc,
            action_completed_at_utc AS ActionCompletedAtUtc,
            row_version AS RowVersion
            FROM vote_rounds";

        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteVoteStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public VoteConfiguration? GetConfiguration(VoteKind kind)
        {
            RequireDefined(kind, nameof(kind));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<ConfigurationRow>(
                ConfigurationSelect + " WHERE vote_kind = @Kind;",
                new { Kind = kind.ToString() });
            return row == null ? null : ToConfiguration(row);
        }

        public VoteConfiguration SaveConfiguration(VoteConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var parameters = new
            {
                configuration.ConfigurationId,
                VoteKind = configuration.Kind.ToString(),
                Enabled = configuration.Enabled ? 1 : 0,
                DurationMs = Milliseconds(configuration.Duration),
                configuration.ThresholdPercent,
                configuration.MinimumParticipants,
                InitiatorMinimumOnlineMs = Milliseconds(configuration.InitiatorMinimumOnline),
                ParticipantMinimumOnlineMs = Milliseconds(configuration.ParticipantMinimumOnline),
                InitiatorCooldownMs = Milliseconds(configuration.InitiatorCooldown),
                TargetCooldownMs = Milliseconds(configuration.TargetCooldown),
                GlobalCooldownMs = Milliseconds(configuration.GlobalCooldown),
                configuration.MutualExclusionScope,
                AllowVoteChange = configuration.AllowVoteChange ? 1 : 0,
                UpdatedAtUtc = configuration.UpdatedAtUtc.ToUnixTimeMilliseconds(),
                ExpectedRowVersion = configuration.RowVersion
            };
            var exists = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM vote_configurations WHERE configuration_id = @ConfigurationId;",
                parameters,
                transaction) != 0;
            if (!exists)
            {
                if (configuration.RowVersion != 0) throw new CommunityConflictException();
                connection.Execute(
                    @"INSERT INTO vote_configurations (
                          configuration_id, vote_kind, enabled, duration_ms,
                          threshold_percent, minimum_participants,
                          initiator_minimum_online_ms, participant_minimum_online_ms,
                          initiator_cooldown_ms, target_cooldown_ms, global_cooldown_ms,
                          mutual_exclusion_scope, allow_vote_change, updated_at_utc, row_version)
                      VALUES (
                          @ConfigurationId, @VoteKind, @Enabled, @DurationMs,
                          @ThresholdPercent, @MinimumParticipants,
                          @InitiatorMinimumOnlineMs, @ParticipantMinimumOnlineMs,
                          @InitiatorCooldownMs, @TargetCooldownMs, @GlobalCooldownMs,
                          @MutualExclusionScope, @AllowVoteChange, @UpdatedAtUtc, 0);",
                    parameters,
                    transaction);
            }
            else if (connection.Execute(
                    @"UPDATE vote_configurations
                      SET vote_kind = @VoteKind, enabled = @Enabled,
                          duration_ms = @DurationMs,
                          threshold_percent = @ThresholdPercent,
                          minimum_participants = @MinimumParticipants,
                          initiator_minimum_online_ms = @InitiatorMinimumOnlineMs,
                          participant_minimum_online_ms = @ParticipantMinimumOnlineMs,
                          initiator_cooldown_ms = @InitiatorCooldownMs,
                          target_cooldown_ms = @TargetCooldownMs,
                          global_cooldown_ms = @GlobalCooldownMs,
                          mutual_exclusion_scope = @MutualExclusionScope,
                          allow_vote_change = @AllowVoteChange,
                          updated_at_utc = @UpdatedAtUtc,
                          row_version = row_version + 1
                      WHERE configuration_id = @ConfigurationId
                        AND row_version = @ExpectedRowVersion;",
                    parameters,
                    transaction) != 1)
            {
                throw new CommunityConflictException();
            }
            var saved = connection.QuerySingle<ConfigurationRow>(
                ConfigurationSelect + " WHERE configuration_id = @ConfigurationId;",
                new { configuration.ConfigurationId },
                transaction);
            transaction.Commit();
            return ToConfiguration(saved);
        }

        public VoteStartResult TryStart(VoteRoundDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var request = draft.Request;
            var configuration = draft.Configuration;
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);

            var existing = connection.QuerySingleOrDefault<RoundRow>(
                RoundSelect + " WHERE idempotency_key = @IdempotencyKey;",
                new { request.IdempotencyKey },
                transaction);
            if (existing != null)
            {
                EnsureReplayMatches(connection, transaction, existing, draft);
                transaction.Commit();
                return new VoteStartResult(VoteStartStatus.Replayed, ToRound(existing));
            }

            if (connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM vote_rounds WHERE scope_key = @ScopeKey AND state = 'Open';",
                    new { ScopeKey = configuration.MutualExclusionScope },
                    transaction) != 0)
            {
                transaction.Commit();
                return new VoteStartResult(VoteStartStatus.ScopeBusy, null);
            }

            var openedAtUtc = request.OpenedAtUtc.ToUnixTimeMilliseconds();
            if (HasRecent(
                    connection,
                    transaction,
                    "initiator_crossplatform_id = @Identity",
                    request.InitiatorCrossplatformId,
                    openedAtUtc,
                    Milliseconds(configuration.InitiatorCooldown)))
            {
                transaction.Commit();
                return new VoteStartResult(VoteStartStatus.InitiatorCooldown, null);
            }

            if (request.TargetCrossplatformId != null && HasRecent(
                    connection,
                    transaction,
                    "target_crossplatform_id = @Identity",
                    request.TargetCrossplatformId,
                    openedAtUtc,
                    Milliseconds(configuration.TargetCooldown)))
            {
                transaction.Commit();
                return new VoteStartResult(VoteStartStatus.TargetCooldown, null);
            }

            if (HasRecent(
                    connection,
                    transaction,
                    "scope_key = @Identity",
                    configuration.MutualExclusionScope,
                    openedAtUtc,
                    Milliseconds(configuration.GlobalCooldown)))
            {
                transaction.Commit();
                return new VoteStartResult(VoteStartStatus.GlobalCooldown, null);
            }

            connection.Execute(
                @"INSERT INTO vote_rounds (
                      round_id, configuration_id, vote_kind, state,
                      initiator_crossplatform_id, target_crossplatform_id, scope_key,
                      eligible_count, threshold_percent, minimum_participants,
                      allow_vote_change, idempotency_key, correlation_id,
                      opened_at_utc, expires_at_utc, row_version)
                  VALUES (
                      @RoundId, @ConfigurationId, @VoteKind, 'Open',
                      @InitiatorCrossplatformId, @TargetCrossplatformId, @ScopeKey,
                      @EligibleCount, @ThresholdPercent, @MinimumParticipants,
                      @AllowVoteChange, @IdempotencyKey, @CorrelationId,
                      @OpenedAtUtc, @ExpiresAtUtc, 0);",
                new
                {
                    request.RoundId,
                    configuration.ConfigurationId,
                    VoteKind = request.Kind.ToString(),
                    request.InitiatorCrossplatformId,
                    request.TargetCrossplatformId,
                    ScopeKey = configuration.MutualExclusionScope,
                    EligibleCount = draft.EligibleCrossplatformIds.Count,
                    configuration.ThresholdPercent,
                    configuration.MinimumParticipants,
                    AllowVoteChange = configuration.AllowVoteChange ? 1 : 0,
                    request.IdempotencyKey,
                    request.CorrelationId,
                    OpenedAtUtc = openedAtUtc,
                    ExpiresAtUtc = draft.ExpiresAtUtc.ToUnixTimeMilliseconds()
                },
                transaction);
            foreach (var crossplatformId in draft.EligibleCrossplatformIds)
            {
                connection.Execute(
                    @"INSERT INTO vote_eligible_players (
                          round_id, crossplatform_id, snapshotted_at_utc)
                      VALUES (@RoundId, @CrossplatformId, @SnapshottedAtUtc);",
                    new
                    {
                        request.RoundId,
                        CrossplatformId = crossplatformId,
                        SnapshottedAtUtc = openedAtUtc
                    },
                    transaction);
            }

            var inserted = GetRound(connection, transaction, request.RoundId);
            transaction.Commit();
            return new VoteStartResult(VoteStartStatus.Started, inserted);
        }

        public VoteRoundSnapshot GetRound(string roundId)
        {
            roundId = RequireText(roundId, nameof(roundId));
            using var connection = connectionFactory.Open();
            return GetRound(connection, null, roundId);
        }

        public VoteRoundSnapshot? FindOpenRound(VoteKind kind, string crossplatformId)
        {
            RequireDefined(kind, nameof(kind));
            crossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            using var connection = connectionFactory.Open();
            var row = connection.QueryFirstOrDefault<RoundRow>(
                RoundSelect + @"
                    WHERE round_id = (
                        SELECT rounds.round_id
                        FROM vote_rounds AS rounds
                        INNER JOIN vote_eligible_players AS eligible
                            ON eligible.round_id = rounds.round_id
                        WHERE rounds.vote_kind = @Kind AND rounds.state = 'Open'
                          AND eligible.crossplatform_id = @CrossplatformId
                        ORDER BY rounds.opened_at_utc DESC, rounds.round_id DESC
                        LIMIT 1);",
                new { Kind = kind.ToString(), CrossplatformId = crossplatformId });
            return row == null ? null : ToRound(row);
        }

        public VoteCastResult Cast(
            string roundId,
            string crossplatformId,
            VoteChoice choice,
            DateTimeOffset castAtUtc)
        {
            roundId = RequireText(roundId, nameof(roundId));
            crossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            RequireDefined(choice, nameof(choice));
            RequireUtc(castAtUtc, nameof(castAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var row = connection.QuerySingleOrDefault<RoundRow>(
                RoundSelect + " WHERE round_id = @RoundId;",
                new { RoundId = roundId },
                transaction);
            if (row == null)
            {
                transaction.Commit();
                return new VoteCastResult(VoteCastStatus.RoundNotFound, null);
            }

            var round = ToRound(row);
            if (round.State != VoteRoundState.Open)
            {
                transaction.Commit();
                return new VoteCastResult(VoteCastStatus.RoundClosed, round);
            }
            if (castAtUtc >= round.ExpiresAtUtc)
            {
                transaction.Commit();
                return new VoteCastResult(VoteCastStatus.VotingExpired, round);
            }
            if (connection.ExecuteScalar<int>(
                    @"SELECT COUNT(*) FROM vote_eligible_players
                      WHERE round_id = @RoundId AND crossplatform_id = @CrossplatformId;",
                    new { RoundId = roundId, CrossplatformId = crossplatformId },
                    transaction) == 0)
            {
                transaction.Commit();
                return new VoteCastResult(VoteCastStatus.NotEligible, round);
            }

            var existing = connection.QuerySingleOrDefault<BallotRow>(
                @"SELECT choice AS Choice, change_count AS ChangeCount
                  FROM vote_ballots
                  WHERE round_id = @RoundId AND crossplatform_id = @CrossplatformId;",
                new { RoundId = roundId, CrossplatformId = crossplatformId },
                transaction);
            if (existing == null)
            {
                var timestamp = castAtUtc.ToUnixTimeMilliseconds();
                connection.Execute(
                    @"INSERT INTO vote_ballots (
                          ballot_id, round_id, crossplatform_id, choice,
                          change_count, cast_at_utc, updated_at_utc)
                      VALUES (@BallotId, @RoundId, @CrossplatformId, @Choice, 0, @Timestamp, @Timestamp);",
                    new
                    {
                        BallotId = Guid.NewGuid().ToString("D"),
                        RoundId = roundId,
                        CrossplatformId = crossplatformId,
                        Choice = choice.ToString(),
                        Timestamp = timestamp
                    },
                    transaction);
                transaction.Commit();
                return new VoteCastResult(VoteCastStatus.Accepted, round);
            }

            if (string.Equals(existing.Choice, choice.ToString(), StringComparison.Ordinal))
            {
                transaction.Commit();
                return new VoteCastResult(VoteCastStatus.Replayed, round);
            }
            if (!round.AllowVoteChange || existing.ChangeCount >= 1)
            {
                transaction.Commit();
                return new VoteCastResult(VoteCastStatus.ChangeNotAllowed, round);
            }

            connection.Execute(
                @"UPDATE vote_ballots
                  SET choice = @Choice, change_count = change_count + 1,
                      updated_at_utc = @UpdatedAtUtc
                  WHERE round_id = @RoundId AND crossplatform_id = @CrossplatformId
                    AND change_count = 0;",
                new
                {
                    Choice = choice.ToString(),
                    UpdatedAtUtc = castAtUtc.ToUnixTimeMilliseconds(),
                    RoundId = roundId,
                    CrossplatformId = crossplatformId
                },
                transaction);
            transaction.Commit();
            return new VoteCastResult(VoteCastStatus.Changed, round);
        }

        public VoteSettlementResult TrySettle(string roundId, DateTimeOffset settledAtUtc)
        {
            roundId = RequireText(roundId, nameof(roundId));
            RequireUtc(settledAtUtc, nameof(settledAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var round = GetRound(connection, transaction, roundId);
            var counts = GetCounts(connection, transaction, roundId);
            if (round.State != VoteRoundState.Open)
            {
                transaction.Commit();
                return new VoteSettlementResult(
                    VoteSettlementStatus.AlreadySettled,
                    round,
                    counts.ParticipantCount,
                    counts.YesCount,
                    counts.NoCount,
                    false);
            }
            if (settledAtUtc < round.ExpiresAtUtc)
            {
                transaction.Commit();
                return new VoteSettlementResult(
                    VoteSettlementStatus.NotDue,
                    round,
                    counts.ParticipantCount,
                    counts.YesCount,
                    counts.NoCount,
                    false);
            }

            var next = counts.ParticipantCount < round.MinimumParticipants
                ? VoteRoundState.Expired
                : (long)counts.YesCount * 100 >=
                  (long)round.ThresholdPercent * counts.ParticipantCount
                    ? VoteRoundState.Passed
                    : VoteRoundState.Rejected;
            var changed = connection.Execute(
                @"UPDATE vote_rounds
                  SET state = @State, settled_at_utc = @SettledAtUtc,
                      row_version = row_version + 1
                  WHERE round_id = @RoundId AND state = 'Open' AND row_version = @RowVersion;",
                new
                {
                    State = next.ToString(),
                    SettledAtUtc = settledAtUtc.ToUnixTimeMilliseconds(),
                    RoundId = roundId,
                    round.RowVersion
                },
                transaction);
            var settled = GetRound(connection, transaction, roundId);
            transaction.Commit();
            return new VoteSettlementResult(
                changed == 1 ? VoteSettlementStatus.Settled : VoteSettlementStatus.AlreadySettled,
                settled,
                counts.ParticipantCount,
                counts.YesCount,
                counts.NoCount,
                changed == 1);
        }

        public bool TryQueueAction(string roundId, long expectedRowVersion, DateTimeOffset queuedAtUtc)
        {
            roundId = RequireText(roundId, nameof(roundId));
            if (expectedRowVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            RequireUtc(queuedAtUtc, nameof(queuedAtUtc));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE vote_rounds
                  SET state = 'ActionQueued', row_version = row_version + 1
                  WHERE round_id = @RoundId AND state = 'Passed'
                    AND row_version = @ExpectedRowVersion;",
                new { RoundId = roundId, ExpectedRowVersion = expectedRowVersion }) == 1;
        }

        public bool TryCompleteAction(
            string roundId,
            long expectedRowVersion,
            VoteRoundState resultState,
            string? actionJobId,
            string? actionOperationId,
            DateTimeOffset completedAtUtc)
        {
            roundId = RequireText(roundId, nameof(roundId));
            if (expectedRowVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            RequireUtc(completedAtUtc, nameof(completedAtUtc));
            if (!VoteStateMachine.CanTransition(VoteRoundState.ActionQueued, resultState))
                throw new ArgumentOutOfRangeException(nameof(resultState));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE vote_rounds
                  SET state = @State, action_job_id = @ActionJobId,
                      action_operation_id = @ActionOperationId,
                      action_completed_at_utc = @CompletedAtUtc,
                      row_version = row_version + 1
                  WHERE round_id = @RoundId AND state = 'ActionQueued'
                    AND row_version = @ExpectedRowVersion;",
                new
                {
                    State = resultState.ToString(),
                    ActionJobId = NullIfWhiteSpace(actionJobId),
                    ActionOperationId = NullIfWhiteSpace(actionOperationId),
                    CompletedAtUtc = completedAtUtc.ToUnixTimeMilliseconds(),
                    RoundId = roundId,
                    ExpectedRowVersion = expectedRowVersion
                }) == 1;
        }

        public IReadOnlyList<VoteRoundSnapshot> ListActionQueued()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<RoundRow>(
                    RoundSelect + " WHERE state = 'ActionQueued' ORDER BY opened_at_utc, round_id;")
                .Select(ToRound)
                .ToArray();
        }

        public IReadOnlyList<VoteRoundSnapshot> ListRounds()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<RoundRow>(
                    RoundSelect + " ORDER BY opened_at_utc ASC, round_id ASC;")
                .Select(ToRound)
                .ToArray();
        }

        public IReadOnlyList<VoteRoundSnapshot> ListDueOpenRounds(DateTimeOffset dueAtUtc)
        {
            RequireUtc(dueAtUtc, nameof(dueAtUtc));
            using var connection = connectionFactory.Open();
            return connection.Query<RoundRow>(
                    RoundSelect + @" WHERE state = 'Open' AND expires_at_utc <= @DueAtUtc
                                     ORDER BY expires_at_utc, opened_at_utc, round_id;",
                    new { DueAtUtc = dueAtUtc.ToUnixTimeMilliseconds() })
                .Select(ToRound)
                .ToArray();
        }

        private static void EnsureReplayMatches(
            IDbConnection connection,
            IDbTransaction transaction,
            RoundRow existing,
            VoteRoundDraft draft)
        {
            var request = draft.Request;
            var configuration = draft.Configuration;
            var savedEligible = connection.Query<string>(
                    @"SELECT crossplatform_id FROM vote_eligible_players
                      WHERE round_id = @RoundId ORDER BY crossplatform_id;",
                    new { existing.RoundId },
                    transaction)
                .ToArray();
            if (!string.Equals(existing.RoundId, request.RoundId, StringComparison.Ordinal) ||
                !string.Equals(existing.ConfigurationId, configuration.ConfigurationId, StringComparison.Ordinal) ||
                !string.Equals(existing.VoteKind, request.Kind.ToString(), StringComparison.Ordinal) ||
                !string.Equals(existing.InitiatorCrossplatformId, request.InitiatorCrossplatformId, StringComparison.Ordinal) ||
                !string.Equals(existing.TargetCrossplatformId, request.TargetCrossplatformId, StringComparison.Ordinal) ||
                !savedEligible.SequenceEqual(draft.EligibleCrossplatformIds, StringComparer.Ordinal))
            {
                throw new VoteIdempotencyConflictException();
            }
        }

        private static bool HasRecent(
            IDbConnection connection,
            IDbTransaction transaction,
            string predicate,
            string identity,
            long openedAtUtc,
            long cooldownMs)
        {
            if (cooldownMs <= 0) return false;
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM vote_rounds WHERE " + predicate +
                " AND opened_at_utc > @CooldownBoundary AND opened_at_utc <= @OpenedAtUtc;",
                new
                {
                    Identity = identity,
                    CooldownBoundary = openedAtUtc - cooldownMs,
                    OpenedAtUtc = openedAtUtc
                },
                transaction) != 0;
        }

        private static VoteCounts GetCounts(
            IDbConnection connection,
            IDbTransaction transaction,
            string roundId) =>
            connection.QuerySingle<VoteCounts>(
                @"SELECT COUNT(*) AS ParticipantCount,
                         COALESCE(SUM(CASE WHEN choice = 'Yes' THEN 1 ELSE 0 END), 0) AS YesCount,
                         COALESCE(SUM(CASE WHEN choice = 'No' THEN 1 ELSE 0 END), 0) AS NoCount
                  FROM vote_ballots WHERE round_id = @RoundId;",
                new { RoundId = roundId },
                transaction);

        private static VoteRoundSnapshot GetRound(
            IDbConnection connection,
            IDbTransaction? transaction,
            string roundId)
        {
            var row = connection.QuerySingleOrDefault<RoundRow>(
                RoundSelect + " WHERE round_id = @RoundId;",
                new { RoundId = roundId },
                transaction);
            return row == null ? throw new VoteRoundNotFoundException() : ToRound(row);
        }

        private static VoteConfiguration ToConfiguration(ConfigurationRow row) =>
            new VoteConfiguration(
                row.ConfigurationId,
                Parse<VoteKind>(row.VoteKind),
                row.Enabled != 0,
                TimeSpan.FromMilliseconds(row.DurationMs),
                row.ThresholdPercent,
                row.MinimumParticipants,
                TimeSpan.FromMilliseconds(row.InitiatorMinimumOnlineMs),
                TimeSpan.FromMilliseconds(row.ParticipantMinimumOnlineMs),
                TimeSpan.FromMilliseconds(row.InitiatorCooldownMs),
                TimeSpan.FromMilliseconds(row.TargetCooldownMs),
                TimeSpan.FromMilliseconds(row.GlobalCooldownMs),
                row.MutualExclusionScope,
                row.AllowVoteChange != 0,
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.RowVersion);

        private static VoteRoundSnapshot ToRound(RoundRow row) =>
            new VoteRoundSnapshot(
                row.RoundId,
                row.ConfigurationId,
                Parse<VoteKind>(row.VoteKind),
                Parse<VoteRoundState>(row.State),
                row.InitiatorCrossplatformId,
                row.TargetCrossplatformId,
                row.ScopeKey,
                row.EligibleCount,
                row.ThresholdPercent,
                row.MinimumParticipants,
                row.AllowVoteChange != 0,
                row.IdempotencyKey,
                row.ActionJobId,
                row.ActionOperationId,
                row.CorrelationId,
                DateTimeOffset.FromUnixTimeMilliseconds(row.OpenedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.ExpiresAtUtc),
                FromNullableUnixMilliseconds(row.SettledAtUtc),
                FromNullableUnixMilliseconds(row.ActionCompletedAtUtc),
                row.RowVersion);

        private static T Parse<T>(string value) where T : struct, Enum =>
            (T)Enum.Parse(typeof(T), value, ignoreCase: false);

        private static DateTimeOffset? FromNullableUnixMilliseconds(long? value) =>
            value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null;

        private static long Milliseconds(TimeSpan value) => checked((long)value.TotalMilliseconds);

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
            return value;
        }

        private static string? NullIfWhiteSpace(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        private static void RequireDefined<T>(T value, string parameterName) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(parameterName);
        }

        private sealed class ConfigurationRow
        {
            public string ConfigurationId { get; set; } = string.Empty;
            public string VoteKind { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public long DurationMs { get; set; }
            public int ThresholdPercent { get; set; }
            public int MinimumParticipants { get; set; }
            public long InitiatorMinimumOnlineMs { get; set; }
            public long ParticipantMinimumOnlineMs { get; set; }
            public long InitiatorCooldownMs { get; set; }
            public long TargetCooldownMs { get; set; }
            public long GlobalCooldownMs { get; set; }
            public string MutualExclusionScope { get; set; } = string.Empty;
            public int AllowVoteChange { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class RoundRow
        {
            public string RoundId { get; set; } = string.Empty;
            public string ConfigurationId { get; set; } = string.Empty;
            public string VoteKind { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string InitiatorCrossplatformId { get; set; } = string.Empty;
            public string? TargetCrossplatformId { get; set; }
            public string ScopeKey { get; set; } = string.Empty;
            public int EligibleCount { get; set; }
            public int ThresholdPercent { get; set; }
            public int MinimumParticipants { get; set; }
            public int AllowVoteChange { get; set; }
            public string IdempotencyKey { get; set; } = string.Empty;
            public string? ActionJobId { get; set; }
            public string? ActionOperationId { get; set; }
            public string? CorrelationId { get; set; }
            public long OpenedAtUtc { get; set; }
            public long ExpiresAtUtc { get; set; }
            public long? SettledAtUtc { get; set; }
            public long? ActionCompletedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class BallotRow
        {
            public string Choice { get; set; } = string.Empty;
            public int ChangeCount { get; set; }
        }

        private sealed class VoteCounts
        {
            public int ParticipantCount { get; set; }
            public int YesCount { get; set; }
            public int NoCount { get; set; }
        }
    }
}
