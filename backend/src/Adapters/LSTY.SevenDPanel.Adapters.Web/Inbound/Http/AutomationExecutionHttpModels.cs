using System;
using System.Linq;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Domain.Automations;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class AutomationExecutionHttpResponse
    {
        public AutomationExecutionHttpResponse(
            AutomationExecutionRecord execution,
            System.Collections.Generic.IReadOnlyList<AutomationConditionExecutionResult> conditions,
            System.Collections.Generic.IReadOnlyList<AutomationActionExecutionResult> actions)
        {
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            ExecutionId = execution.ExecutionId;
            RuleId = execution.RuleId;
            TriggerId = execution.TriggerId;
            Status = execution.Status.ToString();
            CorrelationId = execution.CorrelationId;
            StartedAtUtc = execution.StartedAtUtc;
            CompletedAtUtc = execution.CompletedAtUtc;
            ErrorCode = execution.ErrorCode;
            Conditions = (conditions ?? throw new ArgumentNullException(nameof(conditions)))
                .Select(result => new AutomationConditionResultHttpResponse(
                    result.NodeId,
                    result.Truth.ToString()))
                .ToArray();
            Actions = (actions ?? throw new ArgumentNullException(nameof(actions)))
                .Select(result => new AutomationActionResultHttpResponse(
                    result.Ordinal,
                    result.ActionType,
                    result.Status.ToString(),
                    result.ErrorCode,
                    result.StartedAtUtc,
                    result.CompletedAtUtc))
                .ToArray();
        }

        public string ExecutionId { get; }
        public string RuleId { get; }
        public string TriggerId { get; }
        public string Status { get; }
        public string CorrelationId { get; }
        public DateTimeOffset? StartedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public string? ErrorCode { get; }
        public AutomationConditionResultHttpResponse[] Conditions { get; }
        public AutomationActionResultHttpResponse[] Actions { get; }
    }

    public sealed record AutomationConditionResultHttpResponse(
        string NodeId,
        string Truth);

    public sealed record AutomationActionResultHttpResponse(
        int Ordinal,
        string ActionType,
        string Status,
        string? ErrorCode,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc);
}
