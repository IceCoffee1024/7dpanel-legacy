using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Hosting.ServerEvents;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs
{
    public sealed class ServerEventLiveWindow
    {
        private readonly object sync = new object();
        private readonly int capacity;
        private readonly Queue<ServerEvent> entries;
        private long nextSequence = 1L;

        public ServerEventLiveWindow(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
            entries = new Queue<ServerEvent>(capacity);
        }

        public ServerEvent AppendConsoleLog(ConsoleLogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            return Append(sequence => ServerEvent.CreateConsoleLog(
                sequence,
                entry.FormattedMessage,
                entry.Message,
                entry.Trace,
                entry.LogType.ToString().ToLowerInvariant(),
                entry.Timestamp,
                entry.UptimeMilliseconds));
        }

        public ServerEvent AppendGameReady(DateTime occurredAtUtc) =>
            Append(sequence => ServerEvent.CreateGameReady(sequence, occurredAtUtc));

        public ServerEvent AppendServerStopping(DateTime occurredAtUtc) =>
            Append(sequence => ServerEvent.CreateServerStopping(sequence, occurredAtUtc));

        public ServerEventWindowReadResult ReadAfter(long? afterSequence, int limit)
        {
            if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));

            lock (sync)
            {
                if (entries.Count == 0)
                {
                    return new ServerEventWindowReadResult(
                        Array.Empty<ServerEvent>(),
                        null,
                        null,
                        false);
                }

                var oldestSequence = entries.Peek().Sequence;
                var latestSequence = entries.Last().Sequence;
                var hasGap = afterSequence.HasValue &&
                    afterSequence.Value < oldestSequence - 1L;
                var result = entries
                    .Where(entry => !afterSequence.HasValue || entry.Sequence > afterSequence.Value)
                    .Take(limit)
                    .ToArray();

                return new ServerEventWindowReadResult(
                    result,
                    oldestSequence,
                    latestSequence,
                    hasGap);
            }
        }

        private ServerEvent Append(Func<long, ServerEvent> create)
        {
            lock (sync)
            {
                var retainedEvent = create(nextSequence);
                nextSequence = checked(nextSequence + 1L);
                if (entries.Count == capacity) entries.Dequeue();
                entries.Enqueue(retainedEvent);
                return retainedEvent;
            }
        }
    }
}
