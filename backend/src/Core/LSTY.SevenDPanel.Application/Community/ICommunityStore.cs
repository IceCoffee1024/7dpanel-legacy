using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Domain.Community;

namespace LSTY.SevenDPanel.Application.Community
{
    public interface ICommunityStore
    {
        TeleportSettings GetTeleportSettings(TeleportKind kind);
        TeleportSettings SaveTeleportSettings(TeleportSettings settings);

        PlayerHome SaveHome(PlayerHome home, int maxHomes);
        IReadOnlyList<PlayerHome> ListHomes(string crossplatformId);
        PlayerHome? FindHome(string crossplatformId, string name);
        bool DeleteHome(string crossplatformId, string name);

        City SaveCity(City city);
        IReadOnlyList<City> ListCities();
        IReadOnlyList<City> ListEnabledCities();
        City? FindEnabledCity(string name);

        FriendRequest CreateFriendRequest(FriendRequest request);
        FriendRequest RespondToFriendRequest(
            string requestId,
            string responderCrossplatformId,
            bool accept,
            string? friendshipId,
            DateTimeOffset respondedAtUtc);
        bool AreFriends(string firstCrossplatformId, string secondCrossplatformId);
        IReadOnlyList<Friendship> ListFriendships();
        bool RemoveFriendship(string firstCrossplatformId, string secondCrossplatformId);

        PlayerReturnPoint? GetReturnPoint(string crossplatformId);
        DateTimeOffset? GetCooldown(string crossplatformId, TeleportKind kind);

        TeleportOperation CreateTeleportOperation(TeleportOperationDraft draft);
        IReadOnlyList<TeleportOperation> ListTeleportOperations();
        TeleportOperation? FindTeleportOperation(string operationId);
        bool TryTransitionTeleportOperation(
            string operationId,
            TeleportOperationState expectedState,
            TeleportOperationState nextState,
            string? errorCode,
            DateTimeOffset updatedAtUtc);
        TeleportOperation CompleteTeleportOperation(
            string operationId,
            WorldPosition origin,
            DateTimeOffset kindAvailableAtUtc,
            DateTimeOffset globalAvailableAtUtc,
            DateTimeOffset completedAtUtc);
    }
}
