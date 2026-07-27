using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LSTY.SevenDPanel.Domain.Automations;

namespace LSTY.SevenDPanel.Application.Automations
{
    public enum AutomationTriggerType
    {
        PlayerJoined,
        PlayerLeft,
        ChatMessage,
        Cron,
        BloodMoonPhaseEntered
    }

    public enum AutomationFieldValueKind
    {
        Text,
        Number,
        StringSet,
        LocalTime,
        Truth
    }

    public enum AutomationTargetKind
    {
        Global,
        TriggerPlayer,
        StablePlayer,
        DiscordTarget
    }

    public enum AutomationActorRole
    {
        Owner,
        Admin,
        Viewer
    }

    public enum AutomationDependencyStatus
    {
        Ready,
        Disabled,
        Unavailable
    }

    public sealed class AuthenticatedActor
    {
        public AuthenticatedActor(string subject, AutomationActorRole role)
        {
            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("An actor subject is required.", nameof(subject));
            if (!Enum.IsDefined(typeof(AutomationActorRole), role))
                throw new ArgumentOutOfRangeException(nameof(role));

            Subject = subject;
            Role = role;
        }

        public string Subject { get; }
        public AutomationActorRole Role { get; }
    }

    public sealed class AutomationTarget
    {
        private AutomationTarget(AutomationTargetKind kind, string? referenceId)
        {
            Kind = kind;
            ReferenceId = referenceId;
        }

        public AutomationTargetKind Kind { get; }
        public string? ReferenceId { get; }

        public static AutomationTarget Global { get; } =
            new(AutomationTargetKind.Global, null);

        public static AutomationTarget TriggerPlayer { get; } =
            new(AutomationTargetKind.TriggerPlayer, null);

        public static AutomationTarget StablePlayer(string crossplatformId) =>
            new(
                AutomationTargetKind.StablePlayer,
                AutomationActionDraft.RequireReference(crossplatformId, nameof(crossplatformId)));

        public static AutomationTarget DiscordTarget(string targetKey) =>
            new(
                AutomationTargetKind.DiscordTarget,
                AutomationActionDraft.RequireReference(targetKey, nameof(targetKey)));
    }

    public abstract class AutomationActionDraft
    {
        protected AutomationActionDraft(string id, AutomationTarget target)
        {
            Id = RequireReference(id, nameof(id));
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public string Id { get; }
        public AutomationTarget Target { get; }
        public abstract string Type { get; }

        internal abstract AutomationAction ToDomainAction();

        protected AutomationAction Domain(
            string? textValue = null,
            long? amount = null,
            TimeSpan? duration = null) =>
            new(
                Id,
                Type,
                Target.Kind.ToString(),
                textValue,
                Target.ReferenceId,
                amount,
                duration);

        internal static string RequireReference(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > AutomationCondition.MaxStringLength)
                throw new ArgumentException("A bounded catalog reference is required.", parameterName);
            return value;
        }

        internal static string RequirePlainText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > AutomationAction.MaxTextLength)
                throw new ArgumentException("Bounded plain text is required.", parameterName);
            if (value.Any(character => char.IsControl(character) &&
                character != '\r' && character != '\n' && character != '\t'))
            {
                throw new ArgumentException("The text contains an unsupported control character.", parameterName);
            }
            return value;
        }

        internal static string RequireCatalogKey(string value, string parameterName)
        {
            value = RequireReference(value, parameterName);
            if (value.Any(character =>
                !(char.IsLetterOrDigit(character) ||
                  character == '-' || character == '_' || character == '.' || character == ':')))
            {
                throw new ArgumentException("A fixed catalog key is required.", parameterName);
            }
            return value;
        }

        protected static AutomationTarget RequireTarget(
            AutomationTarget target,
            params AutomationTargetKind[] allowedKinds)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!allowedKinds.Contains(target.Kind))
                throw new ArgumentException("The action target kind is not allowed.", nameof(target));
            return target;
        }
    }

    public sealed class BroadcastMessageActionDraft : AutomationActionDraft
    {
        public BroadcastMessageActionDraft(string id, string message)
            : base(id, AutomationTarget.Global) =>
            Message = RequirePlainText(message, nameof(message));

        public override string Type => "BroadcastMessage";
        public string Message { get; }
        internal override AutomationAction ToDomainAction() => Domain(Message);
    }

    public sealed class PrivateMessageActionDraft : AutomationActionDraft
    {
        public PrivateMessageActionDraft(string id, AutomationTarget target, string message)
            : base(id, RequireTarget(
                target,
                AutomationTargetKind.TriggerPlayer,
                AutomationTargetKind.StablePlayer)) =>
            Message = RequirePlainText(message, nameof(message));

        public override string Type => "PrivateMessage";
        public string Message { get; }
        internal override AutomationAction ToDomainAction() => Domain(Message);
    }

    public sealed class AnnouncementActionDraft : AutomationActionDraft
    {
        public AnnouncementActionDraft(string id, string message)
            : base(id, AutomationTarget.Global) =>
            Message = RequirePlainText(message, nameof(message));

        public override string Type => "Announcement";
        public string Message { get; }
        internal override AutomationAction ToDomainAction() => Domain(Message);
    }

    public sealed class GrantItemActionDraft : AutomationActionDraft
    {
        public GrantItemActionDraft(
            string id,
            AutomationTarget target,
            string resourceId,
            long amount)
            : base(id, RequireTarget(
                target,
                AutomationTargetKind.TriggerPlayer,
                AutomationTargetKind.StablePlayer))
        {
            ResourceId = RequireCatalogKey(resourceId, nameof(resourceId));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Amount = amount;
        }

        public override string Type => "GrantItem";
        public string ResourceId { get; }
        public long Amount { get; }
        internal override AutomationAction ToDomainAction() => Domain(ResourceId, Amount);
    }

    public sealed class GrantRewardPackageActionDraft : AutomationActionDraft
    {
        public GrantRewardPackageActionDraft(
            string id,
            AutomationTarget target,
            string rewardPackageId)
            : base(id, RequireTarget(
                target,
                AutomationTargetKind.TriggerPlayer,
                AutomationTargetKind.StablePlayer)) =>
            RewardPackageId = RequireCatalogKey(rewardPackageId, nameof(rewardPackageId));

        public override string Type => "GrantRewardPackage";
        public string RewardPackageId { get; }
        internal override AutomationAction ToDomainAction() => Domain(RewardPackageId);
    }

    public sealed class AdjustEconomyActionDraft : AutomationActionDraft
    {
        public AdjustEconomyActionDraft(string id, AutomationTarget target, long amount)
            : base(id, RequireTarget(
                target,
                AutomationTargetKind.TriggerPlayer,
                AutomationTargetKind.StablePlayer))
        {
            if (amount == 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Amount = amount;
        }

        public override string Type => "AdjustEconomy";
        public long Amount { get; }
        internal override AutomationAction ToDomainAction() => Domain(amount: Amount);
    }

    public sealed class KickPlayerActionDraft : AutomationActionDraft
    {
        public KickPlayerActionDraft(string id, AutomationTarget target, string reason)
            : base(id, RequireTarget(
                target,
                AutomationTargetKind.TriggerPlayer,
                AutomationTargetKind.StablePlayer)) =>
            Reason = RequirePlainText(reason, nameof(reason));

        public override string Type => "KickPlayer";
        public string Reason { get; }
        internal override AutomationAction ToDomainAction() => Domain(Reason);
    }

    public sealed class MutePlayerActionDraft : AutomationActionDraft
    {
        public MutePlayerActionDraft(
            string id,
            AutomationTarget target,
            TimeSpan duration,
            string reason)
            : base(id, RequireTarget(
                target,
                AutomationTargetKind.TriggerPlayer,
                AutomationTargetKind.StablePlayer))
        {
            if (duration <= TimeSpan.Zero || duration.Ticks % TimeSpan.TicksPerSecond != 0)
                throw new ArgumentException("A positive whole-second duration is required.", nameof(duration));
            Duration = duration;
            Reason = RequirePlainText(reason, nameof(reason));
        }

        public override string Type => "MutePlayer";
        public TimeSpan Duration { get; }
        public string Reason { get; }
        internal override AutomationAction ToDomainAction() => Domain(Reason, duration: Duration);
    }

    public sealed class RestrictedCommandActionDraft : AutomationActionDraft
    {
        public RestrictedCommandActionDraft(
            string id,
            AutomationTarget target,
            string commandCatalogKey)
            : base(id, RequireTarget(
                target,
                AutomationTargetKind.Global,
                AutomationTargetKind.TriggerPlayer,
                AutomationTargetKind.StablePlayer)) =>
            CommandCatalogKey = RequireCatalogKey(commandCatalogKey, nameof(commandCatalogKey));

        public override string Type => "RestrictedCommand";
        public string CommandCatalogKey { get; }
        internal override AutomationAction ToDomainAction() => Domain(CommandCatalogKey);
    }

    public sealed class DiscordMessageActionDraft : AutomationActionDraft
    {
        public DiscordMessageActionDraft(string id, AutomationTarget target, string message)
            : base(id, RequireTarget(target, AutomationTargetKind.DiscordTarget)) =>
            Message = RequirePlainText(message, nameof(message));

        public override string Type => "DiscordMessage";
        public string Message { get; }
        internal override AutomationAction ToDomainAction() => Domain(Message);
    }

    public sealed class AutomationRuleDraft
    {
        public AutomationRuleDraft(
            string id,
            long expectedVersion,
            string name,
            bool isEnabled,
            AutomationTriggerType triggerType,
            AutomationCondition conditionRoot,
            IEnumerable<AutomationActionDraft> actions,
            TimeSpan cooldownDuration,
            AutomationCooldownScope cooldownScope,
            AutomationConcurrencyPolicy concurrencyPolicy,
            AutomationFailurePolicy failurePolicy)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            ExpectedVersion = expectedVersion;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            IsEnabled = isEnabled;
            TriggerType = triggerType;
            ConditionRoot = conditionRoot ?? throw new ArgumentNullException(nameof(conditionRoot));
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            var copiedActions = actions.ToArray();
            if (copiedActions.Any(action => action == null))
                throw new ArgumentException("Rule actions cannot be null.", nameof(actions));
            Actions = new ReadOnlyCollection<AutomationActionDraft>(copiedActions);
            CooldownDuration = cooldownDuration;
            CooldownScope = cooldownScope;
            ConcurrencyPolicy = concurrencyPolicy;
            FailurePolicy = failurePolicy;
        }

        public string Id { get; }
        public long ExpectedVersion { get; }
        public string Name { get; }
        public bool IsEnabled { get; }
        public AutomationTriggerType TriggerType { get; }
        public AutomationCondition ConditionRoot { get; }
        public IReadOnlyList<AutomationActionDraft> Actions { get; }
        public TimeSpan CooldownDuration { get; }
        public AutomationCooldownScope CooldownScope { get; }
        public AutomationConcurrencyPolicy ConcurrencyPolicy { get; }
        public AutomationFailurePolicy FailurePolicy { get; }

        internal IReadOnlyList<AutomationAction> ToDomainActions() =>
            Actions.Select(action => action.ToDomainAction()).ToArray();
    }

    public sealed record AutomationValidationIssue(string Code, string Path);

    public sealed class AutomationValidationResult
    {
        public AutomationValidationResult(IEnumerable<AutomationValidationIssue> issues)
        {
            if (issues == null) throw new ArgumentNullException(nameof(issues));
            Issues = new ReadOnlyCollection<AutomationValidationIssue>(issues.ToArray());
        }

        public bool IsValid => Issues.Count == 0;
        public IReadOnlyList<AutomationValidationIssue> Issues { get; }

        public static AutomationValidationResult Valid { get; } =
            new(Array.Empty<AutomationValidationIssue>());
    }

    public sealed record AutomationDependencyState(
        AutomationDependencyStatus Status,
        string? ErrorCode)
    {
        public bool IsReady => Status == AutomationDependencyStatus.Ready;

        public static AutomationDependencyState Ready { get; } =
            new(AutomationDependencyStatus.Ready, null);

        public static AutomationDependencyState Disabled(string errorCode) =>
            new(
                AutomationDependencyStatus.Disabled,
                AutomationActionDraft.RequireReference(errorCode, nameof(errorCode)));

        public static AutomationDependencyState Unavailable(string errorCode) =>
            new(
                AutomationDependencyStatus.Unavailable,
                AutomationActionDraft.RequireReference(errorCode, nameof(errorCode)));
    }

    public interface IAutomationDependencyCatalog
    {
        AutomationDependencyState Resolve(AutomationAction action);
    }

    public sealed record AutomationTargetResolution(
        bool IsResolved,
        string? ResolvedId,
        string? ErrorCode)
    {
        public static AutomationTargetResolution Resolved(string resolvedId) =>
            new(
                true,
                AutomationActionDraft.RequireReference(resolvedId, nameof(resolvedId)),
                null);

        public static AutomationTargetResolution Unresolved(string errorCode) =>
            new(
                false,
                null,
                AutomationActionDraft.RequireReference(errorCode, nameof(errorCode)));
    }

    public interface IAutomationTargetResolver
    {
        AutomationTargetResolution Resolve(
            AutomationAction action,
            AutomationTriggerSnapshot snapshot);
    }

    public sealed record AutomationConditionTrace(
        string NodeId,
        string? FieldKey,
        AutomationTruth Truth,
        bool IsValueKnown);

    public sealed record AutomationConditionEvaluation(
        AutomationTruth Truth,
        IReadOnlyList<AutomationConditionTrace> Trace);

    public sealed record AutomationPlannedAction(
        int Ordinal,
        string ActionId,
        string ActionType,
        AutomationDependencyState Dependency,
        AutomationTargetResolution Target,
        bool WouldExecute);

    public sealed record AutomationDryRunResult(
        AutomationValidationResult Validation,
        AutomationConditionEvaluation? Evaluation,
        IReadOnlyList<AutomationPlannedAction> PlannedActions);

    public sealed class AutomationAuthorizationException : InvalidOperationException
    {
        public AutomationAuthorizationException()
            : base("automation_owner_required") => Code = "automation_owner_required";

        public string Code { get; }
    }

    public sealed class AutomationRuleValidationException : InvalidOperationException
    {
        public AutomationRuleValidationException(AutomationValidationResult validation)
            : base("automation_rule_invalid") =>
            Validation = validation ?? throw new ArgumentNullException(nameof(validation));

        public AutomationValidationResult Validation { get; }
    }

    internal static class AutomationAuthorization
    {
        internal static void RequireOwner(AuthenticatedActor actor)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            if (actor.Role != AutomationActorRole.Owner)
                throw new AutomationAuthorizationException();
        }
    }
}
