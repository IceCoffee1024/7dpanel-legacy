using System;
using System.Threading;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime
{
    public interface IMainThreadDispatcher
    {
        void Post(string operationName, Action action);
    }

    public interface IMainThreadDeadlineScheduler
    {
        IDisposable Schedule(TimeSpan timeout, Action callback);
    }

    public sealed class SystemMainThreadDeadlineScheduler : IMainThreadDeadlineScheduler
    {
        public IDisposable Schedule(TimeSpan timeout, Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return new Timer(_ => callback(), null, timeout, Timeout.InfiniteTimeSpan);
        }
    }
}
