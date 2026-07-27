using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Domain.Automations;

namespace LSTY.SevenDPanel.Application.Automations
{
    public enum AutomationExecutionStatus
    {
        Pending,
        Running,
        Queued,
        Skipped,
        Succeeded,
        Failed,
        ResultUnknown
    }

    public enum AutomationActionResultStatus
    {
        Pending,
        Running,
        Succeeded,
        Failed,
        ResultUnknown
    }

    public sealed record AutomationTriggerSnapshot(
        string TriggerId,
        string TriggerType,
        DateTimeOffset OccurredAtUtc,
        string? ActorCrossplatformId,
        long? ActorEntityId,
        string? ActorGroup,
        int? PermissionLevel,
        string? ChatText,
        DateTimeOffset? ScheduledForUtc,
        string? BloodMoonPhase,
        IReadOnlyList<string> GapIds);

    public sealed record AutomationExecutionRecord(
        string ExecutionId,
        string RuleId,
        string TriggerId,
        AutomationExecutionStatus Status,
        string CorrelationId,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        string? ErrorCode);

    public sealed record AutomationExecutionStartResult(
        AutomationExecutionRecord Execution,
        bool WasCreated);

    public sealed record AutomationConditionExecutionResult(
        string ExecutionId,
        string NodeId,
        AutomationTruth Truth,
        string? ValueSummary);

    public sealed record AutomationActionExecutionResult(
        string ExecutionId,
        int Ordinal,
        string ActionType,
        AutomationActionResultStatus Status,
        string ConsumerIdempotencyKey,
        string? ErrorCode,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc);

    public sealed record AutomationCooldownEvidence(
        string RuleId,
        string? ActorCrossplatformId,
        DateTimeOffset StartedAtUtc);

    public interface IAutomationStore
    {
        IReadOnlyList<AutomationRule> ListRules();

        AutomationRule? FindRule(string ruleId);

        void SaveRule(AutomationRule rule, long expectedVersion);

        void DeleteRule(
            string ruleId,
            long expectedVersion,
            DateTimeOffset deletedAtUtc);

        void SaveTrigger(AutomationTriggerSnapshot trigger);

        AutomationExecutionStartResult TryStartExecution(AutomationExecutionRecord execution);

        void RecordConditionResult(AutomationConditionExecutionResult result);

        void RecordActionResult(AutomationActionExecutionResult result);

        IReadOnlyList<AutomationConditionExecutionResult> ListConditionResults(string executionId);

        IReadOnlyList<AutomationActionExecutionResult> ListActionResults(string executionId);
    }

    public interface IAutomationExecutionStateStore : IAutomationStore
    {
        AutomationTriggerSnapshot? FindTrigger(string triggerId);

        bool TryMarkExecutionRunning(
            string executionId,
            AutomationExecutionStatus expectedStatus,
            DateTimeOffset startedAtUtc);

        bool TryCompleteExecution(
            string executionId,
            AutomationExecutionStatus expectedStatus,
            AutomationExecutionStatus terminalStatus,
            DateTimeOffset completedAtUtc,
            string? errorCode);
    }

    public interface IAutomationExecutionRecoveryStore : IAutomationExecutionStateStore
    {
        IReadOnlyList<AutomationExecutionRecord> ListUnfinishedExecutions(int maxCount);

        IReadOnlyList<AutomationCooldownEvidence> ListCooldownEvidence(int maxCount);

        bool TryMarkExecutionResultUnknown(
            string executionId,
            AutomationExecutionStatus expectedStatus,
            DateTimeOffset completedAtUtc,
            string errorCode);
    }

    public interface IAutomationExecutionQuery
    {
        IReadOnlyList<AutomationExecutionRecord> ListExecutions(int take);

        AutomationExecutionRecord? FindExecution(string executionId);

        IReadOnlyList<AutomationConditionExecutionResult> ListConditionResults(
            string executionId);

        IReadOnlyList<AutomationActionExecutionResult> ListActionResults(
            string executionId);
    }

    public sealed class AutomationVersionConflictException : InvalidOperationException
    {
        public AutomationVersionConflictException()
            : base("automation_rule_version_conflict")
        {
        }
    }
}
