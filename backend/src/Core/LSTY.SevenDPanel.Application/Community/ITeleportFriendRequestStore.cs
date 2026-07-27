using System;

namespace LSTY.SevenDPanel.Application.Community
{
    public interface ITeleportFriendRequestStore
    {
        bool AreFriends(string firstCrossplatformId, string secondCrossplatformId);
        TeleportFriendRequest CreateTeleportFriendRequest(TeleportFriendRequest request);
        TeleportFriendRequest? GetTeleportFriendRequest(string requestId);
        TeleportFriendRequest? FindPendingTeleportFriendRequest(string targetCrossplatformId);
        bool TryRespondToTeleportFriendRequest(
            string requestId,
            string responderCrossplatformId,
            bool accept,
            string? teleportOperationId,
            DateTimeOffset respondedAtUtc);
        bool TryExpireTeleportFriendRequest(
            string requestId,
            string responderCrossplatformId,
            DateTimeOffset expiredAtUtc);
    }
}
