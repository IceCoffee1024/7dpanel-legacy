using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerHistoryRuntimeTests
    {
        [Fact]
        public void History_starts_before_and_stops_after_its_inner_runtime()
        {
            var order = new List<string>();
            var subject = new PlayerHistoryRuntime(
                () => order.Add("history-start"),
                () => order.Add("history-stop"),
                new RecordingRuntime(order));

            subject.Start();
            subject.Stop();

            Assert.Equal(
                new[] { "history-start", "inner-start", "inner-stop", "history-stop" },
                order);
        }

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly ICollection<string> order;

            public RecordingRuntime(ICollection<string> order)
            {
                this.order = order;
            }

            public void Start() => order.Add("inner-start");

            public void MarkGameReady()
            {
            }

            public void Stop() => order.Add("inner-stop");
        }
    }
}
