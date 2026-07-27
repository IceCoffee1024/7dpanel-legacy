using System;
using System.Collections.Generic;
using System.Threading;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Chat;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Chat
{
    public sealed class SevenDaysChatRuntime : IModRuntime, IDisposable
    {
        private readonly ChatRuntimeState runtimeState;
        private readonly ChatHistoryWriteService writer;
        private readonly Func<IDisposable> subscribe;
        private readonly IModRuntime inner;
        private readonly ChatMuteExpiryService? muteExpiry;
        private readonly object gate = new object();
        private IDisposable? subscription;
        private bool started;
        private bool stopped;

        public SevenDaysChatRuntime(
            ChatRuntimeState runtimeState,
            ChatHistoryWriteService writer,
            SevenDaysChatMessageCoordinator coordinator,
            IModRuntime inner,
            ChatMuteExpiryService? muteExpiry = null)
            : this(runtimeState, writer, () => Subscribe(coordinator), inner, muteExpiry) { }

        internal SevenDaysChatRuntime(
            ChatRuntimeState runtimeState,
            ChatHistoryWriteService writer,
            Func<IDisposable> subscribe,
            IModRuntime inner,
            ChatMuteExpiryService? muteExpiry = null)
        {
            this.runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
            this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
            this.subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.muteExpiry = muteExpiry;
        }

        public void Start()
        {
            lock (gate)
            {
                if (started || stopped) return;
                try
                {
                    writer.Start();
                    runtimeState.Load();
                    muteExpiry?.Start();
                    subscription = subscribe();
                    inner.Start();
                    started = true;
                }
                catch
                {
                    try { subscription?.Dispose(); } catch { }
                    try { writer.Stop(); } catch { }
                    try { muteExpiry?.Stop(); } catch { }
                    try { inner.Stop(); } catch { }
                    stopped = true;
                    throw;
                }
            }
        }

        public void MarkGameReady() => inner.MarkGameReady();

        public void Stop()
        {
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
                started = false;
                var failures = new List<Exception>();
                try { Interlocked.Exchange(ref subscription, null)?.Dispose(); } catch (Exception exception) { failures.Add(exception); }
                try { writer.Stop(); } catch (Exception exception) { failures.Add(exception); }
                try { muteExpiry?.Stop(); } catch (Exception exception) { failures.Add(exception); }
                try { inner.Stop(); } catch (Exception exception) { failures.Add(exception); }
                if (failures.Count > 0) throw new AggregateException(failures);
            }
        }

        public void Dispose() => Stop();

        private static IDisposable Subscribe(SevenDaysChatMessageCoordinator coordinator)
        {
            if (coordinator == null) throw new ArgumentNullException(nameof(coordinator));
            ModEvents.ModEventInterruptibleHandlerDelegate<ModEvents.SChatMessageData> callback = coordinator.Handle;
            ModEvents.ChatMessage.RegisterHandler(callback);
            return new Subscription(() => ModEvents.ChatMessage.UnregisterHandler(callback));
        }

        private sealed class Subscription : IDisposable
        {
            private Action? unsubscribe;
            public Subscription(Action unsubscribe) => this.unsubscribe = unsubscribe;
            public void Dispose() => Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
        }
    }
}
