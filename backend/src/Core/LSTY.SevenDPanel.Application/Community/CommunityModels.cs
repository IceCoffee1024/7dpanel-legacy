using System;
using System.Linq;
using LSTY.SevenDPanel.Domain.Community;

namespace LSTY.SevenDPanel.Application.Community
{
    public enum TeleportKind
    {
        Global,
        Home,
        City,
        Friend,
        Return,
        Admin
    }

    public enum FriendRequestState
    {
        Pending,
        Accepted,
        Rejected,
        Cancelled,
        Expired
    }

    public enum TeleportActionStatus
    {
        Succeeded,
        Rejected,
        Failed,
        Cancelled,
        ResultUnknown
    }

    public static class TeleportFailureCodes
    {
        public const string TeleportDisabled = "teleport_disabled";
        public const string PlayerNotOnline = "player_not_online";
        public const string PlayerDead = "player_dead";
        public const string PlayerNotSpawned = "player_not_spawned";
        public const string TargetChanged = "target_changed";
        public const string TargetNotAllowed = "target_not_allowed";
        public const string DestinationNotFound = "destination_not_found";
        public const string DestinationWorldMismatch = "destination_world_mismatch";
        public const string DestinationOutOfBounds = "destination_out_of_bounds";
        public const string BloodMoonDenied = "blood_moon_denied";
        public const string CooldownActive = "cooldown_active";
        public const string TeleportAlreadyInProgress = "teleport_already_in_progress";
        public const string InsufficientFunds = "insufficient_funds";
        public const string AccountUnavailable = "account_unavailable";
        public const string GatewayFailure = "gateway_failure";
        public const string Cancelled = "cancelled";
        public const string ResultUnknown = "result_unknown";
        public const string StateConflict = "state_conflict";
    }

    public sealed record WorldPosition
    {
        public WorldPosition(string worldId, double x, double y, double z, double yaw)
        {
            WorldId = CommunityModelValidation.RequireText(worldId, nameof(worldId));
            X = CommunityModelValidation.RequireFinite(x, nameof(x));
            Y = CommunityModelValidation.RequireFinite(y, nameof(y));
            Z = CommunityModelValidation.RequireFinite(z, nameof(z));
            Yaw = CommunityModelValidation.RequireFinite(yaw, nameof(yaw));
        }

        public string WorldId { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double Yaw { get; }
    }

    public sealed class WorldBounds
    {
        public WorldBounds(double minimumX, double maximumX, double minimumZ, double maximumZ)
        {
            MinimumX = CommunityModelValidation.RequireFinite(minimumX, nameof(minimumX));
            MaximumX = CommunityModelValidation.RequireFinite(maximumX, nameof(maximumX));
            MinimumZ = CommunityModelValidation.RequireFinite(minimumZ, nameof(minimumZ));
            MaximumZ = CommunityModelValidation.RequireFinite(maximumZ, nameof(maximumZ));
            if (minimumX > maximumX) throw new ArgumentOutOfRangeException(nameof(minimumX));
            if (minimumZ > maximumZ) throw new ArgumentOutOfRangeException(nameof(minimumZ));
        }

        public double MinimumX { get; }
        public double MaximumX { get; }
        public double MinimumZ { get; }
        public double MaximumZ { get; }

        public bool Contains(WorldPosition position)
        {
            if (position == null) throw new ArgumentNullException(nameof(position));
            return position.X >= MinimumX && position.X <= MaximumX &&
                   position.Z >= MinimumZ && position.Z <= MaximumZ;
        }
    }

    public sealed class TeleportSettings
    {
        public TeleportSettings(
            TeleportKind kind,
            bool enabled,
            int? maxHomes,
            TimeSpan cooldown,
            TimeSpan globalCooldown,
            bool denyDuringBloodMoon,
            long feeAmount,
            DateTimeOffset updatedAtUtc,
            long rowVersion,
            HomeTeleportExperience? homeExperience = null)
        {
            CommunityModelValidation.RequireOperationKind(kind, nameof(kind));
            if (maxHomes < 0) throw new ArgumentOutOfRangeException(nameof(maxHomes));
            if (cooldown < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(cooldown));
            if (globalCooldown < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(globalCooldown));
            if (feeAmount < 0) throw new ArgumentOutOfRangeException(nameof(feeAmount));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            CommunityModelValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            Kind = kind;
            Enabled = enabled;
            MaxHomes = maxHomes;
            Cooldown = cooldown;
            GlobalCooldown = globalCooldown;
            DenyDuringBloodMoon = denyDuringBloodMoon;
            FeeAmount = feeAmount;
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
            HomeExperience = kind == TeleportKind.Home
                ? homeExperience ?? HomeTeleportExperience.Default
                : null;
        }

        public TeleportKind Kind { get; }
        public bool Enabled { get; }
        public int? MaxHomes { get; }
        public TimeSpan Cooldown { get; }
        public TimeSpan GlobalCooldown { get; }
        public bool DenyDuringBloodMoon { get; }
        public long FeeAmount { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
        public HomeTeleportExperience? HomeExperience { get; }
    }

    public sealed class HomeTeleportExperience
    {
        public static readonly HomeTeleportExperience Default = new HomeTeleportExperience(
            0, "homes", "sethome", "delhome", "home",
            "You have no saved homes.", "Home limit reached.", "Home '{name}' saved.",
            "Home '{name}' updated.", "Home '{name}' deleted.", "Home '{name}' was not found.",
            "Teleport cooldown is active.", "Teleported to home '{name}'.",
            "Not enough balance to set a home.", "Not enough balance to teleport home.",
            "Home teleport is disabled during a blood moon.");

        public HomeTeleportExperience(
            long setFeeAmount, string listCommandName, string setCommandName,
            string deleteCommandName, string teleportCommandName, string noHomesMessage,
            string limitMessage, string setSuccessMessage, string overwriteMessage,
            string deleteSuccessMessage, string notFoundMessage, string cooldownMessage,
            string teleportSuccessMessage, string setInsufficientFundsMessage,
            string teleportInsufficientFundsMessage, string bloodMoonMessage)
        {
            if (setFeeAmount < 0) throw new ArgumentOutOfRangeException(nameof(setFeeAmount));
            SetFeeAmount = setFeeAmount;
            ListCommandName = Require(listCommandName, nameof(listCommandName), false);
            SetCommandName = Require(setCommandName, nameof(setCommandName), false);
            DeleteCommandName = Require(deleteCommandName, nameof(deleteCommandName), false);
            TeleportCommandName = Require(teleportCommandName, nameof(teleportCommandName), false);
            var commands = new[] { ListCommandName, SetCommandName, DeleteCommandName, TeleportCommandName };
            if (commands.Distinct(StringComparer.OrdinalIgnoreCase).Count() != commands.Length)
                throw new ArgumentException("Home command names must be unique.");
            ValidateCommand(ListCommandName);
            ValidateCommand(SetCommandName);
            ValidateCommand(DeleteCommandName);
            ValidateCommand(TeleportCommandName);
            NoHomesMessage = Require(noHomesMessage, nameof(noHomesMessage), true);
            LimitMessage = Require(limitMessage, nameof(limitMessage), true);
            SetSuccessMessage = Require(setSuccessMessage, nameof(setSuccessMessage), true);
            OverwriteMessage = Require(overwriteMessage, nameof(overwriteMessage), true);
            DeleteSuccessMessage = Require(deleteSuccessMessage, nameof(deleteSuccessMessage), true);
            NotFoundMessage = Require(notFoundMessage, nameof(notFoundMessage), true);
            CooldownMessage = Require(cooldownMessage, nameof(cooldownMessage), true);
            TeleportSuccessMessage = Require(teleportSuccessMessage, nameof(teleportSuccessMessage), true);
            SetInsufficientFundsMessage = Require(setInsufficientFundsMessage, nameof(setInsufficientFundsMessage), true);
            TeleportInsufficientFundsMessage = Require(teleportInsufficientFundsMessage, nameof(teleportInsufficientFundsMessage), true);
            BloodMoonMessage = Require(bloodMoonMessage, nameof(bloodMoonMessage), true);
        }

        public long SetFeeAmount { get; }
        public string ListCommandName { get; }
        public string SetCommandName { get; }
        public string DeleteCommandName { get; }
        public string TeleportCommandName { get; }
        public string NoHomesMessage { get; }
        public string LimitMessage { get; }
        public string SetSuccessMessage { get; }
        public string OverwriteMessage { get; }
        public string DeleteSuccessMessage { get; }
        public string NotFoundMessage { get; }
        public string CooldownMessage { get; }
        public string TeleportSuccessMessage { get; }
        public string SetInsufficientFundsMessage { get; }
        public string TeleportInsufficientFundsMessage { get; }
        public string BloodMoonMessage { get; }

        private static string Require(string value, string parameterName, bool allowWhitespace)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A value is required.", parameterName);
            var normalized = value.Trim();
            if (!allowWhitespace && normalized.Any(char.IsWhiteSpace))
                throw new ArgumentException("Command names cannot contain whitespace.", parameterName);
            return normalized;
        }

        private static void ValidateCommand(string value)
        {
            if (string.Equals(value, "help", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A home command name conflicts with another registered command.");
        }
    }

    public sealed class PlayerHome
    {
        public PlayerHome(
            string homeId,
            string crossplatformId,
            string name,
            WorldPosition position,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            HomeId = CommunityModelValidation.RequireText(homeId, nameof(homeId));
            CrossplatformId = CommunityModelValidation.RequireText(crossplatformId, nameof(crossplatformId));
            Name = CommunityModelValidation.RequireName(name, nameof(name));
            Position = position ?? throw new ArgumentNullException(nameof(position));
            CreatedAtUtc = CommunityModelValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            UpdatedAtUtc = CommunityModelValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (updatedAtUtc < createdAtUtc) throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            RowVersion = rowVersion;
        }

        public string HomeId { get; }
        public string CrossplatformId { get; }
        public string Name { get; }
        public WorldPosition Position { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class City
    {
        public City(
            string cityId,
            string name,
            string description,
            bool enabled,
            WorldPosition position,
            int sortOrder,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            CityId = CommunityModelValidation.RequireText(cityId, nameof(cityId));
            Name = CommunityModelValidation.RequireName(name, nameof(name));
            Description = description?.Trim() ?? throw new ArgumentNullException(nameof(description));
            Position = position ?? throw new ArgumentNullException(nameof(position));
            CreatedAtUtc = CommunityModelValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            UpdatedAtUtc = CommunityModelValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (updatedAtUtc < createdAtUtc) throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            Enabled = enabled;
            SortOrder = sortOrder;
            RowVersion = rowVersion;
        }

        public string CityId { get; }
        public string Name { get; }
        public string Description { get; }
        public bool Enabled { get; }
        public WorldPosition Position { get; }
        public int SortOrder { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class Friendship
    {
        public Friendship(
            string friendshipId,
            string memberACrossplatformId,
            string memberBCrossplatformId,
            string createdByCrossplatformId,
            DateTimeOffset acceptedAtUtc)
        {
            FriendshipId = CommunityModelValidation.RequireText(friendshipId, nameof(friendshipId));
            MemberACrossplatformId = CommunityModelValidation.RequireText(
                memberACrossplatformId, nameof(memberACrossplatformId));
            MemberBCrossplatformId = CommunityModelValidation.RequireText(
                memberBCrossplatformId, nameof(memberBCrossplatformId));
            if (string.CompareOrdinal(MemberACrossplatformId, MemberBCrossplatformId) >= 0)
                throw new ArgumentException("Friendship members must use canonical ordering.");
            CreatedByCrossplatformId = CommunityModelValidation.RequireText(
                createdByCrossplatformId, nameof(createdByCrossplatformId));
            AcceptedAtUtc = CommunityModelValidation.RequireUtc(acceptedAtUtc, nameof(acceptedAtUtc));
        }

        public string FriendshipId { get; }
        public string MemberACrossplatformId { get; }
        public string MemberBCrossplatformId { get; }
        public string CreatedByCrossplatformId { get; }
        public DateTimeOffset AcceptedAtUtc { get; }
    }

    public sealed class FriendRequest
    {
        public FriendRequest(
            string requestId,
            string requesterCrossplatformId,
            string targetCrossplatformId,
            FriendRequestState state,
            string? friendshipId,
            DateTimeOffset createdAtUtc,
            DateTimeOffset expiresAtUtc,
            DateTimeOffset? respondedAtUtc,
            long rowVersion)
        {
            CommunityModelValidation.RequireDefined(state, nameof(state));
            RequestId = CommunityModelValidation.RequireText(requestId, nameof(requestId));
            RequesterCrossplatformId = CommunityModelValidation.RequireText(
                requesterCrossplatformId, nameof(requesterCrossplatformId));
            TargetCrossplatformId = CommunityModelValidation.RequireText(
                targetCrossplatformId, nameof(targetCrossplatformId));
            if (string.Equals(RequesterCrossplatformId, TargetCrossplatformId, StringComparison.Ordinal))
                throw new ArgumentException("A player cannot invite itself.", nameof(targetCrossplatformId));
            FriendshipId = CommunityModelValidation.OptionalText(friendshipId);
            CreatedAtUtc = CommunityModelValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            ExpiresAtUtc = CommunityModelValidation.RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
            if (expiresAtUtc <= createdAtUtc) throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
            if (respondedAtUtc.HasValue)
            {
                RespondedAtUtc = CommunityModelValidation.RequireUtc(
                    respondedAtUtc.Value, nameof(respondedAtUtc));
                if (respondedAtUtc.Value < createdAtUtc)
                    throw new ArgumentOutOfRangeException(nameof(respondedAtUtc));
            }
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            State = state;
            RowVersion = rowVersion;
        }

        public string RequestId { get; }
        public string RequesterCrossplatformId { get; }
        public string TargetCrossplatformId { get; }
        public FriendRequestState State { get; }
        public string? FriendshipId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset ExpiresAtUtc { get; }
        public DateTimeOffset? RespondedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class PlayerReturnPoint
    {
        public PlayerReturnPoint(
            string crossplatformId,
            string sourceOperationId,
            WorldPosition position,
            DateTimeOffset savedAtUtc,
            long rowVersion)
        {
            CrossplatformId = CommunityModelValidation.RequireText(crossplatformId, nameof(crossplatformId));
            SourceOperationId = CommunityModelValidation.RequireText(sourceOperationId, nameof(sourceOperationId));
            Position = position ?? throw new ArgumentNullException(nameof(position));
            SavedAtUtc = CommunityModelValidation.RequireUtc(savedAtUtc, nameof(savedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            RowVersion = rowVersion;
        }

        public string CrossplatformId { get; }
        public string SourceOperationId { get; }
        public WorldPosition Position { get; }
        public DateTimeOffset SavedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class TeleportPlayerSnapshot
    {
        public TeleportPlayerSnapshot(
            string crossplatformId,
            int entityId,
            WorldPosition position,
            bool isOnline,
            bool isAlive,
            bool isSpawned,
            bool isBloodMoon,
            bool allowsFriendTeleport,
            WorldBounds worldBounds)
        {
            CrossplatformId = CommunityModelValidation.RequireText(crossplatformId, nameof(crossplatformId));
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            EntityId = entityId;
            Position = position ?? throw new ArgumentNullException(nameof(position));
            WorldBounds = worldBounds ?? throw new ArgumentNullException(nameof(worldBounds));
            IsOnline = isOnline;
            IsAlive = isAlive;
            IsSpawned = isSpawned;
            IsBloodMoon = isBloodMoon;
            AllowsFriendTeleport = allowsFriendTeleport;
        }

        public string CrossplatformId { get; }
        public int EntityId { get; }
        public WorldPosition Position { get; }
        public bool IsOnline { get; }
        public bool IsAlive { get; }
        public bool IsSpawned { get; }
        public bool IsBloodMoon { get; }
        public bool AllowsFriendTeleport { get; }
        public WorldBounds WorldBounds { get; }
    }

    public sealed class TeleportExecutionRequest
    {
        public TeleportExecutionRequest(
            string operationId,
            string idempotencyKey,
            TeleportPlayerSnapshot player,
            string actorKind,
            string actorId,
            string? correlationId)
        {
            OperationId = CommunityModelValidation.RequireText(operationId, nameof(operationId));
            IdempotencyKey = CommunityModelValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            Player = player ?? throw new ArgumentNullException(nameof(player));
            ActorKind = CommunityModelValidation.RequireText(actorKind, nameof(actorKind));
            ActorId = CommunityModelValidation.RequireText(actorId, nameof(actorId));
            CorrelationId = CommunityModelValidation.OptionalText(correlationId);
        }

        public string OperationId { get; }
        public string IdempotencyKey { get; }
        public TeleportPlayerSnapshot Player { get; }
        public string ActorKind { get; }
        public string ActorId { get; }
        public string? CorrelationId { get; }
    }

    public sealed class TeleportOperationDraft
    {
        public TeleportOperationDraft(
            string operationId,
            TeleportKind kind,
            string crossplatformId,
            string? targetCrossplatformId,
            int expectedEntityId,
            string expectedWorldId,
            WorldPosition destination,
            string idempotencyKey,
            string? reservationId,
            string actorKind,
            string actorId,
            string? correlationId,
            DateTimeOffset createdAtUtc)
        {
            CommunityModelValidation.RequireOperationKind(kind, nameof(kind));
            OperationId = CommunityModelValidation.RequireText(operationId, nameof(operationId));
            Kind = kind;
            CrossplatformId = CommunityModelValidation.RequireText(crossplatformId, nameof(crossplatformId));
            TargetCrossplatformId = CommunityModelValidation.OptionalText(targetCrossplatformId);
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            ExpectedEntityId = expectedEntityId;
            ExpectedWorldId = CommunityModelValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            IdempotencyKey = CommunityModelValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            ReservationId = CommunityModelValidation.OptionalText(reservationId);
            ActorKind = CommunityModelValidation.RequireText(actorKind, nameof(actorKind));
            ActorId = CommunityModelValidation.RequireText(actorId, nameof(actorId));
            CorrelationId = CommunityModelValidation.OptionalText(correlationId);
            CreatedAtUtc = CommunityModelValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
        }

        public string OperationId { get; }
        public TeleportKind Kind { get; }
        public string CrossplatformId { get; }
        public string? TargetCrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public WorldPosition Destination { get; }
        public string IdempotencyKey { get; }
        public string? ReservationId { get; }
        public string ActorKind { get; }
        public string ActorId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
    }

    public sealed class TeleportOperation
    {
        public TeleportOperation(
            TeleportOperationDraft draft,
            WorldPosition? origin,
            TeleportOperationState state,
            string? errorCode,
            DateTimeOffset updatedAtUtc,
            DateTimeOffset? completedAtUtc,
            long rowVersion)
        {
            Draft = draft ?? throw new ArgumentNullException(nameof(draft));
            CommunityModelValidation.RequireDefined(state, nameof(state));
            Origin = origin;
            ErrorCode = CommunityModelValidation.OptionalText(errorCode);
            UpdatedAtUtc = CommunityModelValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (updatedAtUtc < draft.CreatedAtUtc) throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
            if (completedAtUtc.HasValue)
            {
                CompletedAtUtc = CommunityModelValidation.RequireUtc(
                    completedAtUtc.Value, nameof(completedAtUtc));
            }
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            State = state;
            RowVersion = rowVersion;
        }

        public TeleportOperationDraft Draft { get; }
        public string OperationId => Draft.OperationId;
        public TeleportKind Kind => Draft.Kind;
        public string CrossplatformId => Draft.CrossplatformId;
        public string? TargetCrossplatformId => Draft.TargetCrossplatformId;
        public WorldPosition Destination => Draft.Destination;
        public string? ReservationId => Draft.ReservationId;
        public WorldPosition? Origin { get; }
        public TeleportOperationState State { get; }
        public string? ErrorCode { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public long RowVersion { get; }
    }

    public class CommunityException : InvalidOperationException
    {
        public CommunityException(string code) : base(code) => Code = code;
        public string Code { get; }
    }

    public sealed class CommunityConflictException : CommunityException
    {
        public CommunityConflictException() : base("community_conflict") { }
    }

    public sealed class CommunityLimitExceededException : CommunityException
    {
        public CommunityLimitExceededException() : base("community_limit_exceeded") { }
    }

    public sealed class CommunityNotFoundException : CommunityException
    {
        public CommunityNotFoundException() : base("community_not_found") { }
    }

    public sealed class TeleportRejectedException : CommunityException
    {
        public TeleportRejectedException(string code, DateTimeOffset? availableAtUtc = null)
            : base(code) => AvailableAtUtc = availableAtUtc;

        public DateTimeOffset? AvailableAtUtc { get; }
    }

    internal static class CommunityModelValidation
    {
        public static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        public static string RequireName(string value, string parameterName)
        {
            var normalized = RequireText(value, parameterName);
            if (normalized.Length > 64)
                throw new ArgumentOutOfRangeException(parameterName);
            return normalized;
        }

        public static string? OptionalText(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        public static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
            return value;
        }

        public static double RequireFinite(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }

        public static void RequireDefined<T>(T value, string parameterName) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }

        public static void RequireOperationKind(TeleportKind kind, string parameterName)
        {
            RequireDefined(kind, parameterName);
            if (kind == TeleportKind.Global) throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
