using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Community;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class GameChatCommandHttpResponse
    {
        public GameChatCommandHttpResponse(GameChatCommandDescriptor command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            CommandId = command.CommandId;
            Name = command.Name;
            Aliases = command.Aliases.ToArray();
            IsEnabled = command.IsEnabled;
        }

        public string CommandId { get; }
        public string Name { get; }
        public IReadOnlyList<string> Aliases { get; }
        public bool IsEnabled { get; }
    }

    public sealed class CommunityGameCommandConfigurationHttpResponse
    {
        public CommunityGameCommandConfigurationHttpResponse(
            CommunityGameCommandConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            Commands = configuration.Commands
                .Select(command => new CommunityGameCommandSettingHttpModel(command))
                .ToArray();
            UpdatedAtUtc = configuration.UpdatedAtUtc.UtcDateTime;
            RowVersion = configuration.RowVersion;
        }

        public IReadOnlyList<CommunityGameCommandSettingHttpModel> Commands { get; }
        public DateTime UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class CommunityGameCommandConfigurationUpsertHttpRequest
    {
        public CommunityGameCommandSettingHttpModel[]? Commands { get; set; }
        public long ExpectedRowVersion { get; set; }

        internal CommunityGameCommandConfiguration ToDomain(DateTimeOffset updatedAtUtc) =>
            new CommunityGameCommandConfiguration(
                (Commands ?? throw new ArgumentException("Commands are required."))
                    .Select(command => command.ToDomain()),
                updatedAtUtc,
                ExpectedRowVersion);
    }

    public sealed class CommunityGameCommandSettingHttpModel
    {
        public CommunityGameCommandSettingHttpModel()
        {
        }

        internal CommunityGameCommandSettingHttpModel(CommunityGameCommandSetting setting)
        {
            CommandId = setting.CommandId.ToString();
            Name = setting.Name;
            Aliases = setting.Aliases.ToArray();
        }

        public string? CommandId { get; set; }
        public string? Name { get; set; }
        public string[]? Aliases { get; set; }

        internal CommunityGameCommandSetting ToDomain()
        {
            if (!Enum.TryParse(CommandId, true, out CommunityGameCommandId commandId) ||
                !Enum.IsDefined(typeof(CommunityGameCommandId), commandId))
            {
                throw new ArgumentException("The Community command ID is invalid.");
            }
            return new CommunityGameCommandSetting(
                commandId,
                Name!,
                Aliases ?? throw new ArgumentException("Aliases are required."));
        }
    }

    public sealed class CommunityWorldPositionHttpModel
    {
        public CommunityWorldPositionHttpModel()
        {
        }

        public CommunityWorldPositionHttpModel(WorldPosition position)
        {
            if (position == null) throw new ArgumentNullException(nameof(position));
            WorldId = position.WorldId;
            X = position.X;
            Y = position.Y;
            Z = position.Z;
            Yaw = position.Yaw;
        }

        public string? WorldId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Yaw { get; set; }

        internal WorldPosition ToDomain() =>
            new WorldPosition(WorldId!, X, Y, Z, Yaw);
    }

    public sealed class CommunityWorldBoundsHttpRequest
    {
        public double MinimumX { get; set; }
        public double MaximumX { get; set; }
        public double MinimumZ { get; set; }
        public double MaximumZ { get; set; }

        internal WorldBounds ToDomain() =>
            new WorldBounds(MinimumX, MaximumX, MinimumZ, MaximumZ);
    }

    public sealed class TeleportSettingsHttpResponse
    {
        public TeleportSettingsHttpResponse(TeleportSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            Kind = settings.Kind.ToString();
            Enabled = settings.Enabled;
            MaxHomes = settings.MaxHomes;
            CooldownMs = checked((long)settings.Cooldown.TotalMilliseconds);
            GlobalCooldownMs = checked((long)settings.GlobalCooldown.TotalMilliseconds);
            DenyDuringBloodMoon = settings.DenyDuringBloodMoon;
            FeeAmount = settings.FeeAmount;
            HomeExperience = settings.HomeExperience == null
                ? null
                : new HomeTeleportExperienceHttpModel(settings.HomeExperience);
            UpdatedAtUtc = settings.UpdatedAtUtc.UtcDateTime;
            RowVersion = settings.RowVersion;
        }

        public string Kind { get; }
        public bool Enabled { get; }
        public int? MaxHomes { get; }
        public long CooldownMs { get; }
        public long GlobalCooldownMs { get; }
        public bool DenyDuringBloodMoon { get; }
        public long FeeAmount { get; }
        public HomeTeleportExperienceHttpModel? HomeExperience { get; }
        public DateTime UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class TeleportSettingsUpsertHttpRequest
    {
        public bool Enabled { get; set; }
        public int? MaxHomes { get; set; }
        public long CooldownMs { get; set; }
        public long GlobalCooldownMs { get; set; }
        public bool DenyDuringBloodMoon { get; set; }
        public long FeeAmount { get; set; }
        public HomeTeleportExperienceHttpModel? HomeExperience { get; set; }
        public long ExpectedRowVersion { get; set; }
    }

    public sealed class HomeTeleportExperienceHttpModel
    {
        public HomeTeleportExperienceHttpModel() { }

        internal HomeTeleportExperienceHttpModel(HomeTeleportExperience value)
        {
            SetFeeAmount = value.SetFeeAmount;
            ListCommandName = value.ListCommandName;
            SetCommandName = value.SetCommandName;
            DeleteCommandName = value.DeleteCommandName;
            TeleportCommandName = value.TeleportCommandName;
            NoHomesMessage = value.NoHomesMessage;
            LimitMessage = value.LimitMessage;
            SetSuccessMessage = value.SetSuccessMessage;
            OverwriteMessage = value.OverwriteMessage;
            DeleteSuccessMessage = value.DeleteSuccessMessage;
            NotFoundMessage = value.NotFoundMessage;
            CooldownMessage = value.CooldownMessage;
            TeleportSuccessMessage = value.TeleportSuccessMessage;
            SetInsufficientFundsMessage = value.SetInsufficientFundsMessage;
            TeleportInsufficientFundsMessage = value.TeleportInsufficientFundsMessage;
            BloodMoonMessage = value.BloodMoonMessage;
        }

        public long SetFeeAmount { get; set; }
        public string ListCommandName { get; set; } = "homes";
        public string SetCommandName { get; set; } = "sethome";
        public string DeleteCommandName { get; set; } = "delhome";
        public string TeleportCommandName { get; set; } = "home";
        public string NoHomesMessage { get; set; } = string.Empty;
        public string LimitMessage { get; set; } = string.Empty;
        public string SetSuccessMessage { get; set; } = string.Empty;
        public string OverwriteMessage { get; set; } = string.Empty;
        public string DeleteSuccessMessage { get; set; } = string.Empty;
        public string NotFoundMessage { get; set; } = string.Empty;
        public string CooldownMessage { get; set; } = string.Empty;
        public string TeleportSuccessMessage { get; set; } = string.Empty;
        public string SetInsufficientFundsMessage { get; set; } = string.Empty;
        public string TeleportInsufficientFundsMessage { get; set; } = string.Empty;
        public string BloodMoonMessage { get; set; } = string.Empty;

        internal HomeTeleportExperience ToDomain() => new HomeTeleportExperience(
            SetFeeAmount, ListCommandName, SetCommandName, DeleteCommandName,
            TeleportCommandName, NoHomesMessage, LimitMessage, SetSuccessMessage,
            OverwriteMessage, DeleteSuccessMessage, NotFoundMessage, CooldownMessage,
            TeleportSuccessMessage, SetInsufficientFundsMessage,
            TeleportInsufficientFundsMessage, BloodMoonMessage);
    }

    public sealed class PlayerHomeHttpResponse
    {
        public PlayerHomeHttpResponse(PlayerHome home)
        {
            if (home == null) throw new ArgumentNullException(nameof(home));
            HomeId = home.HomeId;
            CrossplatformId = home.CrossplatformId;
            Name = home.Name;
            Position = new CommunityWorldPositionHttpModel(home.Position);
            CreatedAtUtc = home.CreatedAtUtc.UtcDateTime;
            UpdatedAtUtc = home.UpdatedAtUtc.UtcDateTime;
            RowVersion = home.RowVersion;
        }

        public string HomeId { get; }
        public string CrossplatformId { get; }
        public string Name { get; }
        public CommunityWorldPositionHttpModel Position { get; }
        public DateTime CreatedAtUtc { get; }
        public DateTime UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class CityHttpResponse
    {
        public CityHttpResponse(City city)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            CityId = city.CityId;
            Name = city.Name;
            Description = city.Description;
            Enabled = city.Enabled;
            Position = new CommunityWorldPositionHttpModel(city.Position);
            SortOrder = city.SortOrder;
            CreatedAtUtc = city.CreatedAtUtc.UtcDateTime;
            UpdatedAtUtc = city.UpdatedAtUtc.UtcDateTime;
            RowVersion = city.RowVersion;
        }

        public string CityId { get; }
        public string Name { get; }
        public string Description { get; }
        public bool Enabled { get; }
        public CommunityWorldPositionHttpModel Position { get; }
        public int SortOrder { get; }
        public DateTime CreatedAtUtc { get; }
        public DateTime UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class CityUpsertHttpRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool Enabled { get; set; }
        public CommunityWorldPositionHttpModel? Position { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class FriendshipStatusHttpResponse
    {
        public FriendshipStatusHttpResponse(
            string firstCrossplatformId,
            string secondCrossplatformId,
            bool areFriends)
        {
            FirstCrossplatformId = firstCrossplatformId;
            SecondCrossplatformId = secondCrossplatformId;
            AreFriends = areFriends;
        }

        public string FirstCrossplatformId { get; }
        public string SecondCrossplatformId { get; }
        public bool AreFriends { get; }
    }

    public sealed class FriendshipHttpResponse
    {
        public FriendshipHttpResponse(Friendship friendship)
        {
            if (friendship == null) throw new ArgumentNullException(nameof(friendship));
            FriendshipId = friendship.FriendshipId;
            MemberACrossplatformId = friendship.MemberACrossplatformId;
            MemberBCrossplatformId = friendship.MemberBCrossplatformId;
            CreatedByCrossplatformId = friendship.CreatedByCrossplatformId;
            AcceptedAtUtc = friendship.AcceptedAtUtc.UtcDateTime;
        }

        public string FriendshipId { get; }
        public string MemberACrossplatformId { get; }
        public string MemberBCrossplatformId { get; }
        public string CreatedByCrossplatformId { get; }
        public DateTime AcceptedAtUtc { get; }
    }

    public sealed class FriendRequestHttpResponse
    {
        public FriendRequestHttpResponse(FriendRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            RequestId = request.RequestId;
            RequesterCrossplatformId = request.RequesterCrossplatformId;
            TargetCrossplatformId = request.TargetCrossplatformId;
            State = request.State.ToString();
            FriendshipId = request.FriendshipId;
            CreatedAtUtc = request.CreatedAtUtc.UtcDateTime;
            ExpiresAtUtc = request.ExpiresAtUtc.UtcDateTime;
            RespondedAtUtc = request.RespondedAtUtc?.UtcDateTime;
            RowVersion = request.RowVersion;
        }

        public string RequestId { get; }
        public string RequesterCrossplatformId { get; }
        public string TargetCrossplatformId { get; }
        public string State { get; }
        public string? FriendshipId { get; }
        public DateTime CreatedAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
        public DateTime? RespondedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class CreateFriendRequestHttpRequest
    {
        public string? RequestId { get; set; }
        public string? RequesterCrossplatformId { get; set; }
        public string? TargetCrossplatformId { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    public sealed class RespondFriendRequestHttpRequest
    {
        public string? ResponderCrossplatformId { get; set; }
        public bool Accept { get; set; }
        public string? FriendshipId { get; set; }
    }

    public sealed class TeleportPlayerHttpRequest
    {
        public string? CrossplatformId { get; set; }
        public int EntityId { get; set; }
        public CommunityWorldPositionHttpModel? Position { get; set; }
        public bool IsOnline { get; set; }
        public bool IsAlive { get; set; }
        public bool IsSpawned { get; set; }
        public bool IsBloodMoon { get; set; }
        public bool AllowsFriendTeleport { get; set; }
        public CommunityWorldBoundsHttpRequest? WorldBounds { get; set; }

        internal TeleportPlayerSnapshot ToDomain() =>
            new TeleportPlayerSnapshot(
                CrossplatformId!,
                EntityId,
                Position!.ToDomain(),
                IsOnline,
                IsAlive,
                IsSpawned,
                IsBloodMoon,
                AllowsFriendTeleport,
                WorldBounds!.ToDomain());
    }

    public sealed class CreateTeleportOperationHttpRequest
    {
        public string? OperationId { get; set; }
        public string? IdempotencyKey { get; set; }
        public string? Kind { get; set; }
        public TeleportPlayerHttpRequest? Player { get; set; }
        public TeleportPlayerHttpRequest? Target { get; set; }
        public string? DestinationName { get; set; }
        public CommunityWorldPositionHttpModel? Destination { get; set; }
        public string? ActorId { get; set; }
        public string? CorrelationId { get; set; }
    }

    public sealed class TeleportOperationHttpResponse
    {
        public TeleportOperationHttpResponse(TeleportOperation operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            OperationId = operation.OperationId;
            Kind = operation.Kind.ToString();
            CrossplatformId = operation.CrossplatformId;
            TargetCrossplatformId = operation.TargetCrossplatformId;
            Destination = new CommunityWorldPositionHttpModel(operation.Destination);
            Origin = operation.Origin == null
                ? null
                : new CommunityWorldPositionHttpModel(operation.Origin);
            State = operation.State.ToString();
            ErrorCode = operation.ErrorCode;
            CorrelationId = operation.Draft.CorrelationId;
            CreatedAtUtc = operation.Draft.CreatedAtUtc.UtcDateTime;
            UpdatedAtUtc = operation.UpdatedAtUtc.UtcDateTime;
            CompletedAtUtc = operation.CompletedAtUtc?.UtcDateTime;
            RowVersion = operation.RowVersion;
        }

        public string OperationId { get; }
        public string Kind { get; }
        public string CrossplatformId { get; }
        public string? TargetCrossplatformId { get; }
        public CommunityWorldPositionHttpModel Destination { get; }
        public CommunityWorldPositionHttpModel? Origin { get; }
        public string State { get; }
        public string? ErrorCode { get; }
        public string? CorrelationId { get; }
        public DateTime CreatedAtUtc { get; }
        public DateTime UpdatedAtUtc { get; }
        public DateTime? CompletedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class VoteConfigurationHttpResponse
    {
        public VoteConfigurationHttpResponse(VoteConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            ConfigurationId = configuration.ConfigurationId;
            Kind = configuration.Kind.ToString();
            Enabled = configuration.Enabled;
            DurationMs = checked((long)configuration.Duration.TotalMilliseconds);
            ThresholdPercent = configuration.ThresholdPercent;
            MinimumParticipants = configuration.MinimumParticipants;
            InitiatorMinimumOnlineMs = checked((long)configuration.InitiatorMinimumOnline.TotalMilliseconds);
            ParticipantMinimumOnlineMs = checked((long)configuration.ParticipantMinimumOnline.TotalMilliseconds);
            InitiatorCooldownMs = checked((long)configuration.InitiatorCooldown.TotalMilliseconds);
            TargetCooldownMs = checked((long)configuration.TargetCooldown.TotalMilliseconds);
            GlobalCooldownMs = checked((long)configuration.GlobalCooldown.TotalMilliseconds);
            MutualExclusionScope = configuration.MutualExclusionScope;
            AllowVoteChange = configuration.AllowVoteChange;
            UpdatedAtUtc = configuration.UpdatedAtUtc.UtcDateTime;
            RowVersion = configuration.RowVersion;
        }

        public string ConfigurationId { get; }
        public string Kind { get; }
        public bool Enabled { get; }
        public long DurationMs { get; }
        public int ThresholdPercent { get; }
        public int MinimumParticipants { get; }
        public long InitiatorMinimumOnlineMs { get; }
        public long ParticipantMinimumOnlineMs { get; }
        public long InitiatorCooldownMs { get; }
        public long TargetCooldownMs { get; }
        public long GlobalCooldownMs { get; }
        public string MutualExclusionScope { get; }
        public bool AllowVoteChange { get; }
        public DateTime UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class VoteConfigurationUpsertHttpRequest
    {
        public bool Enabled { get; set; }
        public long DurationMs { get; set; }
        public int ThresholdPercent { get; set; }
        public int MinimumParticipants { get; set; }
        public long InitiatorMinimumOnlineMs { get; set; }
        public long ParticipantMinimumOnlineMs { get; set; }
        public long InitiatorCooldownMs { get; set; }
        public long TargetCooldownMs { get; set; }
        public long GlobalCooldownMs { get; set; }
        public string? MutualExclusionScope { get; set; }
        public bool AllowVoteChange { get; set; }
        public long ExpectedRowVersion { get; set; }
    }

    public sealed class VoteEligiblePlayerHttpRequest
    {
        public string? CrossplatformId { get; set; }
        public long OnlineDurationMs { get; set; }
    }

    public sealed class StartVoteRoundHttpRequest
    {
        public string? RoundId { get; set; }
        public string? Kind { get; set; }
        public string? InitiatorCrossplatformId { get; set; }
        public string? TargetCrossplatformId { get; set; }
        public IReadOnlyList<VoteEligiblePlayerHttpRequest>? EligiblePlayers { get; set; }
        public string? IdempotencyKey { get; set; }
        public string? CorrelationId { get; set; }
    }

    public sealed class CastVoteHttpRequest
    {
        public string? CrossplatformId { get; set; }
        public string? Choice { get; set; }
    }

    public sealed class VoteRoundHttpResponse
    {
        public VoteRoundHttpResponse(VoteRoundSnapshot round)
        {
            if (round == null) throw new ArgumentNullException(nameof(round));
            RoundId = round.RoundId;
            ConfigurationId = round.ConfigurationId;
            Kind = round.Kind.ToString();
            State = round.State.ToString();
            InitiatorCrossplatformId = round.InitiatorCrossplatformId;
            TargetCrossplatformId = round.TargetCrossplatformId;
            ScopeKey = round.ScopeKey;
            EligibleCount = round.EligibleCount;
            ThresholdPercent = round.ThresholdPercent;
            MinimumParticipants = round.MinimumParticipants;
            AllowVoteChange = round.AllowVoteChange;
            ActionJobId = round.ActionJobId;
            ActionOperationId = round.ActionOperationId;
            CorrelationId = round.CorrelationId;
            OpenedAtUtc = round.OpenedAtUtc.UtcDateTime;
            ExpiresAtUtc = round.ExpiresAtUtc.UtcDateTime;
            SettledAtUtc = round.SettledAtUtc?.UtcDateTime;
            ActionCompletedAtUtc = round.ActionCompletedAtUtc?.UtcDateTime;
            RowVersion = round.RowVersion;
        }

        public string RoundId { get; }
        public string ConfigurationId { get; }
        public string Kind { get; }
        public string State { get; }
        public string InitiatorCrossplatformId { get; }
        public string? TargetCrossplatformId { get; }
        public string ScopeKey { get; }
        public int EligibleCount { get; }
        public int ThresholdPercent { get; }
        public int MinimumParticipants { get; }
        public bool AllowVoteChange { get; }
        public string? ActionJobId { get; }
        public string? ActionOperationId { get; }
        public string? CorrelationId { get; }
        public DateTime OpenedAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
        public DateTime? SettledAtUtc { get; }
        public DateTime? ActionCompletedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class VoteStartHttpResponse
    {
        public VoteStartHttpResponse(VoteStartResult result)
        {
            Status = result.Status.ToString();
            Round = result.Round == null ? null : new VoteRoundHttpResponse(result.Round);
        }

        public string Status { get; }
        public VoteRoundHttpResponse? Round { get; }
    }

    public sealed class VoteCastHttpResponse
    {
        public VoteCastHttpResponse(VoteCastResult result)
        {
            Status = result.Status.ToString();
            Round = result.Round == null ? null : new VoteRoundHttpResponse(result.Round);
        }

        public string Status { get; }
        public VoteRoundHttpResponse? Round { get; }
    }

    public sealed class VoteSettlementHttpResponse
    {
        public VoteSettlementHttpResponse(VoteSettlementResult result)
        {
            Status = result.Status.ToString();
            Round = new VoteRoundHttpResponse(result.Round);
            ParticipantCount = result.ParticipantCount;
            YesCount = result.YesCount;
            NoCount = result.NoCount;
            WasSettled = result.WasSettled;
        }

        public string Status { get; }
        public VoteRoundHttpResponse Round { get; }
        public int ParticipantCount { get; }
        public int YesCount { get; }
        public int NoCount { get; }
        public bool WasSettled { get; }
    }

    public sealed class VoteActionDispatchHttpResponse
    {
        public VoteActionDispatchHttpResponse(VoteActionDispatchResult result)
        {
            Status = result.Status.ToString();
            Round = new VoteRoundHttpResponse(result.Round);
        }

        public string Status { get; }
        public VoteRoundHttpResponse Round { get; }
    }
}
