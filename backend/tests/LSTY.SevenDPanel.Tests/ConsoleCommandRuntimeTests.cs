using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ConsoleCommandRuntimeTests
    {
        [Fact]
        public void Runtime_starts_audit_then_commands_then_inner_and_stops_in_reverse()
        {
            var order = new List<string>();
            var runtime = new ConsoleCommandRuntime(
                new RecordingRuntime("audit", order),
                new RecordingRuntime("commands", order),
                new RecordingRuntime("inner", order));

            runtime.Start();
            runtime.MarkGameReady();
            runtime.Stop();
            runtime.Stop();

            Assert.Equal(
                new[]
                {
                    "audit:start", "commands:start", "inner:start",
                    "audit:ready", "commands:ready", "inner:ready",
                    "inner:stop", "commands:stop", "audit:stop"
                },
                order);
        }

        [Fact]
        public void Start_failure_rolls_back_only_started_components_in_reverse()
        {
            var order = new List<string>();
            var runtime = new ConsoleCommandRuntime(
                new RecordingRuntime("audit", order),
                new RecordingRuntime("commands", order, failStart: true),
                new RecordingRuntime("inner", order));

            var exception = Assert.Throws<InvalidOperationException>(runtime.Start);

            Assert.Equal("commands start failure", exception.Message);
            Assert.Equal(
                new[] { "audit:start", "commands:start", "audit:stop" },
                order);
        }

        [Fact]
        public void Stop_attempts_every_component_and_aggregates_failures()
        {
            var order = new List<string>();
            var runtime = new ConsoleCommandRuntime(
                new RecordingRuntime("audit", order, failStop: true),
                new RecordingRuntime("commands", order, failStop: true),
                new RecordingRuntime("inner", order, failStop: true));
            runtime.Start();

            var exception = Assert.Throws<AggregateException>(runtime.Stop);

            Assert.Equal(3, exception.InnerExceptions.Count);
            Assert.Equal(
                new[] { "inner:stop", "commands:stop", "audit:stop" },
                order.GetRange(3, 3));
        }

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly string name;
            private readonly List<string> order;
            private readonly bool failStart;
            private readonly bool failStop;

            public RecordingRuntime(
                string name,
                List<string> order,
                bool failStart = false,
                bool failStop = false)
            {
                this.name = name;
                this.order = order;
                this.failStart = failStart;
                this.failStop = failStop;
            }

            public void Start()
            {
                order.Add(name + ":start");
                if (failStart) throw new InvalidOperationException(name + " start failure");
            }

            public void MarkGameReady() => order.Add(name + ":ready");

            public void Stop()
            {
                order.Add(name + ":stop");
                if (failStop) throw new InvalidOperationException(name + " stop failure");
            }
        }
    }
}