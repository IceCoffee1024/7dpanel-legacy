using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Domain.Community;

namespace LSTY.SevenDPanel.Application.Community
{
    public sealed class TeleportFriendRequestUseCases
    {
        private readonly ITeleportFriendRequestStore store;
        private readonly TeleportUseCases teleports;
        private readonly ICommunityPlayerCommandSnapshotProvider players;
        private readonly TimeSpan requestLifetime;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Func<string> idFactory;

        public TeleportFriendRequestUseCases(
            ITeleportFriendRequestStore store,
            TeleportUseCases teleports,
            ICommunityPlayerCommandSnapshotProvider players,
            TimeSpan requestLifetime,
            Func<DateTimeOffset> utcNow,
            Func<string> idFactory)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.teleports = teleports ?? throw new ArgumentNullException(nameof(teleports));
            this.players = players ?? throw new ArgumentNullException(nameof(players));
            if (requestLifetime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(requestLifetime));
            this.requestLifetime = requestLifetime;
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.idFactory = idFactory ?? throw new ArgumentNullException(nameof(idFactory));
        }

        public TeleportFriendRequestCreateResult Request(
            string requesterCrossplatformId,
            string targetSelector)
        {
            requesterCrossplatformId = RequireText(requesterCrossplatformId, nameof(requesterCrossplatformId));
            targetSelector = RequireText(targetSelector, nameof(targetSelector));
            var requester = players.FindOnlineByCrossplatformId(requesterCrossplatformId);
            if (requester == null)
            {
                return new TeleportFriendRequestCreateResult(
                    TeleportFriendRequestCreateStatus.RequesterNotOnline,
                    null);
            }
            var target = players.ResolveOnline(targetSelector);
            if (target == null)
            {
                return new TeleportFriendRequestCreateResult(
                    TeleportFriendRequestCreateStatus.TargetNotOnline,
                    null);
            }
            if (!store.AreFriends(requester.CrossplatformId, target.CrossplatformId))
            {
                return new TeleportFriendRequestCreateResult(
                    TeleportFriendRequestCreateStatus.NotFriends,
                    null);
            }

            var now = GetUtcNow();
            var requestId = Id();
            var request = new TeleportFriendRequest(
                requestId,
                "teleport-friend-request:" + requestId,
                requester.CrossplatformId,
                requester.Player.EntityId,
                requester.Player.Position.WorldId,
                target.CrossplatformId,
                target.Player.EntityId,
                target.Player.Position.WorldId,
                TeleportFriendRequestState.Pending,
                null,
                now,
                now.Add(requestLifetime),
                null,
                0);
            try
            {
                return new TeleportFriendRequestCreateResult(
                    TeleportFriendRequestCreateStatus.Created,
                    store.CreateTeleportFriendRequest(request));
            }
            catch (CommunityConflictException)
            {
                return new TeleportFriendRequestCreateResult(
                    TeleportFriendRequestCreateStatus.Conflict,
                    null);
            }
        }

        public async Task<TeleportFriendRequestResponse> AcceptAsync(
            string targetCrossplatformId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            targetCrossplatformId = RequireText(targetCrossplatformId, nameof(targetCrossplatformId));
            var now = GetUtcNow();
            var request = store.FindPendingTeleportFriendRequest(targetCrossplatformId);
            if (request == null)
            {
                return new TeleportFriendRequestResponse(
                    TeleportFriendRequestResponseStatus.NoPendingRequest,
                    null);
            }
            if (request.ExpiresAtUtc <= now)
            {
                if (!store.TryExpireTeleportFriendRequest(request.RequestId, targetCrossplatformId, now))
                {
                    return new TeleportFriendRequestResponse(
                        TeleportFriendRequestResponseStatus.NoPendingRequest,
                        null);
                }
                return new TeleportFriendRequestResponse(
                    TeleportFriendRequestResponseStatus.Expired,
                    RequirePersisted(request.RequestId));
            }

            var requester = players.FindOnlineByCrossplatformId(request.RequesterCrossplatformId);
            var target = players.FindOnlineByCrossplatformId(request.TargetCrossplatformId);
            if (!Matches(requester, request.RequesterEntityId, request.RequesterWorldId) ||
                !Matches(target, request.TargetEntityId, request.TargetWorldId))
            {
                return new TeleportFriendRequestResponse(
                    TeleportFriendRequestResponseStatus.SnapshotChanged,
                    request);
            }
            if (!store.AreFriends(request.RequesterCrossplatformId, request.TargetCrossplatformId))
            {
                return new TeleportFriendRequestResponse(
                    TeleportFriendRequestResponseStatus.FriendshipChanged,
                    request);
            }

            var operationId = Id();
            if (!store.TryRespondToTeleportFriendRequest(
                    request.RequestId,
                    targetCrossplatformId,
                    true,
                    operationId,
                    now))
            {
                return new TeleportFriendRequestResponse(
                    TeleportFriendRequestResponseStatus.NoPendingRequest,
                    null);
            }
            var accepted = RequirePersisted(request.RequestId);
            var operation = await teleports.TeleportFriendAsync(
                    new TeleportExecutionRequest(
                        accepted.TeleportOperationId!,
                        accepted.IdempotencyKey + ":accept",
                        requester!.Player,
                        "Player",
                        accepted.RequesterCrossplatformId,
                        accepted.RequestId),
                    target!.Player,
                    cancellationToken)
                .ConfigureAwait(false);
            return new TeleportFriendRequestResponse(
                TeleportFriendRequestResponseStatus.Accepted,
                accepted,
                operation);
        }

        public TeleportFriendRequestResponse Reject(string targetCrossplatformId)
        {
            targetCrossplatformId = RequireText(targetCrossplatformId, nameof(targetCrossplatformId));
            var now = GetUtcNow();
            var request = store.FindPendingTeleportFriendRequest(targetCrossplatformId);
            if (request == null)
            {
                return new TeleportFriendRequestResponse(
                    TeleportFriendRequestResponseStatus.NoPendingRequest,
                    null);
            }
            if (request.ExpiresAtUtc <= now)
            {
                if (!store.TryExpireTeleportFriendRequest(request.RequestId, targetCrossplatformId, now))
                {
                    return new TeleportFriendRequestResponse(
                        TeleportFriendRequestResponseStatus.NoPendingRequest,
                        null);
                }
                return new TeleportFriendRequestResponse(
                    TeleportFriendRequestResponseStatus.Expired,
                    RequirePersisted(request.RequestId));
            }
            if (!store.TryRespondToTeleportFriendRequest(
                    request.RequestId,
                    targetCrossplatformId,
                    false,
                    null,
                    now))
            {
                return new TeleportFriendRequestResponse(
                    TeleportFriendRequestResponseStatus.NoPendingRequest,
                    null);
            }
            return new TeleportFriendRequestResponse(
                TeleportFriendRequestResponseStatus.Rejected,
                RequirePersisted(request.RequestId));
        }

        private TeleportFriendRequest RequirePersisted(string requestId) =>
            store.GetTeleportFriendRequest(requestId) ?? throw new InvalidOperationException(
                "The teleport friend request disappeared.");

        private static bool Matches(
            CommunityPlayerCommandSnapshot? player,
            int expectedEntityId,
            string expectedWorldId) => player != null &&
            player.Player.EntityId == expectedEntityId &&
            string.Equals(
                player.Player.Position.WorldId,
                expectedWorldId,
                StringComparison.Ordinal);

        private string Id() => RequireText(idFactory(), nameof(idFactory));

        private DateTimeOffset GetUtcNow()
        {
            var value = utcNow();
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("The community clock must return UTC.");
            return value;
        }

        private static string RequireText(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value!.Trim();
        }
    }
}
