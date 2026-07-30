using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.GeoIp;

namespace LSTY.SevenDPanel.Adapters.Local.GeoIp
{
    public sealed class GeoIpRefreshWorker : IGeoIpRefreshQueue, IGeoIpRefreshDiagnostics, IDisposable
    {
        private readonly object sync = new object();
        private readonly IGeoIpAccessPolicyStore store;
        private readonly Dictionary<string, IGeoIpProvider> providers;
        private readonly BlockingCollection<GeoIpRefreshRequest> queue;
        private readonly TimeSpan successTtl;
        private readonly TimeSpan failureTtl;
        private readonly TimeSpan drainTimeout;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly ManualResetEventSlim consumerStarted = new ManualResetEventSlim(false);
        private Task? consumer;
        private bool accepting;
        private bool stopped;
        private long rejectedCount;
        private DateTimeOffset? lastCompletedAtUtc;
        private GeoIpLookupStatus? lastLookupStatus;

        public GeoIpRefreshWorker(
            IGeoIpAccessPolicyStore store,
            IEnumerable<IGeoIpProvider> providers,
            int capacity = 64,
            TimeSpan? successTtl = null,
            TimeSpan? failureTtl = null,
            TimeSpan? drainTimeout = null,
            Func<DateTimeOffset>? utcClock = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (providers == null) throw new ArgumentNullException(nameof(providers));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.providers = providers.ToDictionary(
                provider => provider.Metadata.Provider,
                StringComparer.Ordinal);
            queue = new BlockingCollection<GeoIpRefreshRequest>(capacity);
            this.successTtl = successTtl ?? TimeSpan.FromHours(24);
            this.failureTtl = failureTtl ?? TimeSpan.FromMinutes(5);
            this.drainTimeout = drainTimeout ?? TimeSpan.FromSeconds(5);
            this.utcClock = utcClock ?? (() => DateTimeOffset.UtcNow);
        }

        public void Start()
        {
            var waitForConsumer = false;
            lock (sync)
            {
                if (stopped) throw new ObjectDisposedException(nameof(GeoIpRefreshWorker));
                if (consumer != null) return;
                accepting = true;
                consumer = Task.Factory.StartNew(
                    Consume,
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                waitForConsumer = true;
            }
            if (waitForConsumer) consumerStarted.Wait();
        }

        public bool TryWrite(GeoIpRefreshRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            lock (sync)
            {
                if (!accepting || stopped || !IsCurrentAndEnabled(request))
                {
                    rejectedCount++;
                    return false;
                }
                if (queue.TryAdd(request)) return true;
                rejectedCount++;
                return false;
            }
        }

        public GeoIpRefreshDiagnostics GetDiagnostics()
        {
            lock (sync)
            {
                return new GeoIpRefreshDiagnostics(
                    accepting,
                    queue.Count,
                    rejectedCount,
                    lastCompletedAtUtc,
                    lastLookupStatus,
                    providers.Values.Select(provider => provider.Metadata).ToArray());
            }
        }

        public void Stop()
        {
            Task? candidate;
            lock (sync)
            {
                if (stopped) return;
                stopped = true;
                accepting = false;
                queue.CompleteAdding();
                candidate = consumer;
            }
            if (candidate != null && !candidate.Wait(drainTimeout))
            {
                cancellation.Cancel();
                throw new TimeoutException("The GeoIP refresh queue did not drain before shutdown.");
            }
        }

        public void Dispose()
        {
            try { Stop(); } finally
            {
                cancellation.Dispose();
                consumerStarted.Dispose();
                queue.Dispose();
                foreach (var provider in providers.Values) provider.Dispose();
            }
        }

        private void Consume()
        {
            consumerStarted.Set();
            foreach (var request in queue.GetConsumingEnumerable(cancellation.Token))
            {
                if (!IsCurrentAndEnabled(request)) continue;
                GeoIpLookupResult result;
                if (!providers.TryGetValue(request.Provider, out var provider))
                {
                    result = GeoIpLookupResult.Unavailable(
                        request.Provider,
                        GeoIpLookupFailure.Unexpected);
                }
                else
                {
                    try
                    {
                        result = provider.LookupAsync(
                                request.CanonicalIp,
                                cancellation.Token)
                            .GetAwaiter()
                            .GetResult();
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        return;
                    }
                    catch
                    {
                        result = GeoIpLookupResult.Unavailable(
                            request.Provider,
                            GeoIpLookupFailure.Unexpected,
                            provider.Metadata.SourceVersion);
                    }
                }

                var completedAt = utcClock();
                var ttl = result.Status == GeoIpLookupStatus.Unavailable
                    ? failureTtl
                    : successTtl;
                try
                {
                    store.UpsertCache(new GeoIpCacheEntry(
                        request.CanonicalIp,
                        result.Status.ToString(),
                        result.CountryCode,
                        result.Source,
                        result.SourceVersion,
                        completedAt,
                        completedAt.Add(ttl)));
                }
                catch
                {
                }
                lock (sync)
                {
                    lastCompletedAtUtc = completedAt;
                    lastLookupStatus = result.Status;
                }
            }
        }

        private bool IsCurrentAndEnabled(GeoIpRefreshRequest request)
        {
            try
            {
                var settings = store.GetSettings();
                return settings != null &&
                    settings.IsEnabled &&
                    settings.Version == request.SettingsVersion &&
                    string.Equals(settings.Provider, request.Provider, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }
}
