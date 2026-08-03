using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Compatibility;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "SevenDays")]
    public sealed class ConsoleCommandHarmonyRuntimeTests
    {
        [Fact]
        public void Stop_stops_inner_before_unpatching_and_is_idempotent()
        {
            var order = new List<string>();
            var runtime = new ConsoleCommandHarmonyRuntime(
                new RecordingRuntime(order),
                () => order.Add("harmony:unpatch"));

            runtime.Start();
            runtime.MarkGameReady();
            runtime.Stop();
            runtime.Stop();

            Assert.Equal(
                new[] { "inner:start", "inner:ready", "inner:stop", "harmony:unpatch" },
                order);
        }

        [Fact]
        public void Stop_unpatches_when_inner_stop_fails_and_aggregates_both_failures()
        {
            var order = new List<string>();
            var runtime = new ConsoleCommandHarmonyRuntime(
                new RecordingRuntime(order, failStop: true),
                () =>
                {
                    order.Add("harmony:unpatch");
                    throw new InvalidOperationException("unpatch failure");
                });
            runtime.Start();

            var exception = Assert.Throws<AggregateException>(runtime.Stop);

            Assert.Equal(2, exception.InnerExceptions.Count);
            Assert.Equal(new[] { "inner:start", "inner:stop", "harmony:unpatch" }, order);
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "SevenDays")]

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly List<string> order;
            private readonly bool failStop;

            public RecordingRuntime(List<string> order, bool failStop = false)
            {
                this.order = order;
                this.failStop = failStop;
            }

            public void Start() => order.Add("inner:start");
            public void MarkGameReady() => order.Add("inner:ready");

            public void Stop()
            {
                order.Add("inner:stop");
                if (failStop) throw new InvalidOperationException("inner stop failure");
            }
        }
    }
}