using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application.ConsoleCommands
{
    public enum ConsoleCommandCompletionKind
    {
        Completed,
        Threw
    }

    public sealed class ConsoleCommandAuditEntry
    {
        public ConsoleCommandAuditEntry(
            string auditId,
            string rawCommand,
            IEnumerable<string> tokens,
            IEnumerable<string> output,
            string source,
            string? actorSubject,
            DateTimeOffset startedAtUtc,
            DateTimeOffset completedAtUtc,
            ConsoleCommandCompletionKind completionKind,
            string? exceptionType)
        {
            if (string.IsNullOrWhiteSpace(auditId))
                throw new ArgumentException("An audit identifier is required.", nameof(auditId));
            if (rawCommand == null) throw new ArgumentNullException(nameof(rawCommand));
            if (tokens == null) throw new ArgumentNullException(nameof(tokens));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("A command source is required.", nameof(source));
            if (completedAtUtc < startedAtUtc)
                throw new ArgumentOutOfRangeException(
                    nameof(completedAtUtc),
                    "The completion time cannot precede the start time.");
            if (completionKind == ConsoleCommandCompletionKind.Completed && exceptionType != null)
                throw new ArgumentException(
                    "Completed commands cannot have an exception type.",
                    nameof(exceptionType));
            if (completionKind == ConsoleCommandCompletionKind.Threw &&
                string.IsNullOrWhiteSpace(exceptionType))
            {
                throw new ArgumentException(
                    "Thrown commands require an exception type.",
                    nameof(exceptionType));
            }

            AuditId = auditId;
            RawCommand = rawCommand;
            Tokens = tokens.ToArray();
            Output = output.ToArray();
            Source = source;
            ActorSubject = actorSubject;
            StartedAtUtc = startedAtUtc;
            CompletedAtUtc = completedAtUtc;
            CompletionKind = completionKind;
            ExceptionType = exceptionType;
        }

        public string AuditId { get; }
        public string RawCommand { get; }
        public IReadOnlyList<string> Tokens { get; }
        public IReadOnlyList<string> Output { get; }
        public string Source { get; }
        public string? ActorSubject { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public ConsoleCommandCompletionKind CompletionKind { get; }
        public string? ExceptionType { get; }
    }
}