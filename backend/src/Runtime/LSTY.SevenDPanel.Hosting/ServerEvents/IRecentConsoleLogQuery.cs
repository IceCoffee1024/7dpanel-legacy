using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Hosting.ServerEvents
{
    public interface IRecentConsoleLogQuery
    {
        IReadOnlyList<ConsoleLogEventData> ReadRecentConsoleLogs(int limit);
    }

    public sealed class RecentConsoleLogsUnavailableException : Exception
    {
        public RecentConsoleLogsUnavailableException()
            : base("Recent console logs are unavailable.")
        {
        }
    }
}
