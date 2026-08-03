using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "SevenDays")]
    public sealed class PlayerEvidenceRuntimeTests
    {
        [Fact]
        public void Runtime_starts_writer_then_projection_then_inner_and_stops_in_reverse()
        {
            var order = new List<string>();
            var runtime = new PlayerEvidenceRuntime(
                () => order.Add("writer:start"),
                () => order.Add("writer:stop"),
                () => order.Add("projection:start"),
                () => order.Add("projection:stop"),
                new RecordingRuntime(order));

            runtime.Start();
            runtime.Stop();

            Assert.Equal(
                new[]
                {
                    "writer:start",
                    "projection:start",
                    "inner:start",
                    "inner:stop",
                    "projection:stop",
                    "writer:stop"
                },
                order);
        }

        [Fact]
        public async Task Runtime_rolls_back_writer_when_projection_subscription_fails()
        {
            var order = new List<string>();
            var runtime = new PlayerEvidenceRuntime(
                () => order.Add("writer:start"),
                () => order.Add("writer:stop"),
                () => throw new InvalidOperationException("subscribe failed"),
                () => order.Add("projection:stop"),
                new RecordingRuntime(order));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Task.Run(runtime.Start));

            Assert.Equal(
                new[] { "writer:start", "projection:stop", "writer:stop" },
                order);
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "SevenDays")]

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly IList<string> order;

            public RecordingRuntime(IList<string> order)
            {
                this.order = order;
            }

            public void Start() => order.Add("inner:start");

            public void MarkGameReady() => order.Add("inner:ready");

            public void Stop() => order.Add("inner:stop");
        }
    }
}
