using System.Collections.Generic;
using LSTY.SevenDPanel.Hosting.ServerEvents;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs
{
    public sealed class ServerEventWindowReadResult
    {
        internal ServerEventWindowReadResult(
            IReadOnlyList<ServerEvent> entries,
            long? oldestSequence,
            long? latestSequence,
            bool hasGap)
        {
            Entries = entries;
            OldestSequence = oldestSequence;
            LatestSequence = latestSequence;
            HasGap = hasGap;
        }

        public IReadOnlyList<ServerEvent> Entries { get; }
        public long? OldestSequence { get; }
        public long? LatestSequence { get; }
        public bool HasGap { get; }
    }
}
