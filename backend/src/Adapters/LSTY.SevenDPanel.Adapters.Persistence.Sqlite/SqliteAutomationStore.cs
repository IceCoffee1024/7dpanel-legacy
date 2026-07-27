using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Domain.Automations;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteAutomationStore :
        IAutomationExecutionRecoveryStore,
        IAutomationExecutionQuery
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteAutomationStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public IReadOnlyList<AutomationRule> ListRules()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<string>(
                    @"SELECT rule_id FROM automation_rules
                      WHERE deleted = 0
                      ORDER BY name COLLATE NOCASE, rule_id;")
                .Select(ruleId => LoadRule(connection, ruleId))
                .ToArray();
        }

        public AutomationRule? FindRule(string ruleId)
        {
            RequireText(ruleId, nameof(ruleId));
            using var connection = connectionFactory.Open();
            return connection.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM automation_rules
                  WHERE rule_id = @RuleId AND deleted = 0;",
                new { RuleId = ruleId }) == 0
                ? null
                : LoadRule(connection, ruleId);
        }

        public void SaveRule(AutomationRule rule, long expectedVersion)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            if (expectedVersion < 0 || rule.Version != expectedVersion + 1)
                throw new ArgumentOutOfRangeException(nameof(expectedVersion));
            var cooldownSeconds = WholeSeconds(rule.CooldownDuration, nameof(rule));

            using var connection = connectionFactory.Open();
            ExecuteImmediate(connection, () =>
            {
                var parameters = new
                {
                    RuleId = rule.Id,
                    rule.Version,
                    rule.Name,
                    TriggerType = rule.Trigger.Type,
                    Enabled = rule.IsEnabled ? 1 : 0,
                    CooldownSeconds = cooldownSeconds,
                    CooldownScope = rule.CooldownScope.ToString(),
                    ConcurrencyPolicy = rule.ConcurrencyPolicy.ToString(),
                    FailurePolicy = rule.FailurePolicy.ToString(),
                    CreatedUtc = Milliseconds(rule.CreatedAtUtc),
                    UpdatedUtc = Milliseconds(rule.UpdatedAtUtc),
                    ExpectedVersion = expectedVersion
                };
                int affected;
                if (expectedVersion == 0)
                {
                    affected = connection.Execute(
                        @"INSERT INTO automation_rules (
                              rule_id, version, name, trigger_type, enabled, deleted, cooldown_seconds,
                              cooldown_scope, concurrency_policy, failure_policy,
                              created_utc, updated_utc)
                          VALUES (
                              @RuleId, @Version, @Name, @TriggerType, @Enabled, 0, @CooldownSeconds,
                              @CooldownScope, @ConcurrencyPolicy, @FailurePolicy,
                              @CreatedUtc, @UpdatedUtc)
                          ON CONFLICT(rule_id) DO NOTHING;",
                        parameters);
                }
                else
                {
                    affected = connection.Execute(
                        @"UPDATE automation_rules
                          SET version = @Version,
                              name = @Name,
                              trigger_type = @TriggerType,
                              enabled = @Enabled,
                              cooldown_seconds = @CooldownSeconds,
                              cooldown_scope = @CooldownScope,
                              concurrency_policy = @ConcurrencyPolicy,
                              failure_policy = @FailurePolicy,
                              updated_utc = @UpdatedUtc
                          WHERE rule_id = @RuleId
                            AND version = @ExpectedVersion
                            AND deleted = 0;",
                        parameters);
                }

                if (affected != 1) throw new AutomationVersionConflictException();

                if (expectedVersion > 0)
                {
                    connection.Execute(
                        "DELETE FROM automation_condition_nodes WHERE rule_id = @RuleId;",
                        new { RuleId = rule.Id });
                    connection.Execute(
                        "DELETE FROM automation_actions WHERE rule_id = @RuleId;",
                        new { RuleId = rule.Id });
                }

                InsertCondition(connection, rule.Id, null, 0, rule.ConditionRoot);
                for (var ordinal = 0; ordinal < rule.Actions.Count; ordinal++)
                {
                    var action = rule.Actions[ordinal];
                    connection.Execute(
                        @"INSERT INTO automation_actions (
                              action_id, rule_id, ordinal, action_type, target_kind,
                              text_value, reference_id, amount, duration_seconds)
                          VALUES (
                              @ActionId, @RuleId, @Ordinal, @ActionType, @TargetKind,
                              @TextValue, @ReferenceId, @Amount, @DurationSeconds);",
                        new
                        {
                            ActionId = action.Id,
                            RuleId = rule.Id,
                            Ordinal = ordinal,
                            ActionType = action.Type,
                            action.TargetKind,
                            action.TextValue,
                            action.ReferenceId,
                            action.Amount,
                            DurationSeconds = action.Duration.HasValue
                                ? WholeSeconds(action.Duration.Value, nameof(action.Duration))
                                : (long?)null
                        });
                }
            });
        }

        public void DeleteRule(
            string ruleId,
            long expectedVersion,
            DateTimeOffset deletedAtUtc)
        {
            RequireText(ruleId, nameof(ruleId));
            if (expectedVersion <= 0 || expectedVersion == long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(expectedVersion));
            RequireUtc(deletedAtUtc, nameof(deletedAtUtc));

            using var connection = connectionFactory.Open();
            ExecuteImmediate(connection, () =>
            {
                var affected = connection.Execute(
                    @"UPDATE automation_rules
                      SET version = version + 1,
                          enabled = 0,
                          deleted = 1,
                          updated_utc = @DeletedAtUtc
                      WHERE rule_id = @RuleId
                        AND version = @ExpectedVersion
                        AND deleted = 0
                        AND created_utc <= @DeletedAtUtc;",
                    new
                    {
                        RuleId = ruleId,
                        ExpectedVersion = expectedVersion,
                        DeletedAtUtc = Milliseconds(deletedAtUtc)
                    });
                if (affected != 1) throw new AutomationVersionConflictException();
            });
        }

        public void SaveTrigger(AutomationTriggerSnapshot trigger)
        {
            if (trigger == null) throw new ArgumentNullException(nameof(trigger));
            RequireText(trigger.TriggerId, nameof(trigger));
            RequireText(trigger.TriggerType, nameof(trigger));
            RequireUtc(trigger.OccurredAtUtc, nameof(trigger));
            if (trigger.ScheduledForUtc.HasValue)
                RequireUtc(trigger.ScheduledForUtc.Value, nameof(trigger));
            if (trigger.GapIds == null) throw new ArgumentException("Trigger gap IDs are required.", nameof(trigger));
            var gapIds = trigger.GapIds.ToArray();
            if (gapIds.Any(string.IsNullOrWhiteSpace) ||
                gapIds.Distinct(StringComparer.Ordinal).Count() != gapIds.Length)
                throw new ArgumentException("Trigger gap IDs must be unique stable IDs.", nameof(trigger));

            using var connection = connectionFactory.Open();
            ExecuteImmediate(connection, () =>
            {
                var parameters = new
                {
                    trigger.TriggerId,
                    trigger.TriggerType,
                    OccurredUtc = Milliseconds(trigger.OccurredAtUtc),
                    trigger.ActorCrossplatformId,
                    trigger.ActorEntityId,
                    trigger.ActorGroup,
                    trigger.PermissionLevel,
                    trigger.ChatText,
                    ScheduledForUtc = NullableMilliseconds(trigger.ScheduledForUtc),
                    trigger.BloodMoonPhase
                };
                var inserted = connection.Execute(
                    @"INSERT INTO automation_triggers (
                          trigger_id, trigger_type, occurred_utc, actor_crossplatform_id,
                          actor_entity_id, actor_group, permission_level, chat_text,
                          scheduled_for_utc, blood_moon_phase)
                      VALUES (
                          @TriggerId, @TriggerType, @OccurredUtc, @ActorCrossplatformId,
                          @ActorEntityId, @ActorGroup, @PermissionLevel, @ChatText,
                          @ScheduledForUtc, @BloodMoonPhase)
                      ON CONFLICT(trigger_id) DO NOTHING;",
                    parameters);

                if (inserted == 0)
                {
                    var exact = connection.ExecuteScalar<int>(
                        @"SELECT COUNT(*) FROM automation_triggers
                          WHERE trigger_id = @TriggerId
                            AND trigger_type = @TriggerType
                            AND occurred_utc = @OccurredUtc
                            AND actor_crossplatform_id IS @ActorCrossplatformId
                            AND actor_entity_id IS @ActorEntityId
                            AND actor_group IS @ActorGroup
                            AND permission_level IS @PermissionLevel
                            AND chat_text IS @ChatText
                            AND scheduled_for_utc IS @ScheduledForUtc
                            AND blood_moon_phase IS @BloodMoonPhase;",
                        parameters);
                    var existingGaps = connection.Query<string>(
                            @"SELECT gap_id FROM automation_trigger_gaps
                              WHERE trigger_id = @TriggerId ORDER BY gap_id;",
                            new { trigger.TriggerId })
                        .ToArray();
                    if (exact != 1 || !existingGaps.SequenceEqual(
                            gapIds.OrderBy(value => value, StringComparer.Ordinal),
                            StringComparer.Ordinal))
                    {
                        throw new InvalidOperationException("automation_trigger_idempotency_conflict");
                    }
                    return;
                }

                foreach (var gapId in gapIds)
                {
                    connection.Execute(
                        @"INSERT INTO automation_trigger_gaps (trigger_id, gap_id)
                          VALUES (@TriggerId, @GapId);",
                        new { trigger.TriggerId, GapId = gapId });
                }
            });
        }

        public AutomationTriggerSnapshot? FindTrigger(string triggerId)
        {
            RequireText(triggerId, nameof(triggerId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<TriggerRow>(
                @"SELECT trigger_id, trigger_type, occurred_utc, actor_crossplatform_id,
                         actor_entity_id, actor_group, permission_level, chat_text,
                         scheduled_for_utc, blood_moon_phase
                  FROM automation_triggers
                  WHERE trigger_id = @TriggerId;",
                new { TriggerId = triggerId });
            if (row == null) return null;
            var gaps = connection.Query<string>(
                    @"SELECT gap_id FROM automation_trigger_gaps
                      WHERE trigger_id = @TriggerId ORDER BY gap_id;",
                    new { TriggerId = triggerId })
                .ToArray();
            return new AutomationTriggerSnapshot(
                row.trigger_id,
                row.trigger_type,
                Utc(row.occurred_utc),
                row.actor_crossplatform_id,
                row.actor_entity_id,
                row.actor_group,
                row.permission_level,
                row.chat_text,
                NullableUtc(row.scheduled_for_utc),
                row.blood_moon_phase,
                Array.AsReadOnly(gaps));
        }

        public AutomationExecutionStartResult TryStartExecution(AutomationExecutionRecord execution)
        {
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            ValidateExecution(execution);
            using var connection = connectionFactory.Open();
            return ExecuteImmediate(connection, () =>
            {
                var inserted = connection.Execute(
                    @"INSERT INTO automation_executions (
                          execution_id, rule_id, trigger_id, status, correlation_id,
                          started_utc, completed_utc, error_code)
                      VALUES (
                          @ExecutionId, @RuleId, @TriggerId, @Status, @CorrelationId,
                          @StartedUtc, @CompletedUtc, @ErrorCode)
                      ON CONFLICT(rule_id, trigger_id) DO NOTHING;",
                    new
                    {
                        execution.ExecutionId,
                        execution.RuleId,
                        execution.TriggerId,
                        Status = execution.Status.ToString(),
                        execution.CorrelationId,
                        StartedUtc = NullableMilliseconds(execution.StartedAtUtc),
                        CompletedUtc = NullableMilliseconds(execution.CompletedAtUtc),
                        execution.ErrorCode
                    });
                var row = connection.QuerySingle<ExecutionRow>(
                    @"SELECT execution_id, rule_id, trigger_id, status, correlation_id,
                             started_utc, completed_utc, error_code
                      FROM automation_executions
                      WHERE rule_id = @RuleId AND trigger_id = @TriggerId;",
                    new { execution.RuleId, execution.TriggerId });
                return new AutomationExecutionStartResult(Map(row), inserted == 1);
            });
        }

        public IReadOnlyList<AutomationExecutionRecord> ListUnfinishedExecutions(int maxCount)
        {
            if (maxCount <= 0 || maxCount > AutomationExecutionRecoveryService.MaxBatchSize)
                throw new ArgumentOutOfRangeException(nameof(maxCount));
            using var connection = connectionFactory.Open();
            return connection.Query<ExecutionRow>(
                    @"SELECT execution_id, rule_id, trigger_id, status, correlation_id,
                             started_utc, completed_utc, error_code
                      FROM automation_executions
                      WHERE status IN ('Pending', 'Running', 'Queued')
                      ORDER BY COALESCE(started_utc, 0), execution_id
                      LIMIT @MaxCount;",
                    new { MaxCount = maxCount })
                .Select(Map)
                .ToArray();
        }

        public IReadOnlyList<AutomationCooldownEvidence> ListCooldownEvidence(int maxCount)
        {
            if (maxCount <= 0 || maxCount > AutomationExecutionRecoveryService.MaxCooldownEvidenceCount)
                throw new ArgumentOutOfRangeException(nameof(maxCount));
            using var connection = connectionFactory.Open();
            return connection.Query<CooldownEvidenceRow>(
                    @"SELECT execution.rule_id,
                             CASE WHEN rule.cooldown_scope = 'RulePlayer'
                                  THEN trigger.actor_crossplatform_id ELSE NULL END
                                 AS actor_crossplatform_id,
                             MAX(COALESCE(execution.started_utc, action.started_utc)) AS started_utc
                      FROM automation_executions AS execution
                      INNER JOIN automation_rules AS rule ON rule.rule_id = execution.rule_id
                      INNER JOIN automation_triggers AS trigger ON trigger.trigger_id = execution.trigger_id
                      LEFT JOIN (
                          SELECT execution_id, MIN(started_utc) AS started_utc
                          FROM automation_action_results
                          WHERE status <> 'Pending'
                          GROUP BY execution_id
                      ) AS action ON action.execution_id = execution.execution_id
                      WHERE rule.deleted = 0
                        AND rule.cooldown_seconds > 0
                        AND COALESCE(execution.started_utc, action.started_utc) IS NOT NULL
                        AND (rule.cooldown_scope = 'Rule'
                             OR trigger.actor_crossplatform_id IS NOT NULL)
                      GROUP BY execution.rule_id,
                               CASE WHEN rule.cooldown_scope = 'RulePlayer'
                                    THEN trigger.actor_crossplatform_id ELSE NULL END
                      ORDER BY started_utc DESC, execution.rule_id,
                               actor_crossplatform_id
                      LIMIT @MaxCount;",
                    new { MaxCount = maxCount })
                .Select(row => new AutomationCooldownEvidence(
                    row.rule_id,
                    row.actor_crossplatform_id,
                    Utc(row.started_utc)))
                .ToArray();
        }

        public IReadOnlyList<AutomationExecutionRecord> ListExecutions(int take)
        {
            if (take < 1 || take > 200) throw new ArgumentOutOfRangeException(nameof(take));
            using var connection = connectionFactory.Open();
            return connection.Query<ExecutionRow>(
                    @"SELECT execution_id, rule_id, trigger_id, status, correlation_id,
                             started_utc, completed_utc, error_code
                      FROM automation_executions
                      ORDER BY COALESCE(started_utc, completed_utc, 0) DESC,
                               execution_id DESC
                      LIMIT @Take;",
                    new { Take = take })
                .Select(Map)
                .ToArray();
        }

        public AutomationExecutionRecord? FindExecution(string executionId)
        {
            RequireText(executionId, nameof(executionId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<ExecutionRow>(
                @"SELECT execution_id, rule_id, trigger_id, status, correlation_id,
                         started_utc, completed_utc, error_code
                  FROM automation_executions
                  WHERE execution_id = @ExecutionId;",
                new { ExecutionId = executionId });
            return row == null ? null : Map(row);
        }

        public bool TryMarkExecutionResultUnknown(
            string executionId,
            AutomationExecutionStatus expectedStatus,
            DateTimeOffset completedAtUtc,
            string errorCode)
        {
            RequireText(executionId, nameof(executionId));
            if (expectedStatus != AutomationExecutionStatus.Pending &&
                expectedStatus != AutomationExecutionStatus.Running &&
                expectedStatus != AutomationExecutionStatus.Queued)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedStatus));
            }
            RequireUtc(completedAtUtc, nameof(completedAtUtc));
            RequireText(errorCode, nameof(errorCode));

            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE automation_executions
                  SET status = 'ResultUnknown',
                      completed_utc = @CompletedAtUtc,
                      error_code = @ErrorCode
                  WHERE execution_id = @ExecutionId
                    AND status = @ExpectedStatus
                    AND status IN ('Pending', 'Running', 'Queued');",
                new
                {
                    ExecutionId = executionId,
                    ExpectedStatus = expectedStatus.ToString(),
                    CompletedAtUtc = Milliseconds(completedAtUtc),
                    ErrorCode = errorCode
                }) == 1;
        }

        public bool TryMarkExecutionRunning(
            string executionId,
            AutomationExecutionStatus expectedStatus,
            DateTimeOffset startedAtUtc)
        {
            RequireText(executionId, nameof(executionId));
            if (expectedStatus != AutomationExecutionStatus.Pending &&
                expectedStatus != AutomationExecutionStatus.Queued)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedStatus));
            }
            RequireUtc(startedAtUtc, nameof(startedAtUtc));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE automation_executions
                  SET status = 'Running', started_utc = @StartedAtUtc
                  WHERE execution_id = @ExecutionId
                    AND status = @ExpectedStatus
                    AND started_utc IS NULL
                    AND completed_utc IS NULL;",
                new
                {
                    ExecutionId = executionId,
                    ExpectedStatus = expectedStatus.ToString(),
                    StartedAtUtc = Milliseconds(startedAtUtc)
                }) == 1;
        }

        public bool TryCompleteExecution(
            string executionId,
            AutomationExecutionStatus expectedStatus,
            AutomationExecutionStatus terminalStatus,
            DateTimeOffset completedAtUtc,
            string? errorCode)
        {
            RequireText(executionId, nameof(executionId));
            if (expectedStatus != AutomationExecutionStatus.Pending &&
                expectedStatus != AutomationExecutionStatus.Running &&
                expectedStatus != AutomationExecutionStatus.Queued)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedStatus));
            }
            if (terminalStatus != AutomationExecutionStatus.Skipped &&
                terminalStatus != AutomationExecutionStatus.Succeeded &&
                terminalStatus != AutomationExecutionStatus.Failed &&
                terminalStatus != AutomationExecutionStatus.ResultUnknown)
            {
                throw new ArgumentOutOfRangeException(nameof(terminalStatus));
            }
            RequireUtc(completedAtUtc, nameof(completedAtUtc));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE automation_executions
                  SET status = @TerminalStatus,
                      completed_utc = @CompletedAtUtc,
                      error_code = @ErrorCode
                  WHERE execution_id = @ExecutionId
                    AND status = @ExpectedStatus
                    AND completed_utc IS NULL;",
                new
                {
                    ExecutionId = executionId,
                    ExpectedStatus = expectedStatus.ToString(),
                    TerminalStatus = terminalStatus.ToString(),
                    CompletedAtUtc = Milliseconds(completedAtUtc),
                    ErrorCode = errorCode
                }) == 1;
        }

        public void RecordConditionResult(AutomationConditionExecutionResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            using var connection = connectionFactory.Open();
            var affected = connection.Execute(
                @"INSERT INTO automation_condition_results (
                      execution_id, node_id, truth, value_summary)
                  SELECT @ExecutionId, @NodeId, @Truth, @ValueSummary
                  FROM automation_executions AS execution
                  INNER JOIN automation_condition_nodes AS node
                      ON node.rule_id = execution.rule_id
                  WHERE execution.execution_id = @ExecutionId AND node.node_id = @NodeId
                  ON CONFLICT(execution_id, node_id) DO UPDATE SET
                      truth = excluded.truth,
                      value_summary = excluded.value_summary;",
                new
                {
                    result.ExecutionId,
                    result.NodeId,
                    Truth = result.Truth.ToString(),
                    result.ValueSummary
                });
            if (affected != 1) throw new InvalidOperationException("automation_condition_result_target_missing");
        }

        public void RecordActionResult(AutomationActionExecutionResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.Ordinal < 0) throw new ArgumentOutOfRangeException(nameof(result));
            RequireUtc(result.StartedAtUtc, nameof(result));
            if (result.CompletedAtUtc.HasValue)
                RequireUtc(result.CompletedAtUtc.Value, nameof(result));
            using var connection = connectionFactory.Open();
            var affected = connection.Execute(
                @"INSERT INTO automation_action_results (
                      execution_id, ordinal, action_type, status,
                      consumer_idempotency_key, error_code, started_utc, completed_utc)
                  SELECT @ExecutionId, @Ordinal, @ActionType, @Status,
                      @ConsumerIdempotencyKey, @ErrorCode, @StartedUtc, @CompletedUtc
                  FROM automation_executions AS execution
                  INNER JOIN automation_actions AS action
                      ON action.rule_id = execution.rule_id AND action.ordinal = @Ordinal
                  WHERE execution.execution_id = @ExecutionId
                    AND action.action_type = @ActionType
                   ON CONFLICT(execution_id, ordinal) DO UPDATE SET
                       status = excluded.status,
                       error_code = excluded.error_code,
                       started_utc = CASE
                           WHEN automation_action_results.status = 'Pending'
                           THEN excluded.started_utc
                           ELSE automation_action_results.started_utc
                       END,
                       completed_utc = excluded.completed_utc;",
                new
                {
                    result.ExecutionId,
                    result.Ordinal,
                    result.ActionType,
                    Status = result.Status.ToString(),
                    result.ConsumerIdempotencyKey,
                    result.ErrorCode,
                    StartedUtc = Milliseconds(result.StartedAtUtc),
                    CompletedUtc = NullableMilliseconds(result.CompletedAtUtc)
                });
            if (affected != 1) throw new InvalidOperationException("automation_action_result_target_missing");
        }

        public IReadOnlyList<AutomationConditionExecutionResult> ListConditionResults(string executionId)
        {
            RequireText(executionId, nameof(executionId));
            using var connection = connectionFactory.Open();
            return connection.Query<ConditionResultRow>(
                    @"SELECT execution_id, node_id, truth, value_summary
                      FROM automation_condition_results
                      WHERE execution_id = @ExecutionId
                      ORDER BY node_id;",
                    new { ExecutionId = executionId })
                .Select(row => new AutomationConditionExecutionResult(
                    row.execution_id,
                    row.node_id,
                    Parse<AutomationTruth>(row.truth),
                    row.value_summary))
                .ToArray();
        }

        public IReadOnlyList<AutomationActionExecutionResult> ListActionResults(string executionId)
        {
            RequireText(executionId, nameof(executionId));
            using var connection = connectionFactory.Open();
            return connection.Query<ActionResultRow>(
                    @"SELECT execution_id, ordinal, action_type, status,
                             consumer_idempotency_key, error_code, started_utc, completed_utc
                      FROM automation_action_results
                      WHERE execution_id = @ExecutionId
                      ORDER BY ordinal;",
                    new { ExecutionId = executionId })
                .Select(row => new AutomationActionExecutionResult(
                    row.execution_id,
                    row.ordinal,
                    row.action_type,
                    Parse<AutomationActionResultStatus>(row.status),
                    row.consumer_idempotency_key,
                    row.error_code,
                    Utc(row.started_utc),
                    NullableUtc(row.completed_utc)))
                .ToArray();
        }

        private static AutomationRule LoadRule(SqliteConnection connection, string ruleId)
        {
            var rule = connection.QuerySingle<RuleRow>(
                @"SELECT rule_id, version, name, trigger_type, enabled, cooldown_seconds,
                         cooldown_scope, concurrency_policy, failure_policy,
                         created_utc, updated_utc
                  FROM automation_rules WHERE rule_id = @RuleId;",
                new { RuleId = ruleId });
            var nodes = connection.Query<ConditionNodeRow>(
                    @"SELECT node_id, parent_node_id, ordinal, node_kind, field_key,
                             operator, scalar_value, min_value, max_value
                      FROM automation_condition_nodes
                      WHERE rule_id = @RuleId
                      ORDER BY parent_node_id, ordinal;",
                    new { RuleId = ruleId })
                .ToArray();
            var values = connection.Query<SetValueRow>(
                    @"SELECT value.node_id, value.ordinal, value.value
                      FROM automation_condition_set_values AS value
                      INNER JOIN automation_condition_nodes AS node ON node.node_id = value.node_id
                      WHERE node.rule_id = @RuleId
                      ORDER BY value.node_id, value.ordinal;",
                    new { RuleId = ruleId })
                .GroupBy(value => value.node_id, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(value => value.value).ToArray(),
                    StringComparer.Ordinal);
            var root = nodes.Single(node => node.parent_node_id == null);
            var condition = BuildCondition(root, nodes, values);
            var actions = connection.Query<ActionRow>(
                    @"SELECT action_id, action_type, target_kind, text_value,
                             reference_id, amount, duration_seconds
                      FROM automation_actions
                      WHERE rule_id = @RuleId ORDER BY ordinal;",
                    new { RuleId = ruleId })
                .Select(action => new AutomationAction(
                    action.action_id,
                    action.action_type,
                    action.target_kind,
                    action.text_value,
                    action.reference_id,
                    action.amount,
                    action.duration_seconds.HasValue
                        ? TimeSpan.FromSeconds(action.duration_seconds.Value)
                        : (TimeSpan?)null))
                .ToArray();
            return new AutomationRule(
                rule.rule_id,
                rule.version,
                rule.name,
                rule.enabled != 0,
                new AutomationTrigger(rule.trigger_type),
                condition,
                actions,
                TimeSpan.FromSeconds(rule.cooldown_seconds),
                Parse<AutomationCooldownScope>(rule.cooldown_scope),
                Parse<AutomationConcurrencyPolicy>(rule.concurrency_policy),
                Parse<AutomationFailurePolicy>(rule.failure_policy),
                Utc(rule.created_utc),
                Utc(rule.updated_utc));
        }

        private static AutomationCondition BuildCondition(
            ConditionNodeRow node,
            IReadOnlyList<ConditionNodeRow> nodes,
            IReadOnlyDictionary<string, string[]> setValues)
        {
            var children = nodes
                .Where(candidate => string.Equals(
                    candidate.parent_node_id,
                    node.node_id,
                    StringComparison.Ordinal))
                .OrderBy(candidate => candidate.ordinal)
                .Select(child => BuildCondition(child, nodes, setValues))
                .ToArray();
            switch (node.node_kind)
            {
                case "All":
                    return AutomationCondition.All(node.node_id, children);
                case "Any":
                    return AutomationCondition.Any(node.node_id, children);
                case "Not":
                    return AutomationCondition.Not(node.node_id, children.Single());
            }

            var field = node.field_key!;
            switch (node.@operator)
            {
                case "Equals":
                    return AutomationCondition.TextEquals(node.node_id, field, node.scalar_value!);
                case "NotEquals":
                    return AutomationCondition.TextNotEquals(node.node_id, field, node.scalar_value!);
                case "InSet":
                    return AutomationCondition.InSet(
                        node.node_id,
                        field,
                        setValues.TryGetValue(node.node_id, out var values)
                            ? values
                            : Array.Empty<string>());
                case "NumberRange":
                    return AutomationCondition.NumberRange(
                        node.node_id,
                        field,
                        node.min_value!.Value,
                        node.max_value!.Value);
                case "TimeWindow":
                    return AutomationCondition.TimeWindow(
                        node.node_id,
                        field,
                        new AutomationTimeWindow(
                            node.scalar_value!,
                            TimeOfDay(node.min_value!.Value),
                            TimeOfDay(node.max_value!.Value)));
                case "PlayerGroup":
                    return AutomationCondition.PlayerGroup(node.node_id, field, node.scalar_value!);
                case "Permission":
                    return AutomationCondition.Permission(node.node_id, field, node.scalar_value!);
                case "Cooldown":
                    return AutomationCondition.Cooldown(node.node_id, field);
                default:
                    throw new InvalidOperationException("automation_condition_operator_invalid");
            }
        }

        private static void InsertCondition(
            SqliteConnection connection,
            string ruleId,
            string? parentNodeId,
            int ordinal,
            AutomationCondition condition)
        {
            string? scalar = condition.ScalarValue;
            long? minimum = condition.MinimumInclusive;
            long? maximum = condition.MaximumInclusive;
            if (condition.Window != null)
            {
                scalar = condition.Window.TimeZoneId;
                minimum = Minutes(condition.Window.StartInclusive);
                maximum = Minutes(condition.Window.EndInclusive);
            }
            connection.Execute(
                @"INSERT INTO automation_condition_nodes (
                      node_id, rule_id, parent_node_id, ordinal, node_kind, field_key,
                      operator, scalar_value, min_value, max_value)
                  VALUES (
                      @NodeId, @RuleId, @ParentNodeId, @Ordinal, @NodeKind, @FieldKey,
                      @Operator, @ScalarValue, @MinValue, @MaxValue);",
                new
                {
                    condition.NodeId,
                    RuleId = ruleId,
                    ParentNodeId = parentNodeId,
                    Ordinal = ordinal,
                    NodeKind = condition.Kind.ToString(),
                    condition.FieldKey,
                    Operator = condition.Operator?.ToString(),
                    ScalarValue = scalar,
                    MinValue = minimum,
                    MaxValue = maximum
                });
            for (var valueOrdinal = 0; valueOrdinal < condition.SetValues.Count; valueOrdinal++)
            {
                connection.Execute(
                    @"INSERT INTO automation_condition_set_values (node_id, ordinal, value)
                      VALUES (@NodeId, @Ordinal, @Value);",
                    new
                    {
                        condition.NodeId,
                        Ordinal = valueOrdinal,
                        Value = condition.SetValues[valueOrdinal]
                    });
            }
            for (var childOrdinal = 0; childOrdinal < condition.Children.Count; childOrdinal++)
            {
                InsertCondition(
                    connection,
                    ruleId,
                    condition.NodeId,
                    childOrdinal,
                    condition.Children[childOrdinal]);
            }
        }

        private static AutomationExecutionRecord Map(ExecutionRow row) => new(
            row.execution_id,
            row.rule_id,
            row.trigger_id,
            Parse<AutomationExecutionStatus>(row.status),
            row.correlation_id,
            NullableUtc(row.started_utc),
            NullableUtc(row.completed_utc),
            row.error_code);

        private static void ValidateExecution(AutomationExecutionRecord execution)
        {
            RequireText(execution.ExecutionId, nameof(execution));
            RequireText(execution.RuleId, nameof(execution));
            RequireText(execution.TriggerId, nameof(execution));
            RequireText(execution.CorrelationId, nameof(execution));
            if (execution.StartedAtUtc.HasValue)
                RequireUtc(execution.StartedAtUtc.Value, nameof(execution));
            if (execution.CompletedAtUtc.HasValue)
                RequireUtc(execution.CompletedAtUtc.Value, nameof(execution));
        }

        private static T ExecuteImmediate<T>(SqliteConnection connection, Func<T> action)
        {
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                var result = action();
                connection.Execute("COMMIT;");
                return result;
            }
            catch
            {
                connection.Execute("ROLLBACK;");
                throw;
            }
        }

        private static void ExecuteImmediate(SqliteConnection connection, Action action) =>
            ExecuteImmediate(connection, () =>
            {
                action();
                return true;
            });

        private static long WholeSeconds(TimeSpan value, string parameterName)
        {
            if (value < TimeSpan.Zero || value.Ticks % TimeSpan.TicksPerSecond != 0)
                throw new ArgumentException("A non-negative whole-second duration is required.", parameterName);
            return value.Ticks / TimeSpan.TicksPerSecond;
        }

        private static int Minutes(AutomationTimeOfDay value) => (value.Hour * 60) + value.Minute;

        private static AutomationTimeOfDay TimeOfDay(long minutes) =>
            new(checked((int)(minutes / 60)), checked((int)(minutes % 60)));

        private static T Parse<T>(string value) where T : struct =>
            Enum.TryParse<T>(value, out var parsed)
                ? parsed
                : throw new InvalidOperationException("automation_store_value_invalid");

        private static void RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A value is required.", parameterName);
        }

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("The timestamp must use UTC.", parameterName);
        }

        private static long Milliseconds(DateTimeOffset value)
        {
            RequireUtc(value, nameof(value));
            return value.ToUnixTimeMilliseconds();
        }

        private static long? NullableMilliseconds(DateTimeOffset? value) =>
            value.HasValue ? Milliseconds(value.Value) : (long?)null;

        private static DateTimeOffset Utc(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);

        private static DateTimeOffset? NullableUtc(long? value) =>
            value.HasValue ? Utc(value.Value) : (DateTimeOffset?)null;

        private sealed class RuleRow
        {
            public string rule_id { get; set; } = string.Empty;
            public long version { get; set; }
            public string name { get; set; } = string.Empty;
            public string trigger_type { get; set; } = string.Empty;
            public long enabled { get; set; }
            public long cooldown_seconds { get; set; }
            public string cooldown_scope { get; set; } = string.Empty;
            public string concurrency_policy { get; set; } = string.Empty;
            public string failure_policy { get; set; } = string.Empty;
            public long created_utc { get; set; }
            public long updated_utc { get; set; }
        }

        private sealed class ConditionNodeRow
        {
            public string node_id { get; set; } = string.Empty;
            public string? parent_node_id { get; set; }
            public int ordinal { get; set; }
            public string node_kind { get; set; } = string.Empty;
            public string? field_key { get; set; }
            public string? @operator { get; set; }
            public string? scalar_value { get; set; }
            public long? min_value { get; set; }
            public long? max_value { get; set; }
        }

        private sealed class SetValueRow
        {
            public string node_id { get; set; } = string.Empty;
            public int ordinal { get; set; }
            public string value { get; set; } = string.Empty;
        }

        private sealed class ActionRow
        {
            public string action_id { get; set; } = string.Empty;
            public string action_type { get; set; } = string.Empty;
            public string target_kind { get; set; } = string.Empty;
            public string? text_value { get; set; }
            public string? reference_id { get; set; }
            public long? amount { get; set; }
            public long? duration_seconds { get; set; }
        }

        private sealed class ExecutionRow
        {
            public string execution_id { get; set; } = string.Empty;
            public string rule_id { get; set; } = string.Empty;
            public string trigger_id { get; set; } = string.Empty;
            public string status { get; set; } = string.Empty;
            public string correlation_id { get; set; } = string.Empty;
            public long? started_utc { get; set; }
            public long? completed_utc { get; set; }
            public string? error_code { get; set; }
        }

        private sealed class TriggerRow
        {
            public string trigger_id { get; set; } = string.Empty;
            public string trigger_type { get; set; } = string.Empty;
            public long occurred_utc { get; set; }
            public string? actor_crossplatform_id { get; set; }
            public long? actor_entity_id { get; set; }
            public string? actor_group { get; set; }
            public int? permission_level { get; set; }
            public string? chat_text { get; set; }
            public long? scheduled_for_utc { get; set; }
            public string? blood_moon_phase { get; set; }
        }

        private sealed class CooldownEvidenceRow
        {
            public string rule_id { get; set; } = string.Empty;
            public string? actor_crossplatform_id { get; set; }
            public long started_utc { get; set; }
        }

        private sealed class ConditionResultRow
        {
            public string execution_id { get; set; } = string.Empty;
            public string node_id { get; set; } = string.Empty;
            public string truth { get; set; } = string.Empty;
            public string? value_summary { get; set; }
        }

        private sealed class ActionResultRow
        {
            public string execution_id { get; set; } = string.Empty;
            public int ordinal { get; set; }
            public string action_type { get; set; } = string.Empty;
            public string status { get; set; } = string.Empty;
            public string consumer_idempotency_key { get; set; } = string.Empty;
            public string? error_code { get; set; }
            public long started_utc { get; set; }
            public long? completed_utc { get; set; }
        }
    }
}
