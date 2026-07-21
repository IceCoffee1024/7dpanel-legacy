using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class OnlinePlayerQueryBusyException : Exception
    {
        public OnlinePlayerQueryBusyException()
            : base("The online player query is already in progress.")
        {
        }
    }

    public sealed class OnlinePlayerSnapshotUnavailableException : Exception
    {
        public OnlinePlayerSnapshotUnavailableException()
            : base("The online player snapshot is unavailable.")
        {
        }
    }
}
