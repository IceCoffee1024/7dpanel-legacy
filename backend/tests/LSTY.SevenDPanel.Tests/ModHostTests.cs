using System;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ModHostTests
    {
        [Fact]
        public void Start_and_stop_are_idempotent()
        {
            var webHost = new FakeWebHost();
            var host = new ModHost(() => webHost);

            host.Start();
            host.Start();
            host.Stop();
            host.Stop();

            Assert.Equal(1, webHost.StartCount);
            Assert.Equal(1, webHost.DisposeCount);
            Assert.Equal(ModHostState.Stopped, host.State);
        }

        [Fact]
        public void Start_failure_moves_host_to_faulted_and_disposes_candidate()
        {
            var webHost = new FakeWebHost { StartException = new InvalidOperationException("failed") };
            var host = new ModHost(() => webHost);

            host.Start();

            Assert.Equal(ModHostState.Faulted, host.State);
            Assert.Equal(1, webHost.DisposeCount);
        }

        [Fact]
        public void Stop_before_start_prevents_later_start()
        {
            var webHost = new FakeWebHost();
            var host = new ModHost(() => webHost);

            host.Stop();
            host.Start();

            Assert.Equal(0, webHost.StartCount);
            Assert.Equal(ModHostState.Stopped, host.State);
        }

        private sealed class FakeWebHost : IPanelWebHost
        {
            public int StartCount { get; private set; }
            public int DisposeCount { get; private set; }
            public Exception StartException { get; set; }

            public void Start()
            {
                StartCount++;
                if (StartException != null) throw StartException;
            }

            public void Dispose() { DisposeCount++; }
        }
    }
}
