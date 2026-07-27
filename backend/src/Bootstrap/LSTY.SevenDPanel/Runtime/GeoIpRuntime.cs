using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Adapters.Local.GeoIp;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.AccessPolicies;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal sealed class GeoIpRuntime : IModRuntime
    {
        private readonly GeoIpRefreshWorker refreshWorker;
        private readonly SevenDaysGeoIpJoinPolicyRuntime joinPolicy;
        private readonly IModRuntime inner;
        private readonly object sync = new object();
        private bool started;

        public GeoIpRuntime(
            GeoIpRefreshWorker refreshWorker,
            SevenDaysGeoIpJoinPolicyRuntime joinPolicy,
            IModRuntime inner)
        {
            this.refreshWorker = refreshWorker ?? throw new ArgumentNullException(nameof(refreshWorker));
            this.joinPolicy = joinPolicy ?? throw new ArgumentNullException(nameof(joinPolicy));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start()
        {
            lock (sync)
            {
                if (started) return;
                var refreshStarted = false;
                var policyStarted = false;
                try
                {
                    refreshWorker.Start();
                    refreshStarted = true;
                    joinPolicy.Start();
                    policyStarted = true;
                    inner.Start();
                    started = true;
                }
                catch
                {
                    var failures = new List<Exception>();
                    if (policyStarted)
                    {
                        try { joinPolicy.Stop(); }
                        catch (Exception exception) { failures.Add(exception); }
                    }
                    if (refreshStarted)
                    {
                        try { refreshWorker.Stop(); }
                        catch (Exception exception) { failures.Add(exception); }
                    }
                    if (failures.Count != 0) throw new AggregateException(failures);
                    throw;
                }
            }
        }

        public void MarkGameReady()
        {
            lock (sync) inner.MarkGameReady();
        }

        public void Stop()
        {
            lock (sync)
            {
                if (!started) return;
                var failures = new List<Exception>();
                try { inner.Stop(); }
                catch (Exception exception) { failures.Add(exception); }
                try { joinPolicy.Stop(); }
                catch (Exception exception) { failures.Add(exception); }
                try { refreshWorker.Stop(); }
                catch (Exception exception) { failures.Add(exception); }
                if (failures.Count == 0) started = false;
                else throw new AggregateException(failures);
            }
        }
    }
}
