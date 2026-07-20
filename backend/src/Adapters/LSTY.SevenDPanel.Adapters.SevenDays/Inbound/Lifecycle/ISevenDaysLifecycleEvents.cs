using System;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Lifecycle
{
    internal interface ISevenDaysLifecycleEvents
    {
        IDisposable SubscribeGameStartDone(Action handler);
        IDisposable SubscribeWorldShuttingDown(Action handler);
        IDisposable SubscribeGameShutdown(Action handler);
    }
}
