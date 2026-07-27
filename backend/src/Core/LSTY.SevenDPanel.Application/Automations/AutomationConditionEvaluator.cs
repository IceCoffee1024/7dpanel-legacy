using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using LSTY.SevenDPanel.Domain.Automations;

namespace LSTY.SevenDPanel.Application.Automations
{
    public sealed class AutomationConditionEvaluator
    {
        private readonly TimeZoneInfo serverTimeZone;

        public AutomationConditionEvaluator(TimeZoneInfo serverTimeZone) =>
            this.serverTimeZone = serverTimeZone ??
                throw new ArgumentNullException(nameof(serverTimeZone));

        public AutomationConditionEvaluation Evaluate(
            AutomationCondition condition,
            AutomationTriggerType triggerType,
            AutomationTriggerSnapshot snapshot)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (!Enum.IsDefined(typeof(AutomationTriggerType), triggerType))
                throw new ArgumentOutOfRangeException(nameof(triggerType));

            var trace = new List<AutomationConditionTrace>();
            var truth = EvaluateNode(condition, triggerType, snapshot, trace);
            return new AutomationConditionEvaluation(
                truth,
                new ReadOnlyCollection<AutomationConditionTrace>(trace));
        }

        private AutomationTruth EvaluateNode(
            AutomationCondition condition,
            AutomationTriggerType triggerType,
            AutomationTriggerSnapshot snapshot,
            ICollection<AutomationConditionTrace> trace)
        {
            if (condition.Kind == AutomationConditionKind.Predicate)
            {
                var value = ResolveValue(triggerType, condition.FieldKey!, snapshot);
                var truth = condition.Evaluate(_ => value);
                trace.Add(new AutomationConditionTrace(
                    condition.NodeId,
                    condition.FieldKey,
                    truth,
                    value != null));
                return truth;
            }

            var childTruths = condition.Children
                .Select(child => EvaluateNode(child, triggerType, snapshot, trace))
                .ToArray();
            AutomationTruth result;
            switch (condition.Kind)
            {
                case AutomationConditionKind.All:
                    result = EvaluateAll(childTruths);
                    break;
                case AutomationConditionKind.Any:
                    result = EvaluateAny(childTruths);
                    break;
                case AutomationConditionKind.Not:
                    result = Negate(childTruths[0]);
                    break;
                default:
                    throw new InvalidOperationException("automation_condition_kind_invalid");
            }

            trace.Add(new AutomationConditionTrace(
                condition.NodeId,
                null,
                result,
                childTruths.All(value => value != AutomationTruth.Unknown)));
            return result;
        }

        private AutomationConditionValue? ResolveValue(
            AutomationTriggerType triggerType,
            string fieldKey,
            AutomationTriggerSnapshot snapshot)
        {
            if (string.Equals(fieldKey, AutomationFieldKeys.OccurredLocalTime, StringComparison.Ordinal))
                return LocalTime(snapshot.OccurredAtUtc);

            if (IsPlayerTrigger(triggerType))
            {
                if (string.Equals(
                    fieldKey,
                    AutomationFieldKeys.ActorCrossplatformId,
                    StringComparison.Ordinal))
                {
                    return snapshot.ActorCrossplatformId == null
                        ? null
                        : AutomationConditionValue.Text(snapshot.ActorCrossplatformId);
                }
                if (string.Equals(fieldKey, AutomationFieldKeys.ActorEntityId, StringComparison.Ordinal))
                {
                    return snapshot.ActorEntityId.HasValue
                        ? AutomationConditionValue.Number(snapshot.ActorEntityId.Value)
                        : null;
                }
                if (string.Equals(fieldKey, AutomationFieldKeys.ActorGroup, StringComparison.Ordinal))
                {
                    return snapshot.ActorGroup == null
                        ? null
                        : AutomationConditionValue.Set(new[] { snapshot.ActorGroup });
                }
                if (string.Equals(fieldKey, AutomationFieldKeys.ActorPermission, StringComparison.Ordinal))
                {
                    return snapshot.PermissionLevel.HasValue
                        ? AutomationConditionValue.Set(new[]
                        {
                            snapshot.PermissionLevel.Value.ToString(CultureInfo.InvariantCulture)
                        })
                        : null;
                }
                if (string.Equals(
                    fieldKey,
                    AutomationFieldKeys.ActorPermissionLevel,
                    StringComparison.Ordinal))
                {
                    return snapshot.PermissionLevel.HasValue
                        ? AutomationConditionValue.Number(snapshot.PermissionLevel.Value)
                        : null;
                }
            }

            if (triggerType == AutomationTriggerType.ChatMessage &&
                string.Equals(fieldKey, AutomationFieldKeys.ChatText, StringComparison.Ordinal))
            {
                return snapshot.ChatText == null
                    ? null
                    : AutomationConditionValue.Text(snapshot.ChatText);
            }
            if (triggerType == AutomationTriggerType.Cron &&
                string.Equals(fieldKey, AutomationFieldKeys.ScheduledLocalTime, StringComparison.Ordinal))
            {
                return snapshot.ScheduledForUtc.HasValue
                    ? LocalTime(snapshot.ScheduledForUtc.Value)
                    : null;
            }
            if (triggerType == AutomationTriggerType.BloodMoonPhaseEntered &&
                string.Equals(fieldKey, AutomationFieldKeys.BloodMoonPhase, StringComparison.Ordinal))
            {
                return snapshot.BloodMoonPhase == null
                    ? null
                    : AutomationConditionValue.Text(snapshot.BloodMoonPhase);
            }
            return null;
        }

        private AutomationConditionValue LocalTime(DateTimeOffset value)
        {
            var local = TimeZoneInfo.ConvertTime(value, serverTimeZone);
            return AutomationConditionValue.LocalTime(new AutomationLocalTime(
                serverTimeZone.Id,
                new AutomationTimeOfDay(local.Hour, local.Minute)));
        }

        private static bool IsPlayerTrigger(AutomationTriggerType type) =>
            type == AutomationTriggerType.PlayerJoined ||
            type == AutomationTriggerType.PlayerLeft ||
            type == AutomationTriggerType.ChatMessage;

        private static AutomationTruth EvaluateAll(IEnumerable<AutomationTruth> values)
        {
            var sawUnknown = false;
            foreach (var value in values)
            {
                if (value == AutomationTruth.NotMatched) return AutomationTruth.NotMatched;
                if (value == AutomationTruth.Unknown) sawUnknown = true;
            }
            return sawUnknown ? AutomationTruth.Unknown : AutomationTruth.Matched;
        }

        private static AutomationTruth EvaluateAny(IEnumerable<AutomationTruth> values)
        {
            var sawUnknown = false;
            foreach (var value in values)
            {
                if (value == AutomationTruth.Matched) return AutomationTruth.Matched;
                if (value == AutomationTruth.Unknown) sawUnknown = true;
            }
            return sawUnknown ? AutomationTruth.Unknown : AutomationTruth.NotMatched;
        }

        private static AutomationTruth Negate(AutomationTruth value)
        {
            switch (value)
            {
                case AutomationTruth.Matched:
                    return AutomationTruth.NotMatched;
                case AutomationTruth.NotMatched:
                    return AutomationTruth.Matched;
                case AutomationTruth.Unknown:
                    return AutomationTruth.Unknown;
                default:
                    throw new InvalidOperationException("automation_condition_truth_invalid");
            }
        }
    }
}
