using System;

namespace LSTY.SevenDPanel.Hosting.ServerEvents
{
    public static class ServerEventNames
    {
        public const string ChatMessage = "chat-message";
        public const string ConsoleLog = "console-log";
        public const string GameReady = "game-ready";
        public const string ServerStopping = "server-stopping";
    }

    public sealed class ServerEvent
    {
        private ServerEvent(long sequence, string eventName, object data)
        {
            if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            EventName = eventName;
            Data = data;
        }

        public long Sequence { get; }
        public string EventName { get; }
        public object Data { get; }

        public static ServerEvent CreateConsoleLog(
            long sequence,
            string? formattedMessage,
            string? message,
            string? trace,
            string logType,
            DateTime timestamp,
            long uptimeMilliseconds) =>
            new ServerEvent(
                sequence,
                ServerEventNames.ConsoleLog,
                new ConsoleLogEventData(
                    sequence,
                    formattedMessage,
                    message,
                    trace,
                    logType,
                    timestamp,
                    uptimeMilliseconds));

        public static ServerEvent CreateChatMessage(
            long sequence,
            DateTimeOffset occurredAtUtc,
            int entityId,
            string? crossplatformId,
            string senderName,
            string channel,
            string sourceKind,
            string message) =>
            new ServerEvent(
                sequence,
                ServerEventNames.ChatMessage,
                new ChatMessageEventData(
                    sequence,
                    occurredAtUtc,
                    entityId,
                    crossplatformId,
                    senderName,
                    channel,
                    sourceKind,
                    message));

        public static ServerEvent CreateGameReady(long sequence, DateTime occurredAtUtc) =>
            new ServerEvent(
                sequence,
                ServerEventNames.GameReady,
                new GameReadyEventData(sequence, occurredAtUtc));

        public static ServerEvent CreateServerStopping(long sequence, DateTime occurredAtUtc) =>
            new ServerEvent(
                sequence,
                ServerEventNames.ServerStopping,
                new ServerStoppingEventData(sequence, occurredAtUtc));
    }

    public sealed class ChatMessageEventData
    {
        internal ChatMessageEventData(
            long sequence,
            DateTimeOffset occurredAtUtc,
            int entityId,
            string? crossplatformId,
            string senderName,
            string channel,
            string sourceKind,
            string message)
        {
            Sequence = sequence;
            OccurredAtUtc = occurredAtUtc;
            EntityId = entityId;
            CrossplatformId = crossplatformId;
            SenderName = senderName ?? throw new ArgumentNullException(nameof(senderName));
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            SourceKind = sourceKind ?? throw new ArgumentNullException(nameof(sourceKind));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public long Sequence { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public int EntityId { get; }
        public string? CrossplatformId { get; }
        public string SenderName { get; }
        public string Channel { get; }
        public string SourceKind { get; }
        public string Message { get; }
    }

    public sealed class ConsoleLogEventData
    {
        internal ConsoleLogEventData(
            long sequence,
            string? formattedMessage,
            string? message,
            string? trace,
            string logType,
            DateTime timestamp,
            long uptimeMilliseconds)
        {
            Sequence = sequence;
            FormattedMessage = formattedMessage;
            Message = message;
            Trace = trace;
            LogType = logType ?? throw new ArgumentNullException(nameof(logType));
            Timestamp = timestamp;
            UptimeMilliseconds = uptimeMilliseconds;
        }

        public long Sequence { get; }
        public string? FormattedMessage { get; }
        public string? Message { get; }
        public string? Trace { get; }
        public string LogType { get; }
        public DateTime Timestamp { get; }
        public long UptimeMilliseconds { get; }
    }

    public sealed class GameReadyEventData
    {
        internal GameReadyEventData(long sequence, DateTime occurredAtUtc)
        {
            Sequence = sequence;
            OccurredAtUtc = occurredAtUtc;
        }

        public long Sequence { get; }
        public DateTime OccurredAtUtc { get; }
    }

    public sealed class ServerStoppingEventData
    {
        internal ServerStoppingEventData(long sequence, DateTime occurredAtUtc)
        {
            Sequence = sequence;
            OccurredAtUtc = occurredAtUtc;
        }

        public long Sequence { get; }
        public DateTime OccurredAtUtc { get; }
    }
}
