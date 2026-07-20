using System;
using System.Threading;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Lifecycle
{
    internal sealed class SevenDaysModEvents : ISevenDaysLifecycleEvents
    {
        public IDisposable SubscribeGameStartDone(Action handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ModEvents.ModEventHandlerDelegate<ModEvents.SGameStartDoneData> callback =
                delegate(ref ModEvents.SGameStartDoneData data) { handler(); };
            return Subscribe(ModEvents.GameStartDone, callback);
        }

        public IDisposable SubscribeWorldShuttingDown(Action handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ModEvents.ModEventHandlerDelegate<ModEvents.SWorldShuttingDownData> callback =
                delegate(ref ModEvents.SWorldShuttingDownData data) { handler(); };
            return Subscribe(ModEvents.WorldShuttingDown, callback);
        }

        public IDisposable SubscribeGameShutdown(Action handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ModEvents.ModEventHandlerDelegate<ModEvents.SGameShutdownData> callback =
                delegate(ref ModEvents.SGameShutdownData data) { handler(); };
            return Subscribe(ModEvents.GameShutdown, callback);
        }

        private static IDisposable Subscribe<TData>(
            ModEvents.ModEvent<TData> modEvent,
            ModEvents.ModEventHandlerDelegate<TData> callback)
            where TData : struct
        {
            var subscription = new Subscription(() => modEvent.UnregisterHandler(callback));
            try
            {
                modEvent.RegisterHandler(callback);
                return subscription;
            }
            catch
            {
                try { subscription.Dispose(); } catch { }
                throw;
            }
        }

        private sealed class Subscription : IDisposable
        {
            private Action? unregister;

            public Subscription(Action unregister)
            {
                this.unregister = unregister;
            }

            public void Dispose()
            {
                var current = Interlocked.Exchange(ref unregister, null);
                if (current != null) current();
            }
        }
    }
}
