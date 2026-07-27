using System;
using LSTY.SevenDPanel.Domain.Community;

namespace LSTY.SevenDPanel.Application.Community
{
    public sealed class TeleportFriendRequest
    {
        public TeleportFriendRequest(
            string requestId,
            string idempotencyKey,
            string requesterCrossplatformId,
            int requesterEntityId,
            string requesterWorldId,
            string targetCrossplatformId,
            int targetEntityId,
            string targetWorldId,
            TeleportFriendRequestState state,
            string? teleportOperationId,
            DateTimeOffset createdAtUtc,
            DateTimeOffset expiresAtUtc,
            DateTimeOffset? respondedAtUtc,
            long rowVersion)
        {
            CommunityModelValidation.RequireDefined(state, nameof(state));
            RequestId = CommunityModelValidation.RequireText(requestId, nameof(requestId));
            IdempotencyKey = CommunityModelValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            RequesterCrossplatformId = CommunityModelValidation.RequireText(
                requesterCrossplatformId, nameof(requesterCrossplatformId));
            if (requesterEntityId < 0) throw new ArgumentOutOfRangeException(nameof(requesterEntityId));
            RequesterEntityId = requesterEntityId;
            RequesterWorldId = CommunityModelValidation.RequireText(requesterWorldId, nameof(requesterWorldId));
            TargetCrossplatformId = CommunityModelValidation.RequireText(
                targetCrossplatformId, nameof(targetCrossplatformId));
            if (string.Equals(RequesterCrossplatformId, TargetCrossplatformId, StringComparison.Ordinal))
                throw new ArgumentException("A player cannot request teleportation to itself.", nameof(targetCrossplatformId));
            if (targetEntityId < 0) throw new ArgumentOutOfRangeException(nameof(targetEntityId));
            TargetEntityId = targetEntityId;
            TargetWorldId = CommunityModelValidation.RequireText(targetWorldId, nameof(targetWorldId));
            CreatedAtUtc = CommunityModelValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            ExpiresAtUtc = CommunityModelValidation.RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
            if (ExpiresAtUtc <= CreatedAtUtc) throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
            if (respondedAtUtc.HasValue)
            {
                RespondedAtUtc = CommunityModelValidation.RequireUtc(
                    respondedAtUtc.Value, nameof(respondedAtUtc));
                if (RespondedAtUtc.Value < CreatedAtUtc)
                    throw new ArgumentOutOfRangeException(nameof(respondedAtUtc));
            }
            TeleportOperationId = CommunityModelValidation.OptionalText(teleportOperationId);
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            ValidateResponse(state, TeleportOperationId, RespondedAtUtc);
            State = state;
            RowVersion = rowVersion;
        }

        public string RequestId { get; }
        public string IdempotencyKey { get; }
        public string RequesterCrossplatformId { get; }
        public int RequesterEntityId { get; }
        public string RequesterWorldId { get; }
        public string TargetCrossplatformId { get; }
        public int TargetEntityId { get; }
        public string TargetWorldId { get; }
        public TeleportFriendRequestState State { get; }
        public string? TeleportOperationId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset ExpiresAtUtc { get; }
        public DateTimeOffset? RespondedAtUtc { get; }
        public long RowVersion { get; }

        private static void ValidateResponse(
            TeleportFriendRequestState state,
            string? teleportOperationId,
            DateTimeOffset? respondedAtUtc)
        {
            if (state == TeleportFriendRequestState.Pending)
            {
                if (respondedAtUtc.HasValue || teleportOperationId != null)
                    throw new ArgumentException("A pending request cannot have a response.");
                return;
            }

            if (!respondedAtUtc.HasValue)
                throw new ArgumentException("A final request state requires a response time.", nameof(respondedAtUtc));
            if (state == TeleportFriendRequestState.Accepted)
            {
                if (teleportOperationId == null)
                    throw new ArgumentException("An accepted request requires a teleport operation.", nameof(teleportOperationId));
                return;
            }
            if (teleportOperationId != null)
                throw new ArgumentException("Only an accepted request can have a teleport operation.", nameof(teleportOperationId));
        }
    }

    public enum TeleportFriendRequestCreateStatus
    {
        Created,
        RequesterNotOnline,
        TargetNotOnline,
        NotFriends,
        Conflict
    }

    public sealed class TeleportFriendRequestCreateResult
    {
        public TeleportFriendRequestCreateResult(
            TeleportFriendRequestCreateStatus status,
            TeleportFriendRequest? request)
        {
            CommunityModelValidation.RequireDefined(status, nameof(status));
            if ((status == TeleportFriendRequestCreateStatus.Created) != (request != null))
                throw new ArgumentException("Only a created result can include a request.", nameof(request));
            Status = status;
            Request = request;
        }

        public TeleportFriendRequestCreateStatus Status { get; }
        public TeleportFriendRequest? Request { get; }
    }

    public enum TeleportFriendRequestResponseStatus
    {
        Accepted,
        Rejected,
        Expired,
        NoPendingRequest,
        SnapshotChanged,
        FriendshipChanged
    }

    public sealed class TeleportFriendRequestResponse
    {
        public TeleportFriendRequestResponse(
            TeleportFriendRequestResponseStatus status,
            TeleportFriendRequest? request,
            TeleportOperation? teleportOperation = null)
        {
            CommunityModelValidation.RequireDefined(status, nameof(status));
            Status = status;
            Request = request;
            TeleportOperation = teleportOperation;
        }

        public TeleportFriendRequestResponseStatus Status { get; }
        public TeleportFriendRequest? Request { get; }
        public TeleportOperation? TeleportOperation { get; }
    }
}
