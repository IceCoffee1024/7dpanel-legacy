using System;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Domain.Community;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Community
{
    internal static class CommunityRowMapper
    {
        internal static TeleportSettings ToSettings(SqliteCommunityStore.SettingsRow row) => new TeleportSettings(
            Parse<TeleportKind>(row.Kind),
            row.Enabled != 0,
            row.MaxHomes,
            TimeSpan.FromMilliseconds(row.CooldownMs),
            TimeSpan.FromMilliseconds(row.GlobalCooldownMs),
            row.DenyDuringBloodMoon != 0,
            row.FeeAmount,
            DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
            row.RowVersion,
            string.Equals(row.Kind, TeleportKind.Home.ToString(), StringComparison.Ordinal)
                ? new HomeTeleportExperience(
                    row.SetFeeAmount, row.ListCommandName, row.SetCommandName,
                    row.DeleteCommandName, row.TeleportCommandName, row.NoHomesMessage,
                    row.HomeLimitMessage, row.SetSuccessMessage, row.OverwriteMessage,
                    row.DeleteSuccessMessage, row.HomeNotFoundMessage, row.HomeCooldownMessage,
                    row.TeleportSuccessMessage, row.SetInsufficientFundsMessage,
                    row.TeleportInsufficientFundsMessage, row.BloodMoonMessage)
                : null);

        internal static PlayerHome ToHome(SqliteCommunityStore.HomeRow row) => new PlayerHome(
            row.HomeId,
            row.CrossplatformId,
            row.Name,
            Position(row.WorldId, row.X, row.Y, row.Z, row.Yaw),
            DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
            DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
            row.RowVersion);

        internal static City ToCity(SqliteCommunityStore.CityRow row) => new City(
            row.CityId,
            row.Name,
            row.Description,
            row.Enabled != 0,
            Position(row.WorldId, row.X, row.Y, row.Z, row.Yaw),
            row.SortOrder,
            DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
            DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
            row.RowVersion);

        internal static Friendship ToFriendship(SqliteCommunityStore.FriendshipRow row) => new Friendship(
            row.FriendshipId,
            row.MemberACrossplatformId,
            row.MemberBCrossplatformId,
            row.CreatedByCrossplatformId,
            DateTimeOffset.FromUnixTimeMilliseconds(row.AcceptedAtUtc));

        internal static FriendRequest ToFriendRequest(SqliteCommunityStore.FriendRequestRow row) => new FriendRequest(
            row.RequestId,
            row.RequesterCrossplatformId,
            row.TargetCrossplatformId,
            Parse<FriendRequestState>(row.State),
            row.FriendshipId,
            DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
            DateTimeOffset.FromUnixTimeMilliseconds(row.ExpiresAtUtc),
            row.RespondedAtUtc.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(row.RespondedAtUtc.Value)
                : null,
            row.RowVersion);

        internal static TeleportFriendRequest ToTeleportFriendRequest(
            SqliteCommunityStore.TeleportFriendRequestRow row) => new TeleportFriendRequest(
                row.RequestId,
                row.IdempotencyKey,
                row.RequesterCrossplatformId,
                row.RequesterEntityId,
                row.RequesterWorldId,
                row.TargetCrossplatformId,
                row.TargetEntityId,
                row.TargetWorldId,
                Parse<TeleportFriendRequestState>(row.State),
                row.TeleportOperationId,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.ExpiresAtUtc),
                row.RespondedAtUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(row.RespondedAtUtc.Value)
                    : null,
                row.RowVersion);

        internal static PlayerReturnPoint ToReturnPoint(SqliteCommunityStore.ReturnPointRow row) => new PlayerReturnPoint(
            row.CrossplatformId,
            row.SourceOperationId,
            Position(row.WorldId, row.X, row.Y, row.Z, row.Yaw),
            DateTimeOffset.FromUnixTimeMilliseconds(row.SavedAtUtc),
            row.RowVersion);

        internal static TeleportOperation ToOperation(SqliteCommunityStore.OperationRow row)
        {
            var draft = new TeleportOperationDraft(
                row.OperationId,
                Parse<TeleportKind>(row.Kind),
                row.CrossplatformId,
                row.TargetCrossplatformId,
                row.ExpectedEntityId,
                row.ExpectedWorldId,
                Position(
                    row.DestinationWorldId,
                    row.DestinationX,
                    row.DestinationY,
                    row.DestinationZ,
                    row.DestinationYaw),
                row.IdempotencyKey,
                row.ReservationId,
                row.ActorKind,
                row.ActorId,
                row.CorrelationId,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc));
            var origin = row.OriginWorldId == null
                ? null
                : Position(
                    row.OriginWorldId,
                    row.OriginX!.Value,
                    row.OriginY!.Value,
                    row.OriginZ!.Value,
                    row.OriginYaw!.Value);
            return new TeleportOperation(
                draft,
                origin,
                Parse<TeleportOperationState>(row.State),
                row.ErrorCode,
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.CompletedAtUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(row.CompletedAtUtc.Value)
                    : null,
                row.RowVersion);
        }

        private static WorldPosition Position(
            string worldId,
            double x,
            double y,
            double z,
            double yaw) => new WorldPosition(worldId, x, y, z, yaw);

        private static T Parse<T>(string value) where T : struct, Enum =>
            (T)Enum.Parse(typeof(T), value, ignoreCase: false);
    }
}
