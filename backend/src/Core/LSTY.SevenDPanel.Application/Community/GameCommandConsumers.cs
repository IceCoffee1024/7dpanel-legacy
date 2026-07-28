using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Community
{
    public enum CommunityGameCommandId
    {
        Balance,
        Pay,
        MoneyTop,
        Daily,
        Shop,
        Buy,
        Redeem,
        Homes,
        SetHome,
        DeleteHome,
        Home,
        Cities,
        City,
        TeleportAsk,
        TeleportAccept,
        TeleportReject,
        Back,
        VoteKick,
        VoteRestart
    }

    public sealed class CommunityGameCommandDefinition
    {
        private readonly Func<IReadOnlyList<string>, bool> validate;

        internal CommunityGameCommandDefinition(
            CommunityGameCommandId id,
            string name,
            IReadOnlyList<string> aliases,
            Func<IReadOnlyList<string>, bool> validate)
        {
            Id = id;
            Name = name;
            Aliases = aliases;
            this.validate = validate;
        }

        public CommunityGameCommandId Id { get; }
        public string Name { get; }
        public IReadOnlyList<string> Aliases { get; }

        internal bool Accepts(IReadOnlyList<string> arguments) => validate(arguments);
    }

    public static class CommunityGameCommandDirectory
    {
        private static readonly IReadOnlyList<CommunityGameCommandDefinition> FixedDefinitions =
            new[]
            {
                Define(CommunityGameCommandId.Balance, "bal", NoArguments, "balance", "money"),
                Define(CommunityGameCommandId.Pay, "pay", PayArguments, "transfer", "send"),
                Define(CommunityGameCommandId.MoneyTop, "moneytop", OptionalPositiveInteger, "baltop", "ecotop"),
                Define(CommunityGameCommandId.Daily, "daily", NoArguments, "claim"),
                Define(CommunityGameCommandId.Shop, "shop", OptionalPositiveInteger),
                Define(CommunityGameCommandId.Buy, "buy", BuyArguments),
                Define(CommunityGameCommandId.Redeem, "redeem", OneSafeToken),
                Define(CommunityGameCommandId.Homes, "homes", NoArguments),
                Define(CommunityGameCommandId.SetHome, "sethome", OptionalSafeToken),
                Define(CommunityGameCommandId.DeleteHome, "delhome", OptionalSafeToken),
                Define(CommunityGameCommandId.Home, "home", OptionalSafeToken),
                Define(CommunityGameCommandId.Cities, "cities", NoArguments),
                Define(CommunityGameCommandId.City, "city", OneSafeToken),
                Define(CommunityGameCommandId.TeleportAsk, "tpa", OneSafeToken),
                Define(CommunityGameCommandId.TeleportAccept, "tpaccept", NoArguments),
                Define(CommunityGameCommandId.TeleportReject, "tpreject", NoArguments),
                Define(CommunityGameCommandId.Back, "back", NoArguments),
                Define(CommunityGameCommandId.VoteKick, "votekick", VoteKickArguments),
                Define(CommunityGameCommandId.VoteRestart, "voterestart", VoteRestartArguments)
            };

        private static readonly IReadOnlyDictionary<string, CommunityGameCommandDefinition> ByName =
            BuildLookup(FixedDefinitions);

        public static IReadOnlyList<CommunityGameCommandDefinition> Definitions => FixedDefinitions;

        public static CommunityGameCommandDefinition? Find(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName)) return null;
            return ByName.TryGetValue(commandName.Trim(), out var definition) ? definition : null;
        }

        private static CommunityGameCommandDefinition Define(
            CommunityGameCommandId id,
            string name,
            Func<IReadOnlyList<string>, bool> validate,
            params string[] aliases) =>
            new CommunityGameCommandDefinition(id, name, aliases, validate);

        private static IReadOnlyDictionary<string, CommunityGameCommandDefinition> BuildLookup(
            IEnumerable<CommunityGameCommandDefinition> definitions)
        {
            var result = new Dictionary<string, CommunityGameCommandDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in definitions)
            {
                result.Add(definition.Name, definition);
                foreach (var alias in definition.Aliases) result.Add(alias, definition);
            }

            return result;
        }

        private static bool NoArguments(IReadOnlyList<string> arguments) => arguments.Count == 0;

        private static bool OneSafeToken(IReadOnlyList<string> arguments) =>
            arguments.Count == 1 && IsSafeToken(arguments[0]);

        private static bool OptionalSafeToken(IReadOnlyList<string> arguments) =>
            arguments.Count == 0 || OneSafeToken(arguments);

        private static bool OptionalPositiveInteger(IReadOnlyList<string> arguments) =>
            arguments.Count == 0 ||
            (arguments.Count == 1 && IsPositiveInteger(arguments[0]));

        private static bool PayArguments(IReadOnlyList<string> arguments) =>
            arguments.Count == 2 && IsSafeToken(arguments[0]) && IsPositiveLong(arguments[1]);

        private static bool BuyArguments(IReadOnlyList<string> arguments) =>
            (arguments.Count == 1 || arguments.Count == 2) &&
            IsSafeToken(arguments[0]) &&
            (arguments.Count == 1 || IsPositiveInteger(arguments[1]));

        private static bool VoteKickArguments(IReadOnlyList<string> arguments) =>
            arguments.Count == 1 && (IsVoteChoice(arguments[0]) || IsSafeToken(arguments[0]));

        private static bool VoteRestartArguments(IReadOnlyList<string> arguments) =>
            arguments.Count == 0 || (arguments.Count == 1 && IsVoteChoice(arguments[0]));

        private static bool IsVoteChoice(string value) =>
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "n", StringComparison.OrdinalIgnoreCase);

        private static bool IsPositiveInteger(string value) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

        private static bool IsPositiveLong(string value) =>
            long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

        private static bool IsSafeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character) ||
                    character == '-' || character == '_' || character == '.' || character == ':' || character == '@')
                {
                    continue;
                }

                return false;
            }

            return true;
        }
    }

    public sealed class CommunityGameCommandContext
    {
        public CommunityGameCommandContext(
            string crossplatformId,
            string displayName,
            IEnumerable<string> arguments)
        {
            CrossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            DisplayName = RequireText(displayName, nameof(displayName));
            Arguments = (arguments ?? throw new ArgumentNullException(nameof(arguments)))
                .Select(argument => argument ?? string.Empty)
                .ToArray();
        }

        public string CrossplatformId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<string> Arguments { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }
    }

    public enum CommunityCommandConsumerStatus
    {
        Succeeded,
        Rejected,
        PermissionDenied,
        Failed
    }

    public sealed class CommunityCommandConsumerResult
    {
        private CommunityCommandConsumerResult(
            CommunityCommandConsumerStatus status,
            string? codeSuffix,
            IReadOnlyList<string> messages)
        {
            Status = status;
            CodeSuffix = codeSuffix;
            Messages = messages;
        }

        public CommunityCommandConsumerStatus Status { get; }
        public string? CodeSuffix { get; }
        public IReadOnlyList<string> Messages { get; }

        public static CommunityCommandConsumerResult Succeeded(params string[] messages) =>
            new CommunityCommandConsumerResult(
                CommunityCommandConsumerStatus.Succeeded,
                null,
                messages ?? Array.Empty<string>());

        public static CommunityCommandConsumerResult Rejected(string codeSuffix, params string[] messages) =>
            new CommunityCommandConsumerResult(
                CommunityCommandConsumerStatus.Rejected,
                RequireCodeSuffix(codeSuffix),
                messages ?? Array.Empty<string>());

        public static CommunityCommandConsumerResult PermissionDenied() =>
            new CommunityCommandConsumerResult(
                CommunityCommandConsumerStatus.PermissionDenied,
                null,
                Array.Empty<string>());

        public static CommunityCommandConsumerResult Failed() =>
            new CommunityCommandConsumerResult(
                CommunityCommandConsumerStatus.Failed,
                null,
                Array.Empty<string>());

        private static string RequireCodeSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Any(character => !(char.IsLower(character) || char.IsDigit(character) || character == '_')))
            {
                throw new ArgumentException("A stable lowercase result suffix is required.", nameof(value));
            }

            return value;
        }
    }

    public interface ICommunityGameCommandConsumer
    {
        CommunityGameCommandId Command { get; }
        bool IsEnabled { get; }
        CommunityCommandConsumerResult Execute(CommunityGameCommandContext context);
    }

    public sealed class CommunityGameCommandResult
    {
        public CommunityGameCommandResult(
            bool isHandled,
            string code,
            IReadOnlyList<string> messages)
        {
            IsHandled = isHandled;
            Code = code;
            Messages = messages;
        }

        public bool IsHandled { get; }
        public string Code { get; }
        public IReadOnlyList<string> Messages { get; }
    }

    public sealed class CommunityGameCommandRouter
    {
        private readonly IReadOnlyDictionary<CommunityGameCommandId, ICommunityGameCommandConsumer> consumers;

        public CommunityGameCommandRouter(IEnumerable<ICommunityGameCommandConsumer> consumers)
        {
            if (consumers == null) throw new ArgumentNullException(nameof(consumers));
            var byCommand = new Dictionary<CommunityGameCommandId, ICommunityGameCommandConsumer>();
            foreach (var consumer in consumers)
            {
                if (consumer == null) throw new ArgumentException("Consumers cannot contain null.", nameof(consumers));
                if (byCommand.ContainsKey(consumer.Command))
                    throw new ArgumentException("A command can have only one consumer.", nameof(consumers));
                byCommand.Add(consumer.Command, consumer);
            }

            this.consumers = byCommand;
        }

        public CommunityGameCommandResult Route(
            string commandName,
            CommunityGameCommandContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var definition = CommunityGameCommandDirectory.Find(commandName);
            if (definition == null)
                return Result(true, "community.command.unknown");
            return Route(definition, context);
        }

        internal CommunityGameCommandResult Route(
            CommunityGameCommandDefinition definition,
            CommunityGameCommandContext context)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!definition.Accepts(context.Arguments))
                return Result(true, Code(definition, "invalid_arguments"));
            if (!consumers.TryGetValue(definition.Id, out var consumer) || !consumer.IsEnabled)
                return Result(true, Code(definition, "unavailable"));

            CommunityCommandConsumerResult consumerResult;
            try
            {
                consumerResult = consumer.Execute(context) ?? CommunityCommandConsumerResult.Failed();
            }
            catch
            {
                consumerResult = CommunityCommandConsumerResult.Failed();
            }

            switch (consumerResult.Status)
            {
                case CommunityCommandConsumerStatus.Succeeded:
                    return Result(true, Code(definition, "succeeded"), consumerResult.Messages);
                case CommunityCommandConsumerStatus.Rejected:
                    return Result(true, Code(definition, consumerResult.CodeSuffix!), consumerResult.Messages);
                case CommunityCommandConsumerStatus.PermissionDenied:
                    return Result(true, Code(definition, "permission_denied"));
                default:
                    return Result(true, Code(definition, "failed"));
            }
        }

        private static string Code(CommunityGameCommandDefinition definition, string suffix) =>
            "community.command." + definition.Name + "." + suffix;

        private static CommunityGameCommandResult Result(
            bool isHandled,
            string code,
            IReadOnlyList<string>? messages = null) =>
            new CommunityGameCommandResult(
                isHandled,
                code,
                messages ?? Array.Empty<string>());
    }

    public sealed class VoteCommandSnapshot
    {
        public VoteCommandSnapshot(
            string? targetCrossplatformId,
            IReadOnlyList<VoteEligiblePlayer> eligiblePlayers)
        {
            TargetCrossplatformId = targetCrossplatformId;
            EligiblePlayers = eligiblePlayers ?? throw new ArgumentNullException(nameof(eligiblePlayers));
        }

        public string? TargetCrossplatformId { get; }
        public IReadOnlyList<VoteEligiblePlayer> EligiblePlayers { get; }
    }

    public interface ICommunityVoteCommandSnapshotProvider
    {
        VoteCommandSnapshot Capture(
            VoteKind kind,
            string initiatorCrossplatformId,
            string? targetSelector);
    }

    public sealed class VoteGameCommandConsumer : ICommunityGameCommandConsumer
    {
        private readonly VoteKind kind;
        private readonly StartVoteUseCase startVote;
        private readonly CastVoteUseCase castVote;
        private readonly ICommunityVoteCommandSnapshotProvider snapshots;
        private readonly Func<bool> isEnabled;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly Func<string> roundIdFactory;

        public VoteGameCommandConsumer(
            VoteKind kind,
            StartVoteUseCase startVote,
            CastVoteUseCase castVote,
            ICommunityVoteCommandSnapshotProvider snapshots,
            Func<bool> isEnabled,
            Func<DateTimeOffset> utcClock,
            Func<string>? roundIdFactory = null)
        {
            if (!Enum.IsDefined(typeof(VoteKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            this.kind = kind;
            this.startVote = startVote ?? throw new ArgumentNullException(nameof(startVote));
            this.castVote = castVote ?? throw new ArgumentNullException(nameof(castVote));
            this.snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            this.isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            this.roundIdFactory = roundIdFactory ?? (() => Guid.NewGuid().ToString("D"));
            Command = kind == VoteKind.Kick
                ? CommunityGameCommandId.VoteKick
                : CommunityGameCommandId.VoteRestart;
        }

        public CommunityGameCommandId Command { get; }
        public bool IsEnabled => isEnabled();

        public CommunityCommandConsumerResult Execute(CommunityGameCommandContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var now = utcClock();
            if (now.Offset != TimeSpan.Zero) throw new InvalidOperationException("vote_command_clock_not_utc");
            if (context.Arguments.Count == 1 && TryChoice(context.Arguments[0], out var choice))
            {
                var cast = castVote.ExecuteActive(kind, context.CrossplatformId, choice, now);
                return cast.Status == VoteCastStatus.Accepted ||
                    cast.Status == VoteCastStatus.Replayed ||
                    cast.Status == VoteCastStatus.Changed
                    ? CommunityCommandConsumerResult.Succeeded()
                    : CommunityCommandConsumerResult.Rejected(ToCode(cast.Status));
            }

            var targetSelector = kind == VoteKind.Kick ? context.Arguments[0] : null;
            var snapshot = snapshots.Capture(kind, context.CrossplatformId, targetSelector);
            var roundId = roundIdFactory();
            var started = startVote.Execute(new StartVoteRequest(
                roundId,
                kind,
                context.CrossplatformId,
                snapshot.TargetCrossplatformId,
                snapshot.EligiblePlayers,
                "game-command:" + roundId,
                "game-command:" + roundId,
                now));
            return started.Status == VoteStartStatus.Started || started.Status == VoteStartStatus.Replayed
                ? CommunityCommandConsumerResult.Succeeded()
                : CommunityCommandConsumerResult.Rejected(ToCode(started.Status));
        }

        private static bool TryChoice(string value, out VoteChoice choice)
        {
            if (string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "y", StringComparison.OrdinalIgnoreCase))
            {
                choice = VoteChoice.Yes;
                return true;
            }
            if (string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "n", StringComparison.OrdinalIgnoreCase))
            {
                choice = VoteChoice.No;
                return true;
            }

            choice = default;
            return false;
        }

        private static string ToCode(VoteStartStatus status)
        {
            switch (status)
            {
                case VoteStartStatus.Disabled: return "disabled";
                case VoteStartStatus.InvalidTarget: return "invalid_target";
                case VoteStartStatus.InitiatorIneligible: return "initiator_ineligible";
                case VoteStartStatus.TargetIneligible: return "target_ineligible";
                case VoteStartStatus.InsufficientEligiblePlayers: return "insufficient_eligible_players";
                case VoteStartStatus.ScopeBusy: return "scope_busy";
                case VoteStartStatus.InitiatorCooldown: return "initiator_cooldown";
                case VoteStartStatus.TargetCooldown: return "target_cooldown";
                case VoteStartStatus.GlobalCooldown: return "global_cooldown";
                default: return "failed";
            }
        }

        private static string ToCode(VoteCastStatus status)
        {
            switch (status)
            {
                case VoteCastStatus.NoOpenRound: return "no_open_vote";
                case VoteCastStatus.NotEligible: return "not_eligible";
                case VoteCastStatus.RoundClosed: return "vote_closed";
                case VoteCastStatus.VotingExpired: return "vote_expired";
                case VoteCastStatus.ChangeNotAllowed: return "change_not_allowed";
                default: return "failed";
            }
        }
    }
}
