using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Chat
{
    public sealed class ChatHistoryKeyset
    {
        public ChatHistoryKeyset(DateTimeOffset occurredAtUtc, long rowId)
        {
            if (rowId <= 0) throw new ArgumentOutOfRangeException(nameof(rowId));
            OccurredAtUtc = ChatValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            RowId = rowId;
        }

        public DateTimeOffset OccurredAtUtc { get; }
        public long RowId { get; }
    }

    public sealed class ChatHistoryQuery
    {
        public const int DefaultPageSize = 100;
        public const int MaximumPageSize = 200;

        public ChatHistoryQuery(
            int pageSize,
            string? crossplatformId,
            string? senderName,
            ChatChannel? channel,
            ChatSourceKind? sourceKind,
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc,
            ChatHistoryKeyset? keyset)
        {
            if (pageSize < 1 || pageSize > MaximumPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (startUtc.HasValue) ChatValidation.RequireUtc(startUtc.Value, nameof(startUtc));
            if (endUtc.HasValue) ChatValidation.RequireUtc(endUtc.Value, nameof(endUtc));
            if (startUtc > endUtc)
                throw new ArgumentException("The history start time cannot be after the end time.", nameof(startUtc));
            if (channel.HasValue && !Enum.IsDefined(typeof(ChatChannel), channel.Value))
                throw new ArgumentOutOfRangeException(nameof(channel));
            if (sourceKind.HasValue && !Enum.IsDefined(typeof(ChatSourceKind), sourceKind.Value))
                throw new ArgumentOutOfRangeException(nameof(sourceKind));

            PageSize = pageSize;
            CrossplatformId = ChatValidation.OptionalText(crossplatformId);
            SenderName = ChatValidation.OptionalText(senderName);
            Channel = channel;
            SourceKind = sourceKind;
            StartUtc = startUtc;
            EndUtc = endUtc;
            Keyset = keyset;
        }

        public int PageSize { get; }
        public string? CrossplatformId { get; }
        public string? SenderName { get; }
        public ChatChannel? Channel { get; }
        public ChatSourceKind? SourceKind { get; }
        public DateTimeOffset? StartUtc { get; }
        public DateTimeOffset? EndUtc { get; }
        public ChatHistoryKeyset? Keyset { get; }
    }

    public sealed class ChatHistoryGap
    {
        public required DateTimeOffset StartedAtUtc { get; init; }
        public required DateTimeOffset EndedAtUtc { get; init; }
        public required long DroppedMessageCount { get; init; }
        public required string Reason { get; init; }
    }

    public sealed class ChatHistoryPage
    {
        public ChatHistoryPage(
            IEnumerable<ChatMessage> messages,
            ChatHistoryKeyset? nextKeyset,
            IEnumerable<ChatHistoryGap> gaps)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (gaps == null) throw new ArgumentNullException(nameof(gaps));
            Messages = messages.ToArray();
            NextKeyset = nextKeyset;
            Gaps = gaps.ToArray();
        }

        public IReadOnlyList<ChatMessage> Messages { get; }
        public ChatHistoryKeyset? NextKeyset { get; }
        public IReadOnlyList<ChatHistoryGap> Gaps { get; }
    }

    public sealed class ColoredChatProfileKeyset
    {
        public ColoredChatProfileKeyset(DateTimeOffset updatedAtUtc, string crossplatformId)
        {
            UpdatedAtUtc = ChatValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            CrossplatformId = ChatValidation.RequireBusinessKey(crossplatformId, nameof(crossplatformId));
        }

        public DateTimeOffset UpdatedAtUtc { get; }
        public string CrossplatformId { get; }
    }

    public sealed class ColoredChatProfileQuery
    {
        public const int DefaultPageSize = 50;
        public const int MaximumPageSize = 100;

        public ColoredChatProfileQuery(
            int pageSize,
            string? crossplatformId,
            string? customName,
            string? nameColor,
            string? textColor,
            DateTimeOffset? createdAfterUtc,
            DateTimeOffset? createdBeforeUtc,
            ColoredChatProfileKeyset? keyset)
        {
            if (pageSize < 1 || pageSize > MaximumPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (createdAfterUtc.HasValue)
                ChatValidation.RequireUtc(createdAfterUtc.Value, nameof(createdAfterUtc));
            if (createdBeforeUtc.HasValue)
                ChatValidation.RequireUtc(createdBeforeUtc.Value, nameof(createdBeforeUtc));
            if (createdAfterUtc > createdBeforeUtc)
                throw new ArgumentException("The profile start time cannot be after the end time.", nameof(createdAfterUtc));

            PageSize = pageSize;
            CrossplatformId = ChatValidation.OptionalText(crossplatformId);
            CustomName = ChatValidation.OptionalText(customName);
            NameColor = ChatValidation.NormalizeColor(nameColor);
            TextColor = ChatValidation.NormalizeColor(textColor);
            CreatedAfterUtc = createdAfterUtc;
            CreatedBeforeUtc = createdBeforeUtc;
            Keyset = keyset;
        }

        public int PageSize { get; }
        public string? CrossplatformId { get; }
        public string? CustomName { get; }
        public string? NameColor { get; }
        public string? TextColor { get; }
        public DateTimeOffset? CreatedAfterUtc { get; }
        public DateTimeOffset? CreatedBeforeUtc { get; }
        public ColoredChatProfileKeyset? Keyset { get; }
    }

    public sealed class ColoredChatProfilePage
    {
        public ColoredChatProfilePage(
            IEnumerable<ColoredChatProfile> profiles,
            ColoredChatProfileKeyset? nextKeyset)
        {
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            Profiles = profiles.ToArray();
            NextKeyset = nextKeyset;
        }

        public IReadOnlyList<ColoredChatProfile> Profiles { get; }
        public ColoredChatProfileKeyset? NextKeyset { get; }
    }
}
