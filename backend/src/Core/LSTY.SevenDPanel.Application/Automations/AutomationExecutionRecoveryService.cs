using System;
using System.Collections.Generic;
using System.Threading;

namespace LSTY.SevenDPanel.Application.Automations
{
    public sealed record AutomationExecutionRecoveryReport(
        int ExaminedCount,
        int MarkedResultUnknownCount,
        IReadOnlyList<string> UnchangedExecutionIds);

    public sealed class AutomationExecutionRecoveryService
    {
        public const int MaxBatchSize = 256;
        public const int DefaultBatchSize = MaxBatchSize;
        public const int MaxCooldownEvidenceCount = 4096;
        public const string InterruptedErrorCode = "automation_process_interrupted";

        private readonly IAutomationExecutionRecoveryStore store;
        private readonly AutomationExecutionEngine engine;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly int batchSize;

        public AutomationExecutionRecoveryService(
            IAutomationExecutionRecoveryStore store,
            AutomationExecutionEngine engine)
            : this(store, engine, () => DateTimeOffset.UtcNow, DefaultBatchSize)
        {
        }

        internal AutomationExecutionRecoveryService(
            IAutomationExecutionRecoveryStore store,
            AutomationExecutionEngine engine,
            Func<DateTimeOffset> utcNow,
            int batchSize)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            if (batchSize <= 0 || batchSize > MaxBatchSize)
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            this.batchSize = batchSize;
        }

        public AutomationExecutionRecoveryReport Recover()
        {
            var completedAtUtc = utcNow();
            if (completedAtUtc.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("automation_recovery_clock_must_be_utc");

            var cooldownEvidence = store.ListCooldownEvidence(MaxCooldownEvidenceCount) ??
                throw new InvalidOperationException("automation_recovery_store_returned_no_cooldown_evidence");
            if (cooldownEvidence.Count > MaxCooldownEvidenceCount)
                throw new InvalidOperationException("automation_recovery_store_exceeded_cooldown_evidence_limit");
            engine.RestoreCooldowns(cooldownEvidence);

            var executions = store.ListUnfinishedExecutions(batchSize) ??
                throw new InvalidOperationException("automation_recovery_store_returned_no_executions");
            if (executions.Count > batchSize)
                throw new InvalidOperationException("automation_recovery_store_exceeded_batch_size");

            var marked = 0;
            var unchanged = new List<string>();
            foreach (var execution in executions)
            {
                if (execution == null)
                    throw new InvalidOperationException("automation_recovery_store_returned_null_execution");
                if (!IsUnfinished(execution.Status))
                    throw new InvalidOperationException("automation_recovery_store_returned_completed_execution");

                try
                {
                    var rule = store.FindRule(execution.RuleId);
                    var trigger = store.FindTrigger(execution.TriggerId);
                    if (rule != null && trigger != null)
                    {
                        var outcome = engine.RecoverAsync(
                                rule,
                                trigger,
                                execution,
                                CancellationToken.None)
                            .GetAwaiter()
                            .GetResult();
                        if (outcome.Status == AutomationExecutionStatus.ResultUnknown)
                            marked++;
                        continue;
                    }
                }
                catch
                {
                }

                var wasMarked = false;
                try
                {
                    wasMarked = store.TryMarkExecutionResultUnknown(
                        execution.ExecutionId,
                        execution.Status,
                        completedAtUtc,
                        InterruptedErrorCode);
                }
                catch
                {
                }
                if (wasMarked) marked++;
                else unchanged.Add(execution.ExecutionId);
            }

            return new AutomationExecutionRecoveryReport(
                executions.Count,
                marked,
                unchanged.AsReadOnly());
        }

        private static bool IsUnfinished(AutomationExecutionStatus status) =>
            status == AutomationExecutionStatus.Pending ||
            status == AutomationExecutionStatus.Running ||
            status == AutomationExecutionStatus.Queued;
    }
}
