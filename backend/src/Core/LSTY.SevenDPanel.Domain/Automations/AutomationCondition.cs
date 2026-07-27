using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Domain.Automations
{
    public enum AutomationTruth
    {
        Matched,
        NotMatched,
        Unknown
    }

    public enum AutomationConditionOperator
    {
        Equals,
        NotEquals,
        InSet,
        NumberRange,
        TimeWindow,
        PlayerGroup,
        Permission,
        Cooldown
    }

    public enum AutomationConditionKind
    {
        All,
        Any,
        Not,
        Predicate
    }

    public sealed class AutomationTimeOfDay
    {
        public AutomationTimeOfDay(int hour, int minute)
        {
            if (hour < 0 || hour > 23)
                throw new ArgumentOutOfRangeException(nameof(hour));
            if (minute < 0 || minute > 59)
                throw new ArgumentOutOfRangeException(nameof(minute));

            Hour = hour;
            Minute = minute;
        }

        public int Hour { get; }
        public int Minute { get; }

        internal int MinutesSinceMidnight => (Hour * 60) + Minute;
    }

    public sealed class AutomationLocalTime
    {
        public AutomationLocalTime(string timeZoneId, AutomationTimeOfDay timeOfDay)
        {
            TimeZoneId = AutomationCondition.RequireString(
                timeZoneId,
                nameof(timeZoneId));
            TimeOfDay = timeOfDay ?? throw new ArgumentNullException(nameof(timeOfDay));
        }

        public string TimeZoneId { get; }
        public AutomationTimeOfDay TimeOfDay { get; }
    }

    public sealed class AutomationTimeWindow
    {
        public AutomationTimeWindow(
            string timeZoneId,
            AutomationTimeOfDay startInclusive,
            AutomationTimeOfDay endInclusive)
        {
            TimeZoneId = AutomationCondition.RequireString(
                timeZoneId,
                nameof(timeZoneId));
            StartInclusive = startInclusive ??
                throw new ArgumentNullException(nameof(startInclusive));
            EndInclusive = endInclusive ??
                throw new ArgumentNullException(nameof(endInclusive));
        }

        public string TimeZoneId { get; }
        public AutomationTimeOfDay StartInclusive { get; }
        public AutomationTimeOfDay EndInclusive { get; }

        public bool Contains(AutomationLocalTime localTime)
        {
            if (localTime == null) throw new ArgumentNullException(nameof(localTime));
            if (!string.Equals(TimeZoneId, localTime.TimeZoneId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The local time must use the window time zone.",
                    nameof(localTime));
            }

            var start = StartInclusive.MinutesSinceMidnight;
            var end = EndInclusive.MinutesSinceMidnight;
            var actual = localTime.TimeOfDay.MinutesSinceMidnight;
            return start <= end
                ? actual >= start && actual <= end
                : actual >= start || actual <= end;
        }
    }

    internal enum AutomationConditionValueKind
    {
        Text,
        Number,
        LocalTime,
        Set,
        Truth
    }

    public sealed class AutomationConditionValue
    {
        private AutomationConditionValue(
            AutomationConditionValueKind kind,
            string? textValue,
            long numberValue,
            AutomationLocalTime? localTimeValue,
            IReadOnlyList<string> setValues,
            AutomationTruth truthValue)
        {
            Kind = kind;
            TextValue = textValue;
            NumberValue = numberValue;
            LocalTimeValue = localTimeValue;
            SetValues = setValues;
            TruthValue = truthValue;
        }

        internal AutomationConditionValueKind Kind { get; }
        internal string? TextValue { get; }
        internal long NumberValue { get; }
        internal AutomationLocalTime? LocalTimeValue { get; }
        internal IReadOnlyList<string> SetValues { get; }
        internal AutomationTruth TruthValue { get; }

        public static AutomationConditionValue Text(string value) =>
            new AutomationConditionValue(
                AutomationConditionValueKind.Text,
                AutomationCondition.RequireString(value, nameof(value), allowEmpty: true),
                0,
                null,
                AutomationCondition.EmptyStrings,
                AutomationTruth.Unknown);

        public static AutomationConditionValue Number(long value) =>
            new AutomationConditionValue(
                AutomationConditionValueKind.Number,
                null,
                value,
                null,
                AutomationCondition.EmptyStrings,
                AutomationTruth.Unknown);

        public static AutomationConditionValue LocalTime(AutomationLocalTime value) =>
            new AutomationConditionValue(
                AutomationConditionValueKind.LocalTime,
                null,
                0,
                value ?? throw new ArgumentNullException(nameof(value)),
                AutomationCondition.EmptyStrings,
                AutomationTruth.Unknown);

        public static AutomationConditionValue Set(IEnumerable<string> values) =>
            new AutomationConditionValue(
                AutomationConditionValueKind.Set,
                null,
                0,
                null,
                AutomationCondition.CopyStrings(values, nameof(values), requireAny: false),
                AutomationTruth.Unknown);

        public static AutomationConditionValue Truth(AutomationTruth value)
        {
            AutomationCondition.RequireDefined(value, nameof(value));
            return new AutomationConditionValue(
                AutomationConditionValueKind.Truth,
                null,
                0,
                null,
                AutomationCondition.EmptyStrings,
                value);
        }
    }

    public sealed class AutomationCondition
    {
        public const int MaxDepth = 5;
        public const int MaxNodeCount = 64;
        public const int MaxStringLength = 256;
        public const int MaxSetElementCount = 50;

        private static readonly IReadOnlyList<AutomationCondition> NoChildren =
            new ReadOnlyCollection<AutomationCondition>(new AutomationCondition[0]);

        internal static readonly IReadOnlyList<string> EmptyStrings =
            new ReadOnlyCollection<string>(new string[0]);

        private AutomationCondition(
            string nodeId,
            AutomationConditionKind kind,
            string? fieldKey,
            AutomationConditionOperator? @operator,
            string? scalarValue,
            long? minimumInclusive,
            long? maximumInclusive,
            IReadOnlyList<string> setValues,
            AutomationTimeWindow? window,
            IReadOnlyList<AutomationCondition> children,
            int depth,
            int nodeCount)
        {
            NodeId = nodeId;
            Kind = kind;
            FieldKey = fieldKey;
            Operator = @operator;
            ScalarValue = scalarValue;
            MinimumInclusive = minimumInclusive;
            MaximumInclusive = maximumInclusive;
            SetValues = setValues;
            Window = window;
            Children = children;
            Depth = depth;
            NodeCount = nodeCount;
        }

        public string NodeId { get; }
        public AutomationConditionKind Kind { get; }
        public string? FieldKey { get; }
        public AutomationConditionOperator? Operator { get; }
        public string? ScalarValue { get; }
        public long? MinimumInclusive { get; }
        public long? MaximumInclusive { get; }
        public IReadOnlyList<string> SetValues { get; }
        public AutomationTimeWindow? Window { get; }
        public IReadOnlyList<AutomationCondition> Children { get; }
        public int Depth { get; }
        public int NodeCount { get; }

        public static AutomationCondition All(
            string nodeId,
            params AutomationCondition[] children) =>
            Composite(nodeId, AutomationConditionKind.All, children);

        public static AutomationCondition Any(
            string nodeId,
            params AutomationCondition[] children) =>
            Composite(nodeId, AutomationConditionKind.Any, children);

        public static AutomationCondition Not(
            string nodeId,
            AutomationCondition child) =>
            Composite(
                nodeId,
                AutomationConditionKind.Not,
                new[] { child ?? throw new ArgumentNullException(nameof(child)) });

        public static AutomationCondition TextEquals(
            string nodeId,
            string fieldKey,
            string expected) =>
            TextPredicate(
                nodeId,
                fieldKey,
                AutomationConditionOperator.Equals,
                expected);

        public static AutomationCondition TextNotEquals(
            string nodeId,
            string fieldKey,
            string expected) =>
            TextPredicate(
                nodeId,
                fieldKey,
                AutomationConditionOperator.NotEquals,
                expected);

        public static AutomationCondition InSet(
            string nodeId,
            string fieldKey,
            IEnumerable<string> expectedValues) =>
            Predicate(
                nodeId,
                fieldKey,
                AutomationConditionOperator.InSet,
                null,
                null,
                null,
                CopyStrings(expectedValues, nameof(expectedValues), requireAny: true),
                null);

        public static AutomationCondition NumberRange(
            string nodeId,
            string fieldKey,
            long minimumInclusive,
            long maximumInclusive)
        {
            if (minimumInclusive > maximumInclusive)
            {
                throw new ArgumentException(
                    "The minimum must not exceed the maximum.",
                    nameof(minimumInclusive));
            }

            return Predicate(
                nodeId,
                fieldKey,
                AutomationConditionOperator.NumberRange,
                null,
                minimumInclusive,
                maximumInclusive,
                EmptyStrings,
                null);
        }

        public static AutomationCondition TimeWindow(
            string nodeId,
            string fieldKey,
            AutomationTimeWindow window) =>
            Predicate(
                nodeId,
                fieldKey,
                AutomationConditionOperator.TimeWindow,
                null,
                null,
                null,
                EmptyStrings,
                window ?? throw new ArgumentNullException(nameof(window)));

        public static AutomationCondition PlayerGroup(
            string nodeId,
            string fieldKey,
            string expectedGroup) =>
            TextPredicate(
                nodeId,
                fieldKey,
                AutomationConditionOperator.PlayerGroup,
                expectedGroup);

        public static AutomationCondition Permission(
            string nodeId,
            string fieldKey,
            string expectedPermission) =>
            TextPredicate(
                nodeId,
                fieldKey,
                AutomationConditionOperator.Permission,
                expectedPermission);

        public static AutomationCondition Cooldown(
            string nodeId,
            string fieldKey) =>
            Predicate(
                nodeId,
                fieldKey,
                AutomationConditionOperator.Cooldown,
                null,
                null,
                null,
                EmptyStrings,
                null);

        public AutomationTruth Evaluate(
            Func<string, AutomationConditionValue?> valueResolver)
        {
            if (valueResolver == null)
                throw new ArgumentNullException(nameof(valueResolver));

            switch (Kind)
            {
                case AutomationConditionKind.All:
                    return EvaluateAll(valueResolver);
                case AutomationConditionKind.Any:
                    return EvaluateAny(valueResolver);
                case AutomationConditionKind.Not:
                    return Negate(Children[0].Evaluate(valueResolver));
                case AutomationConditionKind.Predicate:
                    return EvaluatePredicate(valueResolver(FieldKey!));
                default:
                    throw new InvalidOperationException("The condition kind is invalid.");
            }
        }

        internal static string RequireString(
            string value,
            string parameterName,
            bool allowEmpty = false)
        {
            if (value == null) throw new ArgumentNullException(parameterName);
            if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) ||
                value.Length > MaxStringLength)
            {
                throw new ArgumentException(
                    "The value is outside the allowed string bounds.",
                    parameterName);
            }

            return value;
        }

        internal static IReadOnlyList<string> CopyStrings(
            IEnumerable<string> values,
            string parameterName,
            bool requireAny)
        {
            if (values == null) throw new ArgumentNullException(parameterName);
            var copied = values.ToArray();
            if ((requireAny && copied.Length == 0) ||
                copied.Length > MaxSetElementCount)
            {
                throw new ArgumentException(
                    "The set is outside the allowed element bounds.",
                    parameterName);
            }

            for (var index = 0; index < copied.Length; index++)
            {
                copied[index] = RequireString(
                    copied[index],
                    parameterName,
                    allowEmpty: true);
            }

            return new ReadOnlyCollection<string>(copied);
        }

        internal static void RequireDefined<T>(T value, string parameterName)
            where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static AutomationCondition Composite(
            string nodeId,
            AutomationConditionKind kind,
            IEnumerable<AutomationCondition> children)
        {
            nodeId = RequireString(nodeId, nameof(nodeId));
            RequireDefined(kind, nameof(kind));
            if (kind == AutomationConditionKind.Predicate)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (children == null) throw new ArgumentNullException(nameof(children));

            var copied = children.ToArray();
            if (copied.Length == 0 ||
                (kind == AutomationConditionKind.Not && copied.Length != 1))
            {
                throw new ArgumentException(
                    "The boolean condition has an invalid child count.",
                    nameof(children));
            }

            if (copied.Any(child => child == null))
                throw new ArgumentException("Condition children cannot be null.", nameof(children));

            var depth = 1 + copied.Max(child => child.Depth);
            var nodeCount = 1 + copied.Sum(child => child.NodeCount);
            if (depth > MaxDepth)
                throw new ArgumentException("The condition tree is too deep.", nameof(children));
            if (nodeCount > MaxNodeCount)
                throw new ArgumentException("The condition tree has too many nodes.", nameof(children));

            var nodeIds = new HashSet<string>(StringComparer.Ordinal) { nodeId };
            foreach (var child in copied)
            {
                if (!CollectNodeIds(child, nodeIds))
                    throw new ArgumentException("Condition node IDs must be unique.", nameof(children));
            }

            return new AutomationCondition(
                nodeId,
                kind,
                null,
                null,
                null,
                null,
                null,
                EmptyStrings,
                null,
                new ReadOnlyCollection<AutomationCondition>(copied),
                depth,
                nodeCount);
        }

        private static bool CollectNodeIds(
            AutomationCondition condition,
            ISet<string> nodeIds)
        {
            if (!nodeIds.Add(condition.NodeId)) return false;
            foreach (var child in condition.Children)
            {
                if (!CollectNodeIds(child, nodeIds)) return false;
            }
            return true;
        }

        private static AutomationCondition TextPredicate(
            string nodeId,
            string fieldKey,
            AutomationConditionOperator @operator,
            string scalarValue) =>
            Predicate(
                nodeId,
                fieldKey,
                @operator,
                RequireString(scalarValue, nameof(scalarValue), allowEmpty: true),
                null,
                null,
                EmptyStrings,
                null);

        private static AutomationCondition Predicate(
            string nodeId,
            string fieldKey,
            AutomationConditionOperator @operator,
            string? scalarValue,
            long? minimumInclusive,
            long? maximumInclusive,
            IReadOnlyList<string> setValues,
            AutomationTimeWindow? window)
        {
            RequireDefined(@operator, nameof(@operator));
            return new AutomationCondition(
                RequireString(nodeId, nameof(nodeId)),
                AutomationConditionKind.Predicate,
                RequireString(fieldKey, nameof(fieldKey)),
                @operator,
                scalarValue,
                minimumInclusive,
                maximumInclusive,
                setValues,
                window,
                NoChildren,
                1,
                1);
        }

        private AutomationTruth EvaluateAll(
            Func<string, AutomationConditionValue?> valueResolver)
        {
            var sawUnknown = false;
            foreach (var child in Children)
            {
                var truth = child.Evaluate(valueResolver);
                if (truth == AutomationTruth.NotMatched) return AutomationTruth.NotMatched;
                if (truth == AutomationTruth.Unknown) sawUnknown = true;
            }
            return sawUnknown ? AutomationTruth.Unknown : AutomationTruth.Matched;
        }

        private AutomationTruth EvaluateAny(
            Func<string, AutomationConditionValue?> valueResolver)
        {
            var sawUnknown = false;
            foreach (var child in Children)
            {
                var truth = child.Evaluate(valueResolver);
                if (truth == AutomationTruth.Matched) return AutomationTruth.Matched;
                if (truth == AutomationTruth.Unknown) sawUnknown = true;
            }
            return sawUnknown ? AutomationTruth.Unknown : AutomationTruth.NotMatched;
        }

        private AutomationTruth EvaluatePredicate(AutomationConditionValue? actual)
        {
            if (actual == null) return AutomationTruth.Unknown;

            switch (Operator)
            {
                case AutomationConditionOperator.Equals:
                    return MatchText(actual, equals: true);
                case AutomationConditionOperator.NotEquals:
                    return MatchText(actual, equals: false);
                case AutomationConditionOperator.InSet:
                    return MatchSet(actual);
                case AutomationConditionOperator.NumberRange:
                    return MatchNumber(actual);
                case AutomationConditionOperator.TimeWindow:
                    return MatchTime(actual);
                case AutomationConditionOperator.PlayerGroup:
                case AutomationConditionOperator.Permission:
                    return MatchMembership(actual);
                case AutomationConditionOperator.Cooldown:
                    return actual.Kind == AutomationConditionValueKind.Truth
                        ? actual.TruthValue
                        : AutomationTruth.Unknown;
                default:
                    throw new InvalidOperationException("The condition operator is invalid.");
            }
        }

        private AutomationTruth MatchText(AutomationConditionValue actual, bool equals)
        {
            if (actual.Kind != AutomationConditionValueKind.Text)
                return AutomationTruth.Unknown;
            var matched = string.Equals(actual.TextValue, ScalarValue, StringComparison.Ordinal);
            if (!equals) matched = !matched;
            return matched ? AutomationTruth.Matched : AutomationTruth.NotMatched;
        }

        private AutomationTruth MatchSet(AutomationConditionValue actual)
        {
            if (actual.Kind != AutomationConditionValueKind.Text)
                return AutomationTruth.Unknown;
            return SetValues.Contains(actual.TextValue!, StringComparer.Ordinal)
                ? AutomationTruth.Matched
                : AutomationTruth.NotMatched;
        }

        private AutomationTruth MatchNumber(AutomationConditionValue actual)
        {
            if (actual.Kind != AutomationConditionValueKind.Number)
                return AutomationTruth.Unknown;
            return actual.NumberValue >= MinimumInclusive!.Value &&
                actual.NumberValue <= MaximumInclusive!.Value
                ? AutomationTruth.Matched
                : AutomationTruth.NotMatched;
        }

        private AutomationTruth MatchTime(AutomationConditionValue actual)
        {
            if (actual.Kind != AutomationConditionValueKind.LocalTime)
                return AutomationTruth.Unknown;
            var localTime = actual.LocalTimeValue!;
            if (!string.Equals(Window!.TimeZoneId, localTime.TimeZoneId, StringComparison.Ordinal))
                return AutomationTruth.Unknown;
            return Window.Contains(localTime)
                ? AutomationTruth.Matched
                : AutomationTruth.NotMatched;
        }

        private AutomationTruth MatchMembership(AutomationConditionValue actual)
        {
            if (actual.Kind != AutomationConditionValueKind.Set)
                return AutomationTruth.Unknown;
            return actual.SetValues.Contains(ScalarValue!, StringComparer.Ordinal)
                ? AutomationTruth.Matched
                : AutomationTruth.NotMatched;
        }

        private static AutomationTruth Negate(AutomationTruth truth)
        {
            switch (truth)
            {
                case AutomationTruth.Matched:
                    return AutomationTruth.NotMatched;
                case AutomationTruth.NotMatched:
                    return AutomationTruth.Matched;
                case AutomationTruth.Unknown:
                    return AutomationTruth.Unknown;
                default:
                    throw new InvalidOperationException("The condition truth is invalid.");
            }
        }
    }
}
