using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "SevenDays")]
    public sealed class OnlinePlayerProjectionRuntimeTests
    {
        [Fact]
        public void Runtime_starts_projection_first_and_stops_inner_first()
        {
            var trace = new List<string>();
            var inner = new RecordingRuntime(trace);
            var runtime = CreateRuntime(trace, inner);

            runtime.Start();
            runtime.MarkGameReady();
            runtime.Stop();

            Assert.Equal(
                new[]
                {
                    "projection:start", "inner:start", "inner:ready",
                    "inner:stop", "projection:stop"
                },
                trace);
        }

        [Fact]
        public void Inner_start_failure_stops_projection_and_preserves_the_original_exception()
        {
            var trace = new List<string>();
            var expected = new InvalidOperationException("start failed");
            var inner = new RecordingRuntime(trace) { StartException = expected };
            var runtime = CreateRuntime(trace, inner);

            var actual = Assert.Throws<InvalidOperationException>(() => runtime.Start());

            Assert.Same(expected, actual);
            Assert.Equal(
                new[] { "projection:start", "inner:start", "projection:stop" },
                trace);
        }

        [Fact]
        public void Inner_stop_failure_still_stops_projection_and_repeat_stop_is_idempotent()
        {
            var trace = new List<string>();
            var inner = new RecordingRuntime(trace)
            {
                StopException = new InvalidOperationException("stop failed")
            };
            var projectionStopCount = 0;
            var runtime = new OnlinePlayerProjectionRuntime(
                () => trace.Add("projection:start"),
                () =>
                {
                    projectionStopCount++;
                    trace.Add("projection:stop");
                },
                inner);
            runtime.Start();

            Assert.Throws<AggregateException>(() => runtime.Stop());
            runtime.Stop();

            Assert.Equal(1, projectionStopCount);
            Assert.Equal(1, inner.StopCount);
        }

        private static OnlinePlayerProjectionRuntime CreateRuntime(
            IList<string> trace,
            IModRuntime inner) =>
            new OnlinePlayerProjectionRuntime(
                () => trace.Add("projection:start"),
                () => trace.Add("projection:stop"),
                inner);

        [Trait("Capability", "Players")]

        [Trait("Boundary", "SevenDays")]

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly IList<string> trace;

            public RecordingRuntime(IList<string> trace)
            {
                this.trace = trace;
            }

            public Exception? StartException { get; set; }

            public Exception? StopException { get; set; }

            public int StopCount { get; private set; }

            public void Start()
            {
                trace.Add("inner:start");
                if (StartException != null) throw StartException;
            }

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