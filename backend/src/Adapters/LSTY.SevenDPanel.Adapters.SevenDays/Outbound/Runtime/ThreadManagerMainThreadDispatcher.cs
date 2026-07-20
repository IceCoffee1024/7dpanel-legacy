using System;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime
{
    public sealed class ThreadManagerMainThreadDispatcher : IMainThreadDispatcher
    {
        public void Post(string operationName, Action action)
        {
            if (string.IsNullOrWhiteSpace(operationName)) throw new ArgumentException("An operation name is required.", nameof(operationName));
            if (action == null) throw new ArgumentNullException(nameof(action));
            ThreadManager.AddSingleTaskMainThread(operationName, action);
        }
    }
}
