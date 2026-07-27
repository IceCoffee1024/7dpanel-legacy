using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Chat
{
    public sealed class ChatMuteExpiryService : IModRuntime, IDisposable
    {
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
        public const int MaximumDeletes = 100;

        private readonly IChatMuteExpirationStore store;
        private readonly IChatMuteRuntimeConfiguration runtime;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Action<string> log;
        private Timer? timer;
        private int stopped;

        public ChatMuteExpiryService(
            IChatMuteExpirationStore store,
            IChatMuteRuntimeConfiguration runtime,
            Func<DateTimeOffset> utcNow,
            Action<string>? log = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.log = log ?? (_ => { });
        }

        public void Start()
        {
            if (Volatile.Read(ref stopped) != 0 || timer != null) return;
            timer = new Timer(_ => RunOnce(), null, Interval, Interval);
        }

        public void MarkGameReady() { }

        public void Stop()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            Interlocked.Exchange(ref timer, null)?.Dispose();
        }

        public void Dispose() => Stop();

        public void RunOnce()
        {
            try
            {
                ChatMuteUseCases.ExecuteSerialized(() =>
                {
                    var now = utcNow();
                    if (now.Offset != TimeSpan.Zero) throw new ArgumentException("A UTC timestamp is required.", nameof(utcNow));
                    var records = store.Expire(now, MaximumDeletes);
                    runtime.ReplaceMuteSnapshot(new ReadOnlyDictionary<string, ChatMuteRecord>(
                        records.ToDictionary(record => record.CrossplatformId, StringComparer.Ordinal)));
                    return 0;
                });
            }
            catch
            {
                try { log("Chat mute expiry failed."); } catch { }
            }
        }
    }
}
