using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Automations;
using LSTY.SevenDPanel.Application.Automations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Automation")]
    [Trait("Boundary", "SevenDays")]
    public sealed class AutomationTriggerRuntimeTests
    {
        [Fact]
        public async Task TryWrite_is_non_blocking_reports_full_and_copies_gap_ids()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var observed = new List<AutomationTriggerSnapshot>();
            using var runtime = new AutomationTriggerRuntime(
                async (trigger, _) =>
                {
                    observed.Add(trigger);
                    if (trigger.TriggerId == "first")
                    {
                        entered.TrySetResult(true);
                        await release.Task;
                    }
                },
                queueCapacity: 1,
                drainTimeout: TimeSpan.FromSeconds(2));
            runtime.Start();
            var gaps = new List<string> { "gap-1" };

            Assert.True(runtime.TryWrite(Trigger("first", gaps)));
            await entered.Task;
            gaps[0] = "mutated";
            Assert.True(runtime.TryWrite(Trigger("second")));
            var elapsed = Stopwatch.StartNew();
            Assert.False(runtime.TryWrite(Trigger("full")));
            elapsed.Stop();

            Assert.True(elapsed.Elapsed < TimeSpan.FromMilliseconds(250));
            release.TrySetResult(true);
            runtime.Complete();
            await runtime.DrainAsync(TestContext.Current.CancellationToken);
            Assert.Equal("gap-1", observed[0].GapIds[0]);
            Assert.Equal(new[] { "first", "second" }, observed.ConvertAll(item => item.TriggerId));
        }

        [Fact]
        public async Task Stop_stops_producers_then_completes_ingress_and_drains_accepted_work()
        {
            var order = new List<string>();
            using var runtime = new AutomationTriggerRuntime(
                (trigger, _) =>
                {
                    order.Add("execute:" + trigger.TriggerId);
                    return Task.CompletedTask;
                },
                queueCapacity: 4,
                drainTimeout: TimeSpan.FromSeconds(2));
            runtime.Start();

            await runtime.StopAsync(
                () =>
                {
                    order.Add("stop-producers");
                    Assert.True(runtime.TryWrite(Trigger("last")));
                },
                TestContext.Current.CancellationToken);

            Assert.Equal(new[] { "stop-producers", "execute:last" }, order);
            Assert.False(runtime.TryWrite(Trigger("too-late")));
            Assert.True(runtime.Completion.IsCompleted);
            Assert.False(runtime.Completion.IsFaulted);
        }

        [Fact]
        public async Task Blood_moon_phase_publishes_only_false_or_unknown_to_true_edges_as_immutable_snapshots()
        {
            var observed = new List<AutomationTriggerSnapshot>();
            using var runtime = new AutomationTriggerRuntime(
                (trigger, _) =>
                {
                    observed.Add(trigger);
                    return Task.CompletedTask;
                },
                queueCapacity: 4,
                drainTimeout: TimeSpan.FromSeconds(2));
            runtime.Start();

            runtime.ObserveBloodMoonPhase(null, Utc(0));
            runtime.ObserveBloodMoonPhase(true, Utc(1));
            runtime.ObserveBloodMoonPhase(true, Utc(2));
            runtime.ObserveBloodMoonPhase(false, Utc(3));
            runtime.ObserveBloodMoonPhase(true, Utc(4));
            runtime.Complete();
            await runtime.DrainAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, observed.Count);
            Assert.Equal(
                new[] { "blood-moon-entered:1785110460000", "blood-moon-entered:1785110640000" },
                observed.ConvertAll(trigger => trigger.TriggerId));
            Assert.All(observed, trigger =>
            {
                Assert.Equal("BloodMoonPhaseEntered", trigger.TriggerType);
                Assert.Equal("Active", trigger.BloodMoonPhase);
                Assert.Null(trigger.ActorCrossplatformId);
                Assert.Null(trigger.ActorEntityId);
                Assert.Null(trigger.ActorGroup);
                Assert.Null(trigger.PermissionLevel);
                Assert.Null(trigger.ChatText);
                Assert.Null(trigger.ScheduledForUtc);
                Assert.Empty(trigger.GapIds);
                Assert.Throws<NotSupportedException>(() =>
                    ((IList<string>)trigger.GapIds).Add("mutation"));
            });
        }

        private static AutomationTriggerSnapshot Trigger(
            string id,
            IReadOnlyList<string>? gaps = null) =>
            new(
                id,
                "PlayerJoined",
                new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
                "player-1",
                7,
                "member",
                10,
                null,
                null,
                null,
                gaps ?? Array.Empty<string>());

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 27, 0, minute, 0, TimeSpan.Zero);
    }
}
