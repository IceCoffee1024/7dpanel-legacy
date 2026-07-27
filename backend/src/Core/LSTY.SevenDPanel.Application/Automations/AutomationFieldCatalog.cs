using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LSTY.SevenDPanel.Domain.Automations;

namespace LSTY.SevenDPanel.Application.Automations
{
    public static class AutomationFieldKeys
    {
        public const string OccurredLocalTime = "trigger.occurredLocalTime";
        public const string ActorCrossplatformId = "actor.crossplatformId";
        public const string ActorEntityId = "actor.entityId";
        public const string ActorGroup = "actor.group";
        public const string ActorPermission = "actor.permission";
        public const string ActorPermissionLevel = "actor.permissionLevel";
        public const string ChatText = "chat.text";
        public const string ScheduledLocalTime = "cron.scheduledLocalTime";
        public const string BloodMoonPhase = "bloodMoon.phase";
    }

    public sealed class AutomationFieldDefinition
    {
        public AutomationFieldDefinition(
            string key,
            AutomationFieldValueKind valueKind,
            IEnumerable<AutomationConditionOperator> allowedOperators)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A field key is required.", nameof(key));
            if (!Enum.IsDefined(typeof(AutomationFieldValueKind), valueKind))
                throw new ArgumentOutOfRangeException(nameof(valueKind));
            if (allowedOperators == null)
                throw new ArgumentNullException(nameof(allowedOperators));
            var copied = allowedOperators.Distinct().ToArray();
            if (copied.Length == 0 || copied.Any(value =>
                !Enum.IsDefined(typeof(AutomationConditionOperator), value)))
            {
                throw new ArgumentException("Approved field operators are required.", nameof(allowedOperators));
            }

            Key = key;
            ValueKind = valueKind;
            AllowedOperators = new ReadOnlyCollection<AutomationConditionOperator>(copied);
        }

        public string Key { get; }
        public AutomationFieldValueKind ValueKind { get; }
        public IReadOnlyList<AutomationConditionOperator> AllowedOperators { get; }
    }

    public sealed class AutomationFieldCatalog
    {
        private static readonly AutomationConditionOperator[] TextOperators =
        {
            AutomationConditionOperator.Equals,
            AutomationConditionOperator.NotEquals,
            AutomationConditionOperator.InSet
        };

        private readonly IReadOnlyDictionary<AutomationTriggerType,
            IReadOnlyDictionary<string, AutomationFieldDefinition>> fields;

        public AutomationFieldCatalog()
        {
            var occurred = Field(
                AutomationFieldKeys.OccurredLocalTime,
                AutomationFieldValueKind.LocalTime,
                AutomationConditionOperator.TimeWindow);
            var actor = new[]
            {
                occurred,
                Field(
                    AutomationFieldKeys.ActorCrossplatformId,
                    AutomationFieldValueKind.Text,
                    TextOperators),
                Field(
                    AutomationFieldKeys.ActorEntityId,
                    AutomationFieldValueKind.Number,
                    AutomationConditionOperator.NumberRange),
                Field(
                    AutomationFieldKeys.ActorGroup,
                    AutomationFieldValueKind.StringSet,
                    AutomationConditionOperator.PlayerGroup),
                Field(
                    AutomationFieldKeys.ActorPermission,
                    AutomationFieldValueKind.StringSet,
                    AutomationConditionOperator.Permission),
                Field(
                    AutomationFieldKeys.ActorPermissionLevel,
                    AutomationFieldValueKind.Number,
                    AutomationConditionOperator.NumberRange)
            };
            var chat = actor.Concat(new[]
            {
                Field(
                    AutomationFieldKeys.ChatText,
                    AutomationFieldValueKind.Text,
                    TextOperators)
            });
            var cron = new[]
            {
                occurred,
                Field(
                    AutomationFieldKeys.ScheduledLocalTime,
                    AutomationFieldValueKind.LocalTime,
                    AutomationConditionOperator.TimeWindow)
            };
            var bloodMoon = new[]
            {
                occurred,
                Field(
                    AutomationFieldKeys.BloodMoonPhase,
                    AutomationFieldValueKind.Text,
                    TextOperators)
            };

            fields = new ReadOnlyDictionary<AutomationTriggerType,
                IReadOnlyDictionary<string, AutomationFieldDefinition>>(
                new Dictionary<AutomationTriggerType,
                    IReadOnlyDictionary<string, AutomationFieldDefinition>>
                {
                    [AutomationTriggerType.PlayerJoined] = Index(actor),
                    [AutomationTriggerType.PlayerLeft] = Index(actor),
                    [AutomationTriggerType.ChatMessage] = Index(chat),
                    [AutomationTriggerType.Cron] = Index(cron),
                    [AutomationTriggerType.BloodMoonPhaseEntered] = Index(bloodMoon)
                });
            TriggerTypes = new ReadOnlyCollection<AutomationTriggerType>(
                fields.Keys.OrderBy(value => value).ToArray());
        }

        public IReadOnlyList<AutomationTriggerType> TriggerTypes { get; }

        public IReadOnlyList<AutomationFieldDefinition> GetFields(
            AutomationTriggerType triggerType)
        {
            if (!fields.TryGetValue(triggerType, out var definitions))
                throw new ArgumentOutOfRangeException(nameof(triggerType));
            return new ReadOnlyCollection<AutomationFieldDefinition>(
                definitions.Values.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray());
        }

        public AutomationFieldDefinition? Find(
            AutomationTriggerType triggerType,
            string fieldKey)
        {
            if (fieldKey == null) throw new ArgumentNullException(nameof(fieldKey));
            return fields.TryGetValue(triggerType, out var definitions) &&
                definitions.TryGetValue(fieldKey, out var definition)
                ? definition
                : null;
        }

        private static AutomationFieldDefinition Field(
            string key,
            AutomationFieldValueKind kind,
            params AutomationConditionOperator[] operators) =>
            new(key, kind, operators);

        private static IReadOnlyDictionary<string, AutomationFieldDefinition> Index(
            IEnumerable<AutomationFieldDefinition> definitions) =>
            new ReadOnlyDictionary<string, AutomationFieldDefinition>(
                definitions.ToDictionary(value => value.Key, StringComparer.Ordinal));
    }
}
