using System;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Chat
{
    public interface IGameChatCommandAuditTrail
    {
        long Begin(GameChatCommandAuditIntent intent);
        void Complete(long auditId, GameChatCommandAuditCompletion completion);
    }

    public sealed class GameChatCommandAuditIntent
    {
        public GameChatCommandAuditIntent(
            string actorSubject,
            string commandName,
            string invokedName,
            DateTimeOffset occurredAtUtc)
        {
            ActorSubject = Require(actorSubject, nameof(actorSubject));
            CommandName = RequireToken(commandName, nameof(commandName));
            InvokedName = RequireToken(invokedName, nameof(invokedName));
            if (occurredAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", nameof(occurredAtUtc));
            OccurredAtUtc = occurredAtUtc;
        }

        public string ActorSubject { get; }
        public string CommandName { get; }
        public string InvokedName { get; }
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

    public sealed class GameChatCommandAuditCompletion
    {
        public GameChatCommandAuditCompletion(string resultCode, bool isHandled)
        {
            if (string.IsNullOrWhiteSpace(resultCode))
                throw new ArgumentException("A non-empty result code is required.", nameof(resultCode));
            ResultCode = resultCode.Trim();
            IsHandled = isHandled;
        }

        public string ResultCode { get; }
        public bool IsHandled { get; }
    }
}
