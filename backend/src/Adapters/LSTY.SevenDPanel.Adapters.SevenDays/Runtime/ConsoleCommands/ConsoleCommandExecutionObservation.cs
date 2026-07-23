using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.ConsoleCommands;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands
{
    internal sealed class ConsoleCommandExecutionObservation
    {
        public ConsoleCommandExecutionObservation(
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

        public ConsoleCommandAuditEntry ToAuditEntry()
        {
            return new ConsoleCommandAuditEntry(
                AuditId,
                RawCommand,
                Tokens,
                Output,
                Source,
                ActorSubject,
                StartedAtUtc,
                CompletedAtUtc,
                CompletionKind,
                ExceptionType);
        }
    }
}