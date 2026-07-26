using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class GameResourceCatalogRuntimeTests
    {
        [Fact]
        public void Start_only_starts_inner_and_first_ready_forwards_before_one_build()
        {
            var trace = new List<string>();
            var inner = new RecordingRuntime(trace);
            var runtime = new GameResourceCatalogRuntime(
                _ =>
                {
                    trace.Add("catalog:build");
                    return Task.CompletedTask;
                },
                inner,
                TimeSpan.FromSeconds(1));

            runtime.Start();
            Assert.Equal(new[] { "inner:start" }, trace);

            runtime.MarkGameReady();
            runtime.MarkGameReady();

            Assert.Equal(
                new[]
                {
                    "inner:start", "inner:ready", "catalog:build", "inner:ready"
                },
                trace);
        }

        [Fact]
        public void Stop_cancels_and_boundedly_waits_for_build_before_stopping_inner()
        {
            var trace = new List<string>();
            var inner = new RecordingRuntime(trace);
            var runtime = new GameResourceCatalogRuntime(
                token =>
                {
                    var completion = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    token.Register(() =>
                    {
                        trace.Add("catalog:cancelled");
                        completion.TrySetCanceled();
                    });
                    return completion.Task;
                },
                inner,
                TimeSpan.FromSeconds(1));
            runtime.Start();
            runtime.MarkGameReady();

            runtime.Stop();
            runtime.Stop();

            Assert.Equal(
                new[]
                {
                    "inner:start", "inner:ready", "catalog:cancelled", "inner:stop"
                },
                trace);
            Assert.Equal(1, inner.StopCount);
        }

        [Fact]
        public void Stop_attempts_inner_and_aggregates_build_and_inner_failures()
        {
            var trace = new List<string>();
            var inner = new RecordingRuntime(trace)
            {
                StopException = new InvalidOperationException("inner stop")
            };
            var runtime = new GameResourceCatalogRuntime(
                _ => Task.FromException(new InvalidOperationException("build failed")),
                inner,
                TimeSpan.FromSeconds(1));
            runtime.Start();
            runtime.MarkGameReady();

            var exception = Assert.Throws<AggregateException>(runtime.Stop);

            Assert.Equal(2, exception.InnerExceptions.Count);
            Assert.Contains(exception.InnerExceptions, failure => failure.Message == "build failed");
            Assert.Contains(exception.InnerExceptions, failure => failure.Message == "inner stop");
            Assert.Equal("inner:stop", trace.Last());
        }

        [Fact]
        public void Stop_timeout_is_bounded_and_still_stops_inner()
        {
            var trace = new List<string>();
            var inner = new RecordingRuntime(trace);
            var runtime = new GameResourceCatalogRuntime(
                _ => new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously).Task,
                inner,
                TimeSpan.FromMilliseconds(25));
            runtime.Start();
            runtime.MarkGameReady();

            var exception = Assert.Throws<AggregateException>(runtime.Stop);

            Assert.Contains(exception.InnerExceptions, failure => failure is TimeoutException);
            Assert.Equal("inner:stop", trace.Last());
        }

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly IList<string> trace;

            public RecordingRuntime(IList<string> trace)
            {
                this.trace = trace;
            }

            public Exception? StopException { get; set; }
            public int StopCount { get; private set; }

            public void Start() => trace.Add("inner:start");

            public void MarkGameReady() => trace.Add("inner:ready");

            public void Stop()
            {
                StopCount++;
                trace.Add("inner:stop");
                if (StopException != null) throw StopException;
            }
        }
    }
}
