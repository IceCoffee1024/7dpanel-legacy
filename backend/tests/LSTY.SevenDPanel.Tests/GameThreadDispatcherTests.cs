using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Platform")]
    [Trait("Boundary", "SevenDays")]
    public sealed class GameThreadDispatcherTests
    {
        [Fact]
        public async Task Cancellation_before_start_prevents_execution()
        {
            var executed = false;
            var request = new GameThreadDispatchRequest<int>(() =>
            {
                executed = true;
                return 1;
            });
            using var cancellation = new CancellationTokenSource();
            var waiting = request.WaitAsync(TimeSpan.FromMinutes(1), cancellation.Token);

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            request.Execute();
            Assert.False(executed);
        }

        [Fact]
        public async Task Timeout_before_start_prevents_execution()
        {
            var executed = false;
            var request = new GameThreadDispatchRequest<int>(() =>
            {
                executed = true;
                return 1;
            });
            var waiting = request.WaitAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

            Assert.True(request.TryTimeout());

            await Assert.ThrowsAsync<TimeoutException>(() => waiting);
            request.Execute();
            Assert.False(executed);
        }

        [Fact]
        public async Task Cancellation_after_start_waits_for_the_real_result()
        {
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var request = new GameThreadDispatchRequest<int>(() =>
            {
                started.Set();
                release.Wait(TestContext.Current.CancellationToken);
                return 42;
            });
            using var cancellation = new CancellationTokenSource();
            var waiting = request.WaitAsync(TimeSpan.FromMinutes(1), cancellation.Token);
            var execution = Task.Factory.StartNew(
                request.Execute,
                TestContext.Current.CancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Assert.True(started.Wait(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));

            cancellation.Cancel();
            Assert.False(waiting.IsCompleted);

            release.Set();
            await execution;
            Assert.Equal(42, await waiting);
        }

        [Fact]
        public async Task Timeout_signal_after_start_does_not_replace_the_real_result()
        {
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var request = new GameThreadDispatchRequest<string>(() =>
            {
                started.Set();
                release.Wait(TestContext.Current.CancellationToken);
                return "completed";
            });
            var waiting = request.WaitAsync(TimeSpan.FromMinutes(1), CancellationToken.None);
            var execution = Task.Factory.StartNew(
                request.Execute,
                TestContext.Current.CancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Assert.True(started.Wait(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));

            Assert.False(request.TryTimeout());
            Assert.False(waiting.IsCompleted);

            release.Set();
            await execution;
            Assert.Equal("completed", await waiting);
        }
    }
}
