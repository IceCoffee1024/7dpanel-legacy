using System;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Chat
{
    public interface IGameChatCommandAuditTrail
    {
        void Record(GameChatCommandAuditEntry entry);
    }

    public sealed class GameChatCommandAuditEntry
    {
        public GameChatCommandAuditEntry(
            string actorSubject,
            string commandName,
            string invokedName,
            string resultCode,
            bool isHandled,
            DateTimeOffset occurredAtUtc)
        {
            ActorSubject = Require(actorSubject, nameof(actorSubject));
            CommandName = RequireToken(commandName, nameof(commandName));
            InvokedName = RequireToken(invokedName, nameof(invokedName));
            ResultCode = Require(resultCode, nameof(resultCode));
            if (occurredAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", nameof(occurredAtUtc));
            IsHandled = isHandled;
            OccurredAtUtc = occurredAtUtc;
        }

        public string ActorSubject { get; }
        public string CommandName { get; }
        public string InvokedName { get; }
        public string ResultCode { get; }
        public bool IsHandled { get; }
        public DateTimeOffset OccurredAtUtc { get; }

        private static string Require(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        private static string RequireToken(string value, string parameterName)
        {
            var normalized = Require(value, parameterName);
            if (normalized.Any(char.IsWhiteSpace))
                throw new ArgumentException("A command token cannot contain whitespace.", parameterName);
            return normalized;
        }
    }
}
