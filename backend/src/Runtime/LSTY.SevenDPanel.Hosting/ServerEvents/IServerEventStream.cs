using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Hosting.ServerEvents
{
    public interface IServerEventStream
    {
        IReadOnlyList<ServerEvent> ReadAfter(
            long? afterSequence,
            int limit,
            out bool hasGap);

        bool TrySubscribe(int capacity, out IServerEventSubscription? subscription);
    }

    public interface IServerEventSubscription : IDisposable
    {
        bool IsOverflowed { get; }

        Task<ServerEvent?> ReadAsync(CancellationToken cancellationToken);
    }
}
