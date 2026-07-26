using System;

namespace LSTY.SevenDPanel.Application.Chat
{
    public enum ChatChannel
    {
        Global,
        Friends,
        Party,
        Whisper,
        Unknown
    }

    public enum ChatSourceKind
    {
        Player,
        Administrator,
        System
    }

    public sealed class ChatMessage
    {
        public required long Sequence { get; init; }
        public required DateTimeOffset OccurredAtUtc { get; init; }
        public required int EntityId { get; init; }
        public string? CrossplatformId { get; init; }
        public required string SenderName { get; init; }
        public required ChatChannel Channel { get; init; }
        public required ChatSourceKind SourceKind { get; init; }
        public required string Message { get; init; }
    }
}
