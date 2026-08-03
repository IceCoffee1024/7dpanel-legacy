using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Platform")]
    [Trait("Boundary", "SevenDays")]
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

        [Fact]
        public void Start_does_not_mark_the_game_ready()
        {
            var webHost = new FakeWebHost();
            var host = new ModHost(() => webHost);

            host.Start();

            Assert.Equal(ModHostState.Running, host.State);
            Assert.Equal(GameReadinessState.Loading, host.GameReadiness);
        }

        [Fact]
        public void Mark_game_ready_is_idempotent_and_does_not_restart_the_web_host()
        {
            var webHost = new FakeWebHost();
            var host = new ModHost(() => webHost);
            host.Start();

            host.MarkGameReady();
            host.MarkGameReady();

            Assert.Equal(GameReadinessState.Ready, host.GameReadiness);
            Assert.Equal(1, webHost.StartCount);
        }

        [Fact]
        public void Game_ready_before_start_is_preserved()
        {
            var webHost = new FakeWebHost();
            var host = new ModHost(() => webHost);

            host.MarkGameReady();
            host.Start();

            Assert.Equal(GameReadinessState.Ready, host.GameReadiness);
            Assert.Equal(ModHostState.Running, host.State);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Stop_moves_game_readiness_to_stopping(bool markReady)
        {
            var host = new ModHost(() => new FakeWebHost());
            host.Start();
            if (markReady) host.MarkGameReady();

            host.Stop();

            Assert.Equal(GameReadinessState.Stopping, host.GameReadiness);
        }

        [Fact]
        public void Stop_wins_over_late_game_ready()
        {
            var host = new ModHost(() => new FakeWebHost());
            host.Start();
            host.Stop();

            host.MarkGameReady();

            Assert.Equal(GameReadinessState.Stopping, host.GameReadiness);
        }

        [Fact]
        public async Task Stop_wins_over_concurrent_game_ready()
        {
            var host = new ModHost(() => new FakeWebHost());
            host.Start();

            await Task.WhenAll(
                Task.Run(host.MarkGameReady, TestContext.Current.CancellationToken),
                Task.Run(host.Stop, TestContext.Current.CancellationToken));

            Assert.Equal(GameReadinessState.Stopping, host.GameReadiness);
        }

        [Fact]
        public async Task Stop_while_start_is_blocked_prevents_late_running_publication()
        {
            var webHost = new FakeWebHost(blockStart: true);
            var host = new ModHost(() => webHost);
            var startTask = Task.Factory.StartNew(
                host.Start,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            try
            {
                Assert.True(webHost.WaitUntilStartEntered(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));
                host.Stop();
                Assert.Equal(ModHostState.Stopped, host.State);
            }
            finally
            {
                webHost.AllowStartToComplete();
                await startTask;
            }

            Assert.Equal(ModHostState.Stopped, host.State);
            Assert.Equal(1, webHost.StartCount);
            Assert.Equal(1, webHost.DisposeCount);
        }

        [Fact]
        public async Task Stop_while_start_is_blocked_prevents_late_faulted_publication()
        {
            var webHost = new FakeWebHost(blockStart: true)
            {
                StartException = new InvalidOperationException("failed")
            };
            var host = new ModHost(() => webHost);
            var startTask = Task.Factory.StartNew(
                host.Start,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            try
            {
                Assert.True(webHost.WaitUntilStartEntered(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));
                host.Stop();
                Assert.Equal(ModHostState.Stopped, host.State);
            }
            finally
            {
                webHost.AllowStartToComplete();
                await startTask;
            }

            Assert.Equal(ModHostState.Stopped, host.State);
            Assert.Equal(1, webHost.StartCount);
            Assert.Equal(1, webHost.DisposeCount);
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "SevenDays")]

        private sealed class FakeWebHost : IPanelWebHost
        {
            private readonly ManualResetEventSlim startEntered = new ManualResetEventSlim();
            private readonly ManualResetEventSlim allowStartToComplete = new ManualResetEventSlim();
            private readonly bool blockStart;
            private int startCount;
            private int disposeCount;

            public FakeWebHost(bool blockStart = false)
            {
                this.blockStart = blockStart;
            }

            public int StartCount => Volatile.Read(ref startCount);
            public int DisposeCount => Volatile.Read(ref disposeCount);
            public Exception? StartException { get; set; }

            public bool WaitUntilStartEntered(TimeSpan timeout, CancellationToken cancellationToken) =>
                startEntered.Wait(timeout, cancellationToken);

            public void AllowStartToComplete() => allowStartToComplete.Set();

            public void Start()
            {
                Interlocked.Increment(ref startCount);
                startEntered.Set();
                if (blockStart) allowStartToComplete.Wait();
                if (StartException != null) throw StartException;
            }

            public void Dispose() { Interlocked.Increment(ref disposeCount); }
        }
    }
}
