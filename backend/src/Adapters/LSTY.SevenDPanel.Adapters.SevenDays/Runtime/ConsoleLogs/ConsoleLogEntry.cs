using System;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs
{
    public sealed class ConsoleLogEntry
    {
        internal ConsoleLogEntry(
            string? formattedMessage,
            string? message,
            string? trace,
            ConsoleLogType logType,
            DateTime timestamp,
            long uptimeMilliseconds)
        {
            FormattedMessage = formattedMessage;
            Message = message;
            Trace = trace;
            LogType = logType;
            Timestamp = timestamp;
            UptimeMilliseconds = uptimeMilliseconds;
        }

        private ConsoleLogEntry(long sequence, ConsoleLogEntry source)
        {
            Sequence = sequence;
            FormattedMessage = source.FormattedMessage;
            Message = source.Message;
            Trace = source.Trace;
            LogType = source.LogType;
            Timestamp = source.Timestamp;
            UptimeMilliseconds = source.UptimeMilliseconds;
        }

        internal ConsoleLogEntry WithSequence(long sequence) => new ConsoleLogEntry(sequence, this);

        public long Sequence { get; }
        public string? FormattedMessage { get; }
        public string? Message { get; }
        public string? Trace { get; }
        public ConsoleLogType LogType { get; }
        public DateTime Timestamp { get; }
        public long UptimeMilliseconds { get; }
    }
}
