using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Chat
{
    public enum ChatMuteOperationKind
    {
        Create,
        Update,
        Release,
        Expire
    }

    public sealed class ChatMuteRecord
    {
        public ChatMuteRecord(
            string crossplatformId,
            string? displayName,
            string reason,
            DateTimeOffset? mutedUntilUtc,
            string createdBy,
            DateTimeOffset createdAtUtc,
            string updatedBy,
            DateTimeOffset updatedAtUtc)
        {
            CrossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            DisplayName = Normalize(displayName);
            Reason = RequireText(reason, nameof(reason));
            if (mutedUntilUtc.HasValue) RequireUtc(mutedUntilUtc.Value, nameof(mutedUntilUtc));
            CreatedBy = RequireText(createdBy, nameof(createdBy));
            RequireUtc(createdAtUtc, nameof(createdAtUtc));
            UpdatedBy = RequireText(updatedBy, nameof(updatedBy));
            RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (updatedAtUtc < createdAtUtc)
                throw new ArgumentException("The update time cannot precede creation.", nameof(updatedAtUtc));

            MutedUntilUtc = mutedUntilUtc;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
        }

        public string CrossplatformId { get; }
        public string? DisplayName { get; }
        public string Reason { get; }
        public DateTimeOffset? MutedUntilUtc { get; }
        public string CreatedBy { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public string UpdatedBy { get; }
        public DateTimeOffset UpdatedAtUtc { get; }

        public bool IsActiveAt(DateTimeOffset nowUtc)
        {
            RequireUtc(nowUtc, nameof(nowUtc));
            return !MutedUntilUtc.HasValue || MutedUntilUtc.Value > nowUtc;
        }

        internal static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        internal static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        internal static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }
    }

    public sealed class ChatMuteOperation
    {
        public ChatMuteOperation(
            string operationId,
            ChatMuteOperationKind kind,
            string targetCrossplatformId,
            string? actorSubject,
            DateTimeOffset occurredAtUtc,
            string result,
            string? correlationId,
            DateTimeOffset? mutedUntilUtc,
            string? reason)
        {
            if (!Guid.TryParseExact(operationId, "D", out _))
                throw new ArgumentException("A canonical GUID operation identifier is required.", nameof(operationId));
            if (!Enum.IsDefined(typeof(ChatMuteOperationKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            OperationId = operationId;
            Kind = kind;
            TargetCrossplatformId = ChatMuteRecord.RequireText(targetCrossplatformId, nameof(targetCrossplatformId));
            ActorSubject = ChatMuteRecord.Normalize(actorSubject);
            ChatMuteRecord.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            OccurredAtUtc = occurredAtUtc;
            Result = ChatMuteRecord.RequireText(result, nameof(result));
            CorrelationId = ChatMuteRecord.Normalize(correlationId);
            if (mutedUntilUtc.HasValue) ChatMuteRecord.RequireUtc(mutedUntilUtc.Value, nameof(mutedUntilUtc));
            MutedUntilUtc = mutedUntilUtc;
            Reason = ChatMuteRecord.Normalize(reason);
        }

        public string OperationId { get; }
        public ChatMuteOperationKind Kind { get; }
        public string TargetCrossplatformId { get; }
        public string? ActorSubject { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public string Result { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset? MutedUntilUtc { get; }
        public string? Reason { get; }
    }

    public sealed class ChatMuteCursor
    {
        public ChatMuteCursor(DateTimeOffset updatedAtUtc, string crossplatformId)
        {
            ChatMuteRecord.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            UpdatedAtUtc = updatedAtUtc;
            CrossplatformId = ChatMuteRecord.RequireText(crossplatformId, nameof(crossplatformId));
        }

        public DateTimeOffset UpdatedAtUtc { get; }
        public string CrossplatformId { get; }
    }

    public sealed class ChatMutePage
    {
        public ChatMutePage(IEnumerable<ChatMuteRecord> records, ChatMuteCursor? nextCursor)
        {
            Records = (records ?? throw new ArgumentNullException(nameof(records))).ToArray();
            NextCursor = nextCursor;
        }

        public IReadOnlyList<ChatMuteRecord> Records { get; }
        public ChatMuteCursor? NextCursor { get; }
    }

    public interface IChatMuteStore
    {
        ChatMutePage GetPage(int pageSize, ChatMuteCursor? cursor);
        ChatMuteRecord? Find(string crossplatformId);
        IReadOnlyList<ChatMuteRecord> Create(ChatMuteRecord record, ChatMuteOperation operation);
        IReadOnlyList<ChatMuteRecord> Update(ChatMuteRecord record, ChatMuteOperation operation);
        IReadOnlyList<ChatMuteRecord> Release(string crossplatformId, ChatMuteOperation operation);
    }

    public interface IChatMuteExpirationStore
    {
        IReadOnlyList<ChatMuteRecord> Expire(DateTimeOffset nowUtc, int maximumDeletes);
    }
}
